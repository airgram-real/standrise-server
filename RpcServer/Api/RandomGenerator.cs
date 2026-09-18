using System;
using System.Collections.Generic;
using System.Linq;
using Axlebolt.Bolt.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;

namespace StandRiseServer.RpcServer.Api;

public class RandomGenerator
{
	private Random random;

	/// <summary>
	/// true — открывается пак наклеек / брелоков / граффити, а не оружейный кейс.
	/// Фильтр лута у них ПРОТИВОПОЛОЖНЫЙ: для кейса наклейки и брелоки — мусор,
	/// а для пака они и есть весь его состав. Ставится рецептом перед GetRandomItem.
	/// </summary>
	public bool StickerCharmPackMode { get; set; }

	private Dictionary<SkinValue, double> dropChances;

	private readonly Dictionary<CurrencyAmount, double> _currencyChances = new Dictionary<CurrencyAmount, double>();

	private readonly Dictionary<InventoryId, double> _boxAndCaseChances = new Dictionary<InventoryId, double>();

	private readonly Dictionary<InventoryId, double> _itemsChances = new Dictionary<InventoryId, double>();

	private readonly Dictionary<InventoryId, double> _giftBoxDropChances = new Dictionary<InventoryId, double>();

	private readonly Dictionary<InventoryId, double> _caseProbabilities = new Dictionary<InventoryId, double>();

	private readonly Dictionary<InventoryId, double> _boxProbabilities = new Dictionary<InventoryId, double>();

	private readonly Dictionary<InventoryId, double> _secretProbabilities = new Dictionary<InventoryId, double>();

	public static BoltInventoryItemDefinitionDocument[] definitions;

	public static List<CurrencyAmount> Currencies = new List<CurrencyAmount>();

	public static int statTrakChance;

	public Dictionary<InventoryId, double> GiftBoxDropChances => _giftBoxDropChances;

	public RandomGenerator()
	{
		random = new Random();
		dropChances = new Dictionary<SkinValue, double>();
		if (definitions == null)
		{
			AddDefinitions(BoltGameDatabaseProvider.Instance.GetAllItemDefinitionsByType(0u).ToArray());
		}
		statTrakChance = 20;
	}

	public void AddDropChance(SkinValue itemType, double chance)
	{
		dropChances[itemType] = chance;
	}

	public void AddCurrencyLuckChance(CurrencyAmount currency, double chance)
	{
		Currencies.Add(currency);
		_currencyChances[currency] = chance;
	}

	public void AddBoxAndChance(InventoryId boxId, double chance)
	{
		_boxProbabilities[boxId] = chance;
	}

	public void AddCaseAndChance(InventoryId caseId, double chance)
	{
		_caseProbabilities[caseId] = 1000.0;
	}

	public void AddSkinsInGameChance(InventoryId inventoryId, double chance)
	{
		_secretProbabilities[inventoryId] = 1000.0;
	}

	public void ClearCurrencyLuckChances()
	{
		Currencies.Clear();
		_currencyChances.Clear();
	}

	public void AddItemsLuckChance(InventoryId currency, double chance)
	{
		_itemsChances[currency] = chance;
	}

	private void ClearItemsLuckChances()
	{
		_itemsChances.Clear();
	}

	public void AddGiftBox2018DropChance(InventoryId itemId, double chance)
	{
		_giftBoxDropChances[itemId] = 1000.0;
	}

	public void AddDefinitions(BoltInventoryItemDefinitionDocument[] Definitions)
	{
		definitions = Definitions;
	}

