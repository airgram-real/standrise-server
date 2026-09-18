using System.Text.Json;

namespace ProjectRework.Web;

/// <summary>
/// Связки «telegram id → playerId». Файл общий с ботом
/// (donate_bot_users_&lt;первые 8 символов токена&gt;.json), формат тот же:
/// {"8014265847":"6a9c0a273f1c44f40f4cd472"}.
///
/// Сайт из него читает. Писать сюда сайт НЕ будет: бот держит связки в памяти
/// и перезаписывает файл целиком, так что чужая запись потерялась бы при
/// следующем сохранении бота. Привязка аккаунта остаётся за ботом — сайт, если
/// связки нет, просто отправляет игрока нажать /start.
/// </summary>
public static class LinkStore
{
    private static readonly object Gate = new();
    private static string _path = "";
    private static Dictionary<long, string> _cache = new();
    private static DateTime _mtimeUtc = DateTime.MinValue;
    private static DateTime _checkedAtUtc = DateTime.MinValue;

    public static void Configure(string path) => _path = path;

    public static string GetPlayerId(long telegramId)
    {
        Refresh();
        lock (Gate)
            return _cache.TryGetValue(telegramId, out string pid) ? pid : "";
    }

    public static int Count
    {
        get { Refresh(); lock (Gate) return _cache.Count; }
    }

    /// <summary>Записать связку (после verify в Mini App). Файл тот же, что у бота.</summary>
    public static void Upsert(long telegramId, string playerId)
    {
        if (telegramId <= 0 || string.IsNullOrWhiteSpace(playerId)) return;
        lock (Gate)
        {
            Refresh(force: true);
            _cache[telegramId] = playerId.Trim();
            try
            {
                if (string.IsNullOrEmpty(_path))
                    return;
                string json = JsonSerializer.Serialize(_cache);
                File.WriteAllText(_path, json);
                _mtimeUtc = File.GetLastWriteTimeUtc(_path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Web] связка не записана: {ex.Message}");
            }
        }
    }

    /// <summary>Перечитывает файл, если он поменялся. Проверка не чаще раза в 2 секунды.</summary>
    private static void Refresh(bool force = false)
    {
        lock (Gate)
        {
            if (!force && (DateTime.UtcNow - _checkedAtUtc).TotalSeconds < 2) return;
            _checkedAtUtc = DateTime.UtcNow;
            try
            {
                if (!File.Exists(_path)) return;
                var mtime = File.GetLastWriteTimeUtc(_path);
                if (mtime == _mtimeUtc) return;
                var dict = JsonSerializer.Deserialize<Dictionary<long, string>>(File.ReadAllText(_path));
                if (dict != null)
                {
                    _cache = dict;
                    _mtimeUtc = mtime;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Web] связки не читаются: {ex.Message}");
            }
        }
    }
}

/// <summary>Список админов — тот же bot_roles.json, что читает бот.</summary>
public static class AdminStore
{
    private static readonly object Gate = new();
    private static string _path = "";
    private static HashSet<long> _admins = new();
    private static DateTime _mtimeUtc = DateTime.MinValue;
    private static DateTime _checkedAtUtc = DateTime.MinValue;

    public static void Configure(string path) => _path = path;

    public static bool IsAdmin(long telegramId)
    {
        Refresh();
        lock (Gate) return _admins.Contains(telegramId);
    }

    private static void Refresh()
    {
        lock (Gate)
        {
            if ((DateTime.UtcNow - _checkedAtUtc).TotalSeconds < 5) return;
            _checkedAtUtc = DateTime.UtcNow;
            try
            {
                if (!File.Exists(_path)) return;
                var mtime = File.GetLastWriteTimeUtc(_path);
                if (mtime == _mtimeUtc) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(_path));
                var set = new HashSet<long>();
                if (doc.RootElement.TryGetProperty("AdminIds", out var ids) &&
                    ids.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in ids.EnumerateArray())
                        if (el.TryGetInt64(out long id)) set.Add(id);
                }
                _admins = set;
                _mtimeUtc = mtime;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Web] роли не читаются: {ex.Message}");
            }
        }
    }
}
