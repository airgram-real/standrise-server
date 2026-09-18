using System;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;

namespace StandRiseServer.RpcServer.Api
{
    public class GlobalChatRemoteService : RpcClass
    {
        public GlobalChatRemoteService(UserService user) : base(user)
        {
        }

        private string GetPlayerId()
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
            {
                return playerId;
            }
            return null;
        }

        public override void Invoke(RpcRequest request)
        {
            string playerId = GetPlayerId();
            Console.WriteLine($"GlobalChatRemoteService.{request.MethodName} called, playerId: {playerId}");

            try
            {
                switch (request.MethodName)
                {
                    case "sendGlobalChatMessage":
                        SendGlobalChatMessage(request.Params.ToArray(), request.Id, playerId);
                        break;
                    default:
                        Console.WriteLine($"GlobalChatRemoteService: Unknown method {request.MethodName}");
                        ReturnError(request.Id, 404);
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"GlobalChatRemoteService.{request.MethodName} error: {ex.Message}");
                ReturnError(request.Id, 500);
            }
        }

        private void SendGlobalChatMessage(BinaryValue[] values, string guid, string playerId)
        {
            Console.WriteLine($"SendGlobalChatMessage called by {playerId}");
            // TODO: Implement global chat message broadcasting
            ReturnEmpty(guid);
        }

        private void ReturnEmpty(string guid)
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

        private void ReturnError(string guid, int code)
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
