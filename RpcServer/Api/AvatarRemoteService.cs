using System;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using MongoDB.Bson;

namespace StandRiseServer.RpcServer.Api
{
    public class AvatarRemoteService : RpcClass
    {
        private static byte[] WrapRepeatedMessages<TMessage>(System.Collections.Generic.IEnumerable<TMessage> messages) where TMessage : Google.Protobuf.IMessage
        {
            if (messages == null)
            {
                return new byte[0];
            }
            using (var stream = new System.IO.MemoryStream())
            {
                using (var output = new Google.Protobuf.CodedOutputStream(stream))
                {
                    foreach (var message in messages)
                    {
                        if (message == null)
                            continue;
                        output.WriteRawTag(10);
                        output.WriteBytes(Google.Protobuf.MessageExtensions.ToByteString(message));
                    }
                    output.Flush();
                    return stream.ToArray();
                }
            }
        }
    
        public AvatarRemoteService(UserService user) : base(user)
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
            Console.WriteLine($"AvatarRemoteService.{request.MethodName} called, playerId: {playerId}");

            try
            {
                string method = request.MethodName.ToLowerInvariant();
                switch (method)
                {
                    case "getavatars":
                    case "getavatars2":
                        GetAvatars(request.Params.ToArray(), request.Id, request.MethodName);
                        break;
                    case "getdefaultavatars":
                    case "getdefaultavatars2":
                        GetDefaultAvatars(request.Params.ToArray(), request.Id);
                        break;
                    default:
                        Console.WriteLine($"AvatarRemoteService: Unknown method {request.MethodName}");
                        ReturnError(request.Id, 404);
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"AvatarRemoteService.{request.MethodName} error: {ex.Message}");
                ReturnError(request.Id, 500);
            }
        }

        private const string DefaultAvatarsVersion = "0.28.1";

        private static readonly string[] AvatarIds = new[]
        {
            "avatar_default_01",
            "avatar_default_02",
            "avatar_default_03",
            "avatar_event_halloween",
            "avatar_event_new_year"
        };

        private void GetAvatars(BinaryValue[] values, string guid, string methodName)
        {
            Console.WriteLine("AvatarRemoteService.GetAvatars called");
            string[] ids = null;
            if (methodName != null && methodName.EndsWith("2") && values != null && values.Length > 0 && !values[0].IsNull && values[0].One != null)
            {
                var list = new System.Collections.Generic.List<string>();
                try
                {
                    using var input = new Google.Protobuf.CodedInputStream(values[0].One.ToByteArray());
                    while (!input.IsAtEnd)
                    {
                        uint tag = input.ReadTag();
                        if (tag == 0) break;
                        if (tag == 10) // Tag 1 (Repeated string)
                        {
                            list.Add(input.ReadString());
                        }
                        else
                        {
                            input.SkipLastField();
                        }
                    }
                }
                catch { }
                ids = list.ToArray();
            }
            else
            {
                ids = (string[])new FromByteMethod(typeof(string[])).FromBytes(values[0]);
            }
            
            var avatars = BoltGameDatabaseProvider.Instance.GetAvatars(ids);
            
            BinaryValue returnVal;
            if (methodName != null && methodName.EndsWith("2"))
            {
                returnVal = new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(WrapRepeatedMessages(avatars)) };
            }
            else
            {
                returnVal = new ToByteMethod(typeof(AvatarBinary[])).ToBytes(avatars);
            }
            
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = returnVal
                }
            });
        }

        private void GetDefaultAvatars(BinaryValue[] values, string guid)
        {
            string clientLastUpdated = "";
            if (values != null && values.Length > 0 && !values[0].IsNull)
            {
                if (values[0].One != null && values[0].One.Length > 0)
                {
                    try
                    {
                        using var input = new Google.Protobuf.CodedInputStream(values[0].One.ToByteArray());
                        while (true)
                        {
                            var tag = input.ReadTag();
                            if (tag == 0) break;
                            if (tag == 10)
                            {
                                clientLastUpdated = input.ReadString()?.Trim() ?? "";
                            }
                            else
                            {
                                input.SkipLastField();
                            }
                        }
                    }
                    catch {}
                }
            }

            var includeAvatars = !clientLastUpdated.Equals(DefaultAvatarsVersion, StringComparison.Ordinal);
            var responseBytes = BuildDefaultAvatarsResponse(
                includeAvatars ? AvatarIds : System.Array.Empty<string>(),
                DefaultAvatarsVersion);

            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue
                    {
                        IsNull = false,
                        One = Google.Protobuf.ByteString.CopyFrom(responseBytes)
                    }
                }
            });
        }

        private static byte[] BuildDefaultAvatarsResponse(System.Collections.Generic.IEnumerable<string> avatarIds, string lastUpdated)
        {
            using var stream = new System.IO.MemoryStream();
            using var output = new Google.Protobuf.CodedOutputStream(stream);

            foreach (var avatarId in avatarIds)
            {
                if (string.IsNullOrWhiteSpace(avatarId)) continue;
                output.WriteRawTag(10);
                output.WriteBytes(Google.Protobuf.ByteString.CopyFrom(BuildDefaultAvatar(avatarId.Trim())));
            }

            if (!string.IsNullOrWhiteSpace(lastUpdated))
            {
                output.WriteRawTag(18);
                output.WriteString(lastUpdated);
            }

            output.Flush();
            return stream.ToArray();
        }

        private static byte[] BuildDefaultAvatar(string avatarId)
        {
            using var stream = new System.IO.MemoryStream();
            using var output = new Google.Protobuf.CodedOutputStream(stream);
            output.WriteRawTag(10);
            output.WriteString(avatarId);
            output.Flush();
            return stream.ToArray();
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
