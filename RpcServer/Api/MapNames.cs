using System;
using System.Collections.Generic;
using System.Linq;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// Имена карт для истории матчей. Вынесено отдельно и намеренно без зависимостей
    /// (ни Mongo, ни protobuf, ни настроек), чтобы это можно было прогнать тестами:
    /// tests\MapNames подключает ровно этот файл.
    ///
    /// Сервер знает пять карт (MatchmakingManager): sandstone, province, rust, sakura, zone9.
    /// Клиент 0.17 показывает их как "Sandstone", "Province", "Rust", "Sakura", "Zone 9",
    /// а для союзников — те же имена с суффиксом " 2x2".
    /// </summary>
    public static class MapNames
    {
        public const string AlliesSuffix = " 2x2";

        /// <summary>Куда уезжает матч, если карту не удалось определить вообще ничем.</summary>
        public const string FallbackKey = "province";

        private static readonly Dictionary<string, string> DisplayByKey =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "sandstone", "Sandstone" },
                { "breeze",    "Sandstone" }, // старое имя той же карты
                { "province",  "Province"  },
                { "rust",      "Rust"      },
                { "sakura",    "Sakura"    },
                { "zone9",     "Zone 9"    },
            };

        /// <summary>
        /// Нормализованное имя режима для клиента 0.17: "Ranked2v2" (союзники) либо
        /// "RankedDefuse" (соревновательный). Пустое — считаем союзниками.
        /// </summary>
        public static string NormalizeMode(string mode)
        {
            string m = (mode ?? "").Trim();
            if (m.IndexOf("Ranked2v2", StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf("allies", StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf("2v2", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Ranked2v2";
            if (m.IndexOf("Ranked", StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf("Defuse", StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf("competitive", StringComparison.OrdinalIgnoreCase) >= 0)
                return "RankedDefuse";
            return string.IsNullOrEmpty(m) ? "Ranked2v2" : m;
        }

        /// <summary>Режим союзников — там у карт суффикс " 2x2".</summary>
        public static bool IsAlliesMode(string mode) => NormalizeMode(mode) == "Ranked2v2";

        /// <summary>Имя карты по названию режима.</summary>
        public static string ForModeName(string raw, string mode) => ForMode(raw, IsAlliesMode(mode));

        /// <summary>
        /// Ключ карты: только буквы и цифры в нижнем регистре, хвост "2x2" отброшен.
        /// "Zone 9", "zone9", "Zone 9 2x2", "Sakura_2x2", "RUST-2X2" → "zone9" / "sakura" / "rust".
        /// </summary>
        public static string Key(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string key = new string(raw.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            while (key.EndsWith("2x2", StringComparison.Ordinal))
                key = key.Substring(0, key.Length - 3);
            return key;
        }

        /// <summary>Известна ли карта серверу.</summary>
        public static bool IsKnown(string raw) => DisplayByKey.ContainsKey(Key(raw));

        /// <summary>
        /// Похоже на голый hex-идентификатор (matchId), а не на имя карты.
        /// </summary>
        public static bool LooksLikeHexId(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 16 || s.Length > 64) return false;
            foreach (char c in s)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>
        /// Имя карты для строки истории. allies=true — режим союзников, там у клиента
        /// отдельные подписи с " 2x2".
        ///
        /// Операция идемпотентна: результат можно прогнать через неё повторно, суффикс
        /// не удвоится. Это важно, потому что RebuildFromLegacy нормализует уже
        /// сохранённые имена ещё раз при каждой выдаче истории.
        /// </summary>
        public static string ForMode(string raw, bool allies)
        {
            string key = Key(raw);

            // Пусто, id комнаты (Ranked2v2_<guid>) или голый hex — карту не узнать.
            if (key.Length == 0 || key.StartsWith("ranked", StringComparison.Ordinal) || LooksLikeHexId(key))
                key = FallbackKey;

            if (!DisplayByKey.TryGetValue(key, out string display))
                display = char.ToUpperInvariant(key[0]) + (key.Length > 1 ? key.Substring(1) : "");

            return allies ? display + AlliesSuffix : display;
        }
    }
}
