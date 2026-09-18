using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;

namespace StandRiseServer.RpcServer.Security
{
	public sealed class InventoryPurchaseValidation
	{
		public bool Allowed { get; set; }
		public int ErrorCode { get; set; } = 403;
		public string Reason { get; set; }
		public double ItemPrice { get; set; }
		public double TotalCost { get; set; }
	}

	public static class InventoryDupeProtection
	{
		private static readonly int[] BlockedPurchaseItemIds = { 501, 701, 901 };

		public static InventoryPurchaseValidation ValidatePurchase(
			string playerId,
			int itemId,
			int quantity,
			int currencyId,
			BoltGameDatabaseProvider db)
		{
			var result = new InventoryPurchaseValidation();

			if (quantity <= 0)
			{
				return Reject(result, "negative or zero quantity");
			}

			if (currencyId <= 0)
			{
				return Reject(result, "invalid currency");
			}

			if (quantity > 100)
			{
				return Reject(result, "suspicious quantity");
			}

			// Потолок 100_000 отсекал почти треть каталога: у скинов id доходит до 31_240_326
			// (например S3 AKR12 "Year of the Horse"), и покупка любого такого предмета
			// отбивалась как "suspicious item id" — клиент показывал ошибку запроса.
			// Реальную проверку делает поиск определения ниже: чего нет в каталоге, то не купить.
			if (itemId < 0 || itemId > 100_000_000)
			{
				return Reject(result, "suspicious item id");
			}

			if (BlockedPurchaseItemIds.Contains(itemId))
			{
				return Reject(result, "blocked item purchase", itemId);
			}

			BoltInventoryItemDefinitionDocument definition = db.GetInventoryItemDefinition(itemId, 1);
			if (definition == null)
			{
				result.ErrorCode = 404;
				return Reject(result, "item definition not found");
			}

			BsonDocument priceDoc = ResolvePrice(definition, itemId, currencyId);
			if (priceDoc == null)
			{
				return Reject(result, "item not for sale");
			}

			double itemPrice = priceDoc["value"].ToDouble();
			if (itemPrice <= 0 || itemPrice > 100_000)
			{
				return Reject(result, "invalid server price");
			}

			double totalCost = itemPrice * quantity;
			if (totalCost <= 0)
			{
				return Reject(result, "invalid total cost");
			}

			if (!db.IsEnoughFunds(playerId, currencyId.ToString(), (float)totalCost))
			{
				result.ErrorCode = 402;
				return Reject(result, "insufficient funds");
			}

			if (!ValidatePriceMap(db, itemId, currencyId, itemPrice))
			{
				return Reject(result, "price/id swap detected");
			}

			result.Allowed = true;
			result.ItemPrice = itemPrice;
			result.TotalCost = totalCost;
			return result;
		}

		public static bool IsBlockedExchangeRecipe(string recipeCode)
		{
			return string.Equals(recipeCode, "RECIPE_OPEN_GIFT_501", StringComparison.OrdinalIgnoreCase);
		}

		// Пост-матч дропы (RECIPE_DROP_IN_GAME / RECIPE_GOOD_GAME_* и т.д.) раньше не имели
		// НИКАКОЙ защиты - клиент мог слать exchangeInventoryItems с этим recipeCode сколько
		// угодно раз подряд и фармить валюту+скин бесконечно (дюп). Простой кулдаун по
		// таймеру тоже не спасает - его можно пересидеть и дюпать медленнее, но бесконечно
		// (именно так и было отмечено: "буду слать раз в 30 сек, тогда").
		//
		// Правильный фикс - "кредитная" система: сервер даёт кредит на пост-матч дроп ТОЛЬКО
		// когда матч РЕАЛЬНО завершился - это сообщает Photon-плагин (OnCloseGame), который
		// клиент подделать не может, в отличие от обычного RPC. Каждый вызов ExchangeInventoryItems
		// с пост-матч recipeCode тратит один кредит; без кредита - отказ. Так дюп невозможен
		// вообще, независимо от того, как часто клиент спамит RPC.
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> PostMatchDropCredits
			= new System.Collections.Concurrent.ConcurrentDictionary<string, int>();

