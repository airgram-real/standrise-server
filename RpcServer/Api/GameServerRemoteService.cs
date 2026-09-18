using System;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using StandRiseServer.MongoDB;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("GameServerRemoteService")]
    public class GameServerRemoteService : RpcClass
    {
        public GameServerRemoteService(UserService user) : base(user) { }

        public override async Task InvokeAsync(RpcRequest request)
        {
            switch (request.MethodName)
            {
                case "serverHandshake":
                    await DoServerHandshake(request.Params.ToArray(), request.Id);
                    break;
                case "logout":
                    await Logout(request.Id);
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

        private async Task DoServerHandshake(BinaryValue[] values, string guid)
        {
            try
            {
                var handshake = Axlebolt.Bolt.Protobuf.ServerHandshake.Parser.ParseFrom(values[0].One.ToByteArray());
                _user.GameServerToken = handshake.ApiKey;
                StaticClasses.GameServerUsers.Add(_user.TcpClient);
                Console.WriteLine($"[GS] Server Handshake: GameId={handshake.GameId}, Version={handshake.Version}");
                SendResponse(guid);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task Logout(string guid)
        {
            try
            {
                StaticClasses.GameServerUsers.Remove(_user.TcpClient);
                SendResponse(guid);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private void SendResponse(string guid, IMessage response = null)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = response != null ? new BinaryValue { One = response.ToByteString() } : new BinaryValue { IsNull = true }
                }
            });
        }

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in GameServerRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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
