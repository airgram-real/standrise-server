using Google.Protobuf;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.MongoDB.Game;

namespace StandRiseServer.RpcServer.Api
{
    public class ChatRemoteService : RpcClass
    {
        public ChatRemoteService(UserService user) : base(user)
        {
        }

        private string GetPlayerId()
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                return playerId;
            return null;
        }

        public override void Invoke(RpcRequest request)
        {
            string playerId = GetPlayerId();
            if (string.IsNullOrEmpty(playerId))
            {
                Logger.Error($"[Chat] {request.MethodName} called without authenticated user");
                ReturnEmpty(request.Id);
                return;
            }

            string method = (request.MethodName ?? string.Empty).ToLowerInvariant();
            switch (method)
            {
                case "sendfriendmsg":
                  case "sendfriendmsg2":
                      SendFriendMsg(request.Params.ToArray(), request.Id, playerId, method);
                    break;
                case "getunreadchatuserscount":
                  case "getunreadchatuserscount2":
                      GetUnreadChatUsersCount(request.Params.ToArray(), request.Id, playerId, method);
                    break;
                case "getchatusers":
                case "getchatusersbyoffset":
                case "getchatusersbypage":
                  case "getchatusers2":
                  case "getchatusersbyoffset2":
                  case "getchatusersbypage2":
                      GetChatUsers(request.Params.ToArray(), request.Id, playerId, method);
                    break;
                case "getchatuserslite":
                  case "getchatuserslite2":
                      GetChatUsersLite(request.Params.ToArray(), request.Id, playerId, method);
                    break;
                case "readfriendmsgs":
                  case "readfriendmsgs2":
                      ReadFriendMsgs(request.Params.ToArray(), request.Id, playerId, method);
                    break;
                case "getfriendmsgsbyoffset":
                  case "getfriendmsgsbyoffset2":
                      GetFriendMsgsByOffset(request.Params.ToArray(), request.Id, playerId, method);
                    break;
                case "deletefriendmsgs":
                  case "deletefriendmsgs2":
                      DeleteFriendMsgs(request.Params.ToArray(), request.Id, playerId, method);
                    break;
                default:
                    Logger.Error($"[Chat] Unknown method: {request.MethodName}");
                    MethodNotFound(request.Id);
                    break;
            }
        }

        public override Task InvokeAsync(RpcRequest request)
        {
            Invoke(request);
            return Task.CompletedTask;
        }

