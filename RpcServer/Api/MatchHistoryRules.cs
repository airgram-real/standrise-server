using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// Чистые правила истории 0.17: без Mongo/protobuf, чтобы гонять tests\MatchHistory.
    /// </summary>
    public static class MatchHistoryRules
    {
        public const int CompetitiveMaxScore = 10;
        public const int AlliesMaxScore = 8;

        public const int ResultLoss = 0;
        public const int ResultWin = 1;
        public const int ResultDraw = 2;

        public static bool LooksAllies(string mode, string map)
        {
            if (!string.IsNullOrEmpty(mode)
                && mode.IndexOf("Ranked2v2", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (string.IsNullOrEmpty(map)) return false;
            return map.IndexOf("2x2", StringComparison.OrdinalIgnoreCase) >= 0
                || map.IndexOf("2v2", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static int ClampScore(int score, bool allies)
        {
            if (score < 0) return 0;
            int max = allies ? AlliesMaxScore : CompetitiveMaxScore;
            return score > max ? max : score;
        }

        /// <summary>
        /// 1 победа, 0 поражение, 2 ничья (равный ненулевой счёт, нет победителя).
        /// Сдача 0:0 с winner_team — не ничья.
        /// </summary>
        public static int ResultForViewer(int tr, int ct, int viewerTeam, int winnerTeam, int mmrDelta)
        {
            if (mmrDelta > 0) return ResultWin;
            if (mmrDelta < 0) return ResultLoss;
            if (winnerTeam > 0)
                return viewerTeam == winnerTeam ? ResultWin : ResultLoss;
            if (tr == ct && tr > 0) return ResultDraw;
            if (tr > ct) return viewerTeam == 1 ? ResultWin : ResultLoss;
            if (ct > tr) return viewerTeam == 2 ? ResultWin : ResultLoss;
            return ResultLoss;
        }

        /// <summary>Цвет акцента 0.17: жёлтый +, красный −, серый ничья.</summary>
        public static string AccentFromResult(int result, int mmrDelta)
        {
            if (mmrDelta > 0 || result == ResultWin) return "yellow";
            if (result == ResultDraw && mmrDelta == 0) return "gray";
            return "red";
        }

        /// <summary>
        /// MATCH ID — 32 hex без дефисов (Guid формат N). Дефисы → getMatch «invalid»
        /// и префаб Sandstone 9:9. Число 1…∞ кодируем в Guid, не Guid.Empty.
        /// </summary>
        public static string ToWireMatchId(string publicId)
        {
            string n = ToListMatchId(publicId);
            if (IsShortNumeric(n) && long.TryParse(n, out long v) && v > 0)
            {
                if (v > 0xFFFFFFFFFFFFL) v = 0xFFFFFFFFFFFFL;
                return (v.ToString("x8").PadLeft(8, '0')
                    + "000040008000"
                    + v.ToString("x12").PadLeft(12, '0')).ToLowerInvariant();
            }
            if (!string.IsNullOrEmpty(n) && n.Length == 32 && IsHex32(n) && !IsPaddedNumeric(n))
                return n.ToLowerInvariant();
            return UuidFromNumeric(string.IsNullOrEmpty(n) ? "1" : n);
        }

        /// <summary>Публичный номер «1», «2»… для логов и поиска в БД.</summary>
        public static string ToListMatchId(string publicId)
        {
            if (string.IsNullOrWhiteSpace(publicId)) return publicId;
            string id = publicId.Trim().Replace("-", "");
            if (IsShortNumeric(id)) return id;
            if (id.Length == 32 && IsHex32(id) && id.IndexOf("000040008000", StringComparison.Ordinal) == 8)
            {
                string last = TrimLeadingZeros(id.Substring(20));
                if (IsShortNumeric(last)) return last;
            }
            if (id.Length == 32 && IsPaddedNumeric(id)) return TrimLeadingZeros(id);
            if (id.Length == 32 && IsHex32(id))
            {
                string n = NumericFromUuid(id);
                if (!string.IsNullOrEmpty(n)) return n;
            }
            return id;
        }

        public static string FromListMatchId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return id;
            string t = id.Trim().Replace("-", "");
            if (IsShortNumeric(t)) return t;
            if (t.Length != 32 || !IsHex32(t)) return t;
            if (t.IndexOf("000040008000", StringComparison.Ordinal) == 8)
            {
                string last = TrimLeadingZeros(t.Substring(20));
                if (IsShortNumeric(last)) return last;
            }
            if (IsPaddedNumeric(t)) return TrimLeadingZeros(t);
            string numeric = NumericFromUuid(t);
            return numeric ?? t;
        }

        public static string UuidFromNumeric(string n)
        {
            if (string.IsNullOrEmpty(n)) return n;
            using (var md5 = MD5.Create())
            {
                byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes("standrise-match:" + n));
                h[6] = (byte)((h[6] & 0x0f) | 0x40);
                h[8] = (byte)((h[8] & 0x3f) | 0x80);
                var sb = new StringBuilder(32);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                string hex = sb.ToString();
                lock (UuidMap)
                {
                    UuidMap[hex] = n;
                    UuidMap[n] = hex;
                }
                return hex;
            }
        }

        private static readonly Dictionary<string, string> UuidMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string NumericFromUuid(string hex)
        {
            lock (UuidMap)
            {
                if (UuidMap.TryGetValue(hex, out string n)) return n;
            }
            for (int i = 1; i <= 65536; i++)
            {
                string s = i.ToString();
                if (string.Equals(UuidFromNumeric(s), hex, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }

        private static bool IsHex32(string s)
        {
            if (s == null || s.Length != 32) return false;
            foreach (char c in s)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        private static bool IsPaddedNumeric(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (c < '0' || c > '9') return false;
            return true;
        }

        private static string TrimLeadingZeros(string s)
        {
            string t = s.TrimStart('0');
            return t.Length == 0 ? "0" : t;
        }

        public static bool IsShortNumeric(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 18) return false;
            foreach (char c in s)
                if (c < '0' || c > '9') return false;
            return !(s.Length > 1 && s[0] == '0');
        }

        public static string FormatMsk(long unixMs)
        {
            if (unixMs <= 0) return "";
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs)
                .ToOffset(TimeSpan.FromHours(3))
                .ToString("HH:mm");
        }
    }
}
