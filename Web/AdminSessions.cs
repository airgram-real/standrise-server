using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ProjectRework.Web;

/// <summary>
/// Вход в админку сайта без Telegram Mini App.
///
/// Зачем: проверка подписи Telegram работает только когда магазин открыт кнопкой
/// в боте. Если он открыт обычной ссылкой, сервер не знает, кто пришёл — и пускать
/// в админку по введённому вручную игровому ID нельзя: тогда админом станет любой,
/// кто наберёт чужой номер.
///
/// Поэтому личность подтверждает сам бот: админ жмёт кнопку в боте, бот просит у
/// сайта одноразовую ссылку и присылает её лично в чат. Переход по ссылке меняет
/// одноразовый билет на сессию. Билет живёт 15 минут, сессия — 12 часов, оба
/// только в памяти сайта: перезапуск их сбрасывает, и это правильно.
/// </summary>
public static class AdminSessions
{
    private sealed record Ticket(long TelegramId, DateTime ExpiresUtc);
    private sealed record Session(long TelegramId, DateTime ExpiresUtc);

    private static readonly ConcurrentDictionary<string, Ticket> Tickets = new();
    private static readonly ConcurrentDictionary<string, Session> Live = new();

    private static string NewToken()
    {
        Span<byte> buf = stackalloc byte[32];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToHexString(buf).ToLowerInvariant();
    }

    /// <summary>Выдаёт одноразовый билет для админа с таким telegram id.</summary>
    public static string IssueTicket(long telegramId)
    {
        Cleanup();
        string t = NewToken();
        Tickets[t] = new Ticket(telegramId, DateTime.UtcNow.AddMinutes(15));
        return t;
    }

    /// <summary>Меняет билет на сессию. Билет одноразовый — второй раз не сработает.</summary>
    public static bool Redeem(string ticket, out string sessionToken, out long telegramId)
    {
        sessionToken = "";
        telegramId = 0;
        Cleanup();
        if (string.IsNullOrWhiteSpace(ticket)) return false;
        if (!Tickets.TryRemove(ticket.Trim(), out var t)) return false;
        if (t.ExpiresUtc < DateTime.UtcNow) return false;

        telegramId = t.TelegramId;
        sessionToken = NewToken();
        Live[sessionToken] = new Session(telegramId, DateTime.UtcNow.AddHours(12));
        return true;
    }

    /// <summary>Кто пришёл с этой сессией. 0 — сессии нет или истекла.</summary>
    public static long Resolve(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken)) return 0;
        if (!Live.TryGetValue(sessionToken.Trim(), out var s)) return 0;
        if (s.ExpiresUtc < DateTime.UtcNow) { Live.TryRemove(sessionToken.Trim(), out _); return 0; }
        return s.TelegramId;
    }

    private static void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in Tickets) if (kv.Value.ExpiresUtc < now) Tickets.TryRemove(kv.Key, out _);
        foreach (var kv in Live) if (kv.Value.ExpiresUtc < now) Live.TryRemove(kv.Key, out _);
    }
}
