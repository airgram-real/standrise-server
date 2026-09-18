using System;
using System.Collections.Generic;
using System.Linq;
using Axlebolt.Bolt.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;

namespace StandRiseServer.RpcServer.Api
{
    internal static class ClanStatsPayloadBuilder
    {
        private static readonly Dictionary<string, int> DefaultClanStats = new Dictionary<string, int>
        {
            // Uncalibrated clan defaults: mmr=0, rank=-1, no calibration matches played yet.
            // When played_matches < 10 the client renders the empty/uncalibrated rank icon
            // instead of falling back to Bronze I.
            { "clan_ranked_current_mmr", 0 },
            { "clan_ranked_rank", -1 },
            { "clan_ranked_best_rank", -1 },
            { "clan_ranked_played_matches", 0 },
            { "clan_ranked_won_match_count", 0 },
            { "clan_ranked_calibration_match_count", 0 },
            { "clan_ranked_xp", 0 },
            { "clan_ranked_best_rank_history1", 0 },
            { "clan_ranked_season_id", 3 },
            { "clan_ranked_is_banned", 0 }
        };

        private static readonly Dictionary<string, int> DefaultMemberStats = new Dictionary<string, int>
        {
            { "clan_member_ranked_xp", 0 },
            { "clan_member_ranked_played_matches", 0 },
            { "clan_member_ranked_won_match_count", 0 },
            { "clan_member_ranked_last_match_status", 1 },
            { "clan_member_ranked_last_match_start_time", 0 }
        };

        public static string NormalizeSeasonId(string requestedSeasonId, ClanDocument clanDocument = null)
        {
            string live = MatchHistoryBuilder.CurrentSeasonId;
            if (string.IsNullOrWhiteSpace(requestedSeasonId))
                return live;
            int parsed;
            if (int.TryParse(requestedSeasonId, out parsed)
                && parsed >= 1 && parsed <= MatchHistoryBuilder.CurrentSeasonNumber)
                return requestedSeasonId.Trim();
            return live;
        }

        public static ClanStats BuildClanStats(ClanDocument clanDocument, string requestedSeasonId = null)
        {
            var clanStats = new ClanStats
            {
                ClanId = clanDocument?._id.ToString() ?? string.Empty,
                SeasonId = NormalizeSeasonId(requestedSeasonId, clanDocument)
            };

            if (clanDocument?.stats != null)
            {
                foreach (var stat in clanDocument.stats)
                {
                    if (TryConvertClanStat(stat.Name, stat.Value, out var clanStat))
                    {
                        clanStats.Stats.Add(clanStat);
                    }
                }
            }

            EnsureRequiredClanStats(clanStats, clanDocument);
            NormalizeUncalibratedClanStats(clanStats);
            ApplyClanSeasonHistory(clanDocument, clanStats);
            // UI клана залипал на сезоне 2 из Mongo — всегда живой сезон из настроек.
            clanStats.SeasonId = MatchHistoryBuilder.CurrentSeasonId;
            SetClanStatInt(clanStats, "clan_ranked_season_id", MatchHistoryBuilder.CurrentSeasonNumber);
            return clanStats;
        }

