using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.RpcServer.Model;

namespace StandRiseServer.RpcServer.Api
{
    public class RankedManager
    {
        private static RankedManager _instance;
        public static RankedManager Instance => _instance ??= new RankedManager();

        private const int CalibrationMatchesRequired = 10;

        public int GetRankIdByMmr(int mmr, string mode)
        {
            return AlliesMmrSystem.RankFromMmr(mmr);
        }

        public (int deltaMmr, int newMmr) CalculateMmrUpdate(int currentMmr, bool isWin, int teamAvgMmr, int enemyAvgMmr, int rankId)
        {
            // Simple Elo-like calculation with a K-factor
            // K-factor can be higher for calibration matches
            // For now, using a simpler logic: base 25 points +/- depending on team difference
            
            int kFactor = 25;
            
            // Adjust kFactor based on rank (lower for higher ranks)
            if (rankId > 15) kFactor = 15;
            
            double expectedScore = 1.0 / (1.0 + Math.Pow(10, (enemyAvgMmr - teamAvgMmr) / 400.0));
            double actualScore = isWin ? 1.0 : 0.0;
            
            int delta = (int)Math.Round(kFactor * (actualScore - expectedScore));
            
            // Ensure a minimum change
            if (isWin && delta < 10) delta = 15;
            if (!isWin && delta > -10) delta = -15;

            int newMmr = Math.Max(0, currentMmr + delta);
            return (delta, newMmr);
        }

        public async System.Threading.Tasks.Task UpdatePlayerRankStats(string playerId, string mode, bool isWin, int teamAvgMmr, int enemyAvgMmr)
        {
            var db = BoltGameDatabaseProvider.Instance;
            var statsDoc = db.GetOrCreatePlayerStatsDocument(playerId);
            if (statsDoc == null || statsDoc.stats == null) return;

            string mmrKey = mode == "Allies" ? "ranked_2v2_current_mmr" : "ranked_current_mmr";
            string rankKey = mode == "Allies" ? "ranked_2v2_rank" : "ranked_rank";
            string bestRankKey = mode == "Allies" ? "ranked_2v2_best_rank" : "ranked_best_rank";
            string playedKey = mode == "Allies" ? "ranked_2v2_played_matches" : "ranked_played_matches";
            string wonKey = mode == "Allies" ? "ranked_2v2_won_match_count" : "ranked_won_match_count";
            string calibrationKey = mode == "Allies" ? "ranked_2v2_won_match_count" : "ranked_calibration_match_count"; // Flipped naming in DB for 2v2

            int currentMmr = statsDoc.stats.Contains(mmrKey) ? statsDoc.stats[mmrKey].ToInt32() : 1000;
            int playedMatches = statsDoc.stats.Contains(playedKey) ? statsDoc.stats[playedKey].ToInt32() : 0;
            int wonMatches = statsDoc.stats.Contains(wonKey) ? statsDoc.stats[wonKey].ToInt32() : 0;
            int rankId = statsDoc.stats.Contains(rankKey) ? statsDoc.stats[rankKey].ToInt32() : 0;

            var (delta, newMmr) = CalculateMmrUpdate(currentMmr, isWin, teamAvgMmr, enemyAvgMmr, rankId);
            int newRankId = GetRankIdByMmr(newMmr, mode);

            // Update stats document
            statsDoc.stats[mmrKey] = newMmr;
            statsDoc.stats[rankKey] = newRankId;
            statsDoc.stats[playedKey] = playedMatches + 1;
            if (isWin) statsDoc.stats[wonKey] = wonMatches + 1;

            // Handle best rank
            int bestRank = statsDoc.stats.Contains(bestRankKey) ? statsDoc.stats[bestRankKey].ToInt32() : -1;
            if (newRankId > bestRank) statsDoc.stats[bestRankKey] = newRankId;

            // Handle calibration
            if (playedMatches < CalibrationMatchesRequired)
            {
                // Logic for calibration match count
                if (statsDoc.stats.Contains(calibrationKey))
                {
                    statsDoc.stats[calibrationKey] = statsDoc.stats[calibrationKey].ToInt32() + 1;
                }
            }

            // Save to DB
            db.StoreStat(ObjectId.Parse(playerId), mmrKey, newMmr);
            db.StoreStat(ObjectId.Parse(playerId), rankKey, newRankId);
            db.StoreStat(ObjectId.Parse(playerId), playedKey, playedMatches + 1);
            if (isWin) db.StoreStat(ObjectId.Parse(playerId), wonKey, wonMatches + 1);
            if (newRankId > bestRank) db.StoreStat(ObjectId.Parse(playerId), bestRankKey, newRankId);
            
            Console.WriteLine($"[RankedManager] Updated {playerId} ({mode}): MMR {currentMmr} -> {newMmr} (delta {delta}), Rank {newRankId}");
        }
    }
}
