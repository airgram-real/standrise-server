using System;
using System.Collections.Generic;
using Axlebolt.Bolt.Protobuf;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// Stub helper for clan stats - provides default values when stats are missing
    /// </summary>
    public static class ClanStatsStub
    {
        /// <summary>
        /// Creates a stub ClanStats with default values for testing
        /// </summary>
        public static ClanStats CreateStubClanStats(string clanId, int seasonId = 3)
        {
            var stats = new ClanStats
            {
                ClanId = clanId,
                SeasonId = seasonId.ToString()
            };

            stats.Stats.Add(new ClanStat { StatId = "clan_ranked_current_mmr", IntValue = 0 });
            stats.Stats.Add(new ClanStat { StatId = "clan_ranked_rank", IntValue = -1 });
            stats.Stats.Add(new ClanStat { StatId = "clan_ranked_best_rank", IntValue = -1 });
            stats.Stats.Add(new ClanStat { StatId = "clan_ranked_played_matches", IntValue = 0 });
            stats.Stats.Add(new ClanStat { StatId = "clan_ranked_won_match_count", IntValue = 0 });
            stats.Stats.Add(new ClanStat { StatId = "clan_ranked_calibration_match_count", IntValue = 0 });
            stats.Stats.Add(new ClanStat { StatId = "clan_ranked_xp", IntValue = 0 });
            stats.Stats.Add(new ClanStat { StatId = "clan_ranked_best_rank_history1", IntValue = 0 });
            stats.Stats.Add(new ClanStat { StatId = "clan_ranked_season_id", IntValue = seasonId });

            return stats;
        }

        /// <summary>
        /// Creates stub ClanMemberStats with default values
        /// </summary>
        public static ClanMemberStats CreateStubMemberStats(string playerId)
        {
            var memberStats = new ClanMemberStats
            {
                PlayerId = playerId
            };

            // Add all required member stats with default values
            memberStats.Stats.Add(new ClanMemberStat { StatId = "clan_member_ranked_xp", IntValue = 100 });
            memberStats.Stats.Add(new ClanMemberStat { StatId = "clan_member_ranked_played_matches", IntValue = 5 });
            memberStats.Stats.Add(new ClanMemberStat { StatId = "clan_member_ranked_won_match_count", IntValue = 2 });
            memberStats.Stats.Add(new ClanMemberStat { StatId = "clan_member_ranked_last_match_status", IntValue = 0 });
            memberStats.Stats.Add(new ClanMemberStat { StatId = "clan_member_ranked_last_match_start_time", IntValue = 0 });

            return memberStats;
        }

        /// <summary>
        /// Ensures all required stats are present, adds missing ones with defaults
        /// </summary>
        public static void EnsureAllStatsPresent(ClanStats stats)
        {
            var requiredStats = new Dictionary<string, int>
            {
                { "clan_ranked_current_mmr", 0 },
                { "clan_ranked_rank", -1 },
                { "clan_ranked_best_rank", -1 },
                { "clan_ranked_played_matches", 0 },
                { "clan_ranked_won_match_count", 0 },
                { "clan_ranked_calibration_match_count", 0 },
                { "clan_ranked_xp", 0 },
                { "clan_ranked_best_rank_history1", 0 },
                { "clan_ranked_season_id", 3 }
            };

            var existingStatIds = new HashSet<string>();
            foreach (var stat in stats.Stats)
            {
                existingStatIds.Add(stat.StatId);
            }

            foreach (var kvp in requiredStats)
            {
                if (!existingStatIds.Contains(kvp.Key))
                {
                    stats.Stats.Add(new ClanStat { StatId = kvp.Key, IntValue = kvp.Value });
                }
            }
        }

        /// <summary>
        /// Ensures all required member stats are present
        /// </summary>
        public static void EnsureAllMemberStatsPresent(ClanMemberStats memberStats)
        {
            var requiredStats = new Dictionary<string, int>
            {
                { "clan_member_ranked_xp", 100 },
                { "clan_member_ranked_played_matches", 5 },
                { "clan_member_ranked_won_match_count", 2 },
                { "clan_member_ranked_last_match_status", 0 },
                { "clan_member_ranked_last_match_start_time", 0 }
            };

            var existingStatIds = new HashSet<string>();
            foreach (var stat in memberStats.Stats)
            {
                existingStatIds.Add(stat.StatId);
            }

            foreach (var kvp in requiredStats)
            {
                if (!existingStatIds.Contains(kvp.Key))
                {
                    memberStats.Stats.Add(new ClanMemberStat { StatId = kvp.Key, IntValue = kvp.Value });
                }
            }
        }
    }
}
