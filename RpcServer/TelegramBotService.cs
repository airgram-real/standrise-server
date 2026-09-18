using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using StandRiseServer.MongoDB;
using MongoDB.Bson;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using StandRiseServer.RpcServer;
using System.IO;
using System.Text.Json;
using StandRiseServer.MongoDB.Game;
using Telegram.Bot.Types.Payments;
using StandRiseServer.RpcServer.Api;

namespace StandRiseServer.RpcServer
{
    public class TelegramBotService
    {
        private ITelegramBotClient _botClient;
        private CancellationTokenSource _cts;
        private BotRoleManager _roleManager;
        private string _botToken;
        private readonly AmiraAiService _amiraService = new AmiraAiService();

        // Instance-based state tracking
        private ConcurrentDictionary<long, UserState> _userStates = new();
        private ConcurrentDictionary<long, string> _targetUserIds = new();
        private ConcurrentDictionary<long, string> _promoCodes = new();
        private ConcurrentDictionary<long, int> _promoMaxUses = new();
        private ConcurrentDictionary<long, string> _promoItems = new();
        private ConcurrentDictionary<long, string> _promoCurrencies = new();
        private ConcurrentDictionary<long, string> _customItemIds = new();
        private ConcurrentDictionary<long, string> _customItemNames = new();
        private ConcurrentDictionary<long, string> _customItemCategories = new();
        private ConcurrentDictionary<long, int> _customItemPrice = new();
        private ConcurrentDictionary<long, string> _customItemRarity = new();
        private ConcurrentDictionary<long, string> _reasons = new();

        // Universal /pass gate: nobody can use the bot until they send the correct password.
        private const string AccessPassword = "emar10223!";
        private static readonly string PassFile = "bot_pass_users.txt";
        private ConcurrentDictionary<long, byte> _hasPassed = new ConcurrentDictionary<long, byte>();

        public TelegramBotService()
        {
        }

        private void LoadPassedUsers()
        {
            try
            {
                if (System.IO.File.Exists(PassFile))
                {
                    foreach (var line in System.IO.File.ReadAllLines(PassFile))
                    {
                        if (long.TryParse(line.Trim(), out long id)) _hasPassed[id] = 0;
                    }
                }
            }
            catch { }
        }

        private void SavePassedUsers()
        {
            try { System.IO.File.WriteAllLines(PassFile, _hasPassed.Keys.Select(k => k.ToString())); } catch { }
        }

        // Returns true if the update should be processed, false if it was swallowed by the gate.
        private async Task<bool> EnsurePassedAsync(ITelegramBotClient botClient, long userId, long chatId, string text, CancellationToken ct)
        {
            if (userId != 0 && _hasPassed.ContainsKey(userId)) return true;

            if (!string.IsNullOrEmpty(text) && text.Trim().Equals("/pass " + AccessPassword, StringComparison.OrdinalIgnoreCase))
            {
                _hasPassed[userId] = 0;
                SavePassedUsers();
                await botClient.SendMessage(chatId, "✅ Пароль принят!", cancellationToken: ct);
                await ShowMainMenu(botClient, chatId, ct);
                return true;
            }

            if (!string.IsNullOrEmpty(text) && text.Trim().StartsWith("/pass ", StringComparison.OrdinalIgnoreCase))
            {
                await botClient.SendMessage(chatId, "❌ Пароль введён неверно. Попробуй ещё раз.", cancellationToken: ct);
            }
            else
            {
                await botClient.SendMessage(chatId, "🔒 Бот защищён паролем.\nВведи команду `/pass` с паролем, чтобы получить доступ к админ-панели.", cancellationToken: ct);
            }
            return false;
        }

        public void Start(string token, List<string> adminUsernames, List<long> adminIds)
        {
            _botClient = new TelegramBotClient(token);
            _botToken = token;
            _roleManager = new BotRoleManager(adminUsernames, adminIds);
            _cts = new CancellationTokenSource();
            LoadItems();
            LoadPassedUsers();

            _botClient.StartReceiving(
            HandleUpdateAsync,
            HandlePollingErrorAsync,
            new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            _cts.Token
            );

            Console.WriteLine($"[Bot] Started with token: {token.Substring(0, 10)}...");
        }

        public static void StartStatic(string token, List<string> adminUsernames, List<long> adminIds)
        {
            var service = new TelegramBotService();
            service.Start(token, adminUsernames, adminIds);
        }
        private class BotRoleManager
        {
            private const string RolesFile ="bot_roles.json";
            private RoleData _data;

            public class ModeratorLimitsData
            {
                public int MaxPromosPerDay { get; set; } = 5;
                public int MaxCurrencyPerPromo { get; set; } = 5000;
            }

            private class RoleData
            {
                public List<string> AdminUsernames { get; set; } = new List<string>();
                public List<long> AdminIds { get; set; } = new List<long>();
                public List<long> ModeratorIds { get; set; } = new List<long>();
                public List<long> JuniorModeratorIds { get; set; } = new List<long>();
                public bool WhitelistEnabled { get; set; } = false;
                public List<long> WhitelistIds { get; set; } = new List<long>();
                public List<string> GameWhitelistIds { get; set; } = new List<string>();
                // Раньше лимит (5 промо/день, 5000 голды за промо) был жёстко зашит в код и
                // применялся ТОЛЬКО к мелким модерам. Теперь любому модеру (и мелкому, и
                // обычному) можно выставить свои лимиты через админ-панель; если для ID нет
                // записи - действуют дефолты из ModeratorLimitsData.
                public Dictionary<long, ModeratorLimitsData> ModeratorLimits { get; set; } = new Dictionary<long, ModeratorLimitsData>();
            }

            public BotRoleManager(List<string> defaultUsernames, List<long> defaultIds)
            {
                _defaultUsernames = defaultUsernames ?? new List<string>();
                _defaultIds = defaultIds ?? new List<long>();
                Load();
            }

            private List<string> _defaultUsernames;
            private List<long> _defaultIds;

            private void Load()
            {
                if (System.IO.File.Exists(RolesFile))
                {
                    try
                    {
                        string json = System.IO.File.ReadAllText(RolesFile);
                        _data = System.Text.Json.JsonSerializer.Deserialize<RoleData>(json);
                    }
                    catch { _data = new RoleData(); }
                }
                else
                {
                    _data = new RoleData();
                }
                _data ??= new RoleData();
                _data.AdminUsernames ??= new List<string>();
                _data.AdminIds ??= new List<long>();
                _data.ModeratorIds ??= new List<long>();
                _data.JuniorModeratorIds ??= new List<long>();
                _data.WhitelistIds ??= new List<long>();
                _data.GameWhitelistIds ??= new List<string>();
                _data.ModeratorLimits ??= new Dictionary<long, ModeratorLimitsData>();

                // Ensure default admins from token config
                foreach (var def in _defaultUsernames)
                {
                    if (!_data.AdminUsernames.Contains(def.ToLower())) _data.AdminUsernames.Add(def.ToLower());
                }
                foreach (var id in _defaultIds)
                {
                    if (!_data.AdminIds.Contains(id)) _data.AdminIds.Add(id);
                }
                Save();
            }

            public void Save()
            {
                string json = System.Text.Json.JsonSerializer.Serialize(_data);
                System.IO.File.WriteAllText(RolesFile, json);
            }

            public bool IsAdmin(string username, long id)
            {
                return (username != null && _data.AdminUsernames.Contains(username.ToLower())) || _data.AdminIds.Contains(id);
            }

            public bool IsModerator(string username, long id)
            {
                return IsAdmin(username, id) || _data.ModeratorIds.Contains(id);
            }

            public bool IsWhitelistEnabled() => _data.WhitelistEnabled;

            public bool IsWhitelisted(long id)
            {
                return _data.WhitelistIds.Contains(id);
            }

            public bool IsGameWhitelisted(string id)
            {
                return !string.IsNullOrWhiteSpace(id) && _data.GameWhitelistIds.Contains(id);
            }

            public void SetWhitelistEnabled(bool enabled)
            {
                _data.WhitelistEnabled = enabled;
                Save();
            }

            public void AddWhitelist(long id)
            {
                if (!_data.WhitelistIds.Contains(id))
                {
                    _data.WhitelistIds.Add(id);
                    Save();
                }
            }

            public void RemoveWhitelist(long id)
            {
                if (_data.WhitelistIds.Remove(id))
                {
                    Save();
                }
            }

            public void AddGameWhitelist(string id)
            {
                id = id?.Trim();
                if (!string.IsNullOrWhiteSpace(id) && !_data.GameWhitelistIds.Contains(id))
                {
                    _data.GameWhitelistIds.Add(id);
                    Save();
                }
            }

            public void RemoveGameWhitelist(string id)
            {
                id = id?.Trim();
                if (!string.IsNullOrWhiteSpace(id) && _data.GameWhitelistIds.Remove(id))
                {
                    Save();
                }
            }

            public List<long> GetWhitelistIds() => _data.WhitelistIds;
            public List<string> GetGameWhitelistIds() => _data.GameWhitelistIds;

            public void AddModerator(long id)
            {
                if (!_data.ModeratorIds.Contains(id))
                {
                    _data.ModeratorIds.Add(id);
                    Save();
                }
            }

            public void RemoveModerator(long id)
            {
                if (_data.ModeratorIds.Remove(id))
                {
                    Save();
                }
            }

            public List<long> GetModerators() => _data.ModeratorIds;
            public List<long> GetAdminIds() => _data.AdminIds;

            public bool IsJuniorModerator(long id)
            {
                return _data.JuniorModeratorIds.Contains(id);
            }

            public void AddJuniorModerator(long id)
            {
                if (!_data.JuniorModeratorIds.Contains(id))
                {
                    _data.JuniorModeratorIds.Add(id);
                    Save();
                }
            }

            public void RemoveJuniorModerator(long id)
            {
                if (_data.JuniorModeratorIds.Remove(id))
                {
                    Save();
                }
            }

            public ModeratorLimitsData GetModeratorLimits(long id)
            {
                return _data.ModeratorLimits.TryGetValue(id, out var l) ? l : new ModeratorLimitsData();
            }

            public void SetModeratorLimits(long id, int maxPromosPerDay, int maxCurrencyPerPromo)
            {
                _data.ModeratorLimits[id] = new ModeratorLimitsData
                {
                    MaxPromosPerDay = Math.Max(0, maxPromosPerDay),
                    MaxCurrencyPerPromo = Math.Max(0, maxCurrencyPerPromo)
                };
                Save();
            }

            public Dictionary<long, ModeratorLimitsData> GetAllModeratorLimits() => _data.ModeratorLimits;
        }

        private enum UserState
        {
            None,
            WaitingForKickUserId,
            WaitingForBanUserId,
            WaitingForBanReason,
            WaitingForBanCustomCode,
            WaitingForBanCustomReason,
            WaitingForUnbanUserId,
            WaitingForPromoCode,
            WaitingForPromoMaxUses,
            WaitingForPromoItems,
            WaitingForPromoItemSearchQuery,
            WaitingForPromoItemSearchCount,
            WaitingForPromoCurrencies,
            WaitingForPromoCurrencyAmount,
            PromoEditor,
            PromoEditMaxCustom,
            PromoEditCurrencyCustom,
            PromoEditSkinCount,
            WaitingForCustomIdOld,
            WaitingForCustomIdNew,
            WaitingForAddModId,
            WaitingForDelModId,
            WaitingForAddJModId,
            WaitingForDelJModId,
            WaitingForClearInvUid,
            WaitingForSetLevelUserId,
            WaitingForSetLevelValue,
            WaitingForSkinSearchQuery,
            WaitingForWeaponSearchQuery,
            WaitingForRemoveItemEverywhereQuery,
            WaitingForModLimitsTargetId,
            WaitingForModLimitsValues,
            WaitingForNewsTitle,
            WaitingForNewsImage,
            WaitingForNewsLink,
            ChatBot,
            WaitingForGiveItemUid,
            WaitingForGiveItemId,
            WaitingForGiveGoldUid,
            WaitingForGiveGoldAmount,
            WaitingForGiveSpinsUid,
            WaitingForGiveSpinsAmount,
            WaitingForGiveBpUid
        }

        private class ItemDef
        {
            public string displayName { get; set; }
            public int key { get; set; }
            public JsonElement properties { get; set; }
        }

        private List<ItemDef> _cachedItems = new List<ItemDef>();

        private class UserContext
        {
            public UserState State { get; set; } = UserState.None;
            public string PromoCode { get; set; }
            public int MaxUses { get; set; }
            public List<Tuple<int, int>> Items { get; set; } = new List<Tuple<int, int>>();
            public List<Tuple<int, int>> Currencies { get; set; } = new List<Tuple<int, int>>();
            public string TargetUserId { get; set; }
            public string Username { get; set; }
            public int PendingBanCode { get; set; } = 1001;
            public int PendingSearchItemKey { get; set; }
            public string PromoSearchQuery { get; set; }
            public int PendingCurrencyId { get; set; }
            public int PromoPendingSkinKey { get; set; }
            public int PromoBrowseCollection { get; set; }
            public int PromoBrowseRarity { get; set; }
            public bool PromoReturnToEditor { get; set; }
            public long PendingLimitsTargetId { get; set; }
            public string NewsTitle { get; set; }
            public string NewsImage { get; set; }
            public string NewsLink { get; set; }
        }

        private class PendingPromoInfo
        {
            public string Code { get; set; }
            public int Max { get; set; }
            public List<Tuple<int, int>> Items { get; set; }
            public List<Tuple<int, int>> Currencies { get; set; }
            public long CreatorId { get; set; }
            public long CreatorChat { get; set; }
        }

        private ConcurrentDictionary<long, UserContext> _userContexts = new ConcurrentDictionary<long, UserContext>();
        private ConcurrentDictionary<long, int> _modDailyPromos = new ConcurrentDictionary<long, int>();
        private DateTime _lastPromoResetDate = DateTime.UtcNow.Date;
        private ConcurrentDictionary<string, string> _pendingPromos = new ConcurrentDictionary<string, string>();


