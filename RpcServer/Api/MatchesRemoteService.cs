using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using MongoDB.Bson;
using Google.Protobuf;
using StandRiseServer.RpcServer;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("MatchesRemoteService")]
    public class MatchesRemoteService : RpcClass
    {
        public MatchesRemoteService(UserService user) : base(user) { }

        public override async Task InvokeAsync(RpcRequest request)
        {
            switch (request.MethodName)
            {
                case "getClanMatches":
                    await GetClanMatches(request.Params.ToArray(), request.Id);
                    break;
                case "getMatch":
                    await GetMatch(request.Params.ToArray(), request.Id);
                    break;
                case "getCurrentPlayerLastMatch":
                    await GetCurrentPlayerLastMatch(request.Params.ToArray(), request.Id);
                    break;
                case "getClanLastMatch":
                    await GetClanLastMatch(request.Params.ToArray(), request.Id);
                    break;
                case "getPlayerMatches":
                    await GetPlayerMatches(request.Params.ToArray(), request.Id);
                    break;
                case "getPlayerLastMatch":
                    await GetPlayerLastMatch(request.Params.ToArray(), request.Id);
                    break;
                default:
                    MethodNotFound(request);
                    break;
            }
        }

        public override void Invoke(RpcRequest request)
        {
            _ = InvokeAsync(request);
        }

        private async Task GetClanMatches(BinaryValue[] values, string guid)
        {
            try
            {
                // Пустой One(FinishedMatch) клиент рисует белым квадратом как «первый матч».
                SendNullList(guid);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetMatch(BinaryValue[] values, string guid)
        {
            try
            {
                string matchId = null;
                if (values != null && values.Length > 0 && values[0] != null && !values[0].IsNull)
                {
                    try { matchId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]); }
                    catch
                    {
                        try
                        {
                            var raw = values[0].One.ToByteArray();
                            if (raw != null && raw.Length > 0)
                            {
                                // Клиент может прислать FinishedMatch / обёртку — достаём MatchId.
                                var fmArg = FinishedMatch.SafeParseFrom(raw);
                                if (fmArg != null && !string.IsNullOrEmpty(fmArg.MatchId))
                                    matchId = fmArg.MatchId;
                                else
                                    matchId = System.Text.Encoding.UTF8.GetString(raw).Trim('\0', ' ', '"');
                            }
                        }
                        catch { }
                    }
                }

                Logger.Log($"[Matches] getMatch id='{matchId ?? ""}'");
                var response = new GetMatchResponse();
                string selfId = null;
                StaticClasses.Users.TryGetValue(_user.TcpClient, out selfId);
                if (!string.IsNullOrWhiteSpace(matchId))
                {
                    matchId = matchId.Trim();
                    matchId = FromHistoryListMatchId(matchId);
                    var payload = BoltGameDatabaseProvider.Instance.GetMatchHistoryPayloadByMatchId(matchId);
                    if (payload != null && payload.Length > 0)
                    {
                        var fm = FinishedMatch.SafeParseFrom(payload);
                        if (fm == null) fm = new FinishedMatch();
                        if (string.IsNullOrEmpty(fm.MatchId))
                            fm.MatchId = BoltGameDatabaseProvider.CanonicalMatchId(matchId);
                        if (fm.FinishDate == 0)
                            fm.FinishDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        fm = PrepareMatchForClient(fm, payload, fm.FinishDate, selfId);
                        response.Match = fm;
                        SendMatchDetail(guid, fm);
                        return;
                    }
                    else
                    {
                        Logger.LogWarn($"[Matches] getMatch: no payload for {matchId}");
                    }
                }
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetCurrentPlayerLastMatch(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }
                var response = new GetCurrentPlayerLastMatchResponse();
                var payload = BoltGameDatabaseProvider.Instance.GetPlayerLastMatchPayload(playerId);
                if (payload != null && payload.Length > 0)
                {
                    try { response.Match = PrepareMatchForClient(FinishedMatch.SafeParseFrom(payload), payload, 0); }
                    catch { }
                }
                if (response.Match != null)
                    SendResponse(guid, response);
                else
                    SendNullList(guid);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetClanLastMatch(BinaryValue[] values, string guid)
        {
            try
            {
                SendNullList(guid);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

		private async Task GetPlayerMatches(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string selfId))
                {
                    SendError(guid, 401);
                    return;
                }

                string targetId = selfId;
                int pageOffset = 0;
                int pageSize = 30;
                if (values != null && values.Length > 0 && values[0] != null && !values[0].IsNull)
                {
                    string parsedId = null;
                    try
                    {
                        if (values[0].One != null && values[0].One.Length > 0)
                        {
                            var input = new CodedInputStream(values[0].One.ToByteArray());
                            uint tag;
                            while ((tag = input.ReadTag()) != 0)
                            {
                                if (tag == 10)
                                    parsedId = input.ReadString();
                                else if (tag == 18)
                                {
                                    var paging = input.ReadBytes();
                                    var pIn = new CodedInputStream(paging.ToByteArray());
                                    uint pt;
                                    while ((pt = pIn.ReadTag()) != 0)
                                    {
                                        if (pt == 8) pageOffset = pIn.ReadInt32();
                                        else if (pt == 16) pageSize = pIn.ReadInt32();
                                        else pIn.SkipLastField();
                                    }
                                }
                                else
                                    input.SkipLastField();
                            }
                        }
                    }
                    catch { }

                    if (string.IsNullOrWhiteSpace(parsedId))
                    {
                        try
                        {
                            parsedId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                        }
                        catch { }
                    }

                    if (!string.IsNullOrWhiteSpace(parsedId))
                        targetId = parsedId.Trim();
                }
                if (pageSize <= 0 || pageSize > 50) pageSize = 30;
                if (pageOffset < 0) pageOffset = 0;
                // Клиент 0.17 часто шлёт (size=20, offset=0) как field1/field2. Если принять
                // field1 как offset, пропускаем все 14 матчей и UI пустой при returned=14.
                if (pageOffset > 0 && pageOffset <= 50 && pageSize <= 1)
                {
                    int maybeSize = pageOffset;
                    pageOffset = pageSize;
                    pageSize = maybeSize;
                    if (pageSize <= 0 || pageSize > 50) pageSize = 30;
                }

                // uid / nickname → ObjectId
                if (!ObjectId.TryParse(targetId, out _))
                {
                    try
                    {
                        var byUid = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(targetId);
                        if (byUid != null && byUid.Length > 0)
                            targetId = byUid[0]._id.ToString();
                        else
                        {
                            var byName = BoltMainDatabaseProvider.Instance.FindPlayersByUidOrName(targetId, 1);
                            if (byName != null && byName.Length > 0)
                                targetId = byName[0]._id.ToString();
                        }
                    }
                    catch { }
                }

                string viewerUid = null;
                string viewerName = null;
                string viewerAvatar = "0";
                try
                {
                    if (ObjectId.TryParse(targetId, out ObjectId targetOid))
                    {
                        var profile = BoltMainDatabaseProvider.Instance.GetPlayerDocument(targetOid);
                        if (profile != null)
                        {
                            viewerUid = profile.uid;
                            viewerName = profile.name;
                            if (!string.IsNullOrWhiteSpace(profile.avatarId))
                                viewerAvatar = FinishedMatch.SanitizeAvatarId(profile.avatarId);
                        }
                    }
                }
                catch { }

                var response = new GetPlayerMatchesResponse();
                int fetchLimit = Math.Min(pageOffset + pageSize, 50);
                var entries = BoltGameDatabaseProvider.Instance.GetPlayerMatchHistoryEntries(targetId, fetchLimit);
                int added = 0;
            // Compact: RankedDefuse@10 + карта « 2x2» у союзников (Ranked2v2@4 = префаб).
                foreach (var entry in entries.Skip(pageOffset).Take(pageSize))
                {
                    if (entry.payload == null || entry.payload.Length == 0) continue;
                    if (added >= pageSize) break;
                    try
                    {
                        var legacy = FinishedMatch.SafeParseFrom(entry.payload);
                        if (legacy == null) continue;
                        if (string.IsNullOrEmpty(legacy.MatchId) && !string.IsNullOrEmpty(entry.MatchId))
                            legacy.MatchId = entry.MatchId;
                        legacy.NormalizeClientDates();
                        if (legacy.FinishDate == 0 && entry.FinishDate != 0)
                            legacy.FinishDate = FinishedMatch.ClientDateToUnixMs(entry.FinishDate);

                        string publicId = entry.MatchId;
                        if (!IsAllDigits(publicId))
                        {
                            publicId = BoltGameDatabaseProvider.Instance.FindPublicMatchId(
                                string.IsNullOrEmpty(legacy.MatchId) ? entry.MatchId : legacy.MatchId);
                        }
                        if (!IsAllDigits(publicId))
                        {
                            publicId = EnsureMatchId32(
                                string.IsNullOrEmpty(legacy.MatchId) ? entry.MatchId : legacy.MatchId);
                        }
                        var fm = MatchHistoryBuilder.RebuildFromLegacy(legacy, targetId);
                        string mode = fm.GetOverallString("mode") ?? "Ranked2v2";
                        string map = fm.GetOverallString("map") ?? fm.GetOverallString("level") ?? "Province";
                        fm.UpsertOverallStringPublic("map", map);
                        fm.UpsertOverallStringPublic("level", map);
                        fm.UpsertOverallStringPublic("mode", mode);
                        MatchHistoryBuilder.StampForClient(fm, mode);
                        fm.MatchId = publicId;
                        fm.EnsureViewerPlayerRow(targetId, viewerUid, viewerName, viewerAvatar);
                        fm.CompactForHistoryList();
                        bool alliesRow = MatchHistoryRules.LooksAllies(mode, map)
                            || MatchHistoryRules.LooksAllies(fm.GetOverallString("mode"), fm.GetOverallString("map"));
                        string displayMap = MapNames.ForMode(
                            fm.GetOverallString("map") ?? fm.GetOverallString("level") ?? map,
                            alliesRow);
                        fm.UpsertOverallStringPublic("map", displayMap);
                        fm.UpsertOverallStringPublic("level", displayMap);
                        if (!fm.IsClientCanceled)
                        {
                        int viewerResult = 0;
                        int viewerDelta = 0;
                        int viewerTeam = 1;
                        foreach (var snap in fm.SnapshotPlayerRows())
                        {
                            if (snap == null) continue;
                            if (!string.Equals(snap.Id, targetId, StringComparison.OrdinalIgnoreCase)) continue;
                            viewerResult = snap.GetStat("result");
                            viewerDelta = snap.GetStat("DeltaMmr");
                            if (viewerDelta == 0) viewerDelta = snap.GetStat("mmr_delta");
                            int t = snap.GetStat("team");
                            if (t > 0) viewerTeam = t;
                            break;
                        }
                        int trScore = fm.GetOverallInt("score1");
                        if (trScore == 0) trScore = fm.GetOverallInt("TrScore");
                        int ctScore = fm.GetOverallInt("score2");
                        if (ctScore == 0) ctScore = fm.GetOverallInt("CtScore");
                        int winnerTeam = fm.GetOverallInt("winner_team");
                        viewerResult = MatchHistoryRules.ResultForViewer(
                            trScore, ctScore, viewerTeam, winnerTeam, viewerDelta);
                        fm.UpsertOverallIntPublic("result", viewerResult);
                        fm.UpsertOverallIntPublic("win", viewerResult == 1 ? 1 : 0);
                        fm.UpsertOverallSigned("mmr_delta", viewerDelta);
                        fm.UpsertOverallSigned("DeltaMmr", viewerDelta);
                        }
                        fm.UseStructuredWire();
                        fm.NormalizeClientDates();
                        long finish = FinishedMatch.ClientDateToUnixMs(entry.FinishDate);
                        if (finish <= 0)
                            finish = FinishedMatch.ClientDateToUnixMs(legacy.FinishDate);
                        if (finish <= 0)
                            finish = FinishedMatch.ClientDateToUnixMs(fm.FinishDate);
                        if (finish <= 0)
                            finish = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        fm.FinishDate = finish;
                        fm.StartDate = finish - 600_000L;
                        fm.MatchId = ToHistoryListMatchId(publicId);
                        fm.BakeWirePayload();
                        response.Matches.Add(fm);
                        added++;
                        if (added <= 12)
                        {
                            // Ровно то, что клиент рисует в строке истории: карта, счёт,
                            // статус и дельта MMR. Если на экране пусто — видно, чего не хватает.
                            int rowTr = fm.GetOverallInt("TrScore");
                            if (rowTr == 0) rowTr = fm.GetOverallInt("score1");
                            int rowCt = fm.GetOverallInt("CtScore");
                            if (rowCt == 0) rowCt = fm.GetOverallInt("score2");
                            int rowDelta = 0, rowResult = 0;
                            foreach (var snap in fm.SnapshotPlayerRows())
                            {
                                if (snap == null) continue;
                                if (!string.Equals(snap.Id, targetId, StringComparison.OrdinalIgnoreCase)) continue;
                                rowDelta = snap.GetStat("DeltaMmr");
                                rowResult = snap.GetStat("result");
                                break;
                            }
                            int rowGiveUp = fm.GetOverallInt("is_give_up");
                            string wireMode = fm.GetOverallString("mode") ?? mode;
                            string wireMap = fm.GetOverallString("map") ?? map;
                            var uids = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                            foreach (var snap in fm.SnapshotPlayerRows())
                            {
                                if (snap == null) continue;
                                string u = !string.IsNullOrEmpty(snap.Uid) ? snap.Uid : snap.Id;
                                if (!string.IsNullOrEmpty(u)) uids.Add(u);
                            }
                            long finishUnix = FinishedMatch.ClientDateToUnixMs(fm.FinishDate);
                            string msk = "";
                            try
                            {
                                msk = DateTimeOffset.FromUnixTimeMilliseconds(finishUnix)
                                    .ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm");
                            }
                            catch { msk = finishUnix.ToString(); }
                            Logger.Log($"[Matches]   #{added} id='{fm.MatchId}' map='{wireMap}' mode='{wireMode}' "
                                + $"type={fm.MatchType} season='{fm.SeasonId}' score={rowTr}-{rowCt} result={rowResult} "
                                + $"deltaMmr={rowDelta} canceled={fm.IsClientCanceled} giveUp={rowGiveUp} "
                                + $"finish={finishUnix} msk={msk} "
                                + $"players={fm.PlayerRowCount} uniqueUids={uids.Count}");
                        }
                    }
                    catch (System.Exception parseEx)
                    {
                        Logger.LogWarn($"[Matches] getPlayerMatches parse skip: {parseEx.Message}");
                    }
                }

                Logger.Log($"[Matches] getPlayerMatches self={selfId} target={targetId} offset={pageOffset} size={pageSize} returned={added} db={entries.Count}");
                if (added == 0)
                {
                    SendNullList(guid);
                    return;
                }
                response.PageOffset = pageOffset;
                response.PageSize = added;
                WireDumped.Clear();
                DumpWireOnce("getPlayerMatches", targetId, response);
                SendMatchList(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetPlayerLastMatch(BinaryValue[] values, string guid)
        {
            try
            {
                string targetId = null;
                if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string selfId))
                    targetId = selfId;
                if (values != null && values.Length > 0 && values[0] != null && !values[0].IsNull)
                {
                    try
                    {
                        string parsed = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                        if (!string.IsNullOrWhiteSpace(parsed)) targetId = parsed;
                    }
                    catch { }
                }
                if (!string.IsNullOrWhiteSpace(targetId) && !ObjectId.TryParse(targetId, out _))
                {
                    try
                    {
                        var byUid = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(targetId);
                        if (byUid != null && byUid.Length > 0)
                            targetId = byUid[0]._id.ToString();
                        else
                        {
                            var byName = BoltMainDatabaseProvider.Instance.FindPlayersByUidOrName(targetId, 1);
                            if (byName != null && byName.Length > 0)
                                targetId = byName[0]._id.ToString();
                        }
                    }
                    catch { }
                }

                string viewerUid = null;
                string viewerName = null;
                try
                {
                    if (ObjectId.TryParse(targetId, out ObjectId targetOid))
                    {
                        var profile = BoltMainDatabaseProvider.Instance.GetPlayerDocument(targetOid);
                        if (profile != null)
                        {
                            viewerUid = profile.uid;
                            viewerName = profile.name;
                        }
                    }
                }
                catch { }

                var response = new GetPlayerLastMatchResponse();
                var payload = BoltGameDatabaseProvider.Instance.GetPlayerLastMatchPayload(targetId);
                if (payload != null && payload.Length > 0)
                {
                    try
                    {
                        var fm = PrepareMatchForClient(FinishedMatch.SafeParseFrom(payload), payload, 0);
                        fm.EnsureViewerPlayerRow(targetId, viewerUid, viewerName);
                        response.Match = fm;
                    }
                    catch { }
                }

                Logger.Log($"[Matches] getPlayerLastMatch target={targetId} hasMatch={response.Match != null}");
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private static FinishedMatch PrepareMatchForClient(FinishedMatch fm, byte[] payload, long finishDate, string ownerPlayerId = null)
        {
            if (fm == null) fm = new FinishedMatch();
            if (string.IsNullOrEmpty(fm.MatchId) && payload != null)
            {
                try { fm = FinishedMatch.SafeParseFrom(payload) ?? fm; } catch { }
            }
            string publicId = null;
            if (!string.IsNullOrEmpty(fm.MatchId))
                publicId = BoltGameDatabaseProvider.Instance.FindPublicMatchId(fm.MatchId);
            if (!IsAllDigits(publicId))
                publicId = EnsureMatchId32(string.IsNullOrEmpty(fm.MatchId) ? "" : fm.MatchId);
            fm.MatchId = publicId;
            fm.NormalizeClientDates();
            if (fm.FinishDate == 0 && finishDate != 0)
                fm.FinishDate = FinishedMatch.ClientDateToUnixMs(finishDate);
            fm = MatchHistoryBuilder.RebuildFromLegacy(fm, ownerPlayerId);
            fm.MatchId = publicId;
            MatchHistoryBuilder.StampForClient(fm);
            if (!string.IsNullOrWhiteSpace(ownerPlayerId))
                fm.EnsureViewerPlayerRow(ownerPlayerId);
            // Список и деталь: CompactForHistoryList (союзники 2x2 / соревы 5v5).
            fm.CompactForHistoryList();
            fm.MatchId = MatchHistoryRules.ToWireMatchId(
                IsAllDigits(publicId) ? publicId : (publicId ?? fm.MatchId));
            fm.UseStructuredWire();
            fm.NormalizeClientDates();
            fm.BakeWirePayload();
            Logger.Log($"[Matches] prepare detail id='{fm.MatchId}' public='{publicId}' map='{fm.GetOverallString("map")}' "
                + $"score={fm.GetOverallInt("score1")}-{fm.GetOverallInt("score2")} "
                + $"result={fm.GetOverallInt("result")} players={fm.PlayerRowCount}");
            return fm;
        }

        /// <summary>
        /// Клиент 0.17: 32 hex ИЛИ цифровой id «1», «2», … Цифры не хешируем.
        /// </summary>
        private static string EnsureMatchId32(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) raw = "";
            raw = raw.Trim();
            if (IsAllDigits(raw)) return raw;
            string canon = BoltGameDatabaseProvider.CanonicalMatchId(raw);
            if (!string.IsNullOrEmpty(canon) && canon.Length == 32)
            {
                bool hex = true;
                foreach (char c in canon)
                {
                    bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                    if (!ok) { hex = false; break; }
                }
                if (hex) return canon.ToLowerInvariant();
            }
            if (IsAllDigits(canon)) return canon;
            string seed = string.IsNullOrEmpty(canon) ? (raw.Length > 0 ? raw : Guid.NewGuid().ToString("N")) : canon;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
                var sb = new StringBuilder(32);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>В proto — Guid с закодированным номером 1, 2, 3…</summary>
        private static string ToHistoryListMatchId(string publicId) =>
            MatchHistoryRules.ToWireMatchId(publicId);

        /// <summary>getMatch присылает 32 hex с ведущими нулями — обратно в «2».</summary>
        private static string FromHistoryListMatchId(string id) =>
            MatchHistoryRules.FromListMatchId(id);

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (c < '0' || c > '9') return false;
            return s.Length > 0 && s.Length <= 18 && !(s.Length > 1 && s[0] == '0');
        }

        // Профиль показывает пустой список при returned>0 — значит клиент не принимает
        // наш ответ. Один раз на игрока за запуск сервера пишем точные байты ответа,
        // чтобы разобрать формат по факту, а не гадать.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> WireDumped =
            new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();

        private static void DumpWireOnce(string tag, string targetId, IMessage response)
        {
            try
            {
                string key = tag + ":" + (targetId ?? "");
                if (!WireDumped.TryAdd(key, 1)) return;
                byte[] raw = response.ToByteString().ToByteArray();
                Logger.Log($"[Matches] WIRE {tag} target={targetId} bytes={raw.Length} b64={System.Convert.ToBase64String(raw)}");
            }
            catch (System.Exception ex)
            {
                Logger.LogWarn($"[Matches] wire dump failed: {ex.Message}");
            }
        }

        private void SendMatchDetail(string guid, FinishedMatch fm)
        {
            byte[] raw = (fm != null && fm.WirePayload != null && fm.WirePayload.Length > 0)
                ? fm.WirePayload
                : fm?.ToByteArray();
            var bv = new BinaryValue { IsNull = false };
            if (raw != null && raw.Length > 0)
            {
                // Клиент getMatch читает One как FinishedMatch (field1=string matchId).
                // GetMatchResponse.field1=message на том же теге → «ПОВТОРИТЬ».
                // Array не дублируем — иначе наложение/тестовая обложка как у списка.
                bv.One = Google.Protobuf.ByteString.CopyFrom(raw);
            }
            Logger.Log($"[Matches] sendMatchDetail id='{fm?.MatchId}' bytes={raw?.Length ?? 0}");
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = bv
                }
            });
        }

        private void SendResponse(string guid, IMessage response)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { One = response.ToByteString() }
                }
            });
        }

        private void SendNullList(string guid)
        {
            Logger.Log("[Matches] sendNullList IsNull=true");
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { IsNull = true }
                }
            });
        }

        /// <summary>
        /// getPlayerMatches возвращает FinishedMatch[]. RPC кладёт каждый матч в Array.
        /// One-wrapper клиент не разворачивает в строки — одна ячейка-префаб Sandstone 9:9.
        /// </summary>
        private void SendMatchList(string guid, GetPlayerMatchesResponse wrapper)
        {
            if (wrapper == null) wrapper = new GetPlayerMatchesResponse();
            var matches = new FinishedMatch[wrapper.Matches.Count];
            for (int i = 0; i < wrapper.Matches.Count; i++)
                matches[i] = wrapper.Matches[i];
            var bv = new ToByteMethod(typeof(FinishedMatch[])).ToBytes(matches);
            if (bv == null) bv = new BinaryValue { IsNull = false };
            bv.One = Google.Protobuf.ByteString.Empty;
            Logger.Log($"[Matches] sendMatchList count={matches.Length} array={bv.Array.Count} oneBytes={bv.One?.Length ?? 0}");
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = bv
                }
            });
        }

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in MatchesRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                    {
                        Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                        Code = code
                    }
                }
            });
        }
    }
}
