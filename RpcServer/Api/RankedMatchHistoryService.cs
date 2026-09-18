using System;
using System.Collections.Generic;
using System.Linq;
using Axlebolt.Bolt.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.MongoDB.Main.PlayerStats;
using StandRiseServer.RpcServer;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// Сохраняет FinishedMatch в player_match_history после ranked/allies матчей.
    /// </summary>
    public static class RankedMatchHistoryService
    {
        public sealed class RankSnapshot
        {
            public int Mmr;
            public int Rank;
            public int Played;
        }

        public static RankSnapshot CaptureSnapshot(string playerId, string rankedMode)
        {
            var db = BoltGameDatabaseProvider.Instance;
            if (rankedMode == "allies")
            {
                int mmr = (int)db.GetPlayerStat(playerId, "ranked_2v2_current_mmr");
                if (mmr <= 0) mmr = (int)AlliesMmrSystem.DefaultMmr;
                return new RankSnapshot
                {
                    Mmr = mmr,
                    Rank = (int)db.GetPlayerStat(playerId, "ranked_2v2_rank"),
                    Played = (int)db.GetPlayerStat(playerId, "ranked_2v2_played_matches"),
                };
            }

            int rankedMmr = (int)db.GetPlayerStat(playerId, "ranked_current_mmr");
            if (rankedMmr <= 0) rankedMmr = (int)AlliesMmrSystem.DefaultMmr;
            return new RankSnapshot
            {
                Mmr = rankedMmr,
                Rank = (int)db.GetPlayerStat(playerId, "ranked_rank"),
                Played = (int)db.GetPlayerStat(playerId, "ranked_played_matches"),
            };
        }

        public static void SaveFromMatchResult(
            HttpApiServer.MatchResult data,
            IReadOnlyDictionary<string, RankSnapshot> preSnapshots,
            string rankedMode,
            IReadOnlyDictionary<string, PlayerStatsRemoteService.MatchRewardResult> rewardsByPlayer = null)
        {
            if (data?.Scores == null || data.Scores.Count == 0) return;

            try
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                BoltGameDatabaseProvider.Instance.EnsureNumericMatchIds();
                string hexId = BoltGameDatabaseProvider.CanonicalMatchId(
                    string.IsNullOrWhiteSpace(data.MatchId) ? data.RoomId : data.MatchId);
                string matchId = BoltGameDatabaseProvider.Instance.FindPublicMatchId(hexId);
                if (string.IsNullOrEmpty(matchId) && !string.IsNullOrWhiteSpace(data.RoomId))
                    matchId = BoltGameDatabaseProvider.Instance.FindPublicMatchId(data.RoomId);
                if (string.IsNullOrEmpty(matchId))
                    matchId = BoltGameDatabaseProvider.Instance.AllocateNextMatchNumber().ToString();
                string clientMode;
                if (rankedMode == "allies") clientMode = "Ranked2v2";
                else if (rankedMode == "ranked") clientMode = "RankedDefuse";
                else if (rankedMode == "deathmatch") clientMode = "DeathMatch";
                else if (rankedMode == "defuse") clientMode = "Defuse";
                else if (rankedMode == "armsrace") clientMode = "ArmsRace";
                else clientMode = rankedMode ?? "DeathMatch";

                var uniqueScores = data.Scores
                    .Where(s => !string.IsNullOrWhiteSpace(s.PlayerId))
                    .GroupBy(s => s.PlayerId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                var resolvedRows = new List<(string Id, string Name, string Uid, string Avatar, HttpApiServer.PlayerScore Score)>();
                foreach (var score in uniqueScores)
                {
                    string resolvedId = ResolvePlayerId(score.PlayerId);
                    string display = ResolvePlayerDisplayName(resolvedId);
                    string uid = "";
                    string avatar = "0";
                    try
                    {
                        if (ObjectId.TryParse(resolvedId, out ObjectId oid))
                        {
                            var doc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);
                            if (doc != null)
                            {
                                if (!string.IsNullOrWhiteSpace(doc.name))
                                    display = doc.name.Trim();
                                if (!string.IsNullOrWhiteSpace(doc.uid)
                                    && (doc.uid.Length != 24 || doc.uid.IndexOf('_') >= 0))
                                    uid = doc.uid.Trim();
                                if (!string.IsNullOrWhiteSpace(doc.avatarId))
                                    avatar = doc.avatarId;
                            }
                        }
                    }
                    catch { }
                    if (string.IsNullOrWhiteSpace(uid)) uid = display;

                    resolvedRows.Add((resolvedId, display, uid, avatar, score));
                }

                string mapFromPlugin = (data.Map ?? "").Trim();
                string mapName = mapFromPlugin;
                string mapSource = "plugin";
                if (string.IsNullOrEmpty(mapName))
                {
                    mapName = MatchHistoryBuilder.RecallMatchMap(data.MatchId, data.RoomId);
                    mapSource = "matchmaking";
                }
                if (string.IsNullOrEmpty(mapName))
                {
                    mapName = ExtractMapFromRoomId(data.RoomId);
                    mapSource = "roomId";
                }
                if (string.IsNullOrEmpty(mapName))
                {
                    mapName = clientMode == "Ranked2v2" ? "Province 2x2" : "Province";
                    mapSource = "fallback";
                }
                Logger.Log($"[MatchHistory] map matchId='{data.MatchId}' room='{data.RoomId}' "
                    + $"fromPlugin='{mapFromPlugin}' → '{mapName}' (source={mapSource}, mode={clientMode})");

                var playerRows = new List<MatchHistoryBuilder.PlayerRow>();
                foreach (var row in resolvedRows)
                {
                    bool isWinner = data.WinnerTeam != 0 && row.Score.Team == data.WinnerTeam;
                    if (!data.IsGiveUp && (data.TrScore > 0 || data.CtScore > 0) && row.Score.Team > 0)
                    {
                        bool byScore = (row.Score.Team == 1 && data.TrScore > data.CtScore)
                            || (row.Score.Team == 2 && data.CtScore > data.TrScore);
                        if (data.WinnerTeam != 0 && isWinner != byScore)
                            Logger.LogWarn($"[MatchHistory] winner mismatch player={row.Id} team={row.Score.Team} WinnerTeam={data.WinnerTeam} byScore={byScore} → using score");
                        isWinner = byScore;
                    }
                    preSnapshots.TryGetValue(row.Id, out RankSnapshot pre);
                    if (pre == null && !string.IsNullOrWhiteSpace(row.Score.PlayerId))
                        preSnapshots.TryGetValue(row.Score.PlayerId, out pre);
                    if (pre == null && !string.IsNullOrWhiteSpace(row.Uid))
                        preSnapshots.TryGetValue(row.Uid, out pre);
                    bool preMissing = pre == null;
                    if (pre == null) pre = CaptureSnapshot(row.Id, rankedMode);
                    RankSnapshot post = CaptureSnapshot(row.Id, rankedMode);
                    if (post.Mmr > pre.Mmr) isWinner = true;
                    else if (post.Mmr < pre.Mmr) isWinner = false;
                    Logger.Log($"[MatchHistory] mmr {row.Id}: pre={pre.Mmr} post={post.Mmr} delta={post.Mmr - pre.Mmr} "
                        + $"rankPre={pre.Rank} rankPost={post.Rank} preSnapshotMissing={preMissing} won={isWinner} "
                        + $"snapKeys=[{string.Join(",", preSnapshots.Keys)}]");
                    playerRows.Add(new MatchHistoryBuilder.PlayerRow
                    {
                        PlayerId = row.Id,
                        Uid = row.Uid,
                        Name = row.Name,
                        Avatar = row.Avatar,
                        Kills = row.Score.Kills,
                        Deaths = row.Score.Deaths,
                        Assists = row.Score.Assists,
                        Score = row.Score.Score,
                        Team = row.Score.Team,
                        Won = isWinner,
                        Mmr = post.Mmr,
                        MmrOld = pre.Mmr,
                        ClientRank = ClientRankDisplay.ToClientRankFromMmr(post.Mmr, SanitizeRank(post.Rank)),
                        ClientRankOld = ClientRankDisplay.ToClientRankFromMmr(pre.Mmr, SanitizeRank(pre.Rank)),
                    });
                }

                foreach (var row in resolvedRows)
                {
                    try
                    {
                        int viewerRank = 1;
                        var mine = playerRows.FirstOrDefault(p =>
                            string.Equals(p.PlayerId, row.Id, StringComparison.OrdinalIgnoreCase));
                        if (mine != null) viewerRank = mine.ClientRank;

                        var copy = MatchHistoryBuilder.Build(
                            matchId,
                            mapName,
                            clientMode,
                            now - 600_000L,
                            now,
                            data.TrScore,
                            data.CtScore,
                            data.IsGiveUp,
                            playerRows,
                            viewerRank,
                            data.WinnerTeam);
                        copy.EnsureViewerPlayerRow(row.Id, row.Uid, row.Name, row.Avatar);
                        if (mine != null)
                        {
                            int delta = mine.Mmr - mine.MmrOld;
                            int result = MatchHistoryRules.ResultForViewer(
                                data.TrScore, data.CtScore, mine.Team, data.WinnerTeam, delta);
                            copy.UpsertOverallIntPublic("result", result);
                            copy.UpsertOverallIntPublic("win", result == 1 ? 1 : 0);
                            copy.UpsertOverallIntPublic("is_win", result == 1 ? 1 : 0);
                            copy.UpsertOverallSigned("mmr_delta", delta);
                            copy.UpsertOverallSigned("DeltaMmr", delta);
                            copy.UpsertOverallSigned("mmr_change", delta);
                            copy.UpsertOverallIntPublic("Mmr", mine.Mmr);
                        }
                        if (rewardsByPlayer != null
                            && (rewardsByPlayer.TryGetValue(row.Id, out var reward)
                                || (!string.IsNullOrWhiteSpace(row.Score.PlayerId) && rewardsByPlayer.TryGetValue(row.Score.PlayerId, out reward))))
                        {
                            copy.UpsertOverallXp((long)reward.XpEarned);
                            copy.UpsertOverallIntPublic("gold", reward.Gold);
                            copy.UpsertOverallIntPublic("silver", reward.Silver);
                            if (reward.HasDrop && reward.DropItemDefinitionId > 0)
                                copy.AttachViewerDrop(row.Id, reward.DropItemDefinitionId);
                        }
                        copy.EnsureViewerPlayerRow(row.Id, row.Uid, row.Name, row.Avatar);
                        copy.NormalizeClientDates();
                        copy.IncludeTeamGroupsForDetail();
                        copy.BakeWirePayload();

                        string roomAlias = string.IsNullOrWhiteSpace(data.RoomId) ? hexId : data.RoomId;
                        BoltGameDatabaseProvider.Instance.SavePlayerLastMatch(row.Id, copy, roomAlias);
                        BroadcastMatchFinished(row.Id, copy);
                        Logger.Log($"[MatchHistory] Saved match {matchId} for {row.Id} ({clientMode}, map='{mapName}', players={copy.PlayerRowCount}, season={copy.SeasonId}, score={data.TrScore}-{data.CtScore})");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarn($"[MatchHistory] Save failed for {row.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarn($"[MatchHistory] SaveFromMatchResult failed: {ex.Message}");
            }
        }

        private static void AddPerPlayerOverallStats(FinishedMatch fm, string playerId, string rankedMode)
        {
            var db = BoltGameDatabaseProvider.Instance;
            if (rankedMode == "allies")
            {
                int played = (int)db.GetPlayerStat(playerId, "ranked_2v2_played_matches");
                int rank = (int)db.GetPlayerStat(playerId, "ranked_2v2_rank");
                int calib = (int)db.GetPlayerStat(playerId, "ranked_2v2_won_match_count");
                fm.AddOverallStat(new FinishedMatch.StatValueRow { Name = "ranked_2v2_played_matches", Type = 0, IntValue = played });
                fm.AddOverallStat(new FinishedMatch.StatValueRow { Name = "ranked_2v2_rank", Type = 0, IntValue = ClientRankDisplay.ToClientRankFromMmr(
                    (float)db.GetPlayerStat(playerId, "ranked_2v2_current_mmr"), SanitizeRank(rank)) });
                fm.AddOverallStat(new FinishedMatch.StatValueRow { Name = "calibration_matches_played", Type = 0, IntValue = Math.Clamp(played, 0, 10) });
                fm.AddOverallStat(new FinishedMatch.StatValueRow { Name = "calibration_match_count", Type = 0, IntValue = 10 });
                fm.AddOverallStat(new FinishedMatch.StatValueRow { Name = "ranked_2v2_won_match_count", Type = 0, IntValue = calib });
            }
            else
            {
                int played = (int)db.GetPlayerStat(playerId, "ranked_played_matches");
                int rank = (int)db.GetPlayerStat(playerId, "ranked_rank");
                float mmr = (float)db.GetPlayerStat(playerId, "ranked_current_mmr");
                fm.AddOverallStat(new FinishedMatch.StatValueRow { Name = "ranked_played_matches", Type = 0, IntValue = played });
                fm.AddOverallStat(new FinishedMatch.StatValueRow { Name = "ranked_rank", Type = 0, IntValue = ClientRankDisplay.ToClientRankFromMmr(mmr, SanitizeRank(rank)) });
                fm.AddOverallStat(new FinishedMatch.StatValueRow { Name = "calibration_matches_played", Type = 0, IntValue = Math.Clamp(played, 0, 10) });
                fm.AddOverallStat(new FinishedMatch.StatValueRow { Name = "calibration_match_count", Type = 0, IntValue = 10 });
            }
        }

        private static List<FinishedMatch.StatValueRow> BuildPlayerStats(
            HttpApiServer.PlayerScore score,
            int trScore,
            int ctScore,
            bool isWinner,
            RankSnapshot pre,
            RankSnapshot post)
        {
            int mmrDelta = post.Mmr - pre.Mmr;
            return new List<FinishedMatch.StatValueRow>
            {
                new FinishedMatch.StatValueRow { Name = "kill", Type = 0, IntValue = score.Kills },
                new FinishedMatch.StatValueRow { Name = "kills", Type = 0, IntValue = score.Kills },
                new FinishedMatch.StatValueRow { Name = "death", Type = 0, IntValue = score.Deaths },
                new FinishedMatch.StatValueRow { Name = "deaths", Type = 0, IntValue = score.Deaths },
                new FinishedMatch.StatValueRow { Name = "assist", Type = 0, IntValue = score.Assists },
                new FinishedMatch.StatValueRow { Name = "assists", Type = 0, IntValue = score.Assists },
                new FinishedMatch.StatValueRow { Name = "score", Type = 0, IntValue = score.Score },
                new FinishedMatch.StatValueRow { Name = "team", Type = 0, IntValue = score.Team },
                new FinishedMatch.StatValueRow { Name = "result", Type = 0, IntValue = isWinner ? 1 : 0 },
                new FinishedMatch.StatValueRow { Name = "mmr", Type = 0, IntValue = post.Mmr },
                new FinishedMatch.StatValueRow { Name = "mmr_old", Type = 0, IntValue = pre.Mmr },
                new FinishedMatch.StatValueRow { Name = "mmr_delta", Type = 0, IntValue = mmrDelta },
                new FinishedMatch.StatValueRow { Name = "current_rank", Type = 0, IntValue = ClientRankDisplay.ToClientRankFromMmr(post.Mmr, SanitizeRank(post.Rank)) },
                new FinishedMatch.StatValueRow { Name = "rank_old", Type = 0, IntValue = ClientRankDisplay.ToClientRankFromMmr(pre.Mmr, SanitizeRank(pre.Rank)) },
                new FinishedMatch.StatValueRow { Name = "score1", Type = 0, IntValue = trScore },
                new FinishedMatch.StatValueRow { Name = "score2", Type = 0, IntValue = ctScore },
                new FinishedMatch.StatValueRow { Name = "TrScore", Type = 0, IntValue = trScore },
                new FinishedMatch.StatValueRow { Name = "CtScore", Type = 0, IntValue = ctScore },
            };
        }

        private static void BroadcastMatchFinished(string playerId, FinishedMatch match)
        {
            try
            {
                if (match == null) return;
                var userService = StaticClasses.ResolveEventDeliveryService(playerId);
                if (userService == null)
                    return;

                match.UseStructuredWire();
                match.BakeWirePayload();
                var evt = new OnMatchFinishedEvent { Match = match };
                userService.SendResponce(new Axlebolt.RpcSupport.Protobuf.ResponseMessage
                {
                    EventResponse = new Axlebolt.RpcSupport.Protobuf.EventResponse
                    {
                        ListenerName = "MatchesRemoteEventListener",
                        EventName = "onMatchFinished",
                        Params = { new ToByteMethod(typeof(OnMatchFinishedEvent)).ToBytes(evt) }
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogWarn($"[MatchHistory] onMatchFinished broadcast failed for {playerId}: {ex.Message}");
            }
        }

        private static string ResolvePlayerId(string rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId)) return rawId;
            rawId = rawId.Trim();
            if (ObjectId.TryParse(rawId, out _)) return rawId;
            try
            {
                var byUid = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(rawId);
                if (byUid != null && byUid.Length > 0)
                    return byUid[0]._id.ToString();
            }
            catch { }
            try
            {
                var byName = BoltMainDatabaseProvider.Instance.FindPlayersByUidOrName(rawId, 1);
                if (byName != null && byName.Length > 0)
                    return byName[0]._id.ToString();
            }
            catch { }
            return rawId;
        }

        private static string ResolvePlayerDisplayName(string playerId)
        {
            try
            {
                if (ObjectId.TryParse(playerId, out ObjectId oid))
                {
                    var doc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);
                    if (doc != null)
                    {
                        if (!string.IsNullOrWhiteSpace(doc.name)) return doc.name.Trim();
                        if (!string.IsNullOrWhiteSpace(doc.uid) && doc.uid.IndexOf('_') < 0) return doc.uid.Trim();
                        if (!string.IsNullOrWhiteSpace(doc.uid)) return doc.uid.Trim();
                    }
                }
            }
            catch { }
            return playerId ?? "";
        }

        private static int SanitizeRank(int rank)
        {
            // Клиент 0.17 падает на zigzag(-1) в ranked_*_rank → молча отбрасывает всю историю.
            if (rank < 0) return 0;
            if (rank > ClientRankDisplay.InternalLegendRank) return ClientRankDisplay.InternalLegendRank;
            return rank;
        }

        internal static bool LooksLikeMatchIdHex(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            string t = token.Trim();
            if (t.Length < 16 || t.Length > 64) return false;
            foreach (char c in t)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static string ExtractMapFromRoomId(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId)) return "";
            string s = roomId;
            int idx = s.IndexOf('_');
            if (idx >= 0 && idx < s.Length - 1)
                s = s.Substring(idx + 1);
            foreach (var token in s.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = token.Trim();
                // Ranked2v2_<32hex> — hex это matchId, не карта (иначе UI пустой).
                if (LooksLikeMatchIdHex(t)) continue;
                if (t.Length >= 3 && char.IsLetter(t[0]))
                    return char.ToUpperInvariant(t[0]) + t.Substring(1).ToLowerInvariant();
            }
            return "";
        }
    }
}
