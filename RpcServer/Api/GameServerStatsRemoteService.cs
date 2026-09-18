using System;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main.PlayerStats;
using scg = System.Collections.Generic;
using StandRiseServer.RpcServer.Security;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("GameServerStatsRemoteService")]
    public class GameServerStatsRemoteService : RpcClass
    {
        public GameServerStatsRemoteService(UserService user) : base(user) { }

        public override async Task InvokeAsync(RpcRequest request)
        {
            string methodName = request.MethodName.ToLowerInvariant();
            switch (methodName)
            {
                case "getstats":
                    await GetStats(request.Params.ToArray(), request.Id);
                    break;
                case "getcurrentstats":
                    await GetCurrentStats(request.Params.ToArray(), request.Id);
                    break;
                case "storeplayersstats":
                    await StorePlayersStats(request.Params.ToArray(), request.Id, respondWithEmptyMessage: false);
                    break;
                case "storeplayersstats2":
                    await StorePlayersStats(request.Params.ToArray(), request.Id, respondWithEmptyMessage: true);
                    break;
                case "getplayersstats":
                    // New clients send one protobuf request; legacy clients send raw params array.
                    if (request.Params.Count == 1 && request.Params[0].One != null)
                        await GetPlayersStatsGeneric(request.Params.ToArray(), request.Id);
                    else
                        await GetPlayersStatsSimple(request.Params.ToArray(), request.Id);
                    break;
                case "getplayerstats":
                    await GetPlayerStats(request.Params.ToArray(), request.Id);
                    break;
                case "storeplayerstats":
                    await StorePlayerStats(request.Params.ToArray(), request.Id, respondWithEmptyMessage: false);
                    break;
                case "storeplayerstats2":
                    await StorePlayerStats(request.Params.ToArray(), request.Id, respondWithEmptyMessage: true);
                    break;
                case "storestats":
                case "storestats2":
                    await StoreStats(request.Params.ToArray(), request.Id);
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

        private async Task GetStats(BinaryValue[] values, string guid)
        {
            try
            {
                string playerId = string.Empty;
                string[] apiNames = Array.Empty<string>();
                bool requestStyle = false;

                // Legacy signature: GetStats(string playerId, string[] apiNames)
                if (values != null && values.Length >= 2)
                {
                    try
                    {
                        playerId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                        apiNames = (string[])new FromByteMethod(typeof(string[])).FromBytes(values[1]);
                    }
                    catch
                    {
                        // Fallback to message parsing below.
                    }
                }

                // Some builds can send a single protobuf request payload.
                if (string.IsNullOrWhiteSpace(playerId) && values != null && values.Length >= 1 && values[0]?.One != null)
                {
                    try
                    {
                        var request = GetPlayerStatsRequest.Parser.ParseFrom(values[0].One.ToByteArray());
                        playerId = request.PlayerId;
                        apiNames = request.ApiNames.ToArray();
                        requestStyle = true;
                    }
                    catch
                    {
                        // Keep defaults; handled below.
                    }
                }

                playerId = ResolvePlayerIdOrCurrent(playerId);
                if (string.IsNullOrWhiteSpace(playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                apiNames = ResolveApiNames(playerId, apiNames);
                Logger.Debug($"[GameServerStats] getStats playerId={playerId}, apiNames={apiNames.Length}");
                var storeStats = BoltGameDatabaseProvider.Instance.GetPlayerStats(playerId, apiNames);
                var stats = ConvertStoreStatsToStats(storeStats);
                if (requestStyle)
                {
                    SendWrappedStatsResponse(guid, stats);
                }
                else
                {
                    SendResponse(guid, stats);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task StorePlayersStats(BinaryValue[] values, string guid, bool respondWithEmptyMessage)
        {
            try
            {
                var request = StorePlayersStatsRequest.Parser.ParseFrom(values[0].One.ToByteArray());
                foreach (var playerStats in request.StorePlayersStats)
                {
                    if (!IsPlayerInActiveMatch(playerStats.PlayerId, out string reason))
                    {
                        Logger.LogWarn($"[GameServerStats] storePlayersStats: skipping stats for {playerStats.PlayerId} (closed/missing match: {reason})");
                        continue;
                    }
                    BoltGameDatabaseProvider.Instance.StorePlayerStats(playerStats.PlayerId, playerStats.Stats);
                }
                SendResponseCompat(guid, respondWithEmptyMessage);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetCurrentStats(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                var statsDoc = BoltGameDatabaseProvider.Instance.GetOrCreatePlayerStatsDocument(playerId);
                var stats = new Stats();
                if (statsDoc?.stats != null)
                {
                    foreach (var bsonElement in statsDoc.stats)
                    {
                        if (bsonElement.Value.IsInt32 || bsonElement.Value.IsInt64 || bsonElement.Value.IsDouble || bsonElement.Value.IsString)
                        {
                            stats.Stat.Add(bsonElement.GetPlayerStat());
                        }
                    }
                }
                stats.Stat.AppendAliases();
                stats.EnsureLiveSeasonStat(3);
                stats.Stat.ApplyClientRankDisplayOffset();

                var response = new GetCurrentStatsResponse
                {
                    Stats = stats
                };
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetPlayersStatsSimple(BinaryValue[] values, string guid)
        {
            try
            {
                string[] playerIds = (string[])new FromByteMethod(typeof(string[])).FromBytes(values[0]);
                string[] apiNames = (string[])new FromByteMethod(typeof(string[])).FromBytes(values[1]);

                playerIds = NormalizePlayerIds(playerIds);
                if (playerIds.Length == 0 && TryGetCurrentPlayerId(out var currentPlayerId))
                {
                    playerIds = new[] { currentPlayerId };
                }

                var results = playerIds.Select(id =>
                {
                    var resolvedPlayerId = ResolvePlayerIdOrCurrent(id);
                    var resolvedApiNames = ResolveApiNames(resolvedPlayerId, apiNames);
                    var sps = new StorePlayerStats { PlayerId = resolvedPlayerId };
                    sps.Stats.Add(BoltGameDatabaseProvider.Instance.GetPlayerStats(resolvedPlayerId, resolvedApiNames));
                    return sps;
                }).ToArray();
                
                SendResponse(guid, results);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetPlayerStats(BinaryValue[] values, string guid)
        {
            try
            {
                string requestedPlayerId = string.Empty;
                string[] requestedApiNames = Array.Empty<string>();

                if (values != null && values.Length > 0 && values[0]?.One != null)
                {
                    var request = GetPlayerStatsRequest.Parser.ParseFrom(values[0].One.ToByteArray());
                    requestedPlayerId = request.PlayerId;
                    requestedApiNames = request.ApiNames.ToArray();
                }
                else if (values != null && values.Length > 0)
                {
                    // Legacy fallback if payload is sent as separate params.
                    requestedPlayerId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                    if (values.Length > 1)
                    {
                        requestedApiNames = (string[])new FromByteMethod(typeof(string[])).FromBytes(values[1]);
                    }
                }

                var resolvedPlayerId = ResolvePlayerIdOrCurrent(requestedPlayerId);
                if (string.IsNullOrWhiteSpace(resolvedPlayerId))
                {
                    SendError(guid, 401);
                    return;
                }

                var resolvedApiNames = ResolveApiNames(resolvedPlayerId, requestedApiNames);
                Logger.Debug($"[GameServerStats] getPlayerStats playerId={resolvedPlayerId}, apiNames={resolvedApiNames.Length}");
                var stats = BoltGameDatabaseProvider.Instance.GetPlayerStats(resolvedPlayerId, resolvedApiNames);
                var response = new GetPlayerStatsResponse
                {
                    PlayerStats = new Axlebolt.Bolt.Protobuf.PlayerStats
                    {
                        PlayerId = resolvedPlayerId
                    }
                };
                response.PlayerStats.Stats.Add(stats);
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetPlayersStatsGeneric(BinaryValue[] values, string guid)
        {
            try
            {
                var request = GetPlayersStatsRequest.Parser.ParseFrom(values[0].One.ToByteArray());
                var response = new GetPlayersStatsResponse();
                var playerIds = NormalizePlayerIds(request.PlayerIds);
                if (playerIds.Length == 0 && TryGetCurrentPlayerId(out var currentPlayerId))
                {
                    playerIds = new[] { currentPlayerId };
                }

                foreach (var playerId in playerIds)
                {
                    var resolvedPlayerId = ResolvePlayerIdOrCurrent(playerId);
                    var resolvedApiNames = ResolveApiNames(resolvedPlayerId, request.ApiNames);
                    Logger.Debug($"[GameServerStats] getPlayersStats playerId={resolvedPlayerId}, apiNames={resolvedApiNames.Length}");
                    var playerStats = new Axlebolt.Bolt.Protobuf.PlayerStats
                    {
                        PlayerId = resolvedPlayerId,
                    };
                    playerStats.Stats.Add(BoltGameDatabaseProvider.Instance.GetPlayerStats(resolvedPlayerId, resolvedApiNames));
                    response.PlayersStats.Add(playerStats);
                }
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task StorePlayerStats(BinaryValue[] values, string guid, bool respondWithEmptyMessage)
        {
            try
            {
                Axlebolt.Bolt.Protobuf.StorePlayerStats payload = null;

                if (values != null && values.Length > 0 && values[0]?.One != null)
                {
                    var request = StorePlayerStatsRequest.Parser.ParseFrom(values[0].One.ToByteArray());
                    payload = request.StorePlayerStats;
                    if (payload == null && (!string.IsNullOrWhiteSpace(request.PlayerId) || request.Stats.Count > 0))
                    {
                        payload = new Axlebolt.Bolt.Protobuf.StorePlayerStats
                        {
                            PlayerId = request.PlayerId
                        };
                        payload.Stats.Add(request.Stats);
                    }
                }
                else if (values != null && values.Length > 0)
                {
                    string playerId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                    payload = new Axlebolt.Bolt.Protobuf.StorePlayerStats
                    {
                        PlayerId = playerId
                    };

                    if (values.Length > 1)
                    {
                        StorePlayerStat[] legacyStats = (StorePlayerStat[])new FromByteMethod(typeof(StorePlayerStat[])).FromBytes(values[1]);
                        if (legacyStats != null)
                        {
                            payload.Stats.Add(legacyStats);
                        }
                    }
                }

                if (payload != null)
                {
                    payload.PlayerId = ResolvePlayerIdOrCurrent(payload.PlayerId);
                    if (IsPlayerInActiveMatch(payload.PlayerId, out string reason))
                    {
                        BoltGameDatabaseProvider.Instance.StorePlayerStats(payload.PlayerId, payload.Stats);
                    }
                    else
                    {
                        Logger.LogWarn($"[GameServerStats] storePlayerStats: skipping stats for {payload.PlayerId} (closed/missing match: {reason})");
                    }
                }
                SendResponseCompat(guid, respondWithEmptyMessage);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task StoreStats(BinaryValue[] values, string guid)
        {
            try
            {
                StorePlayerStats[] stats = null;
                bool requestStyle = false;

                if (values != null && values.Length > 0 && values[0]?.One != null)
                {
                    requestStyle = TryParseStoreStatsRequest(values[0].One.ToByteArray(), out stats);
                }

                if (stats == null && values != null && values.Length > 0)
                {
                    stats = (StorePlayerStats[])new FromByteMethod(typeof(StorePlayerStats[])).FromBytes(values[0]);
                }

                var db = BoltGameDatabaseProvider.Instance;
                foreach (var playerStats in stats ?? Array.Empty<StorePlayerStats>())
                {
                    if (playerStats == null || string.IsNullOrWhiteSpace(playerStats.PlayerId))
                    {
                        continue;
                    }

                    // Bug 17: don't persist stats for a player who is no longer in an
                    // active match. After the room is closed the cached PlayInGame.photonGame
                    // is cleared (see MatchmakingRemoteService.LeaveLobbyInternal and the
                    // stale-status branch in GetLobby), so this also covers late stat pushes
                    // arriving after the game server tore down the room.
                    if (!IsPlayerInActiveMatch(playerStats.PlayerId, out string skipReason))
                    {
                        Logger.LogWarn($"[GameServerStats] storeStats: skipping stats for {playerStats.PlayerId} (closed/missing match: {skipReason})");
                        continue;
                    }

                    // Bug 26: a single bad stat or transient mongo error for ONE player must
                    // not abort the whole batch (which would surface to the client as a 500
                    // and roll back level_xp / level_id for everyone else in the match).
                    // Validation, store-safe filtering and the per-player persist+integrity
                    // refresh are now wrapped so other players' progression still goes through.
                    try
                    {
                        var statsDocument = db.GetOrCreatePlayerStatsDocument(playerStats.PlayerId);
                        bool validated = StatsIntegrityGuard.TryValidateStoreStats(playerStats.PlayerId, statsDocument?.stats, playerStats.Stats, out string rejectReason);
                        if (!validated)
                        {
                            Logger.LogWarn($"[SECURITY] storeStats suspicious payload for {playerStats.PlayerId}: {rejectReason}. Applying safe values.");
                        }

                        var safeStats = new Google.Protobuf.Collections.RepeatedField<StorePlayerStat>();
                        foreach (var stat in playerStats.Stats)
                        {
                            if (IsStoreStatPayloadSafe(stat))
                            {
                                safeStats.Add(stat);
                            }
                        }

                        db.StorePlayerStats(playerStats.PlayerId, safeStats);
                        try
                        {
                            db.RefreshStatsIntegrityHash(playerStats.PlayerId);
                        }
                        catch (System.Exception hashEx)
                        {
                            // The integrity refresh failing must not unwind the stat writes
                            // we just persisted - the next legitimate storeStats call will
                            // re-hash on its own. Without this, mode-specific quirks in the
                            // integrity calc (e.g. unicode mode names) would otherwise nuke
                            // the whole storeStats response with a 500.
                            Logger.LogWarn($"[GameServerStats] storeStats: integrity refresh failed for {playerStats.PlayerId}: {hashEx.Message}");
                        }
                    }
                    catch (System.Exception perPlayerEx)
                    {
                        Logger.LogWarn($"[GameServerStats] storeStats: failed to persist stats for {playerStats.PlayerId}: {perPlayerEx.Message}");
                    }
                }

                SendResponseCompat(guid, requestStyle);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private static bool TryParseStoreStatsRequest(byte[] payload, out StorePlayerStats[] stats)
        {
            stats = null;
            if (payload == null || payload.Length == 0)
            {
                return false;
            }

            try
            {
                // StoreStatsRequest in 0.19 has repeated StorePlayerStats on field #1,
                // wire-compatible with StorePlayersStatsRequest.
                var request = StorePlayersStatsRequest.Parser.ParseFrom(payload);
                if (request?.StorePlayersStats != null && request.StorePlayersStats.Count > 0)
                {
                    stats = request.StorePlayersStats.ToArray();
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        // Bug 17: returns false when the subject player has no active photon match.
        // Stats from rooms that have already been torn down should not be persisted,
        // otherwise late pushes from the game server (or replays of an old request
        // after the player has left/reconnected) keep mutating their progression.
        // Game-server-originated calls are still trusted, but callers from a plain
        // client TcpClient must also have an active in-game session for the subject.
        private bool IsPlayerInActiveMatch(string playerId, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(playerId))
            {
                reason = "missing player id";
                return false;
            }

            if (!StaticClasses.PlayersStatus.TryGetValue(playerId, out PlayerStatus status) || status == null)
            {
                reason = "no cached PlayerStatus";
                return IsCallerTrustedGameServer();
            }

            if (status.playInGame == null || status.playInGame.photonGame == null)
            {
                reason = "photon match cleared";
                return IsCallerTrustedGameServer();
            }

            return true;
        }

        private bool IsCallerTrustedGameServer()
        {
            try
            {
                return _user != null && _user.TcpClient != null && StaticClasses.GameServerUsers.Contains(_user.TcpClient);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsStoreStatPayloadSafe(StorePlayerStat stat)
        {
            if (stat == null || string.IsNullOrWhiteSpace(stat.Name))
            {
                return false;
            }

            if (stat.Name.StartsWith("_srv_", StringComparison.Ordinal))
            {
                return false;
            }

            if (float.IsNaN(stat.StoreFloat) || float.IsInfinity(stat.StoreFloat))
            {
                return false;
            }

            return stat.StoreInt >= 0 && stat.StoreLong >= 0 && stat.StoreFloat >= 0f;
        }

        private void SendResponseCompat(string guid, bool sendEmptyMessage)
        {
            if (sendEmptyMessage)
            {
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new BinaryValue { One = ByteString.Empty }
                    }
                });
                return;
            }

            SendResponse(guid);
        }

        private void SendWrappedStatsResponse(string guid, Stats stats)
        {
            stats ??= new Stats();
            byte[] statsBytes = stats.ToByteArray();

            byte[] wrappedBytes;
            using (var stream = new System.IO.MemoryStream())
            {
                var output = new CodedOutputStream(stream);
                output.WriteTag(1, WireFormat.WireType.LengthDelimited);
                output.WriteBytes(ByteString.CopyFrom(statsBytes));
                output.Flush();
                wrappedBytes = stream.ToArray();
            }

            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { One = ByteString.CopyFrom(wrappedBytes) }
                }
            });
        }

        private void SendResponse(string guid, object response = null)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = response != null ? (response is IMessage message ? new BinaryValue { One = message.ToByteString() } : ProtoReflectionUtils.CreateToByteMethod(response.GetType()).ToBytes(response)) : new BinaryValue { IsNull = true }
                }
            });
        }

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in GameServerStatsRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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

        private static Stats ConvertStoreStatsToStats(Google.Protobuf.Collections.RepeatedField<StorePlayerStat> storeStats)
        {
            var stats = new Stats();
            if (storeStats == null)
            {
                return stats;
            }

            foreach (var storeStat in storeStats)
            {
                if (storeStat == null || string.IsNullOrWhiteSpace(storeStat.Name))
                {
                    continue;
                }

                var playerStat = new PlayerStat
                {
                    Name = storeStat.Name
                };

                if (ShouldUseIntegralEncoding(storeStat.Name))
                {
                    long integral = storeStat.StoreLong != 0L ? storeStat.StoreLong : storeStat.StoreInt;
                    playerStat.Type = StatDefType.Int;
                    playerStat.LongValue = integral;
                    playerStat.IntValue = ToInt32Safe(integral);
                }
                else
                {
                    float floatValue = storeStat.StoreFloat;
                    if (floatValue == 0f)
                    {
                        if (storeStat.StoreLong != 0L) floatValue = storeStat.StoreLong;
                        else if (storeStat.StoreInt != 0) floatValue = storeStat.StoreInt;
                    }

                    playerStat.Type = StatDefType.Float;
                    playerStat.FloatValue = floatValue;
                }

                stats.Stat.Add(playerStat);
            }

            return stats;
        }

        private static int ToInt32Safe(long value)
        {
            if (value > int.MaxValue) return int.MaxValue;
            if (value < int.MinValue) return int.MinValue;
            return (int)value;
        }

        private static bool ShouldUseIntegralEncoding(string statName)
        {
            if (string.IsNullOrWhiteSpace(statName))
            {
                return false;
            }

            return statName.EndsWith("_mmr", StringComparison.OrdinalIgnoreCase) ||
                   statName.EndsWith("_rank", StringComparison.OrdinalIgnoreCase) ||
                   statName.EndsWith("_matches", StringComparison.OrdinalIgnoreCase) ||
                   statName.EndsWith("_count", StringComparison.OrdinalIgnoreCase) ||
                   statName.EndsWith("_xp", StringComparison.OrdinalIgnoreCase) ||
                   statName.EndsWith("_time", StringComparison.OrdinalIgnoreCase) ||
                   statName.EndsWith("_id", StringComparison.OrdinalIgnoreCase) ||
                   statName.EndsWith("_until", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetCurrentPlayerId(out string playerId)
        {
            playerId = null;
            return StaticClasses.Users.TryGetValue(_user.TcpClient, out playerId) &&
                   !string.IsNullOrWhiteSpace(playerId);
        }

        private string ResolvePlayerIdOrCurrent(string requestedPlayerId)
        {
            if (!string.IsNullOrWhiteSpace(requestedPlayerId))
            {
                return requestedPlayerId.Trim();
            }

            return TryGetCurrentPlayerId(out var currentPlayerId) ? currentPlayerId : string.Empty;
        }

        private string[] ResolveApiNames(string playerId, scg::IEnumerable<string> requestedApiNames)
        {
            var normalized = NormalizeApiNames(requestedApiNames);
            if (normalized.Length > 0)
            {
                return normalized;
            }

            if (string.IsNullOrWhiteSpace(playerId))
            {
                return Array.Empty<string>();
            }

            var statsDocument = BoltGameDatabaseProvider.Instance.GetOrCreatePlayerStatsDocument(playerId);
            if (statsDocument?.stats == null)
            {
                return Array.Empty<string>();
            }

            return NormalizeApiNames(statsDocument.stats.Names);
        }

        private static string[] NormalizePlayerIds(scg::IEnumerable<string> playerIds)
        {
            if (playerIds == null)
            {
                return Array.Empty<string>();
            }

            return playerIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] NormalizeApiNames(scg::IEnumerable<string> apiNames)
        {
            if (apiNames == null)
            {
                return Array.Empty<string>();
            }

            return apiNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
