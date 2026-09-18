using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer.Helpers;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// Локальный админ-API поверх ТЕХ ЖЕ методов, которыми пользуются инлайн-кнопки бота.
    /// Ничего не дублирует: выдача идёт через DonateBotService (partial-мост),
    /// тумблеры — через BoltGameDatabaseProvider / GameWhitelist / AlliesMatchmakingConfig.
    /// Слушает только на localhost (HttpApiServer), наружу его проксирует сайт
    /// после проверки Telegram initData и списка админов бота.
    /// </summary>
    public static class AdminApi
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

        // ---------------------------------------------------------------- доступ

        private static string _cachedKey;
        private static readonly object KeyGate = new object();

        /// <summary>
        /// Общий ключ сайта и сервера: SHA-256 от токена бота. Порт 2224 открыт наружу,
        /// поэтому без ключа админ-маршруты не отвечают вообще. Сам токен по сети не ходит,
        /// и второй копии секрета не появляется — оба процесса читают local.settings.json.
        /// </summary>
        public static string SharedKey()
        {
            lock (KeyGate)
            {
                if (_cachedKey != null) return _cachedKey;
                string token = "";
                try
                {
                    string file = System.IO.Path.Combine(AppContext.BaseDirectory, "local.settings.json");
                    using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
                    if (doc.RootElement.TryGetProperty("TelegramBotToken", out var t))
                        token = t.GetString() ?? "";
                }
                catch { }
                if (string.IsNullOrEmpty(token)) { _cachedKey = ""; return _cachedKey; }
                using var sha = System.Security.Cryptography.SHA256.Create();
                var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
                _cachedKey = string.Concat(hash.Select(x => x.ToString("x2")));
                return _cachedKey;
            }
        }

        private static bool KeyOk(string given)
        {
            string expected = SharedKey();
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given)) return false;
            var a = System.Text.Encoding.UTF8.GetBytes(expected);
            var b = System.Text.Encoding.UTF8.GetBytes(given.Trim());
            if (a.Length != b.Length) return false;
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
        }

        /// <summary>Единая точка входа. path начинается с /api/admin/.</summary>
        public static Task<Result> HandleAsync(string method, string path, Func<string, string> query, string body)
        {
            try
            {
                string route = (path ?? "").ToLowerInvariant();
                JsonElement b = default;
                bool hasBody = false;
                if (!string.IsNullOrWhiteSpace(body))
                {
                    try { b = JsonDocument.Parse(body).RootElement; hasBody = true; }
                    catch { return Task.FromResult(Err(400, "bad json")); }
                }

                string Arg(string name)
                {
                    if (hasBody && b.ValueKind == JsonValueKind.Object && b.TryGetProperty(name, out var v))
                    {
                        if (v.ValueKind == JsonValueKind.String) return v.GetString();
                        if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
                        if (v.ValueKind == JsonValueKind.True) return "true";
                        if (v.ValueKind == JsonValueKind.False) return "false";
                    }
                    return query?.Invoke(name);
                }

                long ArgLong(string name, long fallback = 0)
                    => long.TryParse((Arg(name) ?? "").Trim(), out long n) ? n : fallback;

                if (!KeyOk(Arg("key")))
                    return Task.FromResult(Err(403, "нет доступа"));

                switch (route)
                {
                    case "/api/admin/player":
                        return Task.FromResult(PlayerCard(Arg("id")));

                    case "/api/admin/toggles":
                        return Task.FromResult(Toggles());

                    case "/api/admin/toggle":
                        if (method != "POST") return Task.FromResult(Err(405, "POST only"));
                        return Task.FromResult(SetToggle(Arg("name"), Arg("value")));

                    case "/api/admin/grant":
                        if (method != "POST") return Task.FromResult(Err(405, "POST only"));
                        return Task.FromResult(Grant(Arg("id"), Arg("kind"), ArgLong("amount", 1),
                                                     (int)ArgLong("itemId", 0), ArgLong("adminTg", 0)));

                    case "/api/admin/moderate":
                        if (method != "POST") return Task.FromResult(Err(405, "POST only"));
                        return Task.FromResult(Moderate(Arg("id"), Arg("action"), Arg("reason")));

                    case "/api/admin/customid":
                        if (method != "POST") return Task.FromResult(Err(405, "POST only"));
                        return Task.FromResult(CustomId(Arg("id"), Arg("newId")));

                    case "/api/admin/promo":
                        if (method != "POST") return Task.FromResult(Err(405, "POST only"));
                        return PromoAsync(Arg("id"), Arg("code"));

                    case "/api/admin/promo-create":
                        if (method != "POST") return Task.FromResult(Err(405, "POST only"));
                        return CreatePromoAsync(Arg("code"), (int)ArgLong("uses", 1),
                                                ArgLong("gold", 0), (int)ArgLong("spins", 0),
                                                (Arg("goldpass") ?? "").Trim().ToLowerInvariant() == "true",
                                                (int)ArgLong("itemId", 0), (int)ArgLong("amount", 1));

                    case "/api/admin/telegram-verify-start":
                        if (method != "POST") return Task.FromResult(Err(405, "POST only"));
                        return Task.FromResult(TelegramVerifyStart(ArgLong("telegramId", 0), Arg("id")));

                    case "/api/admin/telegram-verify-confirm":
                        if (method != "POST") return Task.FromResult(Err(405, "POST only"));
                        return Task.FromResult(TelegramVerifyConfirm(ArgLong("telegramId", 0), Arg("code")));
                }

                return Task.FromResult(Err(404, "unknown admin route"));
            }
            catch (Exception ex)
            {
                try { Logger.LogWarn($"[AdminApi] {path}: {ex.Message}"); } catch { }
                return Task.FromResult(Err(500, ex.Message));
            }
        }

        // ---------------------------------------------------------------- игрок

        /// <summary>Поиск по ObjectId / игровому UID / нику. Возвращает ник, аватар и карточку.</summary>
        private static Result PlayerCard(string idOrUid)
        {
            if (string.IsNullOrWhiteSpace(idOrUid)) return Err(400, "empty id");
            if (!DonateBotService.BridgeResolvePlayerId(idOrUid, out string pid, out string error))
                return Err(404, string.IsNullOrEmpty(error) ? "игрок не найден" : error);

            PlayerDocument doc = null;
            try { doc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(pid)); } catch { }
            if (doc == null) return Err(404, "игрок не найден");

            var db = BoltGameDatabaseProvider.Instance;
            int Stat(string key) { try { return (int)db.GetPlayerStat(pid, key); } catch { return 0; } }

            long gold = 0, silver = 0;
            string medalName = "";
            int medalId = 0;
            try
            {
                var inv = db.GetPlayerInventoryDocument(ObjectId.Parse(pid));
                if (inv?.Currencies != null)
                {
                    gold = ReadCurrency(inv.Currencies, "102");
                    silver = ReadCurrency(inv.Currencies, "101");
                }
                medalId = ReadEquippedMedalId(inv);
                if (medalId > 0)
                {
                    try
                    {
                        var def = InventoryCatalogueLoader.Instance.GetByKey(medalId);
                        medalName = string.IsNullOrWhiteSpace(def?.displayName)
                            ? $"Медаль #{medalId}"
                            : def.displayName;
                    }
                    catch { medalName = $"Медаль #{medalId}"; }
                }
            }
            catch { }

            int alliesRank = Stat("ranked_2v2_rank");
            int compRank = Stat("ranked_rank");

            int bpLevel = 1;
            int bpMax = 50;
            bool bpGold = false;
            try { bpGold = GameEventRemoteService.PlayerOwnsGoldPass(pid); } catch { }
            try
            {
                var collection = db.GetDatabase.GetCollection<BsonDocument>("game_event_progress");
                var filter = Builders<BsonDocument>.Filter.Eq("playerId", pid)
                    & Builders<BsonDocument>.Filter.Eq("eventId", "CURSED_SOULS");
                var existing = collection.Find(filter).FirstOrDefault();
                if (existing != null && existing.Contains("levels") && existing["levels"].IsBsonDocument)
                {
                    var lv = existing["levels"].AsBsonDocument;
                    if (lv.Contains("free")) bpLevel = Math.Max(bpLevel, lv["free"].ToInt32());
                    if (lv.Contains("premium")) bpLevel = Math.Max(bpLevel, lv["premium"].ToInt32());
                }
            }
            catch { }

            string avatar = null;
            if (!string.IsNullOrWhiteSpace(doc.avatarId))
            {
                try
                {
                    var arr = db.GetAvatars(new[] { doc.avatarId });
                    if (arr != null && arr.Length > 0 && arr[0]?.Avatar != null && arr[0].Avatar.Length > 0)
                        avatar = "data:image/png;base64," + Convert.ToBase64String(arr[0].Avatar.ToByteArray());
                }
                catch { }
            }

            bool online = false;
            try { online = StaticClasses.UserServices.ContainsKey(pid); } catch { }

            return Ok(new
            {
                ok = true,
                playerId = pid,
                uid = doc.uid,
                name = doc.name,
                avatar,
                online,
                banned = doc.isBanned,
                banReason = doc.banReason,
                muted = doc.isMuted,
                gold,
                silver,
                medal = medalId > 0 ? new { id = medalId, name = medalName } : null,
                level = Stat("level_id"),
                battlePass = new { level = bpLevel, max = bpMax, gold = bpGold },
                timeInGame = doc.timeInGame,
                allies = new
                {
                    rank = alliesRank,
                    rankName = SafeRankName(alliesRank),
                    mmr = Stat("ranked_2v2_current_mmr"),
                    wins = Stat("ranked_2v2_calibration_match_count"),
                    losses = Stat("allies_losses"),
                    kills = Stat("allies_kills"),
                    deaths = Stat("allies_deaths")
                },
                competitive = new
                {
                    rank = compRank,
                    rankName = SafeRankName(compRank),
                    mmr = Stat("ranked_current_mmr")
                }
            });
        }

        private static string SafeRankName(int rank)
        {
            try { return PlayerStatsManager.GetAlliesRankName(rank); }
            catch { return rank.ToString(); }
        }

        private static long ReadCurrency(BsonDocument currencies, string id)
        {
            if (currencies == null || !currencies.Contains(id)) return 0;
            try
            {
                var v = currencies[id];
                if (v.IsBsonDocument)
                {
                    var doc = v.AsBsonDocument;
                    if (doc.Contains("value")) return Convert.ToInt64(doc["value"].ToDouble());
                }
                return Convert.ToInt64(v.ToDouble());
            }
            catch { return 0; }
        }

        private static int ReadEquippedMedalId(PlayerInventoryDocument inv)
        {
            if (inv == null) return 0;
            int FromValue(BsonValue v)
            {
                if (v == null || v.IsBsonNull) return 0;
                try
                {
                    if (v.IsInt32 || v.IsInt64 || v.IsDouble) return Convert.ToInt32(v.ToDouble());
                    if (v.IsString && int.TryParse(v.AsString, out int parsed)) return parsed;
                    if (v.IsBsonDocument)
                    {
                        var d = v.AsBsonDocument;
                        if (d.Contains("itemDefinitionId")) return Convert.ToInt32(d["itemDefinitionId"].ToDouble());
                        if (d.Contains("id")) return Convert.ToInt32(d["id"].ToDouble());
                    }
                }
                catch { }
                return 0;
            }

            bool IsMedal(int defId) => defId >= 100 && defId <= 200;

            if (inv.equipedItems != null)
            {
                foreach (var el in inv.equipedItems.Elements)
                {
                    int defId = FromValue(el.Value);
                    if (IsMedal(defId)) return defId;
                }
            }

            if (inv.InventoryItems != null)
            {
                foreach (var el in inv.InventoryItems.Elements)
                {
                    if (!el.Value.IsBsonDocument) continue;
                    var d = el.Value.AsBsonDocument;
                    int defId = d.Contains("itemDefinitionId") ? Convert.ToInt32(d["itemDefinitionId"].ToDouble()) : 0;
                    int flags = d.Contains("flags") ? Convert.ToInt32(d["flags"].ToDouble()) : 0;
                    if (IsMedal(defId) && flags != 0) return defId;
                }
                foreach (var el in inv.InventoryItems.Elements)
                {
                    if (!el.Value.IsBsonDocument) continue;
                    var d = el.Value.AsBsonDocument;
                    int defId = d.Contains("itemDefinitionId") ? Convert.ToInt32(d["itemDefinitionId"].ToDouble()) : 0;
                    if (IsMedal(defId)) return defId;
                }
            }
            return 0;
        }

        // ---------------------------------------------------------------- выдача

        private static Result Grant(string idOrUid, string kind, long amount, int itemId, long adminTg)
        {
            if (!DonateBotService.BridgeResolvePlayerId(idOrUid, out string pid, out string error))
                return Err(404, string.IsNullOrEmpty(error) ? "игрок не найден" : error);

            kind = (kind ?? "").Trim().ToLowerInvariant();
            if (amount <= 0) amount = 1;

            switch (kind)
            {
                case "gold":
                    DonateBotService.BridgeGrantGold(pid, amount, adminTg);
                    return Ok(new { ok = true, playerId = pid, granted = $"{amount} gold" });

                case "goldpass":
                case "bp":
                    DonateBotService.BridgeGrantItem(pid, DonateBotService.BridgeGoldPassItemDefinitionId);
                    return Ok(new { ok = true, playerId = pid, granted = "Gold Pass" });

                case "levels":
                    if (!DonateBotService.BridgeAddBattlePassLevels(pid, (int)amount))
                        return Err(500, "не удалось добавить уровни");
                    return Ok(new { ok = true, playerId = pid, granted = $"+{amount} уровней BP" });

                case "spins":
                    DonateBotService.BridgeGrantItem(pid, DonateBotService.BridgeSpinTokenItemDefinitionId, (int)amount);
                    return Ok(new { ok = true, playerId = pid, granted = $"{amount} спинов" });

                case "item":
                    if (itemId <= 0) return Err(400, "itemId обязателен");
                    DonateBotService.BridgeGrantItem(pid, itemId, (int)amount);
                    return Ok(new { ok = true, playerId = pid, granted = $"предмет #{itemId} x{amount}" });
            }

            return Err(400, "kind: gold|goldpass|levels|spins|item");
        }

        private static Result CustomId(string idOrUid, string newId)
        {
            if (string.IsNullOrWhiteSpace(newId)) return Err(400, "newId обязателен");
            if (!DonateBotService.BridgeResolvePlayerId(idOrUid, out string pid, out string error))
                return Err(404, string.IsNullOrEmpty(error) ? "игрок не найден" : error);
            if (!DonateBotService.BridgeSetCustomId(pid, newId.Trim(), out string reason))
                return Err(400, string.IsNullOrEmpty(reason) ? "не удалось сменить id" : reason);
            return Ok(new { ok = true, playerId = pid, uid = newId.Trim() });
        }

        // ---------------------------------------------------------------- модерация

        private static Result Moderate(string idOrUid, string action, string reason)
        {
            if (!DonateBotService.BridgeResolvePlayerId(idOrUid, out string pid, out string error))
                return Err(404, string.IsNullOrEmpty(error) ? "игрок не найден" : error);

            action = (action ?? "").Trim().ToLowerInvariant();
            var oid = ObjectId.Parse(pid);

            switch (action)
            {
                case "ban":
                {
                    string why = string.IsNullOrWhiteSpace(reason) ? "Banned by admin" : reason.Trim();
                    BoltMainDatabaseProvider.Instance.BanPlayer(oid, why, 1);
                    try
                    {
                        var doc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);
                        StaticClasses.KickWithBan(pid, why, 1, doc?.uid ?? pid);
                    }
                    catch { }
                    return Ok(new { ok = true, playerId = pid, action = "ban", reason = why });
                }

                case "unban":
                    BoltMainDatabaseProvider.Instance.UnbanPlayer(oid);
                    return Ok(new { ok = true, playerId = pid, action = "unban" });

                case "kick":
                {
                    bool online = false;
                    try
                    {
                        if (StaticClasses.UserServices.TryGetValue(pid, out var us))
                        {
                            us.ForceDisconnect();
                            online = true;
                        }
                    }
                    catch { }
                    return Ok(new { ok = true, playerId = pid, action = "kick", online });
                }
            }

            return Err(400, "action: ban|unban|kick");
        }

        // ---------------------------------------------------------------- тумблеры

        private static Result Toggles()
        {
            var db = BoltGameDatabaseProvider.Instance;
            bool marketClosed = false, arcane = false, spinEnabled = false, whitelist = false;
            long spinStart = 0, spinEnd = 0;
            int alliesPlayers = 2;

            try { marketClosed = db.GetMarketClosed(); } catch { }
            try { arcane = db.GetArcaneBoost(); } catch { }
            try
            {
                var s = db.GetSpinAdminSettings();
                spinEnabled = s.enabled; spinStart = s.startUnixMs; spinEnd = s.endUnixMs;
            }
            catch { }
            try { whitelist = GameWhitelist.IsEnabled(); } catch { }
            try { AlliesMatchmakingConfig.Refresh(); alliesPlayers = AlliesMatchmakingConfig.GetRequiredPlayers(); } catch { }
            int compPlayers = CompetitiveMatchmakingConfig.Full;
            try { CompetitiveMatchmakingConfig.Refresh(); compPlayers = CompetitiveMatchmakingConfig.GetRequiredPlayers(); } catch { }

            return Ok(new
            {
                ok = true,
                marketClosed,
                arcane,
                spin = new { enabled = spinEnabled, startUnixMs = spinStart, endUnixMs = spinEnd },
                whitelist,
                alliesRequiredPlayers = alliesPlayers,
                competitiveRequiredPlayers = compPlayers
            });
        }

        private static Result SetToggle(string name, string value)
        {
            var db = BoltGameDatabaseProvider.Instance;
            name = (name ?? "").Trim().ToLowerInvariant();
            string v = (value ?? "").Trim().ToLowerInvariant();
            bool? want = v == "true" || v == "1" || v == "on" ? true
                       : v == "false" || v == "0" || v == "off" ? false
                       : (bool?)null;

            switch (name)
            {
                case "market":
                {
                    // value = "closed"/"open" тоже поддерживаем
                    bool next = v == "closed" ? true : v == "open" ? false
                              : want ?? !db.GetMarketClosed();
                    db.SetMarketClosed(next, next ? "Рынок закрыт на учёт администратором" : null);
                    try { MarketplaceRemoteService.ForcePushMarketStateNow(); } catch { }
                    return Ok(new { ok = true, name = "market", marketClosed = next });
                }

                case "arcane":
                {
                    bool next = want ?? !db.GetArcaneBoost();
                    db.SetArcaneBoost(next);
                    return Ok(new { ok = true, name = "arcane", arcane = next });
                }

                case "spin":
                {
                    var s = db.GetSpinAdminSettings();
                    bool next = want ?? !s.enabled;
                    db.SetSpinAdminSettings(next, s.startUnixMs, s.endUnixMs);
                    return Ok(new { ok = true, name = "spin", spin = next });
                }

                case "whitelist":
                {
                    bool next = want ?? !GameWhitelist.IsEnabled();
                    GameWhitelist.SetEnabled(next);
                    return Ok(new { ok = true, name = "whitelist", whitelist = next });
                }

                case "allies":
                {
                    int count = int.TryParse(v, out int n) ? n : AlliesMatchmakingConfig.GetRequiredPlayers();
                    AlliesMatchmakingConfig.SetRequiredPlayers(count);
                    return Ok(new { ok = true, name = "allies", alliesRequiredPlayers = AlliesMatchmakingConfig.GetRequiredPlayers() });
                }

                case "competitive":
                {
                    int count = int.TryParse(v, out int cn) ? cn : CompetitiveMatchmakingConfig.GetRequiredPlayers();
                    CompetitiveMatchmakingConfig.SetRequiredPlayers(count);
                    return Ok(new
                    {
                        ok = true,
                        name = "competitive",
                        competitiveRequiredPlayers = CompetitiveMatchmakingConfig.GetRequiredPlayers()
                    });
                }
            }

            return Err(400, "name: market|arcane|spin|whitelist|allies|competitive");
        }

        // ---------------------------------------------------------------- промокод

        /// <summary>
        /// Создание промокода. Тот же CreateCoupon, которым пользуется мастер промокодов
        /// в боте, — формат наград и правила активаций общие, расходиться нечему.
        /// </summary>
        private static async Task<Result> CreatePromoAsync(
            string code, int uses, long gold, int spins, bool goldPass, int itemId, int amount)
        {
            code = (code ?? "").Trim();
            if (string.IsNullOrEmpty(code)) return Err(400, "Введи код промокода");
            if (code.Length > 32) return Err(400, "Код слишком длинный (до 32 символов)");
            if (uses <= 0) uses = 1;
            if (amount <= 0) amount = 1;

            var items = new List<Tuple<int, int>>();
            var currencies = new List<Tuple<int, int>>();
            var parts = new List<string>();

            if (gold > 0)
            {
                // CreateCoupon принимает int — больше двух миллиардов голды одним кодом не выдать.
                if (gold > int.MaxValue) return Err(400, "Слишком много голды для одного кода");
                currencies.Add(Tuple.Create(102, (int)gold));
                parts.Add($"{gold} голды");
            }
            if (goldPass)
            {
                items.Add(Tuple.Create(DonateBotService.BridgeGoldPassItemDefinitionId, 1));
                parts.Add("Gold Pass");
            }
            if (spins > 0)
            {
                items.Add(Tuple.Create(DonateBotService.BridgeSpinTokenItemDefinitionId, spins));
                parts.Add($"{spins} спинов");
            }
            if (itemId > 0)
            {
                items.Add(Tuple.Create(itemId, amount));
                parts.Add($"предмет #{itemId} x{amount}");
            }

            if (items.Count == 0 && currencies.Count == 0)
                return Err(400, "Промокод пустой — добавь голду, пропуск, спины или предмет");

            try
            {
                var existing = await BoltGameDatabaseProvider.Instance.GetCouponDocument(code);
                if (existing != null) return Err(409, $"Промокод {code} уже существует");
            }
            catch { }

            try
            {
                await BoltGameDatabaseProvider.Instance.CreateCoupon(code, uses, items, currencies);
            }
            catch (Exception ex)
            {
                Logger.LogWarn($"[AdminApi] промокод {code} не создан: {ex.Message}");
                return Err(500, "Не удалось создать промокод: " + ex.Message);
            }

            string reward = string.Join(", ", parts);
            Logger.Log($"[AdminApi] создан промокод {code}: активаций={uses}, награда={reward}");
            return Ok(new { ok = true, code, uses, reward });
        }

        private static async Task<Result> PromoAsync(string idOrUid, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return Err(400, "Введи промокод");
            if (!DonateBotService.BridgeResolvePlayerId(idOrUid, out string pid, out string error))
                return Err(404, string.IsNullOrEmpty(error) ? "игрок не найден" : error);

            try
            {
                // Тот же метод, что вызывает игра из инвентаря: награда начисляется
                // там же и теми же правилами, второй реализации промокодов нет.
                var resp = await BoltGameDatabaseProvider.Instance.ActivateCoupon(code.Trim(), pid);
                if (resp == null) return Err(400, "Промокод не сработал");

                var parts = new List<string>();
                try
                {
                    foreach (var cur in resp.Currencies)
                        parts.Add(cur.CurrencyId == 102
                            ? $"{(long)cur.Value} голды"
                            : $"валюта #{cur.CurrencyId}: {(long)cur.Value}");
                    int items = resp.InventoryItems.Count;
                    if (items > 0) parts.Add(items == 1 ? "1 предмет" : $"{items} предметов");
                }
                catch { }

                string reward = parts.Count > 0 ? string.Join(", ", parts) : "награда начислена";
                Logger.Log($"[AdminApi] промокод {code.Trim()} -> {pid}: {reward}");
                return Ok(new { ok = true, playerId = pid, code = code.Trim(), reward });
            }
            catch (Helpers.CouponHasAlreadyActivatedRpcException)
            {
                return Err(400, "Этот промокод уже активирован на твоём аккаунте");
            }
            catch (Helpers.ActiveCouponNotFoundRpcException)
            {
                return Err(404, "Промокод не найден или все активации уже разобрали");
            }
            catch (Exception ex)
            {
                // Сообщение типизированного RPC-исключения бывает пустым — тогда
                // игроку уходит хотя бы понятная общая фраза, а точность в логе.
                Logger.LogWarn($"[AdminApi] промокод {code.Trim()} -> {pid}: {ex.GetType().Name} {ex.Message}");
                return Err(400, string.IsNullOrWhiteSpace(ex.Message)
                    ? "Промокод не сработал"
                    : ex.Message);
            }
        }

        private static Result TelegramVerifyStart(long telegramId, string idOrUid)
        {
            if (telegramId <= 0) return Err(400, "telegramId");
            if (string.IsNullOrWhiteSpace(idOrUid)) return Err(400, "укажи игровой ID");
            if (!DonateBotService.BridgeVerifyStart(telegramId, idOrUid.Trim(), out string message, out string error))
                return Err(400, string.IsNullOrEmpty(error) ? "не удалось отправить код" : error);
            return Ok(new { ok = true, message });
        }

        private static Result TelegramVerifyConfirm(long telegramId, string code)
        {
            if (telegramId <= 0) return Err(400, "telegramId");
            if (string.IsNullOrWhiteSpace(code)) return Err(400, "введи код");
            if (!DonateBotService.BridgeVerifyConfirm(telegramId, code.Trim(), out string playerId, out string uid, out string error))
                return Err(400, string.IsNullOrEmpty(error) ? "код не принят" : error);
            return Ok(new { ok = true, playerId, uid, linked = true });
        }
    }
}