        private void LoadItems()
        {
            try
            {
                string[] paths = {"StandRise.inventory_item_definition.json",
                    Path.Combine("Data", "StandRise.inventory_item_definition.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StandRise.inventory_item_definition.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "StandRise.inventory_item_definition.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "StandRise.inventory_item_definition.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Data", "StandRise.inventory_item_definition.json")
                };

                string path = null;
                foreach (var p in paths) { if (File.Exists(p)) { path = p; break; } }

                if (path != null)
                {
                    string json = File.ReadAllText(path);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    _cachedItems = JsonSerializer.Deserialize<List<ItemDef>>(json, options) ?? new List<ItemDef>();
                    Console.WriteLine($"[Telegram] Loaded {_cachedItems.Count} items from {path}.");
                    System.IO.File.AppendAllText("bot_log.txt", $"[Items] Loaded {_cachedItems.Count} items from {path}.\n");
                }
                else
                {
                    Console.WriteLine("[Telegram] StandRise.inventory_item_definition.json not found!");
                    System.IO.File.AppendAllText("bot_log.txt", "[Items] JSON file not found in any search paths.\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Telegram] Error loading items: {ex}");
                System.IO.File.AppendAllText("bot_error.txt", $"[Items] Load Error: {ex}\n");
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                var username = update.Message?.From?.Username ?? update.CallbackQuery?.From?.Username;
                long realChatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id ?? 0;
                var context = realChatId != 0 ? _userContexts.GetOrAdd(realChatId, _ => new UserContext()) : null;

                if (username != null && context != null)
                {
                    context.Username = username;
                }

                // Logging
                try
                {
                    System.IO.File.AppendAllText("bot_log.txt", $"[Update] Type: {update.Type}, User: {username}\n");
                } catch {}

                var userId = update.Message?.From?.Id ?? update.CallbackQuery?.From?.Id ?? update.PreCheckoutQuery?.From?.Id ?? 0;

                // Universal /pass gate: everyone must enter the password before using the bot.
                string msgText = update.Message?.Text;
                if (update.Type == UpdateType.Message)
                {
                    if (!await EnsurePassedAsync(botClient, userId, realChatId, msgText, cancellationToken))
                    {
                        return;
                    }
                }
                else if (update.Type == UpdateType.CallbackQuery)
                {
                    if (userId != 0 && !_hasPassed.ContainsKey(userId))
                    {
                        await botClient.AnswerCallbackQuery(update.CallbackQuery.Id, cancellationToken: cancellationToken);
                        return;
                    }
                }

                // Passed users get full access; role system only picks WHICH menu to show.
                bool isStaff = _roleManager.IsModerator(username, userId) || _roleManager.IsJuniorModerator(userId) || _hasPassed.ContainsKey(userId);

                // Гостям (без модерки/младшей модерки) этот бот больше ничего не показывает -
                // ни магазин (вырезан ранее, теперь отдельный донат-бот), ни встроенную
                // поддержку с пересылкой сообщений админам (та система убрана целиком вместе
                // с ShowUserMenu/HandlePublicCallback/ForwardToAdmins).
                if (!isStaff)
                {
                    if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
                    {
                        await botClient.AnswerCallbackQuery(update.CallbackQuery.Id, cancellationToken: cancellationToken);
                    }
                    return;
                }

                if (update.Type == UpdateType.PreCheckoutQuery)
                {
                    await botClient.AnswerPreCheckoutQuery(update.PreCheckoutQuery.Id, cancellationToken: cancellationToken);
                    return;
                }

                if (update.Message?.SuccessfulPayment != null)
                {
                    await HandleSuccessfulPayment(botClient, update.Message, cancellationToken);
                    return;
                }

                if (update.Type == UpdateType.Message && update.Message != null && realChatId != 0)
                {
                    await HandleMessage(botClient, update.Message, cancellationToken);
                }
                else if (update.Type == UpdateType.CallbackQuery)
                {
                    await HandleCallbackQuery(botClient, update.CallbackQuery, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Telegram] Error handling update: {ex}");
                try
                {
                    System.IO.File.AppendAllText("bot_error.txt", $"[Error] {ex}\n");
                    if (update.Message != null)
                    await botClient.SendMessage(update.Message.Chat.Id, $"Critical Error: {ex.Message}", cancellationToken: cancellationToken);
                } catch { /* Ignore sending error if that fails too, but try to log */ }
            }
        }

        private async Task HandleMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var text = message.Text;
            var userId = message.From?.Id ?? 0;
            var username = message.From?.Username;
            var context = _userContexts.GetOrAdd(chatId, _ => new UserContext());

            if (text == "/start")
            {
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                return;
            }

            if (!string.IsNullOrEmpty(text) && _roleManager.IsAdmin(username, userId) && await TryHandleWhitelistCommand(botClient, chatId, text, cancellationToken))
            {
                return;
            }

            if (!string.IsNullOrEmpty(text) && (text.StartsWith("/find", StringComparison.OrdinalIgnoreCase) || text.StartsWith("/search", StringComparison.OrdinalIgnoreCase)))
            {
                var query = text.Substring(text.IndexOf(' ') + 1);
                await SearchItems(botClient, chatId, query, "skin", cancellationToken);
                await ShowMainMenu(botClient, chatId, cancellationToken);
                return;
            }

            // Always use rude/informal style for everyone as requested
            switch (context.State)
            {
                case UserState.ChatBot:
                await HandleAmiraChatAsync(botClient, chatId, userId, username, text, cancellationToken);
                break;

                case UserState.WaitingForKickUserId:
                await KickPlayer(text, chatId, cancellationToken);
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForBanUserId:
                context.TargetUserId = text;
                context.State = UserState.None; // We are going to menu selection

                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("Working (Cheat)", "ban_reason:Working") },
                    new[] { InlineKeyboardButton.WithCallbackData("Toxic", "ban_reason:Toxic") },
                    new[] { InlineKeyboardButton.WithCallbackData("Exploit", "ban_reason:Exploit") },
                    new[] { InlineKeyboardButton.WithCallbackData("Scam", "ban_reason:Scam") },
                    new[] { InlineKeyboardButton.WithCallbackData("Other", "ban_reason:Other") },
                    new[] { InlineKeyboardButton.WithCallbackData("Cancel", "cancel") }
                });

                await botClient.SendMessage(chatId, "Причина бана: ", replyMarkup: keyboard, cancellationToken: cancellationToken);
                break;

                case UserState.WaitingForBanReason:
                await BanPlayer(context.TargetUserId, text, 1001, chatId, cancellationToken);
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForBanCustomCode:
                if (int.TryParse(text.Trim(), out int customBanCode))
                {
                    context.PendingBanCode = customBanCode;
                    context.State = UserState.WaitingForBanCustomReason;
                    await botClient.SendMessage(chatId, "Введите свою причину бана: ", cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "Банкод должен быть числом. Введите банкод: ", cancellationToken: cancellationToken);
                }
                break;

                case UserState.WaitingForBanCustomReason:
                if (string.IsNullOrWhiteSpace(text))
                {
                    await botClient.SendMessage(chatId, "Причина не может быть пустой. Введите причину: ", cancellationToken: cancellationToken);
                    break;
                }
                await BanPlayer(context.TargetUserId, text.Trim(), context.PendingBanCode, chatId, cancellationToken);
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForUnbanUserId:
                await UnbanPlayer(text, chatId, cancellationToken);
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForPromoCode:
                if (text.Trim().Equals("-", StringComparison.OrdinalIgnoreCase))
                {
                    context.PromoCode = null;
                }
                else
                {
                    context.PromoCode = text;
                }
                if (context.PromoReturnToEditor)
                {
                    context.PromoReturnToEditor = false;
                    context.State = UserState.PromoEditor;
                    await ShowPromoEditor(botClient, chatId, 0, cancellationToken);
                }
                else
                {
                    context.State = UserState.WaitingForPromoMaxUses;
                    await botClient.SendMessage(chatId, "Сколько раз можно активировать промокод?", cancellationToken: cancellationToken);
                }
                break;

                case UserState.PromoEditMaxCustom:
                if (text.Trim().Equals("-", StringComparison.OrdinalIgnoreCase))
                {
                    context.MaxUses = 0;
                }
                else if (int.TryParse(text, out int pmax) && pmax > 0)
                {
                    context.MaxUses = pmax;
                }
                else
                {
                    await botClient.SendMessage(chatId, "Нужно положительное число. Попробуй ещё раз: ", cancellationToken: cancellationToken);
                    break;
                }
                context.State = UserState.PromoEditor;
                await ShowPromoEditor(botClient, chatId, 0, cancellationToken);
                break;

                case UserState.PromoEditCurrencyCustom:
                if (text.Trim().Equals("-", StringComparison.OrdinalIgnoreCase))
                {
                    context.Currencies?.RemoveAll(c => c.Item1 == context.PendingCurrencyId);
                }
                else if (int.TryParse(text, out int camount) && camount > 0)
                {
                    context.Currencies ??= new List<Tuple<int, int>>();
                    var existing = context.Currencies.FirstOrDefault(c => c.Item1 == context.PendingCurrencyId);
                    if (existing != null)
                    {
                        context.Currencies.Remove(existing);
                        context.Currencies.Add(new Tuple<int, int>(context.PendingCurrencyId, existing.Item2 + camount));
                    }
                    else
                    {
                        context.Currencies.Add(new Tuple<int, int>(context.PendingCurrencyId, camount));
                    }
                }
                else
                {
                    await botClient.SendMessage(chatId, "Нужно положительное число. Попробуй ещё раз: ", cancellationToken: cancellationToken);
                    break;
                }
                context.State = UserState.PromoEditor;
                await ShowPromoEditor(botClient, chatId, 0, cancellationToken);
                break;

                case UserState.PromoEditSkinCount:
                if (text.Trim().Equals("-", StringComparison.OrdinalIgnoreCase))
                {
                    context.State = UserState.PromoEditor;
                    await ShowPromoEditor(botClient, chatId, 0, cancellationToken);
                }
                else if (int.TryParse(text, out int scount) && scount > 0)
                {
                    context.Items ??= new List<Tuple<int, int>>();
                    context.Items.Add(new Tuple<int, int>(context.PromoPendingSkinKey, scount));
                    string nm = _cachedItems.FirstOrDefault(i => i.key == context.PromoPendingSkinKey)?.displayName ?? $"id {context.PromoPendingSkinKey}";
                    await botClient.SendMessage(chatId, $"Добавлено: {nm} x{scount}.", cancellationToken: cancellationToken);
                    await ShowPromoRaritySkins(botClient, chatId, (CollectionId)context.PromoBrowseCollection, (SkinValue)context.PromoBrowseRarity, 0, 0, cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "Нужно положительное число. Сколько штук? (или «-», чтобы пропустить)", cancellationToken: cancellationToken);
                }
                break;

                case UserState.WaitingForPromoMaxUses:
                if (int.TryParse(text, out int maxUses))
                {
                    context.MaxUses = maxUses;
                    context.State = UserState.WaitingForPromoItems;
                    await SendPromoItemsPrompt(botClient, chatId, cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "Нужно число. Попробуй ещё раз: ", cancellationToken: cancellationToken);
                }
                break;

                case UserState.WaitingForPromoItems:
                if (text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    context.Items = new List<Tuple<int, int>>();
                    context.State = UserState.WaitingForPromoCurrencies;
                    await SendPromoCurrencyPrompt(botClient, chatId, cancellationToken);
                    break;
                }
                try
                {
                    context.Items = ParsePairs(text);
                    context.State = UserState.WaitingForPromoCurrencies;
                    await SendPromoCurrencyPrompt(botClient, chatId, cancellationToken);
                }
                catch
                {
                    await botClient.SendMessage(chatId, "Неверный формат. Нужно id:count,id:count или 'none' - либо воспользуйся кнопкой поиска выше: ", cancellationToken: cancellationToken);
                }
                break;

                case UserState.WaitingForPromoItemSearchQuery:
                context.PromoSearchQuery = text ?? "";
                await SendPromoItemSearchResults(botClient, chatId, text, 0, 0, cancellationToken);
                context.State = UserState.WaitingForPromoItems;
                break;

