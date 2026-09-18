using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using StandRiseServer.RpcServer.Api;

namespace StandRiseServer.RpcServer
{
    // Мост для сайта/админ-панели. Ничего не реализует заново — только открывает
    // доступ к тем же приватным методам, которыми пользуются инлайн-кнопки бота.
    // Отдельный partial-файл, чтобы не трогать DonateBotService.cs.
    public partial class DonateBotService
    {
        internal static bool BridgeResolvePlayerId(string input, out string playerId, out string error)
            => TryResolvePlayerId(input, out playerId, out error);

        internal static void BridgeGrantItem(string playerId, int itemDefinitionId, int quantity = 1)
            => GrantItem(playerId, itemDefinitionId, quantity);

        internal static void BridgeGrantGold(string playerId, long amount, long adminTgId = 0)
            => GrantGold(playerId, amount, adminTgId);

        internal static bool BridgeAddBattlePassLevels(string playerId, int amount)
            => AddBattlePassLevels(playerId, amount);

        internal static bool BridgeSetCustomId(string playerId, string newId, out string reason)
            => TrySetCustomId(playerId, newId, out reason);

        internal static int BridgeGoldPassItemDefinitionId => GoldPassItemDefinitionId;
        internal static int BridgeSpinTokenItemDefinitionId => SpinTokenItemDefinitionId;

        // ---------------------------------------------------------------- вход в админку сайта

        private const string SiteLocalUrl = "http://127.0.0.1:8080";
        private static readonly HttpClient SiteHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>
        /// Личная одноразовая ссылка в админку сайта.
        ///
        /// Сайт, открытый обычной ссылкой, не знает, кто пришёл: подпись Telegram
        /// бывает только у Mini App. Пускать в админку по введённому вручную ID
        /// нельзя — админом стал бы любой, кто наберёт чужой номер. Поэтому личность
        /// подтверждает бот: он уже знает telegram id того, кто нажал кнопку, и
        /// просит у сайта билет именно для него.
        /// </summary>
        private async Task SendSiteAdminLink(
            ITelegramBotClient botClient, long chatId, long userId, CancellationToken ct)
        {
            try
            {
                string key = AdminApi.SharedKey();
                if (string.IsNullOrEmpty(key))
                {
                    await botClient.SendMessage(chatId,
                        "Не собрался ключ к сайту: в local.settings.json нет TelegramBotToken.",
                        cancellationToken: ct);
                    return;
                }

                string body = JsonSerializer.Serialize(new { key, telegramId = userId.ToString() });
                using var resp = await SiteHttp.PostAsync(
                    SiteLocalUrl + "/api/bot/admin-link",
                    new StringContent(body, Encoding.UTF8, "application/json"),
                    ct);
                string text = await resp.Content.ReadAsStringAsync(ct);

                if (!resp.IsSuccessStatusCode)
                {
                    await botClient.SendMessage(chatId,
                        $"Сайт не выдал ссылку ({(int)resp.StatusCode}): {text}",
                        cancellationToken: ct);
                    return;
                }

                using var doc = JsonDocument.Parse(text);
                string url = doc.RootElement.TryGetProperty("url", out var u) ? (u.GetString() ?? "") : "";
                if (string.IsNullOrEmpty(url))
                {
                    await botClient.SendMessage(chatId, "Сайт ответил без ссылки: " + text, cancellationToken: ct);
                    return;
                }

                await botClient.SendMessage(chatId,
                    "🌐 Вход в админку сайта — ссылка одноразовая, живёт 15 минут:\n" + url +
                    "\n\nПосле перехода админка держится 12 часов. Ссылку никому не пересылай.",
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, "Сайт недоступен: " + ex.Message, cancellationToken: ct);
            }
        }
    }
}
