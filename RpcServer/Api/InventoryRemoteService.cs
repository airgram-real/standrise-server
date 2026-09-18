using Axlebolt.RpcSupport.Protobuf;
using System.Runtime.InteropServices;
using Amazon.Runtime.Internal.Transform;
using System;
using System.Collections.Generic;
using System.IO;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.MongoDB;
using StandRiseServer.RpcServer.Security;
using Axlebolt.Bolt.Protobuf;
using System.Linq;
using System.Collections.Concurrent;
using MongoDB.Bson;
using static System.Enum;
using Google.Protobuf;
namespace StandRiseServer.RpcServer.Api
{



	public class InventoryRemoteService : RpcClass
	{
		private static readonly object MetadataCacheLock = new object();
		private static readonly object ItemDefinitionsCacheLock = new object();
		private static readonly object PropertyDefinitionsCacheLock = new object();
		private static long CachedItemDefinitionsCatalogueVersion = -1;
		private static long CachedPropertyDefinitionsCatalogueVersion = -1;
		private static BinaryValue CachedItemDefinitionsValue;
		private static BinaryValue CachedItemDefinitionsResponseValue;
		private static BinaryValue CachedPropertyDefinitionsValue;
		private static BinaryValue CachedPropertyDefinitionsResponseValue;
		private static readonly ConcurrentDictionary<string, byte> ActiveRecipeExecutions = new ConcurrentDictionary<string, byte>();
		// Идемпотентность спина: повтор того же RpcRequest.Id не выдаёт второй приз.
		private static readonly ConcurrentDictionary<string, byte[]> SpinIdempotentResponses =
			new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
		private static long _spinIdempotencyLastCleanupTicks;

		private sealed class ActiveRecipeExecution : IDisposable
		{
			private readonly string _playerId;

			public ActiveRecipeExecution(string playerId)
			{
				_playerId = playerId;
			}

			public void Dispose()
			{
				ActiveRecipeExecutions.TryRemove(_playerId, out _);
			}
		}

		private static bool IsSpinRouletteRecipe(string recipeCode)
		{
			if (string.IsNullOrWhiteSpace(recipeCode)) return false;
			string r = recipeCode.Trim().ToUpperInvariant();
			return r.Contains("SPIN") || r.Contains("ROULETTE");
		}

		private static string SpinIdempotencyKey(string playerId, string requestId)
			=> (playerId ?? "") + "|" + (requestId ?? "");

		private bool TryReplaySpinIdempotent(string playerId, string requestId)
		{
			if (string.IsNullOrEmpty(requestId)) return false;
			string key = SpinIdempotencyKey(playerId, requestId);
			if (!SpinIdempotentResponses.TryGetValue(key, out byte[] payload) || payload == null || payload.Length == 0)
				return false;
			Logger.Log($"[Inventory] spin idempotent replay requestId={requestId} player={playerId}");
			_user.SendResponce(new ResponseMessage
			{
				RpcResponse = new RpcResponse
				{
					Id = requestId,
					Return = new BinaryValue { One = Google.Protobuf.ByteString.CopyFrom(payload) }
				}
			});
			return true;
		}

		private void RememberSpinIdempotent(string playerId, string requestId, ExchangeResult result)
		{
			if (string.IsNullOrEmpty(requestId) || result == null) return;
			try
			{
				var bv = new ToByteMethod(typeof(ExchangeResult)).ToBytes(result);
				byte[] bytes = bv?.One?.ToByteArray();
				if (bytes == null || bytes.Length == 0) return;
				SpinIdempotentResponses[SpinIdempotencyKey(playerId, requestId)] = bytes;
				long now = Environment.TickCount64;
				if (now - _spinIdempotencyLastCleanupTicks > 60_000)
				{
					_spinIdempotencyLastCleanupTicks = now;
					if (SpinIdempotentResponses.Count > 500)
						SpinIdempotentResponses.Clear();
				}
			}
			catch { }
		}

		private void SendExchangeResult(string requestId, ExchangeResult result)
		{
			if (result == null) result = new ExchangeResult();
			_user.SendResponce(new ResponseMessage
			{
				RpcResponse = new RpcResponse
				{
					Id = requestId,
					Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(result)
				}
			});
		}

		/// <summary>
		/// Спин 0.17 (как RECIPE_703 + MFHJEEBMDBJ):
		/// inventoryItems[0] qty=0 — списание токена, [1] — приз; field 3 spent — ItemId + def#201/219.
		/// </summary>
		private static ExchangeResult BuildSpinExchangeResult(int spentSlot, int spentDef, InventoryItem prize)
		{
			int slot = spentSlot > 0 ? spentSlot : 1;
			int def = spentDef > 0 ? spentDef : 201;
			int variant = LocalServerConfig.Current.SpinResultVariant;
			var result = new ExchangeResult();
			// Рабочая форма ответа. Рецепты боевого пропуска (CURSED_SOULS_SKINS,
			// CURSED_SOULS_CHARMS, RECIPE_3xx ...) отдают ExchangeResult, где лежит
			// РОВНО один InventoryItem — сам приз. Клиент 0.17 их принимает без ошибок.
			// Спин отличался только тем, что мы добавляли вторую строку с Quantity=0
			// (списанный токен) и поле Spent — на этом разбор ответа падал и всплывало
			// Common/RequestFailed = «ОШИБКА ЗАПРОСА», хотя предмет уже был выдан.
			if (LocalServerConfig.Current.SpinCleanResult)
			{
				// Как CURSED_SOULS_SKINS: ровно один InventoryItem = приз.
				// Spent снова давал REQUEST FAILED на 0.17 SpinnerRelease (проверено 13:08).
				if (prize != null) result.InventoryItems.Add(prize);
				return result;
			}
			// Варианты 1 и 4 — без строки списания, только приз.
			if (variant != 1 && variant != 4)
			{
				result.InventoryItems.Add(new InventoryItem
				{
					Id = slot,
					ItemDefinitionId = def,
					Quantity = variant == 5 ? 1 : 0,
					Flags = 0,
					Date = DateTime.UtcNow.ToUniversalTime().Ticks
				});
			}
			if (prize != null)
				result.InventoryItems.Add(prize);
			// Варианты 2 и 4 — без field 3 (spent).
			if (variant != 2 && variant != 4)
			{
				result.Spent.Add(new ExchangeSpentEntry
				{
					Name = slot.ToString(),
					PropertyType = (int)PropertyType.ItemId,
					IntValue = def
				});
			}
			return result;
		}

