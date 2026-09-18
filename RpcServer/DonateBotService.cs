using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types.Payments;
using MongoDB.Bson;
using MongoDB.Driver;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer.Api;

namespace StandRiseServer.RpcServer
{
    // Отдельный донат-бот (не путать с TelegramBotService - тот админский/мультифункциональный).
    // Продаёт скины (по конкретному предмету, цена зависит от коллекции+редкости), уровни/голд
    // пасс баттлпасса и Custom ID - всё исключительно за Telegram Stars (валюта "XTR").
    // Игрок один раз привязывает свой playerId к чату, дальше покупки идут сразу на его аккаунт.
    public partial class DonateBotService
    {
        private ITelegramBotClient _botClient;
        private CancellationTokenSource _cts;
        private string _usersFile;
        private List<long> _adminIds = new();

        private readonly ConcurrentDictionary<long, string> _linkedPlayerId = new();
        private readonly ConcurrentDictionary<long, DonateState> _state = new();
        private readonly ConcurrentDictionary<long, (CollectionId col, SkinValue rar, int page)> _browseContext = new();
        private readonly ConcurrentDictionary<long, byte> _hasPassed = new();
        // Админка: бесплатные цены (вкл по умолчанию для админов после unlock)
        private readonly ConcurrentDictionary<long, bool> _adminFreePrices = new();
        private readonly ConcurrentDictionary<long, string> _adminTargetUid = new();
        private const string AccessPassword = "emar10223!";
        private const string AccessPasswordAlt = "donate";
        private const string AccessPasswordLegacy = "eliseyy22!";
        private static readonly string PassFile = "donate_bot_pass_users.txt";

        private enum DonateState
        {
            None,
            WaitingPlayerId,
            WaitingRelinkPlayerId,
            WaitingVerifyCode,
            WaitingCustomBpLevels,
            WaitingCustomIdNew,
            WaitingCustomSpins,
            WaitingAdminGiveGoldUid,
            WaitingAdminGiveGoldAmount,
            WaitingAdminGiveSpinsUid,
            WaitingAdminGiveSpinsAmount,
            WaitingAdminGiveLevelsUid,
            WaitingAdminGiveLevelsAmount,
            WaitingAdminGiveBpUid,
            WaitingPromoCode,
            WaitingQuickPromo,
            WaitingPromoMaxUses,
            WaitingPromoGoldAmount,
            WaitingPromoSpinsAmount,
            WaitingPromoItemId,
            WaitingPromoItemQty,
            WaitingPromoSearch,
            WaitingWhitelistAdd,
            WaitingWhitelistRemove,
            WaitingMarketWhitelistAdd,
            WaitingMarketWhitelistRemove,
            WaitingSpinStartMs,
            WaitingSpinEndMs,
            WaitingAdminUserUid,
            WaitingAdminBanReason,
            WaitingAdminClanTag,
            WaitingAdminClanNewTag,
            WaitingAdminClanNewName,
            WaitingAdminSetLevelUid,
            WaitingAdminSetLevelValue,
            WaitingAdminGiveItemUid,
            WaitingAdminGiveItemDef,
            WaitingAdminKickUid,
            WaitingAdminRankMmr,
            WaitingAdminRankWins,
            WaitingAdminRankKd,
            WaitingAdminMmrAdjust,
            WaitingAdminCustomId
        }

        private readonly ConcurrentDictionary<long, (string code, int maxUses)> _promoDraft = new();
        private readonly ConcurrentDictionary<long, string> _promoRewardKind = new(); // gold|spins|goldpass|item
        private readonly ConcurrentDictionary<long, List<Tuple<int, int>>> _promoItemDraft = new(); // defId,qty
        // Голда в черновике промокода. Раньше награда была ОДНА на промокод:
        // выбрал голду — промокод сразу создавался, добавить скин было уже нельзя.
        // Теперь черновик работает как корзина: голда + любое число предметов вместе.
        private readonly ConcurrentDictionary<long, int> _promoGoldDraft = new();
        // Последний поисковый запрос по каталогу — чтобы листать страницы результатов.
        private readonly ConcurrentDictionary<long, string> _promoSearchQuery = new();
        private readonly ConcurrentDictionary<long, int> _promoPendingItemDef = new();

        // ---------- Цены (только звёзды - обновлено 07.09.2026, снижены) ----------
        private const int CustomIdPriceStars = 1;
        // Цены теперь в одном файле на бота и сайт — C:\StandRise\Web\catalog.json.
        // Константы ниже остались только как запасной вариант внутри ShopPrices.
        private static int GoldPassPriceStars => ShopPrices.GoldPassStars();
        private const int GoldPassItemDefinitionId = 608; // CursedSoulsGoldPass (0.17.0)
        private const int TestItemPriceStars = 1; // тестовая кнопка для проверки оплаты, видна только админам
        private const int TestItemDefinitionId = 100; // "Bronze" Assistance Medal - безобидный, легко узнать в инвентаре
        private static readonly int[] BpLevelPresets = { 1, 10, 20, 30, 180 };
        private const int StarsPerSpin = 1;
        private static readonly int[] SpinsPresets = { 1, 5, 10, 20, 30 };
        // Cursed Souls / Halloween2021_Spin
        private const int SpinTokenItemDefinitionId = 201;
        private const string ChannelUsername = "@StandRework";
        private static string ShopMiniAppUrl => _shopMiniAppUrl ??= LoadShopMiniAppUrl();
        private static string _shopMiniAppUrl;

