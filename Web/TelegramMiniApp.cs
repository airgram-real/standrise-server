using System.Security.Cryptography;
using System.Text;

namespace ProjectRework.Web;

/// <summary>Кто открыл Mini App — уже проверенный, а не присланный клиентом «на слово».</summary>
public sealed record MiniAppUser(long Id, string Username, string FirstName, string LastName);

/// <summary>
/// Проверка initData от Telegram Mini App.
///
/// Клиент присылает строку вида "query_id=...&user=...&auth_date=...&hash=...".
/// Алгоритм из документации Telegram: secret = HMAC_SHA256("WebAppData", bot_token),
/// затем сверяем HMAC_SHA256(secret, data_check_string) с полем hash.
///
/// Тонкость, из-за которой проверка падала с «подпись не сходится»: поле signature
/// (Ed25519-подпись Telegram, Bot API 7.x) одни клиенты кладут в строку проверки,
/// другие — нет, и в документации это разночтение так и осталось. Поэтому считаем
/// оба варианта: без signature и с ним. Безопасность от этого не страдает — оба
/// варианта всё равно должны совпасть с HMAC по токену бота, подделать его нельзя.
/// </summary>
public static class TelegramMiniApp
{
    public static bool TryValidate(
        string initData,
        string botToken,
        int maxAgeSeconds,
        out MiniAppUser user,
        out string error)
    {
        user = null;
        error = "";

        if (string.IsNullOrWhiteSpace(initData)) { error = "пустая initData"; return false; }
        if (string.IsNullOrWhiteSpace(botToken)) { error = "на сервере нет токена бота"; return false; }

        // Разбираем как query string, сохраняя сырые значения.
        var pairs = new List<(string Key, string Value)>();
        foreach (var chunk in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = chunk.IndexOf('=');
            if (eq <= 0) continue;
            string k = Uri.UnescapeDataString(chunk.Substring(0, eq));
            string v = Uri.UnescapeDataString(chunk.Substring(eq + 1));
            pairs.Add((k, v));
        }

        string hash = pairs.FirstOrDefault(p => p.Key == "hash").Value;
        if (string.IsNullOrEmpty(hash)) { error = "в initData нет hash"; return false; }

        byte[] secret = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(botToken));

        bool ok = Matches(pairs, secret, hash, skipSignature: true)
               || Matches(pairs, secret, hash, skipSignature: false);

        if (!ok)
        {
            string keys = string.Join(",", pairs.Select(p => p.Key));
            error = $"подпись не сходится (поля: {keys})";
            return false;
        }

        string authDateRaw = pairs.FirstOrDefault(p => p.Key == "auth_date").Value;
        if (long.TryParse(authDateRaw, out long authDate) && maxAgeSeconds > 0)
        {
            long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - authDate;
            if (age > maxAgeSeconds) { error = $"initData просрочена ({age} с назад)"; return false; }
        }

        string userJson = pairs.FirstOrDefault(p => p.Key == "user").Value;
        if (string.IsNullOrEmpty(userJson)) { error = "в initData нет user"; return false; }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(userJson);
            var root = doc.RootElement;
            long id = root.GetProperty("id").GetInt64();
            string username = root.TryGetProperty("username", out var u) ? (u.GetString() ?? "") : "";
            string first = root.TryGetProperty("first_name", out var f) ? (f.GetString() ?? "") : "";
            string last = root.TryGetProperty("last_name", out var l) ? (l.GetString() ?? "") : "";
            user = new MiniAppUser(id, username, first, last);
            return true;
        }
        catch (Exception ex)
        {
            error = "не разобрал user: " + ex.Message;
            return false;
        }
    }

    private static bool Matches(
        List<(string Key, string Value)> pairs, byte[] secret, string hash, bool skipSignature)
    {
        string checkString = string.Join("\n", pairs
            .Where(p => p.Key != "hash" && (!skipSignature || p.Key != "signature"))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}"));

        byte[] mine = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(checkString));
        string mineHex = Convert.ToHexString(mine).ToLowerInvariant();

        // Сравнение в постоянном времени — не даём подбирать hash по времени ответа.
        var a = Encoding.ASCII.GetBytes(mineHex);
        var b = Encoding.ASCII.GetBytes(hash.ToLowerInvariant());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
