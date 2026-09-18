using System;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using StandRiseServer.MongoDB;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("RateGameRemoteService")]
    public class RateGameRemoteService : RpcClass
    {
        public RateGameRemoteService(UserService user) : base(user) { }

        public override async Task InvokeAsync(RpcRequest request)
        {
            switch (request.MethodName)
            {
                case "getLastRateGame":
                    await GetLastRateGame(request.Params.ToArray(), request.Id);
                    break;
                case "askLater":
                    await AskLater(request.Params.ToArray(), request.Id);
                    break;
                case "dontAskLater":
                    await DontAskLater(request.Params.ToArray(), request.Id);
                    break;
                case "rateGame":
                    await RateGame(request.Params.ToArray(), request.Id);
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

        private async Task GetLastRateGame(BinaryValue[] values, string guid)
        {
            try
            {
                var response = new GetLastRateGameResponse();
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task AskLater(BinaryValue[] values, string guid)
        {
            try
            {
                SendResponse(guid, new AskLaterResponse());
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task DontAskLater(BinaryValue[] values, string guid)
        {
            try
            {
                SendResponse(guid, new DontAskLaterResponse());
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task RateGame(BinaryValue[] values, string guid)
        {
            try
            {
                SendResponse(guid, new RateGameResponse());
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
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in RateGameRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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
