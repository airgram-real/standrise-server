using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using MongoDB.Bson;

namespace StandRiseServer.RpcServer
{
    public class PromoBotService
    {
        private ITelegramBotClient _botClient;
        private CancellationTokenSource _cts;
        private List<string> _adminUsernames = new List<string>();
        private List<long> _adminIds = new List<long>();

        private class BotUser
        {
            public long ChatId { get; set; }
            public string Username { get; set; }
            public DateTime LastClaim { get; set; } = DateTime.MinValue;
        }

        private ConcurrentDictionary<long, BotUser> _users = new ConcurrentDictionary<long, BotUser>();
        private string _usersFile = "promo_bot_users.json";

        private HashSet<long> _waitingForBroadcast = new HashSet<long>();
        private HashSet<long> _waitingForPromo = new HashSet<long>();

        public void Start(string token, List<string> adminUsernames, List<long> adminIds, string usersFile = "promo_bot_users.json")
        {
            _botClient = new TelegramBotClient(token);
            _adminUsernames = adminUsernames ?? new List<string>();
            _adminIds = adminIds ?? new List<long>();
            _cts = new CancellationTokenSource();
            _usersFile = usersFile;

            LoadUsers();

            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandlePollingErrorAsync,
                new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
                _cts.Token
            );

            Console.WriteLine($"[PromoBot] Started with token: {token.Substring(0, 10)}...");
        }

        private void LoadUsers()
        {
            if (System.IO.File.Exists(_usersFile))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(_usersFile);
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<BotUser>>(json, options);
                    if (list != null)
                    {
                        foreach (var u in list) _users[u.ChatId] = u;
                        Console.WriteLine($"[PromoBot] Loaded {_users.Count} users from {_usersFile}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PromoBot] Error loading users: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[PromoBot] {_usersFile} not found, starting fresh.");
            }
        }

        private void SaveUsers()
        {
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(_users.Values.ToList());
                System.IO.File.WriteAllText(_usersFile, json);
            }
            catch { }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
        {
            if (update.Type == UpdateType.Message && update.Message != null)
            {
                await HandleMessage(botClient, update.Message, ct);
            }
            else if (update.Type == UpdateType.CallbackQuery)
            {
                await HandleCallback(botClient, update.CallbackQuery, ct);
            }
        }

        private async Task<bool> IsSubscribed(ITelegramBotClient botClient, long userId)
        {
            try
            {
                var member = await botClient.GetChatMember("@ProjectShock", userId);
                return member.Status != ChatMemberStatus.Left && member.Status != ChatMemberStatus.Kicked;
            }
            catch
            {
                return false;
            }
        }