	public BoltInventoryItem? GetRandomItem(CollectionId collectionId)
	{
		if (definitions == null || definitions.Length == 0)
		{
			Console.WriteLine("[RandomGenerator] ERROR: GetRandomItem - definitions list is null or empty!");
			return null;
		}

		// Подкрутка из админки: выдаём предмет максимальной редкости.
		// Если в коллекции Arcane нет — спускаемся к следующей по убыванию, чтобы
		// не уронить открытие кейса там, где такой редкости просто не существует.
		bool boost = false;
		try { boost = BoltGameDatabaseProvider.Instance.GetArcaneBoost(); } catch { }
		if (boost)
		{
			foreach (SkinValue forced in new[] { SkinValue.Arcane, SkinValue.Legendary, SkinValue.Epic, SkinValue.Rare })
			{
				BoltInventoryItemDefinitionDocument forcedDef = GetRandomItem(forced, collectionId);
				if (forcedDef == null) continue;
				BoltInventoryItem forcedItem = new BoltInventoryItem
				{
					itemDefinitionId = forcedDef.key,
					date = DateTime.Now,
					flags = 0,
					quantity = 1
				};
				if (forcedDef.IsStattrack())
				{
					forcedItem.Properties.Add(new BoltInventoryItemProperty
					{
						Name = "stattrack_value",
						Type = BoltInventoryItemProperty.PropertyType.Int,
						Value = "0"
					});
				}
				Console.WriteLine($"[RandomGenerator] arcaneBoost: {collectionId} → #{forcedDef.key} ({forced})");
				return forcedItem;
			}
		}

		double totalChances = 0.0;
		foreach (double chance in dropChances.Values)
		{
			totalChances += chance;
		}
		double randomChance = random.NextDouble() * totalChances;
		double accumulatedChances = 0.0;
		foreach (KeyValuePair<SkinValue, double> item in dropChances)
		{
			accumulatedChances += item.Value;
			if (randomChance < accumulatedChances)
			{
				BoltInventoryItemDefinitionDocument boltInventoryItemDefinition = GetRandomItem(item.Key, collectionId);
				if (boltInventoryItemDefinition == null)
				{
					Console.WriteLine($"[RandomGenerator] ERROR: GetRandomItem returned null for collection {collectionId}, skin {item.Key}");
					return null;
				}

				BoltInventoryItem boltInventoryItem = new BoltInventoryItem
				{
					itemDefinitionId = boltInventoryItemDefinition.key,
					date = DateTime.Now,
					flags = 0,
					quantity = 1
				};
				if (boltInventoryItemDefinition.IsStattrack())
				{
					boltInventoryItem.Properties.Add(new BoltInventoryItemProperty
					{
						Name = "stattrack_value",
						Type = BoltInventoryItemProperty.PropertyType.Int,
						Value = "0"
					});
				}
				return boltInventoryItem;
			}
		}
		return null;
	}

	private BoltInventoryItem? GetRandomSecretItem()
	{
		double totalChances = dropChances.Values.Sum();
		double randomChance = random.NextDouble() * totalChances;
		double accumulatedChances = 0.0;
		foreach (KeyValuePair<SkinValue, double> item in dropChances)
		{
			accumulatedChances += item.Value;
			if (randomChance < accumulatedChances)
			{
				BoltInventoryItemDefinitionDocument boltInventoryItemDefinition = GetRandomSecretItem(BoltGameDatabaseProvider.Instance.GetInventoryItemDefinition((int)item.Key, 0));
				BoltInventoryItem boltInventoryItem = new BoltInventoryItem
				{
					itemDefinitionId = boltInventoryItemDefinition.key,
					date = DateTime.UtcNow,
					flags = 0,
					quantity = 1
				};
				if (boltInventoryItemDefinition.IsStattrack())
				{
					boltInventoryItem.Properties.Add(new BoltInventoryItemProperty
					{
						Name = "stattrack_value",
						Type = BoltInventoryItemProperty.PropertyType.Int,
						Value = "25343"
					});
				}
				return boltInventoryItem;
			}
		}
		return null;
	}

	public BoltInventoryItem GetRandomGiftBoxItem()
	{
		double totalChances = _giftBoxDropChances.Values.Sum();
		double randomChance = random.NextDouble() * totalChances;
		double accumulatedChances = 0.0;
		foreach (KeyValuePair<InventoryId, double> item in _giftBoxDropChances)
		{
			accumulatedChances += item.Value;
			if (randomChance < accumulatedChances)
			{
				BoltInventoryItemDefinitionDocument boltInventoryItemDefinition = GetRandomGbItem(item.Key);
				return new BoltInventoryItem
				{
					itemDefinitionId = boltInventoryItemDefinition.key,
					date = DateTime.UtcNow,
					flags = 0,
					quantity = 1
				};
			}
		}
		return null;
	}

