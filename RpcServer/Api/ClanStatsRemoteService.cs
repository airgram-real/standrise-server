using System;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;

using Axlebolt.Bolt.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("ClanStatsRemoteService")]
    public class ClanStatsRemoteService : RpcClass
    {
        public ClanStatsRemoteService(UserService user) : base(user) { }

        public override void Invoke(RpcRequest request)
        {
            try
            {
                switch (request.MethodName)
                {
                    case "getCurrentClanStats":
                        GetCurrentClanStats(request.Params.ToArray(), request.Id);
                        break;
                    case "getClanStats":
                        GetClanStats(request.Params.ToArray(), request.Id);
                        break;
                    case "getCurrentClanStatsForSeason":
                        GetCurrentClanStatsForSeason(request.Params.ToArray(), request.Id);
                        break;
                    case "getClanStatsForSeason":
                        GetClanStatsForSeason(request.Params.ToArray(), request.Id);
                        break;
                    default:
                        MethodNotFound(request);
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
            }
        }

        public void GetCurrentClanStats(BinaryValue[] value, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltMainDatabaseProvider db = BoltMainDatabaseProvider.Instance;
                var player = db.GetPlayerDocument(ObjectId.Parse(Id));
                ClanDocument clanDocument = db.GetPlayerClanDocument(player);

                if (clanDocument == null)
                {
                    SendException(guid, 404, "Player is not in a clan");
                    return;
                }

                string clanId = clanDocument._id.ToString();

                var response = new GetCurrentClanStatsResponse
                {
                    ClanId = clanId,
                    Stats = new ClanStatsMap(),
                    ClanStats = ClanStatsPayloadBuilder.BuildClanStats(clanDocument)
                };
                response.ClanMemberStats.Add(ClanStatsPayloadBuilder.BuildClanMemberStatsForClan(clanDocument));

                SendResponse(guid, response);
            }
        }

        public void GetClanStats(BinaryValue[] value, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                var req = GetClanStatsRequest.Parser.ParseFrom(value[0].One.ToByteArray());
                
                var response = new GetClanStatsResponse
                {
                    ClanId = req.ClanId,
                    Stats = new ClanStatsMap(),
                    ClanStats = new Axlebolt.Bolt.Protobuf.ClanStats { ClanId = req.ClanId, SeasonId = "3" }
                };
                
                ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetClanDocument(req.ClanId);
                if (clanDocument != null)
                {
                    response.ClanStats = ClanStatsPayloadBuilder.BuildClanStats(clanDocument);
                    response.ClanMemberStats.Add(ClanStatsPayloadBuilder.BuildClanMemberStatsForClan(clanDocument));
                }
                
                SendResponse(guid, response);
            }
        }

        public void GetCurrentClanStatsForSeason(BinaryValue[] value, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendException(guid, 401, "unauthorized");
                    return;
                }

                var request = ParseRequest(value, GetCurrentClanStatsForSeasonRequest.Parser);
                var playerDoc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(playerId));
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
                    response.ClanMemberStats.Add(ClanStatsPayloadBuilder.BuildClanMemberStatsForClan(clanDoc));
                }

                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendException(guid, 500, "internal_error");
            }
        }

        public void GetClanStatsForSeason(BinaryValue[] value, string guid)
        {
            try
            {
                var request = ParseRequest(value, GetClanStatsForSeasonRequest.Parser);
                ClanDocument clanDoc = null;
                string clanId = request.ClanId;

                if (!string.IsNullOrWhiteSpace(clanId))
                {
                    clanDoc = BoltMainDatabaseProvider.Instance.GetClanDocument(clanId);
                }
                else if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    var playerDoc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(playerId));
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
                    response.ClanMemberStats.Add(ClanStatsPayloadBuilder.BuildClanMemberStatsForClan(clanDoc));
                }

                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendException(guid, 500, "internal_error");
            }
        }

        private void SendException(string guid, int code, string errorInfo)
        {
            SendResponse(guid, new Axlebolt.RpcSupport.Protobuf.Exception
            {
                Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                Code = code
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

        private static T ParseRequest<T>(BinaryValue[] values, MessageParser<T> parser)
            where T : IMessage<T>, new()
        {
            if (values == null || values.Length == 0 || values[0]?.One == null || values[0].One.Length == 0)
            {
                return new T();
            }
            return parser.ParseFrom(values[0].One.ToByteArray());
        }
    }
}
