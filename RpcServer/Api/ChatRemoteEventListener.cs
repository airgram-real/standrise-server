using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.RpcServer.Model;
using System;

namespace StandRiseServer.RpcServer.Api
{
    public class ChatRemoteEventListener : IEventSender
    {
        private UserService User { get; set; }

        // Клиент слушает через IMessagesRemoteEventListener с именем "MessagesRemoteEventListener"
        public string eventListenerName { get; set; } = "MessagesRemoteEventListener";

        public ChatRemoteEventListener(UserService user)
        {
            User = user;
        }

        // Входящее сообщение от друга
        private void OnMsgFromFriend(object[] param)
        {
            UserMessage arg = (UserMessage)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "onMsgFromFriend",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(UserMessage)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        // Обновление счётчика непрочитанных чатов
        private void OnUnreadChatsCountChanged(object[] param)
        {
            int count = (int)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "onUnreadChatsCountChanged",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(int)).ToBytes(count));
            User.SendResponce(responseMessage);
        }

        public void SendEvent(string eventName, object[] param)
        {
            switch (eventName)
            {
                case "onMsgFromFriend":
                    OnMsgFromFriend(param);
                    break;
                case "onUnreadChatsCountChanged":
                    OnUnreadChatsCountChanged(param);
                    break;
                default:
                    Console.WriteLine("Event error in " + eventListenerName + ": " + eventName);
                    break;
            }
        }
    }
}