                case UserState.WaitingForPromoItemSearchCount:
                if (int.TryParse(text, out int addCount) && addCount > 0)
                {
                    context.Items ??= new List<Tuple<int, int>>();
                    context.Items.Add(new Tuple<int, int>(context.PendingSearchItemKey, addCount));
                    string addedName = _cachedItems.FirstOrDefault(i => i.key == context.PendingSearchItemKey)?.displayName ?? $"id {context.PendingSearchItemKey}";
                    context.State = UserState.WaitingForPromoItems;
                    var doneKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Добавить ещё предмет", "promo_search_item") },
                        new[] { InlineKeyboardButton.WithCallbackData("Готово с предметами", "promo_items_done") }
                    });
                    await botClient.SendMessage(chatId, $"Добавлено: {addedName} x{addCount}.\nВсего предметов в промокоде: {context.Items.Count}.", replyMarkup: doneKeyboard, cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "Нужно положительное число. Сколько штук добавить?", cancellationToken: cancellationToken);
                }
                break;

                case UserState.WaitingForPromoCurrencies:
                try
                {
                    var currencies = text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
                        ? new List<Tuple<int, int>>()
                        : ParsePairs(text);
                    await FinalizePromoCreation(botClient, chatId, userId, username, context, currencies, cancellationToken);
                }
                catch (Exception ex)
                {
                    await botClient.SendMessage(chatId, $"Ошибка: {ex.Message}", cancellationToken: cancellationToken);
                    context.State = UserState.None;
                    await ShowMainMenu(botClient, chatId, cancellationToken);
                }
                break;

                case UserState.WaitingForPromoCurrencyAmount:
                if (int.TryParse(text, out int currAmount) && currAmount > 0)
                {
                    context.Currencies ??= new List<Tuple<int, int>>();
                    context.Currencies.Add(new Tuple<int, int>(context.PendingCurrencyId, currAmount));
                    string currName = CurrencyLabel(context.PendingCurrencyId);
                    context.State = UserState.WaitingForPromoCurrencies;
                    var doneCurrKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Голда", "promo_currency:102"), InlineKeyboardButton.WithCallbackData("Серебро", "promo_currency:101") },
                        new[] { InlineKeyboardButton.WithCallbackData("Готово с валютой", "promo_currency_done") }
                    });
                    await botClient.SendMessage(chatId, $"Добавлено: {currName} x{currAmount}.\nВсего валют в промокоде: {context.Currencies.Count}.", replyMarkup: doneCurrKeyboard, cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "Нужно положительное число. Сколько добавить?", cancellationToken: cancellationToken);
                }
                break;

                case UserState.WaitingForCustomIdOld:
                context.TargetUserId = text;
                context.State = UserState.WaitingForCustomIdNew;
                await botClient.SendMessage(chatId, "Какой новый Custom ID поставить?", cancellationToken: cancellationToken);
                break;

                case UserState.WaitingForCustomIdNew:
                string oldUid = context.TargetUserId;
                string newUid = text;

                try
                {
                    bool success = BoltMainDatabaseProvider.Instance.SetPlayerUid(oldUid, newUid);
                    if (success)
                    {
                        ModActionLog.Log(chatId, username, "custom_id", $"old={oldUid} new={newUid}");
                        await botClient.SendMessage(chatId, $"Готово.\nСтарый: {oldUid}\nНовый: {newUid}", cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, $"Не получилось: либо {newUid} уже занят, либо игрок {oldUid} не найден.", cancellationToken: cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    await botClient.SendMessage(chatId, $"Ошибка базы данных: {ex.Message}", cancellationToken: cancellationToken);
                }
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;


                case UserState.WaitingForAddModId:
                if (long.TryParse(text, out long newModId))
                {
                    _roleManager.AddModerator(newModId);
                    ModActionLog.Log(chatId, username, "add_mod", $"targetId={newModId}");
                    await botClient.SendMessage(chatId, $"Теперь этот {newModId} модер.", cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "ID дай, цифрами.", cancellationToken: cancellationToken);
                }
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForDelModId:
                if (long.TryParse(text, out long delModId))
                {
                    _roleManager.RemoveModerator(delModId);
                    ModActionLog.Log(chatId, username, "del_mod", $"targetId={delModId}");
                    await botClient.SendMessage(chatId, $"Снял модерку с {delModId}.", cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "ID дай, цифрами.", cancellationToken: cancellationToken);
                }
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForAddJModId:
                if (long.TryParse(text, out long newJModId))
                {
                    _roleManager.AddJuniorModerator(newJModId);
                    ModActionLog.Log(chatId, username, "add_jmod", $"targetId={newJModId}");
                    await botClient.SendMessage(chatId, $"Теперь этот {newJModId} мелкий модер.", cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "ID дай, цифрами.", cancellationToken: cancellationToken);
                }
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForDelJModId:
                if (long.TryParse(text, out long delJModId))
                {
                    _roleManager.RemoveJuniorModerator(delJModId);
                    ModActionLog.Log(chatId, username, "del_jmod", $"targetId={delJModId}");
                    await botClient.SendMessage(chatId, $"Снял мелкую модерку с {delJModId}.", cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "ID дай, цифрами.", cancellationToken: cancellationToken);
                }
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForClearInvUid:
                await ClearInventory(text, chatId, cancellationToken);
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForSetLevelUserId:
                context.TargetUserId = text;
                context.State = UserState.WaitingForSetLevelValue;
                await botClient.SendMessage(chatId, "Какой уровень поставить игроку?: ", cancellationToken: cancellationToken);
                break;

                case UserState.WaitingForSetLevelValue:
                if (int.TryParse(text, out int setLevelVal))
                {
                    await SetPlayerLevel(context.TargetUserId, setLevelVal, chatId, cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "Уровень должен быть числом.", cancellationToken: cancellationToken);
                }
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForGiveBpUid:
                await GiveGoldPass(text, chatId, username, cancellationToken);
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForGiveGoldUid:
                context.TargetUserId = text;
                context.State = UserState.WaitingForGiveGoldAmount;
                await botClient.SendMessage(chatId, "Сколько голды выдать?", cancellationToken: cancellationToken);
                break;

                case UserState.WaitingForGiveGoldAmount:
                if (int.TryParse(text, out int goldAmount) && goldAmount > 0)
                {
                    await GiveGold(context.TargetUserId, goldAmount, chatId, username, cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "Количество должно быть положительным числом.", cancellationToken: cancellationToken);
                }
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForGiveSpinsUid:
                context.TargetUserId = text;
                context.State = UserState.WaitingForGiveSpinsAmount;
                await botClient.SendMessage(chatId, "Сколько спинов (Spin Token #201) выдать?", cancellationToken: cancellationToken);
                break;

                case UserState.WaitingForGiveSpinsAmount:
                if (int.TryParse(text, out int spinsAmount) && spinsAmount > 0 && spinsAmount <= 10000)
                {
                    await GiveSpins(context.TargetUserId, spinsAmount, chatId, username, cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "Количество: число от 1 до 10000.", cancellationToken: cancellationToken);
                }
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForSkinSearchQuery:
                await SearchItems(botClient, chatId, text, "skin", cancellationToken);
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForWeaponSearchQuery:
                await SearchItems(botClient, chatId, text, "weapon", cancellationToken);
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForRemoveItemEverywhereQuery:
                await RemoveItemEverywhere(botClient, chatId, text, cancellationToken);
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case UserState.WaitingForModLimitsTargetId:
                if (long.TryParse(text.Trim(), out long limitsTargetId))
                {
                    context.PendingLimitsTargetId = limitsTargetId;
                    var curLimits = _roleManager.GetModeratorLimits(limitsTargetId);
                    context.State = UserState.WaitingForModLimitsValues;
                    await botClient.SendMessage(chatId, $"Текущие лимиты для {limitsTargetId}: {curLimits.MaxPromosPerDay} промо/день, {curLimits.MaxCurrencyPerPromo} валюты/промо.\nВведи новые значения через пробел (промо/день валюта/промо), например: 10 10000", cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "ID дай, цифрами.", cancellationToken: cancellationToken);
                }
                break;

                case UserState.WaitingForModLimitsValues:
                {
                    var limitParts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (limitParts.Length == 2 && int.TryParse(limitParts[0], out int newMaxPromos) && int.TryParse(limitParts[1], out int newMaxCurrency))
                    {
                        _roleManager.SetModeratorLimits(context.PendingLimitsTargetId, newMaxPromos, newMaxCurrency);
                        ModActionLog.Log(chatId, username, "set_mod_limits", $"targetId={context.PendingLimitsTargetId} maxPromosPerDay={newMaxPromos} maxCurrencyPerPromo={newMaxCurrency}");
                        await botClient.SendMessage(chatId, $"Готово. Лимиты для {context.PendingLimitsTargetId}: {newMaxPromos} промо/день, {newMaxCurrency} валюты/промо.", cancellationToken: cancellationToken);
                        context.State = UserState.None;
                        await ShowMainMenu(botClient, chatId, cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "Нужно два числа через пробел: промо/день валюта/промо. Например: 10 10000", cancellationToken: cancellationToken);
                    }
                    break;
                }

                case UserState.WaitingForNewsTitle:
                if (string.IsNullOrWhiteSpace(text))
                {
                    await botClient.SendMessage(chatId, "Заголовок не может быть пустым. Введи title:", cancellationToken: cancellationToken);
                    break;
                }
                context.NewsTitle = text.Trim();
                context.State = UserState.WaitingForNewsImage;
                await botClient.SendMessage(chatId, "Шаг 2/3 — Пришли картинку (фото/файл) или ссылку на изображение:", cancellationToken: cancellationToken);
                break;

                case UserState.WaitingForNewsImage:
                string img = await ProcessNewsImageAsync(botClient, message, cancellationToken);
                if (img == null)
                {
                    await botClient.SendMessage(chatId, "Картинку не получилось загрузить. Пришли фото/файл или ссылку на изображение, либо напиши '-' если без картинки:", cancellationToken: cancellationToken);
                    break;
                }
                context.NewsImage = img;
                context.State = UserState.WaitingForNewsLink;
                await botClient.SendMessage(chatId, "Шаг 3/3 — Введи ссылку на канал (напр. https://t.me/...), или напиши '-' если не нужна:", cancellationToken: cancellationToken);
                break;

                case UserState.WaitingForNewsLink:
                context.NewsLink = text.Trim().Equals("-", StringComparison.OrdinalIgnoreCase) ? "" : text.Trim();
                await CreateNewsPopup(botClient, chatId, context, cancellationToken);
                break;

                default:
                await SearchItems(botClient, chatId, text, "skin", cancellationToken);
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;
            }
        }

        private async Task HandleCallbackQuery(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var chatId = callbackQuery.Message.Chat.Id;
            var context = _userContexts.GetOrAdd(chatId, _ => new UserContext());

            // Answer callback to remove loading animation
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

            bool callerIsAdmin = _roleManager.IsAdmin(context.Username, callbackQuery.From.Id) || _hasPassed.ContainsKey(callbackQuery.From.Id);

            switch (callbackQuery.Data)
            {
                case "menu_moderation":
                await ShowModerationMenu(botClient, chatId, callerIsAdmin, cancellationToken);
                break;

                case "menu_promo":
                await ShowPromoMenu(botClient, chatId, cancellationToken);
                break;

                case "menu_news":
                await ShowNewsMenu(botClient, chatId, cancellationToken);
                break;

                case "news_create":
                context.State = UserState.WaitingForNewsTitle;
                context.NewsTitle = null;
                context.NewsImage = null;
                context.NewsLink = null;
                await botClient.SendMessage(chatId, "📍 Создание popup-новости.\n\nШаг 1/3 — Введи заголовок (title):", cancellationToken: cancellationToken);
                break;

                case "news_legendsspin":
                await CreateLegendsSpinPopup(botClient, chatId, cancellationToken);
                break;

                case "news_list":
                await ListNewsPopups(botClient, chatId, cancellationToken);
                break;

                case "menu_items":
                await ShowItemsMenu(botClient, chatId, callerIsAdmin, cancellationToken);
                break;

                case "menu_give":
                if (!callerIsAdmin) return;
                await ShowGiveMenu(botClient, chatId, cancellationToken);
                break;

                case "give_goldpass":
                if (!callerIsAdmin) return;
                context.State = UserState.WaitingForGiveBpUid;
                await botClient.SendMessage(chatId, "Кому выдать Gold Pass? (UID):", cancellationToken: cancellationToken);
                break;

                case "give_gold":
                if (!callerIsAdmin) return;
                context.State = UserState.WaitingForGiveGoldUid;
                await botClient.SendMessage(chatId, "Кому выдать голду? (UID):", cancellationToken: cancellationToken);
                break;

                case "give_spins":
                if (!callerIsAdmin) return;
                context.State = UserState.WaitingForGiveSpinsUid;
                await botClient.SendMessage(chatId, "Кому выдать спины (Spin Token)? (UID):", cancellationToken: cancellationToken);
                break;

                case "menu_staff":
                if (!callerIsAdmin) return;
                await ShowStaffMenu(botClient, chatId, cancellationToken);
                break;

                case "chat_bot":
                context.State = UserState.ChatBot;
                _amiraService.ClearHistory(chatId);
                var botKeyboard = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("Выйти из чата", "cancel") } });
                await botClient.SendMessage(chatId, "🌸 Приветик! Я Амира. О чем поболтаем? (Я могу банить или выдавать промики)", replyMarkup: botKeyboard, cancellationToken: cancellationToken);
                break;

                case "menu_whitelist":
                if (!callerIsAdmin) return;
                await ShowWhitelistMenu(botClient, chatId, cancellationToken);
                break;

case "menu_market":
                if (!callerIsAdmin) return;
                await ShowMarketMenu(botClient, chatId, callbackQuery.Message.MessageId, cancellationToken);
                break;

case "mkt_toggle":
                if (!callerIsAdmin) return;
                await ToggleMarketClosed(botClient, chatId, callbackQuery.Message.MessageId, cancellationToken);
                break;

case "mkt_list":
                if (!callerIsAdmin) return;
                await ShowMarketCollections(botClient, chatId, 0, callbackQuery.Message.MessageId, cancellationToken);
                break;

