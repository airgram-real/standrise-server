using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.MongoDB.Main.PlayerStats;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("SeasonalStatsRemoteService")]
    public class SeasonalStatsRemoteService : RpcClass
    {
        public SeasonalStatsRemoteService(UserService user) : base(user) { }

        public override async Task InvokeAsync(RpcRequest request)
        {
            string methodName = request.MethodName.ToLowerInvariant();
            switch (methodName)
            {
                case "getplayerstatsforseason":
                    await GetPlayerStatsForSeason(request.Params.ToArray(), request.Id);
                    break;
                case "getcurrentclanstatsforseason":
                    await GetCurrentClanStatsForSeason(request.Params.ToArray(), request.Id);
                    break;
                case "getstatsforseason":
                    await GetStatsForSeason(request.Params.ToArray(), request.Id);
                    break;
                case "getclanstatsforseason":
                    await GetClanStatsForSeason(request.Params.ToArray(), request.Id);
                    break;
                case "getcurrentstats":
                    await GetCurrentStats(request.Params.ToArray(), request.Id);
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

        private async Task GetPlayerStatsForSeason(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string currentPlayerId))
                {
                    SendError(guid, 401);
                    return;
                }

                var request = ParseRequest(values, GetPlayerStatsForSeasonRequest.Parser);
                Logger.Log($"[SeasonProbe] getPlayerStatsForSeason requested='{request.SeasonId}' by={currentPlayerId}");
                var targetPlayerId = string.IsNullOrWhiteSpace(request.PlayerId)
                    ? currentPlayerId
                    : request.PlayerId;
                string seasonId = string.IsNullOrWhiteSpace(request.SeasonId) ? "3" : request.SeasonId;

                var response = new GetPlayerStatsForSeasonResponse();
                FillPlayerStats(targetPlayerId, response, seasonId);
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetCurrentClanStatsForSeason(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                var request = ParseRequest(values, GetCurrentClanStatsForSeasonRequest.Parser);
                Logger.Log($"[SeasonProbe] getCurrentClanStatsForSeason requested='{request.SeasonId}'");
                var playerDoc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
                var clanDoc = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDoc);

                var response = new GetCurrentClanStatsForSeasonResponse
                {
                    ClanStats = clanDoc != null
                        ? ClanStatsPayloadBuilder.BuildClanStats(clanDoc, request.SeasonId)
                        : new ClanStats
                        {
                            ClanId = string.Empty,
                            SeasonId = ClanStatsPayloadBuilder.NormalizeSeasonId(request.SeasonId)
                        }
                };

                if (clanDoc != null)
                {
                    response.ClanMemberStats.Add(ClanStatsPayloadBuilder.BuildClanMemberStatsForClan(clanDoc, ClanStatsPayloadBuilder.NormalizeSeasonId(request.SeasonId)));
                }

                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetStatsForSeason(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                string seasonId = "3";
                try
                {
                    var request = ParseRequest(values, GetStatsForSeasonRequest.Parser);
                    Logger.Log($"[SeasonProbe] getStatsForSeason requested='{request.SeasonId}' by={playerId}");
                    if (!string.IsNullOrWhiteSpace(request.SeasonId))
                        seasonId = request.SeasonId;
                }
                catch { }

                var response = new GetPlayerStatsForSeasonResponse();
                FillPlayerStats(playerId, response, seasonId);
                SendResponse(guid, response);
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
                var tempResponse = new GetPlayerStatsForSeasonResponse();
                
                // Добавляем существующие статистики
                if (statsDoc?.stats != null)
                {
                    foreach (var bsonElement in statsDoc.stats)
                    {
                        if (bsonElement.Value.IsInt32 ||
                            bsonElement.Value.IsInt64 ||
                            bsonElement.Value.IsDouble ||
                            bsonElement.Value.IsString)
                        {
                            tempResponse.Stat.Add(bsonElement.GetPlayerStat());
                        }
                    }
                }
                
                // Добавляем недостающие дефолтные статистики
                EnsureDefaultStats(tempResponse);

                // Клиент определяет максимальный сезон переключателя по этому стату.
                // Всегда 3 — иначе Mongo со значением 1 запирает UI на «СЕЗОН 1».
                bool hasSeason = false;
                foreach (var s in tempResponse.Stat)
                {
                    if (s?.Name == "current_season_id")
                    {
                        s.Type = StatDefType.Int;
                        s.IntValue = 3;
                        s.LongValue = 3;
                        hasSeason = true;
                        break;
                    }
                }
                if (!hasSeason)
                {
                    tempResponse.Stat.Add(new PlayerStat
                    {
                        Name = "current_season_id",
                        Type = StatDefType.Int,
                        IntValue = 3,
                        LongValue = 3
                    });
                }
                try { BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "current_season_id", 3); } catch { }

                // Конвертируем в Stats для ответа
                var stats = new Stats();
                foreach (var stat in tempResponse.Stat)
                {
                    stats.Stat.Add(stat);
                }
                stats.Stat.AppendAliases();
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

        private async Task GetClanStatsForSeason(BinaryValue[] values, string guid)
        {
            try
            {
                var request = ParseRequest(values, GetClanStatsForSeasonRequest.Parser);
                Logger.Log($"[SeasonProbe] getClanStatsForSeason requested='{request.SeasonId}' clan='{request.ClanId}'");
                ClanDocument clanDoc = null;
                string clanId = request.ClanId;

                if (!string.IsNullOrWhiteSpace(clanId))
                {
                    clanDoc = BoltMainDatabaseProvider.Instance.GetClanDocument(clanId);
                }
                else if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    var playerDoc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
                    clanDoc = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDoc);
                    clanId = clanDoc?._id.ToString() ?? string.Empty;
                }

                var response = new GetClanStatsForSeasonResponse
                {
                    ClanStats = clanDoc != null
                        ? ClanStatsPayloadBuilder.BuildClanStats(clanDoc, request.SeasonId)
                        : new ClanStats
                        {
                            ClanId = clanId ?? string.Empty,
                            SeasonId = ClanStatsPayloadBuilder.NormalizeSeasonId(request.SeasonId)
                        }
                };

                if (clanDoc != null)
                {
                    response.ClanMemberStats.Add(ClanStatsPayloadBuilder.BuildClanMemberStatsForClan(clanDoc, ClanStatsPayloadBuilder.NormalizeSeasonId(request.SeasonId)));
                }

                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private static T ParseRequest<T>(BinaryValue[] values, MessageParser<T> parser)
            where T : IMessage<T>, new()
        {
            if (values == null || values.Length == 0 || values[0]?.One == null || values[0].One.Length == 0)
            {
                return new T();
            }

            return parser.ParseFrom(values[0].One.ToByteArray());
        }

        private static void FillPlayerStats(string playerId, GetPlayerStatsForSeasonResponse response, string seasonId = "3")
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return;
            }

            var statsDoc = BoltGameDatabaseProvider.Instance.GetOrCreatePlayerStatsDocument(playerId);
            
            // Сначала добавляем существующие статистики из базы
            if (statsDoc?.stats != null)
            {
                foreach (var bsonElement in statsDoc.stats)
                {
                    if (bsonElement.Value.IsInt32 ||
                        bsonElement.Value.IsInt64 ||
                        bsonElement.Value.IsDouble ||
                        bsonElement.Value.IsString)
                    {
                        response.Stat.Add(bsonElement.GetPlayerStat());
                    }
                }
            }
            
            // Ensure current_season_id always reports the LIVE season (3), not the browsed one.
            // Клиент по этому стату определяет верхнюю границу переключателя сезонов
            // (_previousSeasonButton/_nextSeasonButton в BaseCompetitiveStatsTabController).
            // Раньше стата не было в ответе вообще и клиент считал максимальный сезон = 1,
            // из-за чего показывался только "СЕЗОН 1" и кнопки переключения были заблокированы.
            int liveSeason = MatchHistoryBuilder.CurrentSeasonNumber;
            string normalizedSeasonId = string.IsNullOrWhiteSpace(seasonId)
                ? liveSeason.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : seasonId;
            bool hasSeasonStat = false;
            foreach (var stat in response.Stat)
            {
                if (stat.Name == "current_season_id")
                {
                    stat.IntValue = liveSeason;
                    stat.LongValue = liveSeason;
                    hasSeasonStat = true;
                    break;
                }
            }
            if (!hasSeasonStat)
            {
                response.Stat.Add(new PlayerStat
                {
                    Name = "current_season_id",
                    Type = StatDefType.Int,
                    IntValue = liveSeason,
                    LongValue = liveSeason
                });
            }

            // Для прошлых сезонов (1 и 2) показываем отдельную, детерминированно выведенную
            // из текущих статов "историю", чтобы переключение сезонов давало разные цифры,
            // а не одни и те же значения текущего сезона.
            // По умолчанию выключено: вкладка, открытая не на живом сезоне, показывала
            // выдуманные уменьшенные цифры вместо реальных. Включается FakeSeasonHistory.
            if (LocalServerConfig.Current.FakeSeasonHistory
                && int.TryParse(normalizedSeasonId, out int browsedSeason)
                && browsedSeason >= 1 && browsedSeason < liveSeason)
            {
                ApplySeasonHistory(playerId, browsedSeason, response);
            }

            // Затем добавляем недостающие дефолтные статистики
            EnsureDefaultStats(response);
            response.Stat.AppendAliases();
            response.Stat.ApplyClientRankDisplayOffset();
        }

        // Стабильный множитель (0.5..0.95) для "исторического" сезона конкретного игрока
        private static double SeasonFactor(string playerId, int season, string salt)
        {
            string key = $"{playerId}|{season}|{salt}";
            unchecked
            {
                int hash = 17;
                foreach (char c in key) hash = hash * 31 + c;
                hash = Math.Abs(hash);
                return 0.5 + (hash % 46) / 100.0;
            }
        }

        private static void ApplySeasonHistory(string playerId, int season, GetPlayerStatsForSeasonResponse response)
        {
            foreach (var stat in response.Stat)
            {
                if (!stat.Name.StartsWith("ranked", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double factor = SeasonFactor(playerId, season, stat.Name);
                switch (stat.Name)
                {
                    case "ranked_rank":
                    case "ranked_best_rank":
                    case "ranked_2v2_rank":
                    case "ranked_2v2_best_rank":
                        // Ранг берём чуть ниже текущего (не ниже 0)
                        if (stat.IntValue > 0)
                        {
                            int lowered = (int)Math.Round(stat.IntValue + (1 - factor) * 2);
                            stat.IntValue = Math.Max(0, Math.Min(lowered, stat.IntValue + 1));
                        }
                        break;
                    case "ranked_current_mmr":
                    case "ranked_2v2_current_mmr":
                        stat.IntValue = (int)Math.Round(stat.IntValue * factor);
                        break;
                    default:
                        // Счётчики (убийства, победы, матчи и т.п.)
                        if (stat.IntValue != 0)
                        {
                            stat.IntValue = (int)Math.Round(stat.IntValue * factor);
                        }
                        break;
                }
            }
        }
        
        private static void EnsureDefaultStats(GetPlayerStatsForSeasonResponse response)
        {
            // Создаем HashSet существующих имен статистик для быстрой проверки
            var existingStats = new System.Collections.Generic.HashSet<string>();
            foreach (var stat in response.Stat)
            {
                existingStats.Add(stat.Name);
            }
            
            // Список всех необходимых статистик
            var requiredStats = new[]
            {
                // Ranked2v2 general stats (используется WrapMode: gameMode.ToLower() + "_" + apiName)
                ("ranked2v2_kills", StatDefType.Int, 0),
                ("ranked2v2_deaths", StatDefType.Int, 0),
                ("ranked2v2_assists", StatDefType.Int, 0),
                ("ranked2v2_headshots", StatDefType.Int, 0),
                ("ranked2v2_shots", StatDefType.Int, 0),
                ("ranked2v2_hits", StatDefType.Int, 0),
                ("ranked2v2_games_played", StatDefType.Int, 0),
                ("ranked2v2_damage", StatDefType.Int, 0),
                ("ranked2v2_wins", StatDefType.Int, 0),
                ("ranked2v2_losses", StatDefType.Int, 0),
                
                // Ranked2v2 specific stats (используется напрямую в PlayerRanked2v2StatsExtension)
                ("ranked_2v2_current_mmr", StatDefType.Int, 0),
                ("ranked_2v2_rank", StatDefType.Int, -1),
                ("ranked_2v2_best_rank", StatDefType.Int, -1),
                ("ranked_2v2_played_matches", StatDefType.Int, 0),
                ("ranked_2v2_calibration_match_count", StatDefType.Int, 0),
                ("ranked_2v2_won_match_count", StatDefType.Int, 0),
                ("ranked_2v2_best_rank_history1", StatDefType.Int, 0),
                ("ranked_2v2_best_rank_history2", StatDefType.Int, 0),
                ("ranked_2v2_best_rank_history3", StatDefType.Int, 0),
                
                // RankedDefuse general stats
                ("rankeddefuse_kills", StatDefType.Int, 0),
                ("rankeddefuse_deaths", StatDefType.Int, 0),
                ("rankeddefuse_assists", StatDefType.Int, 0),
                ("rankeddefuse_headshots", StatDefType.Int, 0),
                ("rankeddefuse_shots", StatDefType.Int, 0),
                ("rankeddefuse_hits", StatDefType.Int, 0),
                ("rankeddefuse_games_played", StatDefType.Int, 0),
                ("rankeddefuse_damage", StatDefType.Int, 0),
                ("rankeddefuse_wins", StatDefType.Int, 0),
                ("rankeddefuse_losses", StatDefType.Int, 0),
                
                // RankedDefuse specific stats
                ("ranked_current_mmr", StatDefType.Int, 0),
                ("ranked_rank", StatDefType.Int, -1),
                ("ranked_best_rank", StatDefType.Int, -1),
                ("ranked_played_matches", StatDefType.Int, 0),
                ("ranked_calibration_match_count", StatDefType.Int, 0),
                ("ranked_won_match_count", StatDefType.Int, 0),
                ("ranked_best_rank_history1", StatDefType.Int, 0),
                ("ranked_best_rank_history2", StatDefType.Int, 0),
                ("ranked_best_rank_history3", StatDefType.Int, 0),

                // Статусы/время последнего матча: клиент читает их при инициализации,
                // отсутствие статы валит клиент (BOLTPLAYERSTATSEXCEPTION ... NOT FOUND).
                ("ranked_last_match_status", StatDefType.Int, 0),
                ("ranked_last_match_start_time", StatDefType.Int, 0),
                ("ranked_2v2_last_match_status", StatDefType.Int, 0),
                ("ranked_2v2_last_match_start_time", StatDefType.Int, 0)
            };
            
            // Добавляем только те статистики, которых нет
            foreach (var (statName, type, value) in requiredStats)
            {
                if (!existingStats.Contains(statName))
                {
                    var playerStat = new PlayerStat
                    {
                        Name = statName,
                        Type = type
                    };
                    
                    if (type == StatDefType.Int)
                    {
                        playerStat.IntValue = (int)value;
                    }
                    else if (type == StatDefType.Float)
                    {
                        playerStat.FloatValue = (float)value;
                    }
                    
                    response.Stat.Add(playerStat);
                }
            }
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

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in SeasonalStatsRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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
