using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.RpcServer.Model;
using System;

namespace StandRiseServer.RpcServer.Api
{
    // Bug 9: client-side IMatchesRemoteEventListener subscribes to `onMatchFinished`.
    // Server needs to broadcast that event to every player in a match when the
    // game server reports FinishMatch, so the client navigates to the post-match
    // results screen (with chat, drops, and next-map voting).
    public class MatchesRemoteEventListener : IEventSender
    {
        private UserService User { get; set; }

        // The protobuf framework derives the listener name from the C# interface
        // name minus the leading "I", matching the dump's [EventListener] interface.
        public string eventListenerName { get; set; } = "MatchesRemoteEventListener";

        public MatchesRemoteEventListener(UserService user)
        {
            User = user;
        }

        private void OnMatchFinished(object[] param)
        {
            OnMatchFinishedEvent arg = (OnMatchFinishedEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "onMatchFinished",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnMatchFinishedEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        public void SendEvent(string eventName, object[] param)
        {
            switch (eventName)
            {
                case "onMatchFinished":
                    OnMatchFinished(param);
                    break;
                default:
                    Console.WriteLine("Event error in " + eventListenerName + ": " + eventName);
                    break;
            }
        }
    }
}
