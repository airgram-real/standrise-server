using System;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.Bolt;

namespace StandRiseServer.RpcServer.Clans.Mappers
{
    public class ClanChatMessagesMapper : MessageMapper<ClanUserMessage, ClanDocument.ClanMessage>
    {
        public static readonly ClanChatMessagesMapper Instance = new ClanChatMessagesMapper();

        public override ClanDocument.ClanMessage ToOriginal(ClanUserMessage proto)
        {
            if (proto == null) throw new ArgumentNullException(nameof(proto));
            if (proto.MessageType != MessageType.ChatMessage) return null;
            
            return new ClanDocument.ClanMessage
            {
                senderId = proto.ChatMessage.SenderId,
                // message content and timestamp are handled via BsonDocument usually
            };
        }

        public override ClanUserMessage ToProto(ClanDocument.ClanMessage original)
        {
            if (original == null) return null;
            return new ClanUserMessage
            {
                MessageType = MessageType.ChatMessage,
                ChatMessage = new ClanChatMessage
                {
                    SenderId = original.senderId,
                    Message = "" // We need to pass the message here if possible, but original only has senderId and playersRead. 
                    // Actually ClanDocument.GetMessages() returns ClanUserMessage[] directly. 
                    // This mapper might be redundant or needs more fields in ClanMessage class.
                }
            };
        }
    }
}
