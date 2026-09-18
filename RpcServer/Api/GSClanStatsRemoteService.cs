using System;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using Google.Protobuf;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("GSClanStatsRemoteService")]
    public class GSClanStatsRemoteService : RpcClass
    {
        public GSClanStatsRemoteService(UserService user) : base(user) { }

        public override async Task InvokeAsync(RpcRequest request)
        {
            switch (request.MethodName)
            {
                case "saveClanStats":
                    await SaveClanStats(request.Params.ToArray(), request.Id);
                    break;
                case "getClanStats":
                    await GetClanStats(request.Params.ToArray(), request.Id);
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

        private async Task SaveClanStats(BinaryValue[] values, string guid)
        {
            try
            {
                var request = GSSaveClanStatsRequest.Parser.ParseFrom(values[0].One.ToByteArray());
                var db = BoltMainDatabaseProvider.Instance;
                
                foreach (var clanStats in request.ClanStats)
                {
                    var clanDoc = db.GetClanDocument(clanStats.ClanId);
                    if (clanDoc != null)
                    {
                        var statsList = clanStats.Stats.Select(s => new BsonElement(s.StatId, s.Type == StatDefType.Int ? (BsonValue)s.IntValue : (BsonValue)s.FloatValue)).ToList();
                        db.StoreClanStats(clanStats.ClanId, statsList);
                    }
                }

                SendResponse(guid, new GSSaveClanStatsResponse());
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetClanStats(BinaryValue[] values, string guid)
        {
            try
            {
                var request = GSGetClanStatsRequest.Parser.ParseFrom(values[0].One.ToByteArray());
                var db = BoltMainDatabaseProvider.Instance;
                var response = new GSGetClanStatsResponse();

                foreach (var clanId in request.ClanIds)
                {
                    var clanDoc = db.GetClanDocument(clanId);
                    var clanStats = clanDoc != null
                        ? ClanStatsPayloadBuilder.BuildClanStats(clanDoc, "3")
                        : new Axlebolt.Bolt.Protobuf.ClanStats { ClanId = clanId, SeasonId = "3" };

                    response.ClanStats.Add(clanStats);
                }

                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
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
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in GSClanStatsRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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