		// На один реально завершённый матч даём несколько кредитов - у клиента может быть
		// несколько разных пост-матч recipeCode за один матч (RECIPE_DROP_IN_GAME +
		// RECIPE_GOOD_GAME_1/2/3 и т.п.), но кредиты не копятся бесконечно.
		private const int PostMatchDropCreditsPerMatch = 4;
		private const int MaxPostMatchDropCreditsPerPlayer = 8;

		public static void GrantPostMatchDropCredits(string playerId)
		{
			PostMatchDropCredits.AddOrUpdate(playerId,
				_ => Math.Min(PostMatchDropCreditsPerMatch, MaxPostMatchDropCreditsPerPlayer),
				(_, current) => Math.Min(current + PostMatchDropCreditsPerMatch, MaxPostMatchDropCreditsPerPlayer));
		}

		public static bool TryClaimPostMatchDrop(string playerId, string recipeCode)
		{
			bool consumed = false;
			PostMatchDropCredits.AddOrUpdate(playerId,
				addValueFactory: _ => 0,
				updateValueFactory: (_, current) =>
				{
					if (current > 0)
					{
						consumed = true;
						return current - 1;
					}
					return current;
				});

			if (!consumed)
			{
				Logger.LogWarn($"[SECURITY] Post-match drop blocked for player {playerId}, recipe {recipeCode}: no credit (реальное завершение матча не зафиксировано или уже потрачено)");
			}
			return consumed;
		}

		private static bool ValidatePriceMap(BoltGameDatabaseProvider db, int itemId, int currencyId, double itemPrice)
		{
			BoltInventoryItemDefinitionDocument definition = db.GetInventoryItemDefinition(itemId, 1);
			if (definition == null)
			{
				return false;
			}

			BsonDocument priceDoc = ResolvePrice(definition, itemId, currencyId);
			if (priceDoc == null)
			{
				return false;
			}

			double expectedPrice = priceDoc["value"].ToDouble();
			return Math.Abs(expectedPrice - itemPrice) < 0.01;
		}

		private static BsonDocument ResolvePrice(BoltInventoryItemDefinitionDocument definition, int itemId, int currencyId)
		{
			BsonDocument priceDoc = null;
			if (definition.buyPrice != null && definition.buyPrice.IsBsonArray)
			{
				priceDoc = definition.buyPrice.AsBsonArray
					.FirstOrDefault(p => p.IsBsonDocument
					                     && p.AsBsonDocument.Contains("currencyId")
					                     && p.AsBsonDocument["currencyId"].AsInt32 == currencyId)
					?.AsBsonDocument;
			}

			if (priceDoc != null)
			{
				return priceDoc;
			}

			if (itemId is >= 301 and <= 307)
			{
				if (currencyId == 102)
				{
					return new BsonDocument { { "currencyId", 102 }, { "value", 100 } };
				}
			}

			if (itemId is >= 401 and <= 407)
			{
				if (currencyId == 101)
				{
					return new BsonDocument { { "currencyId", 101 }, { "value", 100 } };
				}
			}

			if (itemId == 702)
			{
				if (currencyId == 102)
				{
					return new BsonDocument { { "currencyId", 102 }, { "value", 60 } };
				}
			}

			return null;
		}

		private static InventoryPurchaseValidation Reject(InventoryPurchaseValidation result, string reason, int itemId = -1)
		{
			result.Allowed = false;
			result.Reason = reason;
			if (itemId > 0)
			{
				Logger.LogWarn($"[SECURITY] Inventory purchase blocked (itemId={itemId}): {reason}");
			}
			else
			{
				Logger.LogWarn($"[SECURITY] Inventory purchase blocked: {reason}");
			}
			return result;
		}
	}
}
