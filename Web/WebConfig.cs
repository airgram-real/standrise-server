using System.Text.Json;

namespace ProjectRework.Web;

/// <summary>
/// Настройки сайта. Токен бота намеренно НЕ дублируется здесь: он читается из
/// боевого C:\StandRise\bin\Release\net7.0\local.settings.json, чтобы не было
/// двух копий секрета, которые однажды разъедутся.
/// </summary>
public sealed class WebConfig
{
    /// <summary>Порт, который слушает сайт. Наружу его отдаёт обратный прокси с TLS.</summary>
    public int Port { get; set; } = 8080;

    /// <summary>Слушать только локально. Наружу — через прокси, иначе сайт уйдёт в интернет без https.</summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>Публичный адрес сайта — для ссылок и проверки Origin.</summary>
    public string PublicUrl { get; set; } = "https://projectre.work";

    /// <summary>Откуда брать токен бота и прочие боевые настройки.</summary>
    public string ServerSettingsPath { get; set; } =
        @"C:\StandRise\bin\Release\net7.0\local.settings.json";

    /// <summary>Файл связок Telegram → playerId. Тот же, что у бота.</summary>
    public string LinkedUsersPath { get; set; } =
        @"C:\StandRise\donate_bot_users_89099930.json";

    /// <summary>
    /// Локальный API игрового процесса (HttpApiServer). Через него идут поиск игрока,
    /// выдача и тумблеры — тем же кодом, что и в боте. Ключ доступа — SHA-256 токена бота.
    /// </summary>
    public string GameApiUrl { get; set; } = "http://127.0.0.1:2224";

    /// <summary>Роли бота: отсюда берётся список админов для админки сайта.</summary>
    public string BotRolesPath { get; set; } = @"C:\StandRise\bot_roles.json";

    /// <summary>Готовая сборка фронтенда (папка dist от vite build).</summary>
    public string SiteRoot { get; set; } = @"C:\StandRise\Web\wwwroot";

    /// <summary>
    /// Сколько секунд считать initData Telegram свежей. Телеграм рекомендует
    /// не доверять старым данным — это защита от переигрывания чужой подписи.
    /// </summary>
    public int InitDataMaxAgeSeconds { get; set; } = 86400;

    public static WebConfig Load(string path)
    {
        var cfg = new WebConfig();
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<WebConfig>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (loaded != null) cfg = loaded;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Web] не смог прочитать {path}: {ex.Message}. Беру значения по умолчанию.");
        }
        return cfg;
    }

    /// <summary>Токен бота из боевых настроек сервера.</summary>
    public string ReadBotToken()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ServerSettingsPath));
            if (doc.RootElement.TryGetProperty("TelegramBotToken", out var t))
                return t.GetString() ?? "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Web] не смог прочитать токен бота: {ex.Message}");
        }
        return "";
    }
}