	public (BoltInventoryItem[], CurrencyAmount[]) GetRandomDropInGame(bool pro)
	{
		(BoltInventoryItem[], CurrencyAmount[]) ret = (Array.Empty<BoltInventoryItem>(), Array.Empty<CurrencyAmount>());
		List<BoltInventoryItem> list = new List<BoltInventoryItem>();
		List<CurrencyAmount> list2 = new List<CurrencyAmount>();
		if (!pro)
		{
			BoltInventoryItem caseDrop = GetRandomCaseDrop();
			BoltInventoryItem boxDrop = GetRandomBoxDrop();
			CurrencyAmount[] currencies = GetRandomCurrenciesDrop();
			if (caseDrop == null && boxDrop == null)
			{
				if (currencies != null)
				{
					CurrencyAmount[] array = currencies;
					foreach (CurrencyAmount currency in array)
					{
						list2.Add(currency);
					}
				}
			}
			else if (caseDrop != null)
			{
				list.Add(caseDrop);
			}
			else if (caseDrop == null && boxDrop != null)
			{
				list.Add(boxDrop);
			}
		}
		else
		{
			BoltInventoryItem caseDrop2 = GetRandomCaseDrop();
			BoltInventoryItem boxDrop2 = GetRandomBoxDrop();
			CurrencyAmount[] currencies2 = GetRandomCurrenciesDrop();
			BoltInventoryItem secret = GetRandomSecretItem();
			if (caseDrop2 == null && boxDrop2 == null)
			{
				if (secret != null)
				{
					list.Add(secret);
				}
				else if (currencies2 != null)
				{
					CurrencyAmount[] array2 = currencies2;
					foreach (CurrencyAmount currency2 in array2)
					{
						list2.Add(currency2);
					}
				}
			}
			else if (caseDrop2 != null)
			{
				list.Add(caseDrop2);
			}
			else if (caseDrop2 == null && boxDrop2 != null)
			{
				list.Add(boxDrop2);
			}
		}
		ret.Item1 = list.ToArray();
		ret.Item2 = list2.ToArray();
		return ret;
	}

	private BoltInventoryItem? GetRandomCaseDrop()
	{
		double totalChances = _caseProbabilities.Values.Sum();
		double randomChance = random.NextDouble() * totalChances;
		double accumulatedChances = 0.0;
		foreach (KeyValuePair<InventoryId, double> kvp in _caseProbabilities)
		{
			accumulatedChances += kvp.Value;
			if (randomChance < accumulatedChances)
			{
				return new BoltInventoryItem
				{
					itemDefinitionId = (int)kvp.Key,
					date = DateTime.UtcNow,
					flags = 0,
					quantity = 1
				};
			}
		}
		return null;
	}

	private BoltInventoryItem? GetRandomBoxDrop()
	{
		double totalChances = _caseProbabilities.Values.Sum();
		double randomChance = random.NextDouble() * totalChances;
		double accumulatedChances = 0.0;
		foreach (KeyValuePair<InventoryId, double> kvp in _caseProbabilities)
		{
			accumulatedChances += kvp.Value;
			if (randomChance < accumulatedChances)
			{
				return new BoltInventoryItem
				{
					itemDefinitionId = (int)kvp.Key,
					date = DateTime.UtcNow,
					flags = 0,
					quantity = 1
				};
			}
		}
		return null;
	}

	public CurrencyAmount[]? GetRandomCurrenciesDrop()
	{
		List<CurrencyAmount> currencies = new List<CurrencyAmount>();
		double totalChances = _currencyChances.Values.Sum();
		bool hasAdditionalReward = random.NextDouble() < 0.1;
		double randomChance = random.NextDouble() * totalChances;
		double accumulatedChances = 0.0;
		foreach (KeyValuePair<CurrencyAmount, double> currencyChance in _currencyChances)
		{
			accumulatedChances += currencyChance.Value;
			if (randomChance < accumulatedChances)
			{
				CurrencyAmount currency = GetRandomCurrencyItem();
				currencies.Add(currency);
				ClearCurrencyLuckChances();
				break;
			}
		}
		if (hasAdditionalReward)
		{
			CurrencyAmount additionalCurrency = GetRandomCurrencyItem();
			currencies.Add(additionalCurrency);
		}
		return (currencies.Count > 0) ? currencies.ToArray() : null;
	}

