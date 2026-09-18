using System;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using StandRiseServer.MongoDB;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("GameSeasonRemoteService")]
    public class GameSeasonRemoteService : RpcClass
    {
        public GameSeasonRemoteService(UserService user) : base(user) { }

        public override async Task InvokeAsync(RpcRequest request)
        {
            string methodName = request.MethodName?.ToLowerInvariant() ?? string.Empty;
            switch (methodName)
            {
                case "getgameseasons":
                case "getgameseasons2":
                    await GetGameSeasons(request.Params.ToArray(), request.Id);
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

        private static string ReadSeasonId(BinaryValue[] values)
        {
            if (values == null || values.Length == 0)
                return string.Empty;

            try
            {
                var plain = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                if (!string.IsNullOrWhiteSpace(plain))
                    return plain;
            }
            catch {}

            if (values[0]?.One == null)
                return string.Empty;

            try
            {
                using var stream = new System.IO.MemoryStream(values[0].One.ToByteArray());
                var input = new CodedInputStream(stream);
                while (!input.IsAtEnd)
                {
                    uint tag = input.ReadTag();
                    if (tag == 0) break;
                    if (tag == 10)
                    {
                        string id = input.ReadString();
                        if (!string.IsNullOrWhiteSpace(id))
                            return id;
                    }
                    else
                        input.SkipLastField();
                }
            }
            catch {}

            return string.Empty;
        }

        private Task GetGameSeasons(BinaryValue[] values, string guid)
        {
            try
            {
                string seasonId = ReadSeasonId(values);
                Logger.Log($"[SeasonProbe] getGameSeasons requested='{seasonId}'");
                if (string.IsNullOrWhiteSpace(seasonId))
                    seasonId = "SEASON_03";

                var response = new GetGameSeasonsResponse();

                string normalizedId = seasonId.ToUpperInvariant().Replace("SEASON_", "").Trim();
                if (normalizedId.Length == 1 && char.IsDigit(normalizedId[0]))
                    normalizedId = normalizedId;

                response.Seasons.Add(new GameSeason { Id = "3", Name = "Season 3" });
                response.Seasons.Add(new GameSeason { Id = "2", Name = "Season 2" });
                response.Seasons.Add(new GameSeason { Id = "1", Name = "Season 1" });

                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }

            return Task.CompletedTask;
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
