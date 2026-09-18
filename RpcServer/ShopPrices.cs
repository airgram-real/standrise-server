using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StandRiseServer.RpcServer
{
    /// <summary>
    /// Единый прайс бота и сайта. Источник один — C:\StandRise\Web\catalog.json,
    /// тот же файл, по которому строится витрина в Telegram Mini App.
    ///
    /// Раньше цены были вбиты константами прямо в боте (Gold Pass за 1 звезду,
    /// 5000 голды за 5), а сайт знал свои. Две цены на один товар — это дыра:
    /// покупали бы там, где дешевле. Теперь правится один файл, перезапуск не нужен.
    /// </summary>
    public static class ShopPrices
    {
        public sealed class Pack
        {
            public string Id { get; set; } = "";
            public string Kind { get; set; } = "";
            public int Amount { get; set; }
            public string Title { get; set; } = "";
            public int Stars { get; set; }
        }

        private sealed class CatalogFile
        {
            public List<Pack> Gold { get; set; } = new();
            public List<Pack> Pass { get; set; } = new();
        }

        public static string CatalogPath = @"C:\StandRise\Web\catalog.json";

        private static readonly object Gate = new object();
        private static CatalogFile _cache;
        private static DateTime _mtimeUtc = DateTime.MinValue;
        private static DateTime _checkedAtUtc = DateTime.MinValue;

        private static CatalogFile Current()
        {
            lock (Gate)
            {
                if (_cache != null && (DateTime.UtcNow - _checkedAtUtc).TotalSeconds < 5)
                    return _cache;
                _checkedAtUtc = DateTime.UtcNow;
                try
                {
                    if (File.Exists(CatalogPath))
                    {
                        var mtime = File.GetLastWriteTimeUtc(CatalogPath);
                        if (_cache == null || mtime != _mtimeUtc)
                        {
                            var loaded = JsonSerializer.Deserialize<CatalogFile>(
                                File.ReadAllText(CatalogPath),
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (loaded != null)
                            {
                                _cache = loaded;
                                _mtimeUtc = mtime;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShopPrices] {CatalogPath} не читается: {ex.Message}");
                }
                return _cache ?? Fallback();
            }
        }

        /// <summary>Если файла нет — старые цены, чтобы бот не остался вообще без витрины.</summary>
        private static CatalogFile Fallback() => new CatalogFile
        {
            Gold = new List<Pack>
            {
                new Pack { Id = "g1000",  Kind = "gold", Amount = 1000,  Title = "1 000 GOLD",  Stars = 10 },
                new Pack { Id = "g5000",  Kind = "gold", Amount = 5000,  Title = "5 000 GOLD",  Stars = 45 },
                new Pack { Id = "g10000", Kind = "gold", Amount = 10000, Title = "10 000 GOLD", Stars = 75 },
                new Pack { Id = "g35000", Kind = "gold", Amount = 35000, Title = "35 000 GOLD", Stars = 200 },
                new Pack { Id = "g50000", Kind = "gold", Amount = 50000, Title = "50 000 GOLD", Stars = 400 },
            },
            Pass = new List<Pack>
            {
                new Pack { Id = "bp",    Kind = "goldpass", Amount = 0,  Title = "GOLD PASS",   Stars = 50 },
                new Pack { Id = "lvl1",  Kind = "levels",   Amount = 1,  Title = "+1 УРОВЕНЬ",  Stars = 5 },
                new Pack { Id = "lvl10", Kind = "levels",   Amount = 10, Title = "+10 УРОВНЕЙ", Stars = 25 },
                new Pack { Id = "lvl45", Kind = "levels",   Amount = 45, Title = "+45 УРОВНЕЙ", Stars = 75 },
            },
        };

        /// <summary>Пакеты голды: (сколько голды, сколько звёзд), по возрастанию.</summary>
        public static (int Gold, int Stars)[] GoldPacks() =>
            Current().Gold
                .Where(p => p != null && p.Amount > 0 && p.Stars > 0)
                .OrderBy(p => p.Amount)
                .Select(p => (p.Amount, p.Stars))
                .ToArray();

        /// <summary>Пакеты уровней боевого пропуска: (сколько уровней, сколько звёзд).</summary>
        public static (int Levels, int Stars)[] LevelPacks() =>
            Current().Pass
                .Where(p => p != null && p.Kind == "levels" && p.Amount > 0 && p.Stars > 0)
                .OrderBy(p => p.Amount)
                .Select(p => (p.Amount, p.Stars))
                .ToArray();

        /// <summary>Цена Gold Pass в звёздах.</summary>
        public static int GoldPassStars()
        {
            var p = Current().Pass.FirstOrDefault(x => x != null && x.Kind == "goldpass");
            return p != null && p.Stars > 0 ? p.Stars : 50;
        }

        /// <summary>Есть ли такой пакет голды по такой цене — защита от подделанного callback.</summary>
        public static bool IsAllowedGoldPack(int gold, int stars) =>
            GoldPacks().Any(p => p.Gold == gold && p.Stars == stars);

        /// <summary>Цена пакета уровней. 0 — такого пакета в витрине нет.</summary>
        public static int LevelPackStars(int levels)
        {
            foreach (var p in LevelPacks())
                if (p.Levels == levels) return p.Stars;
            return 0;
        }
    }
}
