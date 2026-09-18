using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using MongoDB.Bson;
using MongoDB.Driver;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>Публичная витрина рынка для апгрейдера на сайте (только чтение).</summary>
    public static class MarketUpgraderApi
    {
        public sealed class Result
        {
            public int Status;
            public string Json;
            public Result(int status, string json) { Status = status; Json = json; }
        }

        private static Result Ok(object o) => new Result(200, JsonSerializer.Serialize(o));
        private static Result Err(int code, string message) =>
            new Result(code, JsonSerializer.Serialize(new { ok = false, error = message }));

        public static Task<Result> CatalogAsync(int limit)
        {
            try
            {
                limit = Math.Clamp(limit, 20, 600);
                var prices = LoadLatestSalePrices(Math.Max(limit * 6, 400));
                InventoryCatalogueLoader.Instance.EnsureLoaded();

                var list = new List<object>();
                foreach (var kv in prices.OrderByDescending(x => x.Value.latestCreate))
                {
                    if (list.Count >= limit) break;
                    int id = kv.Key;
                    var def = InventoryCatalogueLoader.Instance.GetByKey(id);
                    if (def?.properties == null) continue;
                    if (def.GetSkinValue() == SkinValue.None) continue;
                    if (!def.canBeTraded) continue;

                    string name = def.displayName ?? $"#{id}";
                    if (def.IsStattrack()) name += " (StatTrak)";

                    list.Add(new
                    {
                        id,
                        name,
                        rarity = def.GetSkinValue().ToString(),
                        price = Math.Round(kv.Value.price, 2),
                        sales = kv.Value.count,
                        image = SkinImageUrl(id),
                    });
                }

                return Task.FromResult(Ok(new { ok = true, items = list }));
            }
            catch (Exception ex)
            {
                try { Logger.LogWarn($"[MarketUpgraderApi] {ex.Message}"); } catch { }
                return Task.FromResult(Err(500, ex.Message));
            }
        }

        /// <summary>Скины игрока для ставки (цена — последний лот на рынке по defId).</summary>
        public static Task<Result> InventoryAsync(string idOrUid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(idOrUid))
                    return Task.FromResult(Err(400, "empty id"));

                if (!DonateBotService.BridgeResolvePlayerId(idOrUid, out string pid, out string error))
                    return Task.FromResult(Err(404, string.IsNullOrEmpty(error) ? "игрок не найден" : error));

                var inv = BoltGameDatabaseProvider.Instance.GetPlayerInventoryDocument(ObjectId.Parse(pid));
                if (inv?.InventoryItems == null || inv.InventoryItems.ElementCount == 0)
                    return Task.FromResult(Ok(new { ok = true, items = Array.Empty<object>() }));

                var prices = LoadLatestSalePrices(8000);
                InventoryCatalogueLoader.Instance.EnsureLoaded();
                var rows = new List<(string instanceId, int id, string name, string rarity, float price, string image)>();

                foreach (var el in inv.InventoryItems.Elements)
                {
                    var bolt = el.ToBoltInventory();
                    if (bolt == null || bolt.itemDefinitionId <= 0) continue;
                    int defId = bolt.itemDefinitionId;
                    var def = InventoryCatalogueLoader.Instance.GetByKey(defId);
                    if (def?.properties == null) continue;
                    if (def.GetSkinValue() == SkinValue.None) continue;
                    if (!def.canBeTraded) continue;

                    if (!prices.TryGetValue(defId, out var p) || p.price <= 0) continue;

                    string name = def.displayName ?? $"#{defId}";
                    if (def.IsStattrack()) name += " (StatTrak)";

                    rows.Add((
                        el.Name,
                        defId,
                        name,
                        def.GetSkinValue().ToString(),
                        (float)Math.Round(p.price, 2),
                        SkinImageUrl(defId)));
                }

                var list = rows
                    .OrderByDescending(x => x.price)
                    .Select(x => new
                    {
                        instanceId = x.instanceId,
                        id = x.id,
                        name = x.name,
                        rarity = x.rarity,
                        price = x.price,
                        image = x.image,
                    })
                    .ToList();

                return Task.FromResult(Ok(new { ok = true, items = list }));
            }
            catch (Exception ex)
            {
                try { Logger.LogWarn($"[MarketUpgraderApi] inventory: {ex.Message}"); } catch { }
                return Task.FromResult(Err(500, ex.Message));
            }
        }

        private sealed class SaleAgg
        {
            public float price;
            public int count;
            public DateTime latestCreate;
        }

        private static Dictionary<int, SaleAgg> LoadLatestSalePrices(int maxRows)
        {
            var agg = new Dictionary<int, SaleAgg>();
            var db = BoltGameDatabaseProvider.Instance.GetDatabase;
            var collection = db.GetCollection<MarketplaceRequestDocument>("marketplace_request");
            var filter = Builders<MarketplaceRequestDocument>.Filter.Eq(x => x.status, MarketplaceRequestStatus.Active)
                         & Builders<MarketplaceRequestDocument>.Filter.Eq(x => x.type, MarketRequestType.SaleRequest);

            var rows = collection.Find(filter)
                .SortByDescending(x => x.createDate)
                .Limit(maxRows)
                .ToList();

            foreach (var r in rows)
            {
                if (r.itemDefinitionId <= 0 || r.price <= 0) continue;
                if (!agg.TryGetValue(r.itemDefinitionId, out var a))
                {
                    agg[r.itemDefinitionId] = new SaleAgg
                    {
                        price = r.price,
                        count = 1,
                        latestCreate = r.createDate,
                    };
                    continue;
                }
                a.count++;
                if (r.createDate >= a.latestCreate)
                {
                    a.latestCreate = r.createDate;
                    a.price = r.price;
                }
            }
            return agg;
        }

        public static string SkinImageUrl(int itemDefinitionId)
            => $"https://cdn.axlebolt.com/Standoff2/skin_preview/{itemDefinitionId}.png";

        private const double MinProbability = 0.0001;
        private const double MaxProbability = 0.8;
        private const int MaxStakeItems = 6;
        private const int GoldCurrencyId = 102;
        private static readonly Random Rng = new Random();

        public sealed class BetRequest
        {
            public List<string>? inventoryItemIds { get; set; }
            public int targetItemId { get; set; }
            public double targetItemPrice { get; set; }
            public double? addedBalance { get; set; }
        }

        private static double ComputeProbability(double betAmount, double targetPrice)
        {
            if (betAmount <= 0 || targetPrice <= 0) return 0;
            return Math.Min(1.0, betAmount / targetPrice);
        }

        /// <summary>Ставка скинами + опционально голда; приз — defId с рынка.</summary>
        public static Task<Result> BetAsync(string idOrUid, string jsonBody)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(idOrUid))
                    return Task.FromResult(Err(400, "empty id"));

                if (!DonateBotService.BridgeResolvePlayerId(idOrUid, out string pid, out string error))
                    return Task.FromResult(Err(404, string.IsNullOrEmpty(error) ? "игрок не найден" : error));

                BetRequest? req;
                try
                {
                    req = JsonSerializer.Deserialize<BetRequest>(jsonBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                    return Task.FromResult(Err(400, "bad json"));
                }

                if (req == null || req.targetItemId <= 0 || req.inventoryItemIds == null || req.inventoryItemIds.Count == 0)
                    return Task.FromResult(Err(400, "нужны скины и цель"));

                if (req.inventoryItemIds.Count > MaxStakeItems)
                    return Task.FromResult(Err(400, $"не больше {MaxStakeItems} предметов"));

                var oid = ObjectId.Parse(pid);
                var db = BoltGameDatabaseProvider.Instance;
                var inv = db.GetPlayerInventoryDocument(oid);
                if (inv?.InventoryItems == null)
                    return Task.FromResult(Err(400, "пустой инвентарь"));

                var prices = LoadLatestSalePrices(8000);
                InventoryCatalogueLoader.Instance.EnsureLoaded();

                if (!prices.TryGetValue(req.targetItemId, out var targetAgg) || targetAgg.price <= 0)
                    return Task.FromResult(Err(400, "цель не на рынке"));

                double marketTargetPrice = targetAgg.price;
                if (Math.Abs(marketTargetPrice - req.targetItemPrice) > 0.05)
                    return Task.FromResult(Err(400, "устарела цена цели"));

                var targetDef = InventoryCatalogueLoader.Instance.GetByKey(req.targetItemId);
                if (targetDef?.properties == null || targetDef.GetSkinValue() == SkinValue.None || !targetDef.canBeTraded)
                    return Task.FromResult(Err(400, "недопустимая цель"));

                double addedGold = Math.Max(0, req.addedBalance ?? 0);
                addedGold = Math.Round(addedGold, 2, MidpointRounding.ToZero);

                var stakeRows = new List<(int slotId, int defId, string name, float price)>();
                var seenSlots = new HashSet<int>();

                foreach (var rawId in req.inventoryItemIds.Distinct())
                {
                    if (!int.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slotId))
                        return Task.FromResult(Err(400, "неверный id предмета"));

                    if (!seenSlots.Add(slotId))
                        continue;

                    BsonElement? slotEl = null;
                    foreach (var el in inv.InventoryItems.Elements)
                    {
                        if (el.Name == rawId || el.Name == slotId.ToString())
                        {
                            slotEl = el;
                            break;
                        }
                    }

                    if (slotEl == null)
                        return Task.FromResult(Err(400, $"нет предмета {slotId}"));

                    var bolt = slotEl.Value.ToBoltInventory();
                    if (bolt == null || bolt.itemDefinitionId <= 0)
                        return Task.FromResult(Err(400, "битый предмет"));

                    int defId = bolt.itemDefinitionId;
                    var def = InventoryCatalogueLoader.Instance.GetByKey(defId);
                    if (def?.properties == null || def.GetSkinValue() == SkinValue.None || !def.canBeTraded)
                        return Task.FromResult(Err(400, "предмет нельзя поставить"));

                    if (!prices.TryGetValue(defId, out var sp) || sp.price <= 0)
                        return Task.FromResult(Err(400, "нет цены на рынке для ставки"));

                    string name = def.displayName ?? $"#{defId}";
                    if (def.IsStattrack()) name += " (StatTrak)";
                    stakeRows.Add((slotId, defId, name, (float)Math.Round(sp.price, 2)));
                }

                double itemsValue = stakeRows.Sum(x => x.price);
                double betAmount = Math.Round(itemsValue + addedGold, 2, MidpointRounding.ToZero);

                if (betAmount <= 0)
                    return Task.FromResult(Err(400, "нулевая ставка"));

                if (betAmount >= marketTargetPrice)
                    return Task.FromResult(Err(400, "ставка не меньше приза"));

                double probability = ComputeProbability(betAmount, marketTargetPrice);
                if (probability < MinProbability || probability > MaxProbability)
                    return Task.FromResult(Err(400, "шанс вне 0.01%–80%"));

                if (addedGold > 0)
                {
                    double goldBal = 0;
                    if (inv.Currencies != null && inv.Currencies.TryGetValue(GoldCurrencyId.ToString(), out var gv))
                    {
                        if (gv.IsDouble) goldBal = gv.AsDouble;
                        else if (gv.IsInt32) goldBal = gv.AsInt32;
                        else if (gv.IsInt64) goldBal = gv.AsInt64;
                    }

                    if (goldBal + 1e-6 < addedGold)
                        return Task.FromResult(Err(400, "не хватает голды"));
                }

                bool win = Rng.NextDouble() < probability;

                foreach (var row in stakeRows)
                {
                    if (!db.RemoveItemIfOwned(oid, row.slotId, row.defId))
                        return Task.FromResult(Err(409, "не удалось списать ставку"));
                }

                if (addedGold > 0)
                    db.CurrencyMinusValue(oid, GoldCurrencyId, addedGold);

                object? wonItem = null;
                string status = win ? "WON" : "LOST";

                if (win)
                {
                    int newSlot = db.AllocateNextInventoryItemId(oid);
                    var newInv = new BoltInventoryItem
                    {
                        itemDefinitionId = req.targetItemId,
                        quantity = 1,
                        flags = 0,
                        date = BsonDateTime.Create(DateTime.UtcNow),
                    };
                    db.AddItemToPlayerInventoryDocument(oid, newInv, newSlot);

                    string tName = targetDef.displayName ?? $"#{req.targetItemId}";
                    if (targetDef.IsStattrack()) tName += " (StatTrak)";

                    wonItem = new
                    {
                        instanceId = newSlot.ToString(),
                        id = req.targetItemId,
                        name = tName,
                        price = Math.Round(marketTargetPrice, 2),
                        image = SkinImageUrl(req.targetItemId),
                    };
                }

                double balanceAfter = 0;
                var invAfter = db.GetPlayerInventoryDocument(oid);
                if (invAfter?.Currencies != null && invAfter.Currencies.TryGetValue(GoldCurrencyId.ToString(), out var bal))
                {
                    if (bal.IsDouble) balanceAfter = bal.AsDouble;
                    else if (bal.IsInt32) balanceAfter = bal.AsInt32;
                    else if (bal.IsInt64) balanceAfter = bal.AsInt64;
                }

                var betItems = stakeRows.Select(r => new
                {
                    inventoryItemId = r.slotId.ToString(),
                    id = r.defId,
                    name = r.name,
                    price = r.price,
                    image = SkinImageUrl(r.defId),
                }).ToList();

                return Task.FromResult(Ok(new
                {
                    ok = true,
                    status,
                    probability,
                    balance = Math.Round(balanceAfter, 2),
                    wonItem,
                    betItems,
                    target = new
                    {
                        id = req.targetItemId,
                        price = Math.Round(marketTargetPrice, 2),
                        name = targetDef.displayName ?? $"#{req.targetItemId}",
                        image = SkinImageUrl(req.targetItemId),
                    },
                }));
            }
            catch (Exception ex)
            {
                try { Logger.LogWarn($"[MarketUpgraderApi] bet: {ex.Message}"); } catch { }
                return Task.FromResult(Err(500, ex.Message));
            }
        }
    }
}