case "update_market":
                if (!callerIsAdmin) return;
                await ShowMarketMenu(botClient, chatId, callbackQuery.Message.MessageId, cancellationToken);
                break;

                case "mod_limits":
                if (!callerIsAdmin) return;
                context.State = UserState.WaitingForModLimitsTargetId;
                await botClient.SendMessage(chatId, "Telegram ID модера, которому меняем лимиты:", cancellationToken: cancellationToken);
                break;

                case "mod_logs":
                if (!callerIsAdmin) return;
                await ShowModLogs(botClient, chatId, null, cancellationToken);
                break;

                case "create_promo":
                context.PromoCode = null;
                context.MaxUses = 0;
                context.Items = new List<Tuple<int, int>>();
                context.Currencies = new List<Tuple<int, int>>();
                context.State = UserState.PromoEditor;
                await ShowPromoEditor(botClient, chatId, (int)callbackQuery.Message.MessageId, cancellationToken);
                break;

                case "create_custom_id":
                context.State = UserState.WaitingForCustomIdOld;
                await botClient.SendMessage(chatId, "Введи текущий UID игрока: ", cancellationToken: cancellationToken);
                break;

                case "kick_player":
                context.State = UserState.WaitingForKickUserId;
                await botClient.SendMessage(chatId, "Кого кикаем? (UID): ", cancellationToken: cancellationToken);
                break;

                case "ban_user":
                context.State = UserState.WaitingForBanUserId;
                await botClient.SendMessage(chatId, "Кого баним? (UID): ", cancellationToken: cancellationToken);
                break;

                case "unban_user":
                context.State = UserState.WaitingForUnbanUserId;
                await botClient.SendMessage(chatId, "Кого разбаниваем? (UID): ", cancellationToken: cancellationToken);
                break;

                case "list_promos":
                await ListPromos(botClient, chatId, 0, cancellationToken);
                break;

                case "list_items":
                await ListItems(botClient, chatId, 0, cancellationToken);
                break;

                case "server_stats":
                await GetServerStats(botClient, chatId, cancellationToken);
                break;

                case "wl_on":
                if (!callerIsAdmin) return;
                _roleManager.SetWhitelistEnabled(true);
                await botClient.SendMessage(chatId, "Whitelist enabled.", cancellationToken: cancellationToken);
                break;

                case "wl_off":
                if (!callerIsAdmin) return;
                _roleManager.SetWhitelistEnabled(false);
                await botClient.SendMessage(chatId, "Whitelist disabled.", cancellationToken: cancellationToken);
                break;

                case "wl_list":
                if (!callerIsAdmin) return;
                await SendWhitelistStatus(botClient, chatId, cancellationToken);
                break;

                case "cancel":
                context.State = UserState.None;
                await ShowMainMenu(botClient, chatId, cancellationToken);
                break;

                case "add_mod":
                context.State = UserState.WaitingForAddModId;
                await botClient.SendMessage(chatId, "Кого в модеры? (Telegram ID): ", cancellationToken: cancellationToken);
                break;

                case "del_mod":
                context.State = UserState.WaitingForDelModId;
                await botClient.SendMessage(chatId, "Кого снять? (Telegram ID): ", cancellationToken: cancellationToken);
                break;

                case "add_jmod":
                context.State = UserState.WaitingForAddJModId;
                await botClient.SendMessage(chatId, "Кого в мелкие модеры? (Telegram ID): ", cancellationToken: cancellationToken);
                break;

                case "del_jmod":
                context.State = UserState.WaitingForDelJModId;
                await botClient.SendMessage(chatId, "Кого снять с мелких? (Telegram ID): ", cancellationToken: cancellationToken);
                break;

                case "clear_inv":
                context.State = UserState.WaitingForClearInvUid;
                await botClient.SendMessage(chatId, "Кому инвентарь чистим? (UID): ", cancellationToken: cancellationToken);
                break;

                case "set_level":
                context.State = UserState.WaitingForSetLevelUserId;
                await botClient.SendMessage(chatId, "Кому ставим уровень? (UID): ", cancellationToken: cancellationToken);
                break;

                case "search_skin":
                context.State = UserState.WaitingForSkinSearchQuery;
                await botClient.SendMessage(chatId, "Введи название скина/предмета или ID: ", cancellationToken: cancellationToken);
                break;

                case "search_weapon":
                context.State = UserState.WaitingForWeaponSearchQuery;
                await botClient.SendMessage(chatId, "Введи название пушки или ID: ", cancellationToken: cancellationToken);
                break;

                case "remove_item_everywhere":
                if (!callerIsAdmin) return;
                context.State = UserState.WaitingForRemoveItemEverywhereQuery;
                await botClient.SendMessage(chatId, "Введи точное/часть названия предмета или ID. Он будет снят у всех и удален с рынка.", cancellationToken: cancellationToken);
                break;

                case "promo_search_item":
                context.State = UserState.WaitingForPromoItemSearchQuery;
                await botClient.SendMessage(chatId, "Введи название предмета (или часть) либо его ID: ", cancellationToken: cancellationToken);
                break;

                case "noop":
                break;

                case "promo_items_done":
                context.Items ??= new List<Tuple<int, int>>();
                context.State = UserState.WaitingForPromoCurrencies;
                await SendPromoCurrencyPrompt(botClient, chatId, cancellationToken);
                break;

                case "promo_currency_done":
                {
                    var finalCurrencies = context.Currencies ?? new List<Tuple<int, int>>();
                    try
                    {
                        await FinalizePromoCreation(botClient, chatId, callbackQuery.From.Id, context.Username, context, finalCurrencies, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await botClient.SendMessage(chatId, $"Ошибка: {ex.Message}", cancellationToken: cancellationToken);
                        context.State = UserState.None;
                        await ShowMainMenu(botClient, chatId, cancellationToken);
                    }
                    break;
                }

                case "promo_edit": // вернуться на панель редактора
                context.State = UserState.PromoEditor;
                await ShowPromoEditor(botClient, chatId, (int)callbackQuery.Message.MessageId, cancellationToken);
                break;

                case "promo_edit_code":
                context.State = UserState.WaitingForPromoCode;
                context.PromoReturnToEditor = true;
                await botClient.SendMessage(chatId, "Введи код промокода (или «-», чтобы убрать): ", cancellationToken: cancellationToken);
                break;

                case "promo_edit_max":
                await ShowPromoMaxPresetMenu(botClient, chatId, (int)callbackQuery.Message.MessageId, cancellationToken);
                break;

                case "promo_max_custom":
                context.State = UserState.PromoEditMaxCustom;
                await botClient.SendMessage(chatId, "Введи число активаций (или «-», чтобы убрать): ", cancellationToken: cancellationToken);
                break;

                case "promo_edit_cur":
                await ShowPromoCurrencyMenu(botClient, chatId, (int)callbackQuery.Message.MessageId, cancellationToken);
                break;

                case "promo_edit_skin":
                await ShowPromoCollectionList(botClient, chatId, 0, (int)callbackQuery.Message.MessageId, cancellationToken);
                break;

                case "promo_edit_finalize":
                {
                    try
                    {
                        if (string.IsNullOrEmpty(context.PromoCode))
                        {
                            await botClient.SendMessage(chatId, "Сначала задай код промокода.", cancellationToken: cancellationToken);
                            break;
                        }
                        await FinalizePromoCreation(botClient, chatId, callbackQuery.From.Id, context.Username, context, context.Currencies ?? new List<Tuple<int, int>>(), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await botClient.SendMessage(chatId, $"Ошибка: {ex.Message}", cancellationToken: cancellationToken);
                        context.State = UserState.None;
                        await ShowMainMenu(botClient, chatId, cancellationToken);
                    }
                    break;
                }

                case "promo_delete_all":
                {
                    var confirmKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Да, удалить все", "promo_delete_all_confirm"), InlineKeyboardButton.WithCallbackData("Отмена", "list_promos") }
                    });
                    await botClient.SendMessage(chatId, "⚠️ Удалить ВСЕ промокоды безвозвратно? Это действие нельзя отменить.", replyMarkup: confirmKeyboard, cancellationToken: cancellationToken);
                    break;
                }

                case "promo_delete_all_confirm":
                {
                    try
                    {
                        long deleted = await BoltGameDatabaseProvider.Instance.DeleteAllCoupons();
                        await botClient.SendMessage(chatId, $"Удалено промокодов: {deleted}.", cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await botClient.SendMessage(chatId, $"Ошибка при удалении: {ex.Message}", cancellationToken: cancellationToken);
                    }
                    await ShowPromoMenu(botClient, chatId, cancellationToken);
                    break;
                }

                default:
                if (callbackQuery.Data.StartsWith("promoitem:"))
                {
                    int promoItemKey = int.Parse(callbackQuery.Data.Substring("promoitem:".Length));
                    context.PendingSearchItemKey = promoItemKey;
                    context.State = UserState.WaitingForPromoItemSearchCount;
                    string promoItemName = _cachedItems.FirstOrDefault(i => i.key == promoItemKey)?.displayName ?? $"id {promoItemKey}";
                    await botClient.SendMessage(chatId, $"Сколько штук «{promoItemName}» добавить в промокод?", cancellationToken: cancellationToken);
                }
                else if (callbackQuery.Data.StartsWith("promo_search_page:"))
                {
                    int page = int.Parse(callbackQuery.Data.Substring("promo_search_page:".Length));
                    await SendPromoItemSearchResults(botClient, chatId, context.PromoSearchQuery ?? "", page, callbackQuery.Message.MessageId, cancellationToken);
                }
else if (callbackQuery.Data.StartsWith("mkt_cols:"))
                {
                    if (!callerIsAdmin) return;
                    int page = int.Parse(callbackQuery.Data.Substring("mkt_cols:".Length));
                    await ShowMarketCollections(botClient, chatId, page, callbackQuery.Message.MessageId, cancellationToken);
                }
                else if (callbackQuery.Data.StartsWith("mkt_col:"))
                {
                    if (!callerIsAdmin) return;
                    string collection = callbackQuery.Data.Substring("mkt_col:".Length);
                    await ToggleMarketCollection(botClient, chatId, collection, callbackQuery.Message.MessageId, cancellationToken);
                }
                else if (callbackQuery.Data.StartsWith("promo_currency:"))
                {
                    int currId = int.Parse(callbackQuery.Data.Substring("promo_currency:".Length));
                    context.PendingCurrencyId = currId;
                    context.State = UserState.WaitingForPromoCurrencyAmount;
                    await botClient.SendMessage(chatId, $"Сколько {CurrencyLabel(currId)} добавить?", cancellationToken: cancellationToken);
                }
                else if (callbackQuery.Data.StartsWith("promo_max:"))
                {
                    int n = int.Parse(callbackQuery.Data.Substring("promo_max:".Length));
                    context.MaxUses = Math.Max(0, n);
                    context.State = UserState.PromoEditor;
                    await ShowPromoEditor(botClient, chatId, (int)callbackQuery.Message.MessageId, cancellationToken);
                }
                else if (callbackQuery.Data.StartsWith("promo_cur_choose:"))
                {
                    int id = int.Parse(callbackQuery.Data.Substring("promo_cur_choose:".Length));
                    await ShowPromoCurrencyAmounts(botClient, chatId, id, (int)callbackQuery.Message.MessageId, cancellationToken);
                }
                else if (callbackQuery.Data.StartsWith("promo_cur_add:"))
                {
                    // promo_cur_add:{currencyId}:{amount}
                    var parts = callbackQuery.Data.Substring("promo_cur_add:".Length).Split(':');
                    int curId = int.Parse(parts[0]);
                    int amount = int.Parse(parts[1]);
                    context.Currencies ??= new List<Tuple<int, int>>();
                    var existing = context.Currencies.FirstOrDefault(c => c.Item1 == curId);
                    if (existing != null)
                    {
                        context.Currencies.Remove(existing);
                        context.Currencies.Add(new Tuple<int, int>(curId, existing.Item2 + amount));
                    }
                    else
                    {
                        context.Currencies.Add(new Tuple<int, int>(curId, amount));
                    }
                    context.State = UserState.PromoEditor;
                    await ShowPromoEditor(botClient, chatId, (int)callbackQuery.Message.MessageId, cancellationToken);
                    break;
                }
                else if (callbackQuery.Data.StartsWith("promo_cur_custom:"))
                {
                    int curId = int.Parse(callbackQuery.Data.Substring("promo_cur_custom:".Length));
                    context.PendingCurrencyId = curId;
                    context.State = UserState.PromoEditCurrencyCustom;
                    await botClient.SendMessage(chatId, $"Сколько {CurrencyLabel(curId)} добавить? (или «-», чтобы убрать)", cancellationToken: cancellationToken);
                    break;
                }
                else if (callbackQuery.Data.StartsWith("promo_cur_remove:"))
                {
                    int curId = int.Parse(callbackQuery.Data.Substring("promo_cur_remove:".Length));
                    context.Currencies?.RemoveAll(c => c.Item1 == curId);
                    context.State = UserState.PromoEditor;
                    await ShowPromoEditor(botClient, chatId, (int)callbackQuery.Message.MessageId, cancellationToken);
                    break;
                }
                else if (callbackQuery.Data.StartsWith("promo_col:"))
                {
                    int page = int.Parse(callbackQuery.Data.Substring("promo_col:".Length));
                    await ShowPromoCollectionList(botClient, chatId, page, (int)callbackQuery.Message.MessageId, cancellationToken);
                    break;
                }
                else if (callbackQuery.Data.StartsWith("promo_rar:"))
                {
                    // promo_rar:{col}:{rarity}:{backcol}
                    var parts = callbackQuery.Data.Substring("promo_rar:".Length).Split(':');
                    int col = int.Parse(parts[0]);
                    int rarity = int.Parse(parts[1]);
                    await ShowPromoRaritySkins(botClient, chatId, (CollectionId)col, (SkinValue)rarity, 0, (int)callbackQuery.Message.MessageId, cancellationToken);
                    break;
                }
                else if (callbackQuery.Data.StartsWith("promo_skin_addcnt:"))
                {
                    // promo_skin_addcnt:{col}:{rarity}:{page}:{key}:{count}
                    var parts = callbackQuery.Data.Substring("promo_skin_addcnt:".Length).Split(':');
                    int col = int.Parse(parts[0]);
                    int rarity = int.Parse(parts[1]);
                    int page = int.Parse(parts[2]);
                    int key = int.Parse(parts[3]);
                    int count = int.Parse(parts[4]);
                    if (count > 0)
                    {
                        context.Items ??= new List<Tuple<int, int>>();
                        context.Items.Add(new Tuple<int, int>(key, count));
                        string nm = _cachedItems.FirstOrDefault(i => i.key == key)?.displayName ?? $"id {key}";
                        await botClient.SendMessage(chatId, $"Добавлено: {nm} x{count}.", cancellationToken: cancellationToken);
                    }
                    await ShowPromoRaritySkins(botClient, chatId, (CollectionId)col, (SkinValue)rarity, page, (int)callbackQuery.Message.MessageId, cancellationToken);
                    break;
                }
                else if (callbackQuery.Data.StartsWith("promo_skin_choose:"))
                {
                    // promo_skin_choose:{col}:{rarity}:{page}:{key}
                    var parts = callbackQuery.Data.Substring("promo_skin_choose:".Length).Split(':');
                    context.PromoBrowseCollection = int.Parse(parts[0]);
                    context.PromoBrowseRarity = int.Parse(parts[1]);
                    context.PromoPendingSkinKey = int.Parse(parts[3]);
                    context.State = UserState.PromoEditSkinCount;
                    string nm = _cachedItems.FirstOrDefault(i => i.key == context.PromoPendingSkinKey)?.displayName ?? $"id {context.PromoPendingSkinKey}";
                    await botClient.SendMessage(chatId, $"Сколько штук «{nm}» в промокод? (или «-», чтобы пропустить)", cancellationToken: cancellationToken);
                    break;
                }
                else if (callbackQuery.Data.StartsWith("promo_skin_remove:"))
                {
                    int key = int.Parse(callbackQuery.Data.Substring("promo_skin_remove:".Length));
                    context.Items?.RemoveAll(i => i.Item1 == key);
                    context.State = UserState.PromoEditor;
                    await ShowPromoEditor(botClient, chatId, (int)callbackQuery.Message.MessageId, cancellationToken);
                    break;
                }
                else if (callbackQuery.Data.StartsWith("ban_reason:"))
                {
                    var reason = callbackQuery.Data.Substring("ban_reason:".Length);
                    if (reason.Equals("Other", StringComparison.OrdinalIgnoreCase))
                    {
                        context.State = UserState.WaitingForBanCustomCode;
                        context.PendingBanCode = 1001;
                        await botClient.SendMessage(chatId, "Введите свой банкод: ", cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await BanPlayer(context.TargetUserId, reason, 1001, chatId, cancellationToken);
                        context.State = UserState.None;
                        await ShowMainMenu(botClient, chatId, cancellationToken);
                    }
                }
                else if (callbackQuery.Data.StartsWith("delete_promo:"))
                {
                    var promoId = callbackQuery.Data.Substring("delete_promo:".Length);
                    await DeletePromo(botClient, chatId, promoId, cancellationToken);
                }
                else if (callbackQuery.Data.StartsWith("approve_promo:"))
                {
                    var reqId = callbackQuery.Data.Substring("approve_promo:".Length);
                    if (_pendingPromos.TryRemove(reqId, out string promoData))
                    {
                        var promoReq = System.Text.Json.JsonSerializer.Deserialize<PendingPromoInfo>(promoData);
                        await BoltGameDatabaseProvider.Instance.CreateCoupon(promoReq.Code, promoReq.Max, promoReq.Items, promoReq.Currencies);
                        await botClient.SendMessage(chatId, $"Запрос {reqId} одобрен, промокод {promoReq.Code} создан.", cancellationToken: cancellationToken);
                        try { await botClient.SendMessage(promoReq.CreatorChat, $"Твой промокод одобрен и создан.\nТвой промокод: `{promoReq.Code}`", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken); } catch {}
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "Запрос уже обработан или не найден.", cancellationToken: cancellationToken);
                    }
                }
                else if (callbackQuery.Data.StartsWith("reject_promo:"))
                {
                    var reqId = callbackQuery.Data.Substring("reject_promo:".Length);
                    if (_pendingPromos.TryRemove(reqId, out string promoData))
                    {
                        var promoReq = System.Text.Json.JsonSerializer.Deserialize<PendingPromoInfo>(promoData);
                        await botClient.SendMessage(chatId, $"Запрос {reqId} отклонен.", cancellationToken: cancellationToken);
                        try { await botClient.SendMessage(promoReq.CreatorChat, $"Твой промокод {promoReq.Code} отклонен админами.", cancellationToken: cancellationToken); } catch {}
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "Запрос уже обработан или не найден.", cancellationToken: cancellationToken);
                    }
                }
                else if (callbackQuery.Data.StartsWith("list_promos:"))
                {
                    if (int.TryParse(callbackQuery.Data.Split(':')[1], out int page))
                    {
                        await ListPromos(botClient, chatId, page, cancellationToken);
                    }
                }
                else if (callbackQuery.Data.StartsWith("del_news:"))
                {
                    var newsId = callbackQuery.Data.Substring("del_news:".Length);
                    await DeleteNewsPopup(botClient, chatId, newsId, cancellationToken);
                }
                else if (callbackQuery.Data.StartsWith("list_items:"))
                {
                    if (int.TryParse(callbackQuery.Data.Split(':')[1], out int page))
                    {
                        await ListItems(botClient, chatId, page, cancellationToken);
                    }
                }
                break;
            }
        }

        private async Task ShowMainMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var context = _userContexts.TryGetValue(chatId, out var ctx) ? ctx : null;
            string username = context?.Username;

            // Check ID if username is not enough
            long userId = chatId; // Simplified assumption for ChatID == UserID in private

            // Anyone who passed the /pass gate is treated as an admin (full menu).
            if (_roleManager.IsAdmin(username, userId) || _hasPassed.ContainsKey(userId))
            {
                await ShowAdminMenu(botClient, chatId, cancellationToken);
            }
            else if (_roleManager.IsJuniorModerator(userId))
            {
                await ShowJuniorModMenu(botClient, chatId, cancellationToken);
            }
            else
            {
                await ShowModMenu(botClient, chatId, cancellationToken);
            }
        }

        // Раньше это было одно плоское меню на 13-15 кнопок сразу для любой роли ("кабина
        // самолёта"). Теперь главное меню - это категории-панели, каждая открывает свой экран
        // с кнопкой "Назад" (callback "cancel", он уже сбрасывает состояние и зовёт ShowMainMenu).
        private async Task ShowAdminMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Модерация", "menu_moderation"), InlineKeyboardButton.WithCallbackData("Промокоды", "menu_promo") },
                new[] { InlineKeyboardButton.WithCallbackData("Предметы", "menu_items"), InlineKeyboardButton.WithCallbackData("Выдача", "menu_give") },
                new[] { InlineKeyboardButton.WithCallbackData("Персонал", "menu_staff"), InlineKeyboardButton.WithCallbackData("Whitelist", "menu_whitelist") },
                new[] { InlineKeyboardButton.WithCallbackData("Статистика", "server_stats"), InlineKeyboardButton.WithCallbackData("📍 Popup/Новости", "menu_news") },
                new[] { InlineKeyboardButton.WithCallbackData("🌸 Чат-Бот Амира", "chat_bot"), InlineKeyboardButton.WithCallbackData("🛒 Рынок", "menu_market") }
            });

            await botClient.SendMessage(chatId, "*Панель администратора*\nВыбери раздел: ", parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
        }

        private async Task ShowModMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Модерация", "menu_moderation"), InlineKeyboardButton.WithCallbackData("Промокоды", "menu_promo") },
                new[] { InlineKeyboardButton.WithCallbackData("Предметы", "menu_items"), InlineKeyboardButton.WithCallbackData("Статистика", "server_stats") },
                new[] { InlineKeyboardButton.WithCallbackData("📍 Popup/Новости", "menu_news"), InlineKeyboardButton.WithCallbackData("🌸 Чат-Бот Амира", "chat_bot") }
            });

            await botClient.SendMessage(chatId, "*Панель модератора*\nВыбери раздел: ", parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
        }

        private async Task ShowJuniorModMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Промокоды", "menu_promo") },
                new[] { InlineKeyboardButton.WithCallbackData("Поиск скина", "search_skin"), InlineKeyboardButton.WithCallbackData("Поиск пушки", "search_weapon") },
                new[] { InlineKeyboardButton.WithCallbackData("🌸 Чат-Бот Амира", "chat_bot") }
            });

            await botClient.SendMessage(chatId, "*Панель младшего модератора*\nМожно создавать промокоды и искать предметы.", parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard, cancellationToken: cancellationToken);
        }

        private async Task ShowModerationMenu(ITelegramBotClient botClient, long chatId, bool isAdmin, CancellationToken ct)
        {
            var rows = new List<InlineKeyboardButton[]>
            {
                new[] { InlineKeyboardButton.WithCallbackData("Бан", "ban_user"), InlineKeyboardButton.WithCallbackData("Разбан", "unban_user") }
            };
            if (isAdmin)
            {
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Очистить инвентарь", "clear_inv") });
            }
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", "cancel") });

            await botClient.SendMessage(chatId, "*Модерация*", parseMode: ParseMode.Markdown, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowPromoMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Создать промокод", "create_promo"), InlineKeyboardButton.WithCallbackData("Список", "list_promos") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "cancel") }
            });
            await botClient.SendMessage(chatId, "*Промокоды*", parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
        }

        private async Task ShowItemsMenu(ITelegramBotClient botClient, long chatId, bool isAdmin, CancellationToken ct)
        {
            var rows = new List<InlineKeyboardButton[]>
            {
                new[] { InlineKeyboardButton.WithCallbackData("Список предметов", "list_items") },
                new[] { InlineKeyboardButton.WithCallbackData("Поиск скина", "search_skin"), InlineKeyboardButton.WithCallbackData("Поиск пушки", "search_weapon") }
            };
            if (isAdmin)
            {
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Снять предмет у всех", "remove_item_everywhere") });
            }
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", "cancel") });

            await botClient.SendMessage(chatId, "*Предметы*", parseMode: ParseMode.Markdown, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
        }

        private async Task ShowGiveMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Выдать Gold Pass", "give_goldpass") },
                new[] { InlineKeyboardButton.WithCallbackData("Выдать голду", "give_gold"), InlineKeyboardButton.WithCallbackData("Выдать спины", "give_spins") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "cancel") }
            });
            await botClient.SendMessage(chatId, "*Выдача*\nGold Pass / голда / прокруты спина (Spin Token #219).", parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
        }

        private async Task ShowStaffMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            // "Лимиты" и "Логи" - админ-only (сюда и так попадают только через menu_staff,
            // который сам гейтится по callerIsAdmin). Лимиты - настройка на конкретного модера
            // (сколько промо/день, сколько валюты за промо), логи - просмотр аудита действий
            // всех модеров/админов (ModActionLog).
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Добавить модера", "add_mod"), InlineKeyboardButton.WithCallbackData("Убрать модера", "del_mod") },
                new[] { InlineKeyboardButton.WithCallbackData("Добавить мл. модера", "add_jmod"), InlineKeyboardButton.WithCallbackData("Убрать мл. модера", "del_jmod") },
                new[] { InlineKeyboardButton.WithCallbackData("Уровень игрока", "set_level"), InlineKeyboardButton.WithCallbackData("Сменить ID", "create_custom_id") },
                new[] { InlineKeyboardButton.WithCallbackData("Лимиты модеров", "mod_limits"), InlineKeyboardButton.WithCallbackData("Логи действий", "mod_logs") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "cancel") }
            });
            await botClient.SendMessage(chatId, "*Персонал*", parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
        }

        private async Task ShowWhitelistMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Включить", "wl_on"), InlineKeyboardButton.WithCallbackData("Выключить", "wl_off") },
                new[] { InlineKeyboardButton.WithCallbackData("Список", "wl_list") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "cancel") }
            });
            await botClient.SendMessage(chatId, "*Whitelist*", parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
        }