        private static string LoadShopMiniAppUrl()
        {
            try
            {
                string path = @"C:\StandRise\Web\web.settings.json";
                if (!System.IO.File.Exists(path))
                    path = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Web", "web.settings.json");
                if (System.IO.File.Exists(path))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("PublicUrl", out var u))
                    {
                        string url = u.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(url)) return url;
                    }
                }
            }
            catch { }
            return "https://t.me/StandReworkBot/shop";
        }
        private readonly ConcurrentDictionary<long, byte> _channelOk = new();

        private static readonly Dictionary<CollectionId, string> CollectionNames = new()
        {
            { CollectionId.Event2Years, "2 Years" },
            { CollectionId.Origin, "Origin" },
            { CollectionId.Assistance, "Assistance" },
            { CollectionId.Competitive, "Competitive" },
            { CollectionId.Furious, "Furious" },
            { CollectionId.NewYear2020, "New Year 2020" },
            { CollectionId.Project_Z9, "Project Z9" },
            { CollectionId.Revival, "Revival" },
            { CollectionId.Halloween_2020, "Halloween 2020" },
            { CollectionId.New_Year_2021, "New Year 2021" },
            { CollectionId.Event4Years, "4 Years" },
            { CollectionId.Travelers_Bag, "Travelers Bag" },
            { CollectionId.Dragon_Rise, "Dragon Rise" },
            { CollectionId.hot_winter_party_2023, "Cursed Souls" },
            { CollectionId.Halloween_2019, "Halloween 2019" },
            { CollectionId.Fable, "Fable" },
            { CollectionId.Rival, "Rival" },
            { CollectionId.Scorpion, "Scorpion" },
            { CollectionId.Rainbow, "Rainbow" },
            { CollectionId.Empire, "Empire" },
            { CollectionId.Nameless, "Nameless" },
            { (CollectionId)1001, "5 Years" },
            { (CollectionId)1002, "Legends" },
            { (CollectionId)1003, "Project Pandora" },
            { (CollectionId)1004, "Hot Winter Party" },
        };

        private static readonly Dictionary<CollectionId, Dictionary<SkinValue, int>> CollectionPricesStars = new()
        {
            { CollectionId.Event2Years, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 1 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Origin, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 1 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Assistance, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 2 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Competitive, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 1 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Furious, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 1 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 2 } } },
            { CollectionId.NewYear2020, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 2 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Project_Z9, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 2 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Revival, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 2 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Halloween_2020, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 1 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.New_Year_2021, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 2 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Event4Years, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 2 }, { SkinValue.Legendary, 2 } } },
            { CollectionId.Travelers_Bag, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 2 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Dragon_Rise, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 2 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.hot_winter_party_2023, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 2 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Halloween_2019, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 2 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Fable, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 1 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Rival, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 1 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Scorpion, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 1 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Rainbow, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 1 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Empire, new() { { SkinValue.Rare, 1 }, { SkinValue.Epic, 1 }, { SkinValue.Legendary, 2 }, { SkinValue.Arcane, 3 } } },
            { CollectionId.Nameless, new() { { SkinValue.Rare, 2 }, { SkinValue.Epic, 3 }, { SkinValue.Legendary, 4 }, { SkinValue.Arcane, 5 } } },
        };

        // ---------- Bootstrap ----------

        public void Start(string token, List<string> adminUsernames = null, List<long> adminIds = null)
        {
            string tokenPrefix = token.Substring(0, Math.Min(8, token.Length));
            _usersFile = System.IO.Path.Combine(@"C:\StandRise", $"donate_bot_users_{tokenPrefix}.json");
            _adminIds = adminIds ?? new List<long>();
            LoadLinkedPlayers();
            MergeLinkedPlayersFrom(System.IO.Path.Combine(AppContext.BaseDirectory, $"donate_bot_users_{tokenPrefix}.json"));
            LoadPassedUsers();

            _botClient = new TelegramBotClient(token);
            _cts = new CancellationTokenSource();

            try
            {
                // Если висит webhook — long-polling не получает апдейты → /pass «молчит».
                _botClient.DeleteWebhook(dropPendingUpdates: true).GetAwaiter().GetResult();
                Console.WriteLine("[DonateBot] Webhook cleared, starting polling...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DonateBot] DeleteWebhook warn: {ex.Message}");
            }

            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandlePollingErrorAsync,
                new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
                _cts.Token
            );

            try
            {
                var me = _botClient.GetMe().GetAwaiter().GetResult();
                Console.WriteLine($"[DonateBot] OK @{me.Username} id={me.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DonateBot] getMe failed: {ex.Message}");
            }

            OnBotStarted(token);
        }

        partial void OnBotStarted(string token);

        private void LoadPassedUsers()
        {
            try
            {
                if (!System.IO.File.Exists(PassFile)) return;
                foreach (var line in System.IO.File.ReadAllLines(PassFile))
                {
                    if (long.TryParse(line.Trim(), out long id))
                    {
                        _hasPassed[id] = 0;
                        _adminUnlockedByPass[id] = true;
                    }
                }
            }
            catch { }
        }

        private void SavePassedUsers()
        {
            try { System.IO.File.WriteAllLines(PassFile, _hasPassed.Keys.Select(k => k.ToString())); } catch { }
        }

        private async Task<bool> EnsurePassedAsync(ITelegramBotClient botClient, long userId, long chatId, string text, CancellationToken ct)
        {
            string raw = (text ?? "").Trim();
            string normalized = raw;
            // "/pass@BotName emar10223!" → "/pass emar10223!"
            if (normalized.StartsWith("/pass@", StringComparison.OrdinalIgnoreCase))
            {
                int sp = normalized.IndexOf(' ');
                normalized = sp > 0 ? "/pass " + normalized.Substring(sp + 1).Trim() : "/pass";
            }
            else if (normalized.StartsWith("/pass ", StringComparison.OrdinalIgnoreCase) == false
                     && normalized.StartsWith("/pass", StringComparison.OrdinalIgnoreCase)
                     && normalized.Length > 5 && normalized[5] != ' ')
            {
                // уже нормализовано выше в HandleUpdate
            }

            bool isPassCmd = normalized.StartsWith("/pass", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(AccessPassword, StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(AccessPasswordAlt, StringComparison.OrdinalIgnoreCase);

            // ВАЖНО: для /pass НИКОГДА не делаем silent return — иначе «не реагирует».
            if (!isPassCmd)
            {
                if (userId != 0 && IsAdmin(userId))
                {
                    _hasPassed[userId] = 0;
                    if (!_adminFreePrices.ContainsKey(userId))
                        _adminFreePrices[userId] = true;
                    return true;
                }
                if (userId != 0 && _hasPassed.ContainsKey(userId)) return true;
            }

            if (normalized.Equals("/pass " + AccessPassword, StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("/pass " + AccessPasswordAlt, StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("/pass " + AccessPasswordLegacy, StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(AccessPassword, StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(AccessPasswordAlt, StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(AccessPasswordLegacy, StringComparison.OrdinalIgnoreCase))
            {
                _hasPassed[userId] = 0;
                _adminUnlockedByPass[userId] = true;
                if (!_adminFreePrices.ContainsKey(userId))
                    _adminFreePrices[userId] = true;
                SavePassedUsers();
                Console.WriteLine($"[DonateBot] PASS OK user={userId}");
                try
                {
                    await botClient.SendMessage(chatId, "Админка открыта. Бесплатные цены ВКЛ.", cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DonateBot] pass reply failed: {ex.Message}");
                }
                if (_linkedPlayerId.ContainsKey(chatId))
                    await ShowAdminMenu(botClient, chatId, userId, ct);
                else
                {
                    _state[chatId] = DonateState.WaitingPlayerId;
                    await botClient.SendMessage(chatId, "Пришли игровой UID для привязки.", cancellationToken: ct);
                }
                return true;
            }

            if (normalized.Equals("/pass", StringComparison.OrdinalIgnoreCase))
            {
                if (IsAdmin(userId) || _adminIds.Contains(userId) || _adminUnlockedByPass.ContainsKey(userId))
                {
                    _hasPassed[userId] = 0;
                    if (!_adminFreePrices.ContainsKey(userId))
                        _adminFreePrices[userId] = true;
                    await ShowAdminMenu(botClient, chatId, userId, ct);
                    return true;
                }
                await botClient.SendMessage(chatId, "Нужен пароль: /pass emar10223!", cancellationToken: ct);
                return false;
            }

            if (normalized.StartsWith("/pass ", StringComparison.OrdinalIgnoreCase))
            {
                await botClient.SendMessage(chatId, "Неверный пароль. Формат: /pass emar10223!", cancellationToken: ct);
                return false;
            }

            await botClient.SendMessage(chatId, "Админ: /pass emar10223!\nМагазин: /start", cancellationToken: ct);
            return false;
        }

        public static void StartStatic(string token, List<string> adminUsernames, List<long> adminIds)
        {
            var service = new DonateBotService();
            service.Start(token, adminUsernames, adminIds);
        }

        private void LoadLinkedPlayers()
        {
            try
            {
                if (!System.IO.File.Exists(_usersFile)) return;
                string json = System.IO.File.ReadAllText(_usersFile);
                var dict = JsonSerializer.Deserialize<Dictionary<long, string>>(json);
                if (dict == null) return;
                foreach (var kv in dict) _linkedPlayerId[kv.Key] = kv.Value;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DonateBot] LoadLinkedPlayers error: {ex.Message}");
            }
        }

        private void MergeLinkedPlayersFrom(string alternatePath)
        {
            try
            {
                if (string.IsNullOrEmpty(alternatePath)
                    || string.Equals(alternatePath, _usersFile, StringComparison.OrdinalIgnoreCase)
                    || !System.IO.File.Exists(alternatePath))
                    return;
                var dict = JsonSerializer.Deserialize<Dictionary<long, string>>(
                    System.IO.File.ReadAllText(alternatePath));
                if (dict == null || dict.Count == 0) return;
                bool changed = false;
                foreach (var kv in dict)
                {
                    if (!_linkedPlayerId.TryGetValue(kv.Key, out string existing)
                        || !string.Equals(existing, kv.Value, StringComparison.Ordinal))
                    {
                        _linkedPlayerId[kv.Key] = kv.Value;
                        changed = true;
                    }
                }
                if (changed) SaveLinkedPlayers();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DonateBot] MergeLinkedPlayersFrom error: {ex.Message}");
            }
        }

        private void SaveLinkedPlayers()
        {
            try
            {
                string json = JsonSerializer.Serialize(_linkedPlayerId.ToDictionary(k => k.Key, v => v.Value));
                System.IO.File.WriteAllText(_usersFile, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DonateBot] SaveLinkedPlayers error: {ex.Message}");
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
        {
            Console.WriteLine($"[DonateBot] Polling error: {exception.Message}");
            return Task.CompletedTask;
        }

        // ---------- Update routing ----------

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
        {
            try
            {
                if (update.PreCheckoutQuery != null)
                {
                    await botClient.AnswerPreCheckoutQuery(update.PreCheckoutQuery.Id, cancellationToken: ct);
                    return;
                }

                if (update.Message?.SuccessfulPayment != null)
                {
                    await HandleSuccessfulPayment(botClient, update.Message, ct);
                    return;
                }

                if (update.Message != null)
                {
                    long userId = update.Message.From?.Id ?? 0;
                    long chatId = update.Message.Chat.Id;
                    string text = update.Message.Text ?? "";
                    string norm = (text ?? "").Trim();
                    // BotCommand: "/pass@BotName args" → нормализуем
                    if (norm.StartsWith("/", StringComparison.Ordinal))
                    {
                        int at = norm.IndexOf('@');
                        int sp = norm.IndexOf(' ');
                        if (at > 0 && (sp < 0 || at < sp))
                        {
                            string cmd = norm.Substring(1, at - 1);
                            string rest = sp > 0 ? norm.Substring(sp).Trim() : "";
                            norm = string.IsNullOrEmpty(rest) ? "/" + cmd : "/" + cmd + " " + rest;
                            text = norm;
                        }
                    }

                    Console.WriteLine($"[DonateBot] msg user={userId} chat={chatId} text='{text}' admin={IsAdmin(userId)}");

                    if (norm.StartsWith("/pass", StringComparison.OrdinalIgnoreCase)
                        || norm.Equals(AccessPassword, StringComparison.OrdinalIgnoreCase)
                        || norm.Equals(AccessPasswordAlt, StringComparison.OrdinalIgnoreCase))
                    {
                        await EnsurePassedAsync(botClient, userId, chatId, text, ct);
                        return; // не уходим в HandleMessage (иначе /pass парсится как UID)
                    }

                    if (IsAdmin(userId))
                    {
                        _hasPassed[userId] = 0;
                        if (!_adminFreePrices.ContainsKey(userId))
                            _adminFreePrices[userId] = true;
                    }
                    await HandleMessage(botClient, update.Message, ct);
                    return;
                }

                if (update.CallbackQuery != null)
                {
                    long userId = update.CallbackQuery.From?.Id ?? 0;
                    long chatId = update.CallbackQuery.Message?.Chat.Id ?? 0;
                    if (IsAdmin(userId))
                    {
                        _hasPassed[userId] = 0;
                        if (!_adminFreePrices.ContainsKey(userId))
                            _adminFreePrices[userId] = true;
                    }
                    await HandleCallback(botClient, update.CallbackQuery, ct);
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DonateBot] HandleUpdate error: {ex}");
            }
        }

        private async Task HandleMessage(ITelegramBotClient botClient, Message message, CancellationToken ct)
        {
            long chatId = message.Chat.Id;
            long userId = message.From?.Id ?? 0;
            string text = message.Text?.Trim() ?? "";

            if (text == "/start" || text.StartsWith("/start@", StringComparison.OrdinalIgnoreCase) || text.StartsWith("/start ", StringComparison.OrdinalIgnoreCase))
            {
                if (IsAdmin(userId))
                {
                    _hasPassed[userId] = 0;
                    if (!_adminFreePrices.ContainsKey(userId))
                        _adminFreePrices[userId] = true;
                }
                await ShowSubscribeOrShop(botClient, chatId, userId, ct);
                return;
            }

            if (text.StartsWith("/pass", StringComparison.OrdinalIgnoreCase)
                || text.Equals(AccessPassword, StringComparison.OrdinalIgnoreCase)
                || text.Equals(AccessPasswordAlt, StringComparison.OrdinalIgnoreCase))
            {
                await EnsurePassedAsync(botClient, userId, chatId, text, ct);
                return;
            }

            if (text.Equals("/admin", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAdmin(userId))
                {
                    await botClient.SendMessage(chatId, "Нет доступа. Сначала: /pass emar10223!", cancellationToken: ct);
                    return;
                }
                await ShowAdminMenu(botClient, chatId, userId, ct);
                return;
            }

            var state = _state.GetValueOrDefault(chatId, DonateState.None);

            switch (state)
            {
                case DonateState.WaitingPlayerId:
                case DonateState.WaitingRelinkPlayerId:
                    await TryLinkPlayer(botClient, chatId, text, ct, userId);
                    break;

                case DonateState.WaitingVerifyCode:
                    await TryConfirmVerifyCode(botClient, chatId, text, ct, userId);
                    break;

                case DonateState.WaitingCustomBpLevels:
                    if (int.TryParse(text, out int customLevels) && customLevels > 0 && customLevels <= 100000)
                    {
                        _state[chatId] = DonateState.None;
                        await SendLevelsInvoice(botClient, chatId, customLevels, ct, userId);
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "Введи целое число уровней (1-100000):", cancellationToken: ct);
                    }
                    break;

                case DonateState.WaitingCustomIdNew:
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 3 || text.Length > 20)
                    {
                        await botClient.SendMessage(chatId, "Custom ID должен быть от 3 до 20 символов. Введи ещё раз:", cancellationToken: ct);
                    }
                    else
                    {
                        _state[chatId] = DonateState.None;
                        await SendCustomIdInvoice(botClient, chatId, text, ct, userId);
                    }
                    break;

                case DonateState.WaitingCustomSpins:
                    if (int.TryParse(text, out int customSpins) && customSpins > 0 && customSpins <= 10000)
                    {
                        _state[chatId] = DonateState.None;
                        await SendSpinsInvoice(botClient, chatId, customSpins, ct, userId);
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "Введи целое число спинов (1-10000):", cancellationToken: ct);
                    }
                    break;

                case DonateState.WaitingAdminGiveBpUid:
                    {
                        if (TryResolvePlayerId(text, out string bpPid, out string bpErr))
                        {
                            _state[chatId] = DonateState.None;
                            try
                            {
                                GrantItem(bpPid, GoldPassItemDefinitionId);
                                await botClient.SendMessage(chatId, $"Gold Pass (#608) выдан игроку {text} ({bpPid}). Перезайди в игру.", cancellationToken: ct);
                            }
                            catch (Exception ex)
                            {
                                await botClient.SendMessage(chatId, $"Ошибка: {ex.Message}", cancellationToken: ct);
                            }
                        }
                        else
                        {
                            _state[chatId] = DonateState.WaitingAdminGiveBpUid;
                            await botClient.SendMessage(chatId, $"Не найден: {bpErr}\nВведи UID ещё раз:", cancellationToken: ct);
                        }
                        break;
                    }

                case DonateState.WaitingAdminGiveGoldUid:
                    _adminTargetUid[chatId] = text;
                    _state[chatId] = DonateState.WaitingAdminGiveGoldAmount;
                    await botClient.SendMessage(chatId, "Сколько голды выдать?", cancellationToken: ct);
                    break;

                case DonateState.WaitingAdminGiveGoldAmount:
                    {
                        // Без верхнего лимита: long, только > 0.
                        if (!long.TryParse(text, out long goldAmt) || goldAmt <= 0)
                        {
                            _state[chatId] = DonateState.WaitingAdminGiveGoldAmount;
                            await botClient.SendMessage(chatId, "Некорректное число (нужно целое > 0). Введи сумму ещё раз:", cancellationToken: ct);
                            break;
                        }
                        string uid = _adminTargetUid.GetValueOrDefault(chatId, "");
                        if (TryResolvePlayerId(uid, out string goldPid, out string goldErr))
                        {
                            _state[chatId] = DonateState.None;
                            GrantGold(goldPid, goldAmt, adminTgId: chatId);
                            await botClient.SendMessage(chatId, $"Выдано {goldAmt} голды игроку {uid} ({goldPid}).", cancellationToken: ct);
                            string hist = FormatGoldGrantHistory();
                            if (!string.IsNullOrEmpty(hist))
                                await botClient.SendMessage(chatId, "Последние выдачи:\n" + hist, cancellationToken: ct);
                        }
                        else
                        {
                            _state[chatId] = DonateState.WaitingAdminGiveGoldAmount;
                            await botClient.SendMessage(chatId, $"Не найден: {goldErr}\nВведи сумму ещё раз (UID уже сохранён):", cancellationToken: ct);
                        }
                        break;
                    }

                case DonateState.WaitingAdminGiveSpinsUid:
                    _adminTargetUid[chatId] = text;
                    _state[chatId] = DonateState.WaitingAdminGiveSpinsAmount;
                    await botClient.SendMessage(chatId, "Сколько спинов выдать?", cancellationToken: ct);
                    break;

                case DonateState.WaitingAdminGiveSpinsAmount:
                    {
                        if (!int.TryParse(text, out int spinAmt) || spinAmt <= 0 || spinAmt > 100000)
                        {
                            _state[chatId] = DonateState.WaitingAdminGiveSpinsAmount;
                            await botClient.SendMessage(chatId, "Некорректное число (1-100000). Введи ещё раз:", cancellationToken: ct);
                            break;
                        }
                        string uid = _adminTargetUid.GetValueOrDefault(chatId, "");
                        if (TryResolvePlayerId(uid, out string spinPid, out string spinErr))
                        {
                            _state[chatId] = DonateState.None;
                            GrantItem(spinPid, SpinTokenItemDefinitionId, spinAmt);
                            await botClient.SendMessage(chatId, $"Выдано {spinAmt} спинов (#201) игроку {uid}. Перезайди в игру.", cancellationToken: ct);
                        }
                        else
                        {
                            _state[chatId] = DonateState.WaitingAdminGiveSpinsAmount;
                            await botClient.SendMessage(chatId, $"Не найден: {spinErr}\nВведи количество ещё раз:", cancellationToken: ct);
                        }
                        break;
                    }

                case DonateState.WaitingAdminGiveLevelsUid:
                    _adminTargetUid[chatId] = text;
                    _state[chatId] = DonateState.WaitingAdminGiveLevelsAmount;
                    await botClient.SendMessage(chatId, "Сколько уровней БП выдать?", cancellationToken: ct);
                    break;

                case DonateState.WaitingAdminGiveLevelsAmount:
                    {
                        if (!int.TryParse(text, out int lvlAmt) || lvlAmt <= 0 || lvlAmt > 180)
                        {
                            _state[chatId] = DonateState.WaitingAdminGiveLevelsAmount;
                            await botClient.SendMessage(chatId, "Некорректное число (1-180). Введи ещё раз:", cancellationToken: ct);
                            break;
                        }
                        _state[chatId] = DonateState.None;
                        string uid = _adminTargetUid.GetValueOrDefault(chatId, "");
                        if (TryResolvePlayerId(uid, out string lvlPid, out string lvlErr))
                        {
                            bool ok = AddBattlePassLevels(lvlPid, lvlAmt);
                            string passHint = GameEventRemoteService.PlayerOwnsGoldPass(lvlPid) ? "GOLD" : "Free (выдай Gold Pass для premium-наград)";
                            await botClient.SendMessage(chatId, ok
                                ? $"Выдано {lvlAmt} уровней БП игроку {uid}. Pass: {passHint}. Перезайди и открой батл пасс."
                                : "Не удалось выдать уровни.", cancellationToken: ct);
                        }
                        else await botClient.SendMessage(chatId, $"Не найден: {lvlErr}", cancellationToken: ct);
                        break;
                    }

                case DonateState.WaitingQuickPromo:
                    {
                        // Одной строкой: CODE [gold:N] [spins:N] [gp] [uses:N]
                        // Пример: REWORK10 gold:5000 spins:5 uses:100
                        string raw = (text ?? "").Trim();
                        var parts = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0)
                        {
                            await botClient.SendMessage(chatId, "Пусто. Пример: REWORK10 gold:1000 spins:10 uses:50", cancellationToken: ct);
                            break;
                        }
                        string code = parts[0].ToUpperInvariant();
                        if (code.Length < 3 || code.Length > 24)
                        {
                            await botClient.SendMessage(chatId, "Код 3-24 символа.", cancellationToken: ct);
                            break;
                        }
                        int gold = 0, spins = 0, uses = 1;
                        bool gp = false;
                        for (int i = 1; i < parts.Length; i++)
                        {
                            string p = parts[i].ToLowerInvariant();
                            if (p == "gp" || p == "goldpass") { gp = true; continue; }
                            if (p.StartsWith("gold:") && int.TryParse(p.Substring(5), out int g)) { gold = g; continue; }
                            if (p.StartsWith("spins:") && int.TryParse(p.Substring(6), out int s)) { spins = s; continue; }
                            if (p.StartsWith("uses:") && int.TryParse(p.Substring(5), out int u)) { uses = u; continue; }
                        }
                        _state[chatId] = DonateState.None;
                        await CreatePromoFromAdmin(botClient, chatId, code, uses, gold, spins, gp, ct);
                        break;
                    }

                case DonateState.WaitingPromoCode:
                    {
                        string code = (text ?? "").Trim().ToUpperInvariant();
                        if (code.Length < 3 || code.Length > 24 || code.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '-')))
                        {
                            await botClient.SendMessage(chatId, "Код 3-24 символа: латиница, цифры, _ или -.", cancellationToken: ct);
                            break;
                        }
                        _promoDraft[chatId] = (code, 1);
                        _state[chatId] = DonateState.WaitingPromoMaxUses;
                        await botClient.SendMessage(chatId, $"Код `{code}`. Сколько активаций? (1-10000)", parseMode: ParseMode.Markdown, cancellationToken: ct);
                        break;
                    }

                case DonateState.WaitingPromoMaxUses:
                    {
                        if (!int.TryParse(text, out int maxUses) || maxUses < 1 || maxUses > 10000)
                        {
                            await botClient.SendMessage(chatId, "Число активаций 1-10000:", cancellationToken: ct);
                            break;
                        }
                        if (!_promoDraft.TryGetValue(chatId, out var draft))
                        {
                            _state[chatId] = DonateState.None;
                            await botClient.SendMessage(chatId, "Черновик потерян — начни заново.", cancellationToken: ct);
                            break;
                        }
                        _promoDraft[chatId] = (draft.code, maxUses);
                        // Новая корзина промокода. Ввод id предметов текстом доступен сразу.
                        _promoItemDraft[chatId] = new List<Tuple<int, int>>();
                        _promoGoldDraft[chatId] = 0;
                        _state[chatId] = DonateState.WaitingPromoItemId;
                        await ShowPromoDraftMenu(botClient, chatId, ct);
                        break;
                    }

                case DonateState.WaitingPromoItemId:
                    {
                        // Форматы: id / "141600 x2" / название скина (поиск)
                        var parsed = ParsePromoItemList(text);
                        if (parsed.Count == 0)
                        {
                            string typed = (text ?? "").Trim();
                            _promoSearchQuery[chatId] = typed;
                            await ShowPromoSearchResults(botClient, chatId, typed, 0, ct);
                            break;
                        }
                        if (!_promoDraft.ContainsKey(chatId))
                        {
                            _state[chatId] = DonateState.None;
                            await botClient.SendMessage(chatId, "Черновик потерян — начни заново.", cancellationToken: ct);
                            break;
                        }
                        if (parsed.Count == 1 && parsed[0].Item2 == 1 && !(text ?? "").Contains("x", StringComparison.OrdinalIgnoreCase) && !(text ?? "").Contains(","))
                        {
                            AddPromoDraftItem(chatId, parsed[0].Item1, 1);
                            await ShowPromoDraftMenu(botClient, chatId, ct);
                            break;
                        }
                        foreach (var t in parsed)
                            AddPromoDraftItem(chatId, t.Item1, t.Item2);
                        await ShowPromoDraftMenu(botClient, chatId, ct);
                        break;
                    }

                case DonateState.WaitingPromoSearch:
                    {
                        string q = (text ?? "").Trim();
                        if (q.Length < 2)
                        {
                            await botClient.SendMessage(chatId, "Слишком короткий запрос — минимум 2 символа:", cancellationToken: ct);
                            break;
                        }
                        if (!_promoDraft.ContainsKey(chatId))
                        {
                            _state[chatId] = DonateState.None;
                            await botClient.SendMessage(chatId, "Черновик потерян — начни заново.", cancellationToken: ct);
                            break;
                        }
                        // Чистый id — сразу карточка предмета, без списка.
                        if (int.TryParse(q, out int directKey) && directKey > 0 && FindDef(directKey) != null)
                        {
                            _state[chatId] = DonateState.WaitingPromoItemId;
                            await ShowPromoItemCard(botClient, chatId, directKey, ct);
                            break;
                        }
                        _promoSearchQuery[chatId] = q;
                        _state[chatId] = DonateState.WaitingPromoItemId;
                        await ShowPromoSearchResults(botClient, chatId, q, 0, ct);
                        break;
                    }

                case DonateState.WaitingPromoItemQty:
                    {
                        _state[chatId] = DonateState.None;
                        if (!int.TryParse(text, out int qty) || qty < 1 || qty > 1000)
                        {
                            await botClient.SendMessage(chatId, "Число 1-1000.", cancellationToken: ct);
                            break;
                        }
                        if (!_promoDraft.TryGetValue(chatId, out var pdi) || !_promoPendingItemDef.TryGetValue(chatId, out int defId))
                        {
                            await botClient.SendMessage(chatId, "Черновик потерян.", cancellationToken: ct);
                            break;
                        }
                        AddPromoDraftItem(chatId, defId, qty);
                        _state[chatId] = DonateState.WaitingPromoItemId;
                        await ShowPromoDraftMenu(botClient, chatId, ct);
                        break;
                    }

                case DonateState.WaitingPromoGoldAmount:
                case DonateState.WaitingPromoSpinsAmount:
                    {
                        bool wasSpins = _state[chatId] == DonateState.WaitingPromoSpinsAmount;
                        if (!int.TryParse(text, out int amt) || amt <= 0 || amt > 1000000)
                        {
                            await botClient.SendMessage(chatId, "Некорректное число. Введи ещё раз:", cancellationToken: ct);
                            break;
                        }
                        if (!_promoDraft.ContainsKey(chatId))
                        {
                            _state[chatId] = DonateState.None;
                            await botClient.SendMessage(chatId, "Черновик потерян — начни заново.", cancellationToken: ct);
                            break;
                        }
                        // Кладём награду в корзину и возвращаемся в черновик — промокод
                        // больше не создаётся сразу, можно докинуть скины и что угодно ещё.
                        if (wasSpins)
                            AddPromoDraftItem(chatId, SpinTokenItemDefinitionId, amt);
                        else
                            _promoGoldDraft[chatId] = Math.Clamp(_promoGoldDraft.GetValueOrDefault(chatId, 0) + amt, 0, 100000000);
                        _state[chatId] = DonateState.WaitingPromoItemId;
                        await ShowPromoDraftMenu(botClient, chatId, ct);
                        break;
                    }

                case DonateState.WaitingWhitelistAdd:
                    {
                        string addId = (text ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(addId))
                        {
                            _state[chatId] = DonateState.WaitingWhitelistAdd;
                            await botClient.SendMessage(chatId, "Пустой UID. Введи ещё раз:", cancellationToken: ct);
                            break;
                        }
                        _state[chatId] = DonateState.None;
                        GameWhitelist.AddGameId(addId);
                        await botClient.SendMessage(chatId, $"Добавлен в whitelist: `{addId}`", parseMode: ParseMode.Markdown, cancellationToken: ct);
                        break;
                    }

                case DonateState.WaitingWhitelistRemove:
                    {
                        string rmId = (text ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(rmId))
                        {
                            _state[chatId] = DonateState.WaitingWhitelistRemove;
                            await botClient.SendMessage(chatId, "Пустой UID. Введи ещё раз:", cancellationToken: ct);
                            break;
                        }
                        _state[chatId] = DonateState.None;
                        GameWhitelist.RemoveGameId(rmId);
                        await botClient.SendMessage(chatId, $"Удалён из whitelist: `{rmId}`", parseMode: ParseMode.Markdown, cancellationToken: ct);
                        break;
                    }

                case DonateState.WaitingMarketWhitelistAdd:
                    {
                        _state[chatId] = DonateState.None;
                        string addId = (text ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(addId))
                        {
                            await botClient.SendMessage(chatId, "Пустой ID.", cancellationToken: ct);
                            break;
                        }
                        if (TryResolvePlayerId(addId, out string oid, out _))
                            BoltGameDatabaseProvider.Instance.AddMarketWhitelistId(oid);
                        else
                            BoltGameDatabaseProvider.Instance.AddMarketWhitelistId(addId);
                        await botClient.SendMessage(chatId, $"Добавлен в whitelist рынка: `{addId}`", parseMode: ParseMode.Markdown, cancellationToken: ct);
                        await ShowMarketWhitelistMenu(botClient, chatId, ct);
                        break;
                    }

                case DonateState.WaitingMarketWhitelistRemove:
                    {
                        _state[chatId] = DonateState.None;
                        string rmId = (text ?? "").Trim();
                        if (TryResolvePlayerId(rmId, out string oid, out _))
                            BoltGameDatabaseProvider.Instance.RemoveMarketWhitelistId(oid);
                        BoltGameDatabaseProvider.Instance.RemoveMarketWhitelistId(rmId);
                        await botClient.SendMessage(chatId, $"Удалён из whitelist рынка: `{rmId}`", parseMode: ParseMode.Markdown, cancellationToken: ct);
                        await ShowMarketWhitelistMenu(botClient, chatId, ct);
                        break;
                    }

                case DonateState.WaitingSpinStartMs:
                    {
                        _state[chatId] = DonateState.None;
                        if (!TryParseSpinAdminTime(text, out long startMs))
                        {
                            await botClient.SendMessage(chatId, "Не понял дату старта. Пример: `2026-09-11 12:00` или unix-ms.", parseMode: ParseMode.Markdown, cancellationToken: ct);
                            break;
                        }
                        var doc = BoltGameDatabaseProvider.Instance.GetSpinAdminSettings();
                        long end = doc.endUnixMs > startMs ? doc.endUnixMs : startMs + 86_400_000L;
                        BoltGameDatabaseProvider.Instance.SetSpinAdminSettings(doc.enabled, startMs, end);
                        await botClient.SendMessage(chatId, $"Старт спина: {DateTimeOffset.FromUnixTimeMilliseconds(startMs):u}", cancellationToken: ct);
                        await ShowSpinAdminMenu(botClient, chatId, ct);
                        break;
                    }

                case DonateState.WaitingSpinEndMs:
                    {
                        _state[chatId] = DonateState.None;
                        if (!TryParseSpinAdminTime(text, out long endMs))
                        {
                            await botClient.SendMessage(chatId, "Не понял дату окончания. Пример: `+7d` или `2026-09-20 00:00`.", parseMode: ParseMode.Markdown, cancellationToken: ct);
                            break;
                        }
                        var doc = BoltGameDatabaseProvider.Instance.GetSpinAdminSettings();
                        long start = doc.startUnixMs > 0 ? doc.startUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 86_400_000L;
                        BoltGameDatabaseProvider.Instance.SetSpinAdminSettings(doc.enabled, start, endMs);
                        await botClient.SendMessage(chatId, $"Конец спина: {DateTimeOffset.FromUnixTimeMilliseconds(endMs):u}", cancellationToken: ct);
                        await ShowSpinAdminMenu(botClient, chatId, ct);
                        break;
                    }

                case DonateState.WaitingAdminUserUid:
                case DonateState.WaitingAdminBanReason:
                case DonateState.WaitingAdminClanTag:
                case DonateState.WaitingAdminClanNewTag:
                case DonateState.WaitingAdminClanNewName:
                case DonateState.WaitingAdminSetLevelUid:
                case DonateState.WaitingAdminSetLevelValue:
                case DonateState.WaitingAdminGiveItemUid:
                case DonateState.WaitingAdminGiveItemDef:
                case DonateState.WaitingAdminKickUid:
                case DonateState.WaitingAdminRankMmr:
                case DonateState.WaitingAdminRankWins:
                case DonateState.WaitingAdminRankKd:
                case DonateState.WaitingAdminMmrAdjust:
                case DonateState.WaitingAdminCustomId:
                    await HandleAdminExtraMessage(botClient, chatId, userId, text, state, ct);
                    break;

                default:
                    if (!_linkedPlayerId.ContainsKey(chatId))
                    {
                        await PromptGameIdForVerify(botClient, chatId, ct);
                    }
                    else
                    {
                        await ShowMainMenu(botClient, chatId, ct, userId);
                    }
                    break;
            }
        }

        private async Task PromptGameIdForVerify(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            _state[chatId] = DonateState.WaitingPlayerId;
            await botClient.SendMessage(chatId,
                "Чтобы открыть магазин и мини-приложение, пришли свой *игровой ID* (как в профиле).\n" +
                "Код подтверждения придёт в личные сообщения в игре.",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
        }

        private async Task TryLinkPlayer(ITelegramBotClient botClient, long chatId, string input, CancellationToken ct, long userId = 0)
        {
            if (IsAdmin(userId))
            {
                await TryLinkPlayerDirect(botClient, chatId, input, ct, userId);
                return;
            }

            if (!BeginVerification(userId, input, out string msg, out string err))
            {
                await botClient.SendMessage(chatId,
                    string.IsNullOrEmpty(err) ? "Не удалось отправить код." : err,
                    cancellationToken: ct);
                return;
            }

            _state[chatId] = DonateState.WaitingVerifyCode;
            await botClient.SendMessage(chatId, msg, cancellationToken: ct);
        }

        private async Task TryConfirmVerifyCode(ITelegramBotClient botClient, long chatId, string code, CancellationToken ct, long userId)
        {
            if (!ConfirmVerification(userId, code, out string playerId, out string uid, out string err))
            {
                await botClient.SendMessage(chatId,
                    string.IsNullOrEmpty(err) ? "Код не принят." : err,
                    cancellationToken: ct);
                return;
            }

            LinkTelegramPlayer(chatId, userId, playerId, uid);
            await botClient.SendMessage(chatId,
                $"Аккаунт подтверждён: *{uid}*.\nМагазин в Telegram привязан к этому ID.",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
            await ShowMainMenu(botClient, chatId, ct, userId);
        }

        private async Task TryLinkPlayerDirect(ITelegramBotClient botClient, long chatId, string input, CancellationToken ct, long userId = 0)
        {
            input = (input ?? "").Trim();
            if (!TryResolveTargetPlayer(input, out ObjectId oid, out string uid, out string err))
            {
                await botClient.SendMessage(chatId,
                    string.IsNullOrEmpty(err) ? "Игрок не найден." : err + " Проверь айди и пришли ещё раз:",
                    cancellationToken: ct);
                return;
            }

            string playerId = oid.ToString();
            LinkTelegramPlayer(chatId, userId, playerId, uid);
            await botClient.SendMessage(chatId, $"Аккаунт привязан (админ): `{playerId}`", parseMode: ParseMode.Markdown, cancellationToken: ct);
            await ShowMainMenu(botClient, chatId, ct, userId);
        }

        // ---------- Menus ----------

        private readonly ConcurrentDictionary<long, bool> _adminUnlockedByPass = new();

        private bool IsAdmin(long userId) =>
            _adminIds.Contains(userId) || (_adminUnlockedByPass.TryGetValue(userId, out bool on) && on);
        private bool IsAdminFree(long userId) => IsAdmin(userId) && _adminFreePrices.TryGetValue(userId, out bool on) && on;

        private async Task<bool> IsChannelMemberAsync(ITelegramBotClient botClient, long userId, CancellationToken ct)
        {
            if (userId == 0) return false;
            if (IsAdmin(userId)) return true;
            if (_channelOk.ContainsKey(userId)) return true;
            try
            {
                var member = await botClient.GetChatMember(ChannelUsername, userId, ct);
                bool ok = member.Status != ChatMemberStatus.Left && member.Status != ChatMemberStatus.Kicked;
                if (ok) _channelOk[userId] = 1;
                return ok;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DonateBot] channel check failed: {ex.Message}");
                return true;
            }
        }

        private async Task ShowSubscribePrompt(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithUrl("Подписаться на канал", "https://t.me/StandRework") },
                new[] { InlineKeyboardButton.WithCallbackData("Я подписался", "sub_check") },
            });
            await botClient.SendMessage(chatId,
                "Сначала подпишись на канал @StandRework.\nПосле этого нажми «Я подписался».",
                replyMarkup: keyboard, cancellationToken: ct);
        }

        private async Task ShowSubscribeOrShop(ITelegramBotClient botClient, long chatId, long userId, CancellationToken ct, bool forceCheck = false)
        {
            if (forceCheck) _channelOk.TryRemove(userId, out _);
            if (!IsAdmin(userId) && !await IsChannelMemberAsync(botClient, userId, ct))
            {
                if (forceCheck)
                    await botClient.SendMessage(chatId, "Подписка не найдена. Подпишись на @StandRework и нажми ещё раз.", cancellationToken: ct);
                await ShowSubscribePrompt(botClient, chatId, ct);
                return;
            }
            await ShowMainMenu(botClient, chatId, ct, userId);
        }

        private async Task ShowMainMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct, long userId = 0)
        {
            if (!IsAdmin(userId) && !await IsChannelMemberAsync(botClient, userId, ct))
            {
                await ShowSubscribePrompt(botClient, chatId, ct);
                return;
            }

            if (!_linkedPlayerId.ContainsKey(chatId) && !IsAdmin(userId))
            {
                await botClient.SendMessage(chatId,
                    "Подписка подтверждена.\nДальше нужно подтвердить игровой аккаунт — пришли свой ID одним сообщением.",
                    cancellationToken: ct);
                await PromptGameIdForVerify(botClient, chatId, ct);
                return;
            }

            var rows = new List<InlineKeyboardButton[]>
            {
                new[] { InlineKeyboardButton.WithWebApp("Открыть магазин", new WebAppInfo { Url = ShopMiniAppUrl }) },
            };

            if (IsAdmin(userId))
            {
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Админка", "menu_admin") });
            }

            await botClient.SendMessage(chatId,
                "Подписка подтверждена.\nОткрой магазин в мини-приложении.",
                replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowAdminMenu(ITelegramBotClient botClient, long chatId, long userId, CancellationToken ct)
        {
            bool free = IsAdminFree(userId);
            bool marketClosed = false;
            try { marketClosed = BoltGameDatabaseProvider.Instance.GetMarketClosed(); } catch { }
            bool arcaneOn = false;
            try { arcaneOn = BoltGameDatabaseProvider.Instance.GetArcaneBoost(); } catch { }
            bool wlOn = GameWhitelist.IsEnabled();
            string passInfo = DescribeLinkedPass(chatId);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("👤 Игрок (бан/голда/…)", "admin_user_hub"), InlineKeyboardButton.WithCallbackData("🏅 Звание Allies", "admin_rank_menu") },
                new[] { InlineKeyboardButton.WithCallbackData("🏆 Звание Соревн.", "admin_rank_comp_menu") },
                new[] { InlineKeyboardButton.WithCallbackData("Сбросить ВСЕМ калибровку", "admin_reset_all_calib") },
                new[] { InlineKeyboardButton.WithCallbackData("🗄 Сброс Allies БД (безопасный)", "admin_db_reset") },
                new[] { InlineKeyboardButton.WithCallbackData("🏷 Клан (тег/имя)", "admin_clan"), InlineKeyboardButton.WithCallbackData("🎁 Выдачи", "admin_give_menu") },
                new[] { InlineKeyboardButton.WithCallbackData("⚔️ Союзники (MM)", "admin_allies_mm"), InlineKeyboardButton.WithCallbackData("🏆 Соревн. (MM)", "admin_comp_mm") },
                new[] { InlineKeyboardButton.WithCallbackData("Промокоды", "admin_promo") },
                new[] { InlineKeyboardButton.WithCallbackData(wlOn ? "Whitelist: ВКЛ" : "Whitelist: ВЫКЛ", "admin_wl_menu"), InlineKeyboardButton.WithCallbackData(free ? "⚡ Цены: БЕСПЛАТНО" : "Цены: Stars", "admin_toggle_free") },
                new[] { InlineKeyboardButton.WithCallbackData(marketClosed ? "Рынок: ЗАКРЫТ" : "Рынок: ОТКРЫТ", "admin_market_toggle"), InlineKeyboardButton.WithCallbackData("Рынок WL", "admin_market_wl") },
                new[] { InlineKeyboardButton.WithCallbackData("🎡 Спин: окно времени", "admin_spin_menu") },
                new[] { InlineKeyboardButton.WithCallbackData(arcaneOn ? "🍀 Аркан 100%: ВКЛ" : "🍀 Аркан 100%: ВЫКЛ", "admin_arcane_toggle") },
                new[] { InlineKeyboardButton.WithCallbackData("💰 Выдать голду (ID+сумма)", "admin_give_gold") },
                new[] { InlineKeyboardButton.WithCallbackData("Себе GP/спины", "admin_self_menu"), InlineKeyboardButton.WithCallbackData("Статус Pass", "admin_pass_status") },
                new[] { InlineKeyboardButton.WithCallbackData("🌐 Админка на сайте", "admin_site") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_main") },
            });
            await botClient.SendMessage(chatId,
                "⚙️ Админка StandReworkBot\n" +
                "• Голда: одна форма «Выдать голду» (ID игрока + сумма, без лимита)\n" +
                "• Игрок · звания · клан · выдачи · Союзники MM\n" +
                passInfo + "\n" +
                "После выдач баланс пушится онлайн сразу",
                replyMarkup: keyboard, cancellationToken: ct);
        }

        private string DescribeLinkedPass(long chatId)
        {
            if (!_linkedPlayerId.TryGetValue(chatId, out string pid) || string.IsNullOrEmpty(pid))
                return "Pass: (аккаунт не привязан)";
            try
            {
                bool gold = GameEventRemoteService.PlayerOwnsGoldPass(pid);
                var db = BoltGameDatabaseProvider.Instance;
                var collection = db.GetDatabase.GetCollection<BsonDocument>("game_event_progress");
                var filter = Builders<BsonDocument>.Filter.Eq("playerId", pid) & Builders<BsonDocument>.Filter.Eq("eventId", "CURSED_SOULS");
                var existing = collection.Find(filter).FirstOrDefault();
                int lvl = 1;
                if (existing != null && existing.Contains("levels") && existing["levels"].IsBsonDocument)
                {
                    var lv = existing["levels"].AsBsonDocument;
                    if (lv.Contains("free")) lvl = Math.Max(lvl, lv["free"].ToInt32());
                    if (lv.Contains("premium")) lvl = Math.Max(lvl, lv["premium"].ToInt32());
                }
                return $"Pass: {(gold ? "GOLD" : "обычный (Free)")} · уровень {lvl}";
            }
            catch
            {
                return "Pass: (не удалось прочитать)";
            }
        }

        private async Task ShowCollectionsMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var rows = new List<InlineKeyboardButton[]>();
            var pairs = CollectionNames.ToList();
            for (int i = 0; i < pairs.Count; i += 2)
            {
                var row = new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData(pairs[i].Value, $"col:{(int)pairs[i].Key}")
                };
                if (i + 1 < pairs.Count)
                    row.Add(InlineKeyboardButton.WithCallbackData(pairs[i + 1].Value, $"col:{(int)pairs[i + 1].Key}"));
                rows.Add(row.ToArray());
            }
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_main") });

            await botClient.SendMessage(chatId, "Выбери коллекцию:", replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowRarityMenu(ITelegramBotClient botClient, long chatId, CollectionId col, CancellationToken ct)
        {
            if (!CollectionPricesStars.TryGetValue(col, out var prices))
            {
                await botClient.SendMessage(chatId, "Коллекция недоступна.", cancellationToken: ct);
                return;
            }

            var rows = new List<InlineKeyboardButton[]>();
            foreach (var rarity in new[] { SkinValue.Rare, SkinValue.Epic, SkinValue.Legendary, SkinValue.Arcane })
            {
                if (!prices.TryGetValue(rarity, out int price)) continue;
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"{RarityLabel(rarity)} - {price}⭐", $"rar:{(int)col}:{(int)rarity}:0") });
            }

            if (col == CollectionId.Origin)
            {
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData("M9 Bayonet - 5⭐", "m9menu") });
            }

            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_skins") });

            string name = CollectionNames.TryGetValue(col, out var n) ? n : col.ToString();
            await botClient.SendMessage(chatId, $"{name}\n\nВыбери редкость:", replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private static string RarityLabel(SkinValue v) => v switch
        {
            SkinValue.Rare => "Rare",
            SkinValue.Epic => "Epic",
            SkinValue.Legendary => "Legendary",
            SkinValue.Arcane => "Arcane",
            _ => v.ToString()
        };

        private async Task ShowItemsPage(ITelegramBotClient botClient, long chatId, CollectionId col, SkinValue rarity, int page, CancellationToken ct)
        {
            InventoryCatalogueLoader.Instance.EnsureLoaded();
            var items = InventoryCatalogueLoader.Instance.GetAll()
                .Where(d => d != null && d.properties != null && d.GetCollectionId() == col && d.GetSkinValue() == rarity)
                .OrderBy(d => d.displayName)
                .ToList();

            if (items.Count == 0)
            {
                await botClient.SendMessage(chatId, "В этой категории пока нет предметов.", cancellationToken: ct);
                return;
            }

            const int perPage = 8;
            int totalPages = (int)Math.Ceiling(items.Count / (double)perPage);
            page = Math.Max(0, Math.Min(page, totalPages - 1));
            var pageItems = items.Skip(page * perPage).Take(perPage);

            var rows = new List<InlineKeyboardButton[]>();
            foreach (var item in pageItems)
            {
                // В каталоге у каждого скина обычно ДВА документа - обычный и StatTrak-версия,
                // у обоих одинаковый displayName. Без этой пометки в списке было видно "2
                // одинаковых скина" без разницы между ними. Помечаем StatTrak явно.
                string label = item.displayName ?? $"#{item.key}";
                if (item.IsStattrack()) label += " (StatTrak)";
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(label, $"buyskin:{item.key}:{(int)col}:{(int)rarity}") });
            }

            var nav = new List<InlineKeyboardButton>();
            if (page > 0) nav.Add(InlineKeyboardButton.WithCallbackData("<-", $"rar:{(int)col}:{(int)rarity}:{page - 1}"));
            if (page < totalPages - 1) nav.Add(InlineKeyboardButton.WithCallbackData("->", $"rar:{(int)col}:{(int)rarity}:{page + 1}"));
            if (nav.Count > 0) rows.Add(nav.ToArray());

            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", $"col:{(int)col}") });

            await botClient.SendMessage(chatId, $"{RarityLabel(rarity)} (стр. {page + 1}/{totalPages})", replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowM9Menu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var items = InventoryCatalogueLoader.Instance.GetAll()
                .Where(d => d != null && d.GetCollectionId() == CollectionId.Origin
                    && d.displayName != null && d.displayName.Contains("M9", StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.displayName)
                .ToList();

            var rows = items.Select(item => new[] { InlineKeyboardButton.WithCallbackData(item.IsStattrack() ? item.displayName + " (StatTrak)" : item.displayName, $"buym9:{item.key}") }).ToList();
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", "col:2") });
            await botClient.SendMessage(chatId, "M9 Bayonet (Origin) - 5⭐ каждый:", replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowBattlePassMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct, long userId = 0)
        {
            bool free = IsAdminFree(userId);
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(free ? "Gold Pass - БЕСПЛАТНО" : $"Gold Pass - {GoldPassPriceStars}⭐", "buy_goldpass") },
                new[] { InlineKeyboardButton.WithCallbackData("Купить уровни", "bp_levels"), InlineKeyboardButton.WithCallbackData("Купить спины", "spins") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_main") },
            });
            await botClient.SendMessage(chatId, "Battle Pass:", replyMarkup: keyboard, cancellationToken: ct);
        }

        private async Task ShowSpinsMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct, long userId = 0)
        {
            bool free = IsAdminFree(userId);
            var rows = new List<InlineKeyboardButton[]>();
            foreach (int n in SpinsPresets)
            {
                string label = free ? $"{n} шт. - БЕСПЛАТНО" : $"{n} шт. - {n * StarsPerSpin}⭐";
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(label, $"buy_spins:{n}") });
            }
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Своё количество", "spins_custom") });
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_bp") });

            await botClient.SendMessage(chatId, free ? "Покупка спинов (бесплатно для админа):" : $"Покупка спинов ({StarsPerSpin}⭐ за шт.):", replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowBpLevelsMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct, long userId = 0)
        {
            bool free = IsAdminFree(userId);
            var rows = new List<InlineKeyboardButton[]>();
            foreach (var (levels, stars) in ShopPrices.LevelPacks())
            {
                string label = free ? $"{levels} ур. - БЕСПЛАТНО" : $"{levels} ур. - {stars}⭐";
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(label, $"buy_levels:{levels}") });
            }
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_bp") });

            await botClient.SendMessage(chatId, free ? "Покупка уровней (бесплатно для админа):" : "Покупка уровней боевого пропуска:", replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task HandleCallback(ITelegramBotClient botClient, CallbackQuery cq, CancellationToken ct)
        {
            long chatId = cq.Message.Chat.Id;
            long userId = cq.From?.Id ?? 0;
            await botClient.AnswerCallbackQuery(cq.Id, cancellationToken: ct);

            string gateData = cq.Data ?? "";
            if (!_linkedPlayerId.ContainsKey(chatId)
                && gateData != "menu_relink"
                && gateData != "sub_check"
                && gateData != "verify_restart"
                && gateData != "menu_main"
                && !gateData.StartsWith("admin_")
                && !gateData.StartsWith("pcol")
                && !gateData.StartsWith("prar")
                && !gateData.StartsWith("padd")
                && !gateData.StartsWith("promo_")
                && gateData != "menu_admin")
            {
                await ShowSubscribeOrShop(botClient, chatId, userId, ct);
                return;
            }

            string data = cq.Data ?? "";
            string[] parts = data.Split(':');

            switch (parts[0])
            {
                case "sub_check":
                    await ShowSubscribeOrShop(botClient, chatId, userId, ct, forceCheck: true);
                    break;

                case "menu_main":
                    await ShowMainMenu(botClient, chatId, ct, userId);
                    break;

                case "buy_gold":
                    if (parts.Length < 3
                        || !int.TryParse(parts[1], out int goldAmt)
                        || !int.TryParse(parts[2], out int goldStars)
                        || !IsAllowedGoldPack(goldAmt, goldStars))
                    {
                        await botClient.SendMessage(chatId, "Неверный пакет голды.", cancellationToken: ct);
                        break;
                    }
                    await SendGoldInvoice(botClient, chatId, goldAmt, goldStars, ct, userId);
                    break;

                case "menu_skins":
                case "menu_bp":
                    await ShowMainMenu(botClient, chatId, ct, userId);
                    break;

                case "menu_admin":
                    if (!IsAdmin(userId))
                    {
                        await botClient.SendMessage(chatId, "Только для админов.", cancellationToken: ct);
                        break;
                    }
                    // По умолчанию бесплатные цены включены для админов.
                    if (!_adminFreePrices.ContainsKey(userId))
                        _adminFreePrices[userId] = true;
                    await ShowAdminMenu(botClient, chatId, userId, ct);
                    break;

                case "admin_toggle_free":
                    if (!IsAdmin(userId)) break;
                    bool next = !IsAdminFree(userId);
                    _adminFreePrices[userId] = next;
                    await botClient.SendMessage(chatId, next
                        ? "⚡ Бесплатные цены ВКЛ — все покупки в боте без Stars."
                        : "Бесплатные цены ВЫКЛ — обычные цены Stars.", cancellationToken: ct);
                    await ShowAdminMenu(botClient, chatId, userId, ct);
                    break;

                case "admin_give_bp":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingAdminGiveBpUid;
                    await botClient.SendMessage(chatId, "UID игрока для выдачи Gold Pass:", cancellationToken: ct);
                    break;

                case "admin_give_gold":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingAdminGiveGoldUid;
                    {
                        string hist = FormatGoldGrantHistory();
                        string histBlock = string.IsNullOrEmpty(hist) ? "(пока пусто)" : hist;
                        await botClient.SendMessage(chatId,
                            "💰 Выдача голды (единственная форма)\n" +
                            "1) ID игрока (UID / ник / ObjectId)\n" +
                            "2) Сумма (целое > 0, без верхнего лимита)\n\n" +
                            "Последние выдачи:\n" + histBlock,
                            cancellationToken: ct);
                    }
                    break;

                case "admin_give_spins":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingAdminGiveSpinsUid;
                    await botClient.SendMessage(chatId, "UID игрока для выдачи спинов:", cancellationToken: ct);
                    break;

                case "admin_give_levels":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingAdminGiveLevelsUid;
                    await botClient.SendMessage(chatId, "UID игрока для выдачи уровней БП:", cancellationToken: ct);
                    break;

                case "admin_self_bp":
                    if (!IsAdmin(userId)) break;
                    if (_linkedPlayerId.TryGetValue(chatId, out string selfBp))
                    {
                        try
                        {
                            GrantItem(selfBp, GoldPassItemDefinitionId);
                            await botClient.SendMessage(chatId, $"Gold Pass (#608) выдан на {selfBp}. Перезайди в игру.", cancellationToken: ct);
                        }
                        catch (Exception ex)
                        {
                            await botClient.SendMessage(chatId, $"Ошибка выдачи Gold Pass: {ex.Message}", cancellationToken: ct);
                        }
                    }
                    else await botClient.SendMessage(chatId, "Сначала привяжи айди.", cancellationToken: ct);
                    break;

                case "admin_self_spins":
                    if (!IsAdmin(userId)) break;
                    if (_linkedPlayerId.TryGetValue(chatId, out string selfSpins))
                    {
                        try
                        {
                            GrantItem(selfSpins, SpinTokenItemDefinitionId, 100);
                            await botClient.SendMessage(chatId, $"100 спинов (#201) выдано на {selfSpins}. Перезайди.", cancellationToken: ct);
                        }
                        catch (Exception ex)
                        {
                            await botClient.SendMessage(chatId, $"Ошибка выдачи спинов: {ex.Message}", cancellationToken: ct);
                        }
                    }
                    else await botClient.SendMessage(chatId, "Сначала привяжи айди.", cancellationToken: ct);
                    break;

                case "admin_self_levels":
                    if (!IsAdmin(userId)) break;
                    if (_linkedPlayerId.TryGetValue(chatId, out string selfLvl))
                    {
                        bool ok = AddBattlePassLevels(selfLvl, 10);
                        await botClient.SendMessage(chatId, ok
                            ? $"+10 уровней БП на {selfLvl}. Перезайди и открой батл пасс."
                            : "Не удалось выдать уровни.", cancellationToken: ct);
                    }
                    else await botClient.SendMessage(chatId, "Сначала привяжи айди.", cancellationToken: ct);
                    break;

                case "admin_self_gold":
                    // Дубли убраны: только admin_give_gold (ID + сумма).
                    if (!IsAdmin(userId)) break;
                    goto case "admin_give_gold";

                case "admin_pass_status":
                    if (!IsAdmin(userId)) break;
                    await botClient.SendMessage(chatId, DescribeLinkedPass(chatId), cancellationToken: ct);
                    break;

                case "admin_promo":
                    if (!IsAdmin(userId)) break;
                    await ShowPromoMenu(botClient, chatId, ct);
                    break;

                case "admin_promo_create":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingPromoCode;
                    await botClient.SendMessage(chatId, "Введи код промокода (латиница/цифры, 3-24 символа):", cancellationToken: ct);
                    break;

                case "admin_promo_quick":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingQuickPromo;
                    await botClient.SendMessage(chatId,
                        "Одной строкой:\n`КОД gold:1000 spins:5 uses:50 gp`\n(gp — Gold Pass, необязательно)",
                        parseMode: ParseMode.Markdown, cancellationToken: ct);
                    break;

                case "admin_promo_gold":
                case "admin_promo_spins":
                case "admin_promo_goldpass":
                case "admin_promo_items":
                    if (!IsAdmin(userId)) break;
                    if (!_promoDraft.ContainsKey(chatId))
                    {
                        await botClient.SendMessage(chatId, "Сначала создай промокод заново.", cancellationToken: ct);
                        break;
                    }
                    if (cq.Data == "admin_promo_goldpass")
                    {
                        // Gold Pass — такой же предмет корзины (#608), а не «вместо всего».
                        AddPromoDraftItem(chatId, GoldPassItemDefinitionId, 1);
                        _state[chatId] = DonateState.WaitingPromoItemId;
                        await ShowPromoDraftMenu(botClient, chatId, ct);
                    }
                    else if (cq.Data == "admin_promo_items")
                    {
                        if (!_promoItemDraft.ContainsKey(chatId))
                            _promoItemDraft[chatId] = new List<Tuple<int, int>>();
                        _state[chatId] = DonateState.WaitingPromoItemId;
                        await ShowPromoCollectionsMenu(botClient, chatId, ct);
                    }
                    else if (cq.Data == "admin_promo_gold")
                    {
                        _state[chatId] = DonateState.WaitingPromoGoldAmount;
                        int have = _promoGoldDraft.GetValueOrDefault(chatId, 0);
                        await botClient.SendMessage(chatId,
                            have > 0 ? $"Сколько голды добавить? (сейчас в промокоде {have})" : "Сколько голды в промокоде?",
                            cancellationToken: ct);
                    }
                    else
                    {
                        _state[chatId] = DonateState.WaitingPromoSpinsAmount;
                        await botClient.SendMessage(chatId, "Сколько спинов (#201) в промокоде?", cancellationToken: ct);
                    }
                    break;

                case "admin_market_toggle":
                    if (!IsAdmin(userId)) break;
                    try
                    {
                        bool currentlyClosed = BoltGameDatabaseProvider.Instance.GetMarketClosed();
                        bool nextClosed = !currentlyClosed;
                        BoltGameDatabaseProvider.Instance.SetMarketClosed(nextClosed,
                            nextClosed ? "Рынок закрыт на учёт администратором" : null);
                        try { MarketplaceRemoteService.ForcePushMarketStateNow(); } catch { }
                        await botClient.SendMessage(chatId, nextClosed
                            ? "Рынок ЗАКРЫТ на учёт. Whitelist-игроки сохраняют доступ. (push отправлен онлайн)"
                            : "Рынок ОТКРЫТ. (push отправлен онлайн)", cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        await botClient.SendMessage(chatId, $"Ошибка рынка: {ex.Message}", cancellationToken: ct);
                    }
                    await ShowAdminMenu(botClient, chatId, userId, ct);
                    break;

                case "admin_site":
                    if (!IsAdmin(userId)) break;
                    await SendSiteAdminLink(botClient, chatId, userId, ct);
                    break;

                case "admin_arcane_toggle":
                    if (!IsAdmin(userId)) break;
                    try
                    {
                        bool arcaneNow = BoltGameDatabaseProvider.Instance.GetArcaneBoost();
                        bool arcaneNext = !arcaneNow;
                        BoltGameDatabaseProvider.Instance.SetArcaneBoost(arcaneNext);
                        await botClient.SendMessage(chatId, arcaneNext
                            ? "🍀 Подкрутка ВКЛЮЧЕНА: спин, кейсы и паки наклеек выдают предмет максимальной редкости (Arcane, если он есть в наборе)."
                            : "Подкрутка ВЫКЛЮЧЕНА: обычные шансы.", cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        await botClient.SendMessage(chatId, $"Ошибка подкрутки: {ex.Message}", cancellationToken: ct);
                    }
                    await ShowAdminMenu(botClient, chatId, userId, ct);
                    break;

                case "admin_market_wl":
                    if (!IsAdmin(userId)) break;
                    await ShowMarketWhitelistMenu(botClient, chatId, ct);
                    break;

                case "admin_market_wl_add":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingMarketWhitelistAdd;
                    await botClient.SendMessage(chatId, "Введи ObjectId или UID игрока для whitelist рынка:", cancellationToken: ct);
                    break;

                case "admin_market_wl_remove":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingMarketWhitelistRemove;
                    await botClient.SendMessage(chatId, "Введи ObjectId/UID для удаления из whitelist рынка:", cancellationToken: ct);
                    break;

                case "admin_market_wl_list":
                    if (!IsAdmin(userId)) break;
                    {
                        var ids = BoltGameDatabaseProvider.Instance.GetMarketWhitelistIds();
                        string list = ids.Count == 0 ? "(пусто)" : string.Join("\n", ids.Take(50));
                        await botClient.SendMessage(chatId,
                            $"Whitelist рынка (доступ при закрытии)\nВсего: {ids.Count}\n{list}",
                            cancellationToken: ct);
                    }
                    break;

                case "admin_spin_menu":
                    if (!IsAdmin(userId)) break;
                    await ShowSpinAdminMenu(botClient, chatId, ct);
                    break;

                case "admin_spin_toggle":
                    if (!IsAdmin(userId)) break;
                    {
                        var doc = BoltGameDatabaseProvider.Instance.GetSpinAdminSettings();
                        bool spinOn = !doc.enabled;
                        BoltGameDatabaseProvider.Instance.SetSpinAdminSettings(spinOn, doc.startUnixMs, doc.endUnixMs);
                        await botClient.SendMessage(chatId, spinOn ? "Спин ВКЛЮЧЁН (в окне дат)." : "Спин ПОЛНОСТЬЮ ВЫКЛЮЧЕН.", cancellationToken: ct);
                        await ShowSpinAdminMenu(botClient, chatId, ct);
                    }
                    break;

                case "admin_spin_extend_1d":
                    if (!IsAdmin(userId)) break;
                    {
                        var doc = BoltGameDatabaseProvider.Instance.GetSpinAdminSettings();
                        long end = doc.endUnixMs > 0 ? doc.endUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        end += 86_400_000L;
                        BoltGameDatabaseProvider.Instance.SetSpinAdminSettings(doc.enabled, doc.startUnixMs, end);
                        await botClient.SendMessage(chatId, "Спин продлён на +1 сутки.", cancellationToken: ct);
                        await ShowSpinAdminMenu(botClient, chatId, ct);
                    }
                    break;

                case "admin_spin_shorten_1d":
                    if (!IsAdmin(userId)) break;
                    {
                        var doc = BoltGameDatabaseProvider.Instance.GetSpinAdminSettings();
                        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        long end = doc.endUnixMs > 0 ? doc.endUnixMs : now;
                        end = Math.Max(now, end - 86_400_000L);
                        BoltGameDatabaseProvider.Instance.SetSpinAdminSettings(doc.enabled, doc.startUnixMs, end);
                        await botClient.SendMessage(chatId, "Спин сокращён на −1 сутки (не раньше сейчас).", cancellationToken: ct);
                        await ShowSpinAdminMenu(botClient, chatId, ct);
                    }
                    break;

                case "admin_spin_set_start":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingSpinStartMs;
                    await botClient.SendMessage(chatId,
                        "Введи дату/время СТАРТА спина:\n• unix-ms\n• или `yyyy-MM-dd HH:mm` (UTC)\n• или `now`",
                        parseMode: ParseMode.Markdown, cancellationToken: ct);
                    break;

                case "admin_spin_set_end":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingSpinEndMs;
                    await botClient.SendMessage(chatId,
                        "Введи дату/время ОКОНЧАНИЯ спина:\n• unix-ms\n• или `yyyy-MM-dd HH:mm` (UTC)\n• или `+1d` / `+7d`",
                        parseMode: ParseMode.Markdown, cancellationToken: ct);
                    break;

                case "admin_wl_menu":
                    if (!IsAdmin(userId)) break;
                    await ShowWhitelistMenu(botClient, chatId, ct);
                    break;

                case "admin_wl_on":
                    if (!IsAdmin(userId)) break;
                    GameWhitelist.SetEnabled(true);
                    await botClient.SendMessage(chatId, "Сервер закрыт: вход только для UID из whitelist.", cancellationToken: ct);
                    await ShowWhitelistMenu(botClient, chatId, ct);
                    break;

                case "admin_wl_off":
                    if (!IsAdmin(userId)) break;
                    GameWhitelist.SetEnabled(false);
                    await botClient.SendMessage(chatId, "Сервер открыт для всех.", cancellationToken: ct);
                    await ShowWhitelistMenu(botClient, chatId, ct);
                    break;

                case "admin_wl_ranked_on":
                    if (!IsAdmin(userId)) break;
                    GameWhitelist.SetRankedQueueLocked(true);
                    await botClient.SendMessage(chatId, "Союзники/соревновательный закрыты — только UID из whitelist.", cancellationToken: ct);
                    await ShowWhitelistMenu(botClient, chatId, ct);
                    break;

                case "admin_wl_ranked_off":
                    if (!IsAdmin(userId)) break;
                    GameWhitelist.SetRankedQueueLocked(false);
                    await botClient.SendMessage(chatId, "Очереди союзники/соревновательный открыты для всех.", cancellationToken: ct);
                    await ShowWhitelistMenu(botClient, chatId, ct);
                    break;

                case "admin_wl_add":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingWhitelistAdd;
                    await botClient.SendMessage(chatId, "Введи игровой UID (или ObjectId) для whitelist:", cancellationToken: ct);
                    break;

                case "admin_wl_remove":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingWhitelistRemove;
                    await botClient.SendMessage(chatId, "Введи UID для удаления из whitelist:", cancellationToken: ct);
                    break;

                case "admin_wl_list":
                    if (!IsAdmin(userId)) break;
                    {
                        var ids = GameWhitelist.GetGameWhitelistIds();
                        string list = ids.Count == 0 ? "(пусто)" : string.Join("\n", ids.Take(50));
                        await botClient.SendMessage(chatId,
                            $"Whitelist {(GameWhitelist.IsEnabled() ? "ВКЛ" : "ВЫКЛ")}\nВсего: {ids.Count}\n{list}",
                            cancellationToken: ct);
                    }
                    break;

                case "promo_pick":
                    if (!IsAdmin(userId)) break;
                    if (parts.Length < 2 || !int.TryParse(parts[1], out int pickDef) || pickDef <= 0)
                    {
                        await botClient.SendMessage(chatId, "Неверный предмет.", cancellationToken: ct);
                        break;
                    }
                    if (!_promoDraft.ContainsKey(chatId))
                    {
                        await botClient.SendMessage(chatId, "Черновик промо потерян — создай заново.", cancellationToken: ct);
                        break;
                    }
                    await ShowPromoItemCard(botClient, chatId, pickDef, ct);
                    break;

                case "pcol":
                    if (!IsAdmin(userId)) break;
                    if (parts.Length < 2 || !int.TryParse(parts[1], out int pcolId))
                    {
                        await ShowPromoCollectionsMenu(botClient, chatId, ct);
                        break;
                    }
                    await ShowPromoRarityMenu(botClient, chatId, (CollectionId)pcolId, ct);
                    break;

                case "prar":
                    if (!IsAdmin(userId)) break;
                    await ShowPromoItemsPage(botClient, chatId,
                        (CollectionId)int.Parse(parts[1]),
                        (SkinValue)int.Parse(parts[2]),
                        int.Parse(parts[3]), ct);
                    break;

                case "padd":
                    if (!IsAdmin(userId)) break;
                    if (parts.Length < 2 || !int.TryParse(parts[1], out int addDef) || addDef <= 0)
                    {
                        await botClient.SendMessage(chatId, "Неверный предмет.", cancellationToken: ct);
                        break;
                    }
                    if (!_promoDraft.ContainsKey(chatId))
                    {
                        await botClient.SendMessage(chatId, "Черновик промо потерян — создай заново.", cancellationToken: ct);
                        break;
                    }
                    await ShowPromoItemCard(botClient, chatId, addDef, ct);
                    break;

                case "promo_done":
                    if (!IsAdmin(userId)) break;
                    if (!_promoDraft.TryGetValue(chatId, out var doneDraft))
                    {
                        await botClient.SendMessage(chatId, "Черновик потерян.", cancellationToken: ct);
                        break;
                    }
                    await FinalizePromoCart(botClient, chatId, doneDraft.code, doneDraft.maxUses, ct);
                    break;

                case "promo_clear":
                    if (!IsAdmin(userId)) break;
                    _promoItemDraft[chatId] = new List<Tuple<int, int>>();
                    _promoGoldDraft[chatId] = 0;
                    _state[chatId] = DonateState.WaitingPromoItemId;
                    await ShowPromoDraftMenu(botClient, chatId, ct);
                    break;

                case "promo_menu":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingPromoItemId;
                    await ShowPromoPickModeMenu(botClient, chatId, ct);
                    break;

                case "promo_cols":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingPromoItemId;
                    await ShowPromoCollectionsMenu(botClient, chatId, ct);
                    break;

                case "promo_search":
                    if (!IsAdmin(userId)) break;
                    _state[chatId] = DonateState.WaitingPromoSearch;
                    await botClient.SendMessage(chatId,
                        "🔍 Введи название или его часть (например `Night Fury`, `Dragon`, `Bronze`) либо id предмета:",
                        parseMode: ParseMode.Markdown, cancellationToken: ct);
                    break;

                case "psr":
                    if (!IsAdmin(userId)) break;
                    {
                        int srPage = parts.Length > 1 && int.TryParse(parts[1], out int sp) ? sp : 0;
                        string srQuery = _promoSearchQuery.GetValueOrDefault(chatId) ?? "";
                        if (string.IsNullOrWhiteSpace(srQuery))
                        {
                            _state[chatId] = DonateState.WaitingPromoSearch;
                            await botClient.SendMessage(chatId, "Запрос потерян. Введи название заново:", cancellationToken: ct);
                            break;
                        }
                        await ShowPromoSearchResults(botClient, chatId, srQuery, srPage, ct);
                    }
                    break;

                case "pitem":
                    if (!IsAdmin(userId)) break;
                    if (parts.Length < 2 || !int.TryParse(parts[1], out int cardKey) || cardKey <= 0)
                    {
                        await botClient.SendMessage(chatId, "Неверный предмет.", cancellationToken: ct);
                        break;
                    }
                    if (!_promoDraft.ContainsKey(chatId))
                    {
                        await botClient.SendMessage(chatId, "Черновик промо потерян — создай заново.", cancellationToken: ct);
                        break;
                    }
                    await ShowPromoItemCard(botClient, chatId, cardKey, ct);
                    break;

                case "pqty":
                    if (!IsAdmin(userId)) break;
                    if (parts.Length < 3 || !int.TryParse(parts[1], out int qKey) || !int.TryParse(parts[2], out int qAmt)
                        || qKey <= 0 || qAmt <= 0)
                    {
                        await botClient.SendMessage(chatId, "Неверное количество.", cancellationToken: ct);
                        break;
                    }
                    if (!_promoDraft.ContainsKey(chatId))
                    {
                        await botClient.SendMessage(chatId, "Черновик промо потерян — создай заново.", cancellationToken: ct);
                        break;
                    }
                    AddPromoDraftItem(chatId, qKey, qAmt);
                    _state[chatId] = DonateState.WaitingPromoItemId;
                    await ShowPromoDraftMenu(botClient, chatId, ct);
                    break;

                case "pqtyc":
                    if (!IsAdmin(userId)) break;
                    if (parts.Length < 2 || !int.TryParse(parts[1], out int qcKey) || qcKey <= 0)
                    {
                        await botClient.SendMessage(chatId, "Неверный предмет.", cancellationToken: ct);
                        break;
                    }
                    _promoPendingItemDef[chatId] = qcKey;
                    _state[chatId] = DonateState.WaitingPromoItemQty;
                    await botClient.SendMessage(chatId, "Введи количество (1-1000):", cancellationToken: ct);
                    break;

                case "promo_menu_draft":
                    if (!IsAdmin(userId)) break;
                    await ShowPromoDraftMenu(botClient, chatId, ct);
                    break;

                case "admin_give_menu":
                case "admin_self_menu":
                case "admin_user_hub":
                case "admin_rank_menu":
                case "admin_rank_comp_menu":
                case "admin_rank":
                case "admin_rank_comp":
                case "admin_reset_all_calib":
                case "admin_db_reset":
                case "admin_db_reset_yes":
                case "admin_clan":
                case "admin_allies_mm":
                case "admin_user_ban":
                case "admin_user_unban":
                case "admin_user_kick":
                case "admin_user_gold":
                case "admin_user_spins":
                case "admin_user_bp":
                case "admin_user_level":
                case "admin_user_item":
                case "admin_user_info":
                case "admin_user_customid":
                case "admin_user_clearinv":
                case "admin_user_clearinv_yes":
                case "admin_clan_retag":
                case "admin_clan_rename":
                    if (!IsAdmin(userId)) break;
                    await HandleAdminExtraCallback(botClient, chatId, userId, cq.Data ?? "", ct);
                    break;

                case "menu_customid":
                    await ShowMainMenu(botClient, chatId, ct, userId);
                    break;

                case "menu_relink":
                    _state[chatId] = DonateState.WaitingRelinkPlayerId;
                    await botClient.SendMessage(chatId, "Пришли новый айди для привязки:", cancellationToken: ct);
                    break;

                case "col":
                    await ShowRarityMenu(botClient, chatId, (CollectionId)int.Parse(parts[1]), ct);
                    break;

                case "rar":
                    await ShowItemsPage(botClient, chatId, (CollectionId)int.Parse(parts[1]), (SkinValue)int.Parse(parts[2]), int.Parse(parts[3]), ct);
                    break;

                case "m9menu":
                    await ShowM9Menu(botClient, chatId, ct);
                    break;

                case "buym9":
                    await SendSkinInvoice(botClient, chatId, int.Parse(parts[1]), 5, ct, userId);
                    break;

                case "buyskin":
                    {
                        int itemDefId = int.Parse(parts[1]);
                        var col = (CollectionId)int.Parse(parts[2]);
                        var rar = (SkinValue)int.Parse(parts[3]);
                        int price = CollectionPricesStars.TryGetValue(col, out var p) && p.TryGetValue(rar, out int pr) ? pr : 0;
                        if (price <= 0)
                        {
                            await botClient.SendMessage(chatId, "Не удалось определить цену.", cancellationToken: ct);
                            break;
                        }
                        await SendSkinInvoice(botClient, chatId, itemDefId, price, ct, userId);
                        break;
                    }

                case "spins":
                case "spins_custom":
                case "buy_spins":
                case "buy_goldpass":
                case "bp_levels":
                case "bp_custom_levels":
                case "buy_levels":
                    await ShowMainMenu(botClient, chatId, ct, userId);
                    break;

                case "buy_test":
                    if (!IsAdmin(userId))
                    {
                        await botClient.SendMessage(chatId, "Только для админов.", cancellationToken: ct);
                        break;
                    }
                    await SendTestInvoice(botClient, chatId, ct);
                    break;

                default:
                    if (IsAdmin(userId) && (data.StartsWith("admin_", StringComparison.OrdinalIgnoreCase)
                        || data.StartsWith("admin_rank:", StringComparison.OrdinalIgnoreCase)
                        || data.StartsWith("admin_rank_comp:", StringComparison.OrdinalIgnoreCase)
                        || data.StartsWith("admin_calib:", StringComparison.OrdinalIgnoreCase)
                        || data.StartsWith("admin_allies_set:", StringComparison.OrdinalIgnoreCase)))
                    {
                        await HandleAdminExtraCallback(botClient, chatId, userId, data, ct);
                        break;
                    }
                    await ShowMainMenu(botClient, chatId, ct, userId);
                    break;
            }
        }

        // ---------- Invoices ----------

        private async Task FulfilPayload(ITelegramBotClient botClient, long chatId, string payload, CancellationToken ct, long userId = 0)
        {
            string[] parts = (payload ?? "").Split('|');
            try
            {
                switch (parts[0])
                {
                    case "skin":
                        {
                            int itemDefId = int.Parse(parts[1]);
                            string playerId = parts[2];
                            GrantItem(playerId, itemDefId);
                            await botClient.SendMessage(chatId, "Предмет выдан. Перезайди в игру, если не появился сразу.", cancellationToken: ct);
                            break;
                        }
                    case "gold":
                        {
                            int amount = int.Parse(parts[1]);
                            string playerId = parts[2];
                            GrantGold(playerId, amount);
                            await botClient.SendMessage(chatId, $"Выдано {amount} голды. Зайди в игру / обнови инвентарь.", cancellationToken: ct);
                            break;
                        }
                    case "goldpass":
                        {
                            string playerId = parts[1];
                            GrantItem(playerId, GoldPassItemDefinitionId);
                            await botClient.SendMessage(chatId, "Gold Pass выдан. Перезайди в игру.", cancellationToken: ct);
                            break;
                        }
                    case "passplus":
                        {
                            // Gold Pass и уровни одним платежом: сначала пропуск,
                            // иначе уровни лягут во free-ветку без premium-наград.
                            int plusLevels = int.Parse(parts[1]);
                            string passPlusPlayer = parts[2];
                            GrantItem(passPlusPlayer, GoldPassItemDefinitionId);
                            bool levelsOk = AddBattlePassLevels(passPlusPlayer, plusLevels);
                            await botClient.SendMessage(chatId, levelsOk
                                ? $"Gold Pass выдан и начислено {plusLevels} уровней. Перезайди в игру."
                                : "Gold Pass выдан, но уровни начислить не вышло — напиши в поддержку.",
                                cancellationToken: ct);
                            break;
                        }
                    case "levels":
                        {
                            int amount = int.Parse(parts[1]);
                            string playerId = parts[2];
                            bool ok = AddBattlePassLevels(playerId, amount);
                            await botClient.SendMessage(chatId, ok
                                ? $"Начислено {amount} уровней БП. Открой батлпасс в игре, чтобы получить награды."
                                : "Не удалось начислить уровни БП (нет события). Напиши в поддержку.", cancellationToken: ct);
                            break;
                        }
                    case "spins":
                        {
                            int amount = int.Parse(parts[1]);
                            string playerId = parts[2];
                            GrantItem(playerId, SpinTokenItemDefinitionId, amount);
                            await botClient.SendMessage(chatId, $"Выдано {amount} спинов (#201). Зайди в игру.", cancellationToken: ct);
                            break;
                        }
                    case "customid":
                        {
                            string newId = parts[1];
                            string playerId = parts[2];
                            bool ok = TrySetCustomId(playerId, newId, out string reason);
                            await botClient.SendMessage(chatId, ok
                                ? $"Custom ID изменён на «{newId}»."
                                : $"Не удалось установить Custom ID ({reason}).", cancellationToken: ct);
                            break;
                        }
                    case "test":
                        {
                            string playerId = parts[1];
                            GrantItem(playerId, TestItemDefinitionId);
                            await botClient.SendMessage(chatId, $"Тест OK: medal #{TestItemDefinitionId} на {playerId}.", cancellationToken: ct);
                            break;
                        }
                    default:
                        await botClient.SendMessage(chatId, "Неизвестный тип покупки.", cancellationToken: ct);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DonateBot] FulfilPayload error for '{payload}': {ex}");
                await botClient.SendMessage(chatId, "Ошибка выдачи. Смотри логи сервера.", cancellationToken: ct);
            }
        }

        private async Task SendInvoiceSafe(ITelegramBotClient botClient, long chatId, string title, string desc, string payload, int price, CancellationToken ct, long userId = 0)
        {
            if (IsAdminFree(userId) || (IsAdmin(userId) && price <= 0))
            {
                await botClient.SendMessage(chatId, $"⚡ Бесплатно (админ): {title}", cancellationToken: ct);
                await FulfilPayload(botClient, chatId, payload, ct, userId);
                return;
            }

            // Админы с включённым free: price уже может быть >0, но IsAdminFree ловит выше.
            // Если админ без free — обычный invoice.
            try
            {
                await botClient.SendInvoice(
                    chatId: chatId,
                    title: title,
                    description: desc,
                    payload: payload,
                    providerToken: "",
                    currency: "XTR",
                    prices: new[] { new LabeledPrice("Цена", Math.Max(1, price)) },
                    cancellationToken: ct
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DonateBot] Invoice error: {ex}");
                await botClient.SendMessage(chatId, "Не удалось выставить счёт. Попробуй ещё раз позже.", cancellationToken: ct);
            }
        }

        private async Task SendSkinInvoice(ITelegramBotClient botClient, long chatId, int itemDefId, int price, CancellationToken ct, long userId = 0)
        {
            var def = InventoryCatalogueLoader.Instance.GetAll().FirstOrDefault(d => d.key == itemDefId);
            string name = def?.displayName ?? $"Предмет #{itemDefId}";
            if (def != null && def.IsStattrack()) name += " (StatTrak)";
            string playerId = _linkedPlayerId[chatId];
            string payload = $"skin|{itemDefId}|{playerId}";
            await SendInvoiceSafe(botClient, chatId, name, $"Покупка предмета «{name}» на аккаунт {playerId}", payload, price, ct, userId);
        }

        private static bool IsAllowedGoldPack(int gold, int stars) =>
            ShopPrices.IsAllowedGoldPack(gold, stars);

        private async Task SendGoldInvoice(ITelegramBotClient botClient, long chatId, int goldAmount, int stars, CancellationToken ct, long userId = 0)
        {
            if (!_linkedPlayerId.TryGetValue(chatId, out string playerId))
            {
                await botClient.SendMessage(chatId, "Сначала привяжи айди (/start).", cancellationToken: ct);
                return;
            }
            string payload = $"gold|{goldAmount}|{playerId}";
            await SendInvoiceSafe(botClient, chatId, $"{goldAmount} голды", $"Покупка {goldAmount} голды на аккаунт {playerId}", payload, stars, ct, userId);
        }

        private async Task SendGoldPassInvoice(ITelegramBotClient botClient, long chatId, CancellationToken ct, long userId = 0)
        {
            string playerId = _linkedPlayerId[chatId];
            string payload = $"goldpass|{playerId}";
            await SendInvoiceSafe(botClient, chatId, "Battle Pass: Gold Pass", $"Покупка Gold Pass на аккаунт {playerId}", payload, GoldPassPriceStars, ct, userId);
        }

        private async Task SendLevelsInvoice(ITelegramBotClient botClient, long chatId, int amount, CancellationToken ct, long userId = 0)
        {
            if (amount <= 0)
            {
                await botClient.SendMessage(chatId, "Некорректное количество уровней.", cancellationToken: ct);
                return;
            }
            string playerId = _linkedPlayerId[chatId];
            int price = ShopPrices.LevelPackStars(amount);
            if (price <= 0)
            {
                await botClient.SendMessage(chatId, "Такого пакета уровней нет в витрине.", cancellationToken: ct);
                return;
            }
            string payload = $"levels|{amount}|{playerId}";
            await SendInvoiceSafe(botClient, chatId, $"Battle Pass: {amount} уровней", $"Покупка {amount} уровней БП на аккаунт {playerId}", payload, price, ct, userId);
        }

        private async Task SendSpinsInvoice(ITelegramBotClient botClient, long chatId, int amount, CancellationToken ct, long userId = 0)
        {
            if (amount <= 0)
            {
                await botClient.SendMessage(chatId, "Некорректное количество спинов.", cancellationToken: ct);
                return;
            }
            string playerId = _linkedPlayerId[chatId];
            int price = amount * StarsPerSpin;
            string payload = $"spins|{amount}|{playerId}";
            await SendInvoiceSafe(botClient, chatId, $"Battle Pass: {amount} спинов", $"Покупка {amount} спинов БП на аккаунт {playerId}", payload, price, ct, userId);
        }

        private async Task SendCustomIdInvoice(ITelegramBotClient botClient, long chatId, string newId, CancellationToken ct, long userId = 0)
        {
            string playerId = _linkedPlayerId[chatId];
            string payload = $"customid|{newId}|{playerId}";
            await SendInvoiceSafe(botClient, chatId, "Custom ID", $"Смена Custom ID на «{newId}» для аккаунта {playerId}", payload, CustomIdPriceStars, ct, userId);
        }

        // Тестовая кнопка для админов - проверяет ВЕСЬ путь целиком: SendInvoice ->
        // PreCheckoutQuery -> SuccessfulPayment -> реальная выдача предмета в инвентарь
        // привязанного playerId (медаль #100, безобидная и легко узнаваемая в игре).
        private async Task SendTestInvoice(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            if (!_linkedPlayerId.TryGetValue(chatId, out string playerId))
            {
                await botClient.SendMessage(chatId, "Сначала привяжи playerId - иначе некому выдавать тестовый предмет.", cancellationToken: ct);
                return;
            }
            long userId = 0;
            string payload = $"test|{playerId}";
            await SendInvoiceSafe(botClient, chatId, "Тестовый платёж", "Проверка оплаты и выдачи предмета (админ-тест: выдаст медаль #100)", payload, TestItemPriceStars, ct, userId);
        }

        // ---------- Fulfilment ----------

        private async Task HandleSuccessfulPayment(ITelegramBotClient botClient, Message message, CancellationToken ct)
        {
            long chatId = message.Chat.Id;
            long userId = message.From?.Id ?? 0;
            var payment = message.SuccessfulPayment;
            Console.WriteLine($"[DonateBot] SuccessfulPayment payload='{payment.InvoicePayload}' charge={payment.TelegramPaymentChargeId} user={userId}");
            await FulfilPayload(botClient, chatId, payment.InvoicePayload, ct, userId);
            await ShowMainMenu(botClient, chatId, ct, userId);
        }

        private static bool TryResolvePlayerId(string input, out string playerId, out string error)
        {
            playerId = "";
            error = "";
            input = (input ?? "").Trim();
            if (input.StartsWith("@")) input = input.Substring(1).Trim();
            if (input.StartsWith("`") && input.EndsWith("`") && input.Length > 2)
                input = input.Substring(1, input.Length - 2).Trim();
            input = System.Text.RegularExpressions.Regex.Replace(input, "<.*?>", "").Trim();
            if (string.IsNullOrEmpty(input))
            {
                error = "пустой uid";
                return false;
            }
            if (ObjectId.TryParse(input, out ObjectId oid))
            {
                try
                {
                    var doc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);
                    if (doc != null)
                    {
                        playerId = oid.ToString();
                        return true;
                    }
                }
                catch { }
                // ObjectId валидный формат, но документа нет — всё равно пробуем как id
                playerId = oid.ToString();
                return true;
            }
            var byUid = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(input);
            if (byUid != null && byUid.Length > 0)
            {
                playerId = byUid[0]._id.ToString();
                return true;
            }
            try
            {
                var byName = BoltMainDatabaseProvider.Instance.FindPlayersByUidOrName(input, 5);
                if (byName != null && byName.Length > 0)
                {
                    // Предпочитаем точное совпадение uid/name
                    var exact = byName.FirstOrDefault(p =>
                        string.Equals(p.uid, input, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(p.name, input, StringComparison.OrdinalIgnoreCase));
                    playerId = (exact ?? byName[0])._id.ToString();
                    return true;
                }
            }
            catch { }
            error = "игрок не найден (uid/ник/ObjectId)";
            return false;
        }

        private static void GrantItem(string playerId, int itemDefinitionId, int quantity = 1)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("empty playerId");
            if (quantity <= 0) quantity = 1;
            var db = BoltGameDatabaseProvider.Instance;
            var oid = ObjectId.Parse(playerId);
            var inv = db.GetPlayerInventoryDocument(oid);
            if (inv == null)
            {
                db.CreatePlayerInventory(oid);
                inv = db.GetPlayerInventoryDocument(oid);
            }
            int nextId = (inv.InventoryItems.ElementCount > 0)
                ? inv.InventoryItems.Select(x => int.TryParse(x.Name, out int parsed) ? parsed : 0).Max() + 1
                : 1;

            var pushed = new List<Axlebolt.Bolt.Protobuf.InventoryItem>();
            for (int i = 0; i < quantity; i++)
            {
                int invId = nextId + i;
                var item = new BoltInventoryItem
                {
                    itemDefinitionId = itemDefinitionId,
                    quantity = 1,
                    flags = 0,
                    date = BsonDateTime.Create(DateTime.UtcNow)
                };
                db.AddItemToPlayerInventoryDocument(oid, item, invId);
                pushed.Add(new Axlebolt.Bolt.Protobuf.InventoryItem
                {
                    Id = invId,
                    ItemDefinitionId = itemDefinitionId,
                    Quantity = 1
                });
            }
            Console.WriteLine($"[DonateBot] Granted {quantity}x item#{itemDefinitionId} to {playerId}");
            try { InventoryRemoteEventListener.PushDelta(playerId, pushed, null); } catch { }

            // Gold Pass: клиент смотрит наличие #608; дублируем #613 на всякий случай + гарантируем premium level.
            if (itemDefinitionId == GoldPassItemDefinitionId)
            {
                try
                {
                    bool has613 = false;
                    inv = db.GetPlayerInventoryDocument(oid);
                    foreach (var el in inv.InventoryItems)
                    {
                        if (!el.Value.IsBsonDocument) continue;
                        var d = el.Value.AsBsonDocument;
                        if (d.Contains("itemDefinitionId") && d["itemDefinitionId"].ToInt32() == 613) { has613 = true; break; }
                    }
                    if (!has613)
                    {
                        int nid = inv.InventoryItems.ElementCount > 0
                            ? inv.InventoryItems.Select(x => int.TryParse(x.Name, out int parsed) ? parsed : 0).Max() + 1
                            : 1;
                        db.AddItemToPlayerInventoryDocument(oid, new BoltInventoryItem
                        {
                            itemDefinitionId = 613,
                            quantity = 1,
                            flags = 0,
                            date = BsonDateTime.Create(DateTime.UtcNow)
                        }, nid);
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[DonateBot] GoldPass extra 613 warn: {ex.Message}"); }

                try
                {
                    var collection = db.GetDatabase.GetCollection<BsonDocument>("game_event_progress");
                    var filter = Builders<BsonDocument>.Filter.Eq("playerId", playerId) & Builders<BsonDocument>.Filter.Eq("eventId", "CURSED_SOULS");
                    var existing = collection.Find(filter).FirstOrDefault();
                    int lvl = 1;
                    if (existing != null && existing.Contains("levels") && existing["levels"].IsBsonDocument)
                    {
                        var lv = existing["levels"].AsBsonDocument;
                        if (lv.Contains("free")) lvl = Math.Max(lvl, lv["free"].ToInt32());
                        if (lv.Contains("premium")) lvl = Math.Max(lvl, lv["premium"].ToInt32());
                    }
                    collection.UpdateOne(filter, Builders<BsonDocument>.Update
                        .Set("levels.free", lvl)
                        .Set("levels.premium", Math.Max(lvl, 1))
                        .Set("updateDate", DateTime.UtcNow)
                        .SetOnInsert("playerId", playerId)
                        .SetOnInsert("eventId", "CURSED_SOULS")
                        .SetOnInsert("points", 0)
                        .SetOnInsert("challengeProgress", new BsonDocument()),
                        new UpdateOptions { IsUpsert = true });
                    try { GameEventRemoteService.InvalidatePlayerEventsCache(); } catch { }
                }
                catch (Exception ex) { Console.WriteLine($"[DonateBot] GoldPass progress warn: {ex.Message}"); }

                try { GameEventRemoteService.OnGoldPassAcquired(playerId); }
                catch (Exception ex) { Console.WriteLine($"[DonateBot] GoldPass rewards warn: {ex.Message}"); }
            }
        }

        private static void GrantGold(string playerId, int amount)
            => GrantGold(playerId, (long)amount, 0);

        private static readonly System.Collections.Concurrent.ConcurrentQueue<string> GoldGrantHistory
            = new System.Collections.Concurrent.ConcurrentQueue<string>();

        private static void GrantGold(string playerId, long amount, long adminTgId = 0)
        {
            if (amount <= 0 || string.IsNullOrWhiteSpace(playerId)) return;
            // CurrencyPlusValue принимает double — без потолка сверху.
            BoltGameDatabaseProvider.Instance.CurrencyPlusValue(ObjectId.Parse(playerId), 102, amount);
            string line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z adminTg={adminTgId} -> {playerId} +{amount} gold";
            GoldGrantHistory.Enqueue(line);
            while (GoldGrantHistory.Count > 30 && GoldGrantHistory.TryDequeue(out _)) { }
            Console.WriteLine($"[DonateBot] {line}");
            Logger.Log($"[DonateBot] {line}");
        }

        private static string FormatGoldGrantHistory()
        {
            var arr = GoldGrantHistory.ToArray();
            if (arr.Length == 0) return "";
            return string.Join("\n", arr.Reverse().Take(10));
        }

        private async Task ShowMarketWhitelistMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            bool closed = false;
            try { closed = BoltGameDatabaseProvider.Instance.GetMarketClosed(); } catch { }
            int count = 0;
            try { count = BoltGameDatabaseProvider.Instance.GetMarketWhitelistIds().Count; } catch { }
            var kb = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить ID", "admin_market_wl_add"), InlineKeyboardButton.WithCallbackData("➖ Удалить ID", "admin_market_wl_remove") },
                new[] { InlineKeyboardButton.WithCallbackData("📋 Список", "admin_market_wl_list") },
                new[] { InlineKeyboardButton.WithCallbackData(closed ? "Рынок: ЗАКРЫТ" : "Рынок: ОТКРЫТ", "admin_market_toggle") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_admin") },
            });
            await botClient.SendMessage(chatId,
                $"Рынок: {(closed ? "ЗАКРЫТ на учёт" : "открыт")}\nWhitelist рынка: {count} ID\n" +
                "При закрытии только эти игроки видят рынок без плашки и могут торговать.",
                replyMarkup: kb, cancellationToken: ct);
        }

        private async Task ShowSpinAdminMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var win = BoltGameDatabaseProvider.Instance.GetSpinWindowState();
            var doc = BoltGameDatabaseProvider.Instance.GetSpinAdminSettings();
            string startS = DateTimeOffset.FromUnixTimeMilliseconds(win.startMs).ToString("u");
            string endS = DateTimeOffset.FromUnixTimeMilliseconds(win.endMs).ToString("u");
            string rem = win.active ? TimeSpan.FromMilliseconds(win.remainingMs).ToString(@"d\.hh\:mm\:ss") : "0";
            var kb = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(doc.enabled ? "Спин: ВКЛ" : "Спин: ВЫКЛ", "admin_spin_toggle") },
                new[] { InlineKeyboardButton.WithCallbackData("Старт…", "admin_spin_set_start"), InlineKeyboardButton.WithCallbackData("Конец…", "admin_spin_set_end") },
                new[] { InlineKeyboardButton.WithCallbackData("+1 сутки", "admin_spin_extend_1d"), InlineKeyboardButton.WithCallbackData("−1 сутки", "admin_spin_shorten_1d") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_admin") },
            });
            await botClient.SendMessage(chatId,
                $"🎡 Окно спина (серверное время UTC)\n" +
                $"enabled={doc.enabled}\nactive_now={win.active}\n" +
                $"start={startS}\nend={endS}\nremaining={rem}\n\n" +
                "Вне окна / при ВЫКЛ спин недоступен сразу (без рестарта сервера).",
                replyMarkup: kb, cancellationToken: ct);
        }

        private static bool TryParseSpinAdminTime(string text, out long unixMs)
        {
            unixMs = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (string.Equals(text, "now", StringComparison.OrdinalIgnoreCase))
            {
                unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return true;
            }
            if (text.StartsWith("+", StringComparison.Ordinal) && text.EndsWith("d", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(text.Substring(1, text.Length - 2), out int days) && days > 0)
            {
                unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + days * 86_400_000L;
                return true;
            }
            if (long.TryParse(text, out long raw))
            {
                // секунды vs ms
                unixMs = raw < 10_000_000_000L ? raw * 1000L : raw;
                return unixMs > 0;
            }
            string[] formats = { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd" };
            if (DateTime.TryParseExact(text, formats, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out DateTime dt))
            {
                unixMs = new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeMilliseconds();
                return true;
            }
            if (DateTimeOffset.TryParse(text, out DateTimeOffset dto))
            {
                unixMs = dto.ToUnixTimeMilliseconds();
                return true;
            }
            return false;
        }

        private static bool AddBattlePassLevels(string playerId, int amount)
        {
            if (amount <= 0) return false;
            var db = BoltGameDatabaseProvider.Instance;
            // Всегда CURSED_SOULS — клиент 0.17.
            string eventCode = "CURSED_SOULS";
            var collection = db.GetDatabase.GetCollection<BsonDocument>("game_event_progress");
            var filter = Builders<BsonDocument>.Filter.Eq("playerId", playerId) & Builders<BsonDocument>.Filter.Eq("eventId", eventCode);
            var existing = collection.Find(filter).FirstOrDefault();

            int free = 1;
            int prem = 1;
            if (existing != null && existing.Contains("levels") && existing["levels"].IsBsonDocument)
            {
                var lv = existing["levels"].AsBsonDocument;
                if (lv.Contains("free")) free = Math.Max(1, lv["free"].ToInt32());
                if (lv.Contains("premium")) prem = Math.Max(0, lv["premium"].ToInt32());
            }
            int synced = Math.Max(free, prem);
            if (synced < 1) synced = 1;
            int next = Math.Min(180, synced + amount);
            // Points в документе — прогресс ВНУТРИ уровня (0..9), не абсолютные очки.
            // Раньше писали (next-1)*10 → квесты бампили уровни пачками.
            const int withinPoints = 0;
            bool hasGold = GameEventRemoteService.PlayerOwnsGoldPass(playerId);
            int premiumNext = hasGold ? next : Math.Max(0, prem);

            var update = Builders<BsonDocument>.Update
                .Set("levels.free", next)
                .Set("levels.premium", hasGold ? next : premiumNext)
                .Set("points", withinPoints)
                // Сбрасываем claimed до предыдущего уровня — иначе награды за новые уровни не выдаются
                // (ClaimedLevels уже = max после пустых grant'ов).
                .Set("claimedLevels.free", Math.Max(0, synced - 1))
                .Set("claimedLevels.premium", hasGold ? Math.Max(0, synced - 1) : Math.Max(0, prem))
                .Set("updateDate", DateTime.UtcNow)
                .SetOnInsert("playerId", playerId)
                .SetOnInsert("eventId", eventCode)
                .SetOnInsert("challengeProgress", new BsonDocument());

            collection.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });

            // Дублируем на legacy eventId из CursedSouls.json (HOT_WINTER_PARTY), на случай старых клиентов/кэшей.
            try
            {
                foreach (string legacyId in new[] { "HOT_WINTER_PARTY", "HOT_WINTER_PARTY_2023" })
                {
                    var legFilter = Builders<BsonDocument>.Filter.Eq("playerId", playerId) & Builders<BsonDocument>.Filter.Eq("eventId", legacyId);
                    collection.UpdateOne(legFilter, update, new UpdateOptions { IsUpsert = true });
                }
            }
            catch (Exception legEx) { Console.WriteLine($"[DonateBot] BP legacy sync warn: {legEx.Message}"); }

            try { GameEventRemoteService.InvalidatePlayerEventsCache(); } catch { }
            try { GameEventRemoteService.ForceGrantPendingLevelRewards(playerId); } catch (Exception grantEx) { Console.WriteLine($"[DonateBot] BP grant warn: {grantEx.Message}"); }

            Console.WriteLine($"[DonateBot] BP levels {synced}->{next} (withinPts=0, gold={hasGold}) for {playerId} event={eventCode}");
            return true;
        }

        private void AddPromoDraftItem(long chatId, int defId, int qty)
        {
            if (defId <= 0) return;
            qty = Math.Clamp(qty, 1, 1000);
            var list = _promoItemDraft.GetOrAdd(chatId, _ => new List<Tuple<int, int>>());
            int idx = list.FindIndex(t => t.Item1 == defId);
            if (idx >= 0)
                list[idx] = Tuple.Create(defId, Math.Clamp(list[idx].Item2 + qty, 1, 1000));
            else
                list.Add(Tuple.Create(defId, qty));
            _promoItemDraft[chatId] = list;
            _promoRewardKind[chatId] = "item";
        }

        private string DescribePromoDraft(long chatId)
        {
            if (!_promoDraft.TryGetValue(chatId, out var d))
                return "Черновик промокода потерян.";
            var items = _promoItemDraft.GetValueOrDefault(chatId) ?? new List<Tuple<int, int>>();
            int gold = _promoGoldDraft.GetValueOrDefault(chatId, 0);

            var lines = new List<string>();
            if (gold > 0) lines.Add($"• 💰 Голда ×{gold}");
            if (items.Count > 0)
            {
                InventoryCatalogueLoader.Instance.EnsureLoaded();
                var all = InventoryCatalogueLoader.Instance.GetAll();
                foreach (var t in items)
                {
                    var def = all?.FirstOrDefault(x => x != null && x.key == t.Item1);
                    string name = def?.displayName ?? $"#{t.Item1}";
                    string icon = t.Item1 == SpinTokenItemDefinitionId ? "🎟"
                        : t.Item1 == GoldPassItemDefinitionId ? "🏆" : "🎨";
                    lines.Add($"• {icon} {name} (#{t.Item1}) ×{t.Item2}");
                }
            }
            string list = lines.Count == 0
                ? "(пусто — добавь голду, спины, Gold Pass или предметы)"
                : string.Join("\n", lines);

            return $"🎫 Промо `{d.code}` — активаций: {d.maxUses}\n\nВ промокоде:\n{list}";
        }

        private async Task ShowPromoDraftMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            bool empty = (_promoGoldDraft.GetValueOrDefault(chatId, 0) <= 0)
                && ((_promoItemDraft.GetValueOrDefault(chatId)?.Count ?? 0) == 0);

            var rows = new List<InlineKeyboardButton[]>
            {
                // Награды складываются в одну корзину, а не выбираются одна вместо другой.
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💰 Голда", "admin_promo_gold"),
                    InlineKeyboardButton.WithCallbackData("🎟 Спины", "admin_promo_spins")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🏆 Gold Pass", "admin_promo_goldpass"),
                    InlineKeyboardButton.WithCallbackData("🎨 Скины и предметы", "promo_menu")
                },
            };
            if (!empty)
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData("✅ Создать промокод", "promo_done"),
                                 InlineKeyboardButton.WithCallbackData("🗑 Очистить", "promo_clear") });
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Отмена", "admin_promo") });

            string hint = empty
                ? "\n\nДобавь хотя бы одну награду — кнопка создания появится."
                : "\n\nМожно добавлять сколько угодно наград подряд. Ещё можно ввести id текстом (`141600` / `702 x2`).";

            await botClient.SendMessage(chatId, DescribePromoDraft(chatId) + hint,
                parseMode: ParseMode.Markdown, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private static StandRiseServer.MongoDB.Game.BoltInventoryItemDefinitionDocument FindDef(int key)
        {
            try
            {
                InventoryCatalogueLoader.Instance.EnsureLoaded();
                return InventoryCatalogueLoader.Instance.GetAll()?.FirstOrDefault(d => d != null && d.key == key);
            }
            catch { return null; }
        }

        /// <summary>StatTrak-двойник предмета: тот же скин с ключом ±1 000 000.</summary>
        private static StandRiseServer.MongoDB.Game.BoltInventoryItemDefinitionDocument FindStattrackTwin(
            StandRiseServer.MongoDB.Game.BoltInventoryItemDefinitionDocument def)
        {
            if (def == null) return null;
            int twinKey = def.IsStattrack() ? def.key - 1_000_000 : def.key + 1_000_000;
            if (twinKey <= 0) return null;
            var twin = FindDef(twinKey);
            if (twin == null) return null;
            return twin.IsStattrack() != def.IsStattrack() ? twin : null;
        }

        /// <summary>Экран выбора способа: поиск / коллекции / по id.</summary>
        private async Task ShowPromoPickModeMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var kb = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🔍 Поиск по названию", "promo_search") },
                new[] { InlineKeyboardButton.WithCallbackData("📚 По коллекциям", "promo_cols") },
                new[] { InlineKeyboardButton.WithCallbackData("К черновику", "promo_menu_draft") },
            });
            await botClient.SendMessage(chatId,
                DescribePromoDraft(chatId) + "\n\nКак искать предмет?\nМожно просто написать название или id в чат — например `Night Fury` или `144400 x3`.",
                parseMode: ParseMode.Markdown, replyMarkup: kb, cancellationToken: ct);
        }

        /// <summary>Результаты поиска по каталогу, постранично.</summary>
        private async Task ShowPromoSearchResults(ITelegramBotClient botClient, long chatId, string query, int page, CancellationToken ct)
        {
            var found = SearchCatalogueByName(query, 200);
            if (found.Count == 0)
            {
                var kbNone = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("🔍 Искать снова", "promo_search") },
                    new[] { InlineKeyboardButton.WithCallbackData("К черновику", "promo_menu_draft") },
                });
                await botClient.SendMessage(chatId, $"По запросу «{query}» ничего не найдено. Попробуй часть названия или id.",
                    replyMarkup: kbNone, cancellationToken: ct);
                return;
            }

            const int perPage = 8;
            int totalPages = (int)Math.Ceiling(found.Count / (double)perPage);
            page = Math.Max(0, Math.Min(page, totalPages - 1));

            var rows = new List<InlineKeyboardButton[]>();
            foreach (var item in found.Skip(page * perPage).Take(perPage))
            {
                string label = item.displayName ?? $"#{item.key}";
                if (item.IsStattrack()) label = "ST " + label;
                if (label.Length > 50) label = label.Substring(0, 47) + "…";
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(label, $"pitem:{item.key}") });
            }
            var nav = new List<InlineKeyboardButton>();
            if (page > 0) nav.Add(InlineKeyboardButton.WithCallbackData("←", $"psr:{page - 1}"));
            if (page < totalPages - 1) nav.Add(InlineKeyboardButton.WithCallbackData("→", $"psr:{page + 1}"));
            if (nav.Count > 0) rows.Add(nav.ToArray());
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🔍 Новый поиск", "promo_search"),
                             InlineKeyboardButton.WithCallbackData("К черновику", "promo_menu_draft") });

            await botClient.SendMessage(chatId,
                $"🔍 «{query}» — найдено {found.Count} (стр. {page + 1}/{totalPages}):",
                replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        /// <summary>
        /// Карточка предмета: тут выбирается количество и вариант StatTrak,
        /// вместо прежнего «нажал — молча добавилось ×1 обычного».
        /// </summary>
        private async Task ShowPromoItemCard(ITelegramBotClient botClient, long chatId, int key, CancellationToken ct)
        {
            var def = FindDef(key);
            if (def == null)
            {
                await botClient.SendMessage(chatId, $"Предмет #{key} не найден в каталоге.", cancellationToken: ct);
                await ShowPromoPickModeMenu(botClient, chatId, ct);
                return;
            }

            bool st = def.IsStattrack();
            var twin = FindStattrackTwin(def);
            string rarity = RarityLabel(def.GetSkinValue());
            string collection = CollectionNames.TryGetValue(def.GetCollectionId(), out var cn) ? cn : def.GetCollectionId().ToString();

            var rows = new List<InlineKeyboardButton[]>();
            if (twin != null)
            {
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(
                    st ? "🔁 Переключить на обычный" : "🔁 Переключить на StatTrak", $"pitem:{twin.key}") });
            }
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("×1", $"pqty:{key}:1"),
                InlineKeyboardButton.WithCallbackData("×2", $"pqty:{key}:2"),
                InlineKeyboardButton.WithCallbackData("×5", $"pqty:{key}:5"),
            });
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("×10", $"pqty:{key}:10"),
                InlineKeyboardButton.WithCallbackData("×25", $"pqty:{key}:25"),
                InlineKeyboardButton.WithCallbackData("×100", $"pqty:{key}:100"),
            });
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("✏️ Своё количество", $"pqtyc:{key}") });
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🔍 Поиск", "promo_search"),
                             InlineKeyboardButton.WithCallbackData("К черновику", "promo_menu_draft") });

            string variant = st ? "StatTrak" : "обычный";
            if (twin == null) variant += " (другого варианта в каталоге нет)";

            await botClient.SendMessage(chatId,
                $"🎨 *{def.displayName ?? ("#" + def.key)}*  `#{def.key}`\n"
                + $"Коллекция: {collection}\nРедкость: {rarity}\nВариант: {variant}\n\nСколько добавить в промокод?",
                parseMode: ParseMode.Markdown, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowPromoCollectionsMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var rows = new List<InlineKeyboardButton[]>();
            var pairs = CollectionNames.ToList();
            for (int i = 0; i < pairs.Count; i += 2)
            {
                var row = new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData(pairs[i].Value, $"pcol:{(int)pairs[i].Key}")
                };
                if (i + 1 < pairs.Count)
                    row.Add(InlineKeyboardButton.WithCallbackData(pairs[i + 1].Value, $"pcol:{(int)pairs[i + 1].Key}"));
                rows.Add(row.ToArray());
            }
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("К черновику", "promo_menu_draft") });
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Отмена", "admin_promo") });
            await botClient.SendMessage(chatId,
                DescribePromoDraft(chatId) + "\n\nВыбери коллекцию (можно несколько разных предметов):",
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowPromoRarityMenu(ITelegramBotClient botClient, long chatId, CollectionId col, CancellationToken ct)
        {
            var rows = new List<InlineKeyboardButton[]>();
            foreach (var rarity in new[] { SkinValue.Rare, SkinValue.Epic, SkinValue.Legendary, SkinValue.Arcane })
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(RarityLabel(rarity), $"prar:{(int)col}:{(int)rarity}:0") });
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад к коллекциям", "promo_menu") });
            string name = CollectionNames.TryGetValue(col, out var n) ? n : col.ToString();
            await botClient.SendMessage(chatId, $"{name}\nВыбери редкость:", replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowPromoItemsPage(ITelegramBotClient botClient, long chatId, CollectionId col, SkinValue rarity, int page, CancellationToken ct)
        {
            InventoryCatalogueLoader.Instance.EnsureLoaded();
            var items = InventoryCatalogueLoader.Instance.GetAll()
                .Where(d => d != null && d.properties != null && d.GetCollectionId() == col && d.GetSkinValue() == rarity)
                .OrderBy(d => d.displayName)
                .ToList();
            if (items.Count == 0)
            {
                await botClient.SendMessage(chatId, "В этой категории нет предметов. Выбери другую коллекцию.", cancellationToken: ct);
                await ShowPromoCollectionsMenu(botClient, chatId, ct);
                return;
            }
            const int perPage = 8;
            int totalPages = (int)Math.Ceiling(items.Count / (double)perPage);
            page = Math.Max(0, Math.Min(page, totalPages - 1));
            var pageItems = items.Skip(page * perPage).Take(perPage);
            var rows = new List<InlineKeyboardButton[]>();
            foreach (var item in pageItems)
            {
                string label = item.displayName ?? $"#{item.key}";
                if (item.IsStattrack()) label += " (ST)";
                if (label.Length > 54) label = label.Substring(0, 51) + "…";
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"+ {label}", $"padd:{item.key}") });
            }
            var nav = new List<InlineKeyboardButton>();
            if (page > 0) nav.Add(InlineKeyboardButton.WithCallbackData("<-", $"prar:{(int)col}:{(int)rarity}:{page - 1}"));
            if (page < totalPages - 1) nav.Add(InlineKeyboardButton.WithCallbackData("->", $"prar:{(int)col}:{(int)rarity}:{page + 1}"));
            if (nav.Count > 0) rows.Add(nav.ToArray());
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("К черновику", "promo_menu_draft") });
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", $"pcol:{(int)col}") });
            await botClient.SendMessage(chatId, $"{RarityLabel(rarity)} (стр. {page + 1}/{totalPages})\nНажми предмет — откроется карточка с количеством и StatTrak.",
                replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowPromoMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var kb = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⚡ Быстрый промокод", "admin_promo_quick") },
                new[] { InlineKeyboardButton.WithCallbackData("Расширенный (скины…)", "admin_promo_create") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_admin") },
            });
            await botClient.SendMessage(chatId,
                "🎫 Промокоды\n\n"
                + "⚡ Быстрый: одной строкой, например\n"
                + "`REWORK10 gold:5000 spins:5 uses:100 gp`\n\n"
                + "Расширенный — корзина со скинами.",
                parseMode: ParseMode.Markdown,
                replyMarkup: kb, cancellationToken: ct);
        }

        private async Task CreatePromoFromAdmin(ITelegramBotClient botClient, long chatId, string code, int uses, int gold, int spins, bool goldPass, CancellationToken ct)
        {
            try
            {
                var items = new List<Tuple<int, int>>();
                var currencies = new List<Tuple<int, int>>();
                if (gold > 0) currencies.Add(Tuple.Create(102, gold));
                if (goldPass) items.Add(Tuple.Create(GoldPassItemDefinitionId, 1));
                if (spins > 0) items.Add(Tuple.Create(SpinTokenItemDefinitionId, spins));
                if (items.Count == 0 && currencies.Count == 0)
                {
                    await botClient.SendMessage(chatId, "Пустой промокод — добавь gold:, spins: или gp", cancellationToken: ct);
                    return;
                }
                var existing = await BoltGameDatabaseProvider.Instance.GetCouponDocument(code);
                if (existing != null)
                {
                    await botClient.SendMessage(chatId, $"Промокод `{code}` уже есть.", parseMode: ParseMode.Markdown, cancellationToken: ct);
                    return;
                }
                await BoltGameDatabaseProvider.Instance.CreateCoupon(code, uses, items, currencies);
                await botClient.SendMessage(chatId,
                    $"✅ `{code}` · uses={uses} · gold={gold} · spins={spins} · gp={goldPass}",
                    parseMode: ParseMode.Markdown, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, "Ошибка: " + ex.Message, cancellationToken: ct);
            }
        }

        private async Task ShowWhitelistMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            bool on = GameWhitelist.IsEnabled();
            bool rankedLock = GameWhitelist.IsRankedQueueLocked();
            int count = GameWhitelist.GetGameWhitelistIds().Count;
            var kb = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(on ? "Выключить whitelist" : "Включить whitelist (закрыть сервер)", on ? "admin_wl_off" : "admin_wl_on") },
                new[] { InlineKeyboardButton.WithCallbackData(rankedLock ? "Открыть союзники/ранк" : "Закрыть союзники/ранк", rankedLock ? "admin_wl_ranked_off" : "admin_wl_ranked_on") },
                new[] { InlineKeyboardButton.WithCallbackData("Добавить UID", "admin_wl_add"), InlineKeyboardButton.WithCallbackData("Удалить UID", "admin_wl_remove") },
                new[] { InlineKeyboardButton.WithCallbackData("Список", "admin_wl_list") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_admin") },
            });
            await botClient.SendMessage(chatId,
                $"Whitelist сервера: {(on ? "ВКЛ — вход только по списку" : "ВЫКЛ — сервер открыт")}\n" +
                $"Союзники/соревнов.: {(rankedLock ? "ЗАКРЫТЫ — только whitelist" : "открыты для всех")}\nID в списке: {count}",
                replyMarkup: kb, cancellationToken: ct);
        }

        /// <summary>
        /// Создаёт промокод из КОРЗИНЫ черновика: голда и предметы вместе.
        /// Старый FinalizePromo умел только что-то одно, из-за чего после выбора голды
        /// добавить скин было невозможно — промокод создавался сразу после ввода суммы.
        /// </summary>
        private async Task FinalizePromoCart(ITelegramBotClient botClient, long chatId, string code, int maxUses, CancellationToken ct)
        {
            try
            {
                var items = new List<Tuple<int, int>>();
                var cartItems = _promoItemDraft.GetValueOrDefault(chatId);
                if (cartItems != null) items.AddRange(cartItems);

                var currencies = new List<Tuple<int, int>>();
                int gold = _promoGoldDraft.GetValueOrDefault(chatId, 0);
                if (gold > 0) currencies.Add(Tuple.Create(102, gold));

                if (items.Count == 0 && currencies.Count == 0)
                {
                    await botClient.SendMessage(chatId, "Промокод пустой — добавь голду или предметы.", cancellationToken: ct);
                    await ShowPromoDraftMenu(botClient, chatId, ct);
                    return;
                }

                var parts = new List<string>();
                if (gold > 0) parts.Add($"{gold} голды");
                if (items.Count > 0)
                {
                    InventoryCatalogueLoader.Instance.EnsureLoaded();
                    var all = InventoryCatalogueLoader.Instance.GetAll();
                    foreach (var t in items)
                    {
                        var def = all?.FirstOrDefault(x => x != null && x.key == t.Item1);
                        parts.Add($"{def?.displayName ?? $"#{t.Item1}"} ×{t.Item2}");
                    }
                }
                string rewardDesc = string.Join(", ", parts);

                await BoltGameDatabaseProvider.Instance.CreateCoupon(code, maxUses, items, currencies);
                Console.WriteLine($"[Promo] Создан промокод {code}: активаций={maxUses}, голда={gold}, предметов={items.Count}");

                _promoDraft.TryRemove(chatId, out _);
                _promoRewardKind.TryRemove(chatId, out _);
                _promoItemDraft.TryRemove(chatId, out _);
                _promoGoldDraft.TryRemove(chatId, out _);
                _promoSearchQuery.TryRemove(chatId, out _);
                _promoPendingItemDef.TryRemove(chatId, out _);
                _state[chatId] = DonateState.None;

                var kb = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("🎫 Создать ещё один", "admin_promo_create") },
                    new[] { InlineKeyboardButton.WithCallbackData("Назад", "admin_promo") },
                });
                await botClient.SendMessage(chatId,
                    $"✅ Промокод создан.\nКод: `{code}`\nАктиваций: {maxUses}\nНаграда: {rewardDesc}",
                    parseMode: ParseMode.Markdown, replyMarkup: kb, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, $"Ошибка создания промо: {ex.Message}", cancellationToken: ct);
            }
        }

        private async Task FinalizePromo(ITelegramBotClient botClient, long chatId, string code, int maxUses, string kind, int amount, CancellationToken ct)
        {
            try
            {
                var items = new List<Tuple<int, int>>();
                var currencies = new List<Tuple<int, int>>();
                string rewardDesc;
                if (kind == "goldpass")
                {
                    items.Add(Tuple.Create(GoldPassItemDefinitionId, 1));
                    rewardDesc = "Gold Pass (#608)";
                }
                else if (kind == "spins")
                {
                    items.Add(Tuple.Create(SpinTokenItemDefinitionId, Math.Max(1, amount)));
                    rewardDesc = $"{amount}× Spin Token (#201)";
                }
                else if (kind == "item")
                {
                    if (_promoItemDraft.TryGetValue(chatId, out var draftItems) && draftItems != null && draftItems.Count > 0)
                        items.AddRange(draftItems);
                    if (items.Count == 0)
                    {
                        await botClient.SendMessage(chatId, "Нет предметов в промокоде.", cancellationToken: ct);
                        return;
                    }
                    rewardDesc = string.Join(", ", items.Select(t => $"#{t.Item1}×{t.Item2}"));
                }
                else
                {
                    currencies.Add(Tuple.Create(102, Math.Max(1, amount)));
                    rewardDesc = $"{amount} голды";
                }

                await BoltGameDatabaseProvider.Instance.CreateCoupon(code, maxUses, items, currencies);
                _promoDraft.TryRemove(chatId, out _);
                _promoRewardKind.TryRemove(chatId, out _);
                _promoItemDraft.TryRemove(chatId, out _);
                _promoPendingItemDef.TryRemove(chatId, out _);
                _state[chatId] = DonateState.None;
                await botClient.SendMessage(chatId,
                    $"Промокод создан.\nКод: `{code}`\nАктиваций: {maxUses}\nНаграда: {rewardDesc}",
                    parseMode: ParseMode.Markdown, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, $"Ошибка создания промо: {ex.Message}", cancellationToken: ct);
            }
        }

        private static List<Tuple<int, int>> ParsePromoItemList(string text)
        {
            var result = new List<Tuple<int, int>>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var raw in text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                int qty = 1;
                int defId;
                // "141600 x3" / "141600x3" / "141600*3"
                var m = System.Text.RegularExpressions.Regex.Match(part, @"^\s*(\d+)\s*[xX\*]\s*(\d+)\s*$");
                if (m.Success)
                {
                    defId = int.Parse(m.Groups[1].Value);
                    qty = Math.Clamp(int.Parse(m.Groups[2].Value), 1, 1000);
                }
                else if (int.TryParse(part, out defId))
                {
                    qty = 1;
                }
                else continue;
                if (defId <= 0) continue;
                result.Add(Tuple.Create(defId, qty));
            }
            return result;
        }

        private static List<StandRiseServer.MongoDB.Game.BoltInventoryItemDefinitionDocument> SearchCatalogueByName(string query, int limit)
        {
            var result = new List<StandRiseServer.MongoDB.Game.BoltInventoryItemDefinitionDocument>();
            if (string.IsNullOrWhiteSpace(query) || limit <= 0) return result;
            string q = query.Trim();
            try
            {
                InventoryCatalogueLoader.Instance.EnsureLoaded();
                result = InventoryCatalogueLoader.Instance.GetAll()
                    .Where(d => d != null && d.enabled && !string.IsNullOrEmpty(d.displayName)
                                && d.displayName.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.displayName.Length)
                    .ThenBy(d => d.displayName)
                    .Take(limit)
                    .ToList();
            }
            catch { }
            return result;
        }

        private static bool TrySetCustomId(string playerId, string newId, out string reason)
        {
            reason = "";
            if (!ObjectId.TryParse(playerId, out ObjectId oid))
            {
                reason = "неверный playerId";
                return false;
            }
            var player = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);
            if (player == null)
            {
                reason = "player not found";
                return false;
            }

            bool ok = BoltMainDatabaseProvider.Instance.SetPlayerUidByObjectId(oid, newId);
            if (!ok)
                ok = BoltMainDatabaseProvider.Instance.SetPlayerUid(player.uid, newId);
            if (!ok) reason = "id занят или не найден исходный uid";
            return ok;
        }
    }
}
