using System;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// MMR Союзников по правилам вики Standoff:
    /// калибровка 10 матчей; ранг от MMR; дельта = результат матча + разница раундов + личный вклад.
    /// Клиент 0.17: rank только 0..16 (17 крашит).
    /// </summary>
    public static class AlliesMmrSystem
    {
        // Вики Союзники (новая шкала): Master 1700, Elite 1800, Legend 2100.
        // internal: …12 Phoenix 1400, 13 Champion 1600, 14 Master 1700, 15 Elite 1800, 16 Legend 2100.
        // (Ranger 1500 входит в полосу Phoenix 1400–1599 — иначе не хватает слота под Elite.)
        public static readonly int[] RankThresholds =
        {
            250, 300, 400, 500,   // Bronze I-IV
            600, 700, 800, 900,   // Silver I-IV
            1000, 1100, 1200, 1300, // Gold I-IV
            1400,                 // Phoenix (1400–1599)
            1600,                 // Champion
            1700,                 // Master (1700–1799)
            1800,                 // Elite (1800–2099)
            2100                  // Legend
        };

        public const int EliteMmrFloor = 1800;

        public const int CalibrationMatches = 10;
        public const float DefaultMmr = 1000f;
        public const float MinMmr = 250f;
        public const float MaxMmr = 5000f;

        public static int RankFromMmr(float mmr)
        {
            if (mmr < RankThresholds[0]) return 0;
            int rank = 0;
            for (int i = 0; i < RankThresholds.Length; i++)
            {
                if (mmr >= RankThresholds[i]) rank = i;
                else break;
            }
            return Math.Clamp(rank, 0, RankThresholds.Length - 1);
        }

        public static float MidMmrForRank(int rank)
        {
            rank = Math.Clamp(rank, 0, 16);
            if (rank >= RankThresholds.Length - 1)
                return RankThresholds[^1] + 200f; // Legend mid
            return (RankThresholds[rank] + RankThresholds[rank + 1]) / 2f;
        }

        /// <summary>Минимальный MMR для отображения ранга в клиенте (иначе Legend→Elite).</summary>
        public static float MinMmrForRank(int rank)
        {
            if (rank < 0) return DefaultMmr;
            rank = Math.Clamp(rank, 0, RankThresholds.Length - 1);
            return RankThresholds[rank];
        }

        /// <summary>Верхняя граница полоски MMR: следующий ранг (2141|2100 → Legend, target 2500).</summary>
        public static float TargetMmrForRankBar(int internalRank)
        {
            if (internalRank < 0) return RankThresholds[0];
            internalRank = Math.Clamp(internalRank, 0, RankThresholds.Length - 1);
            if (internalRank >= RankThresholds.Length - 1)
                return RankThresholds[^1] + 400f;
            return RankThresholds[internalRank + 1];
        }

        public static string RankName(int rank) => rank switch
        {
            -1 => "Калибровка",
            0 => "Бронза I",
            1 => "Бронза II",
            2 => "Бронза III",
            3 => "Бронза IV",
            4 => "Серебро I",
            5 => "Серебро II",
            6 => "Серебро III",
            7 => "Серебро IV",
            8 => "Золото I",
            9 => "Золото II",
            10 => "Золото III",
            11 => "Золото IV",
            12 => "Феникс",
            13 => "Чемпион",
            14 => "Мастер",
            15 => "Элита",
            16 => "Легенда",
            _ => $"Ранг {rank}"
        };

        /// <summary>Название по MMR (Master 1700–1799, Elite 1800+).</summary>
        public static string TierNameFromMmr(float mmr)
        {
            return RankName(RankFromMmr(mmr));
        }

        /// <summary>t=0 у Bronze, t=1 у Legend — для сжатия наград на высоких рангах.</summary>
        private static float RankScale01(float mmr)
        {
            float t = (mmr - RankThresholds[0]) / (RankThresholds[^1] - RankThresholds[0]);
            return Math.Clamp(t, 0f, 1f);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>
        /// Полный расчёт дельты MMR по 3 критериям вики.
        /// ourRounds/enemyRounds: счёт команд (для Allies first-to-8).
        /// </summary>
        public static float ComputeDelta(
            float currentMmr,
            bool won,
            int ourRounds,
            int enemyRounds,
            int kills,
            int deaths,
            int assists,
            int score,
            float lobbyAvgScore,
            bool abandoned = false)
        {
            if (abandoned)
            {
                // Вики: Bronze 1-4 → -30, остальные → -60.
                int r = RankFromMmr(currentMmr);
                return r <= 3 ? -30f : -60f;
            }

            float t = RankScale01(currentMmr);
            // Вики (после правок): Bronze ~45/35 + round/personal caps ~18; Legend ~15 + caps ~8.
            float matchWin = Lerp(45f, 15f, t);
            float matchLoss = Lerp(35f, 15f, t);
            float roundCap = Lerp(18f, 8f, t);
            float personalCap = Lerp(18f, 8f, t);

            float matchPart = won ? matchWin : -matchLoss;

            int roundDiff = ourRounds - enemyRounds;
            // ~2–2.5 MMR за раунд разницы, с капом.
            float perRound = roundCap / 8f;
            float roundPart = Math.Clamp(roundDiff * perRound, -roundCap, roundCap);

            float avg = lobbyAvgScore > 1f ? lobbyAvgScore : 1f;
            float personalRaw = (score - avg) / avg * personalCap;
            // KD как доп. вклад (слабее score).
            float kd = deaths <= 0 ? kills + assists * 0.5f : (kills + assists * 0.5f) / deaths;
            float kdPart = Math.Clamp((kd - 1f) * (personalCap * 0.35f), -personalCap * 0.5f, personalCap * 0.5f);
            float personalPart = Math.Clamp(personalRaw * 0.65f + kdPart, -personalCap, personalCap);

            float delta = matchPart + roundPart + personalPart;
            // Калибровка: больший размах для быстрой расстановки.
            return delta;
        }

        public static float ApplyDelta(float currentMmr, float delta, bool inCalibration)
        {
            if (currentMmr <= 0f) currentMmr = DefaultMmr;
            if (inCalibration)
                delta *= 1.6f; // вики: калибровка сильнее влияет на итоговый ранг
            float next = currentMmr + delta;
            return Math.Clamp(next, MinMmr, MaxMmr);
        }

        public static (int our, int enemy) RoundsForTeam(int team, int trScore, int ctScore, int winnerTeam)
        {
            int tr = Math.Max(0, trScore);
            int ct = Math.Max(0, ctScore);
            if (tr == 0 && ct == 0)
                return winnerTeam == team ? (8, 6) : (6, 8);
            // Комната: TrScore = T (team 1), CtScore = CT (team 2). Без инверсии.
            if (team == 2) return (ct, tr);
            return (tr, ct);
        }
    }
}