        // sendFriendMsg(friendId: string, message: string)
        private void SendFriendMsg(BinaryValue[] binaryValues, string guid, string playerId, string methodName = "")
        {
            try
            {
                string friendId = "";
                string message = "";
                if (methodName.EndsWith("2") && binaryValues != null && binaryValues.Length > 0) {
                    var req = new JADIJMAKOAC();
                    req.MergeFrom(binaryValues[0].One.ToByteArray());
                    friendId = req.FriendId;
                    message = req.Message;
                } else {
                    friendId = (string)new FromByteMethod(typeof(string)).FromBytes(binaryValues[0]);
                    message = (string)new FromByteMethod(typeof(string)).FromBytes(binaryValues[1]);
                }

                long timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                BoltMainDatabaseProvider.Instance.SaveChatMessage(playerId, friendId, message, timestamp);

                var userMessage = new UserMessage
                {
                    SenderId  = playerId,
                    Message   = message,
                    Timestamp = timestamp,
                    IsRead    = false
                };

                // Отправляем ТОЛЬКО получателю — отправитель уже видит своё сообщение локально
                if (StaticClasses.EventSenders.TryGetValue(friendId, out var receiverSenders))
                {
                    var msgListener = receiverSenders.FirstOrDefault(s => s is ChatRemoteEventListener);
                    msgListener?.SendEvent("onMsgFromFriend", new object[] { userMessage });
                }

                if (methodName.EndsWith("2")) {
                    ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new FPDMIGJDNOK().ToByteArray()) }, guid);
                } else {
                    ReturnEmpty(guid);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Chat] SendFriendMsg error: {ex}");
                ReturnEmpty(guid);
            }
        }

        // getUnreadChatUsersCount() -> int
        private void GetUnreadChatUsersCount(BinaryValue[] binaryValues, string guid, string playerId, string methodName = "")
        {
            try
            {
                // Количество уникальных чатов с непрочитанными сообщениями
                int count = BoltMainDatabaseProvider.Instance.GetUnreadChatsCount(playerId);
                ReturnValue(new ToByteMethod(typeof(int)).ToBytes(count), guid);
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Chat] GetUnreadChatUsersCount error: {ex}");
                if (methodName.EndsWith("2")) {
                    ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new LFFHLDJOHMI().ToByteArray()) }, guid);
                } else {
                    ReturnValue(new ToByteMethod(typeof(int)).ToBytes(0), guid);
                }
            }
        }

        // getChatUsers() -> ChatUser[]
        private void GetChatUsers(BinaryValue[] binaryValues, string guid, string playerId, string methodName = "")
        {
            try
            {
                var chatUsers = BuildChatUsers(playerId, includePartnerProfile: true);
                if (methodName.EndsWith("2")) {
                    var resp = new IEINDJAEFAM();
                    resp.Users.AddRange(chatUsers);
                    ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(resp.ToByteArray()) }, guid);
                } else {
                    if (methodName.EndsWith("2")) {
                    if (methodName == "getchatusers2") {
                        var resp = new ABJAMBOFOAA(); resp.Users.AddRange(chatUsers);
                        ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(resp.ToByteArray()) }, guid);
                    } else if (methodName == "getchatusersbyoffset2") {
                        var resp = new FAKEJMLPEHN(); resp.Users.AddRange(chatUsers);
                        ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(resp.ToByteArray()) }, guid);
                    } else if (methodName == "getchatusersbypage2") {
                        var resp = new COHNBOGIGKA(); resp.Users.AddRange(chatUsers);
                        ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(resp.ToByteArray()) }, guid);
                    } else {
                        var resp = new ABJAMBOFOAA(); resp.Users.AddRange(chatUsers);
                        ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(resp.ToByteArray()) }, guid);
                    }
                } else {
                    ReturnValue(new ToByteMethod(typeof(ChatUser[])).ToBytes(chatUsers), guid);
                }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Chat] GetChatUsers error: {ex}");
                if (methodName.EndsWith("2")) {
                    ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new IEINDJAEFAM().ToByteArray()) }, guid);
                } else {
                    if (methodName.EndsWith("2")) {
                    if (methodName == "getchatusersbyoffset2") ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new FAKEJMLPEHN().ToByteArray()) }, guid);
                    else if (methodName == "getchatusersbypage2") ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new COHNBOGIGKA().ToByteArray()) }, guid);
                    else ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new ABJAMBOFOAA().ToByteArray()) }, guid);
                } else {
                    ReturnValue(new ToByteMethod(typeof(ChatUser[])).ToBytes(new ChatUser[0]), guid);
                }
                }
            }
        }

        // getChatUsersLite() -> ChatUser[] (lightweight refresh used by the messages tab)
        private void GetChatUsersLite(BinaryValue[] binaryValues, string guid, string playerId, string methodName = "")
        {
            try
            {
                // Previously this returned an empty array which made existing chats
                // visually disappear from the messages tab whenever the client used
                // the lite refresh path. Return the same list as getChatUsers so the
                // tab stays consistent across refresh modes.
                var chatUsers = BuildChatUsers(playerId, includePartnerProfile: true);
                if (methodName.EndsWith("2")) {
                    var resp = new IEINDJAEFAM();
                    resp.Users.AddRange(chatUsers);
                    ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(resp.ToByteArray()) }, guid);
                } else {
                    if (methodName.EndsWith("2")) {
                    if (methodName == "getchatusers2") {
                        var resp = new ABJAMBOFOAA(); resp.Users.AddRange(chatUsers);
                        ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(resp.ToByteArray()) }, guid);
                    } else if (methodName == "getchatusersbyoffset2") {
                        var resp = new FAKEJMLPEHN(); resp.Users.AddRange(chatUsers);
                        ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(resp.ToByteArray()) }, guid);
                    } else if (methodName == "getchatusersbypage2") {
                        var resp = new COHNBOGIGKA(); resp.Users.AddRange(chatUsers);
                        ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(resp.ToByteArray()) }, guid);
                    } else {
                        var resp = new ABJAMBOFOAA(); resp.Users.AddRange(chatUsers);
                        ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(resp.ToByteArray()) }, guid);
                    }
                } else {
                    ReturnValue(new ToByteMethod(typeof(ChatUser[])).ToBytes(chatUsers), guid);
                }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Chat] GetChatUsersLite error: {ex}");
                if (methodName.EndsWith("2")) {
                    ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new IEINDJAEFAM().ToByteArray()) }, guid);
                } else {
                    if (methodName.EndsWith("2")) {
                    if (methodName == "getchatusersbyoffset2") ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new FAKEJMLPEHN().ToByteArray()) }, guid);
                    else if (methodName == "getchatusersbypage2") ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new COHNBOGIGKA().ToByteArray()) }, guid);
                    else ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new ABJAMBOFOAA().ToByteArray()) }, guid);
                } else {
                    ReturnValue(new ToByteMethod(typeof(ChatUser[])).ToBytes(new ChatUser[0]), guid);
                }
                }
            }
        }

        private ChatUser[] BuildChatUsers(string playerId, bool includePartnerProfile)
        {
            List<string> partnerIds = BoltMainDatabaseProvider.Instance.GetChatPartners(playerId);
            if (partnerIds == null || partnerIds.Count == 0)
                return Array.Empty<ChatUser>();

            var chatUsers = new List<ChatUser>(partnerIds.Count);
            foreach (string partnerId in partnerIds)
            {
                if (string.IsNullOrWhiteSpace(partnerId)) continue;

                var msgs = BoltMainDatabaseProvider.Instance.GetChatMessages(playerId, partnerId, limit: 1);
                if (msgs.Count == 0) continue;

                var last = msgs[0];

                int unread = BoltMainDatabaseProvider.Instance.GetUnreadCountFromSender(playerId, partnerId);

                var chatUser = new ChatUser
                {
                    Timestamp       = last.timestamp,
                    Message         = last.message ?? string.Empty,
                    UnreadMsgsCount = unread
                };

                if (includePartnerProfile && ObjectId.TryParse(partnerId, out ObjectId partnerOid))
                {
                    var partnerDoc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(partnerOid);
                    if (partnerDoc != null)
                        chatUser.Player = partnerDoc.GetPlayerFriend(playerId);
                }

                // Always include a Player stub so the client renders the chat row
                // even when the partner account is missing/deleted.
                if (chatUser.Player == null)
                {
                    chatUser.Player = new PlayerFriend
                    {
                        Player = new Player
                        {
                            Id   = partnerId,
                            Name = string.Empty
                        }
                    };
                }

                chatUsers.Add(chatUser);
            }

            chatUsers.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            return chatUsers.ToArray();
        }

        
        // getFriendMsgsByOffset(friendId: string, offset: int) -> UserMessage[]
        private void GetFriendMsgsByOffset(BinaryValue[] binaryValues, string guid, string playerId, string methodName = "")
        {
            try
            {
                string friendId = "";
                int offset = 0;
                bool isV2 = false;

                isV2 = methodName.EndsWith("2");
                if (isV2 && binaryValues != null && binaryValues.Length > 0 && binaryValues[0].One != null)
                {
                    // Likely V2 Protobuf request
                    var stream = new Google.Protobuf.CodedInputStream(binaryValues[0].One.ToByteArray());
                    uint tag;
                    while ((tag = stream.ReadTag()) != 0)
                    {
                        if (tag == 10) friendId = stream.ReadString();
                        else if (tag == 16) offset = stream.ReadInt32();
                        else stream.SkipLastField();
                    }
                }
                else if (binaryValues != null && binaryValues.Length > 0)
                {
                    friendId = (string)new FromByteMethod(typeof(string)).FromBytes(binaryValues[0]);
                    offset = binaryValues.Length > 1
                        ? (int)new FromByteMethod(typeof(int)).FromBytes(binaryValues[1])
                        : 0;
                }

                var docs = BoltMainDatabaseProvider.Instance.GetChatMessages(playerId, friendId, limit: 50, offset: offset);

                var messages = docs.Select(d => new UserMessage
                {
                    SenderId  = d.senderId,
                    Message   = d.message,
                    Timestamp = d.timestamp,
                    IsRead    = d.isRead
                }).ToArray();

                if (isV2)
                {
                    var resp = new OLEEDCHIKAA();
                    resp.Messages.AddRange(messages);
                    ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(resp.ToByteArray()) }, guid);
                }
                else
                {
                    ReturnValue(new ToByteMethod(typeof(UserMessage[])).ToBytes(messages), guid);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Chat] GetFriendMsgsByOffset error: {ex}");
                if (methodName.EndsWith("2")) {
                    ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new OLEEDCHIKAA().ToByteArray()) }, guid);
                } else {
                    ReturnValue(new ToByteMethod(typeof(UserMessage[])).ToBytes(new UserMessage[0]), guid);
                }
            }
        }

        // readFriendMsgs(friendId: string)
        private void ReadFriendMsgs(BinaryValue[] binaryValues, string guid, string playerId, string methodName = "")
        {
            try
            {
                string friendId = "";
                if (methodName.EndsWith("2") && binaryValues != null && binaryValues.Length > 0) {
                    var req = new FDAJKEOABND();
                    req.MergeFrom(binaryValues[0].One.ToByteArray());
                    friendId = req.FriendId;
                } else {
                    friendId = (string)new FromByteMethod(typeof(string)).FromBytes(binaryValues[0]);
                }
                BoltMainDatabaseProvider.Instance.MarkMessagesAsRead(playerId, friendId);
                if (methodName.EndsWith("2")) {
                    ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new NGIACCDBMJC().ToByteArray()) }, guid);
                } else {
                    ReturnEmpty(guid);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Chat] ReadFriendMsgs error: {ex}");
                ReturnEmpty(guid);
            }
        }

        // deleteFriendMsgs(friendId: string)
        private void DeleteFriendMsgs(BinaryValue[] binaryValues, string guid, string playerId, string methodName = "")
        {
            try
            {
                string friendId = "";
                if (methodName.EndsWith("2") && binaryValues != null && binaryValues.Length > 0) {
                    var req = new ILEHBMLCGNB();
                    req.MergeFrom(binaryValues[0].One.ToByteArray());
                    friendId = req.FriendId;
                } else {
                    friendId = (string)new FromByteMethod(typeof(string)).FromBytes(binaryValues[0]);
                }
                BoltMainDatabaseProvider.Instance.DeleteChatMessages(playerId, friendId);
                if (methodName.EndsWith("2")) {
                    ReturnValue(new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(new BJHICOKNOBI().ToByteArray()) }, guid);
                } else {
                    ReturnEmpty(guid);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Chat] DeleteFriendMsgs error: {ex}");
                ReturnEmpty(guid);
            }
        }

        private void ReturnEmpty(string guid)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id     = guid,
                    Return = new BinaryValue { IsNull = true }
                }
            });
        }

        private void ReturnValue(BinaryValue value, string guid)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id     = guid,
                    Return = value
                }
            });
        }

        private void MethodNotFound(string guid)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id        = guid,
                    Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                    {
                        Id   = System.BitConverter.ToInt64(System.Guid.NewGuid().ToByteArray(), 8),
                        Code = 404
                    }
                }
            });
        }
    }
}