        private async Task HandleMessage(ITelegramBotClient botClient, Message message, CancellationToken ct)
        {
            long chatId = message.Chat.Id;
            string text = message.Text?.ToLower();
            string username = message.From?.Username;

            var user = _users.GetOrAdd(chatId, new BotUser { ChatId = chatId, Username = username });
            if (user.Username != username) { user.Username = username; SaveUsers(); }

            if (text == "/start")
            {
                if (!await IsSubscribed(botClient, message.From.Id))
                {
                    var subKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithUrl("📢 Подписаться на канал", "https://t.me/ProjectShock") },
                        new[] { InlineKeyboardButton.WithCallbackData("✅ Я подписался", "check_sub_and_start") }
                    });
                    await botClient.SendMessage(chatId, "❌ Чтобы пользоваться ботом, ты должен быть подписан на наш канал @ProjectShock!", replyMarkup: subKeyboard, cancellationToken: ct);
                    return;
                }

                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("🎁 Забрать ежедневный бонус", "claim_daily") }
                });
                await botClient.SendMessage(chatId, "👋 Привет! Данный бот принадлежит теперь приватке на версии 0.13.0 @ProjectShock. Теперь вы можете получать промокоды по данной приватке, функционал бота: Здесь ты можешь получать ежедневные промокоды для нашей игры.\n\nПросто нажми кнопку ниже раз в 24 часа! 👇", replyMarkup: keyboard, cancellationToken: ct);
                return;
            }

            if ((text == "админ" || text == "/admin") && IsAdmin(username, message.From.Id))
            {
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("📢 Сделать рассылку", "admin_broadcast") },
                    new[] { InlineKeyboardButton.WithCallbackData("🎫 Создать промо", "admin_promo") },
                    new[] { InlineKeyboardButton.WithCallbackData("📊 Статистика", "admin_stats") }
                });
                await botClient.SendMessage(chatId, "🛠 Админ-панель Promo Bot:", replyMarkup: keyboard, cancellationToken: ct);
                return;
            }
            else if (text == "админ" || text == "/admin")
            {
                await botClient.SendMessage(chatId, $"❌ Недостаточно прав. Твой ID: {message.From.Id}, Username: {username ?? "None"}", cancellationToken: ct);
                return;
            }

            if (text == "/resetme" && IsAdmin(username, message.From.Id))
            {
                user.LastClaim = DateTime.MinValue;
                SaveUsers();
                await botClient.SendMessage(chatId, "✅ Твой ежедневный бонус сброшен! Можешь получать снова.", cancellationToken: ct);
                return;
            }
            
            // If admin is waiting for broadcast
            if (_waitingForBroadcast.Contains(chatId))
            {
                _waitingForBroadcast.Remove(chatId);
                int count = await DoBroadcast(botClient, chatId, message.MessageId, ct);
                await botClient.SendMessage(chatId, $"✅ Рассылка завершена! Отправлено {count} пользователям.", cancellationToken: ct);
                return;
            }

            // If admin is creating a custom promo
            if (_waitingForPromo.Contains(chatId))
            {
                _waitingForPromo.Remove(chatId);
                await HandleManualPromoCreation(botClient, chatId, text, ct);
                return;
            }
        }

        private async Task HandleCallback(ITelegramBotClient botClient, CallbackQuery callback, CancellationToken ct)
        {
            long chatId = callback.Message.Chat.Id;
            string data = callback.Data;
            string username = callback.From.Username;

            await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

            if (data == "check_sub_and_start")
            {
                if (await IsSubscribed(botClient, callback.From.Id))
                {
                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("🎁 Забрать ежедневный бонус", "claim_daily") }
                    });
                    await botClient.SendMessage(chatId, "✅ Спасибо за подписку! Теперь ты можешь забрать свой бонус. 👇", replyMarkup: keyboard, cancellationToken: ct);
                }
                else
                {
                    await botClient.SendMessage(chatId, "❌ Ты все еще не подписан на канал @ProjectShock!", cancellationToken: ct);
                }
                return;
            }

            if (data == "claim_daily")
            {
                if (!await IsSubscribed(botClient, callback.From.Id))
                {
                    await botClient.SendMessage(chatId, "❌ Ошибка! Ты отписался от канала @ProjectShock. Подпишись обратно, чтобы получать бонусы.", cancellationToken: ct);
                    return;
                }
                var user = _users.GetOrAdd(chatId, new BotUser { ChatId = chatId, Username = username });
                var timeSinceClaim = DateTime.Now - user.LastClaim;
                
                if (timeSinceClaim.TotalHours >= 0 && timeSinceClaim.TotalHours < 24)
                {
                    var waitTime = TimeSpan.FromHours(24) - timeSinceClaim;
                    await botClient.SendMessage(chatId, $"⏳ Ты уже забирал бонус сегодня! Приходи через {(int)waitTime.TotalHours}ч {waitTime.Minutes}м.", cancellationToken: ct);
                    return;
                }

                // Generate rewards
                Random rnd = new Random();
                int gold = 1500 + rnd.Next(3000, 10001);
                
                // Get random item
                int itemDefId = 0;
                string itemName = "Пусто";
                try
                {
                    if (rnd.Next(1, 101) <= 10) // 10% chance
                    {
                        var items = BoltGameDatabaseProvider.Instance.GetAllItemDefinitions();
                        if (items.Count > 0)
                        {
                            var filteredItems = items.Where(i =>
                                i.itemType == 0 && i.properties != null && i.properties.Contains("Collection") &&
                                (i.properties["Collection"] == "Scorpion" ||
                                 i.properties["Collection"] == "Origin" ||
                                 i.properties["Collection"] == "Furious" ||
                                 i.properties["Collection"] == "Rival")).ToList();

                            if (filteredItems.Count > 0)
                            {
                                var skin = filteredItems[rnd.Next(filteredItems.Count)];
                                itemDefId = skin.key;
                                itemName = skin.displayName ?? skin.key.ToString();
                            }
                        }
                    }
                }
                catch { }

                // Create Promo
                string code = "DAILY-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
                var currencies = new List<Tuple<int, int>> { new Tuple<int, int>(102, gold) }; // 102 is Gold
                var itemsList = new List<Tuple<int, int>>();
                if (itemDefId > 0) itemsList.Add(new Tuple<int, int>(itemDefId, 1));

                try
                {
                    await BoltGameDatabaseProvider.Instance.CreateCoupon(code, 1, itemsList, currencies);
                    Console.WriteLine($"[PromoBot] Created daily promo: {code} (Gold: {gold}, Item: {itemName})");
                    
                    user.LastClaim = DateTime.Now;
                    SaveUsers();

                    await botClient.SendMessage(chatId, $"🎁 Твой ежедневный бонус готов!\n\n🎟 Промокод: `{code}`\n💰 Содержимое: {gold} голды + {itemName}\n\n⚠️ Промокод на 1 активацию! Вводи его в игре.", parseMode: ParseMode.Markdown, cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    await botClient.SendMessage(chatId, $"❌ Ошибка при создании промокода: {ex.Message}", cancellationToken: ct);
                }
            }
            else if (data == "admin_broadcast")
            {
                _waitingForBroadcast.Add(chatId);
                await botClient.SendMessage(chatId, "📝 Введи текст для рассылки всем пользователям бота:", cancellationToken: ct);
            }
            else if (data == "admin_promo")
            {
                _waitingForPromo.Add(chatId);
                await botClient.SendMessage(chatId, "📝 Введите параметры промокода в формате:\n`КОД ГОЛДА КОЛ-ВО_АКТИВАЦИЙ` (через пробел)\n\nПример: `TESTPROMO 5000 100`", parseMode: ParseMode.Markdown, cancellationToken: ct);
            }
            else if (data == "admin_stats")
            {
                await botClient.SendMessage(chatId, $"📊 Статистика:\n👥 Пользователей: {_users.Count}", cancellationToken: ct);
            }
        }

        private async Task HandleManualPromoCreation(ITelegramBotClient botClient, long chatId, string text, CancellationToken ct)
        {
            try
            {
                var parts = text.Split(' ');
                if (parts.Length < 1) return;

                string code = parts[0].ToUpper();
                int gold = parts.Length > 1 && int.TryParse(parts[1], out int g) ? g : 1000;
                int maxUses = parts.Length > 2 && int.TryParse(parts[2], out int m) ? m : 1;

                var currencies = new List<Tuple<int, int>> { new Tuple<int, int>(102, gold) };
                var items = new List<Tuple<int, int>>();

                await BoltGameDatabaseProvider.Instance.CreateCoupon(code, maxUses, items, currencies);
                Console.WriteLine($"[PromoBot] Admin created manual promo: {code} (Gold: {gold}, MaxUses: {maxUses})");

                await botClient.SendMessage(chatId, $"✅ Промокод `{code}` успешно создан!\n💰 Золото: {gold}\n👥 Активаций: {maxUses}", parseMode: ParseMode.Markdown, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, $"❌ Ошибка: {ex.Message}", cancellationToken: ct);
            }
        }

        private async Task<int> DoBroadcast(ITelegramBotClient botClient, long fromChatId, int messageId, CancellationToken ct)
        {
            int count = 0;
            foreach (var user in _users.Values)
            {
                try
                {
                    await botClient.CopyMessage(user.ChatId, fromChatId, messageId, cancellationToken: ct);
                    count++;
                }
                catch { }
            }
            return count;
        }

        private bool IsAdmin(string username, long id)
        {
            return (username != null && _adminUsernames.Any(a => a.Equals(username, StringComparison.OrdinalIgnoreCase))) || _adminIds.Contains(id);
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
        {
            Console.WriteLine($"[PromoBot] Error: {exception}");
            return Task.CompletedTask;
        }
    }
}