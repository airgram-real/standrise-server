using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.MongoDB.Main.PlayerStats;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// Канонический wire истории матчей 0.17 (MCMGBNHJEAG).
    /// Минимальный набор полей — клиент молча дропает список при лишнем/битом payload.
    /// </summary>
    public static class MatchHistoryBuilder
    {
        /// <summary>
        /// Live season для союзников / клана / новых матчей.
        /// Берётся из local.settings.json ("LiveSeasonId"), чтобы номер можно было
        /// поменять без пересборки — он должен совпадать с тем, что открывает клиент.
        /// </summary>
        public static string CurrentSeasonId
        {
            get
            {
                try
                {
                    int id = LocalServerConfig.Current.LiveSeasonId;
                    if (id > 0) return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                catch { }
                return "3";
            }
        }

        /// <summary>
        /// Сезон, которым штампуется матч. Один на весь сервер и на все точки выдачи —
        /// история профиля, последний матч, экран завершения, клановые матчи.
        /// </summary>
        /// <summary>
        /// Карта, на которой матч реально стартовал. Результат от Photon-плагина не всегда
        /// доносит имя карты (room prop C1), а из id комнаты Ranked2v2_&lt;guid&gt; его не
        /// достать — без этого вся история уезжала в "Province".
        /// Матчмейкинг знает карту в момент старта, поэтому запоминаем её здесь.
        /// </summary>
        private static readonly ConcurrentDictionary<string, string> StartedMatchMaps =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void RememberMatchMap(string matchId, string roomId, string map)
        {
            if (string.IsNullOrWhiteSpace(map)) return;
            string value = map.Trim();
            // Держим окно последних матчей, иначе словарь растёт вечно.
            if (StartedMatchMaps.Count > 1000) StartedMatchMaps.Clear();
            if (!string.IsNullOrWhiteSpace(matchId)) StartedMatchMaps[matchId.Trim()] = value;
            if (!string.IsNullOrWhiteSpace(roomId)) StartedMatchMaps[roomId.Trim()] = value;
        }

        public static string RecallMatchMap(string matchId, string roomId)
        {
            if (!string.IsNullOrWhiteSpace(matchId)
                && StartedMatchMaps.TryGetValue(matchId.Trim(), out string byMatch))
                return byMatch;
            if (!string.IsNullOrWhiteSpace(roomId)
                && StartedMatchMaps.TryGetValue(roomId.Trim(), out string byRoom))
                return byRoom;
            return "";
        }

        public static string MatchSeasonId(bool clanBattle)
        {
            try
            {
                // Текущий сезон клиента = LiveSeasonId (3). «СЕЗОН 1» в UI — имя,
				// не id: при SeasonId=1 вкладка текущего сезона пустая.
                int id = clanBattle
                    ? LocalServerConfig.Current.MatchSeasonClanBattle
                    : LocalServerConfig.Current.LiveSeasonId;
                if (id <= 0 && !clanBattle)
                    id = LocalServerConfig.Current.MatchSeasonRegular;
                if (id <= 0) id = clanBattle ? 2 : 3;
                return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { }
            return clanBattle ? "2" : "1";
        }

        /// <summary>Клановая ли это битва — по названию режима.</summary>
        public static bool IsClanBattleMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return false;
            string m = mode.ToLowerInvariant();
            return m.Contains("clan");
        }

        /// <summary>
        /// Единая штамповка матча под клиент 0.17. Все места, которые отдают FinishedMatch,
        /// обязаны звать её — иначе поля разъезжаются и клиент выбрасывает список целиком.
        /// </summary>
        public static FinishedMatch StampForClient(FinishedMatch fm, string mode = null)
        {
            if (fm == null) return null;
            string resolvedMode = mode ?? fm.GetOverallString("mode") ?? "";
            bool clanBattle = IsClanBattleMode(resolvedMode);
            fm.Region = "";
            fm.Version = "0.17.0";
            // Casual=0 клиент 0.17 выкидывает из рейтинговой истории. Нужен Ranked / Ranked2v2.
            if (clanBattle)
                fm.MatchType = Axlebolt.Bolt.Protobuf.MatchType.ClanRanked;
            else if (resolvedMode.IndexOf("Ranked", System.StringComparison.OrdinalIgnoreCase) >= 0)
                fm.MatchType = Axlebolt.Bolt.Protobuf.MatchType.Ranked;
            else
                fm.MatchType = Axlebolt.Bolt.Protobuf.MatchType.Ranked;
            fm.SeasonId = MatchSeasonId(clanBattle);
            fm.State = MatchState.Finished;
            return fm;
        }

        /// <summary>Тот же живой сезон числом.</summary>
        public static int CurrentSeasonNumber
        {
            get
            {
                try
                {
                    int id = LocalServerConfig.Current.LiveSeasonId;
                    if (id > 0) return id;
                }
                catch { }
                return 3;
            }
        }

        public sealed class PlayerRow
        {
            public string PlayerId;
            public string Uid;
            public string Name;
            public string Avatar;
            public int Kills;
            public int Deaths;
            public int Assists;
            public int Score;
            public int Team;
            public bool Won;
            public int Mmr;
            public int MmrOld;
            public int ClientRank;
            public int ClientRankOld;
        }

        public static FinishedMatch Build(
            string matchId,
            string mapName,
            string mode,
            long startMs,
            long finishMs,
            int trScore,
            int ctScore,
            bool isGiveUp,
            IReadOnlyList<PlayerRow> players,
            int viewerClientRank = 0,
            int winnerTeam = 0)
        {
            if (finishMs <= 0) finishMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (startMs <= 0 || startMs > finishMs) startMs = finishMs - 600_000L;
            // Клиент с отстающими часами / «будущие» даты → пустой список. Держим окно последних суток.
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (finishMs > nowMs + 60_000L || finishMs < nowMs - 90L * 24 * 3600 * 1000)
            {
                finishMs = nowMs - 3_600_000L;
                startMs = finishMs - 600_000L;
            }

            string clientMode = NormalizeMode(mode);
            bool mapLooksAllies = MatchHistoryRules.LooksAllies(clientMode, mapName);
            if (mapLooksAllies)
                clientMode = "Ranked2v2";
            else
                clientMode = "RankedDefuse";
            string map = MapNames.ForMode(mapName, mapLooksAllies);
            int tr = MatchHistoryRules.ClampScore(trScore, mapLooksAllies);
            int ct = MatchHistoryRules.ClampScore(ctScore, mapLooksAllies);
            int safeViewerRank = ClampClientRank(viewerClientRank);

            // Поля под клиент проставляет StampForClient — единая точка на весь сервер.
            var fm = new FinishedMatch
            {
                MatchId = string.IsNullOrWhiteSpace(matchId) ? Guid.NewGuid().ToString("N") : matchId.Trim(),
                StartDate = startMs,
                FinishDate = finishMs,
            };
            StampForClient(fm, clientMode);

			// winner_team: без поля клиент часто рисует ложный W.
            if (winnerTeam == 0 && !isGiveUp)
            {
                if (tr > ct) winnerTeam = 1;
                else if (ct > tr) winnerTeam = 2;
            }
            if (winnerTeam == 0 && players != null)
            {
                var wp = players.FirstOrDefault(p => p != null && p.Won && p.Team > 0);
                if (wp != null) winnerTeam = wp.Team;
            }

            fm.AddOverallStat(S("map", map));
            fm.AddOverallStat(S("level", map));
            fm.AddOverallStat(S("mode", clientMode));
            fm.AddOverallStat(I("score1", tr));
            fm.AddOverallStat(I("score2", ct));
            fm.AddOverallStat(I("TrScore", tr));
            fm.AddOverallStat(I("CtScore", ct));
            // Список профиля 0.17 скрывает строки с is_give_up=1 → пустое окно.
            // Сдачу оставляем только в winner_team / result.
            fm.AddOverallStat(I("is_give_up", 0));
            if (winnerTeam > 0)
                fm.AddOverallStat(I("winner_team", winnerTeam));

            int rank = safeViewerRank > 0 ? safeViewerRank : 1;
            if (clientMode == "Ranked2v2")
            {
                fm.AddOverallStat(I("ranked_2v2_rank", rank));
                fm.AddOverallStat(I("ranked_2v2_played_matches", 10));
                fm.AddOverallStat(I("calibration_matches_played", 10));
                fm.AddOverallStat(I("calibration_match_count", 10));
            }
            else
            {
                fm.AddOverallStat(I("ranked_rank", rank));
                fm.AddOverallStat(I("ranked_played_matches", 10));
                fm.AddOverallStat(I("calibration_matches_played", 10));
                fm.AddOverallStat(I("calibration_match_count", 10));
            }

            if (players != null)
            {
                foreach (var p in players)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.PlayerId)) continue;
                    int pr = ClampClientRank(p.ClientRank);
                    string name = string.IsNullOrWhiteSpace(p.Name) ? (p.Uid ?? p.PlayerId) : p.Name;
                    string uid = p.Uid;
                    if (string.IsNullOrWhiteSpace(uid) || LooksLikeMongoId(uid))
                        uid = name;
                    if (string.Equals(uid, name, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(p.Uid)
                        && !LooksLikeMongoId(p.Uid)
                        && p.Uid.IndexOf('_') >= 0)
                        uid = p.Uid;
                    int mmr = Math.Max(0, p.Mmr);
                    int delta = p.Mmr - p.MmrOld;
                    int team = p.Team <= 0 ? 1 : p.Team;
                    int result = MatchHistoryRules.ResultForViewer(tr, ct, team, winnerTeam, delta);

                    string avatar = string.IsNullOrWhiteSpace(p.Avatar) ? "0" : p.Avatar.Trim();
                    avatar = FinishedMatch.SanitizeAvatarId(avatar);

                    var stats = new List<FinishedMatch.StatValueRow>
                    {
                        I("kill", Math.Max(0, p.Kills)),
                        I("death", Math.Max(0, p.Deaths)),
                        I("assist", Math.Max(0, p.Assists)),
                        I("score", Math.Max(0, p.Score)),
                        I("team", team),
                        I("result", result),
                        I("Mmr", mmr),
                        I("mmr", mmr),
                        Delta("DeltaMmr", delta),
                        Delta("mmr_delta", delta),
                        I("Rank", pr),
                        I("current_rank", pr),
                        I("score1", tr),
                        I("score2", ct),
                    };
                    fm.AddPlayerRow(p.PlayerId, name, stats, null, uid, avatar);
                }
            }

            fm.UseStructuredWire();
            fm.NormalizeClientDates();
            fm.BakeWirePayload();
            return fm;
        }

        public static FinishedMatch RebuildFromLegacy(FinishedMatch src, string ownerPlayerId = null)
        {
            if (src == null) src = new FinishedMatch();

            string map = src.GetOverallString("map")
                ?? src.GetOverallString("level")
                ?? "Province";
            string mode = src.GetOverallString("mode") ?? "";
            int tr = src.GetOverallInt("TrScore");
            if (tr == 0) tr = src.GetOverallInt("score1");
            int ct = src.GetOverallInt("CtScore");
            if (ct == 0) ct = src.GetOverallInt("score2");

            var extracted = ExtractPlayers(src);
            if (extracted.Count == 0 && !string.IsNullOrWhiteSpace(ownerPlayerId))
            {
                int r = ClampClientRank(src.GetOverallInt("ranked_2v2_rank"));
                if (r <= 0) r = ClampClientRank(src.GetOverallInt("ranked_rank"));
                if (r <= 0) r = 1;
                extracted.Add(new PlayerRow
                {
                    PlayerId = ownerPlayerId,
                    Uid = ownerPlayerId,
                    Name = ownerPlayerId,
                    Team = 1,
                    Won = false,
                    ClientRank = r,
                    ClientRankOld = r,
                    Mmr = 1000,
                    MmrOld = 1000,
                });
            }

            int viewerRank = 0;
            if (!string.IsNullOrWhiteSpace(ownerPlayerId))
            {
                var mine = extracted.FirstOrDefault(p =>
                    string.Equals(p.PlayerId, ownerPlayerId, StringComparison.OrdinalIgnoreCase));
                if (mine != null) viewerRank = mine.ClientRank;
            }
            if (viewerRank <= 0)
                viewerRank = ClampClientRank(src.GetOverallInt("ranked_2v2_rank"));
            if (viewerRank <= 0)
                viewerRank = ClampClientRank(src.GetOverallInt("ranked_rank"));

            int winnerTeam = src.GetOverallInt("winner_team");
            if (winnerTeam <= 0)
            {
                if (tr > ct) winnerTeam = 1;
                else if (ct > tr) winnerTeam = 2;
            }

            bool mapLooksAllies = MatchHistoryRules.LooksAllies(mode, map);
            if (mapLooksAllies)
            {
                map = MapNames.ForMode(map, allies: true);
                mode = "Ranked2v2";
            }
            else
            {
                map = MapNames.ForMode(map, allies: false);
                mode = "RankedDefuse";
            }
            // Список 0.17 биндится только на RankedDefuse@10. 1v1/2v2 без паддинга = «список пуст».
            EnsureRosterSize(extracted, "RankedDefuse", ownerPlayerId, viewerRank, tr, ct, winnerTeam);
            var rebuilt = Build(
                src.MatchId,
                map,
                mode,
                src.StartDate,
                src.FinishDate,
                tr,
                ct,
                src.GetOverallInt("is_give_up") != 0,
                extracted,
                viewerRank,
                winnerTeam);
            if (!string.IsNullOrWhiteSpace(ownerPlayerId))
            {
                var mine = extracted.FirstOrDefault(p =>
                    string.Equals(p.PlayerId, ownerPlayerId, StringComparison.OrdinalIgnoreCase));
                if (mine != null)
                {
                    int d = mine.Mmr - mine.MmrOld;
                    int result = MatchHistoryRules.ResultForViewer(tr, ct, mine.Team, winnerTeam, d);
                    rebuilt.UpsertOverallIntPublic("result", result);
                    rebuilt.UpsertOverallIntPublic("win", result == 1 ? 1 : 0);
                    rebuilt.UpsertOverallSigned("mmr_delta", d);
                    rebuilt.UpsertOverallSigned("DeltaMmr", d);
                    rebuilt.UpsertOverallIntPublic("Mmr", Math.Max(0, mine.Mmr));
                }
            }
            // Не паддить ботами: BOT_3/Player3 клиент 0.17 выкидывает весь список истории.
            src.CopyRewardsOnto(rebuilt);
            rebuilt.UseStructuredWire();
            rebuilt.BakeWirePayload();
            return rebuilt;
        }

        /// <summary>
        /// Сколько строк игроков должно быть в матче союзников: реальный размер режима
        /// из админки (1v1 → 2, 2v2 → 4), либо принудительное число из настроек.
        /// </summary>
        private static int AlliesRosterTarget()
        {
            try
            {
                int forced = LocalServerConfig.Current.HistoryRosterAllies;
                if (forced > 0) return forced;
            }
            catch { }
            try
            {
                int required = AlliesMatchmakingConfig.GetRequiredPlayers();
                if (required > 0) return required;
            }
            catch { }
            return 4;
        }

        /// <summary>
        /// Союзники → размер режима (1v1 = 2, 2v2 = 4), RankedDefuse → 10.
        /// Для RankedDefuse паддинг обязателен: клиент 0.17 обнуляет весь список,
        /// если в нём есть матч этого режима меньше чем с пятью игроками.
        /// </summary>
        private static void EnsureRosterSize(
            List<PlayerRow> players,
            string mode,
            string ownerPlayerId,
            int viewerRank,
            int trScore,
            int ctScore,
            int winnerTeam)
        {
            if (players == null) return;
            // RankedDefuse <5 → клиент 0.17 обнуляет весь список.
            // Ranked2v2 <4 → строка списка не биндится (пустое окно, «список пуст» уже скрыт).
            int need = mode == "RankedDefuse" ? 10 : Math.Max(4, AlliesRosterTarget());
            if (players.Count >= need) return;

            int rank = ClampClientRank(viewerRank > 0 ? viewerRank : 1);
            int seed = players.Count;
            while (players.Count < need)
            {
                seed++;
                int team = (players.Count % 2) + 1;
                bool won = winnerTeam > 0
                    ? team == winnerTeam
                    : (team == 1 && trScore >= ctScore) || (team == 2 && ctScore > trScore);
                string botId = Guid.NewGuid().ToString("N").Substring(0, 24);
                string nick = "N" + botId.Substring(0, 8);
                players.Add(new PlayerRow
                {
                    PlayerId = botId,
                    Uid = nick,
                    Name = nick,
                    Avatar = "0",
                    Kills = Math.Max(0, 3 + (seed % 7)),
                    Deaths = Math.Max(0, 2 + (seed % 5)),
                    Assists = seed % 4,
                    Score = 100 + seed * 10,
                    Team = team,
                    Won = won,
                    Mmr = 1000,
                    MmrOld = 1000,
                    ClientRank = rank,
                    ClientRankOld = rank,
                });
            }

            // Владелец должен остаться в ростере (EnsureViewerPlayerRow тоже чинит).
            if (!string.IsNullOrWhiteSpace(ownerPlayerId)
                && !players.Any(p => string.Equals(p.PlayerId, ownerPlayerId, StringComparison.OrdinalIgnoreCase)))
            {
                players[0].PlayerId = ownerPlayerId;
            }
        }

        private static List<PlayerRow> ExtractPlayers(FinishedMatch src)
        {
            var list = new List<PlayerRow>();
            if (src == null) return list;
            try
            {
                var rows = src.SnapshotPlayerRows();
                if (rows == null) return list;
                foreach (var r in rows)
                {
                    if (r == null || string.IsNullOrWhiteSpace(r.Id)) continue;
                    int rank = ClampClientRank(r.GetStat("current_rank"));
                    if (rank <= 0) rank = ClampClientRank(r.GetStat("Rank"));
                    if (rank <= 0) rank = 1;
                    int mmr = r.GetStat("mmr");
                    if (mmr == 0) mmr = r.GetStat("Mmr");
                    int mmrOld = r.GetStat("mmr_old");
                    if (mmrOld == 0)
                    {
                        int delta = r.GetStat("DeltaMmr");
                        if (delta == 0) delta = r.GetStat("mmr_delta");
                        if (delta == 0) delta = r.GetStat("mmr_change");
                        if (delta != 0) mmrOld = mmr - delta;
                        else mmrOld = mmr;
                    }
                    list.Add(new PlayerRow
                    {
                        PlayerId = r.Id,
                        Uid = string.IsNullOrWhiteSpace(r.Uid) ? r.Name : r.Uid,
                        Name = string.IsNullOrWhiteSpace(r.Name) ? r.Uid : r.Name,
                        Avatar = string.IsNullOrWhiteSpace(r.Avatar) ? "0" : r.Avatar,
                        Kills = Math.Max(r.GetStat("kill"), r.GetStat("kills")),
                        Deaths = Math.Max(r.GetStat("death"), r.GetStat("deaths")),
                        Assists = Math.Max(r.GetStat("assist"), r.GetStat("assists")),
                        Score = r.GetStat("score"),
                        Team = r.GetStat("team") <= 0 ? 1 : r.GetStat("team"),
                        Won = r.GetStat("result") != 0 || r.GetStat("win") != 0 || r.GetStat("is_win") != 0,
                        Mmr = mmr,
                        MmrOld = mmrOld,
                        ClientRank = rank,
                        ClientRankOld = rank,
                    });
                }
            }
            catch { }
            return list;
        }

        public static int ClampClientRank(int rank)
        {
            if (rank < 1) return 1;
            // Список истории 0.17 падает на Rank=17 (Legend) — для wire max Elite=16.
            if (rank > 16) return 16;
            return rank;
        }

        public static int ToSafeClientRank(int internalOrClient)
        {
            if (internalOrClient < 0) return 1;
            if (internalOrClient >= 1 && internalOrClient <= 17)
                return internalOrClient;
            return ClientRankDisplay.ToClientRank(Math.Clamp(internalOrClient, 0, 16));
        }

        // Имена карт и режимов живут в MapNames.cs — отдельно и без зависимостей,
        // чтобы их можно было прогнать тестами (tests\MapNames подключает тот файл).
        private static string NormalizeMode(string mode) => MapNames.NormalizeMode(mode);

        private static string NormalizeMap(string map, string mode) => MapNames.ForModeName(map, mode);

        private static FinishedMatch.StatValueRow S(string name, string value) =>
            new FinishedMatch.StatValueRow { Name = name, Type = 2, StringValue = value ?? "" };

        private static FinishedMatch.StatValueRow I(string name, int value) =>
            new FinishedMatch.StatValueRow { Name = name, Type = 0, IntValue = value };

        private static FinishedMatch.StatValueRow Delta(string name, int value) =>
            new FinishedMatch.StatValueRow { Name = name, Type = 0, IntValue = value, LongValue = value };

        private static bool LooksLikeMongoId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 24) return false;
            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }
    }
}
