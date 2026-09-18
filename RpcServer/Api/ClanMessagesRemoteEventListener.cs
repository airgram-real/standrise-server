using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.RpcServer.Model;
using System;

namespace StandRiseServer.RpcServer.Api
{
    public class ClanMessagesRemoteEventListener : IEventSender
    {
        private UserService User { get; set; }

        public string eventListenerName { get; set; } = "ClanMessagesRemoteEventListener";

        public ClanMessagesRemoteEventListener(UserService user)
        {
            User = user;
        }

        private void OnIncomingClanChatMessage(object[] param)
        {
            ClanUserMessage arg = (ClanUserMessage)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "onIncomingClanChatMessage",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(ClanUserMessage)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        public void SendEvent(string eventName, object[] param)
        {
            switch (eventName)
            {
                case "onIncomingClanChatMessage":
                    OnIncomingClanChatMessage(param);
                    break;
                default:
                    Console.WriteLine("Event error in " + eventListenerName + ": " + eventName);
                    break;
            }
        }
    }
}
