using System;
using System.Collections.Generic;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("SeasonRemoteService")]
    public class SeasonRemoteService : RpcClass
    {
        public SeasonRemoteService(UserService user) : base(user) { }

        protected void GetCurrentSeason(string guid)
        {
            try
            {
                var response = new GetGameSeasonsResponse();
                response.Seasons.Add(new GameSeason { Id = "3", Name = "Season 3" });

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new BinaryValue { One = response.ToByteString() }
                    }
                });
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private void GetAvailableSeasons(string guid)
        {
            try
            {
                var response = new GetGameSeasonsResponse();
                // Текущий сезон первым — клиент берёт default с начала списка.
                response.Seasons.Add(new GameSeason { Id = "3", Name = "Season 3" });
                response.Seasons.Add(new GameSeason { Id = "2", Name = "Season 2" });
                response.Seasons.Add(new GameSeason { Id = "1", Name = "Season 1" });

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new BinaryValue { One = response.ToByteString() }
                    }
                });
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        public override void Invoke(RpcRequest request)
        {
            Logger.Log($"[SeasonProbe] SeasonRemoteService.{request.MethodName}");
            switch (request.MethodName)
            {
                case "getCurrentSeason":
                    GetCurrentSeason(request.Id);
                    break;
                case "getAvailableSeasons":
                case "getSeasons":
                    GetAvailableSeasons(request.Id);
                    break;
                default:
                    MethodNotFound(request);
                    break;
            }
        }
    }
}