	public BoltInventoryItem? GetRandomBoxItem(CollectionId collectionId)
	{
		// Та же подкрутка из админки, что и в GetRandomItem.
		bool boxBoost = false;
		try { boxBoost = BoltGameDatabaseProvider.Instance.GetArcaneBoost(); } catch { }
		if (boxBoost)
		{
			foreach (SkinValue forced in new[] { SkinValue.Arcane, SkinValue.Legendary, SkinValue.Epic, SkinValue.Rare })
			{
				BoltInventoryItemDefinitionDocument forcedDef = GetRandomBoxItem(forced, collectionId);
				if (forcedDef == null) continue;
				Console.WriteLine($"[RandomGenerator] arcaneBoost(box): {collectionId} → #{forcedDef.key} ({forced})");
				return new BoltInventoryItem
				{
					itemDefinitionId = forcedDef.key,
					date = DateTime.Now,
					flags = 0,
					quantity = 1
				};
			}
		}

		double totalChances = 0.0;
		foreach (double chance in dropChances.Values)
		{
			totalChances += chance;
		}
		double randomChance = random.NextDouble() * totalChances;
		double accumulatedChances = 0.0;
		foreach (KeyValuePair<SkinValue, double> item in dropChances)
		{
			accumulatedChances += item.Value;
			if (randomChance < accumulatedChances)
			{
				BoltInventoryItemDefinitionDocument boltInventoryItemDefinition = GetRandomBoxItem(item.Key, collectionId);
				return new BoltInventoryItem
				{
					itemDefinitionId = boltInventoryItemDefinition.key,
					date = DateTime.Now,
					flags = 0,
					quantity = 1
				};
			}
		}
		return null;
	}

	private BoltInventoryItemDefinitionDocument GetRandomItem(SkinValue skinValue, CollectionId collectionId)
	{
		if (definitions == null || definitions.Length == 0)
		{
			Console.WriteLine("[RandomGenerator] CRITICAL: definitions array is null or empty!");
			return null;
		}

		// Только оружие/скины своей коллекции. Без глобального fallback —
		// иначе при пустом Arcane выпадают cursed_souls / брелки / наклейки.
		List<BoltInventoryItemDefinitionDocument> items = definitions
			.Where(a => a.properties != null
				&& a.GetSkinValue() == skinValue
				&& a.GetCollectionId() == collectionId
				&& IsLootFor(a))
			.ToList();
		if (items.Count == 0)
		{
			items = definitions
				.Where(a => a.properties != null
					&& a.GetCollectionId() == collectionId
					&& a.GetSkinValue() != SkinValue.None
					&& IsLootFor(a))
				.ToList();
		}
		if (items.Count == 0)
		{
			Console.WriteLine($"[RandomGenerator] CRITICAL: No items found for collection {collectionId}, value {skinValue}");
			return null;
		}
		int randomValue = random.Next(0, items.Count);
		return items[randomValue];
	}

	private BoltInventoryItemDefinitionDocument GetRandomBoxItem(SkinValue skinValue, CollectionId collectionId)
	{
		List<BoltInventoryItemDefinitionDocument> items = definitions
			.Where(a => a.properties != null
				&& a.GetSkinValue() == skinValue
				&& a.GetCollectionId() == collectionId
				&& !a.IsStattrack()
				&& IsLootFor(a))
			.ToList();
		if (items.Count == 0)
		{
			items = definitions
				.Where(a => a.properties != null
					&& a.GetCollectionId() == collectionId
					&& !a.IsStattrack()
					&& a.GetSkinValue() != SkinValue.None
					&& IsLootFor(a))
				.ToList();
		}
		if (items.Count == 0)
		{
			Console.WriteLine($"[RandomGenerator] CRITICAL: No box items found for collection {collectionId}, value {skinValue}");
			return null;
		}
		int randomValue = random.Next(0, items.Count);
		return items[randomValue];
	}

	/// <summary>Кейс/бокс: только скины оружия своей коллекции (не медали/стикеры/брелки/CS).</summary>
	/// <summary>Какой фильтр применять — зависит от того, что открывают.</summary>
	private bool IsLootFor(BoltInventoryItemDefinitionDocument def)
	{
		return StickerCharmPackMode ? IsPackLoot(def) : IsCaseBoxLootSkin(def);
	}

