using System;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.RpcServer;

namespace StandRiseServer.RpcServer.Api
{
    public class GlobalChatEventListener : IEventSender
    {
        private UserService User { get; set; }
        public string eventListenerName { get; set; } = "GlobalChatEventListener";

        public GlobalChatEventListener(UserService user)
        {
            User = user;
        }

        public void SendEvent(string eventName, object[] param)
        {
            ResponseMessage responseMessage = new ResponseMessage 
            { 
                EventResponse = new EventResponse 
                { 
                    EventName = eventName, 
                    ListenerName = eventListenerName 
                } 
            };

            if (eventName == "onIncomingGlobalChatMessage")
            {
                if (param != null && param.Length > 0)
                {
                    foreach (var p in param)
                    {
                        if (p is BinaryValue bv)
                            responseMessage.EventResponse.Params.Add(bv);
                    }
                }
                User.SendResponce(responseMessage);
            }
        }
    }
}
