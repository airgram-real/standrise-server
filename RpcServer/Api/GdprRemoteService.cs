using System;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using StandRiseServer.MongoDB;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("GdprRemoteService")]
    public class GdprRemoteService : RpcClass
    {
        public GdprRemoteService(UserService user) : base(user) { }

        public override async Task InvokeAsync(RpcRequest request)
        {
            switch (request.MethodName)
            {
                case "getRequestsEncrypted":
                    await GetRequestsEncrypted(request.Params.ToArray(), request.Id);
                    break;
                case "createRequestEncrypted":
                    await CreateRequestEncrypted(request.Params.ToArray(), request.Id);
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

        private async Task GetRequestsEncrypted(BinaryValue[] values, string guid)
        {
            try
            {
                // Note: Encrypted versions involve complex logic, for now providing skeleton
                var response = new GetRequestsEncryptedResponse();
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task CreateRequestEncrypted(BinaryValue[] values, string guid)
        {
            try
            {
                var response = new CreateRequestEncryptedResponse();
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
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in GdprRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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
