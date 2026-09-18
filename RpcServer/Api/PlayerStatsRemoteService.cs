using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.MongoDB.Main.PlayerStats;
using StandRiseServer.MongoDB.Game;

namespace StandRiseServer.RpcServer.Api
{
    public class PlayerStatsRemoteService : RpcClass
    {
        private static byte[] WrapMessageResponse(Google.Protobuf.IMessage message)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                using (var output = new Google.Protobuf.CodedOutputStream(stream))
                {
                    output.WriteRawTag(10);
                    output.WriteBytes(Google.Protobuf.MessageExtensions.ToByteString(message));
                    output.Flush();
                    return stream.ToArray();
                }
            }
        }

        public PlayerStatsRemoteService(UserService user) : base(user) { }

        private sealed class CachedStatsSnapshot
        {
            public Stats Value { get; set; }
            public DateTime ExpireAtUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CachedStatsSnapshot> PlayerStatsCache = new ConcurrentDictionary<string, CachedStatsSnapshot>();
        private static readonly TimeSpan PlayerStatsCacheTtl = TimeSpan.FromSeconds(5);

        private static List<BsonElement> Expect(List<BsonElement> fromt, List<BsonElement> to)
        {
            List<BsonElement> result = new List<BsonElement>();
            HashSet<string> toNames = new HashSet<string>(to.Select(x => x.Name));
            
            foreach (BsonElement bsonElement in fromt)
            {
                if (!toNames.Contains(bsonElement.Name))
                {
                    result.Add(bsonElement);
                }
            }
            return result;
        }

        protected void GetPlayerStats(BinaryValue[] values, string guid)
        {
            try
            {
                FromByteMethod from = new FromByteMethod(typeof(string));
                string playerId = (string)from.FromBytes(values[0]);

                if (TryGetCachedPlayerStats(playerId, out Stats cachedStats))
                {
                    ForceCalibrationRanksOnStats(cachedStats);
                    cachedStats.Stat.AppendAliases();
                    EnsureLiveSeasonStat(cachedStats);
                    PersistLiveSeasonUnlock(playerId, cachedStats);
                    PersistGrantedRankIfNeeded(playerId, cachedStats);
                    var clientStats = cachedStats.Clone();
                    clientStats.Stat.ApplyClientRankDisplayOffset();
                    EnsureLiveSeasonStat(clientStats);
                    LogSeasonUnlockWire("getPlayerStats-cache", playerId, clientStats);
                    _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new ToByteMethod(typeof(Stats)).ToBytes(clientStats) } });
                    return;
                }
                
                BoltGameDatabaseProvider boltMain = BoltGameDatabaseProvider.Instance;
                PlayerStatsDocument playerStatsDocument = boltMain.GetOrCreatePlayerStatsDocument(playerId);
                
                Stats stats = new Stats();
                foreach (var bsonElement in playerStatsDocument?.stats ?? new BsonDocument())
                {
                    stats.Stat.Add(bsonElement.GetPlayerStat());
                }
                stats.Stat.AppendAliases();
                EnsureLiveSeasonStat(stats);
                PersistLiveSeasonUnlock(playerId, stats);
                ForceCalibrationRanksOnStats(stats);
                stats.Stat.AppendAliases();
                PersistGrantedRankIfNeeded(playerId, stats);
                CachePlayerStats(playerId, stats);
                var responseStats = stats.Clone();
                responseStats.Stat.ApplyClientRankDisplayOffset();
                EnsureLiveSeasonStat(responseStats);
                LogSeasonUnlockWire("getPlayerStats", playerId, responseStats);
                _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new ToByteMethod(typeof(Stats)).ToBytes(responseStats) } });
            }
            catch (System.Exception ex)
            {
                _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Code = 500 } } });
            }
        }

        protected void GetPlayerStats2(BinaryValue[] values, string guid)
        {
            try
            {
                if (values == null || values.Length == 0 || values[0] == null || values[0].IsNull)
                {
                    _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Code = 400 } } });
                    return;
                }

                var requestMsg = GetPlayerStatsRequest.Parser.ParseFrom(values[0].One);
                string playerId = requestMsg.PlayerId;

                BoltGameDatabaseProvider boltMain = BoltGameDatabaseProvider.Instance;
                PlayerStatsDocument playerStatsDocument = boltMain.GetOrCreatePlayerStatsDocument(playerId);
                
                Stats stats = new Stats();
                foreach (var bsonElement in playerStatsDocument?.stats ?? new BsonDocument())
                {
                    stats.Stat.Add(bsonElement.GetPlayerStat());
                }
                stats.Stat.AppendAliases();
                EnsureLiveSeasonStat(stats);
                PersistLiveSeasonUnlock(playerId, stats);
                ForceCalibrationRanksOnStats(stats);
                stats.Stat.AppendAliases();
                PersistGrantedRankIfNeeded(playerId, stats);
                if (!stats.Stat.Any(s => s.Name == "xp"))
                    stats.Stat.Add(new Axlebolt.Bolt.Protobuf.PlayerStat { Name = "xp", IntValue = 0 });
                if (!stats.Stat.Any(s => s.Name == "level_id"))
                    stats.Stat.Add(new Axlebolt.Bolt.Protobuf.PlayerStat { Name = "level_id", IntValue = 0 });
                CachePlayerStats(playerId, stats);
                var clientStats2 = stats.Clone();
                clientStats2.Stat.ApplyClientRankDisplayOffset();
                EnsureLiveSeasonStat(clientStats2);
                LogSeasonUnlockWire("getPlayerStats2", playerId, clientStats2);

                var response = new GetPlayerStats2Response
                {
                    Data = new GetPlayerStats2ResponseData
                    {
                        PlayerId = playerId,
                        Stats = clientStats2
                    }
                };
                
                _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(Google.Protobuf.MessageExtensions.ToByteArray(response)) } } });
            }
            catch (System.Exception ex)
            {
                _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Code = 500 } } });
            }
        }

        protected void GetStats(BinaryValue[] values, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider boltMain = BoltGameDatabaseProvider.Instance;
                PlayerStatsDocument playerStatsDocument = boltMain.GetOrCreatePlayerStatsDocument(Id);
                List<BsonElement> playerStatsOrig = playerStatsDocument?.stats?.ToList() ?? new List<BsonElement>();

                Stats stats = new Stats();
                foreach (var bsonElement in playerStatsOrig)
                {
                    if (bsonElement.Value.IsInt32 || bsonElement.Value.IsInt64 || bsonElement.Value.IsDouble || bsonElement.Value.IsString)
                    {
                        stats.Stat.Add(bsonElement.GetPlayerStat());
                    }
                }
                stats.Stat.EnsureMatchStatusIntWire();
                stats.Stat.AppendAliases();
                EnsureLiveSeasonStat(stats);
                PersistLiveSeasonUnlock(Id, stats);
                ForceCalibrationRanksOnStats(stats);
                stats.Stat.AppendAliases();
                stats.Stat.EnsureMatchStatusIntWire();
                PersistGrantedRankIfNeeded(Id, stats);
                stats.Stat.ApplyClientRankDisplayOffset();
                // Offset не должен занулять unlock — ещё раз гарантируем history2/3 после offset.
                EnsureLiveSeasonStat(stats);
                stats.Stat.EnsureMatchStatusIntWire();
                LogSeasonUnlockWire("getStats", Id, stats);
                if (!stats.Stat.Any(s => s.Name == "xp"))
                    stats.Stat.Add(new Axlebolt.Bolt.Protobuf.PlayerStat { Name = "xp", IntValue = 0 });
                if (!stats.Stat.Any(s => s.Name == "level_id"))
                    stats.Stat.Add(new Axlebolt.Bolt.Protobuf.PlayerStat { Name = "level_id", IntValue = 0 });
                
                _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new ToByteMethod(typeof(Stats)).ToBytes(stats) } });
                return;
            }
            _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Code = 401 } } });
        }

        protected void GetCurrentStats(BinaryValue[] values, string guid)
        {
            if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
            {
                SendError(guid, 401);
                return;
            }

            try
            {
                var boltMain = BoltGameDatabaseProvider.Instance;
                var playerStatsDocument = boltMain.GetOrCreatePlayerStatsDocument(playerId);

                var stats = new Stats();
                foreach (var bsonElement in playerStatsDocument?.stats ?? new BsonDocument())
                {
                    if (bsonElement.Value.IsInt32 || bsonElement.Value.IsInt64 || bsonElement.Value.IsDouble || bsonElement.Value.IsString)
                    {
                        stats.Stat.Add(bsonElement.GetPlayerStat());
                    }
                }
                stats.Stat.EnsureMatchStatusIntWire();
                stats.Stat.AppendAliases();
                EnsureLiveSeasonStat(stats);
                PersistLiveSeasonUnlock(playerId, stats);
                ForceCalibrationRanksOnStats(stats);
                stats.Stat.AppendAliases();
                stats.Stat.EnsureMatchStatusIntWire();
                PersistGrantedRankIfNeeded(playerId, stats);
                stats.Stat.ApplyClientRankDisplayOffset();
                EnsureLiveSeasonStat(stats);
                stats.Stat.EnsureMatchStatusIntWire();
                LogSeasonUnlockWire("getCurrentStats", playerId, stats);

                if (!stats.Stat.Any(s => s.Name == "xp"))
                    stats.Stat.Add(new Axlebolt.Bolt.Protobuf.PlayerStat { Name = "xp", IntValue = 0 });
                if (!stats.Stat.Any(s => s.Name == "level_id"))
                    stats.Stat.Add(new Axlebolt.Bolt.Protobuf.PlayerStat { Name = "level_id", IntValue = 0 });

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

        protected void StoreStats(BinaryValue[] values, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                StorePlayerStat[] storePlayerStats = null;
                StoreAchievement[] storeAchievements = null;

                if (values != null && values.Length > 0)
                {
                    try
                    {
                        storePlayerStats = (StorePlayerStat[])new FromByteMethod(typeof(StorePlayerStat[])).FromBytes(values[0]);
                    }
                    catch (System.Exception ex)
                    {
                        Logger.LogWarn($"[StoreStats] Failed to parse stats for player {Id}: {ex.Message}");
                    }
                }

                if (values != null && values.Length > 1)
                {
                    try
                    {
                        storeAchievements = (StoreAchievement[])new FromByteMethod(typeof(StoreAchievement[])).FromBytes(values[1]);
                    }
                    catch (System.Exception ex)
                    {
                        Logger.LogWarn($"[StoreStats] Failed to parse achievements for player {Id}: {ex.Message}");
                    }
                }

                if ((storePlayerStats == null || storePlayerStats.Length == 0) &&
                    TryParseWrappedStoreStats(values, out StorePlayerStat[] wrappedStats))
                {
                    storePlayerStats = wrappedStats;
                }

                BoltGameDatabaseProvider boltMain = BoltGameDatabaseProvider.Instance;

                if (storePlayerStats != null && storePlayerStats.Length > 0)
                {
                    Logger.Log($"[StoreStats] Player {Id} storing {storePlayerStats.Length} stats");
                    foreach (var stat in storePlayerStats)
                    {
                        if (!IsStoreStatPayloadSafe(stat)) continue;
                        boltMain.StoreStat(Id, stat);
                    }

                    // ── Награда за законченный матч ───────────────────────────────
                    // Клиент НЕ вызывает finishMatch (и не шлёт drop-рецепт) после матча;
                    // каждый матч он завершает вызовом storeStats c кумулятивным счётчиком
                    // "{mode}_games_played". Когда этот счётчик увеличился — матч закончился,
                    // выдаём опыт и дроп. Дед-пример по (игрок, режим) защищает от повторных
                    // пачек storeStats в одном матче.
                    try
                    {
                        foreach (var stat in storePlayerStats)
                        {
                            string statName = stat?.Name;
                            if (string.IsNullOrWhiteSpace(statName)) continue;
                            if (!statName.EndsWith("_games_played", StringComparison.OrdinalIgnoreCase)) continue;

                            int gamesPlayed = (int)stat.StoreInt;
                            string mode = statName.Substring(0, statName.Length - "_games_played".Length).ToLowerInvariant();
                            if (string.IsNullOrWhiteSpace(mode)) continue;

                            int lastSeen = GetLastGamesPlayed(Id, mode);
                            if (gamesPlayed > lastSeen)
                            {
                                SetLastGamesPlayed(Id, mode, gamesPlayed);
                                Logger.Log($"[StoreStats] Match finished for player {Id} (mode={mode}, games_played {lastSeen}->{gamesPlayed}); granting XP + drop.");

                                // MMR/звания для ranked — только через Photon → HTTP /api/match/result.
                                // StoreStats раньше гонялся с HTTP и затирал полный allies MMR.
                                string rankedMode = PlayerStatsManager.NormalizeRankedGameMode(mode);
                                bool isRankedMode = rankedMode == "allies" || rankedMode == "ranked";

                                // Ranked/allies: XP и награды приходят из Photon → HTTP /api/match/result.
                                // Повторный GrantMatchReward отсюда давал ложный +1 уровень.
                                if (isRankedMode)
                                {
                                    try { PlayerStatsRemoteService.InvalidateStatsCachePublic(Id); } catch { }
                                    continue;
                                }

                                MatchRewardResult reward = GrantMatchReward(Id, mode);

                                try
                                {
                                    string matchId = Guid.NewGuid().ToString();
                                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                    string modeLower = (mode ?? string.Empty).ToLowerInvariant();
                                    var finished = new FinishedMatch
                                    {
                                        MatchId = matchId,
                                        FinishDate = now,
                                        StartDate = now,
                                    };
                                    MatchHistoryBuilder.StampForClient(finished, mode);

                                    // Per-player reward row: XP earned, level id, gold, silver and the skin drop.
                                    var stats = new System.Collections.Generic.List<FinishedMatch.StatValueRow>();
                                    stats.Add(new FinishedMatch.StatValueRow { Name = "xp", Type = 4, LongValue = (long)reward.XpEarned });
                                    stats.Add(new FinishedMatch.StatValueRow { Name = "level_id", Type = 0, IntValue = reward.NewLevel });
                                    stats.Add(new FinishedMatch.StatValueRow { Name = "gold", Type = 0, IntValue = reward.Gold });
                                    stats.Add(new FinishedMatch.StatValueRow { Name = "silver", Type = 0, IntValue = reward.Silver });
                                    var drops = new System.Collections.Generic.List<FinishedMatch.DroppedItem>();
                                    if (reward.HasDrop)
                                        drops.Add(new FinishedMatch.DroppedItem { ItemId = reward.DropItemDefinitionId.ToString(), Count = 1 });

                                    string playerName = Id;
                                    try
                                    {
                                        var profile = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(Id));
                                        if (profile != null)
                                            playerName = !string.IsNullOrWhiteSpace(profile.uid) ? profile.uid
                                                : (!string.IsNullOrWhiteSpace(profile.name) ? profile.name : Id);
                                    }
                                    catch { }

                                    bool? wonForHistory = DetectMatchWin(storePlayerStats, mode);
                                    if (wonForHistory.HasValue)
                                        stats.Add(new FinishedMatch.StatValueRow { Name = "result", Type = 0, IntValue = wonForHistory.Value ? 1 : 0 });

                                    finished.AddPlayerRow(Id, playerName, stats, drops);
                                    finished.AddOverallStat(new FinishedMatch.StatValueRow { Name = "mode", Type = 2, StringValue = mode });
                                    try
                                    {
                                        if (modeLower.Contains("2v2") || modeLower.Contains("allies"))
                                        {
                                            int played = (int)BoltGameDatabaseProvider.Instance.GetPlayerStat(Id, "ranked_2v2_played_matches");
                                            int rank = played >= 10 ? (int)BoltGameDatabaseProvider.Instance.GetPlayerStat(Id, "ranked_2v2_rank") : -1;
                                            finished.AddOverallStat(new FinishedMatch.StatValueRow { Name = "ranked_2v2_played_matches", Type = 0, IntValue = played });
                                            finished.AddOverallStat(new FinishedMatch.StatValueRow { Name = "ranked_2v2_rank", Type = 0, IntValue = ClientRankDisplay.ToClientRank(rank) });
                                            finished.AddOverallStat(new FinishedMatch.StatValueRow { Name = "calibration_matches_played", Type = 0, IntValue = Math.Clamp(played, 0, 10) });
                                            finished.AddOverallStat(new FinishedMatch.StatValueRow { Name = "calibration_match_count", Type = 0, IntValue = 10 });
                                        }
                                    }
                                    catch { }

                                    try
                                    {
                                        BoltGameDatabaseProvider.Instance.SavePlayerLastMatch(Id, finished);
                                    }
                                    catch (System.Exception saveEx)
                                    {
                                        Logger.LogWarn($"[StoreStats] SavePlayerLastMatch failed: {saveEx.Message}");
                                    }

                                    var evt = new OnMatchFinishedEvent { Match = finished };
                                    SendEventToSession<MatchesRemoteEventListener>(Id, "onMatchFinished", new object[] { evt });
                                    Logger.Log($"[StoreStats] Broadcast onMatchFinished to {Id} (mode={mode})");
                                }
                                catch (System.Exception evEx)
                                {
                                    Logger.LogWarn($"[StoreStats] onMatchFinished broadcast error for {Id}: {evEx.Message}");
                                }
                            }
                        }
                    }
                    catch (System.Exception rwEx)
                    {
                        Logger.LogWarn($"[StoreStats] Match reward grant error for player {Id}: {rwEx.Message}");
                    }
                }

                InvalidatePlayerStatsCache(Id);

                _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new BinaryValue { IsNull = true } } });
                return;
            }
            SendError(guid, 401);
        }

        // ── Награда за матч ────────────────────────────────────────────────────
        // Дед-пример: запоминаем последний переданный клиентом кумулятивный
        // счётчик "{mode}_games_played", чтобы не выдавать награду дважды за матч.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _lastGamesPlayed
            = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static int GetLastGamesPlayed(string playerId, string mode)
        {
            string key = playerId + "|" + mode.ToLowerInvariant();
            return _lastGamesPlayed.TryGetValue(key, out int v) ? v : 0;
        }

        private static void SetLastGamesPlayed(string playerId, string mode, int gamesPlayed)
        {
            string key = playerId + "|" + mode.ToLowerInvariant();
            _lastGamesPlayed[key] = gamesPlayed;
        }

        /// <summary>
        /// Пытается вытащить win/loss из той же пачки storeStats (last_match_status / wins).
        /// </summary>
        private static bool? DetectMatchWin(StorePlayerStat[] storePlayerStats, string mode)
        {
            if (storePlayerStats == null || storePlayerStats.Length == 0) return null;
            string modeLower = (mode ?? "").ToLowerInvariant();
            bool prefer2v2 = modeLower.Contains("2v2") || modeLower.Contains("allies");

            foreach (var s in storePlayerStats)
            {
                if (string.IsNullOrWhiteSpace(s?.Name)) continue;
                string n = s.Name.ToLowerInvariant();
                if (!n.Contains("last_match_status")) continue;
                if (prefer2v2 && !n.Contains("2v2") && n.StartsWith("ranked_") && !n.Contains("ranked_2v2"))
                    continue;
                return (int)s.StoreInt == 1;
            }

            // Фоллбек: если в пачке есть won_match / wins с ненулевым дельта-намёком — только статус.
            foreach (var s in storePlayerStats)
            {
                if (string.IsNullOrWhiteSpace(s?.Name)) continue;
                string n = s.Name.ToLowerInvariant();
                if (prefer2v2 && n.Contains("2v2") && (n.EndsWith("_wins") || n.Contains("won_match_count") || n.Contains("calibration_match_count")))
                {
                    // Нельзя надёжно сказать win без previous; last_match_status предпочтительнее.
                    continue;
                }
            }

            return null;
        }

        /// <summary>
        /// Выдаёт награду за законченный матч: опыт (уровень), золото+серебро и
        /// скиновый дроп. Ничего не отправляет клиенту — изменения сохраняются в БД,
        /// а клиент видит их при следующей загрузке инвентаря/lobby.
        /// </summary>
        public sealed class MatchRewardResult
        {
            public float XpEarned;
            public int NewLevel;
            public int Gold;
            public int Silver;
            public int DropItemDefinitionId;
            public int DropInventoryId;
            public bool HasDrop;
        }

        private static MatchRewardResult GrantMatchReward(string playerId, string mode, bool? isWin = null)
        {
            MatchRewardResult result = new MatchRewardResult();
            try
            {
                var dbXp = BoltGameDatabaseProvider.Instance;
                var curLevel  = (int)dbXp.GetPlayerStat(playerId, "level_id");
                if (curLevel < 1) curLevel = 1;
                float xp;
                if (isWin == true) xp = 120f;
                else if (isWin == false) xp = 60f;
                else xp = 80f;
                if (xp > 0f)
                    PlayerStatsManager.AddExperience(playerId, xp);
                result.XpEarned = xp;
                result.NewLevel = (int)dbXp.GetPlayerStat(playerId, "level_id");
                if (result.NewLevel < 1) result.NewLevel = 1;

                // 2) Валюта: золото + серебро.
                var db = BoltGameDatabaseProvider.Instance;
                var rng = new Random();
                int gold = rng.Next(50, 151);
                int silver = rng.Next(100, 301);
                ObjectId oid = ObjectId.Parse(playerId);
                db.CurrencyPlusValue(oid, 102, gold);
                db.CurrencyPlusValue(oid, 101, silver);
                result.Gold = gold;
                result.Silver = silver;

                // 3) Скиновый дроп — шанс ниже, без гарантированного fallback.
                var dropRng = new Random();
                BoltInventoryItem dropped = null;
                const int postMatchSkinDropPercent = 10;
                if (dropRng.Next(100) < postMatchSkinDropPercent)
                {
                    var generator = new RandomGenerator();
                    MatchDropConfig.ApplyMatchDropWeights(generator);
                    CollectionId[] collections = System.Enum.GetValues(typeof(CollectionId)).Cast<CollectionId>()
                        .Where(c => c != CollectionId.None).ToArray();
                    for (int attempt = 0; attempt < 40 && dropped == null; attempt++)
                    {
                        CollectionId collection = collections[dropRng.Next(collections.Length)];
                        var candidate = generator.GetRandomItem(collection);
                        if (candidate == null) continue;
                        var candDef = db.GetInventoryItemDefinition(candidate.itemDefinitionId, 1);
                        if (candDef != null && MatchDropConfig.IsExcludedFromMatchDrop(candDef)) continue;
                        dropped = candidate;
                    }
                }

                if (dropped != null)
                {
                    var inventory = db.GetPlayerInventoryDocument(oid);
                    int nextId = (inventory != null && inventory.InventoryItems != null && inventory.InventoryItems.ElementCount > 0)
                        ? inventory.InventoryItems.Select(i => int.TryParse(i.Name, out int p) ? p : 0).Max() + 1
                        : 1;
                    db.AddItemToPlayerInventoryDocument(oid, dropped, nextId);
                    result.HasDrop = true;
                    result.DropItemDefinitionId = dropped.itemDefinitionId;
                    result.DropInventoryId = nextId;
                    Logger.Log($"[MatchReward] Player {playerId} ({mode}): +{xp} XP, +{gold} gold, +{silver} silver, drop itemDefinitionId={dropped.itemDefinitionId}");
                }
                else
                {
                    Logger.Log($"[MatchReward] Player {playerId} ({mode}): +{xp} XP, +{gold} gold, +{silver} silver (no skin drop rolled)");
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogWarn($"[MatchReward] Failed to grant reward for player {playerId}: {ex.Message}");
            }
            return result;
        }

        public static MatchRewardResult GrantPostMatchRewardPublic(string playerId, string mode, bool? isWin = null)
            => GrantMatchReward(playerId, mode, isWin);

        public static void NotifyPostMatchRewards(string playerId, MatchRewardResult reward)
        {
            if (string.IsNullOrWhiteSpace(playerId) || reward == null) return;
            try { PlayerStatsRemoteEventListener.PushProfileUpdate(playerId); } catch { }
            try
            {
                if (reward.Gold > 0)
                    InventoryRemoteEventListener.PushCurrencyReward(playerId, 102, reward.Gold);
                if (reward.Silver > 0)
                    InventoryRemoteEventListener.PushCurrencyReward(playerId, 101, reward.Silver);
            }
            catch { }
            try
            {
                if (reward.HasDrop && reward.DropItemDefinitionId > 0)
                {
                    var item = new Axlebolt.Bolt.Protobuf.InventoryItem
                    {
                        Id = reward.DropInventoryId > 0 ? reward.DropInventoryId : 1,
                        ItemDefinitionId = reward.DropItemDefinitionId,
                        Quantity = 1
                    };
                    InventoryRemoteEventListener.PushDelta(playerId, new[] { item }, null);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogWarn($"[MatchReward] drop notify failed for {playerId}: {ex.Message}");
            }
            Logger.Log($"[MatchReward] profile update player={playerId} xp={reward.XpEarned} gold={reward.Gold} silver={reward.Silver} drop={(reward.HasDrop ? reward.DropItemDefinitionId : 0)}");
        }

        private static bool TryParseWrappedStoreStats(BinaryValue[] values, out StorePlayerStat[] stats)
        {
            stats = null;
            if (values == null || values.Length == 0 || values[0]?.One == null)
            {
                return false;
            }

            byte[] payload = values[0].One.ToByteArray();

            try
            {
                StorePlayerStatsRequest wrappedRequest = StorePlayerStatsRequest.Parser.ParseFrom(payload);
                if (wrappedRequest?.StorePlayerStats?.Stats != null && wrappedRequest.StorePlayerStats.Stats.Count > 0)
                {
                    stats = wrappedRequest.StorePlayerStats.Stats.ToArray();
                    return true;
                }
            }
            catch
            {
                // ignore and try next schema
            }

            try
            {
                StorePlayersStatsRequest bulkRequest = StorePlayersStatsRequest.Parser.ParseFrom(payload);
                StorePlayerStats first = bulkRequest?.StorePlayersStats?.FirstOrDefault();
                if (first?.Stats != null && first.Stats.Count > 0)
                {
                    stats = first.Stats.ToArray();
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool IsStoreStatPayloadSafe(StorePlayerStat stat)
        {
            if (stat == null || string.IsNullOrWhiteSpace(stat.Name))
            {
                return false;
            }

            if (!PlayerStatsManager.IsClientStatWriteAllowed(stat.Name))
            {
                Logger.LogWarn($"[StoreStats] Blocked client stat write '{stat.Name}'");
                return false;
            }

            if (float.IsNaN(stat.StoreFloat) || float.IsInfinity(stat.StoreFloat))
            {
                return false;
            }

            return stat.StoreInt >= 0 && stat.StoreLong >= 0 && stat.StoreFloat >= 0f;
        }

        protected void ResetStats(string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider.Instance.ResetStats(Id);
                InvalidatePlayerStatsCache(Id);
                _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new BinaryValue { IsNull = true } } });
                return;
            }
            SendError(guid, 401);
        }

        protected void GetGlobalStats(BinaryValue[] values, string guid)
        {
            // Placeholder for global stats
            SendResponse(guid, new PlayerStat[0]);
        }

        public override async Task InvokeAsync(RpcRequest request)
        {
            string methodName = request.MethodName.ToLowerInvariant();
            switch (methodName)
            {
                case "getplayerstats":
                    GetPlayerStats(request.Params.ToArray(), request.Id);
                    break;
                case "getplayerstats2":
                    GetPlayerStats2(request.Params.ToArray(), request.Id);
                    break;
                case "getstats":
                    GetStats(request.Params.ToArray(), request.Id);
                    break;
                case "getcurrentstats":
                    GetCurrentStats(request.Params.ToArray(), request.Id);
                    break;
                case "storestats":
                case "storestats2":
                    StoreStats(request.Params.ToArray(), request.Id);
                    break;
                case "resetstats":
                    ResetStats(request.Id);
                    break;
                case "getglobalstats":
                    GetGlobalStats(request.Params.ToArray(), request.Id);
                    break;
                default:
                    MethodNotFound(request);
                    break;
            }
        }

        private void SendResponse(string guid, object response = null)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = response != null
                        ? (response is IMessage message
                            ? new BinaryValue { One = message.ToByteString() }
                            : ProtoReflectionUtils.CreateToByteMethod(response.GetType()).ToBytes(response))
                        : new BinaryValue { IsNull = true }
                }
            });
        }

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in PlayerStatsRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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

        private static bool TryGetCachedPlayerStats(string playerId, out Stats stats)
        {
            if (!string.IsNullOrWhiteSpace(playerId) && PlayerStatsCache.TryGetValue(playerId, out CachedStatsSnapshot cached))
            {
                if (cached.ExpireAtUtc > DateTime.UtcNow && cached.Value != null)
                {
                    stats = cached.Value.Clone();
                    return true;
                }

                PlayerStatsCache.TryRemove(playerId, out _);
            }

            stats = null;
            return false;
        }

        private static void CachePlayerStats(string playerId, Stats stats)
        {
            if (string.IsNullOrWhiteSpace(playerId) || stats == null)
            {
                return;
            }

            PlayerStatsCache[playerId] = new CachedStatsSnapshot
            {
                Value = stats.Clone(),
                ExpireAtUtc = DateTime.UtcNow.Add(PlayerStatsCacheTtl)
            };
        }

        private static void InvalidatePlayerStatsCache(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return;
            }
            PlayerStatsCache.TryRemove(playerId, out _);
        }

        /// <summary>Сброс кэша после смены live-сезона / деплоя.</summary>
        public static void ClearAllPlayerStatsCache()
        {
            PlayerStatsCache.Clear();
        }

        public static void InvalidateStatsCachePublic(string playerId) => InvalidatePlayerStatsCache(playerId);

        /// <summary>
        /// Полный снимок статов как в getCurrentStats — для onStatsUpdatedEvent (мерж в профиль).
        /// </summary>
        public static List<PlayerStat> BuildClientPushSnapshot(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return new List<PlayerStat>();

            var boltMain = BoltGameDatabaseProvider.Instance;
            var playerStatsDocument = boltMain.GetOrCreatePlayerStatsDocument(playerId);

            var stats = new Stats();
            foreach (var bsonElement in playerStatsDocument?.stats ?? new BsonDocument())
            {
                if (bsonElement.Value.IsInt32 || bsonElement.Value.IsInt64
                    || bsonElement.Value.IsDouble || bsonElement.Value.IsString)
                {
                    stats.Stat.Add(bsonElement.GetPlayerStat());
                }
            }

            stats.Stat.EnsureMatchStatusIntWire();
            stats.Stat.AppendAliases();
            EnsureLiveSeasonStat(stats);
            ForceCalibrationRanksOnStats(stats);
            stats.Stat.AppendAliases();
            stats.Stat.EnsureMatchStatusIntWire();
            PersistGrantedRankIfNeeded(playerId, stats);
            stats.Stat.ApplyClientRankDisplayOffset();
            stats.Stat.EnsureMatchStatusIntWire();

            return stats.Stat
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Name))
                .Select(ClonePushStat)
                .ToList();
        }

        private static PlayerStat ClonePushStat(PlayerStat src)
        {
            var clone = src.Clone();
            if (clone.Type == StatDefType.Int || clone.Type == StatDefType.Float)
            {
                if (clone.LongValue == 0 && clone.IntValue != 0)
                    clone.LongValue = clone.IntValue;
                if (clone.IntValue == 0 && clone.LongValue != 0)
                    clone.IntValue = (int)Math.Clamp(clone.LongValue, int.MinValue, int.MaxValue);
            }
            return clone;
        }

        private static void EnsureLiveSeasonStat(Stats stats)
        {
            stats.EnsureLiveSeasonStat(MatchHistoryBuilder.CurrentSeasonNumber);
        }

        private static void PersistLiveSeasonUnlock(string playerId, Stats stats)
        {
            if (string.IsNullOrWhiteSpace(playerId) || stats?.Stat == null) return;
            try
            {
                var db = BoltGameDatabaseProvider.Instance;
                void Persist(string name)
                {
                    var s = stats.Stat.FirstOrDefault(x => x?.Name == name);
                    if (s != null) db.SetPlayerStat(playerId, name, s.IntValue);
                }
                Persist("current_season_id");
                Persist("ranked_season_id");
                Persist("ranked_2v2_season_id");
                Persist("allies_season_id");
                Persist("clan_ranked_season_id");
                Persist("ranked_best_rank_history1");
                Persist("ranked_best_rank_history2");
                Persist("ranked_best_rank_history3");
                Persist("ranked_2v2_best_rank_history1");
                Persist("ranked_2v2_best_rank_history2");
                Persist("ranked_2v2_best_rank_history3");
                Persist("clan_ranked_best_rank_history1");
                Persist("clan_ranked_best_rank_history2");
                Persist("clan_ranked_best_rank_history3");
            }
            catch { }
        }

        private static void LogSeasonUnlockWire(string where, string playerId, Stats stats)
        {
            if (stats?.Stat == null) return;
            int Read(string name)
            {
                var s = stats.Stat.FirstOrDefault(x => x?.Name == name);
                return s?.IntValue ?? 0;
            }
            Logger.Log($"[SeasonUnlock] {where} player={playerId} season={Read("current_season_id")} " +
                       $"h1={Read("ranked_best_rank_history1")} h2={Read("ranked_best_rank_history2")} h3={Read("ranked_best_rank_history3")} " +
                       $"a1={Read("ranked_2v2_best_rank_history1")} a2={Read("ranked_2v2_best_rank_history2")} a3={Read("ranked_2v2_best_rank_history3")}");
        }

        /// <summary>
        /// Пока сыграно &lt; 10 ranked/allies — клиенту всегда rank=-1 (не Bronze I / не Gold из MMR).
        /// </summary>
        private static void ForceCalibrationRanksOnStats(Stats stats)
        {
            if (stats?.Stat == null) return;
            int played2v2 = 0;
            int playedRanked = 0;
            int current2v2Rank = -999;
            int currentCompRank = -999;
            foreach (var s in stats.Stat)
            {
                if (s?.Name == null) continue;
                if (s.Name.Equals("ranked_2v2_played_matches", StringComparison.OrdinalIgnoreCase)
                    || s.Name.Equals("allies_played_matches", StringComparison.OrdinalIgnoreCase)
                    || s.Name.Equals("ranked2v2_games_played", StringComparison.OrdinalIgnoreCase))
                    played2v2 = Math.Max(played2v2, s.IntValue);
                if (s.Name.Equals("ranked_played_matches", StringComparison.OrdinalIgnoreCase))
                    playedRanked = Math.Max(playedRanked, s.IntValue);
                if (s.Name.Equals("ranked_2v2_rank", StringComparison.OrdinalIgnoreCase)
                    || s.Name.Equals("allies_rank", StringComparison.OrdinalIgnoreCase))
                    current2v2Rank = Math.Max(current2v2Rank, s.IntValue);
                if (s.Name.Equals("ranked_rank", StringComparison.OrdinalIgnoreCase))
                    currentCompRank = Math.Max(currentCompRank, s.IntValue);
            }

            void SetRank(string name, int value)
            {
                var existing = stats.Stat.FirstOrDefault(x => x.Name != null && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) existing.IntValue = value;
                else stats.Stat.Add(new Axlebolt.Bolt.Protobuf.PlayerStat { Name = name, IntValue = value });
            }

            // Не затираем админ-выданное звание. В БД/кэше 0..16; клиенту Legend → 17 (ApplyClientRankDisplayOffset).
            if (current2v2Rank > 16)
            {
                SetRank("ranked_2v2_rank", 16);
                SetRank("ranked_2v2_current_rank", 16);
                SetRank("allies_rank", 16);
                SetRank("allies_current_rank", 16);
                SetRank("competitive_2v2_rank", 16);
                current2v2Rank = 16;
            }
            if (current2v2Rank >= 0 && played2v2 < 10)
            {
                // Выдали звание при калибровке — в ответе поднимаем played, чтобы профиль не был Unranked.
                void SetPlayed(string name, int value)
                {
                    var existing = stats.Stat.FirstOrDefault(x => x.Name != null && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) { existing.IntValue = value; existing.LongValue = value; }
                    else stats.Stat.Add(new Axlebolt.Bolt.Protobuf.PlayerStat { Name = name, IntValue = value, LongValue = value });
                }
                SetPlayed("ranked_2v2_played_matches", 10);
                SetPlayed("allies_played_matches", 10);
                SetPlayed("ranked2v2_games_played", 10);
                SetPlayed("ranked_2v2_won_match_count", 10);
                // Явно снять калибровку в ответе клиенту.
                SetRank("ranked_2v2_rank", current2v2Rank);
                SetRank("ranked_2v2_current_rank", current2v2Rank);
                SetRank("allies_rank", current2v2Rank);
                SetRank("allies_current_rank", current2v2Rank);
                SetRank("competitive_2v2_rank", current2v2Rank);
                played2v2 = 10;
            }
            if (played2v2 < 10 && current2v2Rank < 0)
            {
                SetRank("ranked_2v2_rank", -1);
                SetRank("ranked_2v2_current_rank", -1);
                SetRank("allies_rank", -1);
                SetRank("allies_current_rank", -1);
                SetRank("competitive_2v2_rank", -1);
            }

            if (currentCompRank > 16)
            {
                SetRank("ranked_rank", 16);
                SetRank("ranked_current_rank", 16);
                currentCompRank = 16;
            }
            if (currentCompRank >= 0 && playedRanked < 10)
            {
                void SetPlayedRanked(string name, int value)
                {
                    var existing = stats.Stat.FirstOrDefault(x => x.Name != null && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) { existing.IntValue = value; existing.LongValue = value; }
                    else stats.Stat.Add(new Axlebolt.Bolt.Protobuf.PlayerStat { Name = name, IntValue = value, LongValue = value });
                }
                SetPlayedRanked("ranked_played_matches", 10);
                SetRank("ranked_rank", currentCompRank);
                SetRank("ranked_current_rank", currentCompRank);
                playedRanked = 10;
            }
            if (playedRanked < 10 && currentCompRank < 0)
            {
                SetRank("ranked_rank", -1);
                SetRank("ranked_current_rank", -1);
            }

            SyncRanksFromMmrInStats(stats);
        }

        /// <summary>MMR — источник правды: ranked_rank = RankFromMmr(mmr) в ответе клиенту.</summary>
        private static void SyncRanksFromMmrInStats(Stats stats)
        {
            if (stats?.Stat == null) return;

            int GetInt(string name, int def = 0)
            {
                var s = stats.Stat.FirstOrDefault(x => x?.Name != null && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                return s?.IntValue ?? def;
            }

            void SetRankKeys(int rank, params string[] keys)
            {
                foreach (var key in keys)
                {
                    var s = stats.Stat.FirstOrDefault(x => x?.Name != null && x.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
                    if (s != null)
                    {
                        s.IntValue = rank;
                        s.LongValue = rank;
                    }
                    else if (rank >= -1)
                        stats.Stat.Add(new Axlebolt.Bolt.Protobuf.PlayerStat { Name = key, IntValue = rank, LongValue = rank });
                }
            }

            int playedComp = GetInt("ranked_played_matches");
            int mmrComp = GetInt("ranked_current_mmr");
            if (playedComp >= AlliesMmrSystem.CalibrationMatches && mmrComp > 0)
            {
                int rank = AlliesMmrSystem.RankFromMmr(mmrComp);
                SetRankKeys(rank, "ranked_rank", "ranked_current_rank");
                int target = (int)Math.Round(AlliesMmrSystem.TargetMmrForRankBar(rank));
                SetRankKeys(target, "ranked_target_mmr");
            }

            int played2v2 = Math.Max(GetInt("ranked_2v2_played_matches"), GetInt("allies_played_matches"));
            int mmr2v2 = Math.Max(GetInt("ranked_2v2_current_mmr"), GetInt("allies_current_mmr"));
            if (played2v2 >= AlliesMmrSystem.CalibrationMatches && mmr2v2 > 0)
            {
                int rank = AlliesMmrSystem.RankFromMmr(mmr2v2);
                SetRankKeys(rank,
                    "ranked_2v2_rank", "ranked_2v2_current_rank",
                    "allies_rank", "allies_current_rank",
                    "competitive_2v2_rank", "ranked2v2_rank");
                int target = (int)Math.Round(AlliesMmrSystem.TargetMmrForRankBar(rank));
                SetRankKeys(target, "ranked_2v2_target_mmr", "allies_target_mmr");
            }
        }

        /// <summary>Если в БД остался rank&gt;16 или звание при калибровке — записать корректные статы.</summary>
        private static void PersistGrantedRankIfNeeded(string playerId, Stats stats)
        {
            if (string.IsNullOrWhiteSpace(playerId) || stats?.Stat == null) return;
            int alliesRank = -999, alliesPlayed = 0;
            int compRank = -999, compPlayed = 0;
            foreach (var s in stats.Stat)
            {
                if (s?.Name == null) continue;
                if (s.Name.Equals("ranked_2v2_rank", StringComparison.OrdinalIgnoreCase)
                    || s.Name.Equals("allies_rank", StringComparison.OrdinalIgnoreCase))
                    alliesRank = Math.Max(alliesRank, s.IntValue);
                if (s.Name.Equals("ranked_2v2_played_matches", StringComparison.OrdinalIgnoreCase)
                    || s.Name.Equals("allies_played_matches", StringComparison.OrdinalIgnoreCase))
                    alliesPlayed = Math.Max(alliesPlayed, s.IntValue);
                if (s.Name.Equals("ranked_rank", StringComparison.OrdinalIgnoreCase))
                    compRank = Math.Max(compRank, s.IntValue);
                if (s.Name.Equals("ranked_played_matches", StringComparison.OrdinalIgnoreCase))
                    compPlayed = Math.Max(compPlayed, s.IntValue);
            }
            try
            {
                if (alliesRank > 16)
                    PlayerStatsManager.SetAlliesRank(playerId, 16);
                else if (alliesRank >= 0 && alliesPlayed < 10)
                    PlayerStatsManager.SetAlliesRank(playerId, alliesRank);
                if (compRank > 16)
                    PlayerStatsManager.SetCompetitiveRank(playerId, 16);
                else if (compRank >= 0 && compPlayed < 10)
                    PlayerStatsManager.SetCompetitiveRank(playerId, compRank);
            }
            catch { }
        }

        public override void Invoke(RpcRequest request)
        {
            _ = InvokeAsync(request);
        }
    }
}
