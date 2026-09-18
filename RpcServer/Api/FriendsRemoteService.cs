using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using MongoDB.Bson;

namespace StandRiseServer.RpcServer.Api
{
    public class FriendsRemoteService : RpcClass
    {

        private string ExtractStringFromWrapper(BinaryValue value)
        {
            if (value == null || value.IsNull) return string.Empty;
            byte[] extracted = Utils.ExtractBinaryValueOne(value.One.ToByteArray());
            return System.Text.Encoding.UTF8.GetString(extracted);
        }

        private BinaryValue WrapEnumResponse(int enumValue)
        {
            using (var stream = new System.IO.MemoryStream())
            using (var output = new CodedOutputStream(stream))
            {
                output.WriteRawTag(8);
                output.WriteInt32(enumValue);
                output.Flush();
                return new BinaryValue { IsNull = false, One = ByteString.CopyFrom(stream.ToArray()) };
            }
        }
        
        private BinaryValue WrapBoolResponse(bool value)
        {
            using (var stream = new System.IO.MemoryStream())
            using (var output = new CodedOutputStream(stream))
            {
                output.WriteRawTag(8);
                output.WriteBool(value);
                output.Flush();
                return new BinaryValue { IsNull = false, One = ByteString.CopyFrom(stream.ToArray()) };
            }
        }
        
        private BinaryValue WrapLongResponse(long value)
        {
            using (var stream = new System.IO.MemoryStream())
            using (var output = new CodedOutputStream(stream))
            {
                output.WriteRawTag(8);
                output.WriteInt64(value);
                output.Flush();
                return new BinaryValue { IsNull = false, One = ByteString.CopyFrom(stream.ToArray()) };
            }
        }
        public FriendsRemoteService(UserService user) : base(user)
        {
        }

