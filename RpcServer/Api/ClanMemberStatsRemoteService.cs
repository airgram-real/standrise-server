using System;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;

using Axlebolt.Bolt.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("ClanMemberStatsRemoteService")]
    public class ClanMemberStatsRemoteService : RpcClass
    {
        public ClanMemberStatsRemoteService(UserService user) : base(user) { }

        public override void Invoke(RpcRequest request)
        {
            try
            {
                switch (request.MethodName)
                {
                    case "getCurrentClanMemberStats":
                        GetCurrentClanMemberStats(request.Params.ToArray(), request.Id);
                        break;
                    case "getClanMembersStats":
                        GetClanMembersStats(request.Params.ToArray(), request.Id);
                        break;
                    case "saveCurrentClanMemberStats":
                        SaveCurrentClanMemberStats(request.Params.ToArray(), request.Id);
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

        public void GetCurrentClanMemberStats(BinaryValue[] value, string guid)
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

                // Default empty response
                var response = new GetCurrentClanMemberStatsResponse
                {
                    ClanId = clanId,
                    ClanMemberStats = ClanStatsPayloadBuilder.BuildClanMemberStats(Id)
                };

                SendResponse(guid, response);
            }
        }

        public void GetClanMembersStats(BinaryValue[] value, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                var req = GetClanMembersStatsRequest.Parser.ParseFrom(value[0].One.ToByteArray());

                var response = new GetClanMembersStatsResponse
                {
                    ClanId = req.ClanId
                };

                ClanDocument clanDocument = null;
                if (!string.IsNullOrWhiteSpace(req.ClanId))
                {
                    clanDocument = BoltMainDatabaseProvider.Instance.GetClanDocument(req.ClanId);
                }
                else
                {
                    var playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(Id));
                    clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);
                    if (clanDocument != null)
                    {
                        response.ClanId = clanDocument._id.ToString();
                    }
                }

                if (clanDocument != null)
                {
                    response.ClanMembersStats.Add(ClanStatsPayloadBuilder.BuildClanMemberStatsForClan(clanDocument));
                }

                SendResponse(guid, response);
            }
        }
        
        public void SaveCurrentClanMemberStats(BinaryValue[] value, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                var req = SaveCurrentClanMemberStatsRequest.Parser.ParseFrom(value[0].One.ToByteArray());

                if (req?.ClanMemberStats != null)
                {
                    foreach (var stat in req.ClanMemberStats.Stats)
                    {
                        if (string.IsNullOrWhiteSpace(stat.StatId) ||
                            !stat.StatId.StartsWith("clan_member_ranked_", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (stat.Type == StatDefType.Float)
                        {
                            BoltGameDatabaseProvider.Instance.SetPlayerStat(Id, stat.StatId, stat.FloatValue);
                        }
                        else if (stat.LongValue != 0)
                        {
                            BoltGameDatabaseProvider.Instance.SetPlayerStat(Id, stat.StatId, stat.LongValue);
                        }
                        else
                        {
                            BoltGameDatabaseProvider.Instance.SetPlayerStat(Id, stat.StatId, stat.IntValue);
                        }
                    }
                }

                var response = new SaveCurrentClanMemberStatsResponse();

                SendResponse(guid, response);
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
    }
}