        // Для прошлых сезонов (1 и 2) отдаём отдельную, детерминированно выведенную из
        // текущих статов клана "историю", чтобы переключение сезонов в статистике клана
        // показывало разные цифры для сезонов 1/2 и 3, а не одинаковые значения текущего.
        private static void ApplyClanSeasonHistory(ClanDocument clanDocument, ClanStats clanStats)
        {
            if (!LocalServerConfig.Current.FakeSeasonHistory)
            {
                return;
            }
            if (!int.TryParse(clanStats.SeasonId, out int season)
                || season < 1 || season >= MatchHistoryBuilder.CurrentSeasonNumber)
            {
                return;
            }

            string clanKey = clanDocument != null ? clanDocument._id.ToString() : (clanStats.ClanId ?? string.Empty);
            int playedMatches = GetClanStatInt(clanStats, "clan_ranked_played_matches");
            bool calibrated = playedMatches > 0 && GetClanStatInt(clanStats, "clan_ranked_calibration_match_count") >= 10;

            foreach (var stat in clanStats.Stats)
            {
                if (stat.StatId == null || !stat.StatId.StartsWith("clan_ranked_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double factor = SeasonFactor(clanKey, season, stat.StatId);
                switch (stat.StatId)
                {
                    case "clan_ranked_current_mmr":
                        if (calibrated) stat.IntValue = (int)Math.Round(stat.IntValue * factor);
                        break;
                    case "clan_ranked_played_matches":
                    case "clan_ranked_won_match_count":
                    case "clan_ranked_xp":
                    case "clan_ranked_best_rank_history1":
                        if (stat.IntValue > 0) stat.IntValue = (int)Math.Round(stat.IntValue * factor);
                        break;
                }
            }

            if (calibrated)
            {
                SetClanStatInt(clanStats, "clan_ranked_season_id", season);
            }
        }

        private static double SeasonFactor(string clanKey, int season, string salt)
        {
            string key = $"{clanKey}|{season}|{salt}";
            unchecked
            {
                int hash = 17;
                foreach (char c in key) hash = hash * 31 + c;
                hash = Math.Abs(hash);
                return 0.5 + (hash % 46) / 100.0;
            }
        }

        public static List<ClanMemberStats> BuildClanMemberStatsForClan(ClanDocument clanDocument, string seasonId = null)
        {
            var result = new List<ClanMemberStats>();
            if (clanDocument == null)
            {
                return result;
            }

            var playerIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var member in clanDocument.GetMembers())
            {
                string playerId = member?.PlayerFriend?.Player?.Id;
                if (!string.IsNullOrWhiteSpace(playerId))
                {
                    playerIds.Add(playerId);
                }
            }

            if (clanDocument.members != null)
            {
                foreach (var memberEntry in clanDocument.members)
                {
                    var fallbackId = memberEntry.Name;
                    if (memberEntry.Value.IsBsonDocument)
                    {
                        var memberDoc = memberEntry.Value.AsBsonDocument;
                        if (memberDoc.TryGetValue("playerId", out var playerIdValue) && playerIdValue.IsString)
                        {
                            fallbackId = playerIdValue.AsString;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(fallbackId))
                    {
                        playerIds.Add(fallbackId);
                    }
                }
            }

            foreach (var playerId in playerIds)
            {
                result.Add(BuildClanMemberStats(playerId, seasonId));
            }

            return result;
        }

        public static ClanMemberStats BuildClanMemberStats(string playerId, string seasonId = null)
        {
            var memberStats = new ClanMemberStats
            {
                PlayerId = playerId ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(playerId))
            {
                EnsureRequiredMemberStats(memberStats);
                return memberStats;
            }

            var statsDocument = BoltGameDatabaseProvider.Instance.GetOrCreatePlayerStatsDocument(playerId);
            if (statsDocument?.stats != null)
            {
                foreach (var stat in statsDocument.stats)
                {
                    if (!stat.Name.StartsWith("clan_member_ranked_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (TryConvertClanMemberStat(stat.Name, stat.Value, out var memberStat))
                    {
                        memberStats.Stats.Add(memberStat);
                    }
                }
            }

            EnsureRequiredMemberStats(memberStats);

            // История прошлых сезонов для участников клана — чтобы статистика клана
            // по сезонам 1 и 2 отличалась от текущего сезона.
            if (LocalServerConfig.Current.FakeSeasonHistory
                && int.TryParse(seasonId, out int season)
                && season >= 1 && season < MatchHistoryBuilder.CurrentSeasonNumber
                && memberStats.Stats.Count > 0)
            {
                foreach (var stat in memberStats.Stats)
                {
                    if (stat.StatId == null ||
                        (!stat.StatId.Equals("clan_member_ranked_xp", StringComparison.OrdinalIgnoreCase) &&
                         !stat.StatId.Equals("clan_member_ranked_played_matches", StringComparison.OrdinalIgnoreCase) &&
                         !stat.StatId.Equals("clan_member_ranked_won_match_count", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (stat.IntValue > 0)
                    {
                        stat.IntValue = (int)Math.Round(stat.IntValue * SeasonFactor(playerId ?? string.Empty, season, stat.StatId));
                    }
                }
            }

            return memberStats;
        }

        private static void EnsureRequiredClanStats(ClanStats clanStats, ClanDocument clanDocument)
        {
            var existing = new HashSet<string>(clanStats.Stats.Select(x => x.StatId), StringComparer.Ordinal);
            foreach (var pair in DefaultClanStats)
            {
                if (existing.Contains(pair.Key))
                {
                    continue;
                }

                clanStats.Stats.Add(new ClanStat
                {
                    StatId = pair.Key,
                    Type = StatDefType.Int,
                    IntValue = pair.Value
                });
            }

            if (!existing.Contains("MEMBERS_COUNT"))
            {
                clanStats.Stats.Add(new ClanStat
                {
                    StatId = "MEMBERS_COUNT",
                    Type = StatDefType.Int,
                    IntValue = Math.Max(clanDocument?.membersCount ?? 0, 0)
                });
            }
        }

        private static void NormalizeUncalibratedClanStats(ClanStats clanStats)
        {
            int playedMatches = GetClanStatInt(clanStats, "clan_ranked_played_matches");
            int calibrationMatches = GetClanStatInt(clanStats, "clan_ranked_calibration_match_count");

            if (playedMatches <= 0 || calibrationMatches < 10)
            {
                SetClanStatInt(clanStats, "clan_ranked_current_mmr", 0);
                SetClanStatInt(clanStats, "clan_ranked_rank", -1);
                SetClanStatInt(clanStats, "clan_ranked_best_rank", -1);
                SetClanStatInt(clanStats, "clan_ranked_best_rank_history1", 0);
                SetClanStatInt(clanStats, "clan_ranked_season_id", MatchHistoryBuilder.CurrentSeasonNumber);
            }
        }

        private static int GetClanStatInt(ClanStats clanStats, string statId)
        {
            var stat = clanStats?.Stats.FirstOrDefault(x => string.Equals(x.StatId, statId, StringComparison.Ordinal));
            return stat == null ? 0 : stat.IntValue;
        }

        private static void SetClanStatInt(ClanStats clanStats, string statId, int value)
        {
            var stat = clanStats.Stats.FirstOrDefault(x => string.Equals(x.StatId, statId, StringComparison.Ordinal));
            if (stat == null)
            {
                clanStats.Stats.Add(new ClanStat
                {
                    StatId = statId,
                    Type = StatDefType.Int,
                    IntValue = value
                });
                return;
            }

            stat.Type = StatDefType.Int;
            stat.IntValue = value;
            stat.FloatValue = 0;
        }

        private static void EnsureRequiredMemberStats(ClanMemberStats memberStats)
        {
            var existing = new HashSet<string>(memberStats.Stats.Select(x => x.StatId), StringComparer.Ordinal);
            foreach (var pair in DefaultMemberStats)
            {
                if (existing.Contains(pair.Key))
                {
                    continue;
                }

                memberStats.Stats.Add(new ClanMemberStat
                {
                    StatId = pair.Key,
                    Type = StatDefType.Int,
                    IntValue = pair.Value
                });
            }
        }

        private static bool TryConvertClanStat(string statId, BsonValue value, out ClanStat clanStat)
        {
            clanStat = null;

            if (string.IsNullOrWhiteSpace(statId) || value == null)
            {
                return false;
            }

            if (value.IsDouble)
            {
                clanStat = new ClanStat
                {
                    StatId = statId,
                    Type = StatDefType.Float,
                    FloatValue = (float)value.AsDouble
                };
                return true;
            }

            if (value.IsInt32 || value.IsInt64 || value.IsBoolean || value.IsString)
            {
                clanStat = new ClanStat
                {
                    StatId = statId,
                    Type = StatDefType.Int,
                    IntValue = ToIntValue(value)
                };
                return true;
            }

            return false;
        }

        private static bool TryConvertClanMemberStat(string statId, BsonValue value, out ClanMemberStat memberStat)
        {
            memberStat = null;

            if (string.IsNullOrWhiteSpace(statId) || value == null)
            {
                return false;
            }

            if (value.IsDouble)
            {
                memberStat = new ClanMemberStat
                {
                    StatId = statId,
                    Type = StatDefType.Float,
                    FloatValue = (float)value.AsDouble
                };
                return true;
            }

            if (value.IsInt32 || value.IsInt64 || value.IsBoolean || value.IsString)
            {
                memberStat = new ClanMemberStat
                {
                    StatId = statId,
                    Type = StatDefType.Int,
                    IntValue = ToIntValue(value)
                };
                return true;
            }

            return false;
        }

        private static int ToIntValue(BsonValue value)
        {
            if (value.IsInt32)
            {
                return value.AsInt32;
            }

            if (value.IsInt64)
            {
                long longValue = value.AsInt64;
                if (longValue > int.MaxValue)
                {
                    return int.MaxValue;
                }

                if (longValue < int.MinValue)
                {
                    return int.MinValue;
                }

                return (int)longValue;
            }

            if (value.IsDouble)
            {
                double doubleValue = value.AsDouble;
                if (doubleValue > int.MaxValue)
                {
                    return int.MaxValue;
                }

                if (doubleValue < int.MinValue)
                {
                    return int.MinValue;
                }

                return (int)doubleValue;
            }

            if (value.IsBoolean)
            {
                return value.AsBoolean ? 1 : 0;
            }

            if (value.IsString && int.TryParse(value.AsString, out int parsed))
            {
                return parsed;
            }

            return 0;
        }
    }
}
