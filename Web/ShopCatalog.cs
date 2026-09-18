using System.Text.Json;

namespace ProjectRework.Web;

/// <summary>Товар витрины. Цена — в звёздах Telegram (валюта XTR).</summary>
public sealed class Product
{
    public string Id { get; set; } = "";
    /// <summary>gold | goldpass | levels — совпадает с первым полем payload бота.</summary>
    public string Kind { get; set; } = "";
    /// <summary>Сколько голды или уровней. Для goldpass не используется.</summary>
    public int Amount { get; set; }
    public string Title { get; set; } = "";
    public int Stars { get; set; }
    public string Cover { get; set; } = "";
    /// <summary>Пометка «выгодно» в витрине, ни на что не влияет.</summary>
    public int Discount { get; set; }

    /// <summary>
    /// payload инвойса — ровно тот формат, который уже разбирает
    /// DonateBotService.FulfilPayload. Второго кода выдачи не появляется.
    /// </summary>
    public string BuildPayload(string playerId) => Kind switch
    {
        "gold" => $"gold|{Amount}|{playerId}",
        "goldpass" => $"goldpass|{playerId}",
        // Пропуск сразу с уровнями — один платёж, поэтому и payload один.
        "passplus" => $"passplus|{Amount}|{playerId}",
        "levels" => $"levels|{Amount}|{playerId}",
        "spins" => $"spins|{Amount}|{playerId}",
        _ => throw new InvalidOperationException($"неизвестный вид товара: {Kind}")
    };

    public string BuildDescription(string playerId) => Kind switch
    {
        "gold" => $"Покупка {Amount} голды на аккаунт {playerId}",
        "goldpass" => $"Покупка Gold Pass на аккаунт {playerId}",
        "passplus" => $"Покупка Gold Pass и {Amount} уровней на аккаунт {playerId}",
        "levels" => $"Покупка {Amount} уровней боевого пропуска на аккаунт {playerId}",
        "spins" => $"Покупка {Amount} спинов на аккаунт {playerId}",
        _ => $"Покупка на аккаунт {playerId}"
    };
}

/// <summary>
/// Витрина. Лежит в отдельном json рядом с сайтом, чтобы цены и состав
/// правились без пересборки — перезапуска сайта тоже не требует.
/// </summary>
public sealed class ShopCatalog
{
    public List<Product> Gold { get; set; } = new();
    public List<Product> Pass { get; set; } = new();

    public IEnumerable<Product> All() => Gold.Concat(Pass);

    public Product Find(string id) =>
        All().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private static readonly object Gate = new();
    private static ShopCatalog _cached;
    private static DateTime _cachedAtUtc = DateTime.MinValue;
    private static string _path = "";

    public static void Configure(string path) => _path = path;

    /// <summary>Перечитывает файл не чаще раза в 5 секунд.</summary>
    public static ShopCatalog Current()
    {
        lock (Gate)
        {
            if (_cached != null && (DateTime.UtcNow - _cachedAtUtc).TotalSeconds < 5)
                return _cached;
            _cached = LoadOrDefault();
            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    private static ShopCatalog LoadOrDefault()
    {
        try
        {
            if (File.Exists(_path))
            {
                var c = JsonSerializer.Deserialize<ShopCatalog>(
                    File.ReadAllText(_path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (c != null && c.All().Any()) return c;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Web] витрина {_path} не читается: {ex.Message}. Беру встроенную.");
        }
        return Default();
    }

    /// <summary>Цены заданы владельцем сервера, в звёздах.</summary>
    public static ShopCatalog Default() => new()
    {
        Gold = new List<Product>
        {
            new() { Id = "g1000",  Kind = "gold", Amount = 1000,  Title = "1 000 GOLD",  Stars = 10,  Cover = "/assets/gold/bigGold1000.webp" },
            new() { Id = "g5000",  Kind = "gold", Amount = 5000,  Title = "5 000 GOLD",  Stars = 45,  Cover = "/assets/gold/bigGold5000.webp",  Discount = 10 },
            new() { Id = "g10000", Kind = "gold", Amount = 10000, Title = "10 000 GOLD", Stars = 75,  Cover = "/assets/gold/bigGold10000.webp", Discount = 25 },
            new() { Id = "g35000", Kind = "gold", Amount = 35000, Title = "35 000 GOLD", Stars = 200, Cover = "/assets/gold/bigGold30000.webp", Discount = 43 },
            new() { Id = "g50000", Kind = "gold", Amount = 50000, Title = "50 000 GOLD", Stars = 400, Cover = "/assets/gold/bigGold30000.webp", Discount = 20 },
        },
        Pass = new List<Product>
        {
            new() { Id = "bp",    Kind = "goldpass", Amount = 0,  Title = "GOLD PASS",    Stars = 50, Cover = "/assets/bp/bigBP.webp" },
            new() { Id = "lvl1",  Kind = "levels",   Amount = 1,  Title = "+1 УРОВЕНЬ",   Stars = 5,  Cover = "/assets/bp/cartLevel1.webp" },
            new() { Id = "lvl10", Kind = "levels",   Amount = 10, Title = "+10 УРОВНЕЙ",  Stars = 25, Cover = "/assets/bp/cartLevel10.webp" },
            new() { Id = "lvl45", Kind = "levels",   Amount = 45, Title = "+45 УРОВНЕЙ",  Stars = 75, Cover = "/assets/bp/cartLevel45.webp" },
        },
    };
}
