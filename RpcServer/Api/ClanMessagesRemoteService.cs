using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer.Core;
using StandRiseServer.RpcServer.Model;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("ClanMessagesRemoteService")]
    public class ClanMessagesRemoteService : RpcClass
    {
        public ClanMessagesRemoteService(UserService user) : base(user) { }

        protected async void GetUnreadChatMessagesCount(string guid)
        {
            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);
            if (clan != null)
            {
                var messages = clan.GetMessages2();
                int num = messages.Count(m => !m.playersRead.Contains(playerDocument._id.ToString()));
                SendResponse(guid, num);
                return;
            }
            SendResponse(guid, 0);
        }

        protected void GetUnreadLogMessagesCount(string guid)
        {
            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);
            if (clan != null)
            {
                SendResponse(guid, 0); 
                return;
            }
            SendResponse(guid, 0);
        }

        private void SendClanChatMessage(BinaryValue[] values, string guid)
        {
            string messages = values.GetValue<string>(0);
            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            BoltMainDatabaseProvider.Instance.SendMessageToClan(messages, playerDocument);
            
            Axlebolt.Bolt.Protobuf.ClanMember[] members = BoltMainDatabaseProvider.Instance.GetAllClanMembers(playerDocument);
            
            ClanUserMessage clanUserMessage = new ClanUserMessage
            {
                Id = "",
                MessageType = MessageType.ChatMessage,
                Timestamp = Utils.ToUnixTime(DateTime.UtcNow),
                ChatMessage = new ClanChatMessage
                {
                    SenderId = playerDocument._id.ToString(),
                    Message = messages
                }
            };
            
            foreach (var member in members)
            {
                SendEventToSession<ClanMessagesRemoteEventListener>(member.PlayerFriend.Player.Id, "onIncomingClanChatMessage", clanUserMessage);
            }
            SendResponse(guid);
        }
        
        private void SendClanChatMessage2(BinaryValue[] values, string guid)
        {
            if (values == null || values.Length == 0 || values[0].IsNull)
            {
                SendResponse(guid);
                return;
            }
            var req = SendClanChatMessage2Request.Parser.ParseFrom(values[0].One.ToByteArray());
            string messages = req.Message;
            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            BoltMainDatabaseProvider.Instance.SendMessageToClan(messages, playerDocument);
            
            Axlebolt.Bolt.Protobuf.ClanMember[] members = BoltMainDatabaseProvider.Instance.GetAllClanMembers(playerDocument);
            
            ClanUserMessage clanUserMessage = new ClanUserMessage
            {
                Id = "",
                MessageType = MessageType.ChatMessage,
                Timestamp = Utils.ToUnixTime(DateTime.UtcNow),
                ChatMessage = new ClanChatMessage
                {
                    SenderId = playerDocument._id.ToString(),
                    Message = messages
                }
            };
            
            foreach (var member in members)
            {
                SendEventToSession<ClanMessagesRemoteEventListener>(member.PlayerFriend.Player.Id, "onIncomingClanChatMessage", clanUserMessage);
            }
            SendResponse(guid, new SendClanChatMessage2Response().ToByteArray());
        }

        protected void GetClanLogMessages(BinaryValue[] values, string guid)
        {
            int from = values.Length > 0 ? values.GetValue<int>(0) : 0;
            int count = values.Length > 1 ? values.GetValue<int>(1) : 20;
            
            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);
            if (clan != null)
            {
                ClanUserMessage[] logs = BoltMainDatabaseProvider.Instance.GetClanLogMessages(clan.tag);
                SendResponse(guid, logs.Skip(from).Take(count).ToArray());
                return;
            }
            SendResponse(guid, new ClanUserMessage[0]);
        }
        
        protected void GetClanLogMessages2(BinaryValue[] values, string guid)
        {
            if (values == null || values.Length == 0 || values[0].IsNull)
            {
                SendResponse(guid);
                return;
            }
            var req = GetClanLogMessages2Request.Parser.ParseFrom(values[0].One.ToByteArray());
            int from = req.SkipCount;
            int count = req.Limit;
            
            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);
            
            var res = new GetClanLogMessages2Response();
            if (clan != null)
            {
                ClanUserMessage[] logs = BoltMainDatabaseProvider.Instance.GetClanLogMessages(clan.tag);
                res.Items.Add(logs.Skip(from).Take(count));
            }
            SendResponse(guid, res.ToByteArray());
        }

        private void GetClanMessages(BinaryValue[] values, string guid)
        {
            int from = values.Length > 0 ? values.GetValue<int>(0) : 0;
            int count = values.Length > 1 ? values.GetValue<int>(1) : 20;

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            ClanUserMessage[] documents = BoltMainDatabaseProvider.Instance.GetClanMsgs(playerDocument);
            List<ClanUserMessage> ponos = documents.ToList();
            ponos.Reverse();
            SendResponse(guid, ponos.Skip(from).Take(count).ToArray());
        }
        
        private void GetClanMessages2(BinaryValue[] values, string guid)
        {
            if (values == null || values.Length == 0 || values[0].IsNull)
            {
                SendResponse(guid);
                return;
            }
            var req = GetClanMessages2Request.Parser.ParseFrom(values[0].One.ToByteArray());
            int from = req.SkipCount;
            int count = req.Limit;

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            ClanUserMessage[] documents = BoltMainDatabaseProvider.Instance.GetClanMsgs(playerDocument);
            List<ClanUserMessage> ponos = documents.ToList();
            ponos.Reverse();
            
            var res = new GetClanMessages2Response();
            res.Items.Add(ponos.Skip(from).Take(count));
            SendResponse(guid, res.ToByteArray());
        }

        protected void GetClanChatMessages(BinaryValue[] values, string guid)
        {
            GetClanMessages(values, guid);
        }
        
        protected void GetClanChatMessages2(BinaryValue[] values, string guid)
        {
            int from = 0;
            int count = 20;
            if (values != null && values.Length > 0 && !values[0].IsNull && values[0].One != null)
            {
                var req = GetClanChatMessages2Request.Parser.ParseFrom(values[0].One.ToByteArray());
                from = req.SkipCount;
                count = req.Limit;
            }
            if (count <= 0) count = 20;

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            ClanUserMessage[] documents = BoltMainDatabaseProvider.Instance.GetClanMsgs(playerDocument);
            List<ClanUserMessage> ponos = documents.ToList();
            ponos.Reverse();
            
            var res = new GetClanChatMessages2Response();
            res.Items.Add(ponos.Skip(from).Take(count));
            SendResponse(guid, res.ToByteArray());
        }

        protected void ReadClanMessages(string guid)
        {
            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            BoltMainDatabaseProvider.Instance.ReadMessagesClan(playerDocument);
            SendResponse(guid);
        }

        protected void ReadClanLogMessages(string guid)
        {
            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            BoltMainDatabaseProvider.Instance.ReadLogMessagesClan(playerDocument);
            SendResponse(guid);
        }

        public override void Invoke(RpcRequest request)
        {
            switch (request.MethodName)
            {
                case "getClanLogMessages":
                    GetClanLogMessages(request.Params.ToArray(), request.Id);
                    break;
                case "getClanLogMessages2":
                    GetClanLogMessages2(request.Params.ToArray(), request.Id);
                    break;
                case "sendClanChatMessage":
                    SendClanChatMessage(request.Params.ToArray(), request.Id);
                    break;
                case "sendClanChatMessage2":
                    SendClanChatMessage2(request.Params.ToArray(), request.Id);
                    break;
                case "getUnreadChatMessagesCount":
                case "getUnreadChatMessagesCount2":
                    GetUnreadChatMessagesCount(request.Id);
                    break;
                case "readClanLogMessages":
                case "readClanLogMessages2":
                    ReadClanLogMessages(request.Id);
                    break;
                case "getUnreadLogMessagesCount":
                case "getUnreadLogMessagesCount2":
                    GetUnreadLogMessagesCount(request.Id);
                    break;
                case "getClanChatMessages":
                    GetClanChatMessages(request.Params.ToArray(), request.Id);
                    break;
                case "getClanChatMessages2":
                    GetClanChatMessages2(request.Params.ToArray(), request.Id);
                    break;
                case "getClanMessages":
                    GetClanMessages(request.Params.ToArray(), request.Id);
                    break;
                case "getClanMessages2":
                    GetClanMessages2(request.Params.ToArray(), request.Id);
                    break;
                case "readClanChatMessages":
                case "readClanChatMessages2":
                    ReadClanMessages(request.Id);
                    break;
                default:
                    MethodNotFound(request);
                    break;
            }
        }
    }
}