		private void SendSpinExchangeResult(string requestId, int reportRemovedSlot, int removedDef, InventoryItem prize)
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();
			bool clean = LocalServerConfig.Current.SpinCleanResult;
			int spinVariant = LocalServerConfig.Current.SpinResultVariant;
			if (prize == null)
			{
				prize = new InventoryItem
				{
					Id = 1,
					ItemDefinitionId = 141600,
					Quantity = 1,
					Flags = 0,
					Date = DateTime.UtcNow.Ticks
				};
			}
			if (!IsCursedSoulsWheelSkin(prize.ItemDefinitionId))
			{
				SpinLogger.Warn($"REWRITE off-wheel prizeDef={prize.ItemDefinitionId} → 141600 requestId={requestId}");
				prize.ItemDefinitionId = 141600;
			}
			if (prize.Quantity <= 0) prize.Quantity = 1;
			// Как ToInventory / BP: Date = ticks. Unix-ms + spent ломали SpinnerRelease.
			prize.Date = DateTime.UtcNow.Ticks;
			prize.Properties?.Remove("stattrack_value");
			string playerId = StaticClasses.Users.TryGetValue(_user.TcpClient, out string pid) ? pid : "?";
			SpinLogger.Info($"OUT player={playerId} requestId={requestId} clean={clean} variant={spinVariant} spentSlot={reportRemovedSlot} spentDef={removedDef} prizeDef={prize.ItemDefinitionId} prizeSlot={prize.Id} prizeDateTicks={prize.Date}");
			var built = BuildSpinExchangeResult(reportRemovedSlot, removedDef, prize);
			// Clean = prize-only (как BP).
			try
			{
				var bv = new ToByteMethod(typeof(ExchangeResult)).ToBytes(built);
				byte[] raw = bv?.One?.ToByteArray() ?? Array.Empty<byte>();
				SpinLogger.Info($"WIRE player={playerId} requestId={requestId} bytes={raw.Length} b64={Convert.ToBase64String(raw)} items={built.InventoryItems.Count} spent={built.Spent.Count} ms={sw.ElapsedMilliseconds}");
			}
			catch (System.Exception ex)
			{
				SpinLogger.Error($"WIRE dump failed requestId={requestId}", ex);
			}
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string pid2))
				RememberSpinIdempotent(pid2, requestId, built);
			SendExchangeResult(requestId, built);
			SpinLogger.Info($"SENT player={playerId} requestId={requestId} ok ms={sw.ElapsedMilliseconds}");
			if (prize == null) return;
			// Приз уже лежит в ExchangeResult, который клиент только что разобрал.
			// Дублирующий onInventoryChanged(added) прилетает поверх анимации колеса —
			// по прежним наблюдениям именно added-push ломал SpinnerRelease.
			// По умолчанию не шлём; включается флагом без пересборки.
			if (LocalServerConfig.Current.SpinPushPrizeAdded
				&& StaticClasses.Users.TryGetValue(_user.TcpClient, out string syncPid) && prize != null)
			{
				try { InventoryRemoteEventListener.PushDelta(syncPid, new[] { prize }, null); }
				catch (System.Exception ex) { SpinLogger.Error($"PushDelta prize failed player={syncPid}", ex); }
			}
			else
			{
				SpinLogger.Info($"prize add-push отключён (SpinPushPrizeAdded=false) player={playerId}");
			}
			// Clean prize-only: после RPC клиент всё ещё держит токен в UI.
			// Без delayed remove 3-й спин → REQUEST FAILED (десинк слотов).
			if (clean)
			{
				if (reportRemovedSlot > 0
					&& StaticClasses.Users.TryGetValue(_user.TcpClient, out string removePid))
				{
					var removed = new InventoryItem
					{
						Id = reportRemovedSlot,
						ItemDefinitionId = removedDef > 0 ? removedDef : 201,
						Quantity = 1,
						Flags = 0,
						Date = DateTime.UtcNow.Ticks
					};
					int delayMs = LocalServerConfig.Current.SpinPushRemoveAfterMs;
					if (delayMs <= 0) delayMs = 1500;
					_ = Task.Run(async () =>
					{
						try
						{
							await Task.Delay(delayMs).ConfigureAwait(false);
							InventoryRemoteEventListener.PushDelta(removePid, null, new[] { removed });
							SpinLogger.Info($"delayed remove-push slot={reportRemovedSlot} def={removed.ItemDefinitionId} player={removePid} delayMs={delayMs}");
						}
						catch (System.Exception ex)
						{
							SpinLogger.Error($"delayed remove-push failed player={removePid}", ex);
						}
					});
				}
				else
				{
					SpinLogger.Info($"clean — no remove slot player={playerId} slot={reportRemovedSlot}");
				}
				return;
			}
			if (spinVariant == 3 || spinVariant == 4) return;
			if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId2)) return;
			if (reportRemovedSlot <= 0) return;
			var removedNow = new InventoryItem
			{
				Id = reportRemovedSlot,
				ItemDefinitionId = removedDef > 0 ? removedDef : 201,
				Quantity = 1,
				Flags = 0,
				Date = DateTime.UtcNow.Ticks
			};
			InventoryRemoteEventListener.PushDelta(playerId2, null, new[] { removedNow });
		}

		private static int[] _cursedSoulsSpinPool;
		private static readonly object _cursedSoulsSpinPoolLock = new object();

		/// <summary>
		/// РЕАЛЬНЫЙ состав колеса Cursed Souls — снят с экрана клиента, по часовой стрелке.
		/// Двенадцать слотов; клиент сопоставляет пришедший приз со своими слотами по
		/// itemDefinitionId. Любой предмет ВНЕ этого списка стрелке некуда поставить —
		/// клиент показывает «ОШИБКА ЗАПРОСА», хотя предмет сервер уже выдал.
		///
		/// Именно так и ломалось: сервер отдавал 141700 / 146300 / 146400
		/// (F/S "Demonic Fog", FabM "Death Herald", M60 "Demonic Fog") — этих предметов
		/// на колесе нет вовсе, они раздаются лентой боевого пропуска.
		/// </summary>
		private static readonly int[] CursedSoulsWheelItems = new[]
		{
			148000, // Tanto "Restless"
			304,    // "Fable" Case
			1296,   // Sticker "Vampirisushi" Swap
			1290,   // Sticker "Demonic Beast" Color
			702,    // "Rainbow" Sticker Pack
			5091,   // Charm "Spirit"
			306,    // "Empire" Case
			5090,   // Charm "Soul"
			305,    // "Scorpion" Case
			141600, // TEC-9 "Restless"
			1288,   // Sticker "Demon Flame"
			303     // "Rival" Case
		};

		/// <summary>
		/// Вес слота в розыгрыше. Игра своих вероятностей для колеса нигде не задаёт,
		/// поэтому взвешиваю по редкости (properties.value): чем реже предмет, тем меньше
		/// вес. Кейсы и паки редкости не имеют — им обычный вес.
		/// Нужны другие шансы — правится здесь либо списком SpinPrizeItems в local.settings.json.
		/// </summary>
		private static int WheelSlotWeight(int defId)
		{
			int rarity = 0;
			try
			{
				var def = InventoryCatalogueLoader.Instance.GetByKey(defId);
				var props = def?.properties;
				if (props != null && props.Contains("value"))
				{
					var v = props["value"];
					if (v.IsInt32 || v.IsInt64 || v.IsDouble) rarity = v.ToInt32();
				}
			}
			catch { }

			switch (rarity)
			{
				case 6: return 1;
				case 5: return 3;
				case 4: return 6;
				case 3: return 10;
				default: return 10; // кейсы и паки
			}
		}

		private static bool IsCursedSoulsWheelSkin(int defId)
		{
			for (int i = 0; i < CursedSoulsWheelItems.Length; i++)
				if (CursedSoulsWheelItems[i] == defId) return true;
			return false;
		}

		private static int[] GetCursedSoulsSpinPool()
		{
			var cached = _cursedSoulsSpinPool;
			if (cached != null && cached.Length > 0) return cached;
			lock (_cursedSoulsSpinPoolLock)
			{
				if (_cursedSoulsSpinPool != null && _cursedSoulsSpinPool.Length > 0)
					return _cursedSoulsSpinPool;

				// Разрешённый набор — ровно двенадцать слотов колеса. Всё, что вне его,
				// клиенту некуда поставить, и он ответит «ОШИБКА ЗАПРОСА».
				var allowed = new System.Collections.Generic.List<int>(CursedSoulsWheelItems);

				// Явный список из local.settings.json сужает набор (лишнее игнорируем,
				// чтобы конфигом нельзя было вернуть предмет не с колеса).
				var configured = LocalServerConfig.Current.SpinPrizeItems;
				if (configured != null && configured.Length > 0)
				{
					var filtered = new System.Collections.Generic.List<int>();
					foreach (int id in configured)
						if (id > 0 && IsCursedSoulsWheelSkin(id)) filtered.Add(id);
					if (filtered.Count > 0) allowed = filtered.Distinct().ToList();
					else Logger.LogWarn("[Inventory] SpinPrizeItems: ни один id не с колеса — беру полный набор колеса");
				}

				var pool = new System.Collections.Generic.List<int>();
				foreach (int defId in allowed)
				{
					int weight = Math.Max(1, WheelSlotWeight(defId));
					for (int w = 0; w < weight; w++) pool.Add(defId);
				}

				if (pool.Count == 0) pool.AddRange(CursedSoulsWheelItems);

				_cursedSoulsSpinPool = pool.ToArray();
				var distinct = new System.Collections.Generic.HashSet<int>(_cursedSoulsSpinPool);
				Logger.Log($"[Inventory] cursed souls wheel pool: {distinct.Count} предметов, {_cursedSoulsSpinPool.Length} взвешенных слотов [{string.Join(",", allowed)}]");
				return _cursedSoulsSpinPool;
			}
		}

		/// <summary>
		/// Редкость слота колеса (properties.value). 0 — если у предмета её нет (кейсы, паки).
		/// </summary>
		private static int WheelSlotRarity(int defId)
		{
			try
			{
				var def = InventoryCatalogueLoader.Instance.GetByKey(defId);
				var props = def?.properties;
				if (props != null && props.Contains("value"))
				{
					var v = props["value"];
					if (v.IsInt32 || v.IsInt64 || v.IsDouble) return v.ToInt32();
				}
			}
			catch { }
			return 0;
		}

		/// <summary>
		/// Самый редкий слот колеса. Считается один раз: состав колеса не меняется.
		/// </summary>
		private static int _cursedSoulsTopPrize = 0;

		private static int GetCursedSoulsTopPrize()
		{
			int cached = _cursedSoulsTopPrize;
			if (cached != 0) return cached;
			int best = 0;
			int bestRarity = -1;
			foreach (int defId in CursedSoulsWheelItems)
			{
				int rarity = WheelSlotRarity(defId);
				if (rarity > bestRarity) { bestRarity = rarity; best = defId; }
			}
			if (best == 0) best = CursedSoulsWheelItems[0];
			_cursedSoulsTopPrize = best;
			Logger.Log($"[Inventory] arcaneBoost: топ-слот колеса #{best} (редкость {bestRarity})");
			return best;
		}

		private static int PickCursedSoulsSpinPrize()
		{
			// Подкрутка удачи из админки бота: всегда самый редкий слот колеса.
			// Предмет всё равно с колеса, поэтому клиенту есть куда поставить указатель.
			try
			{
				if (BoltGameDatabaseProvider.Instance.GetArcaneBoost())
					return GetCursedSoulsTopPrize();
			}
			catch { }

			int[] pool = GetCursedSoulsSpinPool();
			if (pool == null || pool.Length == 0) return 148000;
			return pool[Random.Shared.Next(pool.Length)];
		}

		public InventoryRemoteService(UserService user) : base(user) { }

		public static void Preload()
		{
			try
			{
				BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
				if (boltGame == null)
				{
					Console.WriteLine("[Inventory] Preload skipped: BoltGameDatabaseProvider.Instance is null");
					return;
				}

				InventoryCatalogueLoader.Instance.Start();
				Console.WriteLine($"[Inventory] Preloading catalogue from {InventoryCatalogueLoader.Instance.ResolvedPath}");
				InvalidateMetadataCache();
				EnsureMetadataCache(boltGame);
				Console.WriteLine($"[Inventory] Preload complete. Items={InventoryCatalogueLoader.Instance.Count}, version={InventoryCatalogueLoader.Instance.GetCatalogueVersionTag()}");
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"[Inventory] Preload failed: {ex.Message}");
			}
		}

		public static void InvalidateMetadataCache()
		{
			lock (MetadataCacheLock)
			{
				CachedItemDefinitionsCatalogueVersion = -1;
				CachedPropertyDefinitionsCatalogueVersion = -1;
				CachedItemDefinitionsValue = null;
				CachedItemDefinitionsResponseValue = null;
				CachedPropertyDefinitionsValue = null;
				CachedPropertyDefinitionsResponseValue = null;
			}
			InventoryCatalogueLoader.Instance.ForceReload();
		}

		private static BinaryValue CreateRepeatedResponse<T>(IEnumerable<T> messages, string lastUpdated) where T : IMessage
		{
			var stream = new System.IO.MemoryStream();
			var output = new CodedOutputStream(stream);
			if (messages != null)
			{
				foreach (T message in messages)
				{
					if (message == null)
					{
						continue;
					}
					output.WriteRawTag(10);
					output.WriteBytes(message.ToByteString());
				}
			}
			if (!string.IsNullOrEmpty(lastUpdated))
			{
				output.WriteRawTag(18);
				output.WriteString(lastUpdated);
			}
			output.Flush();
			return new BinaryValue { IsNull = false, One = ByteString.CopyFrom(stream.ToArray()) };
		}

		private static BinaryValue CreateSingleMessageResponse(IMessage message)
		{
			var stream = new System.IO.MemoryStream();
			var output = new CodedOutputStream(stream);
			if (message != null)
			{
				output.WriteRawTag(10);
				output.WriteBytes(message.ToByteString());
			}
			output.Flush();
			return new BinaryValue { IsNull = false, One = ByteString.CopyFrom(stream.ToArray()) };
		}

		private static BoltInventoryItem CreateInventoryItemFromDefinition(BoltInventoryItemDefinitionDocument definition)
		{
			var item = new BoltInventoryItem
			{
				itemDefinitionId = definition.key,
				date = BsonDateTime.Create(DateTime.UtcNow),
				flags = 0,
				quantity = 1
			};

			if (definition.IsStattrack())
			{
				item.Properties.Add(new BoltInventoryItemProperty
				{
					Name = "stattrack_value",
					Type = BoltInventoryItemProperty.PropertyType.Int,
					Value = "0"
				});
			}

			return item;
		}

		private static BinaryValue CreateBuyInventoryItemResponse(IEnumerable<PlayerInventoryItem> items)
		{
			var stream = new MemoryStream();
			var output = new CodedOutputStream(stream);
			foreach (var item in items ?? Enumerable.Empty<PlayerInventoryItem>())
			{
				if (item == null)
				{
					continue;
				}

				output.WriteRawTag(10);
				output.WriteBytes(item.ToByteString());
			}
			output.Flush();
			return new BinaryValue { IsNull = false, One = ByteString.CopyFrom(stream.ToArray()) };
		}

		private static bool TryExtractWrappedMessage(byte[] raw, out byte[] message)
		{
			message = Array.Empty<byte>();
			try
			{
				using (var input = new CodedInputStream(raw))
				{
					while (true)
					{
						var tag = input.ReadTag();
						if (tag == 0)
						{
							break;
						}

						if (tag == 10)
						{
							var bytes = input.ReadBytes().ToByteArray();
							if (bytes.Length > 0)
							{
								message = bytes;
								return true;
							}

							continue;
						}

						input.SkipLastField();
					}
				}
			}
			catch
			{
			}

			return false;
		}

		private static bool TryParseBuyRequestBytes(byte[] bytes, out int itemId, out int quantity, out int currencyId, out bool toManyItems)
		{
			itemId = 0;
			quantity = 0;
			currencyId = 0;
			toManyItems = false;
			try
			{
				var input = new CodedInputStream(bytes);
				uint tag;
				while ((tag = input.ReadTag()) != 0)
				{
					switch (tag)
					{
						case 8:
							itemId = input.ReadInt32();
							break;
						case 16:
							quantity = input.ReadInt32();
							break;
						case 24:
							currencyId = input.ReadInt32();
							break;
						case 32:
							toManyItems = input.ReadBool();
							break;
						default:
							input.SkipLastField();
							break;
					}
				}
				return itemId > 0;
			}
			catch
			{
				return false;
			}
		}

		private bool TryReadBuyInventoryItemRequest(RpcRequest request, out int itemId, out int quantity, out int currencyId, out bool toManyItems, out bool wrapperResponse)
		{
			itemId = 0;
			quantity = 0;
			currencyId = 0;
			toManyItems = false;
			wrapperResponse = false;

			if (request == null || request.Params == null)
			{
				return false;
			}

			if (request.Params.Count >= 3)
			{
				itemId = (int)new FromByteMethod(typeof(int)).FromBytes(request.Params[0]);
				quantity = (int)new FromByteMethod(typeof(int)).FromBytes(request.Params[1]);
				currencyId = (int)new FromByteMethod(typeof(int)).FromBytes(request.Params[2]);
				if (request.Params.Count > 3)
				{
					toManyItems = (bool)new FromByteMethod(typeof(bool)).FromBytes(request.Params[3]);
				}
				return true;
			}

			if (request.Params.Count == 1 && request.Params[0] != null && !request.Params[0].IsNull)
			{
				byte[] rawBytes = request.Params[0].One.ToByteArray();
				bool isEncrypted = request.MethodName.Equals("buyInventoryItemEncrypted", StringComparison.OrdinalIgnoreCase);
				if (isEncrypted)
				{
					byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
					byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
					byte[] decrypted = null;
					try
					{
						decrypted = Utils.DecryptByte(rawBytes, key, IV);
					}
					catch (System.Exception ex)
					{
						Logger.Error($"[Inventory] BuyInventoryItem decrypt failed with session key: {ex.Message}");
					}

					if (decrypted == null)
					{
						try
						{
							decrypted = Utils.DecryptByte(rawBytes, System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl"), System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm"));
						}
						catch (System.Exception ex)
						{
							Logger.Error($"[Inventory] BuyInventoryItem decrypt failed with fallback key: {ex.Message}");
						}
					}

					if (decrypted != null)
					{
						if (TryParseBuyRequestBytes(decrypted, out itemId, out quantity, out currencyId, out toManyItems))
						{
							wrapperResponse = true;
							return true;
						}
						if (TryExtractWrappedMessage(decrypted, out var wrapped) &&
							TryParseBuyRequestBytes(wrapped, out itemId, out quantity, out currencyId, out toManyItems))
						{
							wrapperResponse = true;
							return true;
						}
					}
				}
				else
				{
					if (TryParseBuyRequestBytes(rawBytes, out itemId, out quantity, out currencyId, out toManyItems))
					{
						wrapperResponse = true;
						return true;
					}
					if (TryExtractWrappedMessage(rawBytes, out var wrapped) &&
						TryParseBuyRequestBytes(wrapped, out itemId, out quantity, out currencyId, out toManyItems))
					{
						wrapperResponse = true;
						return true;
					}
				}
			}

			return false;
		}

		public static void WarmDefinitionCaches()
		{
			try
			{
				EnsureItemDefinitionsCache();
				EnsurePropertyDefinitionsCache(BoltGameDatabaseProvider.Instance);
				var cat = InventoryCatalogueLoader.Instance;
				void LogBuy(int id)
				{
					var d = cat.GetByKey(id, 0);
					int n = d?.buyPrice?.Count ?? -1;
					Console.WriteLine($"[Inventory] WarmDef #{id} buyPriceCount={n}");
				}
				LogBuy(201); LogBuy(202); LogBuy(203); LogBuy(204); LogBuy(608);
				Console.WriteLine($"[Inventory] WarmDefinitionCaches OK defs={CachedItemDefinitionsValue != null}");
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"[Inventory] WarmDefinitionCaches failed: {ex.Message}");
			}
		}

		private static void EnsureMetadataCache(BoltGameDatabaseProvider boltGame)
		{
			EnsureItemDefinitionsCache();
			EnsurePropertyDefinitionsCache(boltGame);
		}

		private static void EnsureItemDefinitionsCache()
		{
			InventoryCatalogueLoader catalogue = InventoryCatalogueLoader.Instance;
			catalogue.EnsureLoaded();
			long catalogueVersion = catalogue.CatalogueVersion;

			lock (ItemDefinitionsCacheLock)
			{
				if (CachedItemDefinitionsCatalogueVersion == catalogueVersion && CachedItemDefinitionsValue != null)
				{
					return;
				}

				List<BoltInventoryItemDefinitionDocument> definitions = catalogue.GetAll().ToList();
				if (definitions.Count == 0)
				{
					Logger.LogWarn($"[Inventory] Catalogue empty ({catalogue.ResolvedPath}); TCP will serve 0 item definitions.");
				}

				string versionTag = catalogue.GetCatalogueVersionTag();
				InventoryItemDefinition[] uniqueDefinitions = definitions
					.Where(d => d != null && d.key > 0)
					.Select(d => d.GetInventoryItemDefinition())
					.Where(d => d != null && d.Id > 0)
					.GroupBy(d => d.Id)
					.Select(group => group.First())
					.OrderBy(d => d.Id)
					.ToArray();
				CachedItemDefinitionsValue = new ToByteMethod(typeof(InventoryItemDefinition[])).ToBytes(uniqueDefinitions);
				CachedItemDefinitionsResponseValue = CreateRepeatedResponse(uniqueDefinitions, versionTag);
				CachedItemDefinitionsResponseValue.Array.Add(CachedItemDefinitionsValue.Array);
				Logger.Debug($"[Inventory] TCP catalogue bytes built: count={uniqueDefinitions.Length}, version={versionTag}, path={catalogue.ResolvedPath}");
				CachedItemDefinitionsCatalogueVersion = catalogueVersion;
			}
		}

		private static void EnsurePropertyDefinitionsCache(BoltGameDatabaseProvider boltGame)
		{
			InventoryCatalogueLoader catalogue = InventoryCatalogueLoader.Instance;
			catalogue.EnsureLoaded();
			long catalogueVersion = catalogue.CatalogueVersion;

			lock (PropertyDefinitionsCacheLock)
			{
				if (CachedPropertyDefinitionsCatalogueVersion == catalogueVersion && CachedPropertyDefinitionsValue != null)
				{
					return;
				}

				string versionTag = catalogue.GetCatalogueVersionTag();
				
				var propertyDefinitionsList = new List<InventoryItemPropertyDefinitions>();
				foreach (var doc in catalogue.GetAll())
				{
					if (doc == null || doc.key <= 0 || doc.properties == null)
					{
						continue;
					}

					var itemProps = new InventoryItemPropertyDefinitions
					{
						ItemDefinitionId = doc.key
					};

					foreach (var prop in doc.properties.Elements)
					{
						if (string.IsNullOrWhiteSpace(prop.Name))
						{
							continue;
						}

						itemProps.Definitions[prop.Name] = new InventoryItemPropertyDefinition
						{
							Name = prop.Name,
							PropertyType = ToClientCompatiblePropertyType(MapPropertyType(prop.Value)),
							SaveInTrade = true,
							SetByType = PropertySetByType.Client
						};
					}

					// В каталоге (Data/StandRise.inventory_item_definition.json) у стрик-предметов
					// есть только флаг "stattrack": true, но НЕТ ключа "stattrack_value" в properties -
					// поэтому определение для него никогда не генерировалось выше, и клиент
					// (BoltInventoryService.SetProperty) кидал "Property definition with name
					// stattrack_value not found!" ДО отправки RPC на сервер - отсюда полное отсутствие
					// трафика и счётчик, который всегда 0 у любого стрик-оружия. Добавляем синтетическое
					// определение вручную для всех предметов с stattrack=true.
					if (doc.properties.TryGetValue("stattrack", out var stattrackFlag) && stattrackFlag.IsBoolean && stattrackFlag.AsBoolean
						&& !itemProps.Definitions.ContainsKey("stattrack_value"))
					{
						itemProps.Definitions["stattrack_value"] = new InventoryItemPropertyDefinition
						{
							Name = "stattrack_value",
							PropertyType = PropertyType.Int,
							SaveInTrade = true,
							SetByType = PropertySetByType.Client
						};
					}

					propertyDefinitionsList.Add(itemProps);
				}

				InventoryItemPropertyDefinitions[] mappedPropertyDefinitions = propertyDefinitionsList
					.Where(d => d != null && d.ItemDefinitionId > 0)
					.GroupBy(d => d.ItemDefinitionId)
					.Select(group => group.First())
					.OrderBy(d => d.ItemDefinitionId)
					.ToArray();

				CachedPropertyDefinitionsValue = new ToByteMethod(typeof(InventoryItemPropertyDefinitions[])).ToBytes(mappedPropertyDefinitions);
				CachedPropertyDefinitionsResponseValue = CreateRepeatedResponse(mappedPropertyDefinitions, versionTag);
				CachedPropertyDefinitionsResponseValue.Array.Add(CachedPropertyDefinitionsValue.Array);
				Logger.Debug($"[Inventory] Cached property definitions count={mappedPropertyDefinitions.Length}");
				CachedPropertyDefinitionsCatalogueVersion = catalogueVersion;
			}
		}

		private static PropertyType MapPropertyType(BsonValue value)
		{
			if (value.IsInt32 || value.IsInt64)
			{
				return PropertyType.Int;
			}
			if (value.IsDouble)
			{
				return PropertyType.Float;
			}
			if (value.IsBoolean)
			{
				return PropertyType.Boolean;
			}
			if (value.IsString)
			{
				return MapPropertyTypeFromString(value.AsString);
			}
			return PropertyType.String;
		}

		private static PropertyType MapPropertyTypeFromString(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return PropertyType.String;
			}

			string trimmed = value.Trim().ToLowerInvariant();
			switch (trimmed)
			{
				case "int":
				case "integer":
					return PropertyType.Int;
				case "float":
				case "single":
				case "double":
					return PropertyType.Float;
				case "bool":
				case "boolean":
					return PropertyType.Boolean;
				case "long":
					return PropertyType.Int;
				case "item_id":
				case "itemid":
					return PropertyType.Int;
				default:
					return PropertyType.String;
			}
		}

		private static PropertyType NormalizePropertyType(PropertyType propertyType)
		{
			if (propertyType == PropertyType.Long || propertyType == PropertyType.ItemId)
			{
				return PropertyType.Int;
			}
			return propertyType;
		}

		private static PropertyType ToClientCompatiblePropertyType(PropertyType propertyType)
		{
			var normalized = NormalizePropertyType(propertyType);
			switch (normalized)
			{
				case PropertyType.Int:
					return PropertyType.Int;
				case PropertyType.Float:
					return PropertyType.Float;
				case PropertyType.String:
					return PropertyType.String;
				case PropertyType.Boolean:
					return PropertyType.Boolean;
				default:
					return PropertyType.String;
			}
		}

		private static int NormalizePlayerInventoryPropertyTypes(PlayerInventory payload)
		{
			var fixedCount = 0;
			if (payload == null || payload.InventoryItems == null)
			{
				return 0;
			}

			foreach (var item in payload.InventoryItems)
			{
				if (item == null)
				{
					continue;
				}
				fixedCount += NormalizeItemPropertyTypes(item);
			}

			return fixedCount;
		}

		private static int NormalizeItemPropertyTypes(InventoryItem item)
		{
			var fixedCount = 0;
			if (item == null || item.Properties == null)
			{
				return 0;
			}

			var properties = new List<KeyValuePair<string, InventoryItemProperty>>(item.Properties.Count);
			foreach (var propertyEntry in item.Properties)
			{
				properties.Add(propertyEntry);
			}

			foreach (var propertyEntry in properties)
			{
				var property = propertyEntry.Value;
				if (property == null)
				{
					continue;
				}
				var normalized = ToClientCompatiblePropertyType(property.Type);
				if (normalized == property.Type)
				{
					continue;
				}

				property.Type = normalized;
				fixedCount++;
			}
			return fixedCount;
		}

		protected void GetInventoryItemDefinitions(BinaryValue[] values, string guid)
		{
			try
			{
				if (StaticClasses.Users.TryGetValue(_user.TcpClient, out _))
				{
					BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
					if (boltGame == null)
					{
						Console.WriteLine("[Inventory] Error: BoltGameDatabaseProvider.Instance is null");
						SendError(guid, 500);
						return;
					}

					EnsureItemDefinitionsCache();
					_user.SendResponce(new ResponseMessage
					{
						RpcResponse = new RpcResponse
						{
							Id = guid,
							Return = CachedItemDefinitionsValue?.Clone() ?? new ToByteMethod(typeof(InventoryItemDefinition[])).ToBytes(Array.Empty<InventoryItemDefinition>())
						}
					});
					return;
				}

				_user.SendResponce(new ResponseMessage
				{
					RpcResponse = new RpcResponse
					{
						Id = guid,
						Exception = new Axlebolt.RpcSupport.Protobuf.Exception
						{
							Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
							Code = 401
						}
					}
				});
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"[Inventory] Error in GetInventoryItemDefinitions: {ex.Message}\\n{ex.StackTrace}");
				SendError(guid, 500);
			}
		}

		protected void GetInventoryItemDefinitionsResponse(BinaryValue[] values, string guid, bool isEncrypted = false)
		{
			try
			{
				if (StaticClasses.Users.TryGetValue(_user.TcpClient, out _))
				{
					BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
					if (boltGame == null)
					{
						SendError(guid, 500);
						return;
					}

					EnsureItemDefinitionsCache();
					BinaryValue result = CachedItemDefinitionsResponseValue?.Clone() ?? CreateRepeatedResponse(Array.Empty<InventoryItemDefinition>(), "0.17.0");
					if (isEncrypted)
					{
						byte[] plain = result.One?.ToByteArray() ?? Array.Empty<byte>();
						byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
						byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
						byte[] encrypted = Utils.EncryptByte(plain, key, IV);
						result = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(encrypted) };
					}
					_user.SendResponce(new ResponseMessage
					{
						RpcResponse = new RpcResponse
						{
							Id = guid,
							Return = result
						}
					});
					return;
				}

				_user.SendResponce(new ResponseMessage
				{
					RpcResponse = new RpcResponse
					{
						Id = guid,
						Exception = new Axlebolt.RpcSupport.Protobuf.Exception
						{
							Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
							Code = 401
						}
					}
				});
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"[Inventory] Error in GetInventoryItemDefinitionsResponse: {ex.Message}\n{ex.StackTrace}");
				SendError(guid, 500);
			}
		}

		protected void GetInventoryItemPropertyDefinitions(BinaryValue[] values, string guid)
		{
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out _))
			{
				BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
				if (boltGame == null)
				{
					SendError(guid, 500);
					return;
				}

				EnsurePropertyDefinitionsCache(boltGame);
				_user.SendResponce(new ResponseMessage
				{
					RpcResponse = new RpcResponse
					{
						Id = guid,
						Return = CachedPropertyDefinitionsValue?.Clone() ?? new ToByteMethod(typeof(InventoryItemPropertyDefinitions[])).ToBytes(Array.Empty<InventoryItemPropertyDefinitions>())
					}
				});
				return;
			}

			_user.SendResponce(new ResponseMessage
			{
				RpcResponse = new RpcResponse
				{
					Id = guid,
					Exception = new Axlebolt.RpcSupport.Protobuf.Exception
					{
						Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
						Code = 401
					}
				}
			});
		}

		protected void GetPlayerInventory(BinaryValue[] values, string guid)
		{
			SendPlayerInventory(values, guid, false, false);
		}

		protected void GetPlayerInventoryResponse(BinaryValue[] values, string guid, bool isEncrypted = false)
		{
			SendPlayerInventory(values, guid, true, isEncrypted);
		}

		private void SendPlayerInventory(BinaryValue[] values, string guid, bool wrapperResponse, bool isEncrypted = false)
		{
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
				PlayerInventoryDocument playerInventoryDocument = null;
				try
				{
					playerInventoryDocument = boltGame.GetPlayerInventoryDocument(ObjectId.Parse(Id));
				}
				catch (System.Exception)
				{
					boltGame.CreatePlayerInventory(ObjectId.Parse(Id));
					playerInventoryDocument = boltGame.GetPlayerInventoryDocument(ObjectId.Parse(Id));
				}

				// Приватный сервер: если спинов 0 — выдать ровно 5×#201 один раз при загрузке инвентаря.
				// НЕ догоняем до 20 (иначе счётчик «не крутится»).
				try
				{
					int spinHave = 0;
					if (playerInventoryDocument?.InventoryItems != null)
					{
						foreach (var el in playerInventoryDocument.InventoryItems)
						{
							try
							{
								var d = el.Value.AsBsonDocument;
								int def = d.Contains("itemDefinitionId") ? d["itemDefinitionId"].ToInt32() : 0;
								if (def == 201 || def == 219) spinHave++;
							}
							catch { }
						}
					}
					if (spinHave == 0)
					{
						const int need = 5;
						int nextId = (playerInventoryDocument.InventoryItems?.ElementCount ?? 0) > 0
							? playerInventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int p) ? p : 0).Max() + 1
							: 1;
						for (int i = 0; i < need; i++)
						{
							boltGame.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), new BoltInventoryItem
							{
								itemDefinitionId = 201,
								quantity = 1,
								flags = 0,
								date = BsonDateTime.Create(DateTime.UtcNow)
							}, nextId + i);
						}
						playerInventoryDocument = boltGame.GetPlayerInventoryDocument(ObjectId.Parse(Id));
						Logger.Log($"[Inventory] auto-granted {need}x spin#201 to {Id} (was 0)");
					}
				}
				catch (System.Exception ex)
				{
					Logger.LogWarn($"[Inventory] spin auto-grant failed: {ex.Message}");
				}

				PlayerInventory playerInventory = playerInventoryDocument.GetPlayerInventoryProto();
				_ = NormalizePlayerInventoryPropertyTypes(playerInventory);
				BinaryValue result = wrapperResponse ? CreateSingleMessageResponse(playerInventory) : new ToByteMethod(typeof(PlayerInventory)).ToBytes(playerInventory);
				if (isEncrypted)
				{
					byte[] plain = result.One?.ToByteArray() ?? Array.Empty<byte>();
					byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
					byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
					byte[] encrypted = Utils.EncryptByte(plain, key, IV);
					result = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(encrypted) };
				}
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = result } });
				return;
			}
			_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
		}
		protected void GetInventoryItemPropertyDefinitionsResponse(BinaryValue[] values, string guid, bool isEncrypted = false)
		{
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out _))
			{
				BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
				if (boltGame == null)
				{
					SendError(guid, 500);
					return;
				}

				EnsurePropertyDefinitionsCache(boltGame);
				BinaryValue result = CachedPropertyDefinitionsResponseValue?.Clone() ?? CreateRepeatedResponse(Array.Empty<InventoryItemPropertyDefinitions>(), "0.17.0");
				if (isEncrypted)
				{
					byte[] plain = result.One?.ToByteArray() ?? Array.Empty<byte>();
					byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
					byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
					byte[] encrypted = Utils.EncryptByte(plain, key, IV);
					result = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(encrypted) };
				}
				_user.SendResponce(new ResponseMessage
				{
					RpcResponse = new RpcResponse
					{
						Id = guid,
						Return = result
					}
				});
				return;
			}

			_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
		}

		                        protected void GetOtherPlayerItemsEncrypted(BinaryValue[] values, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                try
                {
                    if (values == null || values.Length == 0 || values[0] == null || values[0].IsNull)
                    {
                        SendError(guid, 400);
                        return;
                    }

                    byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
                        byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
                        byte[] decryptedBytes = Utils.DecryptByte(values[0].One.ToByteArray(), key, IV);
                        
                        Logger.Log($"[GetOtherPlayerItemsEncrypted] Decrypted Hex: {BitConverter.ToString(decryptedBytes)}");
                        
                        var requestMsg = new Axlebolt.Bolt.Protobuf.GetOtherPlayerItemsRequest();
                        try {
                            requestMsg.MergeFrom(decryptedBytes);
                        } catch (System.Exception pEx) {
                            Logger.Log($"[GetOtherPlayerItemsEncrypted] Direct merge failed: {pEx.Message}. Trying ExtractBinaryValueOne...");
                            byte[] extractedBytes = Utils.ExtractBinaryValueOne(decryptedBytes);
                            Logger.Log($"[GetOtherPlayerItemsEncrypted] Extracted Hex: {BitConverter.ToString(extractedBytes)}");
                            requestMsg.MergeFrom(extractedBytes);
                        }
                    string targetPlayerId = requestMsg.PlayerId;
                    var categories = requestMsg.Categories.ToList();

                    var inventoryDocument = BoltGameDatabaseProvider.Instance.GetPlayerInventoryDocument(global::MongoDB.Bson.ObjectId.Parse(targetPlayerId));
                    if (inventoryDocument == null)
                    {
                        SendError(guid, 404);
                        return;
                    }

                    System.Collections.Generic.List<Axlebolt.Bolt.Protobuf.InventoryItem> items = new System.Collections.Generic.List<Axlebolt.Bolt.Protobuf.InventoryItem>();
                    foreach (var kvp in inventoryDocument.InventoryItems.Elements)
                    {
                        try {
                            var inventoryItemProto = kvp.ToInventory();
                            if (inventoryItemProto != null && (categories == null || categories.Count == 0 || categories.Contains(inventoryItemProto.ItemDefinitionId)))
                            {
                                items.Add(inventoryItemProto);
                            }
                        } catch { }
                    }

                    var responseMsg = new Axlebolt.Bolt.Protobuf.GetOtherPlayerItemsResponse();
                    responseMsg.Items.AddRange(items);

                    byte[] plain = Google.Protobuf.MessageExtensions.ToByteArray(responseMsg);
                    byte[] keyOut = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
                    byte[] IVOut = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
                    byte[] encrypted = Utils.EncryptByte(plain, keyOut, IVOut);

                    _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(encrypted) } } });
                }
                catch (System.Exception ex)
                {
                    System.Console.WriteLine($"[GetOtherPlayerItemsEncrypted] Error: {ex}");
                    SendError(guid, 500);
                }
                return;
            }
            SendError(guid, 401);
        }

		protected void GetPlayerInventory(string playerId, int[] itemDefinitionIds, string guid)
		{
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
				PlayerInventoryDocument playerInventoryDocument = boltGame.GetPlayerInventoryDocument(ObjectId.Parse(playerId));
				InventoryItem[] items = playerInventoryDocument.GetInventoryItemsProto(itemDefinitionIds);
				if (items != null)
				{
					foreach (var item in items)
					{
						if (item != null)
						{
							_ = NormalizeItemPropertyTypes(item);
						}
					}
				}
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new ToByteMethod(typeof(InventoryItem[])).ToBytes(items) } });
				return;
			}
			_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
		}
		protected void ExchangeInventoryItems(RpcRequest request)
		{
			if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				_user.ForceDisconnect();
				return;
			}

			// Ждём освобождения предыдущего executeRecipe.
			// Спины: дольше ждём и НИКОГДА не шлём 429 — клиент рисует «ошибка запроса».
			string recipePeek = "";
			try
			{
				if (request.Params != null && request.Params.Count > 0 && !request.Params[0].IsNull)
					recipePeek = (string)new FromByteMethod(typeof(string)).FromBytes(request.Params[0]) ?? "";
			}
			catch { }
			bool spinLock = IsSpinRouletteRecipe(recipePeek);
			int maxWait = spinLock ? 600 : 300; // 30s / 15s
			bool gotLock = false;
			for (int wait = 0; wait < maxWait; wait++)
			{
				if (ActiveRecipeExecutions.TryAdd(Id, 0))
				{
					gotLock = true;
					break;
				}
				System.Threading.Thread.Sleep(50);
			}
			if (!gotLock)
			{
				Logger.LogWarn($"[SECURITY] Concurrent recipe execution blocked for player {Id} recipe={recipePeek}");
				if (spinLock)
				{
					// Ещё одна попытка; если нет — принудительно занимаем слот (спин важнее 429).
					System.Threading.Thread.Sleep(1000);
					ActiveRecipeExecutions.TryRemove(Id, out _);
					if (!ActiveRecipeExecutions.TryAdd(Id, 0))
					{
						Logger.LogWarn($"[Inventory] spin lock force for {Id}");
						ActiveRecipeExecutions[Id] = 0;
					}
				}
				else
				{
					System.Threading.Thread.Sleep(500);
					if (!ActiveRecipeExecutions.TryAdd(Id, 0))
					{
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 429 } } });
						return;
					}
				}
			}

			using var activeRecipeExecution = new ActiveRecipeExecution(Id);

			{
				string recipeCode = "(unknown)";
				try
				{
				BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
				recipeCode = (string)new FromByteMethod(typeof(string)).FromBytes(request.Params[0]);
				if (IsSpinRouletteRecipe(recipeCode) && TryReplaySpinIdempotent(Id, request.Id))
					return;
				if (InventoryDupeProtection.IsBlockedExchangeRecipe(recipeCode))
				{
					Logger.LogWarn($"[SECURITY] Blocked exchange recipe {recipeCode} for player {Id}");
					if (IsSpinRouletteRecipe(recipeCode))
					{
						// Не 403 — клиент спина трактует как «ошибка запроса».
						try
						{
							var oidB = ObjectId.Parse(Id);
							int nid = boltGameDatabaseProvider.AllocateNextInventoryItemId(oidB);
							var item = new BoltInventoryItem { itemDefinitionId = 141600, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.UtcNow) };
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(oidB, item, nid);
							var p = item.ToInventory(nid);
							p.Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
							// slot=0: не пушим remove чужого слота → иначе десинк и «ошибка запроса».
							SendSpinExchangeResult(request.Id, 0, 201, p);
						}
						catch
						{
							SendSpinExchangeResult(request.Id, 0, 201, new InventoryItem
							{
								Id = 1,
								ItemDefinitionId = 141600,
								Quantity = 1,
								Flags = 0,
								Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
							});
						}
						return;
					}
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 403 } } });
					return;
				}
				if (IsPostMatchDropRecipe(recipeCode))
					Console.WriteLine($"[Inventory] exchangeInventoryItems post-match drop: {recipeCode}");
				if (IsSilverForGoldExchangeRecipe(recipeCode))
				{
					ProcessSilverForGoldExchange(Id, recipeCode, boltGameDatabaseProvider, request.Id, 1);
					return;
				}
				// Params[1] - items (can be absent or empty)
				int[] items = new int[0];
				if (request.Params.Count > 1 && !request.Params[1].IsNull)
				{
					items = (int[])new FromByteMethod(typeof(int[])).FromBytes(request.Params[1]);
				}
				
				int[] craftItems = items;
				if (request.Params.Count > 2 && !request.Params[2].IsNull)
				{
					craftItems = (int[])new FromByteMethod(typeof(int[])).FromBytes(request.Params[2]);
				}
				
				if ((items == null || items.Length == 0) && craftItems != null && craftItems.Length > 0)
					items = craftItems;
				Logger.Log($"[Inventory] exchangeInventoryItems: player={Id} recipe='{recipeCode}' params={request.Params.Count} items={(items == null ? -1 : items.Length)} craftItems={(craftItems == null ? -1 : craftItems.Length)} itemIds=[{(items == null || items.Length == 0 ? "" : string.Join(",", items))}]");
				RandomGenerator randomGenerator = new RandomGenerator();
				
				if (IsSpinRouletteRecipe(recipeCode))
				{
					// Диагностика формата запроса прокрута: нужно точно знать, передаёт ли
					// клиент ожидаемый сектор колеса, чтобы приз всегда совпадал с рулеткой.
					for (int pi = 0; pi < request.Params.Count; pi++)
					{
						try
						{
							var bvSpin = request.Params[pi];
							byte[] rawSpin = bvSpin?.One != null ? bvSpin.One.ToByteArray() : Array.Empty<byte>();
							Logger.Log($"[Inventory] {recipeCode} param[{pi}] raw({rawSpin.Length}): {BitConverter.ToString(rawSpin)}");
						}
						catch (System.Exception exSpinLog)
						{
							Logger.Log($"[Inventory] {recipeCode} param[{pi}] log failed: {exSpinLog.Message}");
						}
					}
				}

				if (recipeCode != null && recipeCode.StartsWith("RECIPE_"))
				{
					// Диагностика формата: клиент может класть список выбранных слотов
					// (10 скинов) в params[1]/params[2] в protobuf-виде, который int[]-
					// декодер читает как 1 элемент. Логируем сырые байты каждого
					// параметра, чтобы точно восстановить формат и отдать корректный
					// ExchangeResult вместо 404.
					for (int pi = 0; pi < request.Params.Count; pi++)
					{
						try
						{
							var bv = request.Params[pi];
							byte[] raw = bv?.One != null ? bv.One.ToByteArray() : Array.Empty<byte>();
							Logger.Log($"[Inventory] {recipeCode} param[{pi}] raw({raw.Length}): {BitConverter.ToString(raw)}");
						}
						catch (System.Exception exLog)
						{
							Logger.Log($"[Inventory] {recipeCode} param[{pi}] log failed: {exLog.Message}");
						}
					}

					if (int.TryParse(recipeCode.Substring(7), out int packedId) && packedId > 1000 && TryUnpackGraffitiRecipe(recipeCode, packedId, items, boltGameDatabaseProvider, request, Id))
						return;
				}
				
				switch (recipeCode)
				{
					case "HOT_WINTER_PARTY_2023_SKINS":
					case "HOT_WINTER_PARTY_2023_BOXES":
					case "HOT_WINTER_PARTY_2023_CASES":
					{
						int[] rareItems = new int[] { 146300, 146400, 141700, 144400, 144300, 145300 };
						int selectedDefId = rareItems[new Random().Next(rareItems.Length)];
						PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
						int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
						ExchangeResult exchangeResult = new ExchangeResult();
						BoltInventoryItem invItem = new BoltInventoryItem { itemDefinitionId = selectedDefId, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.Now) };
						exchangeResult.InventoryItems.Add(invItem.ToInventory(nextId));
						boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), invItem, nextId);
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
						return;
					}
					case "HOT_WINTER_PARTY_2023_STICKERS":
					{
						int[] rareItems = new int[] { 1292, 1293, 1289, 1291, 1295, 1294 };
						int selectedDefId = rareItems[new Random().Next(rareItems.Length)];
						PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
						int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
						ExchangeResult exchangeResult = new ExchangeResult();
						BoltInventoryItem invItem = new BoltInventoryItem { itemDefinitionId = selectedDefId, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.Now) };
						exchangeResult.InventoryItems.Add(invItem.ToInventory(nextId));
						boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), invItem, nextId);
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
						return;
					}
					case "HOT_WINTER_PARTY_2023_CHARMS":
					{
						int[] rareItems = new int[] { 5083, 5085, 5093, 5094, 5082, 5092 };
						int selectedDefId = rareItems[new Random().Next(rareItems.Length)];
						PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
						int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
						ExchangeResult exchangeResult = new ExchangeResult();
						BoltInventoryItem invItem = new BoltInventoryItem { itemDefinitionId = selectedDefId, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.Now) };
						exchangeResult.InventoryItems.Add(invItem.ToInventory(nextId));
						boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), invItem, nextId);
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
						return;
					}
					case "HOT_WINTER_PARTY_2023_CHARMS_MASK":
					{
						int[] rareItems = new int[] { 5081, 5084, 5086, 5087, 5088, 5089 };
						int selectedDefId = rareItems[new Random().Next(rareItems.Length)];
						PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
						int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
						ExchangeResult exchangeResult = new ExchangeResult();
						BoltInventoryItem invItem = new BoltInventoryItem { itemDefinitionId = selectedDefId, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.Now) };
						exchangeResult.InventoryItems.Add(invItem.ToInventory(nextId));
						boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), invItem, nextId);
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
						return;
					}

					case "CURSED_SOULS_SKINS":
					{
						// Все скины cursed_souls кроме Tanto (148000) — только в рулетке.
						int[] rareItems = new int[] { 141600, 141700, 144300, 144400, 145300, 146300, 146400 };
						int selectedDefId = rareItems[new Random().Next(rareItems.Length)];
						PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
						int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
						ExchangeResult exchangeResult = new ExchangeResult();
						BoltInventoryItem invItem = new BoltInventoryItem { itemDefinitionId = selectedDefId, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.Now) };
						exchangeResult.InventoryItems.Add(invItem.ToInventory(nextId));
						boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), invItem, nextId);
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
						return;
					}
					case "CURSED_SOULS_BOXES":
					{
						int[] boxIds = new int[] { 401, 402, 403, 404, 405, 406, 407 };
						int selectedDefId = boxIds[new Random().Next(boxIds.Length)];
						PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
						int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
						ExchangeResult exchangeResult = new ExchangeResult();
						BoltInventoryItem invItem = new BoltInventoryItem { itemDefinitionId = selectedDefId, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.Now) };
						exchangeResult.InventoryItems.Add(invItem.ToInventory(nextId));
						boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), invItem, nextId);
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
						return;
					}
					case "CURSED_SOULS_CASES":
					{
						int[] caseIds = new int[] { 301, 302, 303, 304, 305, 306, 307 };
						int selectedDefId = caseIds[new Random().Next(caseIds.Length)];
						PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
						int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
						ExchangeResult exchangeResult = new ExchangeResult();
						BoltInventoryItem invItem = new BoltInventoryItem { itemDefinitionId = selectedDefId, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.Now) };
						exchangeResult.InventoryItems.Add(invItem.ToInventory(nextId));
						boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), invItem, nextId);
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
						return;
					}
					case "CURSED_SOULS_STICKERS":
					{
						int[] rareItems = new int[] { 1288, 1289, 1290, 1291, 1292, 1293, 1294, 1295, 1296 };
						int selectedDefId = rareItems[new Random().Next(rareItems.Length)];
						PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
						int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
						ExchangeResult exchangeResult = new ExchangeResult();
						BoltInventoryItem invItem = new BoltInventoryItem { itemDefinitionId = selectedDefId, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.Now) };
						exchangeResult.InventoryItems.Add(invItem.ToInventory(nextId));
						boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), invItem, nextId);
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
						return;
					}
					case "CURSED_SOULS_CHARMS":
					case "CURSED_SOULS_CHARMS_MASK":
					{
						int[] rareItems = new int[] { 5081, 5082, 5083, 5084, 5085, 5086, 5087, 5088, 5089, 5090, 5091, 5092, 5093, 5094 };
						int selectedDefId = rareItems[new Random().Next(rareItems.Length)];
						PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
						int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
						ExchangeResult exchangeResult = new ExchangeResult();
						BoltInventoryItem invItem = new BoltInventoryItem { itemDefinitionId = selectedDefId, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.Now) };
						exchangeResult.InventoryItems.Add(invItem.ToInventory(nextId));
						boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), invItem, nextId);
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
						return;
					}
					case "CURSED_SOULS_SPIN_ROULETTE":
					case "CURSED_SOULS_SPIN":
					case "HALLOWEEN_2021_SPIN_ROULETTE":
					case "HALLOWEEN_SPIN_ROULETTE":
					case "SPIN_ROULETTE":
						{
							try
							{
								var window = boltGameDatabaseProvider.GetSpinWindowState();
								SpinLogger.Info($"IN player={Id} recipe={recipeCode} requestId={request.Id} windowActive={window.active} remainingMs={window.remainingMs} start={window.startMs} end={window.endMs}");
								if (!window.active)
								{
									// Никогда Exception/403 — клиент залипает на «ОШИБКА ЗАПРОСА».
									SpinLogger.Warn($"window closed player={Id} — soft wheel prize");
									int softNext = boltGameDatabaseProvider.AllocateNextInventoryItemId(ObjectId.Parse(Id));
									var soft = new BoltInventoryItem
									{
										itemDefinitionId = 141600,
										date = BsonDateTime.Create(DateTime.UtcNow),
										flags = 0,
										quantity = 1
									};
									boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), soft, softNext);
									var softPrize = soft.ToInventory(softNext);
									softPrize.Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
									SendSpinExchangeResult(request.Id, 0, 201, softPrize);
									return;
								}
								var oid = ObjectId.Parse(Id);
								int spentSlot = -1;
								int spentDef = 201;
								int clientSlot = (items != null && items.Length > 0) ? items[0]
									: (craftItems != null && craftItems.Length > 0) ? craftItems[0] : -1;
								if (clientSlot > 0)
								{
									if (boltGameDatabaseProvider.RemoveItemIfOwned(oid, clientSlot, 201))
									{ spentSlot = clientSlot; spentDef = 201; }
									else if (boltGameDatabaseProvider.RemoveItemIfOwned(oid, clientSlot, 219))
									{ spentSlot = clientSlot; spentDef = 219; }
									// clientSlot есть, но в БД уже нет — не трогаем другой слот (рассинхрон UI).
								}
								else if (spentSlot < 0)
								{
									spentSlot = boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(oid, 201);
									if (spentSlot >= 0) spentDef = 201;
									else
									{
										spentSlot = boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(oid, 219);
										if (spentSlot >= 0) spentDef = 219;
									}
								}
								if (spentSlot < 0)
								{
									// Нет токена → на приватке докидываем бесплатные спины (без Google Play).
									Logger.Log($"[Inventory] {recipeCode}: no spin token for {Id} clientSlot={clientSlot} — auto-granting 10x #201");
									try
									{
										for (int g = 0; g < 10; g++)
										{
											int grantId = boltGameDatabaseProvider.AllocateNextInventoryItemId(oid);
											boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(oid, new BoltInventoryItem
											{
												itemDefinitionId = 201,
												quantity = 1,
												flags = 0,
												date = BsonDateTime.Create(DateTime.UtcNow)
											}, grantId);
										}
										spentSlot = boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(oid, 201);
										if (spentSlot >= 0) spentDef = 201;
									}
									catch (System.Exception grantEx)
									{
										Logger.Error($"[Inventory] auto-grant spins failed: {grantEx.Message}");
									}
								}
								if (spentSlot < 0 && clientSlot > 0)
								{
									// Stale slot: токен уже списан клиентом — выдаём приз без повторного remove.
									Logger.Log($"[Inventory] {recipeCode}: stale clientSlot={clientSlot} for {Id} — prize only (no spent echo)");
									spentSlot = clientSlot;
									spentDef = 201;
								}
								if (spentSlot < 0)
								{
									SpinLogger.Warn($"no token player={Id} recipe={recipeCode} — synthetic + free wheel skin");
									spentDef = 201;
									int freeNext = boltGameDatabaseProvider.AllocateNextInventoryItemId(oid);
									int freeDef = 141600;
									var freeItem = new BoltInventoryItem
									{
										itemDefinitionId = freeDef,
										date = BsonDateTime.Create(DateTime.UtcNow),
										flags = 0,
										quantity = 1
									};
									boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(oid, freeItem, freeNext);
									var freePrize = freeItem.ToInventory(freeNext);
									freePrize.Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
									// Не репортить slot=1 — иначе delayed-remove снимает чужой предмет.
									SendSpinExchangeResult(request.Id, 0, spentDef, freePrize);
									return;
								}

								int spinNextId = boltGameDatabaseProvider.AllocateNextInventoryItemId(oid);

								int selectedDefId = PickCursedSoulsSpinPrize();

								var boltInventoryItem = new BoltInventoryItem
								{
									itemDefinitionId = selectedDefId,
									date = BsonDateTime.Create(DateTime.UtcNow),
									flags = 0,
									quantity = 1
								};
								// StatTrak только у дефов >= 1_000_000. Раньше порог был 100000 —
								// на ВСЕ скины колеса (141600…) вешался stattrack_value → клиент
								// после 1–2 удачных спинов начинал отдавать SpinnerReleaseFailed.
								if (selectedDefId >= 1_000_000)
								{
									boltInventoryItem.Properties.Add(new BoltInventoryItemProperty
									{
										Name = "stattrack_value",
										Type = BoltInventoryItemProperty.PropertyType.Int,
										Value = "0"
									});
								}

								boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(oid, boltInventoryItem, spinNextId);
								// Счётчик спинов 200001. CurrencyPlusValue дёргает CurrencyUpdate, который
								// ПУШИТ клиенту CurrencyAmount[] с этой валютой. В коде уже записано, что
								// currency 200001, доходя до клиента, вызывала «ошибка запроса»
								// (см. комментарий в getRecipeStatus). По умолчанию не трогаем.
								if (LocalServerConfig.Current.SpinCountCurrency200001)
								{ try { boltGameDatabaseProvider.CurrencyPlusValue(oid, 200001, 1); } catch { } }

								int reportSlot = clientSlot > 0 ? clientSlot : spentSlot;
								var prizeItem = boltInventoryItem.ToInventory(spinNextId);
								// Date = ticks (как BP/ToInventory). SendSpinExchangeResult тоже форсит ticks.
								prizeItem.Quantity = 1;
								if (selectedDefId < 1_000_000 && prizeItem.Properties != null)
									prizeItem.Properties.Remove("stattrack_value");
								Logger.Log($"[Inventory] {recipeCode}: spent slot={reportSlot} def#{spentDef} (clientSlot={clientSlot}, dbSlot={spentSlot}) → granted def#{selectedDefId} slot={spinNextId} to {Id}");
								SendSpinExchangeResult(request.Id, reportSlot, spentDef, prizeItem);
							}
							catch (System.Exception spinEx)
							{
								SpinLogger.Error($"FAIL player={Id} recipe={recipeCode} requestId={request.Id}: {spinEx.Message}", spinEx);
								int failClientSlot = (items != null && items.Length > 0) ? items[0]
									: (craftItems != null && craftItems.Length > 0) ? craftItems[0] : 0;
								try
								{
									var oidFail = ObjectId.Parse(Id);
									int nid = boltGameDatabaseProvider.AllocateNextInventoryItemId(oidFail);
									// Только скин с колеса — иначе клиент снова даст «ОШИБКА ЗАПРОСА».
									var consolation = new BoltInventoryItem
									{
										itemDefinitionId = 141600,
										date = BsonDateTime.Create(DateTime.UtcNow),
										flags = 0,
										quantity = 1
									};
									boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(oidFail, consolation, nid);
									var prize = consolation.ToInventory(nid);
									prize.Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
									SendSpinExchangeResult(request.Id, failClientSlot > 0 ? failClientSlot : 0, 201, prize);
								}
								catch (System.Exception spinEx2)
								{
									SpinLogger.Error($"FAIL consolation player={Id} recipe={recipeCode}", spinEx2);
									SendSpinExchangeResult(request.Id, 0, 201, new InventoryItem
									{
										Id = 1,
										ItemDefinitionId = 141600,
										Quantity = 1,
										Flags = 0,
										Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
									});
								}
							}
							return;
						}

					case "HOT_WINTER_PARTY_2023_SPIN_ROULETTE":
						{
							var windowHw = boltGameDatabaseProvider.GetSpinWindowState();
							SpinLogger.Info($"IN player={Id} recipe={recipeCode} requestId={request.Id} windowActive={windowHw.active} (hot_winter)");
							if (!windowHw.active)
							{
								SpinLogger.Warn($"REJECTED player={Id} hot_winter — window closed");
								SendError(request.Id, 403);
								return;
							}
							var hwOid = ObjectId.Parse(Id);
							int hwSpentSlot = -1;
							int hwSpentDef = 219;
							int hwClientSlot = (items != null && items.Length > 0) ? items[0]
								: (craftItems != null && craftItems.Length > 0) ? craftItems[0] : -1;
							if (hwClientSlot > 0)
							{
								if (boltGameDatabaseProvider.RemoveItemIfOwned(hwOid, hwClientSlot, 219))
								{ hwSpentSlot = hwClientSlot; hwSpentDef = 219; }
								else if (boltGameDatabaseProvider.RemoveItemIfOwned(hwOid, hwClientSlot, 201))
								{ hwSpentSlot = hwClientSlot; hwSpentDef = 201; }
							}
							if (hwSpentSlot < 0)
							{
								hwSpentSlot = boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(hwOid, 219);
								if (hwSpentSlot >= 0) hwSpentDef = 219;
								else
								{
									hwSpentSlot = boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(hwOid, 201);
									if (hwSpentSlot >= 0) hwSpentDef = 201;
								}
							}
							if (hwSpentSlot < 0)
							{
								try { boltGameDatabaseProvider.CurrencyPlusValue(hwOid, 101, 5); } catch { }
								int freeHwId = boltGameDatabaseProvider.AllocateNextInventoryItemId(hwOid);
								var freeHw = new BoltInventoryItem { itemDefinitionId = 141600, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.UtcNow) };
								boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(hwOid, freeHw, freeHwId);
								var freePrize = freeHw.ToInventory(freeHwId);
								freePrize.Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
								SendSpinExchangeResult(request.Id, hwClientSlot > 0 ? hwClientSlot : 0, 201, freePrize);
								return;
							}

							int nextId = boltGameDatabaseProvider.AllocateNextInventoryItemId(hwOid);
							// Только скины колеса — иначе SpinnerReleaseFailed / «ОШИБКА ЗАПРОСА».
							int selectedDefId = PickCursedSoulsSpinPrize();

							BoltInventoryItem boltInventoryItem = new BoltInventoryItem
							{
								itemDefinitionId = selectedDefId,
								date = BsonDateTime.Create(DateTime.UtcNow),
								flags = 0,
								quantity = 1
							};
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(hwOid, boltInventoryItem, nextId);
							int hwReportSlot = hwClientSlot > 0 ? hwClientSlot : hwSpentSlot;
							var hwPrize = boltInventoryItem.ToInventory(nextId);
							hwPrize.Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
							hwPrize.Quantity = 1;
							SendSpinExchangeResult(request.Id, hwReportSlot, hwSpentDef, hwPrize);
							return;
						}

					case "LEGENDS_SPIN":
						{
							var windowLeg = boltGameDatabaseProvider.GetSpinWindowState();
							SpinLogger.Info($"IN player={Id} recipe=LEGENDS_SPIN requestId={request.Id} windowActive={windowLeg.active}");
							if (!windowLeg.active)
							{
								SpinLogger.Warn($"REJECTED player={Id} legends — window closed");
								SendError(request.Id, 403);
								return;
							}
							var legOid = ObjectId.Parse(Id);
							int legSpentSlot = -1;
							int legSpentDef = 219;
							int legClientSlot = (items != null && items.Length > 0) ? items[0]
								: (craftItems != null && craftItems.Length > 0) ? craftItems[0] : -1;
							if (legClientSlot > 0)
							{
								if (boltGameDatabaseProvider.RemoveItemIfOwned(legOid, legClientSlot, 219))
								{ legSpentSlot = legClientSlot; legSpentDef = 219; }
								else if (boltGameDatabaseProvider.RemoveItemIfOwned(legOid, legClientSlot, 201))
								{ legSpentSlot = legClientSlot; legSpentDef = 201; }
							}
							if (legSpentSlot < 0)
							{
								legSpentSlot = boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(legOid, 219);
								if (legSpentSlot >= 0) legSpentDef = 219;
								else
								{
									legSpentSlot = boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(legOid, 201);
									if (legSpentSlot >= 0) legSpentDef = 201;
								}
							}
							if (legSpentSlot < 0)
							{
								int nid = boltGameDatabaseProvider.AllocateNextInventoryItemId(legOid);
								var consolation = new BoltInventoryItem { itemDefinitionId = 141600, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.UtcNow) };
								boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(legOid, consolation, nid);
								var p = consolation.ToInventory(nid);
								p.Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
								SendSpinExchangeResult(request.Id, legClientSlot > 0 ? legClientSlot : 0, 201, p);
								return;
							}

							int legendsNextId = boltGameDatabaseProvider.AllocateNextInventoryItemId(legOid);
							int legendsDefId = PickCursedSoulsSpinPrize();
							BoltInventoryItem legendsInvItem = new BoltInventoryItem
							{
								itemDefinitionId = legendsDefId,
								date = BsonDateTime.Create(DateTime.UtcNow),
								flags = 0,
								quantity = 1
							};
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(legOid, legendsInvItem, legendsNextId);
							int legReportSlot = legClientSlot > 0 ? legClientSlot : legSpentSlot;
							var legPrize = legendsInvItem.ToInventory(legendsNextId);
							legPrize.Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
							legPrize.Quantity = 1;
							SendSpinExchangeResult(request.Id, legReportSlot, legSpentDef, legPrize);
							return;
						}
					
					case "RECIPE_222":
					case "222":
					case "RECIPE_223":
					case "223":
					case "RECIPE_224":
					case "224":
						{
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							int spinsCount = (int)GetLocalCurrencyBalance(inventoryDocument.Currencies, "200001");
							
							int requiredSpins = recipeCode.EndsWith("222") ? 10 : (recipeCode.EndsWith("223") ? 30 : 50);
							if (spinsCount < requiredSpins)
							{
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 101 } } });
								return;
							}
							
							// Check if already claimed
							int claimCurrencyId = requiredSpins == 10 ? 200002 : (requiredSpins == 30 ? 200003 : 200004);
							if (GetLocalCurrencyBalance(inventoryDocument.Currencies, claimCurrencyId.ToString()) > 0)
							{
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 403 } } });
								return;
							}
							
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							
							int[] rewardItems = new int[] { 1290 }; // Default

							if (requiredSpins == 10 || recipeCode.EndsWith("222"))
							{
								rewardItems = new int[] { 1290 }; // Demonic Beast Color
							}
							else if (requiredSpins == 30 || recipeCode.EndsWith("223"))
							{
								rewardItems = new int[] { 5090 }; // Charm Soul
							}
							else if (requiredSpins == 50 || recipeCode.EndsWith("224"))
							{
								rewardItems = new int[] { 148000 }; // Tanto Restless
							}
							
							int selectedDefId = rewardItems[new Random().Next(rewardItems.Length)];
							BoltInventoryItem boltInventoryItem = new BoltInventoryItem
							{
								itemDefinitionId = selectedDefId,
								date = DateTime.Now,
								flags = 0,
								quantity = 1
							};
							if (selectedDefId > 1000000)
							{
								boltInventoryItem.Properties.Add(new BoltInventoryItemProperty { Name = "stattrack_value", Type = BoltInventoryItemProperty.PropertyType.Int, Value = "0" });
							}
							
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							boltGameDatabaseProvider.CurrencyPlusValue(ObjectId.Parse(Id), claimCurrencyId, 1);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_301":
						{
							// SECURITY: ATOMIC OWNERSHIP VALIDATION
							bool removed301 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.OriginCase)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.OriginCase) >= 0;
							if (!removed301)
							{
								Logger.Error($"[Inventory] RECIPE_301: player {Id} — OriginCase not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}

							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Rare, 53);
							randomGenerator.AddDropChance(SkinValue.Epic, 30);
							randomGenerator.AddDropChance(SkinValue.Legendary, 15);
							randomGenerator.AddDropChance(SkinValue.Arcane, 2);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomItem(CollectionId.Origin);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_301: GetRandomItem returned null for Origin"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_302":
						{
							bool removed302 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.FuriousCase)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.FuriousCase) >= 0;
							if (!removed302)
							{
								Logger.Error($"[Inventory] RECIPE_302: player {Id} — FuriousCase not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Rare, 53);
							randomGenerator.AddDropChance(SkinValue.Epic, 30);
							randomGenerator.AddDropChance(SkinValue.Legendary, 15);
							randomGenerator.AddDropChance(SkinValue.Arcane, 2);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomItem(CollectionId.Furious);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_302: GetRandomItem returned null for Furious"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_303":
						{
							bool removed303 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.RivalCase)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.RivalCase) >= 0;
							if (!removed303)
							{
								Logger.Error($"[Inventory] RECIPE_303: player {Id} — RivalCase not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Rare, 53);
							randomGenerator.AddDropChance(SkinValue.Epic, 30);
							randomGenerator.AddDropChance(SkinValue.Legendary, 15);
							randomGenerator.AddDropChance(SkinValue.Arcane, 2);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomItem(CollectionId.Rival);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_303: GetRandomItem returned null for Rival"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_304":
						{
							bool removed304 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.FableCase)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.FableCase) >= 0;
							if (!removed304)
							{
								Logger.Error($"[Inventory] RECIPE_304: player {Id} — FableCase not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Rare, 53);
							randomGenerator.AddDropChance(SkinValue.Epic, 30);
							randomGenerator.AddDropChance(SkinValue.Legendary, 15);
							randomGenerator.AddDropChance(SkinValue.Arcane, 2);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomItem(CollectionId.Fable);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_304: GetRandomItem returned null for Fable"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_305":
						{
							bool removed305 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.ScorpionCase)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.ScorpionCase) >= 0;
							if (!removed305)
							{
								Logger.Error($"[Inventory] RECIPE_305: player {Id} — ScorpionCase not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Rare, 53);
							randomGenerator.AddDropChance(SkinValue.Epic, 30);
							randomGenerator.AddDropChance(SkinValue.Legendary, 15);
							randomGenerator.AddDropChance(SkinValue.Arcane, 2);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomItem(CollectionId.Scorpion);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_305: GetRandomItem returned null for Scorpion"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_306":
						{
							bool removed306 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.EmpireCase)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.EmpireCase) >= 0;
							if (!removed306)
							{
								Logger.Error($"[Inventory] RECIPE_306: player {Id} — EmpireCase not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Rare, 53);
							randomGenerator.AddDropChance(SkinValue.Epic, 30);
							randomGenerator.AddDropChance(SkinValue.Legendary, 15);
							randomGenerator.AddDropChance(SkinValue.Arcane, 2);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomItem(CollectionId.Empire);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_306: GetRandomItem returned null for Empire"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_307":
						{
							bool removed307 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.SharpCase)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.SharpCase) >= 0;
							if (!removed307)
							{
								Logger.Error($"[Inventory] RECIPE_307: player {Id} — SharpCase not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Rare, 53);
							randomGenerator.AddDropChance(SkinValue.Epic, 30);
							randomGenerator.AddDropChance(SkinValue.Legendary, 15);
							randomGenerator.AddDropChance(SkinValue.Arcane, 2);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomItem(CollectionId.Sharp);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_307: GetRandomItem returned null for Sharp"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_401":
						{
							bool removed401 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.OriginBox)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.OriginBox) >= 0;
							if (!removed401)
							{
								Logger.Error($"[Inventory] RECIPE_401: player {Id} — OriginBox not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Common, 56.78);
							randomGenerator.AddDropChance(SkinValue.Uncommon, 37.85);
							randomGenerator.AddDropChance(SkinValue.Rare, 3.79);
							randomGenerator.AddDropChance(SkinValue.Epic, 1.42);
							randomGenerator.AddDropChance(SkinValue.Legendary, 0.14);
							randomGenerator.AddDropChance(SkinValue.Arcane, 0.02);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomBoxItem(CollectionId.Origin);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_401: GetRandomBoxItem returned null for Origin"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_402":
						{
							bool removed402 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.FuriousBox)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.FuriousBox) >= 0;
							if (!removed402)
							{
								Logger.Error($"[Inventory] RECIPE_402: player {Id} — FuriousBox not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Common, 56.78);
							randomGenerator.AddDropChance(SkinValue.Uncommon, 37.85);
							randomGenerator.AddDropChance(SkinValue.Rare, 3.79);
							randomGenerator.AddDropChance(SkinValue.Epic, 1.42);
							randomGenerator.AddDropChance(SkinValue.Legendary, 0.14);
							randomGenerator.AddDropChance(SkinValue.Arcane, 0.02);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomBoxItem(CollectionId.Furious);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_402: GetRandomBoxItem returned null for Furious"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_403":
						{
							bool removed403 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.RivalBox)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.RivalBox) >= 0;
							if (!removed403)
							{
								Logger.Error($"[Inventory] RECIPE_403: player {Id} — RivalBox not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Common, 56.78);
							randomGenerator.AddDropChance(SkinValue.Uncommon, 37.85);
							randomGenerator.AddDropChance(SkinValue.Rare, 3.79);
							randomGenerator.AddDropChance(SkinValue.Epic, 1.42);
							randomGenerator.AddDropChance(SkinValue.Legendary, 0.14);
							randomGenerator.AddDropChance(SkinValue.Arcane, 0.02);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomBoxItem(CollectionId.Rival);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_403: GetRandomBoxItem returned null for Rival"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_404":
						{
							bool removed404 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.FableBox)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.FableBox) >= 0;
							if (!removed404)
							{
								Logger.Error($"[Inventory] RECIPE_404: player {Id} — FableBox not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Common, 56.78);
							randomGenerator.AddDropChance(SkinValue.Uncommon, 37.85);
							randomGenerator.AddDropChance(SkinValue.Rare, 3.79);
							randomGenerator.AddDropChance(SkinValue.Epic, 1.42);
							randomGenerator.AddDropChance(SkinValue.Legendary, 0.14);
							randomGenerator.AddDropChance(SkinValue.Arcane, 0.02);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomBoxItem(CollectionId.Fable);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_404: GetRandomBoxItem returned null for Fable"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_405":
						{
							bool removed405 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.ScorpionBox)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.ScorpionBox) >= 0;
							if (!removed405)
							{
								Logger.Error($"[Inventory] RECIPE_405: player {Id} — ScorpionBox not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Common, 56.78);
							randomGenerator.AddDropChance(SkinValue.Uncommon, 37.85);
							randomGenerator.AddDropChance(SkinValue.Rare, 3.79);
							randomGenerator.AddDropChance(SkinValue.Epic, 1.42);
							randomGenerator.AddDropChance(SkinValue.Legendary, 0.14);
							randomGenerator.AddDropChance(SkinValue.Arcane, 0.02);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							ExchangeResult exchangeResult = new ExchangeResult();
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomBoxItem(CollectionId.Scorpion);
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_405: GetRandomBoxItem returned null for Scorpion"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_701":
						{
							bool removed701 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.Halloween2019StickersPack)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.Halloween2019StickersPack) >= 0;
							if (!removed701)
							{
								Logger.Error($"[Inventory] RECIPE_701: player {Id} — Halloween2019StickersPack not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Rare, 82.3);
							randomGenerator.AddDropChance(SkinValue.Epic, 15.4);
							randomGenerator.AddDropChance(SkinValue.Legendary, 2.1);
							randomGenerator.AddDropChance(SkinValue.Arcane, 0.2);
							// Пак наклеек/брелоков: лут — сам состав коллекции, а не оружейные скины.
							randomGenerator.StickerCharmPackMode = true;
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomItem(CollectionId.Halloween_2019);
							ExchangeResult exchangeResult = new ExchangeResult();
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_701: GetRandomItem returned null for Halloween_2019"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_702":
						{
							bool removed702 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.RainbowStickersPack)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.RainbowStickersPack) >= 0;
							if (!removed702)
							{
								Logger.Error($"[Inventory] RECIPE_702: player {Id} — RainbowStickersPack not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Rare, 82.3);
							randomGenerator.AddDropChance(SkinValue.Epic, 15.4);
							randomGenerator.AddDropChance(SkinValue.Legendary, 2.1);
							randomGenerator.AddDropChance(SkinValue.Arcane, 0.2);
							// Пак наклеек/брелоков: лут — сам состав коллекции, а не оружейные скины.
							randomGenerator.StickerCharmPackMode = true;
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomItem(CollectionId.Rainbow);
							ExchangeResult exchangeResult = new ExchangeResult();
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_702: GetRandomItem returned null for Rainbow"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_703":
						{
							int removedItemId = -1;
							if (items != null && items.Length > 0)
							{
								if (boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], 703))
									removedItemId = items[0];
							}
							else
							{
								removedItemId = boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), 703);
							}
							
							if (removedItemId < 0)
							{
								Logger.Error($"[Inventory] RECIPE_703: player {Id} - Splash Graffiti Pack not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							int[] packedIds = { 4055, 4057, 4061, 4063, 4069, 4075, 4079, 4081, 4053, 4059, 4065, 4071, 4073, 4077, 4049, 4051, 4083, 4085, 4047, 4067 };
							int randomPackedId = packedIds[new Random().Next(packedIds.Length)];
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							BoltInventoryItem boltInventoryItem = new BoltInventoryItem { itemDefinitionId = randomPackedId, date = new global::MongoDB.Bson.BsonDateTime(DateTime.UtcNow) };
							ExchangeResult exchangeResult = new ExchangeResult();
							exchangeResult.InventoryItems.Add(new InventoryItem { Id = removedItemId, Quantity = 0 });
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_901":
						{
							bool removed901 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.Halloween2020CharmPack)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.Halloween2020CharmPack) >= 0;
							if (!removed901)
							{
								Logger.Error($"[Inventory] RECIPE_901: player {Id} — Halloween2020CharmPack not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Rare, 82.3);
							randomGenerator.AddDropChance(SkinValue.Epic, 15.4);
							randomGenerator.AddDropChance(SkinValue.Legendary, 2.1);
							randomGenerator.AddDropChance(SkinValue.Arcane, 0.2);
							// Пак наклеек/брелоков: лут — сам состав коллекции, а не оружейные скины.
							randomGenerator.StickerCharmPackMode = true;
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomItem(CollectionId.Halloween_2020);
							ExchangeResult exchangeResult = new ExchangeResult();
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_901: GetRandomItem returned null for Halloween_2020"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_406":
						{
							bool removed406 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.EmpireBox)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.EmpireBox) >= 0;
							if (!removed406)
							{
								Logger.Error($"[Inventory] RECIPE_406: player {Id} — EmpireBox not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Common, 56.78);
							randomGenerator.AddDropChance(SkinValue.Uncommon, 37.85);
							randomGenerator.AddDropChance(SkinValue.Rare, 3.79);
							randomGenerator.AddDropChance(SkinValue.Epic, 1.25);
							randomGenerator.AddDropChance(SkinValue.Legendary, 0.28);
							randomGenerator.AddDropChance(SkinValue.Arcane, 0.05);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomBoxItem(CollectionId.Empire);
							ExchangeResult exchangeResult = new ExchangeResult();
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_406: GetRandomBoxItem returned null for Empire"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_407":
						{
							bool removed407 = (items != null && items.Length > 0)
								? boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], (int)InventoryId.SharpBox)
								: boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), (int)InventoryId.SharpBox) >= 0;
							if (!removed407)
							{
								Logger.Error($"[Inventory] RECIPE_407: player {Id} — SharpBox not found. items={string.Join(",", items ?? new int[0])}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							randomGenerator.AddDropChance(SkinValue.Common, 56.78);
							randomGenerator.AddDropChance(SkinValue.Uncommon, 37.85);
							randomGenerator.AddDropChance(SkinValue.Rare, 3.79);
							randomGenerator.AddDropChance(SkinValue.Epic, 1.25);
							randomGenerator.AddDropChance(SkinValue.Legendary, 0.28);
							randomGenerator.AddDropChance(SkinValue.Arcane, 0.05);
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							BoltInventoryItem boltInventoryItem = randomGenerator.GetRandomBoxItem(CollectionId.Sharp);
							ExchangeResult exchangeResult = new ExchangeResult();
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_407: GetRandomBoxItem returned null for Sharp"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_OPEN_GIFT_501":
						{
							if (items == null || items.Length == 0 || !boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], 501))
							{
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
								return;
							}
							var inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							var giftBoxProperties = boltGameDatabaseProvider.GetInventoryItemDefinition(501, 1)?.properties;
							var containsProperty = giftBoxProperties?.FirstOrDefault(p => p.Name == "contains").Value.AsString;
							var valuesProperty = giftBoxProperties?.FirstOrDefault(p => p.Name == "values").Value.AsString;
							if (containsProperty != null && valuesProperty != null)
							{
								var containsArray = containsProperty.Split(',').Select(int.Parse).ToArray();
								var valuesArray = valuesProperty.Split(',').Select(int.Parse).ToArray();
								for (var i = 0; i < Math.Min(containsArray.Length, valuesArray.Length); i++)
								{
									randomGenerator.AddGiftBox2018DropChance((InventoryId)containsArray[i], valuesArray[i]);
								}
							}
							var nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
							var exchangeResult = new ExchangeResult();
							var boltInventoryItem = randomGenerator.GetRandomGiftBoxItem();
							if (boltInventoryItem == null) { Logger.Error("[Inventory] RECIPE_OPEN_GIFT_501: GetRandomGiftBoxItem returned null"); _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } }); return; }
							exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "RECIPE_DROP_IN_GAME":
					case "RECIPE_DROP_IN_GAME_PRO":
					case "RECIPE_DROP_IN_GAME_RANKED":
					case "RECIPE_DROP_IN_GAME_PRO_RANKED":
					case "RECIPE_DROP_ON_LVL":
					case "RECIPE_DROP_ON_BONUS":
					case "RECIPE_GOOD_GAME_1":
					case "RECIPE_GOOD_GAME_2":
					case "RECIPE_GOOD_GAME_3":
					case "RECIPE_GOOD_GAME_1_RANKED":
					case "RECIPE_GOOD_GAME_2_RANKED":
					case "RECIPE_GOOD_GAME_3_RANKED":
						{
							ProcessPostMatchDrop(Id, recipeCode, boltGameDatabaseProvider, randomGenerator, request.Id);
							return;
						}
					case "306":
						{
							Console.WriteLine($"[InventoryRemoteService] Processing exchange recipe 306 (Empire Case) for player {Id}");
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) 
								? inventoryDocument.InventoryItems.Select(i => int.TryParse(i.Name, out int parsed) ? parsed : 0).Max() + 1 
								: 1;
								
							BoltInventoryItem item = new BoltInventoryItem {
								itemDefinitionId = 306,
								quantity = 1,
								flags = 0,
								date = (BsonDateTime)DateTime.Now
							};
							
							ExchangeResult exchangeResult = new ExchangeResult();
							exchangeResult.InventoryItems.Add(item.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), item, nextId);
							
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}
					case "406":
						{
							Console.WriteLine($"[InventoryRemoteService] Processing exchange recipe 406 (Empire Box) for player {Id}");
							PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) 
								? inventoryDocument.InventoryItems.Select(i => int.TryParse(i.Name, out int parsed) ? parsed : 0).Max() + 1 
								: 1;
								
							BoltInventoryItem item = new BoltInventoryItem {
								itemDefinitionId = 406,
								quantity = 1,
								flags = 0,
								date = (BsonDateTime)DateTime.Now
							};
							
							ExchangeResult exchangeResult = new ExchangeResult();
							exchangeResult.InventoryItems.Add(item.ToInventory(nextId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), item, nextId);
							
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}

					// ── Покупка серебра за голду ──────────────────────────────────────
					// 100 серебра (currency 101) за 10 голды (currency 102)
					case "RECIPE_BUY_SILVER_FOR_GOLD":
					case "BUY_SILVER_FOR_GOLD":
					case "RECIPE_EXCHANGE_GOLD_TO_SILVER":
					case "EXCHANGE_GOLD_TO_SILVER":
					case "RECIPE_SILVER_PURCHASE":
					case "SILVER_PURCHASE":
					case "BUY_CURRENCY_101":
					case "RECIPE_BUY_CURRENCY_101":
					case "RECIPE_101":
						{
							if (!boltGameDatabaseProvider.IsEnoughFunds(Id, "102", 10))
							{
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 101 } } });
								return;
							}
							boltGameDatabaseProvider.CurrencyMinusValue(ObjectId.Parse(Id), 102, 10);
							boltGameDatabaseProvider.CurrencyPlusValue(ObjectId.Parse(Id), 101, 100);
							Console.WriteLine($"[Inventory] {recipeCode}: gave 100 silver for 10 gold to player {Id}");
							var exchangeResult = new ExchangeResult();
							exchangeResult.Currencies.Add(new CurrencyAmount { CurrencyId = 101, Value = 100, OldValue = 100 });
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
							return;
						}

					// ── Награда за рекламу: 5 серебра ────────────────────────────────
					case "RECIPE_AD_REWARD":
					case "AD_REWARD":
					case "RECIPE_WATCH_AD":
					case "WATCH_AD":
					case "RECIPE_AD_SILVER":
					case "AD_SILVER":
					case "RECIPE_REWARDED_AD":
					case "REWARDED_AD":
						{
							boltGameDatabaseProvider.CurrencyPlusValue(ObjectId.Parse(Id), 101, 5);
							Console.WriteLine($"[Inventory] {recipeCode}: gave 5 silver (ad reward) to player {Id}");
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new BinaryValue { IsNull = true } } });
							return;
						}

					// ── Медали ветерана ────────────────────────────────────────────────
					case "RECIPE_MEDAL_VETERAN2021_BRONZE":
					case "RECIPE_MEDAL_VETERAN2021_SILVER":
					case "RECIPE_MEDAL_VETERAN2021_GOLD":
					case "RECIPE_MEDAL_VETERAN2021_PLATINUM":
					case "RECIPE_MEDAL_VETERAN2021_DIAMOND":
					case "RECIPE_MEDAL_VETERAN2022_BRONZE":
					case "RECIPE_MEDAL_VETERAN2022_SILVER":
					case "RECIPE_MEDAL_VETERAN2022_GOLD":
					case "RECIPE_MEDAL_VETERAN2022_PLATINUM":
					case "RECIPE_MEDAL_VETERAN2022_DIAMOND":
					case "RECIPE_MEDAL_VETERAN2023_BRONZE":
					case "RECIPE_MEDAL_VETERAN2023_SILVER":
					case "RECIPE_MEDAL_VETERAN2023_GOLD":
					case "RECIPE_MEDAL_VETERAN2023_ELITE_GOLD":
					case "RECIPE_MEDAL_VETERAN2023_PLATINUM":
					case "RECIPE_MEDAL_VETERAN2023_DIAMOND":
					case "RECIPE_MEDAL_VETERAN2024_BRONZE":
					case "RECIPE_MEDAL_VETERAN2024_SILVER":
					case "RECIPE_MEDAL_VETERAN2024_GOLD":
					case "RECIPE_MEDAL_VETERAN2024_ELITE_GOLD":
					case "RECIPE_MEDAL_VETERAN2024_PLATINUM":
					case "RECIPE_MEDAL_VETERAN2024_DIAMOND":
					case "RECIPE_MEDAL_VETERAN2025_BRONZE":
					case "RECIPE_MEDAL_VETERAN2025_SILVER":
					case "RECIPE_MEDAL_VETERAN2025_GOLD":
					case "RECIPE_MEDAL_VETERAN2025_ELITE_GOLD":
					case "RECIPE_MEDAL_VETERAN2025_PLATINUM":
					case "RECIPE_MEDAL_VETERAN2025_DIAMOND":
						{
							var medalItem = new BoltInventoryItem { itemDefinitionId = 146, quantity = 1, flags = 0, date = BsonDateTime.Create(DateTime.Now) };
							if (recipeCode.Contains("SILVER")) medalItem.itemDefinitionId = 147;
							else if (recipeCode.Contains("PLATINUM")) medalItem.itemDefinitionId = 149;
							else if (recipeCode.Contains("DIAMOND")) medalItem.itemDefinitionId = 150;
							else if (recipeCode.Contains("ELITE_GOLD")) medalItem.itemDefinitionId = 151;
							else if (recipeCode.Contains("GOLD")) medalItem.itemDefinitionId = 148;
							PlayerInventoryDocument invDoc = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
							int nextMedalId = (invDoc.InventoryItems.ElementCount > 0) ? invDoc.InventoryItems.Select(e => int.TryParse(e.Name, out int p) ? p : 0).Max() + 1 : 1;
							var medalResult = new ExchangeResult();
							medalResult.InventoryItems.Add(medalItem.ToInventory(nextMedalId));
							boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), medalItem, nextMedalId);
							Console.WriteLine($"[Inventory] {recipeCode}: gave medal (defId={medalItem.itemDefinitionId}) to player {Id}");
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(medalResult) } });
							return;
						}

					// ── XP бустеры ────────────────────────────────────────────────────
					case "1000XP":
					case "500XP":
					case "RECIPE_XP_1000":
					case "RECIPE_XP_500":
						{
							float xpAmount = recipeCode.Contains("500") ? 500f : 1000f;
							PlayerStatsManager.AddExperience(Id, xpAmount);
							Console.WriteLine($"[Inventory] {recipeCode}: gave {xpAmount} XP to player {Id}");
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new BinaryValue { IsNull = true } } });
							return;
						}

					// ── Обмен золота на серебро (клиент отправляет EXCHANGE_GOLD) ──────
					case "EXCHANGE_GOLD":
					case "EXCHANGE_GOLD_TO_SILVER_100":
						{
							if (!boltGameDatabaseProvider.IsEnoughFunds(Id, "102", 10))
							{
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 101 } } });
								return;
							}
							boltGameDatabaseProvider.CurrencyMinusValue(ObjectId.Parse(Id), 102, 10);
							boltGameDatabaseProvider.CurrencyPlusValue(ObjectId.Parse(Id), 101, 100);
							Console.WriteLine($"[Inventory] {recipeCode}: gave 100 silver for 10 gold to player {Id}");
							var exResult = new ExchangeResult();
							exResult.Currencies.Add(new CurrencyAmount { CurrencyId = 101, Value = 100, OldValue = 100 });
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exResult) } });
							return;
						}

					default:
						{
							// ── Универсальная распаковка граффити ──────────────────────────────
							// Определяем упакованное/распакованное граффити по НАЗВАНИЮ из каталога,
							// а не по чётности key (в каталоге Packed-граффити лежат и на нечётных,
							// и на чётных key, поэтому прежняя проверка key%2==0 ломала распаковку).
							if (recipeCode.StartsWith("RECIPE_") && recipeCode.Length > 7)
							{
								string keyStr = recipeCode.Substring(7);
								if (int.TryParse(keyStr, out int packedGraffitiKey))
								{
									var allDefs = BoltGameDatabaseProvider.Instance.GetAllItemDefinitionsByType(0);
									var packedDef = allDefs.FirstOrDefault(d => d != null && d.key == packedGraffitiKey);

									bool isGraffiti = packedDef != null
										&& packedDef.displayName != null
										&& packedDef.displayName.Contains("Graffiti", StringComparison.OrdinalIgnoreCase);
									bool isPacked = isGraffiti
										&& packedDef.displayName.IndexOf("Packed", StringComparison.OrdinalIgnoreCase) >= 0;

									if (isGraffiti)
									{
										if (!isPacked)
										{
											// Уже распакованное граффити — нечего распаковывать
											Logger.Error($"[Inventory] {recipeCode}: player {Id} — graffiti {packedGraffitiKey} is already unpacked.");
											_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 400 } } });
											return;
										}

										// Ищем распакованную версию по имени без слова "Packed"
										string unpackedName = packedDef.displayName;
										int idx = unpackedName.IndexOf("Packed", StringComparison.OrdinalIgnoreCase);
										unpackedName = unpackedName.Remove(idx).Trim();
										var unpackedDef = allDefs.FirstOrDefault(d => d != null && d.displayName != null && d.displayName.Trim().Equals(unpackedName, StringComparison.OrdinalIgnoreCase));
										if (unpackedDef == null)
										{
											Logger.Error($"[Inventory] {recipeCode}: player {Id} — unpacked version for graffiti '{packedDef.displayName}' not found in catalogue.");
											_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
											return;
										}
										int unpackedGraffitiKey = unpackedDef.key;

										int removedItemId = -1;
										if (items != null && items.Length > 0)
										{
											if (boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], packedGraffitiKey))
												removedItemId = items[0];
										}
										else
										{
											removedItemId = boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), packedGraffitiKey);
										}

										if (removedItemId < 0)
										{
											Logger.Error($"[Inventory] {recipeCode}: player {Id} — Packed graffiti {packedGraffitiKey} not found. items={string.Join(",", items ?? new int[0])}");
											_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
											return;
										}

										PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
										int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
										ExchangeResult exchangeResult = new ExchangeResult();
										exchangeResult.InventoryItems.Add(new InventoryItem { Id = removedItemId, Quantity = 0 });
										BoltInventoryItem boltInventoryItem = new BoltInventoryItem
										{
											itemDefinitionId = unpackedGraffitiKey,
											quantity = 30,
											flags = 0,
											date = BsonDateTime.Create(DateTime.UtcNow)
										};
										exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
										boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
										Logger.Log($"[Inventory] {recipeCode}: player {Id} unpacked graffiti {packedGraffitiKey} → {unpackedGraffitiKey} (30 uses)");
										_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
										return;
									}
								}
							}
							
							// ── Универсальный обработчик крафта CRAFT_{RARITY}_{COLLECTION} ──
							// Формат: CRAFT_COMMON_ORIGIN, CRAFT_RARE_EMPIRE, CRAFT_EPIC_FABLE и т.д.
							// Логика: сжигаем 10 предметов текущей редкости -> получаем 1 предмет следующей редкости
							if (recipeCode.StartsWith("CRAFT_", StringComparison.OrdinalIgnoreCase))
							{
								if (!TryParseCraftRecipe(recipeCode, out SkinValue craftInputRarity, out CollectionId craftCollection, out bool recipeWantsStattrack))
								{
									Logger.Error($"[Inventory] CRAFT: failed to parse recipe code '{recipeCode}' player={Id}");
									_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
									return;
								}

								SkinValue craftOutputRarity = craftInputRarity switch
								{
									SkinValue.Common    => SkinValue.Uncommon,
									SkinValue.Uncommon  => SkinValue.Rare,
									SkinValue.Rare      => SkinValue.Epic,
									SkinValue.Epic      => SkinValue.Legendary,
									SkinValue.Legendary => SkinValue.Arcane,
									SkinValue.Arcane    => SkinValue.None,
									_                   => SkinValue.None
								};
								if (craftOutputRarity == SkinValue.None)
								{
									Logger.Error($"[Inventory] CRAFT: Arcane is max rarity, cannot upgrade. recipe={recipeCode} player={Id}");
									_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 400 } } });
									return;
								}

								const int craftCost = 10;
								if (items == null || items.Length < craftCost)
								{
									Logger.Error($"[Inventory] CRAFT: not enough items. need={craftCost} got={items?.Length ?? 0} recipe={recipeCode} player={Id}");
									_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
									return;
								}

								var allDefinitions = BoltGameDatabaseProvider.Instance.GetAllItemDefinitionsByType(0)
									.Where(d => d != null && d.key > 0)
									.GroupBy(d => d.key)
									.ToDictionary(g => g.Key, g => g.First());

								var invDoc = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
								bool allValid = true;
								var usedCraftSlots = new HashSet<int>();
								int stattrackInputs = 0;
								int nonStattrackInputs = 0;
								CollectionId? firstSlotCollection = null;
								for (int ci = 0; ci < craftCost; ci++)
								{
									if (!usedCraftSlots.Add(items[ci]))
									{
										Logger.Error($"[Inventory] CRAFT: duplicate slot {items[ci]} in craft request. recipe={recipeCode} player={Id}");
										allValid = false; break;
									}

									var slot = invDoc.InventoryItems.FirstOrDefault(e => e.Name == items[ci].ToString());
									if (slot.Value == null || !slot.Value.IsBsonDocument)
									{
										Logger.Error($"[Inventory] CRAFT: slot {items[ci]} not found in inventory. recipe={recipeCode} player={Id}");
										allValid = false; break;
									}
									var slotDoc = slot.Value.AsBsonDocument;
									BsonValue slotDefValue = slotDoc.Contains("itemDefinitionId") ? slotDoc["itemDefinitionId"]
										: slotDoc.Contains("ItemDefinitionId") ? slotDoc["ItemDefinitionId"] : BsonValue.Create(0);
									int slotDefId = slotDefValue.IsInt32 ? slotDefValue.AsInt32 : slotDefValue.ToInt32();
									if (!allDefinitions.TryGetValue(slotDefId, out var slotInputDef))
									{
										Logger.Error($"[Inventory] CRAFT: slot {items[ci]} defId={slotDefId} definition not found. recipe={recipeCode} player={Id}");
										allValid = false; break;
									}

									var slotRarity = slotInputDef.GetSkinValue();
									var slotCollection = slotInputDef.GetCollectionId();
									if (slotRarity != craftInputRarity || slotCollection == CollectionId.None)
									{
										Logger.Error($"[Inventory] CRAFT: slot {items[ci]} defId={slotDefId} rarity={slotRarity} collection={slotCollection} invalid for recipe rarity={craftInputRarity}. recipe={recipeCode} player={Id}");
										allValid = false; break;
									}

									// Коллекция вывода берётся из рецепта (craftCollection). Если рецепт
									// коллекцию не фиксирует — запоминаем первую фактическую коллекцию.
									if (firstSlotCollection == null)
										firstSlotCollection = slotCollection;

									if (slotInputDef.IsStattrack()) stattrackInputs++;
									else nonStattrackInputs++;
								}
								if (!allValid || (craftCollection == CollectionId.None && firstSlotCollection == null))
								{
									_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
									return;
								}

								if (stattrackInputs > 0 && nonStattrackInputs > 0)
								{
									Logger.Error($"[Inventory] CRAFT: mixed stattrack/non-stattrack inputs not allowed. stattrack={stattrackInputs} regular={nonStattrackInputs} recipe={recipeCode} player={Id}");
									_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 400 } } });
									return;
								}

								bool craftOutputIsStattrack = recipeWantsStattrack || stattrackInputs == craftCost;
								CollectionId outputCollection = craftCollection != CollectionId.None
									? craftCollection
									: firstSlotCollection.Value;

								for (int ci = 0; ci < craftCost; ci++)
									boltGameDatabaseProvider.RemoveItemIfOwned(ObjectId.Parse(Id), items[ci]);

								var outputDefinitions = BoltGameDatabaseProvider.Instance.GetAllItemDefinitionsByType(0)
									.Where(d => d != null && d.key > 0 && d.GetCollectionId() == outputCollection && d.GetSkinValue() == craftOutputRarity && d.IsStattrack() == craftOutputIsStattrack)
									.ToList();

								if (outputDefinitions.Count == 0)
								{
									Logger.Error($"[Inventory] CRAFT: no output definitions. rarity={craftOutputRarity} collection={outputCollection} stattrack={craftOutputIsStattrack} recipe={recipeCode}");
									_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } });
									return;
								}

								var pickedDefinition = outputDefinitions[new Random().Next(outputDefinitions.Count)];
								var craftedItem = CreateInventoryItemFromDefinition(pickedDefinition);

								var freshInv = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
								int craftNextId = (freshInv.InventoryItems.ElementCount > 0)
									? freshInv.InventoryItems.Select(e => int.TryParse(e.Name, out int p) ? p : 0).Max() + 1
									: 1;
								boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), craftedItem, craftNextId);

								var craftResult = new ExchangeResult();
								craftResult.InventoryItems.Add(craftedItem.ToInventory(craftNextId));
								Console.WriteLine($"[Inventory] CRAFT success: recipe={recipeCode} player={Id} → defId={craftedItem.itemDefinitionId} rarity={craftOutputRarity} collection={outputCollection}");
								_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(craftResult) } });
								return;
							}

							if (IsPostMatchDropRecipe(recipeCode))
							{
								ProcessPostMatchDrop(Id, recipeCode, boltGameDatabaseProvider, randomGenerator, request.Id);
								return;
							}

							Logger.Error($"[Inventory] Unknown recipe code: '{recipeCode}' from player {Id}. Params count: {request.Params.Count}");
							if (IsSpinRouletteRecipe(recipeCode))
							{
								try
								{
									var oid2 = ObjectId.Parse(Id);
									int spentSlotFb = -1;
									int spentDefFb = 201;
									int clientSlotFb = (items != null && items.Length > 0) ? items[0]
										: (craftItems != null && craftItems.Length > 0) ? craftItems[0] : -1;
									if (clientSlotFb > 0)
									{
										if (boltGameDatabaseProvider.RemoveItemIfOwned(oid2, clientSlotFb, 201))
										{ spentSlotFb = clientSlotFb; spentDefFb = 201; }
										else if (boltGameDatabaseProvider.RemoveItemIfOwned(oid2, clientSlotFb, 219))
										{ spentSlotFb = clientSlotFb; spentDefFb = 219; }
									}
									if (spentSlotFb < 0)
									{
										spentSlotFb = boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(oid2, 201);
										if (spentSlotFb >= 0) spentDefFb = 201;
										else
										{
											spentSlotFb = boltGameDatabaseProvider.RemoveFirstItemByDefinitionId(oid2, 219);
											if (spentSlotFb >= 0) spentDefFb = 219;
										}
									}
									int prizeDef = PickCursedSoulsSpinPrize();
									int nid = boltGameDatabaseProvider.AllocateNextInventoryItemId(oid2);
									var prize = new BoltInventoryItem
									{
										itemDefinitionId = prizeDef,
										date = BsonDateTime.Create(DateTime.UtcNow),
										flags = 0,
										quantity = 1
									};
									boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(oid2, prize, nid);
									int reportSlot = clientSlotFb > 0 ? clientSlotFb : (spentSlotFb > 0 ? spentSlotFb : 0);
									SendSpinExchangeResult(request.Id, reportSlot, spentDefFb, prize.ToInventory(nid));
								}
								catch (System.Exception spinFbEx)
								{
									Logger.Error($"[Inventory] spin fallback '{recipeCode}' failed: {spinFbEx.Message}");
									SendSpinExchangeResult(request.Id, 0, 201, new InventoryItem
									{
										Id = 1,
										ItemDefinitionId = 141600,
										Quantity = 1,
										Flags = 0,
										Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
									});
								}
								return;
							}
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
							return;
						}
				}
				} // end try
				catch (System.Exception ex)
				{
					Logger.Error($"[Inventory] ExchangeInventoryItems error for recipe '{recipeCode}': {ex.Message}\n{ex.StackTrace}");
					// Спин/рулетка: НИКОГДА Exception — клиент залипает на «ошибка запроса».
					if (IsSpinRouletteRecipe(recipeCode))
					{
						try
						{
							var oidFail = ObjectId.Parse(Id);
							int nid = BoltGameDatabaseProvider.Instance.AllocateNextInventoryItemId(oidFail);
							var consolation = new BoltInventoryItem
							{
								itemDefinitionId = 141600,
								date = BsonDateTime.Create(DateTime.UtcNow),
								flags = 0,
								quantity = 1
							};
							BoltGameDatabaseProvider.Instance.AddItemToPlayerInventoryDocument(oidFail, consolation, nid);
							var consolationPrize = consolation.ToInventory(nid);
							consolationPrize.Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
							SendSpinExchangeResult(request.Id, 0, 201, consolationPrize);
						}
						catch
						{
							SendSpinExchangeResult(request.Id, 0, 201, new InventoryItem
							{
								Id = 1,
								ItemDefinitionId = 141600,
								Quantity = 1,
								Flags = 0,
								Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
							});
						}
						return;
					}
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } });
					return;
				}
			}
			// Сюда попадаем только если игрок не авторизован (Users.TryGetValue вернул false)
		}

		// ── Распаковка упакованного граффити по НАЗВАНИЮ из каталога ──────────────
		// Определяет Packed/распакованную версию по displayName, а не по чётности key
		// или по key±1 (в каталоге Packed-граффити лежат и на нечётных, и на чётных key).
		// Возвращает true, если обработала рецепт (и отправила ответ), иначе false.
		private bool TryUnpackGraffitiRecipe(string recipeCode, int packedGraffitiKey, int[] items, BoltGameDatabaseProvider boltGameDatabaseProviderLocal, RpcRequest request, string Id)
		{
			var allDefs = BoltGameDatabaseProvider.Instance.GetAllItemDefinitionsByType(0);
			var packedDef = allDefs.FirstOrDefault(d => d != null && d.key == packedGraffitiKey);

			bool isGraffiti = packedDef != null
				&& packedDef.displayName != null
				&& packedDef.displayName.Contains("Graffiti", StringComparison.OrdinalIgnoreCase);
			bool isPacked = isGraffiti
				&& packedDef.displayName.IndexOf("Packed", StringComparison.OrdinalIgnoreCase) >= 0;

			if (!isGraffiti)
				return false;

			if (!isPacked)
			{
				// Уже распакованное граффити — нечего распаковывать
				Logger.Error($"[Inventory] {recipeCode}: player {Id} — graffiti {packedGraffitiKey} is already unpacked.");
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 400 } } });
				return true;
			}

			// Ищем распакованную версию по имени без слова "Packed"
			string unpackedName = packedDef.displayName;
			int idx = unpackedName.IndexOf("Packed", StringComparison.OrdinalIgnoreCase);
			unpackedName = unpackedName.Remove(idx).Trim();
			var unpackedDef = allDefs.FirstOrDefault(d => d != null && d.displayName != null && d.displayName.Trim().Equals(unpackedName, StringComparison.OrdinalIgnoreCase));
			if (unpackedDef == null)
			{
				Logger.Error($"[Inventory] {recipeCode}: player {Id} — unpacked version for graffiti '{packedDef.displayName}' not found in catalogue.");
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
				return true;
			}
			int unpackedGraffitiKey = unpackedDef.key;

			int removedItemId = -1;
			if (items != null && items.Length > 0)
			{
				if (boltGameDatabaseProviderLocal.RemoveItemIfOwned(ObjectId.Parse(Id), items[0], packedGraffitiKey))
					removedItemId = items[0];
			}
			else
			{
				removedItemId = boltGameDatabaseProviderLocal.RemoveFirstItemByDefinitionId(ObjectId.Parse(Id), packedGraffitiKey);
			}

			if (removedItemId < 0)
			{
				Logger.Error($"[Inventory] {recipeCode}: player {Id} — Packed graffiti {packedGraffitiKey} not found. items={string.Join(",", items ?? new int[0])}");
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
				return true;
			}

			PlayerInventoryDocument inventoryDocument = boltGameDatabaseProviderLocal.GetPlayerInventoryDocument(ObjectId.Parse(Id));
			int nextId = (inventoryDocument.InventoryItems.ElementCount > 0) ? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max() + 1 : 1;
			ExchangeResult exchangeResult = new ExchangeResult();
			BoltInventoryItem boltInventoryItem = new BoltInventoryItem
			{
				itemDefinitionId = unpackedGraffitiKey,
				quantity = 30,
				flags = 0,
				date = BsonDateTime.Create(DateTime.UtcNow)
			};
			exchangeResult.InventoryItems.Add(boltInventoryItem.ToInventory(nextId));
			boltGameDatabaseProviderLocal.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
			Logger.Log($"[Inventory] {recipeCode}: player {Id} unpacked graffiti {packedGraffitiKey} → {unpackedGraffitiKey} (30 uses)");
			_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
			return true;
		}

		/// <summary>
		/// Базовый спин-токен того же ивента, что и слот магазина: Halloween/Cursed Souls
		/// (#202..#204) -> #201, New Year 2022 (#206..#208) -> #205, Legends (#210..#215) -> #209,
		/// Hot Winter Party 2023 (#222..#224) -> #219. Токены именно базового предмета
		/// тратит рулетка, поэтому пак обязан выдавать их, а не сам себя.
		/// </summary>
		private static int BaseSpinTokenFor(int slotItemId)
		{
			if (slotItemId >= 202 && slotItemId <= 204) return 201;
			if (slotItemId >= 206 && slotItemId <= 208) return 205;
			if (slotItemId >= 210 && slotItemId <= 215) return 209;
			if (slotItemId >= 220 && slotItemId <= 226) return 219;
			return 201;
		}

		/// <summary>
		/// Цена предмета в голде (валюта 102) из каталога предметов — та же, что уходит клиенту
		/// в getInventoryItemDefinitions. Если в каталоге цены нет, возвращается fallback.
		/// </summary>
		private static int GetCataloguePriceInGold(int itemId, int fallback)
		{
			try
			{
				var definition = InventoryCatalogueLoader.Instance.GetByKey(itemId);
				if (definition?.buyPrice != null)
				{
					foreach (var entry in definition.buyPrice)
					{
						if (entry is not BsonDocument priceDoc) continue;
						if (!priceDoc.TryGetValue("currencyId", out BsonValue currency)) continue;
						if (currency.ToInt32() != 102) continue;
						if (!priceDoc.TryGetValue("value", out BsonValue value)) continue;
						int price = value.ToInt32();
						if (price > 0) return price;
					}
				}
			}
			catch (System.Exception ex)
			{
				Logger.LogWarn($"[Inventory] catalogue price lookup failed for #{itemId}: {ex.Message}");
			}
			return fallback;
		}

		protected void BuyInventoryItem(RpcRequest request)
		{
			if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
				return;
			}

			try
			{
				bool isEncrypted = request.MethodName.Equals("buyInventoryItemEncrypted", StringComparison.OrdinalIgnoreCase);
				if (!TryReadBuyInventoryItemRequest(request, out int itemId, out int quantity, out int currencyId, out bool toManyItems, out bool wrapperResponse))
				{
					// Фоллбэк: клиент 0.17 иногда шлёт sku/строку вместо int — пробуем угадать pass/spin.
					string rawHint = "";
					try
					{
						if (request.Params != null)
						{
							foreach (var p in request.Params)
							{
								if (p?.One == null || p.One.Length == 0) continue;
								rawHint += System.Text.Encoding.UTF8.GetString(p.One.ToByteArray()) + " ";
							}
						}
					}
					catch { }
					Logger.Error($"[Inventory] BuyInventoryItem parse fail params={request.Params?.Count} hint='{rawHint}' method={request.MethodName}");
					string h = (rawHint ?? "").ToLowerInvariant();
					if (h.Contains("608") || h.Contains("gold") && h.Contains("pass") || h.Contains("goldpass"))
					{
						itemId = 608; quantity = 1; currencyId = 102; wrapperResponse = true;
					}
					else if (h.Contains("201") || h.Contains("spin") || h.Contains("204") || h.Contains("203") || h.Contains("202"))
					{
						itemId = h.Contains("204") ? 204 : h.Contains("203") ? 203 : h.Contains("202") ? 202 : 201;
						quantity = 1; currencyId = 102; wrapperResponse = true;
					}
					else
					{
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 400 } } });
						return;
					}
				}

				BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
				Logger.Log($"[Inventory] BuyInventoryItem player={Id} item={itemId} qty={quantity} currency={currencyId} wrap={wrapperResponse}");

				// IAP/currency=0 → на приватке всегда голда (каналы Google Play обрезаны).
				if (currencyId <= 0) currencyId = 102;

				// Покупка спинов, слотов магазина рулетки и боевых пропусков — без Google Play.
				// Диапазоны те же, что и в InventoryCatalogueLoader.EnsurePrivateServerBuyPrices:
				// 200..299 — спины и их магазинные слоты, 600..699 — пропуска. Если клиент
				// попросит любой из них, выдаём сами и списываем голду; в биллинг он не уходит.
				bool isSpinFamily = itemId >= 200 && itemId <= 299;
				bool isPassFamily = itemId >= 600 && itemId <= 699;
				if (isSpinFamily || isPassFamily)
				{
					int grantDefId = itemId;
					int grantQty = quantity <= 0 ? 1 : Math.Min(quantity, 100);

					// Сколько спинов в этом слоте — тем же правилом, что каталог ставил цену.
					// Диалог клиента (GetSpinsDialogComponent) предлагает 1/10/20/40 спинов,
					// раньше сервер за те же кнопки выдавал 1/5/10/20 — игрок покупал «10»
					// и получал 5.
					string defName = null;
					try { defName = InventoryCatalogueLoader.Instance.GetByKey(itemId)?.displayName; } catch { }
					int spinPack = isSpinFamily ? InventoryCatalogueLoader.GetSpinPackSize(itemId, defName) : 0;

					// Цену берём из каталога (buyPrice в валюте 102) — ровно ту, которую клиент
					// нарисовал в магазине. Хардкод рядом с каталогом рано или поздно разъезжается,
					// и игрок видит одну цену, а списывается другая.
					int goldCostPer = GetCataloguePriceInGold(itemId, spinPack > 0 ? spinPack : 1);

					// Слот на несколько спинов → выдаём столько же токенов базового спина.
					bool isSpinPack = spinPack > 1;
					if (isSpinPack)
					{
						grantDefId = BaseSpinTokenFor(itemId);
						grantQty = Math.Max(grantQty, spinPack);
					}

					int buyUnits = Math.Max(1, quantity <= 0 ? 1 : quantity);
					// Пак покупается целиком: его цена уже за весь пак, на число токенов внутри
					// не умножается.
					int totalCost = isSpinPack ? goldCostPer : goldCostPer * buyUnits;

					// Спины и паки спинов теперь стоят голду (раньше здесь стояло totalCost = 0
					// и всё выдавалось бесплатно). Боевые пропуска остаются бесплатными —
					// их выдаёт донат-бот, и цена 1 голда в каталоге для них бессмысленна.
					if (isPassFamily) totalCost = 0;

					ObjectId spinOid = ObjectId.Parse(Id);
					if (currencyId == 102 && totalCost > 0
						&& !boltGameDatabaseProvider.IsEnoughFunds(Id, "102", totalCost))
					{
						Logger.Log($"[Inventory] BuyInventoryItem: not enough gold for #{itemId} need={totalCost}");
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 101 } } });
						return;
					}
					if (currencyId == 102 && totalCost > 0)
					{
						try { boltGameDatabaseProvider.CurrencyMinusValue(spinOid, 102, totalCost); }
						catch (System.Exception debitEx)
						{
							Logger.Error($"[Inventory] BuyInventoryItem debit fail: {debitEx.Message}");
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 101 } } });
							return;
						}
					}

					var spinInv = boltGameDatabaseProvider.GetPlayerInventoryDocument(spinOid);
					int spinMaxId = spinInv.InventoryItems.ElementCount > 0
						? spinInv.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max()
						: 0;
					var grantedSpins = new List<PlayerInventoryItem>();
					for (int x = 0; x < grantQty; x++)
					{
						int nextId = spinMaxId + x + 1;
						var spinItem = new BoltInventoryItem
						{
							itemDefinitionId = grantDefId,
							quantity = 1,
							flags = 0,
							date = BsonDateTime.Create(DateTime.UtcNow)
						};
						boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(spinOid, spinItem, nextId);
						grantedSpins.Add(spinItem.ToPlayerInventoryItem(nextId));
					}
					if (grantDefId == 608 || grantDefId == 613)
					{
						try { GameEventRemoteService.OnGoldPassAcquired(Id); } catch { }
					}
					Logger.Log($"[Inventory] BuyInventoryItem: granted {grantQty}x #{grantDefId} (from buy #{itemId}, cost={totalCost}g) to {Id}");
					BinaryValue spinResp = wrapperResponse
						? CreateBuyInventoryItemResponse(grantedSpins)
						: new ToByteMethod(typeof(PlayerInventoryItem[])).ToBytes(grantedSpins.ToArray());
					if (isEncrypted)
					{
						byte[] plain = spinResp.One?.ToByteArray() ?? Array.Empty<byte>();
						byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
						byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
						byte[] encrypted = Utils.EncryptByte(plain, key, IV);
						spinResp = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(encrypted) };
					}
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = spinResp } });
					return;
				}

				if (itemId == 101 && currencyId == 102)
				{
					// Bug 24: buying silver for gold reported an error to the client.
					// Two underlying problems made the call brittle:
					//  1. CurrencyMinusValue throws a bare Exception if the IsEnoughFunds
					//     re-check inside it fails (race vs the outer check). That used to
					//     bubble up to the generic catch and surface as a 500 to the client.
					//  2. The success response was an empty PlayerInventoryItem[] which
					//     decodes to a non-null but empty repeated field, and some client
					//     paths interpret that as "buy failed, no items received".
					// We now wrap the currency mutations defensively, only credit the
					// silver after the gold debit actually succeeds, and respond with a
					// stable IsNull=true value (matches what BuyInventoryItem returns when
					// the response payload is intentionally empty elsewhere).
					int exchangeCount = quantity <= 0 ? 1 : quantity;
					if (exchangeCount > 100)
					{
						exchangeCount = 100;
					}
					int totalGoldCost = 10 * exchangeCount;
					int totalSilverReward = 100 * exchangeCount;
					if (!boltGameDatabaseProvider.IsEnoughFunds(Id, "102", totalGoldCost))
					{
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 101 } } });
						return;
					}

					ObjectId playerOid;
					try { playerOid = ObjectId.Parse(Id); }
					catch (System.Exception parseEx)
					{
						Logger.Error($"[Inventory] BuyInventoryItem silver-for-gold: invalid player id '{Id}': {parseEx.Message}");
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
						return;
					}

					try
					{
						boltGameDatabaseProvider.CurrencyMinusValue(playerOid, 102, totalGoldCost);
					}
					catch (System.Exception minusEx)
					{
						Logger.Error($"[Inventory] BuyInventoryItem silver-for-gold: failed to debit {totalGoldCost} gold from {Id}: {minusEx.Message}");
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 101 } } });
						return;
					}

					try
					{
						boltGameDatabaseProvider.CurrencyPlusValue(playerOid, 101, totalSilverReward);
					}
					catch (System.Exception plusEx)
					{
						// Refund the gold so the player isn't left with a debit they
						// did not get silver for.
						Logger.Error($"[Inventory] BuyInventoryItem silver-for-gold: failed to credit {totalSilverReward} silver to {Id}: {plusEx.Message}");
						try { boltGameDatabaseProvider.CurrencyPlusValue(playerOid, 102, totalGoldCost); } catch { }
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } });
						return;
					}

					Logger.Log($"[Inventory] BuyInventoryItem: gave {totalSilverReward} silver for {totalGoldCost} gold to player {Id}");

					isEncrypted = request.MethodName.Equals("buyInventoryItemEncrypted", StringComparison.OrdinalIgnoreCase);
					BinaryValue emptyBuyResponse = wrapperResponse
						? (isEncrypted 
							? CreateBuyInventoryItemResponse(Array.Empty<PlayerInventoryItem>()) 
							: new BinaryValue { IsNull = true })
						: new ToByteMethod(typeof(PlayerInventoryItem[])).ToBytes(Array.Empty<PlayerInventoryItem>());
					if (isEncrypted)
					{
						byte[] plain = emptyBuyResponse.One?.ToByteArray() ?? Array.Empty<byte>();
						byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
						byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
						byte[] encrypted = Utils.EncryptByte(plain, key, IV);
						emptyBuyResponse = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(encrypted) };
					}
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = emptyBuyResponse } });
					return;
				}

				InventoryPurchaseValidation validation = InventoryDupeProtection.ValidatePurchase(
					Id, itemId, quantity, currencyId, boltGameDatabaseProvider);
				if (!validation.Allowed)
				{
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = validation.ErrorCode } } });
					return;
				}

				boltGameDatabaseProvider.CurrencyMinusValue(ObjectId.Parse(Id), currencyId, validation.TotalCost);
				PlayerInventoryDocument inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id));
				List<PlayerInventoryItem> inventoryItems = new List<PlayerInventoryItem>();
				int maxExistingId = inventoryDocument.InventoryItems.ElementCount > 0
					? inventoryDocument.InventoryItems.Select(e => int.TryParse(e.Name, out int parsed) ? parsed : 0).Max()
					: 0;

				for (int x = 0; x < quantity; x++)
				{
					int nextId = maxExistingId + x + 1;
					BoltInventoryItem boltInventoryItem = new BoltInventoryItem { itemDefinitionId = itemId, date = BsonDateTime.Create(DateTime.UtcNow), flags = 0, quantity = 1 };
					boltGameDatabaseProvider.AddItemToPlayerInventoryDocument(ObjectId.Parse(Id), boltInventoryItem, nextId);
					inventoryItems.Add(boltInventoryItem.ToPlayerInventoryItem(nextId));
				}

				BinaryValue returnValue = wrapperResponse
					? CreateBuyInventoryItemResponse(inventoryItems)
					: new ToByteMethod(typeof(PlayerInventoryItem[])).ToBytes(inventoryItems.ToArray());
				if (isEncrypted)
				{
					byte[] plain = returnValue.One?.ToByteArray() ?? Array.Empty<byte>();
					byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
					byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
					byte[] encrypted = Utils.EncryptByte(plain, key, IV);
					returnValue = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(encrypted) };
				}
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = returnValue } });
			}
			catch (System.Exception ex)
			{
				Logger.Error($"[Inventory] BuyInventoryItem error: {ex.Message}\n{ex.StackTrace}");
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } });
			}
		}
		protected void SetInventoryItemFlags(RpcRequest request)
		{
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
				
				byte[] flagsBytes = request.Params[0].One.ToByteArray();
				if (request.MethodName.Equals("setInventoryItemFlags2", StringComparison.OrdinalIgnoreCase))
				{
					flagsBytes = Utils.ExtractBinaryValueOne(flagsBytes);
				}
				BinaryValue extractedVal = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(flagsBytes) };
				ItemFlags itemFlags = (ItemFlags)new FromByteMethod(typeof(ItemFlags)).FromBytes(extractedVal);
				foreach (KeyValuePair<int, int> keyValuePair in itemFlags.Flags) boltGameDatabaseProvider.SetItemFlag(ObjectId.Parse(Id), keyValuePair.Key, keyValuePair.Value);
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new BinaryValue { IsNull = true } } });
				return;
			}
			_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
		}
		protected void SetInventoryItemsPropertiesEncrypted(RpcRequest request)
  		{
  			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
  			{
  				try 
  				{
  				    byte[] setKey = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
  				    byte[] setIV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
  				    byte[] decryptedBytes = Utils.DecryptByte(request.Params[0].One.ToByteArray(), setKey, setIV);
  				    byte[] innerBytes = Utils.ExtractBinaryValueOne(decryptedBytes);
  				    var innerBinaryValue = new BinaryValue();
  				    innerBinaryValue.MergeFrom(new Google.Protobuf.CodedInputStream(innerBytes));
                    var newReq = new RpcRequest {
                        Id = request.Id,
                        MethodName = "setInventoryItemsProperties",
                        Params = { innerBinaryValue }
                    };
                    SetInventoryItemsProperties(newReq);
  				}
  				catch (System.Exception ex)
  				{
  				    System.Console.WriteLine($"[SetInventoryItemsPropertiesEncrypted] Error: {ex}");
  				    SendError(request.Id, 500);
  				}
  			}
  			else SendError(request.Id, 401);
  		}
        
        protected void SetInventoryItemsProperties(RpcRequest request)
		{
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
				InventoryItemProperties[] inventoryItemProperties = (InventoryItemProperties[])new FromByteMethod(typeof(InventoryItemProperties[])).FromBytes(request.Params[0]);
				foreach (InventoryItemProperties a in inventoryItemProperties) boltGameDatabaseProvider.SetItemProperties(ObjectId.Parse(Id), a.Id, a.Properties.Select(s => new BoltInventoryItemProperty { Name = s.Key, Type = (BoltInventoryItemProperty.PropertyType)s.Value.Type, Value = s.Value.IntValue.ToString() }).ToArray());
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Return = new BinaryValue { IsNull = true } } });
				return;
			}
			_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
		}

		protected async void ApplyInventoryItem(int? consumedItemId, int? appliedItemId, string appliedPropertyName, bool IsRemovable, string guid)
		{
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				try
				{
					BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
					PlayerInventoryDocument inventoryDocument = await Task.Run(() => boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id)));
					
					var consumedBson = inventoryDocument.InventoryItems.FirstOrDefault(i => i.Name == consumedItemId.ToString());
					
					if (consumedItemId.HasValue && consumedBson.Value != null)
					{
						BoltInventoryItem consumedItem = consumedBson.ToBoltInventory();
						string itemName = consumedItem.itemDefinitionId.ToString();
						int defId = consumedItem.itemDefinitionId;
						bool isXpItem = defId == 801 || defId == 802 || defId == 212 || defId == 213 || defId == 214;

						if (isXpItem)
						{
							float xpAmount = 1000f;
							if (defId == 801) xpAmount = 500f;
							else if (defId == 212) xpAmount = 50f;
							else if (defId == 213) xpAmount = 200f;
							else if (defId == 214) xpAmount = 100f;
							PlayerStatsManager.AddExperience(Id, xpAmount);
							await boltGameDatabaseProvider.RemoveItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), consumedItemId.Value);
							Console.WriteLine($"[Inventory] Applied XP item: defId={defId}, gave {xpAmount} XP to player {Id}");
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new BinaryValue { IsNull = true } } });
							return;
						}

						if (appliedItemId.HasValue)
						{
							var appliedBson = inventoryDocument.InventoryItems.FirstOrDefault(i => i.Name == appliedItemId.ToString());
							if (appliedBson.Value == null)
							{
								SendError(guid, 404);
								return;
							}
							BoltInventoryItem appliedItem = appliedBson.ToBoltInventory();
							appliedItem.Properties.RemoveAll(p => p.Name == appliedPropertyName);
							appliedItem.Properties.Add(new BoltInventoryItemProperty 
							{ 
								Name = appliedPropertyName, 
								Type = BoltInventoryItemProperty.PropertyType.Int, 
								Value = consumedItem.itemDefinitionId.ToString() 
							});
							await boltGameDatabaseProvider.RemoveItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), consumedItemId.Value);
							await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), appliedItem, appliedItemId.Value);
							_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new ToByteMethod(typeof(PlayerInventoryItem)).ToBytes(appliedItem.ToPlayerInventoryItem(appliedItemId.Value)) } });
							return;
						}

						await boltGameDatabaseProvider.RemoveItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), consumedItemId.Value);
						Console.WriteLine($"[Inventory] Consumed item: defId={defId} for player {Id}");
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new BinaryValue { IsNull = true } } });
						return;
					}

					if (appliedItemId.HasValue)
					{
						var appliedBson = inventoryDocument.InventoryItems.FirstOrDefault(i => i.Name == appliedItemId.ToString());
						if (appliedBson.Value == null)
						{
							SendError(guid, 404);
							return;
						}
						BoltInventoryItem appliedItem = appliedBson.ToBoltInventory();
						appliedItem.Properties.RemoveAll(p => p.Name == appliedPropertyName);
						await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), appliedItem, appliedItemId.Value);
						_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new ToByteMethod(typeof(PlayerInventoryItem)).ToBytes(appliedItem.ToPlayerInventoryItem(appliedItemId.Value)) } });
						return;
					}

					SendError(guid, 404);
				}
				catch (System.Exception ex)
				{
					Logger.Error($"[Inventory] Error applying items: {ex.Message}");
					SendError(guid, 500);
				}
				return;
			}
			_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
		}

		// Клиент (Axlebolt.Standoff.Main.Inventory.InventoryManager.IncStatTrackProperty) для StatTrack
		// вызывает BoltInventoryService.SetInventoryItemProperty(itemId, propertyName, value) —
		// это ЕДИНСТВЕННОЕ число (setInventoryItemProperty), а наш диспетчер знал только
		// множественное "setInventoryItemsProperties" (массив). Из-за этого килл стриковым
		// оружием никогда не долетал до базы — сервер отвечал 404 "Method not found", и
		// счётчик stattrack_value никогда не увеличивался. Добавляем прямой обработчик.
		// Клиент вызывает ConsumeInventoryItem(inventoryItemId, quantity) для расходников
		// (аптечки/патроны/бустеры и т.п.) — раньше такого обработчика не было вообще, и любой
		// вызов падал в 404 "Method not found: consumeInventoryItem". Уменьшаем quantity предмета,
		// при обнулении — удаляем его из инвентаря целиком, возвращаем актуальное состояние.
		protected void ConsumeInventoryItem(int? itemId, int quantity, string guid)
		{
			if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				SendError(guid, 401);
				return;
			}

			try
			{
				var boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
				var playerOid = ObjectId.Parse(Id);
				var (removed, remaining) = boltGameDatabaseProvider.ConsumeItemQuantity(playerOid, itemId.Value, quantity);

				if (removed)
				{
					// Предмет полностью израсходован и удалён — возвращаем "пустой" PlayerInventoryItem
					// с этим же Id и quantity 0, чтобы клиент понял, что предмета больше нет.
					var emptyItem = new PlayerInventoryItem { Id = itemId.Value, Quantity = 0 };
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new ToByteMethod(typeof(PlayerInventoryItem)).ToBytes(emptyItem) } });
					return;
				}

				var inventoryDocument = boltGameDatabaseProvider.GetPlayerInventoryDocument(playerOid);
				var itemBson = inventoryDocument.InventoryItems.FirstOrDefault(i => i.Name == itemId.ToString());
				if (itemBson.Value == null)
				{
					SendError(guid, 404);
					return;
				}

				BoltInventoryItem updatedItem = itemBson.ToBoltInventory();
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new ToByteMethod(typeof(PlayerInventoryItem)).ToBytes(updatedItem.ToPlayerInventoryItem(itemId.Value)) } });
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"[Inventory] ConsumeInventoryItem error: {ex.Message}");
				SendError(guid, 500);
			}
		}

		protected void SetInventoryItemProperty(int? itemId, string propertyName, int value, string guid)
		{
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				try
				{
					BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
					boltGameDatabaseProvider.SetItemProperty(ObjectId.Parse(Id), itemId.Value, new BoltInventoryItemProperty
					{
						Name = propertyName,
						Type = BoltInventoryItemProperty.PropertyType.Int,
						Value = value.ToString()
					});
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new BinaryValue { IsNull = true } } });
				}
				catch (System.Exception ex)
				{
					Logger.Error($"[Inventory] Error setting item property ({propertyName}={value} on item {itemId}): {ex.Message}");
					SendError(guid, 500);
				}
				return;
			}
			_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
		}

		protected async void RemoveInventoryItemProperty(int? itemId, string propertyName, string guid)
		{
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				try
				{
					BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
					PlayerInventoryDocument inventoryDocument = await Task.Run(() => boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id)));
					
					var itemBson = inventoryDocument.InventoryItems.FirstOrDefault(i => i.Name == itemId.ToString());
					if (itemBson.Value == null)
					{
						SendError(guid, 404);
						return;
					}

					BoltInventoryItem appliedItem = itemBson.ToBoltInventory();
					var property = appliedItem.Properties.FirstOrDefault(a => a.Name == propertyName);
					
					if (property != null)
					{
						// Bug 23: only refund "reusable" mounted items (charms) — stickers and
						// other consumable applies (peel/replace) must NOT come back to the
						// inventory. Previously every property removal that wasn't sticker
						// still went through this branch only for "charm", but the client
						// sometimes sent "charm" as the property name for stickers as well,
						// so we now go through the shared helper.
						if (IsReusableMountProperty(propertyName))
						{
							try
							{
								int charmDefId = int.Parse(property.Value);
								int nextId = inventoryDocument.InventoryItems.ElementCount > 0 
									? inventoryDocument.InventoryItems.Select(i => int.TryParse(i.Name, out int parsed) ? parsed : 0).Max() + 1 
									: 1;

								BoltInventoryItem returnedCharm = new BoltInventoryItem
								{
									itemDefinitionId = charmDefId,
									quantity = 1,
									flags = 0,
									date = BsonDateTime.Create(DateTime.UtcNow)
								};

								await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), returnedCharm, nextId);
							}
							catch (System.Exception ex)
							{
								Logger.Error($"[Inventory] Failed to return charm during detachment: {ex.Message}");
							}
						}
						
						appliedItem.Properties.Remove(property);
					}

					await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), appliedItem, itemId.Value);
					
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new ToByteMethod(typeof(PlayerInventoryItem)).ToBytes(appliedItem.ToPlayerInventoryItem(itemId.Value)) } });
				}
				catch (System.Exception ex)
				{
					Logger.Error($"[Inventory] Error removing item property: {ex.Message}");
					SendError(guid, 500);
				}
				return;
			}
			_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
		}

		protected async void activateCoupon(BinaryValue[] value, string guid)
        {
			try
			{
				string couponId = ReadCouponId(value);
				if (string.IsNullOrEmpty(couponId)) throw new StandRiseServer.RpcServer.Helpers.ActiveCouponNotFoundRpcException();

				Logger.Debug($"[Coupon] activateCoupon requested coupon={couponId} player={PlayerId}");
				var response = await BoltGameDatabaseProvider.Instance.ActivateCoupon(couponId, PlayerId);

				_user.SendResponce(new ResponseMessage
				{
					RpcResponse = new RpcResponse
					{
						Id = guid,
						Return = new ToByteMethod(typeof(ActivateCouponResponse)).ToBytes(response)
					}
				});
			}
			catch (StandRiseServer.RpcServer.Helpers.CouponHasAlreadyActivatedRpcException)
			{
				Logger.Debug($"[Coupon] activateCoupon already activated player={PlayerId}");
				SendError(guid, 3317); // Coupon already activated
			}
			catch (StandRiseServer.RpcServer.Helpers.ActiveCouponNotFoundRpcException)
			{
				Logger.Debug($"[Coupon] activateCoupon not found or expired player={PlayerId}");
				SendError(guid, 3316); // Coupon not found or expired
			}
			catch (System.Exception ex)
			{
				Logger.Error($"[Coupon] Error activating coupon: {ex.Message}\n{ex.StackTrace}");
				SendError(guid, 500);
			}
        }

		protected async void activateCouponEncrypted(BinaryValue[] value, string guid)
		{
			try
			{
				if (value == null || value.Length == 0 || value[0] == null || value[0].IsNull)
				{
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 400 } } });
					return;
				}

				byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
				byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
				byte[] encryptedData = value.Length > 1 && value[1] != null && !value[1].IsNull ? value[1].One.ToByteArray() : value[0].One.ToByteArray();
				byte[] decryptedBytes = Utils.DecryptByte(encryptedData, key, IV);
				
				string couponId = null;
				try
				{
					var parsedRequest = ActivateCouponRequest.Parser.ParseFrom(decryptedBytes);
					if (parsedRequest != null && !string.IsNullOrWhiteSpace(parsedRequest.CouponId))
					{
						couponId = parsedRequest.CouponId.Trim();
					}
				}
				catch { }
				
				if (string.IsNullOrEmpty(couponId))
				{
					byte[] extractedBytes = Utils.ExtractBinaryValueOne(decryptedBytes);
					BinaryValue decryptedVal = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(extractedBytes) };
					couponId = ReadCouponId(new BinaryValue[] { decryptedVal });
				}
				
				if (string.IsNullOrEmpty(couponId)) throw new StandRiseServer.RpcServer.Helpers.ActiveCouponNotFoundRpcException();

				Logger.Debug($"[Coupon] activateCouponEncrypted requested coupon={couponId} player={PlayerId}");
				var response = await BoltGameDatabaseProvider.Instance.ActivateCoupon(couponId, PlayerId);

				SendEncryptedResponse(guid, new ToByteMethod(typeof(ActivateCouponResponse)).ToBytes(response));
			}
			catch (StandRiseServer.RpcServer.Helpers.CouponHasAlreadyActivatedRpcException)
			{
				Logger.Debug($"[Coupon] activateCouponEncrypted already activated player={PlayerId}");
				SendError(guid, 3317); // Coupon already activated
			}
			catch (StandRiseServer.RpcServer.Helpers.ActiveCouponNotFoundRpcException)
			{
				Logger.Debug($"[Coupon] activateCouponEncrypted not found or expired player={PlayerId}");
				SendError(guid, 3316); // Coupon not found or expired
			}
			catch (System.Exception ex)
			{
				Logger.Error($"[Coupon] Error activating coupon encrypted: {ex.Message}\n{ex.StackTrace}");
				SendError(guid, 500);
			}
		}

		private static string ReadCouponId(BinaryValue[] value)
		{
			if (value == null || value.Length == 0)
			{
				return string.Empty;
			}

			try
			{
				var request = (ActivateCouponRequest)StandRiseServer.RpcServer.Core.RpcRequestExtension.GetValue<ActivateCouponRequest>(value, 0);
				if (!string.IsNullOrWhiteSpace(request?.CouponId))
				{
					return request.CouponId.Trim();
				}
			}
			catch
			{
			}

			try
			{
				return ((string)new FromByteMethod(typeof(string)).FromBytes(value[0]))?.Trim() ?? string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		}
		
		protected void GetPlayerCoupons(string guid)
		{
			try
			{
				var response = BoltGameDatabaseProvider.Instance.GetPlayerCoupons(PlayerId).Result;
				SendResponse(guid, response);
			}
			catch (System.Exception ex)
			{
				Logger.Error($"[Coupon] Error getting player coupons: {ex.Message}");
				SendError(guid, 500);
			}
		}

		protected async void MountInventoryItem(BinaryValue[] values, string guid)
		{
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				try
				{
					var request = (MountInventoryItemRequest)StandRiseServer.RpcServer.Core.RpcRequestExtension.GetValue<MountInventoryItemRequest>(values, 0);
					Console.WriteLine($"[Inventory] MountInventoryItem called: ConsumedItemId={request.ConsumedItemId}, ModifiedItemId={request.ModifiedItemId}, ModificationName='{request.ModificationName}'");
					BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
					PlayerInventoryDocument inventoryDocument = await Task.Run(() => boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id)));

					var consumedBson = inventoryDocument.InventoryItems.FirstOrDefault(i => i.Name == request.ConsumedItemId.ToString());
					var modifiedBson = inventoryDocument.InventoryItems.FirstOrDefault(i => i.Name == request.ModifiedItemId.ToString());

					if (consumedBson.Value == null || modifiedBson.Value == null)
					{
						SendError(guid, 404);
						return;
					}

					BoltInventoryItem consumedItem = consumedBson.ToBoltInventory();
					BoltInventoryItem modifiedItem = modifiedBson.ToBoltInventory();

					// Check if item already has this modification mounted — if so, unmount the old one first
					PlayerInventoryItem unmountedProto = null;
					var existingProp = modifiedItem.Properties.FirstOrDefault(p => p.Name == request.ModificationName);
					if (existingProp != null)
					{
						// Bug 23: only charms are reusable — stickers (sticker_0..sticker_4 etc)
						// are CONSUMED when overwritten on a skin and must NOT be re-added to
						// the player's inventory. Previously the old sticker would reappear in
						// the inventory after a "replace sticker" operation. Same fix is applied
						// in UnmountInventoryItem and RemoveInventoryItemProperty.
						bool isReusableMount = IsReusableMountProperty(request.ModificationName);
						if (isReusableMount)
						{
							int oldCharmDefId = int.Parse(existingProp.Value);
							int unmountNextId = inventoryDocument.InventoryItems.ElementCount > 0
								? inventoryDocument.InventoryItems.Select(i => int.TryParse(i.Name, out int parsed) ? parsed : 0).Max() + 1
								: 1;

							BoltInventoryItem returnedCharm = new BoltInventoryItem
							{
								itemDefinitionId = oldCharmDefId,
								quantity = 1,
								flags = 0,
								date = BsonDateTime.Create(DateTime.UtcNow)
							};

							await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), returnedCharm, unmountNextId);
							unmountedProto = returnedCharm.ToPlayerInventoryItem(unmountNextId);
						}

						modifiedItem.Properties.Remove(existingProp);
					}

					// Mount the consumed item as a property
					modifiedItem.Properties.Add(new BoltInventoryItemProperty
					{
						Name = request.ModificationName,
						Type = BoltInventoryItemProperty.PropertyType.Int,
						Value = consumedItem.itemDefinitionId.ToString()
					});

					// Remove consumed item and update modified item
					await boltGameDatabaseProvider.RemoveItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), request.ConsumedItemId);
					await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), modifiedItem, request.ModifiedItemId);

					var response = new MountInventoryItemResponse
					{
						ModifiedItem = modifiedItem.ToPlayerInventoryItem(request.ModifiedItemId),
						UnmountedItem = unmountedProto
					};
					SendResponse(guid, response);
				}
				catch (System.Exception ex)
				{
					Logger.Error($"[Inventory] Error mounting inventory item: {ex.Message}");
					SendError(guid, 500);
				}
				return;
			}
			SendError(guid, 401);
		}

		protected async void MountInventoryItemEncrypted(BinaryValue[] value, string guid)
		{
			try
			{
				if (value == null || value.Length == 0 || value[0] == null || value[0].IsNull)
				{
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 400 } } });
					return;
				}

				byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
				byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
				byte[] decryptedBytes = Utils.DecryptByte(value[0].One.ToByteArray(), key, IV);
  				
  				if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
  				{
                    var request = new Axlebolt.Bolt.Protobuf.MountInventoryItemRequest();
                    try {
                        request.MergeFrom(decryptedBytes);
                    } catch (System.Exception ex) {
                        Console.WriteLine($"[MountInventoryItemEncrypted] Direct parse failed, trying extracted: {ex.Message}");
                        byte[] extractedBytes = Utils.ExtractBinaryValueOne(decryptedBytes);
                        request.MergeFrom(extractedBytes);
                    }
					Console.WriteLine($"[Inventory] MountInventoryItemEncrypted called: ConsumedItemId={request.ConsumedItemId}, ModifiedItemId={request.ModifiedItemId}, ModificationName='{request.ModificationName}'");
					BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
					PlayerInventoryDocument inventoryDocument = await Task.Run(() => boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id)));

					var consumedBson = inventoryDocument.InventoryItems.FirstOrDefault(i => i.Name == request.ConsumedItemId.ToString());
					var modifiedBson = inventoryDocument.InventoryItems.FirstOrDefault(i => i.Name == request.ModifiedItemId.ToString());

					if (consumedBson.Value == null || modifiedBson.Value == null)
					{
						SendError(guid, 404);
						return;
					}

					BoltInventoryItem consumedItem = consumedBson.ToBoltInventory();
					BoltInventoryItem modifiedItem = modifiedBson.ToBoltInventory();

					PlayerInventoryItem unmountedProto = null;
					var existingProp = modifiedItem.Properties.FirstOrDefault(p => p.Name == request.ModificationName);
					if (existingProp != null)
					{
						bool isReusableMount = IsReusableMountProperty(request.ModificationName);
						if (isReusableMount)
						{
							int oldCharmDefId = int.Parse(existingProp.Value);
							int unmountNextId = inventoryDocument.InventoryItems.ElementCount > 0
								? inventoryDocument.InventoryItems.Select(i => int.TryParse(i.Name, out int parsed) ? parsed : 0).Max() + 1
								: 1;

							BoltInventoryItem returnedCharm = new BoltInventoryItem
							{
								itemDefinitionId = oldCharmDefId,
								quantity = 1,
								flags = 0,
								date = BsonDateTime.Create(DateTime.UtcNow)
							};

							await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), returnedCharm, unmountNextId);
							unmountedProto = returnedCharm.ToPlayerInventoryItem(unmountNextId);
						}

						modifiedItem.Properties.Remove(existingProp);
					}

					modifiedItem.Properties.Add(new BoltInventoryItemProperty
					{
						Name = request.ModificationName,
						Type = BoltInventoryItemProperty.PropertyType.Int,
						Value = consumedItem.itemDefinitionId.ToString()
					});

					await boltGameDatabaseProvider.RemoveItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), request.ConsumedItemId);
					await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), modifiedItem, request.ModifiedItemId);

					var response = new MountInventoryItemResponse
					{
						ModifiedItem = modifiedItem.ToPlayerInventoryItem(request.ModifiedItemId),
						UnmountedItem = unmountedProto
					};
					SendEncryptedResponse(guid, new ToByteMethod(typeof(MountInventoryItemResponse)).ToBytes(response));
				}
				else
				{
					SendError(guid, 401);
				}
			}
			catch (System.Exception ex)
			{
				Logger.Error($"[Inventory] Error mounting inventory item encrypted: {ex.Message}\n{ex.StackTrace}");
				SendError(guid, 500);
			}
		}

		protected async void UnmountInventoryItem(BinaryValue[] values, string guid)
		{
			if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				try
				{
					var request = (UnmountInventoryItemRequest)StandRiseServer.RpcServer.Core.RpcRequestExtension.GetValue<UnmountInventoryItemRequest>(values, 0);
					BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
					PlayerInventoryDocument inventoryDocument = await Task.Run(() => boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id)));

					var modifiedBson = inventoryDocument.InventoryItems.FirstOrDefault(i => i.Name == request.ModifiedItemId.ToString());
					if (modifiedBson.Value == null)
					{
						SendError(guid, 404);
						return;
					}

					BoltInventoryItem modifiedItem = modifiedBson.ToBoltInventory();
					var property = modifiedItem.Properties.FirstOrDefault(p => p.Name == request.ModificationName);

					PlayerInventoryItem unmountedProto = null;
					if (property != null)
					{
						// Bug 23: stickers must not be returned to inventory on unmount.
						// Only charms (and other reusable mounts) are refunded.
						if (IsReusableMountProperty(request.ModificationName))
						{
							int charmDefId = int.Parse(property.Value);
							int nextId = inventoryDocument.InventoryItems.ElementCount > 0
								? inventoryDocument.InventoryItems.Select(i => int.TryParse(i.Name, out int parsed) ? parsed : 0).Max() + 1
								: 1;

							BoltInventoryItem returnedCharm = new BoltInventoryItem
							{
								itemDefinitionId = charmDefId,
								quantity = 1,
								flags = 0,
								date = BsonDateTime.Create(DateTime.UtcNow)
							};

							await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), returnedCharm, nextId);
							unmountedProto = returnedCharm.ToPlayerInventoryItem(nextId);
						}

						modifiedItem.Properties.Remove(property);
					}

					// Update the modified item without the property
					await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), modifiedItem, request.ModifiedItemId);

					var response = new UnmountInventoryItemResponse
					{
						UnmountedItem = unmountedProto
					};
					SendResponse(guid, response);
				}
				catch (System.Exception ex)
				{
					Logger.Error($"[Inventory] Error unmounting inventory item: {ex.Message}");
					SendError(guid, 500);
				}
				return;
			}
			SendError(guid, 401);
		}

		protected async void UnmountInventoryItemEncrypted(BinaryValue[] value, string guid)
		{
			try
			{
				if (value == null || value.Length == 0 || value[0] == null || value[0].IsNull)
				{
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 400 } } });
					return;
				}

				byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
				byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
				byte[] decryptedBytes = Utils.DecryptByte(value[0].One.ToByteArray(), key, IV);
  				
  				if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
  				{
                    var request = new Axlebolt.Bolt.Protobuf.UnmountInventoryItemRequest();
                    try {
                        request.MergeFrom(decryptedBytes);
                    } catch (System.Exception ex) {
                        Console.WriteLine($"[UnmountInventoryItemEncrypted] Direct parse failed, trying extracted: {ex.Message}");
                        byte[] extractedBytes = Utils.ExtractBinaryValueOne(decryptedBytes);
                        request.MergeFrom(extractedBytes);
                    }
					BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
					PlayerInventoryDocument inventoryDocument = await Task.Run(() => boltGameDatabaseProvider.GetPlayerInventoryDocument(ObjectId.Parse(Id)));

					var modifiedBson = inventoryDocument.InventoryItems.FirstOrDefault(i => i.Name == request.ModifiedItemId.ToString());
					if (modifiedBson.Value == null)
					{
						SendError(guid, 404);
						return;
					}

					BoltInventoryItem modifiedItem = modifiedBson.ToBoltInventory();
					var property = modifiedItem.Properties.FirstOrDefault(p => p.Name == request.ModificationName);

					PlayerInventoryItem unmountedProto = null;
					if (property != null)
					{
						if (IsReusableMountProperty(request.ModificationName))
						{
							int charmDefId = int.Parse(property.Value);
							int nextId = inventoryDocument.InventoryItems.ElementCount > 0
								? inventoryDocument.InventoryItems.Select(i => int.TryParse(i.Name, out int parsed) ? parsed : 0).Max() + 1
								: 1;

							BoltInventoryItem returnedCharm = new BoltInventoryItem
							{
								itemDefinitionId = charmDefId,
								quantity = 1,
								flags = 0,
								date = BsonDateTime.Create(DateTime.UtcNow)
							};

							await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), returnedCharm, nextId);
							unmountedProto = returnedCharm.ToPlayerInventoryItem(nextId);
						}

						modifiedItem.Properties.Remove(property);
					}

					await boltGameDatabaseProvider.AddItemToPlayerInventoryDocumentAsync(ObjectId.Parse(Id), modifiedItem, request.ModifiedItemId);

					var response = new UnmountInventoryItemResponse
					{
						UnmountedItem = unmountedProto
					};
					SendEncryptedResponse(guid, new ToByteMethod(typeof(UnmountInventoryItemResponse)).ToBytes(response));
				}
				else
				{
					SendError(guid, 401);
				}
			}
			catch (System.Exception ex)
			{
				Logger.Error($"[Inventory] Error unmounting inventory item encrypted: {ex.Message}\n{ex.StackTrace}");
				SendError(guid, 500);
			}
		}

		// Bug 23: returns true when a mounted property is "reusable" — i.e. removing it
		// (peel/unmount) or overwriting it (replace) should give the item BACK to the
		// player's inventory. Charms are reusable; stickers are not. By default we treat
		// anything we don't explicitly know as a sticker as reusable so charm and keychain
		// variants keep refunding the way they did before.
		private static bool IsReusableMountProperty(string propertyName)
		{
			if (string.IsNullOrEmpty(propertyName))
			{
				return false;
			}

			if (propertyName.StartsWith("sticker", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			return true;
		}

		private static bool IsPostMatchDropRecipe(string recipeCode)
		{
			if (string.IsNullOrEmpty(recipeCode)) return false;
			return recipeCode.Equals("RECIPE_DROP_IN_GAME", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_DROP_IN_GAME_PRO", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_DROP_IN_GAME_RANKED", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_DROP_IN_GAME_PRO_RANKED", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_DROP_ON_LVL", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_DROP_ON_BONUS", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_GOOD_GAME_1", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_GOOD_GAME_2", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_GOOD_GAME_3", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_GOOD_GAME_1_RANKED", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_GOOD_GAME_2_RANKED", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_GOOD_GAME_3_RANKED", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsSilverForGoldExchangeRecipe(string recipeCode)
		{
			if (string.IsNullOrWhiteSpace(recipeCode))
			{
				return false;
			}

			if (recipeCode.Equals("RECIPE_BUY_SILVER_FOR_GOLD", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("BUY_SILVER_FOR_GOLD", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_EXCHANGE_GOLD_TO_SILVER", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("EXCHANGE_GOLD_TO_SILVER", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_SILVER_PURCHASE", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("SILVER_PURCHASE", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("BUY_CURRENCY_101", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_BUY_CURRENCY_101", StringComparison.OrdinalIgnoreCase)
				|| recipeCode.Equals("RECIPE_101", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			// Keep compatibility with renamed client recipe ids from different dumps.
			string upper = recipeCode.ToUpperInvariant();
			return upper.Contains("CURRENCY_101")
				|| (upper.Contains("SILVER") && upper.Contains("GOLD"));
		}

		private void ProcessSilverForGoldExchange(string playerId, string recipeCode, BoltGameDatabaseProvider db, string requestId, int exchangeCount)
		{
			int safeExchangeCount = exchangeCount <= 0 ? 1 : exchangeCount;
			if (safeExchangeCount > 100)
			{
				safeExchangeCount = 100;
			}

			int totalGoldCost = 10 * safeExchangeCount;
			int totalSilverReward = 100 * safeExchangeCount;
			if (!db.IsEnoughFunds(playerId, "102", totalGoldCost))
			{
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = requestId, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 101 } } });
				return;
			}

			db.CurrencyMinusValue(ObjectId.Parse(playerId), 102, totalGoldCost);
			db.CurrencyPlusValue(ObjectId.Parse(playerId), 101, totalSilverReward);
			Console.WriteLine($"[Inventory] {recipeCode}: gave {totalSilverReward} silver for {totalGoldCost} gold to player {playerId}");
			var exchangeResult = new ExchangeResult();
			exchangeResult.Currencies.Add(new CurrencyAmount { CurrencyId = 101, Value = totalSilverReward, OldValue = totalSilverReward });
			_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = requestId, Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult) } });
		}

		private static bool IsLuckWeaponDefinition(BoltInventoryItemDefinitionDocument def)
		{
			if (def == null || def.properties == null) return false;
			if (!def.properties.Contains("match_drop")) return false;
			var md = def.properties["match_drop"];
			bool flag = false;
			try
			{
				if (md.IsBoolean) flag = md.AsBoolean;
				else if (md.IsInt32 || md.IsInt64) flag = md.ToInt32() != 0;
				else if (md.IsString) flag = md.AsString == "1" || md.AsString.Equals("true", StringComparison.OrdinalIgnoreCase);
			}
			catch { return false; }
			if (!flag) return false;
			int key = def.key;
			if (key < 1000) return false;
			if (key >= 1200 && key < 2000) return false;
			if (def.properties.Contains("stickerMount")) return false;
			if (def.properties.Contains("maxStackSize")) return false;
			if (def.properties.Contains("contains")) return false;
			var rarity = def.GetSkinValue();
			if (rarity == SkinValue.None || rarity == SkinValue.Arcane) return false;
			return def.GetCollectionId() != CollectionId.None;
		}

		private void ProcessPostMatchDrop(string playerId, string recipeCode, BoltGameDatabaseProvider db, RandomGenerator randomGenerator, string requestId)
		{
			// Защита от дюпа: без этого клиент мог слать exchangeInventoryItems с одним и тем же
			// пост-матч recipeCode сколько угодно раз подряд и бесконечно фармить валюту+скины.
			if (!InventoryDupeProtection.TryClaimPostMatchDrop(playerId, recipeCode))
			{
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = requestId, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 429 } } });
				return;
			}

			Console.WriteLine($"[InventoryRemoteService] post-match drop for player {playerId} ({recipeCode})");
			ObjectId playerOid = ObjectId.Parse(playerId);
			PlayerInventoryDocument inventoryDocument = db.GetPlayerInventoryDocument(playerOid);
			int nextId = (inventoryDocument.InventoryItems.ElementCount > 0)
				? inventoryDocument.InventoryItems.Select(i => int.TryParse(i.Name, out int parsed) ? parsed : 0).Max() + 1
				: 1;

			bool luckWeaponRecipe = !string.IsNullOrEmpty(recipeCode)
				&& recipeCode.IndexOf("DROP_IN_GAME", StringComparison.OrdinalIgnoreCase) >= 0;
			bool goodGameRecipe = !string.IsNullOrEmpty(recipeCode)
				&& recipeCode.IndexOf("GOOD_GAME", StringComparison.OrdinalIgnoreCase) >= 0;

			var dropRng = new Random();
			int goldDrop = dropRng.Next(50, 201);
			int silverDrop = dropRng.Next(50, 301);

			if (!luckWeaponRecipe)
			{
				db.CurrencyPlusValue(playerOid, 102, goldDrop);
				db.CurrencyPlusValue(playerOid, 101, silverDrop);
			}

			if (goodGameRecipe)
			{
				var silverOnly = new ExchangeResult();
				silverOnly.Currencies.Add(new CurrencyAmount { CurrencyId = 102, Value = goldDrop, OldValue = goldDrop });
				silverOnly.Currencies.Add(new CurrencyAmount { CurrencyId = 101, Value = silverDrop, OldValue = silverDrop });
				_user.SendResponce(new ResponseMessage
				{
					RpcResponse = new RpcResponse
					{
						Id = requestId,
						Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(silverOnly)
					}
				});
				return;
			}

			MatchDropConfig.ApplyMatchDropWeights(randomGenerator);

			BoltInventoryItem boltInventoryItem = null;
			bool allowSkinRoll = luckWeaponRecipe || dropRng.Next(100) < 10;
			CollectionId[] dropCollections = System.Enum.GetValues(typeof(CollectionId))
				.Cast<CollectionId>()
				.Where(c => c != CollectionId.None)
				.ToArray();
			if (allowSkinRoll)
			{
			for (int attempt = 0; attempt < 80 && boltInventoryItem == null; attempt++)
			{
				CollectionId collection = dropCollections[dropRng.Next(dropCollections.Length)];
				var candidate = randomGenerator.GetRandomItem(collection);
				if (candidate == null) continue;
				var skinDef = db.GetInventoryItemDefinition(candidate.itemDefinitionId, 1);
				if (skinDef == null || MatchDropConfig.IsExcludedFromMatchDrop(skinDef)) continue;
				if (luckWeaponRecipe)
				{
					if (!MatchDropConfig.IsLuckDropCandidate(skinDef, true)) continue;
				}
				boltInventoryItem = candidate;
			}
			}

			if (allowSkinRoll && boltInventoryItem == null && dropRng.Next(100) < 25)
			{
				var eligibleSkins = db.GetAllItemDefinitionsByType(0)
					.Where(d => !MatchDropConfig.IsExcludedFromMatchDrop(d)
						&& MatchDropConfig.IsLuckDropCandidate(d, luckWeaponRecipe))
					.ToList();
				if (eligibleSkins.Count == 0 && !luckWeaponRecipe)
				{
					eligibleSkins = db.GetAllItemDefinitionsByType(0)
						.Where(d => d != null && !MatchDropConfig.IsExcludedFromMatchDrop(d)
							&& d.key >= 11000 && d.properties != null
							&& d.GetSkinValue() != SkinValue.None
							&& d.GetCollectionId() != CollectionId.None
							&& !d.properties.Contains("stickerMount")
							&& !d.properties.Contains("contains")
							&& !d.properties.Contains("maxStackSize"))
						.ToList();
				}
				if (eligibleSkins.Count > 0)
				{
					var picked = eligibleSkins[dropRng.Next(eligibleSkins.Count)];
					boltInventoryItem = new BoltInventoryItem
					{
						itemDefinitionId = picked.key,
						date = DateTime.Now,
						flags = 0,
						quantity = 1
					};
				}
				else
				{
					boltInventoryItem = new BoltInventoryItem
					{
						itemDefinitionId = 11001,
						date = DateTime.Now,
						flags = 0,
						quantity = 1
					};
				}
			}

			var exchangeResult = new ExchangeResult();
			if (!luckWeaponRecipe)
			{
				exchangeResult.Currencies.Add(new CurrencyAmount { CurrencyId = 102, Value = goldDrop, OldValue = goldDrop });
				exchangeResult.Currencies.Add(new CurrencyAmount { CurrencyId = 101, Value = silverDrop, OldValue = silverDrop });
			}

			if (boltInventoryItem != null)
			{
				var invItem = boltInventoryItem.ToInventory(nextId);
				invItem.Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				exchangeResult.InventoryItems.Add(invItem);
				db.AddItemToPlayerInventoryDocument(playerOid, boltInventoryItem, nextId);
				Console.WriteLine($"[Inventory] {recipeCode}: luck item defId={boltInventoryItem.itemDefinitionId} slot={nextId} player={playerId} items={exchangeResult.InventoryItems.Count}");
			}
			else
				Logger.Error($"[Inventory] {recipeCode}: skin drop failed for player {playerId}");

			_user.SendResponce(new ResponseMessage
			{
				RpcResponse = new RpcResponse
				{
					Id = requestId,
					Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult)
				}
			});
		}

		// Разбор recipe-кода крафта: CRAFT_{RARITY}_{COLLECTION}[_STATTRACK]
		private static bool TryParseCraftRecipe(string recipeCode, out SkinValue inputRarity, out CollectionId collection, out bool wantsStattrack)
		{
			inputRarity = SkinValue.None;
			collection = CollectionId.None;
			wantsStattrack = false;

			if (string.IsNullOrWhiteSpace(recipeCode) || !recipeCode.StartsWith("CRAFT_", StringComparison.OrdinalIgnoreCase))
				return false;

			var parts = recipeCode.Split('_');
			if (parts.Length < 3)
				return false;

			if (!System.Enum.TryParse(parts[1], true, out inputRarity))
				return false;

			int collectionEnd = parts.Length;
			if (parts[parts.Length - 1].Equals("STATTRACK", StringComparison.OrdinalIgnoreCase))
			{
				wantsStattrack = true;
				collectionEnd--;
			}

			if (collectionEnd <= 2)
				return false;

			string collectionRaw = string.Join("_", parts.Skip(2).Take(collectionEnd - 2));
			return TryParseCollectionFlexible(collectionRaw, out collection);
		}

		// Гибкий разбор имени коллекции из recipe-кода крафта: точное имя, затем
		// сопоставление без учёта регистра/подчёркиваний/пробелов (клиентский enum может
		// называться иначе, например Hot_Winter_Party_2023 vs hot_winter_party_2023).
		private static bool TryParseCollectionFlexible(string raw, out CollectionId collection)
		{
			collection = CollectionId.None;
			if (string.IsNullOrWhiteSpace(raw)) return false;
			if (System.Enum.TryParse<CollectionId>(raw, true, out collection)) return true;

			string Normalize(string s)
			{
				var sb = new System.Text.StringBuilder();
				foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
				return sb.ToString();
			}
			string target = Normalize(raw);
			foreach (CollectionId value in System.Enum.GetValues(typeof(CollectionId)))
			{
				if (Normalize(value.ToString()) == target)
				{
					collection = value;
					return true;
				}
			}
			return false;
		}

		protected void getRecipeStatus(BinaryValue[] values, string guid)
		{
			string recipeCode = "";
			if (values != null && values.Length > 0 && !values[0].IsNull)
			{
				try
				{
					var statusRequest = (GetRecipeStatusRequest)new FromByteMethod(typeof(GetRecipeStatusRequest)).FromBytes(values[0]);
					recipeCode = statusRequest?.RecipeCode ?? "";
				}
				catch (System.Exception ex)
				{
					Logger.Error($"[Inventory] getRecipeStatus parse error: {ex.Message}");
				}
			}

			// Всегда 0: раньше сюда попадало currency 200001 (счётчик спинов) → клиент «ошибка запроса».
			var statusResponse = new GetRecipeStatusResponse
			{
				ExecutionIntervalOk = true,
				ExecutionTimingOk = true,
				TimesExecutedTotal = 0
			};
			Console.WriteLine($"[Inventory] getRecipeStatus '{recipeCode}' -> ok");
			_user.SendResponce(new ResponseMessage
			{
				RpcResponse = new RpcResponse
				{
					Id = guid,
					Return = new ToByteMethod(typeof(GetRecipeStatusResponse)).ToBytes(statusResponse)
				}
			});
		}

		private static double GetLocalCurrencyBalance(BsonDocument currencies, string currencyId)
		{
			if (currencies == null || string.IsNullOrEmpty(currencyId) || !currencies.TryGetValue(currencyId, out var value))
			{
				return 0;
			}
			if (value == null || value.IsBsonNull)
			{
				return 0;
			}
			if (value.IsDouble) return value.AsDouble;
			if (value.IsInt32) return value.AsInt32;
			if (value.IsInt64) return value.AsInt64;
			try { return value.ToDouble(); } catch { return 0; }
		}

		private void GetCurrencyBalance(string guid)
		{
			try
			{
				if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
				{
					SendError(guid, 401);
					return;
				}

				var db = BoltGameDatabaseProvider.Instance;
				var inventory = db.GetPlayerInventoryDocument(ObjectId.Parse(Id));

				double silver = GetLocalCurrencyBalance(inventory?.Currencies, "101");
				double gold = GetLocalCurrencyBalance(inventory?.Currencies, "102");

				using (var stream = new System.IO.MemoryStream())
				{
					using (var output = new Google.Protobuf.CodedOutputStream(stream))
					{
						var silverProto = new CurrencyAmount { CurrencyId = 101, Value = (int)silver };
						output.WriteRawTag(10);
						output.WriteMessage(silverProto);

						var goldProto = new CurrencyAmount { CurrencyId = 102, Value = (int)gold };
						output.WriteRawTag(10);
						output.WriteMessage(goldProto);

						output.Flush();
						byte[] responseBytes = stream.ToArray();

						_user.SendResponce(new ResponseMessage
						{
							RpcResponse = new RpcResponse
							{
								Id = guid,
								Return = new BinaryValue
								{
									IsNull = false,
									One = Google.Protobuf.ByteString.CopyFrom(responseBytes)
								}
							}
						});
					}
				}
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"[Inventory] GetCurrencyBalance error: {ex.Message}");
				SendError(guid, 500);
			}
		}

		private void SellInventoryItem(BinaryValue[] values, string guid)
		{
			try
			{
				if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
				{
					SendError(guid, 401);
					return;
				}

				int currencyId = 101;
				int reward = 35;
				if (values != null && values.Length > 1)
				{
					try { currencyId = (int)new FromByteMethod(typeof(int)).FromBytes(values[1]); } catch {}
				}
				if (values != null && values.Length > 2)
				{
					try { reward = (int)new FromByteMethod(typeof(int)).FromBytes(values[2]); } catch {}
				}

				var db = BoltGameDatabaseProvider.Instance;
				var playerOid = ObjectId.Parse(Id);
				db.CurrencyPlusValue(playerOid, currencyId, reward);

				var exchangeResult = new ExchangeResult();
				exchangeResult.Currencies.Add(new CurrencyAmount { CurrencyId = currencyId, Value = reward, OldValue = reward });
				
				_user.SendResponce(new ResponseMessage
				{
					RpcResponse = new RpcResponse
					{
						Id = guid,
						Return = new ToByteMethod(typeof(ExchangeResult)).ToBytes(exchangeResult)
					}
				});
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"[Inventory] SellInventoryItem error: {ex.Message}");
				SendError(guid, 500);
			}
		}

		private void SendEncryptedResponse(string id, BinaryValue returnValue)
		{
			if (returnValue != null)
			{
				returnValue.Array.Clear();
				if (!returnValue.IsNull && returnValue.One != null)
				{
					byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
					byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
					byte[] encrypted = Utils.EncryptByte(returnValue.One.ToByteArray(), key, IV);
					returnValue.One = ByteString.CopyFrom(encrypted);
				}
			}
			_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = id, Return = returnValue } });
		}

		protected void SetInventoryItemFlagsEncrypted(RpcRequest request)
		{
			if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
				return;
			}

			try
			{
				if (request.Params == null || request.Params.Count == 0 || request.Params[0] == null || request.Params[0].IsNull)
				{
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 400 } } });
					return;
				}

				byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
				byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
				
				Logger.Log($"[SetItemFlagsEncrypted] Using key: {(_user.SessionAesKey != null ? "SessionKey" : "DEFAULT")}, IV: {(_user.SessionAesIV != null ? "SessionIV" : "DEFAULT")}");
				Logger.Log($"[SetItemFlagsEncrypted] Encrypted bytes length: {request.Params[0].One.Length}");
				
				byte[] decryptedBytes = Utils.DecryptByte(request.Params[0].One.ToByteArray(), key, IV);
				
				Logger.Log($"[SetItemFlagsEncrypted] Decrypted bytes length: {decryptedBytes.Length}, hex: {BitConverter.ToString(decryptedBytes, 0, Math.Min(decryptedBytes.Length, 64))}");

				byte[] extractedBytes;
				if (request.MethodName.Equals("setInventoryItemFlagsEncrypted2", StringComparison.OrdinalIgnoreCase))
				{
					extractedBytes = decryptedBytes; // Directly parse BMMPPIAHAJG
					extractedBytes = Utils.ExtractBinaryValueOne(extractedBytes); // Unwrap EGIFMPPBLAE from BMMPPIAHAJG
				}
				else
				{
					extractedBytes = Utils.ExtractBinaryValueOne(decryptedBytes);
				}

				BinaryValue decryptedVal = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(extractedBytes) };
				ItemFlags itemFlags = (ItemFlags)new FromByteMethod(typeof(ItemFlags)).FromBytes(decryptedVal);

				Logger.Log($"[SetItemFlagsEncrypted] Parsed ItemFlags count: {itemFlags.Flags.Count}");
				foreach (var kv in itemFlags.Flags)
				{
					Logger.Log($"[SetItemFlagsEncrypted]   Flag entry: ItemId={kv.Key}, Flag={kv.Value}");
				}

				BoltGameDatabaseProvider boltGameDatabaseProvider = BoltGameDatabaseProvider.Instance;
				foreach (KeyValuePair<int, int> keyValuePair in itemFlags.Flags)
				{
					boltGameDatabaseProvider.SetItemFlag(ObjectId.Parse(Id), keyValuePair.Key, keyValuePair.Value);
				}

				SendEncryptedResponse(request.Id, new BinaryValue { IsNull = true });
			}
			catch (System.Exception ex)
			{
				Logger.Error($"[Inventory] SetInventoryItemFlagsEncrypted error: {ex.Message}\n{ex.StackTrace}");
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } });
			}
		}

		private class ParsedExecuteRecipeRequest
		{
			public string RecipeCode = "";
			public List<int> ItemIds = new List<int>();
			public List<int> CraftItems = new List<int>();
		}

		private static ParsedExecuteRecipeRequest ParseExecuteRecipeRequest(byte[] data)
		{
			var req = new ParsedExecuteRecipeRequest();
			var input = new CodedInputStream(data);
			uint tag;
			while ((tag = input.ReadTag()) != 0)
			{
				int fieldNumber = (int)(tag >> 3);
				int wireType = (int)(tag & 7);
				
				if (fieldNumber == 1 && wireType == 2)
				{
					req.RecipeCode = input.ReadString();
				}
				else if (fieldNumber == 2 && wireType == 2)
				{
					byte[] nestedBytes = input.ReadBytes().ToByteArray();
					var nestedInput = new CodedInputStream(nestedBytes);
					int itemId = 0;
					while (!nestedInput.IsAtEnd)
					{
						uint nestedTag = nestedInput.ReadTag();
						if (nestedTag == 0) break;
						int nestedField = (int)(nestedTag >> 3);
						int nestedType = (int)(nestedTag & 7);
						if (nestedField == 1 && nestedType == 0)
						{
							itemId = nestedInput.ReadInt32();
						}
						else
						{
							nestedInput.SkipLastField();
						}
					}
					if (itemId > 0)
					{
						req.ItemIds.Add(itemId);
					}
				}
				else if (fieldNumber == 3)
				{
					if (wireType == 0)
					{
						req.CraftItems.Add(input.ReadInt32());
					}
					else if (wireType == 2)
					{
						byte[] packedBytes = input.ReadBytes().ToByteArray();
						var packedInput = new CodedInputStream(packedBytes);
						while (!packedInput.IsAtEnd)
						{
							req.CraftItems.Add(packedInput.ReadInt32());
						}
					}
					else
					{
						input.SkipLastField();
					}
				}
				else
				{
					input.SkipLastField();
				}
			}
			return req;
		}

		private static byte[] WrapExecuteRecipeResponse(byte[] exchangeResultBytes)
		{
			var stream = new System.IO.MemoryStream();
			var output = new CodedOutputStream(stream);
			output.WriteRawTag(10); // tag 1, wire type 2
			output.WriteBytes(ByteString.CopyFrom(exchangeResultBytes));
			output.Flush();
			return stream.ToArray();
		}

		protected void ExecuteRecipeEncrypted(RpcRequest request)
		{
			if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
			{
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
				return;
			}

			try
			{
				if (request.Params == null || request.Params.Count == 0 || request.Params[0] == null || request.Params[0].IsNull)
				{
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 400 } } });
					return;
				}

				byte[] key = _user.SessionAesKey ?? System.Text.Encoding.ASCII.GetBytes("key_abcdefghijkl");
				byte[] IV = _user.SessionAesIV ?? System.Text.Encoding.ASCII.GetBytes("iv_abcdefghijklm");
				byte[] decryptedBytes = Utils.DecryptByte(request.Params[0].One.ToByteArray(), key, IV);
				
				byte[] extractedBytes;
				if (request.MethodName.Equals("executeRecipeEncrypted2", StringComparison.OrdinalIgnoreCase))
				{
					extractedBytes = decryptedBytes;
				}
				else
				{
					extractedBytes = Utils.ExtractBinaryValueOne(decryptedBytes);
				}

				ParsedExecuteRecipeRequest parsedReq = ParseExecuteRecipeRequest(extractedBytes);

				RpcRequest dummyRequest = new RpcRequest
				{
					Id = request.Id,
					MethodName = "exchangeInventoryItems"
				};
				dummyRequest.Params.Add(new ToByteMethod(typeof(string)).ToBytes(parsedReq.RecipeCode));
				dummyRequest.Params.Add(new ToByteMethod(typeof(int[])).ToBytes(parsedReq.ItemIds.ToArray()));
				dummyRequest.Params.Add(new ToByteMethod(typeof(int[])).ToBytes(parsedReq.CraftItems.ToArray()));

				ResponseMessage capturedResponse = null;
				Action<ResponseMessage> oldInterceptor = _user.ResponseInterceptor;
				_user.ResponseInterceptor = (resp) => {
					capturedResponse = resp;
				};

				try
				{
					ExchangeInventoryItems(dummyRequest);
				}
				finally
				{
					_user.ResponseInterceptor = oldInterceptor;
				}

				if (capturedResponse == null)
				{
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } });
					return;
				}

				if (capturedResponse.RpcResponse.Exception != null)
				{
					_user.SendResponce(capturedResponse);
					return;
				}

				if (capturedResponse.RpcResponse.Return != null && !capturedResponse.RpcResponse.Return.IsNull)
				{
					// Тот же ExchangeResult, что и plain executeRecipe (без ExecuteRecipeResponse-обёртки).
					byte[] exchangeResultBytes = capturedResponse.RpcResponse.Return.One.ToByteArray();
					BinaryValue encryptedReturn = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(exchangeResultBytes) };
					SendEncryptedResponse(request.Id, encryptedReturn);
				}
				else
				{
					SendEncryptedResponse(request.Id, new BinaryValue { IsNull = true });
				}
			}
			catch (System.Exception ex)
			{
				Logger.Error($"[Inventory] ExecuteRecipeEncrypted error: {ex.Message}\n{ex.StackTrace}");
				_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } });
			}
		}

		public override void Invoke(RpcRequest request)
		{
			try
			{
				string methodName = request.MethodName;

				// Диагностика: логируем КАЖДЫЙ входящий Inventory-RPC безусловно (не через
				// Logger.Debug/LogDebug, который может быть выключен и скрывать реальную картину).
				// Нужно однозначно проверить: доходит ли до сервера ВООБЩЕ хоть какой-то запрос
				// в момент килла стриковым оружием, или клиент вообще ничего не шлёт (тогда
				// проблема не в имени метода, а раньше — в клиентской проверке экипировки/скина).
				Logger.Log($"[Inventory] Invoke: {methodName}");

				if (methodName.Equals("getPlayerInventory", StringComparison.OrdinalIgnoreCase))
				{
					if (request.Params.Count > 0) GetPlayerInventoryResponse(request.Params.ToArray(), request.Id, false);
					else GetPlayerInventory(request.Params.ToArray(), request.Id);
				}
				else if (methodName.Equals("getPlayerInventoryEncrypted", StringComparison.OrdinalIgnoreCase)) GetPlayerInventoryResponse(request.Params.ToArray(), request.Id, true);
				else if (methodName.Equals("getInventoryItemPropertyDefinitions", StringComparison.OrdinalIgnoreCase))
				{
					if (request.Params.Count > 0) GetInventoryItemPropertyDefinitionsResponse(request.Params.ToArray(), request.Id, false);
					else GetInventoryItemPropertyDefinitions(request.Params.ToArray(), request.Id);
				}
				else if (methodName.Equals("getInventoryItemPropertyDefinitions2", StringComparison.OrdinalIgnoreCase)) GetInventoryItemPropertyDefinitionsResponse(request.Params.ToArray(), request.Id, false);
				else if (methodName.Equals("getInventoryItemPropertyDefinitionsEncrypted", StringComparison.OrdinalIgnoreCase)) GetInventoryItemPropertyDefinitionsResponse(request.Params.ToArray(), request.Id, true);
				else if (methodName.Equals("getInventoryItemDefinitions", StringComparison.OrdinalIgnoreCase))
				{
					if (request.Params.Count > 0) GetInventoryItemDefinitionsResponse(request.Params.ToArray(), request.Id, false);
					else GetInventoryItemDefinitions(request.Params.ToArray(), request.Id);
				}
				else if (methodName.Equals("getInventoryItemDefinitions2", StringComparison.OrdinalIgnoreCase)) GetInventoryItemDefinitionsResponse(request.Params.ToArray(), request.Id, false);
				else if (methodName.Equals("getInventoryItemDefinitionsEncrypted", StringComparison.OrdinalIgnoreCase)) GetInventoryItemDefinitionsResponse(request.Params.ToArray(), request.Id, true);
				else if (methodName.Equals("exchangeInventoryItems", StringComparison.OrdinalIgnoreCase)
					|| methodName.Equals("consumeInventoryItems", StringComparison.OrdinalIgnoreCase)
					|| methodName.Equals("executeRecipe", StringComparison.OrdinalIgnoreCase)
					|| methodName.Equals("executeRecipe2", StringComparison.OrdinalIgnoreCase))
				{
					// dump 0.17: executeRecipe и exchangeInventoryItems → один тип ExchangeResult
					// (KHHNFKDMECL). Обёртка ExecuteRecipeResponse ломает парсинг → «ошибка запроса».
					ExchangeInventoryItems(request);
				}
				else if (methodName.Equals("buyInventoryItem", StringComparison.OrdinalIgnoreCase)
					|| methodName.Equals("buyInventoryItems", StringComparison.OrdinalIgnoreCase)
					|| methodName.Equals("buyInventoryItem2", StringComparison.OrdinalIgnoreCase)
					|| methodName.Equals("buyInventoryItemEncrypted", StringComparison.OrdinalIgnoreCase)) BuyInventoryItem(request);
				else if (methodName.Equals("getCurrencyBalance", StringComparison.OrdinalIgnoreCase)) GetCurrencyBalance(request.Id);
				else if (methodName.Equals("sellInventoryItem", StringComparison.OrdinalIgnoreCase)) SellInventoryItem(request.Params.ToArray(), request.Id);
				else if (methodName.Equals("setInventoryItemFlags", StringComparison.OrdinalIgnoreCase) || methodName.Equals("setInventoryItemFlags2", StringComparison.OrdinalIgnoreCase)) SetInventoryItemFlags(request);
				else if (methodName.Equals("setInventoryItemFlagsEncrypted", StringComparison.OrdinalIgnoreCase) || methodName.Equals("setInventoryItemFlagsEncrypted2", StringComparison.OrdinalIgnoreCase)) SetInventoryItemFlagsEncrypted(request);
				else if (methodName.Equals("executeRecipeEncrypted", StringComparison.OrdinalIgnoreCase) || methodName.Equals("executeRecipeEncrypted2", StringComparison.OrdinalIgnoreCase)) ExecuteRecipeEncrypted(request);
				else if (methodName.Equals("setInventoryItemsProperties", StringComparison.OrdinalIgnoreCase)) SetInventoryItemsProperties(request);
  				else if (methodName.Equals("setInventoryItemsPropertiesEncrypted", StringComparison.OrdinalIgnoreCase)) SetInventoryItemsPropertiesEncrypted(request);
				else if (methodName.Equals("activateCoupon", StringComparison.OrdinalIgnoreCase)) activateCoupon(request.Params.ToArray(), request.Id);
				else if (methodName.Equals("activateCouponEncrypted", StringComparison.OrdinalIgnoreCase)) activateCouponEncrypted(request.Params.ToArray(), request.Id);
				else if (methodName.Equals("getPlayerCoupons", StringComparison.OrdinalIgnoreCase)) GetPlayerCoupons(request.Id);
				else if (methodName.Equals("applyInventoryItem", StringComparison.OrdinalIgnoreCase)) ApplyInventoryItem((int?)new FromByteMethod(typeof(int?)).FromBytes(request.Params[0]), (int?)new FromByteMethod(typeof(int?)).FromBytes(request.Params[1]), (string)new FromByteMethod(typeof(string)).FromBytes(request.Params[2]), (bool)new FromByteMethod(typeof(bool)).FromBytes(request.Params[3]), request.Id);
				else if (methodName.Equals("removeInventoryItemProperty", StringComparison.OrdinalIgnoreCase)) RemoveInventoryItemProperty((int?)new FromByteMethod(typeof(int?)).FromBytes(request.Params[0]), (string)new FromByteMethod(typeof(string)).FromBytes(request.Params[1]), request.Id);
				else if (methodName.Equals("setInventoryItemProperty", StringComparison.OrdinalIgnoreCase)) SetInventoryItemProperty((int?)new FromByteMethod(typeof(int?)).FromBytes(request.Params[0]), (string)new FromByteMethod(typeof(string)).FromBytes(request.Params[1]), (int)new FromByteMethod(typeof(int)).FromBytes(request.Params[2]), request.Id);
				else if (methodName.Equals("consumeInventoryItem", StringComparison.OrdinalIgnoreCase)) ConsumeInventoryItem((int?)new FromByteMethod(typeof(int?)).FromBytes(request.Params[0]), (int)new FromByteMethod(typeof(int)).FromBytes(request.Params[1]), request.Id);
				else if (methodName.Equals("getOtherPlayerItems", StringComparison.OrdinalIgnoreCase)) GetPlayerInventory((string)new FromByteMethod(typeof(string)).FromBytes(request.Params[0]), (int[])new FromByteMethod(typeof(int[])).FromBytes(request.Params[1]), request.Id);
								else if (methodName.Equals("getOtherPlayerItemsEncrypted", StringComparison.OrdinalIgnoreCase)) GetOtherPlayerItemsEncrypted(request.Params.ToArray(), request.Id);
				else if (methodName.Equals("mountInventoryItem", StringComparison.OrdinalIgnoreCase)) MountInventoryItem(request.Params.ToArray(), request.Id);
				else if (methodName.Equals("mountInventoryItemEncrypted", StringComparison.OrdinalIgnoreCase)) MountInventoryItemEncrypted(request.Params.ToArray(), request.Id);
				else if (methodName.Equals("unmountInventoryItem", StringComparison.OrdinalIgnoreCase)) UnmountInventoryItem(request.Params.ToArray(), request.Id);
				else if (methodName.Equals("unmountInventoryItemEncrypted", StringComparison.OrdinalIgnoreCase)) UnmountInventoryItemEncrypted(request.Params.ToArray(), request.Id);
				else if (methodName.Equals("getRecipeStatus", StringComparison.OrdinalIgnoreCase)
					|| methodName.Equals("getRecipeStatusEncrypted", StringComparison.OrdinalIgnoreCase))
					getRecipeStatus(request.Params.ToArray(), request.Id);

				else
				{
					Logger.Error($"[Inventory] Error: Method not found: {methodName}");
					Console.WriteLine($"[Inventory] Error: Method not found: {methodName}");
					_user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = request.Id, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 404 } } });
				}
			}
			catch (System.Exception ex)
			{
				Console.WriteLine($"[Inventory] Critical error in Invoke ({request.MethodName}): {ex.Message}\n{ex.StackTrace}");
				SendError(request.Id, 500);
			}
		}
	}
}










