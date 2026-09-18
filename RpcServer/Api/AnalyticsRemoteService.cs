using System;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using StandRiseServer.MongoDB;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("AnalyticsRemoteService")]
    public class AnalyticsRemoteService : RpcClass
    {
        public AnalyticsRemoteService(UserService user) : base(user) { }

        public override async Task InvokeAsync(RpcRequest request)
        {
            switch (request.MethodName)
            {
                case "event":
                    await Event(request.Params.ToArray(), request.Id);
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

        private async Task Event(BinaryValue[] values, string guid)
        {
            try
            {
                if (values == null || values.Length == 0 || values[0] == null || values[0].IsNull)
                {
                    SendResponse(guid);
                    return;
                }
                var userEvents = (UserEvent[])new FromByteMethod(typeof(UserEvent[])).FromBytes(values[0]);
                if (userEvents != null)
                {
                    foreach (var ev in userEvents)
                    {
                        if (ev != null)
                            Console.WriteLine($"[Analytics] Event: {ev.Event ?? "null"}, Player={PlayerId}");
                    }
                }
                SendResponse(guid);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendResponse(guid);
            }
        }

        private void SendResponse(string guid)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { IsNull = true }
                }
            });
        }

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in AnalyticsRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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