private async Task ShowMarketMenu(ITelegramBotClient botClient, long chatId, int editMessageId, CancellationToken ct)
        {
            bool closed = BoltGameDatabaseProvider.Instance.GetMarketClosed();
            string message = BoltGameDatabaseProvider.Instance.GetMarketClosedMessage();
            int hiddenCount = BoltGameDatabaseProvider.Instance.GetHiddenMarketCollections().Count;

            string status = closed
                ? $"🔴 Закрыт (на учёт)"
                : "🟢 Открыт";
            string statusText = string.IsNullOrWhiteSpace(message) ? "" : $" — {message}";

            string text = "*🛒 Рынок*\n" +
                          $"Статус: {status}{statusText}\n" +
                          $"Скрытых коллекций: {hiddenCount}\n\n" +
                          "_Рынок можно закрыть «на учёт» — продажи и покупки будут отклонены. Также можно скрыть отдельные коллекции._";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(closed ? "🔓 Открыть рынок" : "🔒 Закрыть на учёт", "mkt_toggle") },
                new[] { InlineKeyboardButton.WithCallbackData("📚 Коллекции", "mkt_list") },
                new[] { InlineKeyboardButton.WithCallbackData("♻️ Обновить", "update_market") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "cancel") }
            });

            if (editMessageId > 0)
            {
                try
                {
                    await botClient.EditMessageText(chatId, editMessageId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
                    return;
                }
                catch
                {
                    // Не смогли отредактировать — отправляем новое
                }
            }
            await botClient.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
        }

private async Task ToggleMarketClosed(ITelegramBotClient botClient, long chatId, int editMessageId, CancellationToken ct)
        {
            bool currentlyClosed = BoltGameDatabaseProvider.Instance.GetMarketClosed();
            bool newState = !currentlyClosed;
            BoltGameDatabaseProvider.Instance.SetMarketClosed(newState, newState ? "Рынок закрыт администратором" : null);
            ModActionLog.Log(chatId, null, "market_toggle", $"marketClosed={newState}");
            try { await botClient.AnswerCallbackQuery($"Рынок {(newState ? "закрыт на учёт" : "открыт")}"); } catch { }
            await ShowMarketMenu(botClient, chatId, editMessageId, ct);
        }

private async Task ToggleMarketCollection(ITelegramBotClient botClient, long chatId, string collection, int editMessageId, CancellationToken ct)
        {
            var hidden = BoltGameDatabaseProvider.Instance.GetHiddenMarketCollections();
            bool isHidden = hidden.Any(c => string.Equals(c, collection, StringComparison.OrdinalIgnoreCase));
            BoltGameDatabaseProvider.Instance.SetCollectionHidden(collection, !isHidden);
            ModActionLog.Log(chatId, null, isHidden ? "market_col_restore" : "market_col_hide", $"collection={collection}");
            try { await botClient.AnswerCallbackQuery($"{collection}: {(isHidden ? "возвращена" : "скрыта")}"); } catch { }
            await ShowMarketCollections(botClient, chatId, 0, editMessageId, ct);
        }

        private List<string> GetMarketCollections()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in InventoryCatalogueLoader.Instance.GetAll())
            {
                if (item?.properties != null
                    && item.properties.TryGetValue("collection", out var value)
                    && !string.IsNullOrWhiteSpace(value?.AsString))
                {
                    set.Add(value.AsString);
                }
            }
            return set.ToList();
        }