	/// <summary>
	/// Лут пака наклеек / брелоков / граффити.
	///
	/// Раньше здесь работал IsCaseBoxLootSkin, который выбрасывает ключи 1200..1999
	/// («стикеры») и 5000..5999 («брелки»). Для оружейного кейса это правильно, а для
	/// пака наклеек — вырезает ВЕСЬ его состав. У «Rainbow» из двадцати наклеек фильтр
	/// пропускал ровно одну — #1140 Sticker "Murder", потому что её ключ меньше 1200.
	/// Отсюда и «постоянно падает Murder»: выбирать было не из чего.
	///
	/// Здесь режем только контейнеры и служебные предметы, а сам состав коллекции
	/// (наклейки, брелоки, граффити) оставляем.
	/// </summary>
	private static bool IsPackLoot(BoltInventoryItemDefinitionDocument def)
	{
		if (def == null) return false;
		int key = def.key;
		// медали, спин-токены, слоты рулетки
		if (key > 0 && key < 300) return false;
		// кейсы и боксы
		if (key >= 301 && key <= 409) return false;
		// подарки, сами паки, боевые пропуска — контейнер не может выпасть из себя
		if (key >= 500 && key <= 999) return false;
		if (def.GetCollectionId() == CollectionId.None) return false;
		return def.GetSkinValue() != SkinValue.None;
	}

	private static bool IsCaseBoxLootSkin(BoltInventoryItemDefinitionDocument def)
	{
		if (def == null) return false;
		int key = def.key;
		// медали / токены / пассы
		if (key > 0 && key < 300) return false;
		// кейсы/боксы сами по себе
		if (key >= 301 && key <= 409) return false;
		// стикеры
		if (key >= 1200 && key < 2000) return false;
		// брелки / chibi
		if (key >= 5000 && key < 6000) return false;
		var col = def.GetCollectionId();
		if (col == CollectionId.None) return false;
		string raw = null;
		if (def.properties != null && def.properties.TryGetValue("collection", out var cv) && cv != null && !cv.IsBsonNull)
			raw = cv.ToString();
		if (!string.IsNullOrEmpty(raw) && raw.IndexOf("cursed", StringComparison.OrdinalIgnoreCase) >= 0)
			return false;
		return def.GetSkinValue() != SkinValue.None;
	}

	private CurrencyAmount[] GetRandomCurrencies()
	{
		return (from _ in Currencies
			select random.Next(Currencies.Count) into randomIndex
			select Currencies[randomIndex]).ToArray();
	}

	private BoltInventoryItemDefinitionDocument GetRandomSecretItem(BoltInventoryItemDefinitionDocument def)
	{
		if (def == null)
		{
			return null;
		}

		CollectionId collectionId = def.GetCollectionId();
		SkinValue skinValue = def.GetSkinValue();
		if (collectionId == CollectionId.None || skinValue == SkinValue.None)
		{
			return null;
		}

		List<BoltInventoryItemDefinitionDocument> items = definitions
			.Where(a => a.GetSkinValue() == skinValue && a.GetCollectionId() == collectionId)
			.ToList();
		if (items.Count == 0)
		{
			items = definitions
				.Where(a => a.GetSkinValue() == skinValue && a.GetCollectionId() == collectionId)
				.ToList();
		}
		if (items.Count == 0)
		{
			return null;
		}
		int randomValue = random.Next(0, items.Count);
		return items[randomValue];
	}

	private CurrencyAmount GetRandomCurrencyItem()
	{
		CurrencyAmount currencyAmount = new CurrencyAmount();
		currencyAmount.Value = 69f;
		currencyAmount.OldValue = 69;
		currencyAmount.CurrencyId = 101;
		return currencyAmount;
	}

	private BoltInventoryItemDefinitionDocument GetRandomGbItem(InventoryId id)
	{
		List<BoltInventoryItemDefinitionDocument> items = definitions
			.Where(a => a.key == (int)id)
			.ToList();
		if (items.Count == 0)
		{
			items = definitions.ToList();
		}
		int randomValue = random.Next(0, items.Count);
		return items[randomValue];
	}

	private BoltInventoryItemDefinitionDocument[] GetRandomItems()
	{
		return definitions;
	}

	private static int CalculateWeight(BoltInventoryItemDefinitionDocument definition)
	{
		int num = (int)(7 - definition.GetSkinValue());
		return num * num;
	}
}
