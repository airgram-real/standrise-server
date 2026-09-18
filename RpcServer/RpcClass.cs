using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.RpcServer.Api;

namespace StandRiseServer.RpcServer
{
    public abstract class RpcClass
    {
        public UserService _user;
        public RpcClass(UserService user)
        {
            _user = user;
        }

        public string PlayerId
        {
            get
            {
                if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    return playerId;
                }
                return null;
            }
        }

        public global::MongoDB.Bson.ObjectId PlayerObjectId => PlayerId != null ? global::MongoDB.Bson.ObjectId.Parse(PlayerId) : global::MongoDB.Bson.ObjectId.Empty;

        public static async Task Async(Action action)
        {
            var task = Task.Factory.StartNew(action);
            await task;
        }

        public virtual Task InvokeAsync(RpcRequest request)
        {
            Invoke(request);
            return Task.CompletedTask;
        }
        public abstract void Invoke(RpcRequest request);

        public void SendResponse<T>(string guid, T value)
        {
            var response = new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid
                }
            };

            if (value == null)
            {
                response.RpcResponse.Return = new BinaryValue { IsNull = true };
            }
            else
            {
                response.RpcResponse.Return = ProtoReflectionUtils.CreateToByteMethod(typeof(T)).ToBytes(value);
            }

            _user.SendResponce(response);
        }

        public void SendResponse(string guid)
        {
            SendResponse<object>(guid, null);
        }

        public void SendError(string guid, int code)
        {
            if (code == 500)
            {
                Console.WriteLine($"\n[EXPLICIT 500] Sent 500 error for Request ID {guid}");
                Console.WriteLine(new System.Diagnostics.StackTrace(true).ToString());
            }

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

        public void SendEventToSession<T>(string playerId, string eventName, object eventObj) where T : IEventSender
        {
            object[] args = eventObj is object[] paramArray ? paramArray : new object[] { eventObj };
            if (StaticClasses.TryGetEventSenders(playerId, out List<IEventSender> eventSenders))
            {
                var sender = eventSenders.FirstOrDefault(a => a is T);
                if (sender != null)
                {
                    sender.SendEvent(eventName, args);
                    return;
                }
            }
            var live = StaticClasses.ResolveEventDeliveryService(playerId);
            if (live == null) return;
            if (typeof(T) == typeof(MatchesRemoteEventListener))
            {
                new MatchesRemoteEventListener(live).SendEvent(eventName, args);
            }
            else if (typeof(T) == typeof(PlayerStatsRemoteEventListener))
            {
                new PlayerStatsRemoteEventListener(live).SendEvent(eventName, args);
            }
            else if (typeof(T) == typeof(InventoryRemoteEventListener))
            {
                new InventoryRemoteEventListener(live).SendEvent(eventName, args);
            }
        }

        public void MethodNotFound(RpcRequest request)
        {
            Console.WriteLine($"[RPC] Method not found: {request.ServiceName}.{request.MethodName}");
            SendError(request.Id, 404);
        }
    }
}