private async Task ShowMarketCollections(ITelegramBotClient botClient, long chatId, int page, int editMessageId, CancellationToken ct)
        {
            var collections = GetMarketCollections();
            var hidden = BoltGameDatabaseProvider.Instance.GetHiddenMarketCollections();
            const int pageSize = 10;

            int totalPages = Math.Max(1, (collections.Count + pageSize - 1) / pageSize);
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;

            var rows = new List<InlineKeyboardButton[]>();
            for (int i = page * pageSize; i < Math.Min(collections.Count, (page + 1) * pageSize); i++)
            {
                string collection = collections[i];
                bool isHidden = hidden.Any(c => string.Equals(c, collection, StringComparison.OrdinalIgnoreCase));
                string label = $"{collection} {(isHidden ? "✗ скрыта" : "✓ в продаже")}";
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(label, $"mkt_col:{collection}") });
            }

            if (totalPages > 1)
            {
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData("◀ Назад", $"mkt_cols:{page - 1}"), InlineKeyboardButton.WithCallbackData($"{page + 1}/{totalPages}", "noop"), InlineKeyboardButton.WithCallbackData("Вперёд ▶", $"mkt_cols:{page + 1}") });
            }

            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("♻️ Обновить", "mkt_list"), InlineKeyboardButton.WithCallbackData("Назад", "menu_market") });

            string text = $"*📚 Коллекции рынка* (стр. {page + 1}/{totalPages})\n\n" +
                          "_Нажми на коллекцию, чтобы скрыть её с продажи или вернуть._";

            var keyboard = new InlineKeyboardMarkup(rows);
            if (editMessageId > 0)
            {
                try
                {
                    await botClient.EditMessageText(chatId, editMessageId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
                    return;
                }
                catch
                {
                    // Не смогли отредактировать — отправляем новое
                }
            }
            await botClient.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
        }

        private async Task<bool> TryHandleWhitelistCommand(ITelegramBotClient botClient, long chatId, string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLowerInvariant();

            if (command == "/wl_on")
            {
                _roleManager.SetWhitelistEnabled(true);
                await botClient.SendMessage(chatId, "Whitelist enabled.", cancellationToken: cancellationToken);
                return true;
            }

            if (command == "/wl_off")
            {
                _roleManager.SetWhitelistEnabled(false);
                await botClient.SendMessage(chatId, "Whitelist disabled.", cancellationToken: cancellationToken);
                return true;
            }

            if (command == "/wl_list")
            {
                await SendWhitelistStatus(botClient, chatId, cancellationToken);
                return true;
            }

            if (command == "/wl_add" && parts.Length >= 2)
            {
                string idText = parts[1].Trim();
                _roleManager.AddGameWhitelist(idText);
                if (long.TryParse(idText, out var id))
                {
                    _roleManager.AddWhitelist(id);
                    await botClient.SendMessage(chatId, $"Added to whitelist: {idText}", cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, $"Added to game whitelist: {idText}", cancellationToken: cancellationToken);
                }
                return true;
            }

            if (command == "/wl_del" && parts.Length >= 2)
            {
                string idText = parts[1].Trim();
                _roleManager.RemoveGameWhitelist(idText);
                if (long.TryParse(idText, out var id))
                {
                    _roleManager.RemoveWhitelist(id);
                    await botClient.SendMessage(chatId, $"Removed from whitelist: {idText}", cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, $"Removed from game whitelist: {idText}", cancellationToken: cancellationToken);
                }
                return true;
            }

            return command.StartsWith("/wl_");
        }

        private async Task SendWhitelistStatus(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var ids = _roleManager.GetWhitelistIds();
            var gameIds = _roleManager.GetGameWhitelistIds();
            string state = _roleManager.IsWhitelistEnabled() ?"enabled": "disabled";
            string list = ids.Count == 0 ?"(empty)": string.Join("\n", ids);
            string gameList = gameIds.Count == 0 ?"(empty)": string.Join("\n", gameIds);
            await botClient.SendMessage(chatId, $"Whitelist: {state}\n\nTelegram IDs:\n{list}\n\nGame UIDs/ObjectIds:\n{gameList}", cancellationToken: cancellationToken);
        }

        private async Task SearchItems(ITelegramBotClient botClient, long chatId, string query, string mode, CancellationToken cancellationToken)
        {
            query = (query ??"").Trim();
            if (query.Length == 0)
            {
                await botClient.SendMessage(chatId, "Пустой запрос.", cancellationToken: cancellationToken);
                return;
            }

            EnsureItemsLoaded();
            if (_cachedItems.Count == 0)
            {
                await botClient.SendMessage(chatId, "JSON с предметами не загружен. Проверь StandRise.inventory_item_definition.json рядом с сервером или в папке Data.", cancellationToken: cancellationToken);
                return;
            }

            IEnumerable<ItemDef> items = _cachedItems.Where(i => i.displayName != null);
            bool searchById = int.TryParse(query, out var id);
            if (searchById)
            {
                items = items.Where(i => i.key == id);
            }
            else
            {
                var normalizedQuery = NormalizeSearchText(query);
                items = items.Where(i => NormalizeSearchText(i.displayName).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
            }

            if (!searchById && mode == "weapon")
            {
                items = items.Where(IsWeaponItem);
            }
            else if (!searchById && mode == "skin")
            {
                items = items.Where(i => !IsWeaponItem(i) || IsSkinLikeItem(i));
            }

            var matches = items.Take(30).ToList();
            if (matches.Count == 0)
            {
                await botClient.SendMessage(chatId, "Ничего не найдено.", cancellationToken: cancellationToken);
                return;
            }

            string title = mode == "weapon"?"Поиск пушек": "Поиск скинов/предметов";
            string message = $"*{title}:*\n\n";
            foreach (var item in matches)
            {
                message += $"`{item.key}` - {item.displayName}\n";
            }

            await botClient.SendMessage(chatId, message.Replace("*", ""), cancellationToken: cancellationToken);
        }

        private async Task RemoveItemEverywhere(ITelegramBotClient botClient, long chatId, string query, CancellationToken cancellationToken)
        {
            var item = ResolveItem(query);
            if (item == null)
            {
                await botClient.SendMessage(chatId, "Не нашел предмет. Сначала найди его через поиск и попробуй по ID.", cancellationToken: cancellationToken);
                return;
            }

            var db = BoltGameDatabaseProvider.Instance;
            long removedInventory = db.RemoveInventoryItemDefinitionFromAllPlayers(item.key);
            long removedMarket = db.RemoveMarketplaceRequestsByItemDefinition(item.key);

            ModActionLog.Log(chatId, null, "remove_item_everywhere", $"itemId={item.key} name={item.displayName} removedInventory={removedInventory} removedMarket={removedMarket}");

            await botClient.SendMessage(
            chatId,
            $"Готово.\nID: `{item.key}`\nПредмет: {item.displayName}\nСнято из инвентарей: {removedInventory}\nУдалено с рынка: {removedMarket}",
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
        }

        private ItemDef ResolveItem(string query)
        {
            EnsureItemsLoaded();
            query = (query ??"").Trim();
            if (int.TryParse(query, out var id))
            {
                return _cachedItems.FirstOrDefault(i => i.key == id);
            }

            var normalizedQuery = NormalizeSearchText(query);
            var matches = _cachedItems
            .Where(i => i.displayName != null && NormalizeSearchText(i.displayName).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private void EnsureItemsLoaded()
        {
            if (_cachedItems.Count == 0)
            {
                LoadItems();
            }
        }

        private static string NormalizeSearchText(string value)
        {
            value = (value ??"").ToLowerInvariant();
            value = value.Replace("ё", "е");
            value = value.Replace("хелоуин", "halloween");
            value = value.Replace("хеллоуин", "halloween");
            value = value.Replace("хэллоуин", "halloween");
            value = value.Replace("чарм", "charm");
            value = value.Replace("брелок", "charm");
            value = value.Replace("брелки", "charm");
            value = value.Replace("стикер", "sticker");
            value = value.Replace("стикерпак", "sticker pack");
            value = value.Replace("пак", "pack");
            return value.Replace("\"", "").Trim();
        }

        private static bool IsSkinLikeItem(ItemDef item)
        {
            string name = item.displayName ??"";
            return name.Contains("Sticker", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Charm", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Medal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Nameless", StringComparison.OrdinalIgnoreCase)
            || IsWeaponItem(item);
        }

        private static bool IsWeaponItem(ItemDef item)
        {
            if (item.properties.ValueKind == JsonValueKind.Object && item.properties.TryGetProperty("weaponId", out _))
            {
                return true;
            }

            string name = item.displayName ??"";
            string[] weapons = {"AKR", "M4", "M16", "AWM", "USP", "G22", "P350", "F/S", "Desert Eagle", "Deagle", "TEC-9", "MP5", "MP7", "P90", "UMP45", "MAC10", "SPAS", "FabM", "M110", "M40", "FN FAL", "FAMAS", "SM1014", "Knife", "Karambit", "Kunai", "jKommando", "Scorpion", "Tanto", "Dual Daggers"};
            return weapons.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
        }

        private static List<Tuple<int, int>> ParsePairs(string input)
        {
            var list = new List<Tuple<int, int>>();
            if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "none" || input.ToLower() == "null") return list;

            var pairs = input.Split(',');
            foreach (var pair in pairs)
            {
                var parts = pair.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int id) && int.TryParse(parts[1], out int count))
                {
                    list.Add(new Tuple<int, int>(id, count));
                }
                else
                {
                    throw new FormatException("Invalid pair format");
                }
            }
            return list;
        }

        // Раньше при создании промокода надо было заранее знать ID предмета, чтобы вписать его
        // руками в "id:count". Теперь можно нажать "Найти предмет", ввести часть названия/ID и
        // выбрать из списка кнопкой - без похода в отдельное меню поиска.
        private async Task SendPromoItemsPrompt(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Найти предмет", "promo_search_item") },
                new[] { InlineKeyboardButton.WithCallbackData("Без предметов", "promo_items_done") }
            });
            await botClient.SendMessage(chatId, "Укажи предметы в формате itemId:count,itemId:count, либо нажми «Найти предмет» и введи название, либо «Без предметов»: ", replyMarkup: keyboard, cancellationToken: ct);
        }

        private async Task SendPromoItemSearchResults(ITelegramBotClient botClient, long chatId, string query, int page, int editMessageId, CancellationToken ct)
        {
            const int pageSize = 10;
            query = (query ?? "").Trim();
            List<ItemDef> matches;
            if (int.TryParse(query, out int qid))
            {
                matches = _cachedItems.Where(i => i.key == qid).ToList();
            }
            else
            {
                matches = _cachedItems.Where(i => i.displayName != null && i.displayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.displayName)
                .ToList();
            }

            if (matches.Count == 0)
            {
                var emptyKeyboard = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("Искать ещё", "promo_search_item") } });
                await botClient.SendMessage(chatId, "Ничего не найдено. Попробуй другой запрос, либо введи itemId:count вручную: ", replyMarkup: emptyKeyboard, cancellationToken: ct);
                return;
            }

            int totalPages = (int)Math.Ceiling(matches.Count / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 0) page = 0;
            if (page > totalPages - 1) page = totalPages - 1;

            var pageItems = matches.Skip(page * pageSize).Take(pageSize).ToList();

            var rows = pageItems.Select(m => new[] { InlineKeyboardButton.WithCallbackData($"{m.displayName} (id {m.key})", $"promoitem:{m.key}") }).ToList();
            var navButtons = new List<InlineKeyboardButton>();
            if (page > 0) navButtons.Add(InlineKeyboardButton.WithCallbackData("◀", $"promo_search_page:{page - 1}"));
            navButtons.Add(InlineKeyboardButton.WithCallbackData($"{page + 1}/{totalPages}", "noop"));
            if (page < totalPages - 1) navButtons.Add(InlineKeyboardButton.WithCallbackData("▶", $"promo_search_page:{page + 1}"));
            rows.Add(navButtons.ToArray());
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Искать ещё", "promo_search_item") });

            string header = int.TryParse(query, out _)
                ? $"Найдено: {matches.Count}."
                : $"Запрос \"{query}\". Всего найдено: {matches.Count}. Страница {page + 1}/{totalPages}.";
            var keyboard = new InlineKeyboardMarkup(rows);
            if (editMessageId > 0)
            {
                try
                {
                    await botClient.EditMessageText(chatId, editMessageId, header, replyMarkup: keyboard, cancellationToken: ct);
                    return;
                }
                catch
                {
                    // Не смогли отредактировать — отправляем новое
                }
            }
            await botClient.SendMessage(chatId, header, replyMarkup: keyboard, cancellationToken: ct);
        }

        // Валюта в игре: 102 = Gold, 101 = Silver. Раньше валюту в промокод надо было вписывать
        // руками как "currencyId:count" - теперь можно выбрать кнопкой.
        private static string CurrencyLabel(int currencyId) => currencyId switch
        {
            102 => "Голда",
            101 => "Серебро",
            _ => $"currencyId {currencyId}"
        };

        private async Task SendPromoCurrencyPrompt(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Голда", "promo_currency:102"), InlineKeyboardButton.WithCallbackData("Серебро", "promo_currency:101") },
                new[] { InlineKeyboardButton.WithCallbackData("Без валюты", "promo_currency_done") }
            });
            await botClient.SendMessage(chatId, "Укажи валюту: выбери кнопкой, либо введи вручную currencyId:count,currencyId:count (или 'none'):", replyMarkup: keyboard, cancellationToken: ct);
        }

        // ===== Панель-редактор промокода (удобное создание) =====

        // Если messageId > 0 — редактируем предыдущее сообщение (чтобы старые
        // сообщения с выбором не копились), иначе отправляем новое.
        private static async Task SendOrEditPromoAsync(ITelegramBotClient botClient, long chatId, int editMessageId, string text, InlineKeyboardMarkup replyMarkup, CancellationToken ct)
        {
            if (editMessageId > 0)
            {
                try
                {
                    await botClient.EditMessageText(chatId, editMessageId, text, parseMode: ParseMode.Markdown, replyMarkup: replyMarkup, cancellationToken: ct);
                    return;
                }
                catch
                {
                    // Не смогли отредактировать — отправляем новое
                }
            }
            await botClient.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: replyMarkup, cancellationToken: ct);
        }

        private static readonly string[] PromoRarityOrder = new[] { "Common", "Uncommon", "Rare", "Epic", "Legendary", "Arcane" };

        private static string PromoRarityLabel(SkinValue v) => v switch
        {
            SkinValue.Common => "Обычное",
            SkinValue.Uncommon => "Необычное",
            SkinValue.Rare => "Редкое",
            SkinValue.Epic => "Эпическое",
            SkinValue.Legendary => "Легендарное",
            SkinValue.Arcane => "Аркан",
            _ => v.ToString()
        };

        private static string PromoCollectionName(CollectionId c)
        {
            var name = c.ToString();
            name = name.Replace("_", " ");
            return name;
        }

        // Список коллекций, в которых реально есть скины (для быстрого обзора).
        private List<CollectionId> PromoSkinCollections()
        {
            InventoryCatalogueLoader.Instance.EnsureLoaded();
            return InventoryCatalogueLoader.Instance.GetAll()
                .Where(d => d != null && d.properties != null && d.GetSkinValue() != SkinValue.None && d.GetCollectionId() != CollectionId.None)
                .Select(d => d.GetCollectionId())
                .Distinct()
                .OrderBy(c => (int)c)
                .ToList();
        }

        private async Task ShowPromoEditor(ITelegramBotClient botClient, long chatId, int editMessageId, CancellationToken ct)
        {
            UserContext ctx = _userContexts.GetOrAdd(chatId, _ => new UserContext());
            string codeLine = string.IsNullOrEmpty(ctx.PromoCode) ? "не задан" : $"`{ctx.PromoCode}`";
            string maxLine = ctx.MaxUses > 0 ? ctx.MaxUses.ToString() : "не задано";

            string itemsLine = ctx.Items != null && ctx.Items.Count > 0
                ? string.Join("\n", ctx.Items.Select(i =>
                {
                    string nm = _cachedItems.FirstOrDefault(c => c.key == i.Item1)?.displayName ?? $"id {i.Item1}";
                    return $"  • {nm} x{i.Item2}";
                }))
                : "  (нет)";

            string curLine = ctx.Currencies != null && ctx.Currencies.Count > 0
                ? string.Join("\n", ctx.Currencies.Select(c => $"  • {CurrencyLabel(c.Item1)}: {c.Item2}"))
                : "  (нет)";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData($"🎫 Код: {FormatShort(ctx.PromoCode, "не задан")}", "promo_edit_code") },
                new[] { InlineKeyboardButton.WithCallbackData($"👥 Активаций: {maxLine}", "promo_edit_max") },
                new[] { InlineKeyboardButton.WithCallbackData("💰 Валюта", "promo_edit_cur"), InlineKeyboardButton.WithCallbackData("🎨 Скины и предметы", "promo_edit_skin") },
                new[] { InlineKeyboardButton.WithCallbackData("✅ Создать промокод", "promo_edit_finalize") },
                new[] { InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel") }
            });

            string msg = $"*Создание промокода*\n\n" +
                $"🔹 _Код:_ {codeLine}\n" +
                $"🔹 _Активаций:_ {maxLine}\n\n" +
                $"💰 _Валюта:_\n{curLine}\n\n" +
                $"🎨 _Скины/предметы:_\n{itemsLine}\n\n" +
                "Нажимай кнопки и добавляй награды. Когда всё готово — «Создать промокод».";

            await SendOrEditPromoAsync(botClient, chatId, editMessageId, msg, keyboard, ct);
        }

        private static string FormatShort(string s, string fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return s.Length <= 28 ? s : s.Substring(0, 25) + "...";
        }

        private async Task ShowPromoMaxPresetMenu(ITelegramBotClient botClient, long chatId, int editMessageId, CancellationToken ct)
        {
            var rows = new List<InlineKeyboardButton[]>();
            rows.Add(new[] {
                InlineKeyboardButton.WithCallbackData("1", "promo_max:1"),
                InlineKeyboardButton.WithCallbackData("5", "promo_max:5"),
                InlineKeyboardButton.WithCallbackData("10", "promo_max:10")
            });
            rows.Add(new[] {
                InlineKeyboardButton.WithCallbackData("50", "promo_max:50"),
                InlineKeyboardButton.WithCallbackData("100", "promo_max:100"),
                InlineKeyboardButton.WithCallbackData("Своё число", "promo_max_custom")
            });
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("← Назад", "promo_edit") });
            await SendOrEditPromoAsync(botClient, chatId, editMessageId, "Сколько раз можно активировать промокод?", new InlineKeyboardMarkup(rows), ct);
        }

        private async Task ShowPromoCurrencyMenu(ITelegramBotClient botClient, long chatId, int editMessageId, CancellationToken ct)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Голда", "promo_cur_choose:102"), InlineKeyboardButton.WithCallbackData("Серебро", "promo_cur_choose:101") },
                new[] { InlineKeyboardButton.WithCallbackData("← Назад", "promo_edit") }
            });
            await SendOrEditPromoAsync(botClient, chatId, editMessageId, "Какую валюту добавить?", keyboard, ct);
        }

        private async Task ShowPromoCurrencyAmounts(ITelegramBotClient botClient, long chatId, int currencyId, int editMessageId, CancellationToken ct)
        {
            var rows = new List<InlineKeyboardButton[]>();
            int[] presets = { 100, 500, 1000, 5000, 10000 };
            var rowA = presets.Take(3).Select(n => InlineKeyboardButton.WithCallbackData(n.ToString(), $"promo_cur_add:{currencyId}:{n}"));
            var rowB = presets.Skip(3).Select(n => InlineKeyboardButton.WithCallbackData(n.ToString(), $"promo_cur_add:{currencyId}:{n}"));
            rows.Add(rowA.ToArray());
            rows.Add(rowB.ToArray());
            rows.Add(new[] {
                InlineKeyboardButton.WithCallbackData("Своё число", $"promo_cur_custom:{currencyId}"),
                InlineKeyboardButton.WithCallbackData("← Назад", "promo_edit_cur")
            });
            await SendOrEditPromoAsync(botClient, chatId, editMessageId, $"Сколько {CurrencyLabel(currencyId)} добавить?", new InlineKeyboardMarkup(rows), ct);
        }

        // Список коллекций с пагинацией (по 6 на страницу).
        private async Task ShowPromoCollectionList(ITelegramBotClient botClient, long chatId, int page, int editMessageId, CancellationToken ct)
        {
            var cols = PromoSkinCollections();
            if (cols.Count == 0)
            {
                await botClient.SendMessage(chatId, "Скинов в каталоге нет.", cancellationToken: ct);
                return;
            }
            const int perPage = 6;
            int totalPages = (int)Math.Ceiling(cols.Count / (double)perPage);
            page = Math.Max(0, Math.Min(page, totalPages - 1));
            var pageCols = cols.Skip(page * perPage).Take(perPage);

            var rows = new List<InlineKeyboardButton[]>();
            foreach (var c in pageCols)
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(PromoCollectionName(c), $"promo_rar:{(int)c}:0") });

            var nav = new List<InlineKeyboardButton>();
            if (page > 0) nav.Add(InlineKeyboardButton.WithCallbackData("←", $"promo_col:{page - 1}"));
            if (page < totalPages - 1) nav.Add(InlineKeyboardButton.WithCallbackData("→", $"promo_col:{page + 1}"));
            if (nav.Count > 0) rows.Add(nav.ToArray());

            rows.Add(new[] {
                InlineKeyboardButton.WithCallbackData("Искать по названию", "promo_search_item"),
                InlineKeyboardButton.WithCallbackData("← Назад", "promo_edit")
            });

            await SendOrEditPromoAsync(botClient, chatId, editMessageId, $"Выбери коллекцию (стр. {page + 1}/{totalPages}):", new InlineKeyboardMarkup(rows), ct);
        }

        // Скины выбранной коллекции и редкости с пагинацией (по 8).
        private async Task ShowPromoRaritySkins(ITelegramBotClient botClient, long chatId, CollectionId col, SkinValue rarity, int page, int editMessageId, CancellationToken ct)
        {
            InventoryCatalogueLoader.Instance.EnsureLoaded();
            var items = InventoryCatalogueLoader.Instance.GetAll()
                .Where(d => d != null && d.properties != null && d.GetCollectionId() == col && d.GetSkinValue() == rarity)
                .OrderBy(d => d.displayName)
                .ToList();

            var rarities = InventoryCatalogueLoader.Instance.GetAll()
                .Where(d => d != null && d.properties != null && d.GetCollectionId() == col && d.GetSkinValue() != SkinValue.None)
                .Select(d => d.GetSkinValue())
                .Distinct()
                .OrderBy(v => (int)v)
                .ToList();

            if (rarities.Count == 0)
            {
                await botClient.SendMessage(chatId, "В этой коллекции нет скинов.", cancellationToken: ct);
                await ShowPromoCollectionList(botClient, chatId, 0, 0, ct);
                return;
            }

            // Редкость = 0 означает "выбрать редкость"
            if (rarity == SkinValue.None)
            {
                var rarityRows = new List<InlineKeyboardButton[]>();
                foreach (var r in rarities)
                    rarityRows.Add(new[] { InlineKeyboardButton.WithCallbackData(PromoRarityLabel(r), $"promo_rar:{(int)col}:{(int)r}") });
                rarityRows.Add(new[] {
                    InlineKeyboardButton.WithCallbackData("Искать по названию", "promo_search_item"),
                    InlineKeyboardButton.WithCallbackData("← Назад", "promo_edit_skin")
                });
                await SendOrEditPromoAsync(botClient, chatId, editMessageId, $"{PromoCollectionName(col)}\n\nВыбери редкость:", new InlineKeyboardMarkup(rarityRows), ct);
                return;
            }

            if (items.Count == 0)
            {
                await botClient.SendMessage(chatId, "В этой категории нет предметов.", cancellationToken: ct);
                await ShowPromoRaritySkins(botClient, chatId, col, SkinValue.None, 0, editMessageId, ct);
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
                if (item.IsStattrack()) label += " (StatTrak)";
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(label, $"promo_skin_choose:{(int)col}:{(int)rarity}:{page}:{item.key}") });
            }

            var nav = new List<InlineKeyboardButton>();
            if (page > 0) nav.Add(InlineKeyboardButton.WithCallbackData("←", $"promo_skin_addcnt:{(int)col}:{(int)rarity}:{page - 1}:0:0"));
            if (page < totalPages - 1) nav.Add(InlineKeyboardButton.WithCallbackData("→", $"promo_skin_addcnt:{(int)col}:{(int)rarity}:{page + 1}:0:0"));

            if (nav.Count > 0) rows.Add(nav.ToArray());
            rows.Add(new[] {
                InlineKeyboardButton.WithCallbackData("Искать по названию", "promo_search_item"),
                InlineKeyboardButton.WithCallbackData("← Редкость", $"promo_rar:{(int)col}:0")
            });

            await SendOrEditPromoAsync(botClient, chatId, editMessageId, $"{PromoCollectionName(col)} — {PromoRarityLabel(rarity)} (стр. {page + 1}/{totalPages}):", new InlineKeyboardMarkup(rows), ct);
        }

        // Принимает картинку либо как отправленное фото/файл (скачивает и кладёт в global_storage),
        // либо как текстовую ссылку/URL. Возвращает готовую картинку (url или имя файла) или null.
        private async Task<string> ProcessNewsImageAsync(ITelegramBotClient botClient, Message message, CancellationToken ct)
        {
            try
            {
                string text = message?.Text?.Trim();
                System.IO.File.AppendAllText("bot_error.txt", $"[news-img] msgType={message?.Type} hasText={message?.Text != null} photo={message?.Photo?.Length} doc={message?.Document != null}\n");

                // Ссылка / отказ от картинки
                if (string.Equals(text, "-", StringComparison.OrdinalIgnoreCase)) return "";
                if (text?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true ||
                    text?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return text;
                }

                // Отправленное фото
                if (message.Photo != null && message.Photo.Length > 0)
                {
                    var largest = message.Photo.OrderByDescending(p => p.Width * p.Height).First();
                    byte[] bytes = await DownloadTelegramFileAsync(largest.FileId, ct);
                    if (bytes == null || bytes.Length == 0) return null;
                    string filename = "news_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".jpg";
                    BoltMainDatabaseProvider.Instance.WriteGlobalFile(filename, bytes);
                    return $"http://{StandRiseServer.RpcServer.StaticClasses.PublicIp}:2224/api/news/image?filename={filename}";
                }

                // Отправленный файл/документ (картинка)
                if (message.Document != null)
                {
                    byte[] bytes = await DownloadTelegramFileAsync(message.Document.FileId, ct);
                    if (bytes == null || bytes.Length == 0) return null;
                    string ext = string.IsNullOrEmpty(message.Document.FileName)
                        ? "jpg"
                        : System.IO.Path.GetExtension(message.Document.FileName).TrimStart('.').ToLower();
                    if (string.IsNullOrEmpty(ext) || ext.Length > 4) ext = "jpg";
                    string filename = "news_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "." + ext;
                    BoltMainDatabaseProvider.Instance.WriteGlobalFile(filename, bytes);
                    return $"http://{StandRiseServer.RpcServer.StaticClasses.PublicIp}:2224/api/news/image?filename={filename}";
                }

                System.IO.File.AppendAllText("bot_error.txt", "[news-img] no image recognized\n");
                return null;
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("bot_error.txt", $"[news-img] ProcessNewsImage error: {ex}\n");
                Console.WriteLine($"[News] ProcessNewsImage error: {ex.Message}");
                return null;
            }
        }

        // Скачивает файл из Telegram по file_id через Bot API (не зависит от версии библиотеки).
        private async Task<byte[]> DownloadTelegramFileAsync(string fileId, CancellationToken ct)
        {
            try
            {
                using (var http = new System.Net.Http.HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(30);
                    string getFileUrl = $"https://api.telegram.org/bot{_botToken}/getFile?file_id={Uri.EscapeDataString(fileId)}";
                    string json = await http.GetStringAsync(getFileUrl, ct);
                    using (var doc = System.Text.Json.JsonDocument.Parse(json))
                    {
                        if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return null;
                        string filePath = doc.RootElement.GetProperty("result").GetProperty("file_path").GetString();
                        if (string.IsNullOrEmpty(filePath)) return null;
                        string downloadUrl = $"https://api.telegram.org/file/bot{_botToken}/{filePath}";
                        return await http.GetByteArrayAsync(downloadUrl, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("bot_error.txt", $"[news-dl] DownloadTelegramFile error: {ex.Message}\n");
                Console.WriteLine($"[News] DownloadTelegramFile error: {ex.Message}");
                return null;
            }
        }

        private async Task CreateNewsPopup(ITelegramBotClient botClient, long chatId, UserContext context, CancellationToken ct)
        {
            try
            {
                string image = string.IsNullOrWhiteSpace(context.NewsImage) ? "" : context.NewsImage;
                BoltMainDatabaseProvider.Instance.PublishAnnouncement(
                    context.NewsTitle ?? "",
                    context.NewsLink ?? "",
                    image);

                ModActionLog.Log(chatId, context.Username, "create_popup", $"title={context.NewsTitle} image={image} link={context.NewsLink}");
                await botClient.SendMessage(chatId, "✅ Popup опубликован!\nТitle: " + context.NewsTitle + "\nСсылка: " + (string.IsNullOrEmpty(context.NewsLink) ? "(нет)" : context.NewsLink), cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, "❌ Ошибка: " + ex.Message, cancellationToken: ct);
            }
            finally
            {
                context.State = UserState.None;
                context.NewsTitle = null;
                context.NewsImage = null;
                context.NewsLink = null;
                await ShowMainMenu(botClient, chatId, ct);
            }
        }

        private async Task CreateLegendsSpinPopup(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            try
            {
                // Legend's Spin popup: продвигает спин; само вращение выполняется в игре
                // через обмен LEGENDS_SPIN за 1 Spin Token (item 219).
                string image = "https://soldgaming.ru/news.png";
                string title = "Legend's Spin — крути и забирай награды!";
                string link = "https://t.me/ProjectRework";
                BoltMainDatabaseProvider.Instance.PublishAnnouncement(title, link, image);

                var logCtx = _userContexts.GetOrAdd(chatId, _ => new UserContext());
                ModActionLog.Log(chatId, logCtx.Username, "create_popup", $"title={title} (Legend's Spin)");
                await botClient.SendMessage(chatId, "✅ Popup Legend's Spin опубликован!\nСпин доступен в игре за 1 Spin Token.", cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, "❌ Ошибка: " + ex.Message, cancellationToken: ct);
            }
            finally
            {
                var ctx = _userContexts.GetOrAdd(chatId, _ => new UserContext());
                ctx.State = UserState.None;
                ctx.NewsTitle = null;
                ctx.NewsImage = null;
                ctx.NewsLink = null;
                await ShowMainMenu(botClient, chatId, ct);
            }
        }

        private async Task ShowNewsMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("➕ Создать popup", "news_create") },
                new[] { InlineKeyboardButton.WithCallbackData("🎡 Legend's Spin popup", "news_legendsspin") },
                new[] { InlineKeyboardButton.WithCallbackData("📋 Список и удаление", "news_list") },
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "cancel") }
            });
            await botClient.SendMessage(chatId, "*Popup / Новости*\nВыбери действие:", parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
        }

        private async Task ListNewsPopups(ITelegramBotClient botClient, long chatId, CancellationToken ct)
        {
            var items = BoltMainDatabaseProvider.Instance.GetAllAnnouncements();
            if (items == null || items.Count == 0)
            {
                await botClient.SendMessage(chatId, "Popup-объявлений пока нет.", cancellationToken: ct);
                return;
            }

            var buttons = new List<InlineKeyboardButton[]>();
            int shown = 0;
            foreach (var it in items)
            {
                if (shown >= 50) break;
                string title = string.IsNullOrEmpty(it.title) ? "(без заголовка)" : it.title;
                string status = it.active ? "" : " (выкл)";
                string shorten = title.Length > 40 ? title.Substring(0, 40) + "…" : title;
                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData($"{shorten}{status}  ❌", $"del_news:{it._id}") });
                shown++;
            }
            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", "menu_news") });

            await botClient.SendMessage(chatId, $"*Popup-объявления ({items.Count}):*\nНажми на ❌ рядом с позицией, чтобы удалить её.",
                parseMode: ParseMode.Markdown, replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
        }

        private async Task DeleteNewsPopup(ITelegramBotClient botClient, long chatId, string newsId, CancellationToken ct)
        {
            bool ok = BoltMainDatabaseProvider.Instance.DeleteAnnouncement(newsId);
            await botClient.SendMessage(chatId, ok
                ? "✅ Popup удалён."
                : "❌ Не удалось удалить (уже удалён или не найден).", cancellationToken: ct);
            await ListNewsPopups(botClient, chatId, ct);
        }

        // Общая логика завершения создания промокода - раньше жила только внутри одного case
        // (ручной ввод валюты текстом). Вынесена в метод, чтобы её же мог вызвать и новый
        // флоу с кнопками выбора валюты (promo_currency_done), не дублируя лимиты/апрув
        // мелких модеров и саму запись в базу.
        private async Task FinalizePromoCreation(ITelegramBotClient botClient, long chatId, long userId, string username, UserContext context, List<Tuple<int, int>> currencies, CancellationToken cancellationToken)
        {
            bool isJMod = _roleManager.IsJuniorModerator(userId);
            // Anyone who passed the /pass gate gets full admin promo powers (no daily caps,
            // no currency limit, no approval step).
            bool isAdmin = _roleManager.IsAdmin(username, userId) || _hasPassed.ContainsKey(userId);

            // Раньше числовой лимит (5 промо/день, 5000 голды/промо) был жёстко зашит в код и
            // действовал ТОЛЬКО на мелких модеров - обычные модеры создавали промокоды вообще
            // без ограничений. Теперь лимиты настраиваются на каждого модера отдельно (админ
            // задаёт их в панели "Персонал" -> "Лимиты модеров") и действуют на ЛЮБОГО не-админа.
            // Апрув админом промокодов С ПРЕДМЕТАМИ остаётся только у мелких модеров - это
            // отдельный уровень доверия, не связанный с числовыми лимитами.
            if (!isAdmin)
            {
                var limits = _roleManager.GetModeratorLimits(userId);

                if (DateTime.UtcNow.Date > _lastPromoResetDate)
                {
                    _modDailyPromos.Clear();
                    _lastPromoResetDate = DateTime.UtcNow.Date;
                }

                int todayCount = _modDailyPromos.GetOrAdd(userId, 0);
                if (todayCount >= limits.MaxPromosPerDay)
                {
                    await botClient.SendMessage(chatId, $"Лимит промокодов на сегодня исчерпан ({limits.MaxPromosPerDay}/день).", cancellationToken: cancellationToken);
                    context.State = UserState.None;
                    await ShowMainMenu(botClient, chatId, cancellationToken);
                    return;
                }

                long totalCurrency = currencies.Sum(c => c.Item2);
                if (totalCurrency > limits.MaxCurrencyPerPromo)
                {
                    await botClient.SendMessage(chatId, $"Максимум {limits.MaxCurrencyPerPromo} валюты за один промокод.", cancellationToken: cancellationToken);
                    return;
                }

                if (isJMod && context.Items != null && context.Items.Count > 0)
                {
                    string requestId = Guid.NewGuid().ToString().Substring(0, 8);
                    var promoReq = new PendingPromoInfo
                    {
                        Code = context.PromoCode,
                        Max = context.MaxUses,
                        Items = context.Items,
                        Currencies = currencies,
                        CreatorId = userId,
                        CreatorChat = chatId
                    };
                    _pendingPromos[requestId] = System.Text.Json.JsonSerializer.Serialize(promoReq);

                    string itemsBreakdown = context.Items.Count > 0
                        ? string.Join("\n", context.Items.Select(i =>
                        {
                            string itemName = _cachedItems.FirstOrDefault(c => c.key == i.Item1)?.displayName ?? $"itemId {i.Item1}";
                            return $"- {itemName} (id {i.Item1}) x{i.Item2}";
                        }))
                        : "(нет)";
                    string currenciesBreakdown = currencies.Count > 0
                        ? string.Join("\n", currencies.Select(c => $"- {CurrencyLabel(c.Item1)}: {c.Item2}"))
                        : "(нет)";
                    string adminMsg = $"*Запрос на промокод из предметов!*\nМелкий модер ID: {userId}\nПромо: {context.PromoCode}\nАктиваций: {context.MaxUses}\n\nВалюта (всего {totalCurrency}):\n{currenciesBreakdown}\n\nПредметы ({context.Items.Count}):\n{itemsBreakdown}";
                    var approvalKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Одобрить", $"approve_promo:{requestId}"), InlineKeyboardButton.WithCallbackData("Отклонить", $"reject_promo:{requestId}") }
                    });

                    foreach (var adminId in _roleManager.GetAdminIds())
                    {
                        try { await botClient.SendMessage(adminId, adminMsg, parseMode: ParseMode.Markdown, replyMarkup: approvalKeyboard, cancellationToken: cancellationToken); } catch { }
                    }

                    await botClient.SendMessage(chatId, "В промокоде есть предметы. Запрос отправлен админам на проверку.", cancellationToken: cancellationToken);
                    _modDailyPromos[userId] = todayCount + 1;
                    ModActionLog.Log(userId, username, "promo_request", $"code={context.PromoCode} items={context.Items.Count} currency={totalCurrency} (на апруве)");
                    context.State = UserState.None;
                    await ShowMainMenu(botClient, chatId, cancellationToken);
                    return;
                }
                _modDailyPromos[userId] = todayCount + 1;
            }

            await BoltGameDatabaseProvider.Instance.CreateCoupon(context.PromoCode, context.MaxUses, context.Items, currencies);
            ModActionLog.Log(userId, username, "promo_create", $"code={context.PromoCode} maxUses={context.MaxUses} items={context.Items?.Count ?? 0} currency={currencies.Sum(c => c.Item2)}");
            await botClient.SendMessage(chatId, $"Промокод создан.\nТвой промокод: `{context.PromoCode}`", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            context.State = UserState.None;
            await ShowMainMenu(botClient, chatId, cancellationToken);
        }

        private async Task KickPlayer(string userUid, long chatId, CancellationToken cancellationToken)
        {
            string playerId = userUid;
            var players = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(userUid);
            if (players.Length > 0)
            {
                playerId = players[0]._id.ToString();
            }

            // Check StaticClasses.UserServices
            if (StaticClasses.UserServices.TryGetValue(playerId, out var userService))
            {
                try
                {
                    userService.TcpClient.Close(); // This should trigger disconnect logic
                    ModActionLog.Log(chatId, null, "kick", $"uid={userUid} playerId={playerId}");
                    await _botClient.SendMessage(chatId, $"Игрок {userUid} (ID: {playerId}) кикнут.", cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    await _botClient.SendMessage(chatId, $"Не удалось кикнуть: {ex.Message}", cancellationToken: cancellationToken);
                }
            }
            else
            {
                await _botClient.SendMessage(chatId, $"Игрок {userUid} не в сети.", cancellationToken: cancellationToken);
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception.ToString();
            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }
        private async Task BanPlayer(string userUid, string reason, int banCode, long chatId, CancellationToken cancellationToken)
        {
            // Resolve UID to ObjectId
            var players = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(userUid);
            if (players.Length == 0)
            {
                await _botClient.SendMessage(chatId, $"Игрок {userUid} не найден.", cancellationToken: cancellationToken);
                return;
            }
            var playerId = players[0]._id;

            try
            {
                BoltMainDatabaseProvider.Instance.BanPlayer(playerId, reason, banCode);

                // Kick if online with ban screen
                var playerDoc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(playerId);
                StaticClasses.KickWithBan(playerId.ToString(), reason, banCode, playerDoc?.uid ?? playerId.ToString());

                ModActionLog.Log(chatId, null, "ban", $"uid={userUid} playerId={playerId} reason={reason} banCode={banCode}");
                await _botClient.SendMessage(chatId, $"Игрок {userUid} забанен.\nПричина: {reason}\nБанкод: {banCode}", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(chatId, $"Не снеслось: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        private async Task UnbanPlayer(string userUid, long chatId, CancellationToken cancellationToken)
        {
            var players = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(userUid);
            if (players.Length == 0)
            {
                await _botClient.SendMessage(chatId, $"Игрок {userUid} не найден.", cancellationToken: cancellationToken);
                return;
            }
            var playerId = players[0]._id;

            try
            {
                BoltMainDatabaseProvider.Instance.UnbanPlayer(playerId);
                ModActionLog.Log(chatId, null, "unban", $"uid={userUid} playerId={playerId}");
                await _botClient.SendMessage(chatId, $"Игрок {userUid} разбанен.", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(chatId, $"Не удалось разбанить: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        // Аудит-лог доступен только админам (см. гейт callerIsAdmin в "mod_logs" в
        // HandleCallbackQuery). filterUserId != null - смотрим действия конкретного модера.
        private async Task ShowModLogs(ITelegramBotClient botClient, long chatId, long? filterUserId, CancellationToken ct)
        {
            var entries = ModActionLog.GetRecentFormatted(30, filterUserId);
            if (entries.Count == 0)
            {
                await botClient.SendMessage(chatId, "Логов пока нет.", cancellationToken: ct);
                return;
            }

            string header = filterUserId == null ? "Последние действия модеров/админов:" : $"Последние действия {filterUserId}:";
            string body = string.Join("\n", entries);
            string full = $"{header}\n\n{body}";

            // Telegram режет сообщения на ~4096 символов - если лог большой, режем на части.
            const int chunkSize = 3500;
            for (int i = 0; i < full.Length; i += chunkSize)
            {
                string chunk = full.Substring(i, Math.Min(chunkSize, full.Length - i));
                await botClient.SendMessage(chatId, chunk, cancellationToken: ct);
            }
        }

        private async Task ListPromos(ITelegramBotClient botClient, long chatId, int page, CancellationToken cancellationToken)
        {
            try
            {
                var coupons = await BoltGameDatabaseProvider.Instance.GetAllCoupons();
                if (coupons.Count == 0)
                {
                    await botClient.SendMessage(chatId, "Список промокодов пуст.", cancellationToken: cancellationToken);
                    await ShowMainMenu(botClient, chatId, cancellationToken);
                    return;
                }

                int itemsPerPage = 5;
                int totalPages = (int)Math.Ceiling((double)coupons.Count / itemsPerPage);
                page = Math.Max(0, Math.Min(page, totalPages - 1));

                var currentCoupons = coupons.Skip(page * itemsPerPage).Take(itemsPerPage);

                string message = $"Список промокодов (Стр. {page + 1}/{totalPages}):\n\n";
                var buttons = new List<InlineKeyboardButton[]>();

                foreach (var coupon in currentCoupons)
                {
                    message += $"{coupon.couponId} | Акт: {coupon.activatedPlayers.Length}/{coupon.maxActivations}\n";
                    buttons.Add(new[] { InlineKeyboardButton.WithCallbackData($"Удалить {coupon.couponId}", $"delete_promo:{coupon.couponId}") });
                }

                // Navigation buttons
                var navButtons = new List<InlineKeyboardButton>();
                if (page > 0) navButtons.Add(InlineKeyboardButton.WithCallbackData("<-", $"list_promos:{page - 1}"));
                if (page < totalPages - 1) navButtons.Add(InlineKeyboardButton.WithCallbackData("->", $"list_promos:{page + 1}"));

                if (navButtons.Count > 0) buttons.Add(navButtons.ToArray());

                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("🗑 Удалить ВСЕ промокоды", "promo_delete_all") });
                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", "cancel") });

                // Use EditMessageText if possible, or SendMessage (simplifying to SendMessage for now to avoid ID tracking complexity,
                // but for better UX we should edit. Since we don't track MessageId easily here without Context change, SendMessage is safer logic-wise)
                // Actually, let's try to just SendMessage to keep it simple as requested, clearing up old ones is harder without tracking.

                await botClient.SendMessage(
                chatId: chatId,
                text: message,
                replyMarkup: new InlineKeyboardMarkup(buttons),
                cancellationToken: cancellationToken
                );
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, $"Ошибка: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        private async Task DeletePromo(ITelegramBotClient botClient, long chatId, string promoId, CancellationToken cancellationToken)
        {
            try
            {
                await BoltGameDatabaseProvider.Instance.DeleteCoupon(promoId);
                await botClient.SendMessage(chatId, $"Промокод {promoId} удален.", cancellationToken: cancellationToken);
                await ListPromos(botClient, chatId, 0, cancellationToken);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, $"Ошибка при удалении: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        private async Task ListItems(ITelegramBotClient botClient, long chatId, int page, CancellationToken cancellationToken)
        {
            try
            {
                var items = _cachedItems
                .Where(i => i.displayName != null)
                .OrderBy(i => i.key)
                .ToList();

                if (items.Count == 0)
                {
                    await botClient.SendMessage(chatId, "JSON с предметами не загружен или пуст.", cancellationToken: cancellationToken);
                    return;
                }

                int itemsPerPage = 20;
                int totalPages = (int)Math.Ceiling((double)items.Count / itemsPerPage);
                page = Math.Max(0, Math.Min(page, totalPages - 1));

                var currentItems = items.Skip(page * itemsPerPage).Take(itemsPerPage);

                var message = $"Список предметов (Стр. {page + 1}/{totalPages}):\n\n";
                foreach (var item in currentItems)
                {
                    message += $"{item.key}: {item.displayName ??"Unnamed"}\n";
                }

                var buttons = new List<InlineKeyboardButton[]>();
                var navButtons = new List<InlineKeyboardButton>();
                if (page > 0) navButtons.Add(InlineKeyboardButton.WithCallbackData("<-", $"list_items:{page - 1}"));
                if (page < totalPages - 1) navButtons.Add(InlineKeyboardButton.WithCallbackData("->", $"list_items:{page + 1}"));

                if (navButtons.Count > 0) buttons.Add(navButtons.ToArray());
                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад", "cancel") });

                await botClient.SendMessage(
                chatId: chatId,
                text: message,
                replyMarkup: new InlineKeyboardMarkup(buttons),
                cancellationToken: cancellationToken
                );
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, $"Ошибка: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        private async Task GetServerStats(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            try
            {
                var activeSessions = StaticClasses.UserServices
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null && pair.Value.IsOnlineActive())
                .ToList();

                int onlineCount = activeSessions.Count;
                int connectedCount = StaticClasses.UserServices.Count;
                var totalPlayers = BoltMainDatabaseProvider.Instance.GetTotalPlayers();
                var bannedPlayers = BoltMainDatabaseProvider.Instance.GetTotalBannedPlayers();

                var process = System.Diagnostics.Process.GetCurrentProcess();
                var ramUsage = process.WorkingSet64 / (1024 * 1024);

                var message = $"*Статистика сервера*\n\n"+
                $"*Онлайн сейчас:* {onlineCount}\n"+
                $"*Сессий в памяти:* {connectedCount}\n"+
                $"*Всего игроков:* {totalPlayers}\n"+
                $"*Забанено:* {bannedPlayers}\n"+
                $"*RAM:* {ramUsage} MB";

                await botClient.SendMessage(chatId, message, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                await ShowMainMenu(botClient, chatId, cancellationToken);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, $"Ошибка получения статистики: {ex.Message}", cancellationToken: cancellationToken);
            }
        }


        private async Task ClearInventory(string uid, long chatId, CancellationToken cancellationToken)
        {
            var players = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(uid);
            if (players.Length == 0)
            {
                await _botClient.SendMessage(chatId, $"Игрок {uid} не найден.", cancellationToken: cancellationToken);
                return;
            }
            var playerId = players[0]._id;

            try
            {
                bool ok = InventoryRemoteEventListener.ClearPlayerAndNotify(playerId);
                ModActionLog.Log(chatId, null, "clear_inventory", $"uid={uid} playerId={playerId}");
                await _botClient.SendMessage(chatId,
                    ok ? $"Инвентарь игрока {uid} очищен." : $"Инвентарь игрока {uid}: документ не найден, создайте инвентарь повторным входом.",
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(chatId, $"Ошибка при очистке: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        private async Task SetPlayerLevel(string uid, int level, long chatId, CancellationToken cancellationToken)
        {
            var players = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(uid);
            if (players.Length == 0)
            {
                await _botClient.SendMessage(chatId, $"Игрок {uid} не найден.", cancellationToken: cancellationToken);
                return;
            }
            var playerId = players[0]._id;

            try
            {
                BoltGameDatabaseProvider.Instance.StoreStat(playerId, "level_id", level);
                ModActionLog.Log(chatId, null, "set_level", $"uid={uid} playerId={playerId} level={level}");
                await _botClient.SendMessage(chatId, $"Игроку {uid} установлен {level} уровень.", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(chatId, $"Ошибка при выдаче уровня: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        private const int AdminGoldPassItemId = 608; // CursedSoulsGoldPass
        private const int AdminSpinTokenItemId = 201; // Halloween2021_Spin / Cursed Souls

        private async Task GiveGoldPass(string uid, long chatId, string username, CancellationToken cancellationToken)
        {
            var players = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(uid);
            if (players.Length == 0)
            {
                await _botClient.SendMessage(chatId, $"Игрок {uid} не найден.", cancellationToken: cancellationToken);
                return;
            }
            string playerId = players[0]._id.ToString();
            try
            {
                GrantInventoryItem(playerId, AdminGoldPassItemId);
                // 613 — Hot Winter gold pass (на случай другого клиента)
                GrantInventoryItem(playerId, 613);
                ModActionLog.Log(chatId, username, "give_goldpass", $"uid={uid} playerId={playerId}");
                await _botClient.SendMessage(chatId, $"Gold Pass выдан игроку {uid} (item 608/613). Пусть перезайдёт в игру.", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(chatId, $"Ошибка выдачи Gold Pass: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        private async Task GiveGold(string uid, int amount, long chatId, string username, CancellationToken cancellationToken)
        {
            var players = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(uid);
            if (players.Length == 0)
            {
                await _botClient.SendMessage(chatId, $"Игрок {uid} не найден.", cancellationToken: cancellationToken);
                return;
            }
            var playerOid = players[0]._id;
            try
            {
                BoltGameDatabaseProvider.Instance.CurrencyPlusValue(playerOid, 102, amount);
                ModActionLog.Log(chatId, username, "give_gold", $"uid={uid} playerId={playerOid} amount={amount}");
                await _botClient.SendMessage(chatId, $"Игроку {uid} выдано {amount} голды.", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(chatId, $"Ошибка выдачи голды: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        private async Task GiveSpins(string uid, int amount, long chatId, string username, CancellationToken cancellationToken)
        {
            var players = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(uid);
            if (players.Length == 0)
            {
                await _botClient.SendMessage(chatId, $"Игрок {uid} не найден.", cancellationToken: cancellationToken);
                return;
            }
            string playerId = players[0]._id.ToString();
            try
            {
                for (int i = 0; i < amount; i++)
                    GrantInventoryItem(playerId, AdminSpinTokenItemId);
                ModActionLog.Log(chatId, username, "give_spins", $"uid={uid} playerId={playerId} amount={amount}");
                await _botClient.SendMessage(chatId, $"Игроку {uid} выдано {amount} спинов (Spin Token #{AdminSpinTokenItemId}).", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(chatId, $"Ошибка выдачи спинов: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        private static void GrantInventoryItem(string playerId, int itemDefinitionId)
        {
            var db = BoltGameDatabaseProvider.Instance;
            var oid = ObjectId.Parse(playerId);
            var inv = db.GetPlayerInventoryDocument(oid);
            int nextId = (inv.InventoryItems.ElementCount > 0)
                ? inv.InventoryItems.Select(x => int.TryParse(x.Name, out int parsed) ? parsed : 0).Max() + 1
                : 1;
            db.AddItemToPlayerInventoryDocument(oid, new BoltInventoryItem
            {
                itemDefinitionId = itemDefinitionId,
                quantity = 1,
                flags = 0,
                date = BsonDateTime.Create(DateTime.UtcNow)
            }, nextId);
        }

        private async Task HandleAmiraChatAsync(ITelegramBotClient botClient, long chatId, long userId, string username, string text, CancellationToken ct)
        {
            await botClient.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);
            var aiResponse = await _amiraService.SendMessageAsync(chatId, username ?? "Участник", text);

            bool continueLoop = true;
            while (continueLoop && aiResponse?.ToolCalls != null && aiResponse.ToolCalls.Count > 0)
            {
                foreach (var toolCall in aiResponse.ToolCalls)
                {
                    if (toolCall.Function.Name == "create_promo")
                    {
                        try
                        {
                            var args = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(toolCall.Function.Arguments);
                            string itemName = args.GetProperty("item_name").GetString();
                            int gold = args.GetProperty("gold").GetInt32();
                            int silver = args.GetProperty("silver").GetInt32();
                            int uses = args.GetProperty("uses").GetInt32();
                            
                            // Find item ID
                            int itemId = 0;
                            if (int.TryParse(itemName, out int parsedId)) itemId = parsedId;
                            else
                            {
                                InventoryCatalogueLoader.Instance.EnsureLoaded();
                                var found = InventoryCatalogueLoader.Instance.GetAll().FirstOrDefault(d => d != null && d.displayName != null && d.displayName.Contains(itemName, StringComparison.OrdinalIgnoreCase));
                                if (found != null) itemId = found.key;
                            }

                            string promoCode = "AMIRA-" + Guid.NewGuid().ToString().Substring(0, 6).ToUpper();
                            var items = new List<Tuple<int, int>>();
                            if (itemId > 0) items.Add(new Tuple<int, int>(itemId, 1));
                            
                            var currencies = new List<Tuple<int, int>>();
                            if (gold > 0) currencies.Add(new Tuple<int, int>(101, gold));
                            if (silver > 0) currencies.Add(new Tuple<int, int>(102, silver));

                            await BoltGameDatabaseProvider.Instance.CreateCoupon(promoCode, uses, items, currencies);
                            ModActionLog.Log(userId, username, "promo_create_amira", $"code={promoCode} maxUses={uses}");
                            
                            aiResponse = await _amiraService.SendMessageAsync(chatId, username, null, new AmiraAiService.ChatMessage { Role = "tool", ToolCallId = toolCall.Id, Name = toolCall.Function.Name, Content = $"Успех! Промокод создан: {promoCode} (Предмет ID: {itemId}, Голда: {gold}, Серебро: {silver}, Использований: {uses})" });
                        }
                        catch (Exception ex)
                        {
                            aiResponse = await _amiraService.SendMessageAsync(chatId, username, null, new AmiraAiService.ChatMessage { Role = "tool", ToolCallId = toolCall.Id, Name = toolCall.Function.Name, Content = $"Ошибка при создании промокода: {ex.Message}" });
                        }
                    }
                    else if (toolCall.Function.Name == "ban_user")
                    {
                        try
                        {
                            var args = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(toolCall.Function.Arguments);
                            string playerId = args.GetProperty("player_id").GetString();
                            string reason = args.GetProperty("reason").GetString();

                            await BanPlayer(playerId, reason, 1001, chatId, ct);
                            aiResponse = await _amiraService.SendMessageAsync(chatId, username, null, new AmiraAiService.ChatMessage { Role = "tool", ToolCallId = toolCall.Id, Name = toolCall.Function.Name, Content = $"Успех! Игрок забанен." });
                        }
                        catch (Exception ex)
                        {
                            aiResponse = await _amiraService.SendMessageAsync(chatId, username, null, new AmiraAiService.ChatMessage { Role = "tool", ToolCallId = toolCall.Id, Name = toolCall.Function.Name, Content = $"Ошибка при бане игрока: {ex.Message}" });
                        }
                    }
                }
                if (aiResponse?.ToolCalls == null || aiResponse.ToolCalls.Count == 0) continueLoop = false;
            }

            if (!string.IsNullOrEmpty(aiResponse?.Content))
            {
                var botKeyboard = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("Выйти из чата", "cancel") } });
                await botClient.SendMessage(chatId, aiResponse.Content, replyMarkup: botKeyboard, cancellationToken: ct);
            }
        }

        // Раньше тут были ShowUserMenu/HandlePublicCallback/ForwardToAdmins - встроенная
        // поддержка для не-стаффа (магазин из этого бота убрали ещё раньше). Теперь гостям
        // вообще ничего не показываем - см. гейт "if (!isStaff)" в начале обработчика Update,
        // который сразу отвечает "Пиши сюда: @StandHeal" и не доходит до этого кода.
        //
        // Магазин через этот бот больше не продаёт ничего, так что успешных платежей сюда
        // прилетать не должно. Оставлено как safety-net на случай старого зависшего инвойса
        // у кого-то в чате - просто уведомляем и сообщаем админам.
        private async Task HandleSuccessfulPayment(ITelegramBotClient botClient, Message message, CancellationToken ct)
        {
            var payment = message.SuccessfulPayment;
            var payload = payment.InvoicePayload;
            var chatId = message.Chat.Id;

            await botClient.SendMessage(chatId, "Оплата получена, но магазин в этом боте отключён. Обратись в поддержку с чеком - разберёмся вручную.", cancellationToken: ct);
            foreach (var adminId in _roleManager.GetAdminIds())
            {
                try { await botClient.SendMessage(adminId, $"Пришёл платёж в отключённый магазин! Юзер: @{message.From?.Username} ({chatId})\nPayload: {payload}"); } catch { }
            }
        }

    }
}

