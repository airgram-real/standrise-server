using System;
using System.Threading.Tasks;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;

namespace StandRiseServer.RpcServer.Api
{
    public class BoltRemoteService : RpcClass
    {
        public BoltRemoteService(UserService user) : base(user)
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

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in BoltRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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

        private void SendResponse(string guid, IMessage response)
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

        private void SendResponseRaw(string guid, BinaryValue data)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = data
                }
            });
        }

        public override async Task InvokeAsync(RpcRequest request)
        {
            string playerId = GetPlayerId();

            try
            {
                switch (request.MethodName)
                {
                    case "subscribe":
                        await Subscribe(request.Params.ToArray(), request.Id, playerId);
                        break;
                    case "subscribe2":
                        await Subscribe2(request.Params.ToArray(), request.Id, playerId);
                        break;
                    case "unsubscribe":
                        await Unsubscribe(request.Params.ToArray(), request.Id, playerId);
                        break;
                    case "unsubscribe2":
                        await Unsubscribe2(request.Params.ToArray(), request.Id, playerId);
                        break;
                    case "systemTime":
                        await SystemTime(request.Id);
                        break;
                    default:
                        MethodNotFound(request);
                        break;
                }
            }
            catch (System.Exception ex)
            {
                SendError(request.Id, 500);
            }
        }

        public override void Invoke(RpcRequest request)
        {
            _ = InvokeAsync(request);
        }

        private Task Subscribe(BinaryValue[] values, string guid, string playerId)
        {
            if (values.Length == 0 || values[0].IsNull)
            {
                SendResponse(guid, null);
                return Task.CompletedTask;
            }

            string topic = "";
            try
            {
                if (guid.EndsWith("2")) {
                    var input = new Google.Protobuf.CodedInputStream(values[0].One.ToByteArray());
                    uint tag;
                    while ((tag = input.ReadTag()) != 0) {
                        if (tag == 10) { topic = input.ReadString(); break; }
                        else input.SkipLastField();
                    }
                } else {
                    topic = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                }
            }
            catch
            {
                try
                {
                    var input = new CodedInputStream(values[0].One.ToByteArray());
                    uint tag;
                    while ((tag = input.ReadTag()) != 0)
                    {
                        int fieldNumber = (int)(tag >> 3);
                        int wireType = (int)(tag & 7);
                        if (fieldNumber == 1 && wireType == 2)
                        {
                            topic = input.ReadString();
                        }
                        else
                        {
                            input.SkipLastField();
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine($"[BoltRemoteService] Error parsing subscribe topic fallback: {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(playerId))
            {
                SendResponse(guid, null);
                return Task.CompletedTask;
            }

            // Remove old subscriptions
            StaticClasses.Subscribes.TryRemove(playerId, out _);
            StaticClasses.SubscribesTrades.TryRemove(playerId, out _);

            // Add new subscription based on topic
            if (topic.Contains("marketplace_trade_"))
            {
                string request = topic.Replace("marketplace_trade_", "");
                if (int.TryParse(request, out int tradeId))
                {
                    StaticClasses.Subscribes[playerId] = tradeId;
                }
            }
            else if (topic.Contains("marketplace_trades"))
            {
                StaticClasses.SubscribesTrades[playerId] = 0;
            }
            else if (topic.StartsWith("global_chat_"))
            {
                string[] parts = topic.Substring("global_chat_".Length).Split('_');
                if (parts.Length >= 1 && int.TryParse(parts[^1], out int room))
                {
                    StaticClasses.SubscribesGlobalChat[playerId] = room;
                }
            }

            SendResponse(guid, null);
            return Task.CompletedTask;
        }

        private Task Unsubscribe(BinaryValue[] values, string guid, string playerId)
        {
            if (values.Length == 0 || values[0].IsNull)
            {
                SendResponse(guid, null);
                return Task.CompletedTask;
            }

            string topic = "";
            try
            {
                if (guid.EndsWith("2")) {
                    var input = new Google.Protobuf.CodedInputStream(values[0].One.ToByteArray());
                    uint tag;
                    while ((tag = input.ReadTag()) != 0) {
                        if (tag == 10) { topic = input.ReadString(); break; }
                        else input.SkipLastField();
                    }
                } else {
                    topic = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                }
            }
            catch
            {
                try
                {
                    var input = new CodedInputStream(values[0].One.ToByteArray());
                    uint tag;
                    while ((tag = input.ReadTag()) != 0)
                    {
                        int fieldNumber = (int)(tag >> 3);
                        int wireType = (int)(tag & 7);
                        if (fieldNumber == 1 && wireType == 2)
                        {
                            topic = input.ReadString();
                        }
                        else
                        {
                            input.SkipLastField();
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine($"[BoltRemoteService] Error parsing unsubscribe topic fallback: {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(playerId))
            {
                SendResponse(guid, null);
                return Task.CompletedTask;
            }

            if (topic.Contains("marketplace_trade_"))
            {
                StaticClasses.Subscribes.TryRemove(playerId, out _);
            }
            else if (topic.Contains("marketplace_trades"))
            {
                StaticClasses.SubscribesTrades.TryRemove(playerId, out _);
            }
            else if (topic.Contains("global_chat_") && StaticClasses.SubscribesGlobalChat.ContainsKey(playerId))
            {
                StaticClasses.SubscribesGlobalChat.TryRemove(playerId, out _);
            }

            SendResponse(guid, null);
            return Task.CompletedTask;
        }

        private Task SystemTime(string guid)
        {
            long time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SendResponseRaw(guid, new ToByteMethod(typeof(long)).ToBytes(time));
            return Task.CompletedTask;
        }

        private Task Subscribe2(BinaryValue[] values, string guid, string playerId)
        {
            if (values.Length == 0 || values[0].IsNull)
            {
                SendResponseRaw(guid, new BinaryValue { IsNull = false, One = ByteString.Empty });
                return Task.CompletedTask;
            }

            string topic = "";
            try
            {
                if (guid.EndsWith("2")) {
                    var input = new Google.Protobuf.CodedInputStream(values[0].One.ToByteArray());
                    uint tag;
                    while ((tag = input.ReadTag()) != 0) {
                        if (tag == 10) { topic = input.ReadString(); break; }
                        else input.SkipLastField();
                    }
                } else {
                    topic = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[BoltRemoteService] Error parsing subscribe2 topic: {ex.Message}");
            }
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(topic))
            {
                SendResponseRaw(guid, new BinaryValue { IsNull = false, One = ByteString.Empty });
                return Task.CompletedTask;
            }

            // Remove old subscriptions
            StaticClasses.Subscribes.TryRemove(playerId, out _);
            StaticClasses.SubscribesTrades.TryRemove(playerId, out _);

            // Add new subscription based on topic
            if (topic.Contains("marketplace_trade_"))
            {
                string request = topic.Replace("marketplace_trade_", "");
                if (int.TryParse(request, out int tradeId))
                {
                    StaticClasses.Subscribes[playerId] = tradeId;
                }
            }
            else if (topic.Contains("marketplace_trades"))
            {
                StaticClasses.SubscribesTrades[playerId] = 0;
            }
            else if (topic.StartsWith("global_chat_"))
            {
                string[] parts = topic.Substring("global_chat_".Length).Split('_');
                if (parts.Length >= 1 && int.TryParse(parts[^1], out int room))
                {
                    StaticClasses.SubscribesGlobalChat[playerId] = room;
                }
            }

            SendResponseRaw(guid, new BinaryValue { IsNull = false, One = ByteString.Empty });
            return Task.CompletedTask;
        }

        private Task Unsubscribe2(BinaryValue[] values, string guid, string playerId)
        {
            if (values.Length == 0 || values[0].IsNull)
            {
                SendResponseRaw(guid, new BinaryValue { IsNull = false, One = ByteString.Empty });
                return Task.CompletedTask;
            }

            string topic = "";
            try
            {
                if (guid.EndsWith("2")) {
                    var input = new Google.Protobuf.CodedInputStream(values[0].One.ToByteArray());
                    uint tag;
                    while ((tag = input.ReadTag()) != 0) {
                        if (tag == 10) { topic = input.ReadString(); break; }
                        else input.SkipLastField();
                    }
                } else {
                    topic = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[BoltRemoteService] Error parsing unsubscribe2 topic: {ex.Message}");
            }
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(topic))
            {
                SendResponseRaw(guid, new BinaryValue { IsNull = false, One = ByteString.Empty });
                return Task.CompletedTask;
            }

            if (topic.Contains("marketplace_trade_"))
            {
                StaticClasses.Subscribes.TryRemove(playerId, out _);
            }
            else if (topic.Contains("marketplace_trades"))
            {
                StaticClasses.SubscribesTrades.TryRemove(playerId, out _);
            }
            else if (topic.Contains("global_chat_") && StaticClasses.SubscribesGlobalChat.ContainsKey(playerId))
            {
                StaticClasses.SubscribesGlobalChat.TryRemove(playerId, out _);
            }

            SendResponseRaw(guid, new BinaryValue { IsNull = false, One = ByteString.Empty });
            return Task.CompletedTask;
        }
    }
}