        private byte[] WrapMessageResponse(Google.Protobuf.IMessage message)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                var output = new Google.Protobuf.CodedOutputStream(stream);
                output.WriteRawTag(10);
                output.WriteMessage(message);
                output.Flush();
                return stream.ToArray();
            }
        }

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in FriendsRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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
                    Return = new BinaryValue { One = response.ToByteString() }
                }
            });
        }

        private static void InvalidateFriendCaches(string playerId, string friendId)
        {
            FriendHelper.InvalidatePlayerFriends(new[] { playerId, friendId });
            FriendHelper.InvalidateFriendRelation(playerId, friendId);
        }

        public async Task getAvatars(BinaryValue[] value, string guid)
        {
            FromByteMethod from = new FromByteMethod(typeof(string[]));
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                string[] ids = (string[])from.FromBytes(value[0]);
                var avatars = BoltGameDatabaseProvider.Instance.GetAvatars(ids);
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(AvatarBinary[])).ToBytes(avatars)
                    }
                });
                return;
            }
            SendError(guid, 401);
        }
        private static byte[] WrapRepeatedStrings(IEnumerable<string> values)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                using (var output = new CodedOutputStream(stream))
                {
                    foreach (var value in values)
                    {
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        output.WriteRawTag(10);
                        output.WriteString(value);
                    }

                    output.Flush();
                    return stream.ToArray();
                }
            }
        }

        private static byte[] WrapRepeatedMessages<TMessage>(IEnumerable<TMessage> messages)
            where TMessage : Google.Protobuf.IMessage
        {
            using (var stream = new System.IO.MemoryStream())
            {
                using (var output = new CodedOutputStream(stream))
                {
                    foreach (var message in messages)
                    {
                        if (message is null)
                        {
                            continue;
                        }

                        output.WriteRawTag(10);
                        output.WriteBytes(message.ToByteString());
                    }

                    output.Flush();
                    return stream.ToArray();
                }
            }
        }

        private RelationshipStatus[] ParseRelationshipStatuses(BinaryValue[] values, EnumFromByteMethod from, string methodName)
        {
            RelationshipStatus[] statuses = null;
            if (values != null && values.Length > 0 && values[0] != null && !values[0].IsNull)
            {
                if (!string.IsNullOrEmpty(methodName) && methodName.EndsWith("2") && values[0].One != null)
                {
                    var list = new List<RelationshipStatus>();
                    try
                    {
                        using var input = new Google.Protobuf.CodedInputStream(values[0].One.ToByteArray());
                        while (!input.IsAtEnd)
                        {
                            uint tag = input.ReadTag();
                            if (tag == 0) break;
                            if (tag == 8) // Unpacked enum
                            {
                                list.Add((RelationshipStatus)input.ReadEnum());
                            }
                            else if (tag == 10) // Packed enums (Tag 1, wire type 2)
                            {
                                byte[] packedData = input.ReadBytes().ToByteArray();
                                using var packedInput = new Google.Protobuf.CodedInputStream(packedData);
                                while (!packedInput.IsAtEnd)
                                {
                                    list.Add((RelationshipStatus)packedInput.ReadEnum());
                                }
                            }
                            else
                            {
                                input.SkipLastField();
                            }
                        }
                    }
                    catch { }
                    if (list.Count > 0) return list.ToArray();
                }

                try
                {
                    statuses = (RelationshipStatus[])from.FromBytes(values[0]);
                }
                catch
                {
                    try
                    {
                        var singleStatus = (RelationshipStatus)new EnumFromByteMethod(typeof(RelationshipStatus)).FromBytes(values[0]);
                        statuses = new[] { singleStatus };
                    }
                    catch
                    {
                        try
                        {
                            if (values[0].Array != null && values[0].Array.Count > 0)
                            {
                                var parsedList = new List<RelationshipStatus>();
                                foreach (var val in values[0].Array)
                                {
                                    try
                                    {
                                        var parsed = Axlebolt.RpcSupport.Protobuf.Enum.Parser.ParseFrom(val);
                                        parsedList.Add((RelationshipStatus)parsed.Value);
                                    }
                                    catch {}
                                }
                                statuses = parsedList.ToArray();
                            }
                        }
                        catch {}
                    }
                }
            }
            if (statuses == null || statuses.Length == 0) statuses = new[] { RelationshipStatus.Friend };
            return statuses;
        }

        protected async Task GetPlayerFriendsIds(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                EnumFromByteMethod from = new EnumFromByteMethod(typeof(RelationshipStatus[]));
                RelationshipStatus[] statuses = ParseRelationshipStatuses(values, from, methodName);
                List<FriendDocument> friendDocuments = await boltGame.GetPlayerFriends(Id);
                
                List<string> playerIds = friendDocuments
                    .Where(a => statuses.Contains(a.relationshipStatus.GetRelationshipStatus(a.playerInitiatorId == Id)))
                    .Select(a => a.playerInitiatorId == Id ? a.playerId : a.playerInitiatorId)
                    .ToList();

                if (methodName.Equals("getPlayerFriendsIds2", StringComparison.OrdinalIgnoreCase))
                {
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Return = new BinaryValue
                            {
                                IsNull = false,
                                One = ByteString.CopyFrom(WrapRepeatedStrings(playerIds))
                            }
                        }
                    });
                    return;
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(string[])).ToBytes(playerIds.ToArray())
                    }
                });
                return;
            }
            SendError(guid, 401);
        }

        protected async Task GetPlayerFriends(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                try
                {
                    BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                    EnumFromByteMethod from = new EnumFromByteMethod(typeof(RelationshipStatus[]));
                    RelationshipStatus[] statuses = ParseRelationshipStatuses(values, from, methodName);
                    int page = values != null && values.Length > 1 && values[1] != null && !values[1].IsNull ? (int)new FromByteMethod(typeof(int)).FromBytes(values[1]) : 0;
                    int size = values != null && values.Length > 2 && values[2] != null && !values[2].IsNull ? (int)new FromByteMethod(typeof(int)).FromBytes(values[2]) : 20;
                    
                    if (methodName.EndsWith("2") && values != null && values.Length > 0 && values[0] != null && !values[0].IsNull) {
                        var input = new CodedInputStream(values[0].One.ToByteArray());
                        uint tag;
                        while ((tag = input.ReadTag()) != 0) {
                            if (tag == 16) page = input.ReadInt32();
                            else if (tag == 24) size = input.ReadInt32();
                            else input.SkipLastField();
                        }
                    }
                
                    List<FriendDocument> friendDocuments = await boltGame.GetPlayerFriends(Id);
                    var filtered = friendDocuments
                        .Where(a => statuses.Contains(a.relationshipStatus.GetRelationshipStatus(a.playerInitiatorId == Id)))
                        .ToList();

                    var paged = filtered.Skip(page * size).Take(size).ToList();
                    List<PlayerFriend> playerFriends = new List<PlayerFriend>();
                    
                    foreach (var doc in paged)
                    {
                        playerFriends.Add(doc.GetPlayerFriend(Id));
                    }

                    if (methodName.Equals("getPlayerFriends2", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] wrapped = WrapRepeatedMessages(playerFriends);
                        _user.SendResponce(new ResponseMessage
                        {
                            RpcResponse = new RpcResponse
                            {
                                Id = guid,
                                Return = new BinaryValue
                                {
                                    IsNull = false,
                                    One = ByteString.CopyFrom(wrapped)
                                }
                            }
                        });
                        return;
                    }

                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Return = new ToByteMethod(typeof(PlayerFriend[])).ToBytes(playerFriends.ToArray())
                        }
                    });
                    return;
                }
                catch (System.Exception ex)
                {
                    System.Console.WriteLine($"\n[EXCEPTION] FriendsRemoteService.GetPlayerFriends: {ex}");
                    SendError(guid, 500);
                    return;
                }
            }
            SendError(guid, 401);
        }
        protected async Task GetPlayerFriendById(string playerId, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                Logger.Log($"[GetPlayerFriendById] Viewer: {Id}, Target: {playerId}");
                
                if (!FriendHelper.TryGetPlayer(playerId, out Player targetPlayer))
                {
                    Logger.Log($"[GetPlayerFriendById] Target player not found: {playerId}");
                    SendError(guid, 404);
                    return;
                }

                Logger.Log($"[GetPlayerFriendById] Target player found: {targetPlayer.Name}");
                Logger.Log($"[GetPlayerFriendById] Target player Attributes is null: {targetPlayer.Attributes == null}");
                if (targetPlayer.Attributes != null && targetPlayer.Attributes.Map != null)
                {
                    Logger.Log($"[GetPlayerFriendById] Target player Attributes count: {targetPlayer.Attributes.Map.Count}");
                    foreach (var kvp in targetPlayer.Attributes.Map)
                    {
                        string value = kvp.Value.String != null ? $"String='{kvp.Value.String}'" : $"Int={kvp.Value.Int}";
                        Logger.Log($"[GetPlayerFriendById]   Attribute: {kvp.Key} = {value}");
                    }
                }

                FriendDocument relationDocument = await BoltGameDatabaseProvider.Instance.GetFriendDocument(Id, playerId);
                RelationshipStatus relationshipStatus = RelationshipStatus.None;
                long lastRelationshipUpdate = 0L;
                if (relationDocument != null)
                {
                    bool viewerIsInitiator = relationDocument.playerInitiatorId == Id;
                    relationshipStatus = relationDocument.relationshipStatus.GetRelationshipStatus(viewerIsInitiator);
                    lastRelationshipUpdate = relationDocument.lastTimeUpdate.MillisecondsSinceEpoch;
                }

                PlayerFriend friend = new PlayerFriend
                {
                    Player = targetPlayer,
                    RelationshipStatus = relationshipStatus,
                    LastRelationshipUpdate = lastRelationshipUpdate
                };
                
                Logger.Log($"[GetPlayerFriendById] Sending PlayerFriend with RelationshipStatus: {relationshipStatus}");
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(PlayerFriend)).ToBytes(friend)
                    }
                });
                return;
            }
            SendError(guid, 401);
        }

        protected async Task GetPlayerFriendById2(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                try
                {
                    FriendActionRequest requestMsg = FriendActionRequest.Parser.ParseFrom(values[0].One);
                    string targetPlayerId = requestMsg.Value;
                    
                    if (!FriendHelper.TryGetPlayer(targetPlayerId, out Player targetPlayer))
                    {
                        Logger.Log($"[GetPlayerFriendById2] Target player not found: {targetPlayerId}");
                        SendError(guid, 404);
                        return;
                    }

                    FriendDocument relationDocument = await BoltGameDatabaseProvider.Instance.GetFriendDocument(Id, targetPlayerId);
                    RelationshipStatus relationshipStatus = RelationshipStatus.None;
                    long lastRelationshipUpdate = 0L;
                    if (relationDocument != null)
                    {
                        bool viewerIsInitiator = relationDocument.playerInitiatorId == Id;
                        relationshipStatus = relationDocument.relationshipStatus.GetRelationshipStatus(viewerIsInitiator);
                        lastRelationshipUpdate = relationDocument.lastTimeUpdate.MillisecondsSinceEpoch;
                    }

                    PlayerFriend friend = new PlayerFriend
                    {
                        Player = targetPlayer,
                        RelationshipStatus = relationshipStatus,
                        LastRelationshipUpdate = lastRelationshipUpdate
                    };

                    GetPlayerFriendResponse responseMsg = new GetPlayerFriendResponse { PlayerFriend = friend };

                    if (methodName != null && methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] wrapped = Google.Protobuf.MessageExtensions.ToByteArray(responseMsg);
                        _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(wrapped) } } });
                    }
                    else
                    {
                        SendResponse(guid, responseMsg);
                    }
                    return;
                }
                catch (System.Exception ex)
                {
                    Logger.Log($"[GetPlayerFriendById2] Error: {ex.Message}");
                    SendError(guid, 500);
                    return;
                }
            }
            SendError(guid, 401);
        }
        protected async Task SendFriendRequest(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                string friendId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                
                if (Id == friendId)
                {
                    SendError(guid, 400);
                    return;
                }

                FriendDocument friendDocument = await boltGame.GetFriendDocument(Id, friendId);
                
                RelationshipStatus resultStatus = RelationshipStatus.RequestRecipient;
                var me = await BoltMainDatabaseProvider.Instance.GetPlayerDocumentAsync(ObjectId.Parse(Id));

                if (friendDocument == null)
                {
                    friendDocument = boltGame.CreatePlayerFriendDocument(new FriendDocument { playerId = friendId, playerInitiatorId = Id, relationshipStatus = RelationshipStatusCustom.Request });
                    InvalidateFriendCaches(Id, friendId);
                    if (StaticClasses.EventSenders.TryGetValue(friendId, out var eventSenders))
                    {
                        eventSenders.FirstOrDefault(a => a is FriendsRemoteEventSender)?.SendEvent("onNewFriendshipRequest", new object[] { me.GetPlayerFriend(friendId) });
                    }
                }
                else 
                {
                    if (friendDocument.relationshipStatus == RelationshipStatusCustom.Friend)
                    {
                        resultStatus = RelationshipStatus.Friend;
                    }
                    else if (friendDocument.relationshipStatus == RelationshipStatusCustom.Blocked)
                    {
                        if (friendDocument.playerInitiatorId != Id)
                        {
                            SendError(guid, 3102); // Blocked by them
                            return;
                        }
                        // If blocked by me, we might want to unblock, but for now just error
                        SendError(guid, 400);
                        return;
                    }
                    else if (friendDocument.relationshipStatus == RelationshipStatusCustom.Request)
                    {
                        if (friendDocument.playerInitiatorId != Id)
                        {
                            // They requested me, I request them -> Friends!
                            friendDocument.relationshipStatus = RelationshipStatusCustom.Friend;
                            boltGame.UpdatePlayerFriendDocument(Id, friendId, friendDocument);
                            InvalidateFriendCaches(Id, friendId);
                            resultStatus = RelationshipStatus.Friend;
                            
                            if (StaticClasses.EventSenders.TryGetValue(friendId, out var eventSenders))
                            {
                                eventSenders.FirstOrDefault(a => a is FriendsRemoteEventSender)?.SendEvent("onFriendAdded", new object[] { me.GetPlayerFriend(friendId) });
                            }
                        }
                        else 
                        {
                            // Already requested by me
                            resultStatus = RelationshipStatus.RequestRecipient;
                        }
                    }
                    else // None or Ignored
                    {
                        friendDocument.playerInitiatorId = Id;
                        friendDocument.playerId = friendId;
                        friendDocument.relationshipStatus = RelationshipStatusCustom.Request;
                        boltGame.UpdatePlayerFriendDocument(Id, friendId, friendDocument);
                        InvalidateFriendCaches(Id, friendId);
                        
                        if (StaticClasses.EventSenders.TryGetValue(friendId, out var eventSenders))
                        {
                            eventSenders.FirstOrDefault(a => a is FriendsRemoteEventSender)?.SendEvent("onNewFriendshipRequest", new object[] { me.GetPlayerFriend(friendId) });
                        }
                    }
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = methodName.EndsWith("2") ? WrapEnumResponse((int)resultStatus) : new EnumToByteMethod(typeof(RelationshipStatus)).ToBytes(resultStatus)
                    }
                });
                return;
            }
            SendError(guid, 401);
        }
        protected async Task AcceptFriendRequest(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                string friendId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                FriendDocument friendDocument = await boltGame.GetFriendDocument(Id, friendId);

                if (friendDocument == null || friendDocument.playerInitiatorId == Id)
                {
                    SendError(guid, 500);
                    return;
                }

                // Allow accepting both pending (Request) and previously declined (Ignored)
                // requests — the "Add as friend" button in the declined-requests tab used
                // to call into this method and error out because the relation was already
                // marked Ignored. The recipient should still be able to befriend the
                // sender from that screen.
                if (friendDocument.relationshipStatus != RelationshipStatusCustom.Request &&
                    friendDocument.relationshipStatus != RelationshipStatusCustom.Ignored)
                {
                    SendError(guid, 500);
                    return;
                }

                friendDocument.relationshipStatus = RelationshipStatusCustom.Friend;
                boltGame.UpdatePlayerFriendDocument(Id, friendId, friendDocument);
                InvalidateFriendCaches(Id, friendId);
                
                var me = await BoltMainDatabaseProvider.Instance.GetPlayerDocumentAsync(ObjectId.Parse(Id));
                if (StaticClasses.EventSenders.TryGetValue(friendId, out var eventSenders))
                {
                    eventSenders.FirstOrDefault(a => a is FriendsRemoteEventSender)?.SendEvent("onFriendAdded", new object[] { me.GetPlayerFriend(friendId) });
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = methodName.EndsWith("2") ? WrapEnumResponse((int)RelationshipStatus.Friend) : new EnumToByteMethod(typeof(RelationshipStatus)).ToBytes(RelationshipStatus.Friend)
                    }
                });
                return;
            }
            SendError(guid, 401);
        }
        protected async Task IgnoreFriendRequest(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                string friendId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                FriendDocument friendDocument = await boltGame.GetFriendDocument(Id, friendId);
                
                if (friendDocument == null || friendDocument.playerInitiatorId == Id)
                {
                    SendError(guid, 500);
                    return;
                }

                friendDocument.relationshipStatus = RelationshipStatusCustom.Ignored;
                boltGame.UpdatePlayerFriendDocument(Id, friendId, friendDocument);
                InvalidateFriendCaches(Id, friendId);
                
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = methodName.EndsWith("2") ? WrapEnumResponse((int)RelationshipStatus.Ignored) : new EnumToByteMethod(typeof(RelationshipStatus)).ToBytes(RelationshipStatus.Ignored)
                    }
                });
                return;
            }
            SendError(guid, 401);
        }
        protected async Task RevokeFriendRequest(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                string friendId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                FriendDocument friendDocument = await boltGame.GetFriendDocument(Id, friendId);
                
                if (friendDocument == null || friendDocument.playerId == Id)
                {
                    SendError(guid, 500);
                    return;
                }

                friendDocument.relationshipStatus = RelationshipStatusCustom.None;
                boltGame.UpdatePlayerFriendDocument(Id, friendId, friendDocument);
                InvalidateFriendCaches(Id, friendId);
                
                if (StaticClasses.EventSenders.TryGetValue(friendId, out var eventSenders))
                {
                    var me = await BoltMainDatabaseProvider.Instance.GetPlayerDocumentAsync(ObjectId.Parse(Id));
                    eventSenders.FirstOrDefault(a => a is FriendsRemoteEventSender)?.SendEvent("onRevokeFriendshipRequest", new object[] { me.GetPlayerFriend(friendId) });
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = methodName.EndsWith("2") ? WrapEnumResponse((int)RelationshipStatus.None) : new EnumToByteMethod(typeof(RelationshipStatus)).ToBytes(RelationshipStatus.None)
                    }
                });
                return;
            }
            SendError(guid, 401);
        }
        protected async Task UnblockFriend(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                string friendId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                FriendDocument friendDocument = await boltGame.GetFriendDocument(Id, friendId);
                
                if (friendDocument == null || friendDocument.playerId == Id)
                {
                    SendError(guid, 500);
                    return;
                }

                friendDocument.relationshipStatus = RelationshipStatusCustom.None;
                boltGame.UpdatePlayerFriendDocument(Id, friendId, friendDocument);
                InvalidateFriendCaches(Id, friendId);
                
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = methodName.EndsWith("2") ? WrapEnumResponse((int)RelationshipStatus.None) : new EnumToByteMethod(typeof(RelationshipStatus)).ToBytes(RelationshipStatus.None)
                    }
                });
                return;
            }
            SendError(guid, 401);
        }
        protected async Task RemoveFriend(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                string friendId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                FriendDocument friendDocument = await boltGame.GetFriendDocument(Id, friendId);
                
                if (friendDocument == null)
                {
                    SendError(guid, 404);
                    return;
                }

                friendDocument.relationshipStatus = RelationshipStatusCustom.None;
                boltGame.UpdatePlayerFriendDocument(Id, friendId, friendDocument);
                InvalidateFriendCaches(Id, friendId);
                
                if (StaticClasses.EventSenders.TryGetValue(friendId, out var eventSenders))
                {
                    eventSenders.FirstOrDefault(a => a is FriendsRemoteEventSender)?.SendEvent("onFriendRemoved", new object[] { Id });
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = methodName.EndsWith("2") ? WrapEnumResponse((int)RelationshipStatus.None) : new EnumToByteMethod(typeof(RelationshipStatus)).ToBytes(RelationshipStatus.None)
                    }
                });
                return;
            }
            SendError(guid, 401);
        }
        protected async Task BlockFriend(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                string friendId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                FriendDocument friendDocument = await boltGame.GetFriendDocument(Id, friendId);
                
                if (friendDocument == null)
                {
                    friendDocument = boltGame.CreatePlayerFriendDocument(new FriendDocument { playerId = friendId, playerInitiatorId = Id, relationshipStatus = RelationshipStatusCustom.Blocked });
                    InvalidateFriendCaches(Id, friendId);
                }
                else
                {
                    if (friendDocument.playerInitiatorId == friendId)
                    {
                        string play = friendDocument.playerId;
                        string initiatior = friendDocument.playerInitiatorId;
                        friendDocument.playerId = initiatior;
                        friendDocument.playerInitiatorId = play;
                    }
                    friendDocument.relationshipStatus = RelationshipStatusCustom.Blocked;
                    boltGame.UpdatePlayerFriendDocument(Id, friendId, friendDocument);
                    InvalidateFriendCaches(Id, friendId);
                    
                    if (StaticClasses.EventSenders.TryGetValue(friendId, out var eventSenders))
                    {
                        eventSenders.FirstOrDefault(a => a is FriendsRemoteEventSender)?.SendEvent("onFriendRemoved", new object[] { Id });
                    }
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = methodName.EndsWith("2") ? WrapEnumResponse((int)RelationshipStatus.Blocked) : new EnumToByteMethod(typeof(RelationshipStatus)).ToBytes(RelationshipStatus.Blocked)
                    }
                });
                return;
            }
            SendError(guid, 401);
        }
        protected async Task SearchPlayers(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                
                string value = string.Empty;
                int page = 0;
                int size = 20;
                
                if (methodName.Equals("searchplayers2", StringComparison.OrdinalIgnoreCase) && values != null && values.Length > 0 && values[0] != null && !values[0].IsNull)
                {
                    var requestMsg = SearchPlayersRequest.Parser.ParseFrom(values[0].One);
                    value = requestMsg.Value;
                    page = requestMsg.Page;
                    size = requestMsg.Size;
                }
                else
                {
                    value = values != null && values.Length > 0 && values[0] != null && !values[0].IsNull ? (string)new FromByteMethod(typeof(string)).FromBytes(values[0]) : string.Empty;
                    page = values != null && values.Length > 1 && values[1] != null && !values[1].IsNull ? (int)new FromByteMethod(typeof(int)).FromBytes(values[1]) : 0;
                    size = values != null && values.Length > 2 && values[2] != null && !values[2].IsNull ? (int)new FromByteMethod(typeof(int)).FromBytes(values[2]) : 20;
                }

                List<FriendDocument> friendDocuments = await boltGame.GetPlayerFriends(Id);
                int requested = (page + 1) * Math.Max(size, 1);
                var players = boltMain.FindPlayersByUidOrName(value, Math.Max(requested, 64));

                List<PlayerFriend> playerFriends = new List<PlayerFriend>();
                foreach (var a in players)
                {
                    FriendDocument friend = friendDocuments.FirstOrDefault(b => b.playerId == a._id.ToString() || b.playerInitiatorId == a._id.ToString());
                    if (friend != null)
                        playerFriends.Add(friend.GetPlayerFriend(Id));
                    else
                        playerFriends.Add(a.GetPlayerFriend());
                }

                var result = playerFriends.Skip(page * size).Take(size);
                
                if (methodName.Equals("searchPlayers2", StringComparison.OrdinalIgnoreCase))
                {
                    var response = new SearchPlayersResponse();
                    response.Players.Add(result);
                    
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Return = new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(Google.Protobuf.MessageExtensions.ToByteArray(response)) }
                        }
                    });
                }
                else
                {
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Return = new ToByteMethod(typeof(PlayerFriend[])).ToBytes(result.ToArray())
                        }
                    });
                }
                return;
            }
            SendError(guid, 401);
        }
        protected async Task GetOnlineStatus(BinaryValue[] values, string guid, string methodName)
        {
            string playerId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
            Axlebolt.Bolt.Protobuf.OnlineStatus result = Axlebolt.Bolt.Protobuf.OnlineStatus.StateOffline;
            
            if (StaticClasses.PlayersStatus.TryGetValue(playerId, out var status))
            {
                result = (Axlebolt.Bolt.Protobuf.OnlineStatus)status.onlineStatus;
            }
            
            _user.SendResponce(new ResponseMessage 
            { 
                RpcResponse = new RpcResponse 
                { 
                    Id = guid, 
                    Return = new EnumToByteMethod(typeof(Axlebolt.Bolt.Protobuf.OnlineStatus)).ToBytes(result) 
                } 
            });
        }

        protected async Task GetPlayerFriendsCount(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                EnumFromByteMethod from = new EnumFromByteMethod(typeof(RelationshipStatus[]));
                RelationshipStatus[] statuses = ParseRelationshipStatuses(values, from, methodName);
                List<FriendDocument> friendDocuments = await boltGame.GetPlayerFriends(Id);
                
                long count = friendDocuments.Count(a => statuses.Contains(a.relationshipStatus.GetRelationshipStatus(a.playerInitiatorId == Id)));
                _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = methodName.EndsWith("2") ? WrapLongResponse(count) : new ToByteMethod(typeof(long)).ToBytes(count) } });
                return;
            }
            SendError(guid, 401);
        }

        protected async Task GetPlayersCount(BinaryValue[] values, string guid, string methodName)
        {
            string value = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
            long count = await BoltMainDatabaseProvider.Instance.GetPlayersCountAsync(value);
            _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = methodName.EndsWith("2") ? WrapLongResponse(count) : new ToByteMethod(typeof(long)).ToBytes(count) } });
        }

        protected Task GetPlayer(BinaryValue[] values, string guid)
        {
            string playerId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
            Logger.Log($"[GetPlayer RPC] Requested player: {playerId}");
            
            if (FriendHelper.TryGetPlayer(playerId, out Player player))
            {
                Logger.Log($"[GetPlayer RPC] Player found: {player.Name} (ID: {player.Id})");
                Logger.Log($"[GetPlayer RPC] Player.Attributes is null: {player.Attributes == null}");
                if (player.Attributes != null)
                {
                    Logger.Log($"[GetPlayer RPC] Player.Attributes.Map is null: {player.Attributes.Map == null}");
                    if (player.Attributes.Map != null)
                    {
                        Logger.Log($"[GetPlayer RPC] Attributes count: {player.Attributes.Map.Count}");
                        foreach (var kvp in player.Attributes.Map)
                        {
                            string value = kvp.Value.String != null ? $"String='{kvp.Value.String}'" : $"Int={kvp.Value.Int}";
                            Logger.Log($"[GetPlayer RPC]   Sending attribute: {kvp.Key} = {value}");
                        }
                    }
                }
                SendResponse(guid, player);
            }
            else
            {
                Logger.Log($"[GetPlayer RPC] Player not found: {playerId}");
                SendError(guid, 404);
            }

            return Task.CompletedTask;
        }

        public override async Task InvokeAsync(RpcRequest request)
        {
            switch (request.MethodName)
            {
                case "getAvatars":
                    await getAvatars(request.Params.ToArray(), request.Id);
                    break;
                case "getPlayerFriendsIds":
                case "getPlayerFriendsIds2":
                    await GetPlayerFriendsIds(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "getPlayerFriends":
                case "getPlayerFriends2":
                    await GetPlayerFriends(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "getPlayerFriendById":
                case "getPlayerFriend":
                    if (request.Params == null || request.Params.Count == 0 || request.Params[0] == null || request.Params[0].IsNull)
                    {
                        SendError(request.Id, 400);
                        break;
                    }
                    await GetPlayerFriendById((string)new FromByteMethod(typeof(string)).FromBytes(request.Params[0]), request.Id);
                    break;
                case "getPlayerFriendById2":
                case "getPlayerById2":
                    await GetPlayerFriendById2(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "searchPlayers":
                case "searchPlayers2":
                    await SearchPlayers(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "blockFriend":
                case "blockFriend2":
                    await BlockFriend(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "removeFriend":
                case "removeFriend2":
                    await RemoveFriend(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "unblockFriend":
                case "unblockFriend2":
                    await UnblockFriend(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "revokeFriendRequest":
                case "revokeFriendRequest2":
                    await RevokeFriendRequest(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "ignoreFriendRequest":
                case "ignoreFriendRequest2":
                    await IgnoreFriendRequest(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "acceptFriendRequest":
                case "acceptFriendRequest2":
                    await AcceptFriendRequest(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "sendFriendRequest":
                case "sendFriendRequest2":
                    await SendFriendRequest(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "getOnlineStatus":
                case "getOnlineStatus2":
                    await GetOnlineStatus(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "getPlayerFriendsCount":
                case "getPlayerFriendsCount2":
                    await GetPlayerFriendsCount(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "getPlayersCount":
                case "getPlayersCount2":
                    await GetPlayersCount(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "getPlayer":
                    await GetPlayer(request.Params.ToArray(), request.Id);
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
    }
    public class FriendsRemoteEventSender : IEventSender
    {
        private UserService User { get; set; }
        public string eventListenerName { get; set; } = "FriendsRemoteEventListener";

        public FriendsRemoteEventSender(UserService user) { User = user; }
        protected void OnFriendRemoved(object[] param)
        {
            string playerId = (string)param[0];
            ResponseMessage responseMessage = new ResponseMessage { EventResponse = new EventResponse { EventName = "onFriendRemoved", ListenerName = eventListenerName } };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(string)).ToBytes(playerId));
            User.SendResponce(responseMessage);
        }
        protected void OnFriendAdded(object[] param)
        {
            PlayerFriend player = (PlayerFriend)param[0];
            ResponseMessage responseMessage = new ResponseMessage { EventResponse = new EventResponse { EventName = "onFriendAdded", ListenerName = eventListenerName } };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(PlayerFriend)).ToBytes(player));
            User.SendResponce(responseMessage);
        }
        protected void OnFriendAvatarChanged(object[] param)
        {
            string playerId = (string)param[0];
            string avatarId = (string)param[1];
            ResponseMessage responseMessage = new ResponseMessage { EventResponse = new EventResponse { EventName = "onFriendAvatarChanged", ListenerName = eventListenerName } };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(string)).ToBytes(playerId));
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(string)).ToBytes(avatarId));
            User.SendResponce(responseMessage);
        }

        protected void OnPlayerStatusChanged(object[] param)
        {
            string playerId = (string)param[0];
            Axlebolt.Bolt.Protobuf.PlayerStatus playerStatus = (Axlebolt.Bolt.Protobuf.PlayerStatus)param[1];
            ResponseMessage responseMessage = new ResponseMessage { EventResponse = new EventResponse { EventName = "onPlayerStatusChanged", ListenerName = eventListenerName } };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(string)).ToBytes(playerId));
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(Axlebolt.Bolt.Protobuf.PlayerStatus)).ToBytes(playerStatus));
            User.SendResponce(responseMessage);
        }
        protected void OnNewFriendshipRequest(object[] param)
        {
            PlayerFriend player = (PlayerFriend)param[0];
            ResponseMessage responseMessage = new ResponseMessage { EventResponse = new EventResponse { EventName = "onNewFriendshipRequest", ListenerName = eventListenerName } };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(PlayerFriend)).ToBytes(player));
            User.SendResponce(responseMessage);
        }
        protected void OnRevokeFriendshipRequest(object[] param)
        {
            PlayerFriend player = (PlayerFriend)param[0];
            ResponseMessage responseMessage = new ResponseMessage { EventResponse = new EventResponse { EventName = "onRevokeFriendshipRequest", ListenerName = eventListenerName } };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(PlayerFriend)).ToBytes(player));
            User.SendResponce(responseMessage);
        }
        protected void OnFriendNameChanged(object[] param)
        {
            string playerId = (string)param[0];
            string newName = (string)param[1];
            ResponseMessage responseMessage = new ResponseMessage { EventResponse = new EventResponse { EventName = "onFriendNameChanged", ListenerName = eventListenerName } };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(string)).ToBytes(playerId));
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(string)).ToBytes(newName));
            User.SendResponce(responseMessage);
        }
        protected void OnFriendMsg(object[] param)
        {
            string senderId = (string)param[0];
            string message  = (string)param[1];
            long timestamp  = (long)param[2];
            ResponseMessage responseMessage = new ResponseMessage { EventResponse = new EventResponse { EventName = "onFriendMsg", ListenerName = eventListenerName } };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(string)).ToBytes(senderId));
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(string)).ToBytes(message));
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(long)).ToBytes(timestamp));
            User.SendResponce(responseMessage);
        }
        public void SendEvent(string eventName, object[] param)
        {
            if (eventName == "onFriendNameChanged") OnFriendNameChanged(param);
            else if (eventName == "onRevokeFriendshipRequest") OnRevokeFriendshipRequest(param);
            else if (eventName == "onNewFriendshipRequest") OnNewFriendshipRequest(param);
            else if (eventName == "onPlayerStatusChanged") OnPlayerStatusChanged(param);
            else if (eventName == "onFriendAvatarChanged") OnFriendAvatarChanged(param);
            else if (eventName == "onFriendAdded") OnFriendAdded(param);
            else if (eventName == "onFriendRemoved") OnFriendRemoved(param);
            else if (eventName == "onFriendMsg") OnFriendMsg(param);
        }
    }

    public sealed class FriendActionRequest : IMessage<FriendActionRequest>
    {
        private static readonly MessageParser<FriendActionRequest> _parser = 
            new MessageParser<FriendActionRequest>(() => new FriendActionRequest());
        public static MessageParser<FriendActionRequest> Parser => _parser;

        private string value_ = "";
        public string Value
        {
            get { return value_; }
            set { value_ = value ?? ""; }
        }

        public FriendActionRequest() { }
        public FriendActionRequest(FriendActionRequest other)
        {
            value_ = other.value_;
        }

        public FriendActionRequest Clone() => new FriendActionRequest(this);

        public override bool Equals(object other) => Equals(other as FriendActionRequest);
        public bool Equals(FriendActionRequest other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (Value != other.Value) return false;
            return true;
        }

        public override int GetHashCode()
        {
            int hash = 1;
            if (Value.Length != 0) hash ^= Value.GetHashCode();
            return hash;
        }

        public override string ToString() => JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(CodedOutputStream output)
        {
            if (Value.Length != 0)
            {
                output.WriteRawTag(10);
                output.WriteString(Value);
            }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (Value.Length != 0)
            {
                size += 1 + CodedOutputStream.ComputeStringSize(Value);
            }
            return size;
        }

        public void MergeFrom(FriendActionRequest other)
        {
            if (other == null) return;
            if (other.Value.Length != 0) Value = other.Value;
        }

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    default:
                        input.SkipLastField();
                        break;
                    case 10u:
                        Value = input.ReadString();
                        break;
                }
            }
        }

        public MessageDescriptor Descriptor => null;
    }

    public sealed class GetPlayerFriendResponse : IMessage<GetPlayerFriendResponse>
    {
        private static readonly MessageParser<GetPlayerFriendResponse> _parser = 
            new MessageParser<GetPlayerFriendResponse>(() => new GetPlayerFriendResponse());
        public static MessageParser<GetPlayerFriendResponse> Parser => _parser;

        private PlayerFriend playerFriend_;
        public PlayerFriend PlayerFriend
        {
            get { return playerFriend_; }
            set { playerFriend_ = value; }
        }

        public GetPlayerFriendResponse() { }
        public GetPlayerFriendResponse(GetPlayerFriendResponse other)
        {
            playerFriend_ = other.playerFriend_ != null ? other.playerFriend_.Clone() : null;
        }

        public GetPlayerFriendResponse Clone() => new GetPlayerFriendResponse(this);

        public override bool Equals(object other) => Equals(other as GetPlayerFriendResponse);
        public bool Equals(GetPlayerFriendResponse other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (!object.Equals(PlayerFriend, other.PlayerFriend)) return false;
            return true;
        }

        public override int GetHashCode()
        {
            int hash = 1;
            if (playerFriend_ != null) hash ^= playerFriend_.GetHashCode();
            return hash;
        }

        public override string ToString() => JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(CodedOutputStream output)
        {
            if (playerFriend_ != null)
            {
                output.WriteRawTag(10);
                output.WriteMessage(PlayerFriend);
            }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (playerFriend_ != null)
            {
                size += 1 + CodedOutputStream.ComputeMessageSize(PlayerFriend);
            }
            return size;
        }

        public void MergeFrom(GetPlayerFriendResponse other)
        {
            if (other == null) return;
            if (other.playerFriend_ != null)
            {
                if (playerFriend_ == null)
                {
                    playerFriend_ = new PlayerFriend();
                }
                playerFriend_.MergeFrom(other.PlayerFriend);
            }
        }

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    default:
                        input.SkipLastField();
                        break;
                    case 10u:
                        if (playerFriend_ == null)
                        {
                            playerFriend_ = new PlayerFriend();
                        }
                        input.ReadMessage(playerFriend_);
                        break;
                }
            }
        }

        public MessageDescriptor Descriptor => null;
    }
}
