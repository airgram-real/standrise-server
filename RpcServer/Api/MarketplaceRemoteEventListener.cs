using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using System;

namespace StandRiseServer.RpcServer.Api
{
    public class MarketplaceRemoteEventListener : IEventSender
    {
        private UserService User { get; set; }

        public string eventListenerName { get; set; } = "MarketplaceRemoteEventListener";

        public MarketplaceRemoteEventListener(UserService user)
        {
            User = user;
        }

        public void OnTradeUpdated(object[] param)
        {
            OnTradeUpdatedEvent arg = (OnTradeUpdatedEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "onTradeUpdated",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        public void OnTradeRequestClosed(object[] param)
        {
            OnTradeRequestClosedEvent arg = (OnTradeRequestClosedEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "onTradeRequestClosed",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        public void OnTradeRequestOpened(object[] param)
        {
            OnTradeRequestOpenedEvent arg = (OnTradeRequestOpenedEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "onTradeRequestOpened",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnTradeRequestOpenedEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        public void OnPlayerRequestClosed(object[] param)
        {
            OnPlayerRequestClosedEvent arg = (OnPlayerRequestClosedEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "onPlayerRequestClosed",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnPlayerRequestClosedEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        public void OnPlayerRequestOpened(object[] param)
        {
            OnPlayerRequestOpenedEvent arg = (OnPlayerRequestOpenedEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "onPlayerRequestOpened",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnPlayerRequestOpenedEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        public void SendEvent(string eventName, object[] param)
        {
            switch (eventName)
            {
                case "onTradeUpdated":
                    OnTradeUpdated(param);
                    break;
                case "onTradeRequestClosed":
                    OnTradeRequestClosed(param);
                    break;
                case "onTradeRequestOpened":
                    OnTradeRequestOpened(param);
                    break;
                case "onPlayerRequestClosed":
                    OnPlayerRequestClosed(param);
                    break;
                case "onPlayerRequestOpened":
                    OnPlayerRequestOpened(param);
                    break;
                default:
                    Console.WriteLine("Unknown marketplace event: " + eventName);
                    break;
            }
        }
    }
}
