using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.RpcServer.Model;
using System.Collections.Generic;
using System.Linq;
using System;

namespace StandRiseServer.RpcServer.Api
{
    public class ClansRemoteEventListener : IEventSender
    {
        private UserService User { get; set; }

        public string eventListenerName { get; set; } = "ClansRemoteEventListener";

        public ClansRemoteEventListener(UserService user)
        {
            User = user;
        }

        private void OnLeftFromClan(object[] param)
        {
            OnLeftFromClan arg = (OnLeftFromClan)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnLeftFromClan",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnLeftFromClan)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnPlayerAttributes(object[] param)
        {
            OnPlayerAttributesChanged arg = (OnPlayerAttributesChanged)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnPlayerAttributesChanged",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnPlayerAttributesChanged)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnJoinRequestTaken(object[] param)
        {
            OnJoinRequestTakenEvent arg = (OnJoinRequestTakenEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnJoinRequestTaken",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnJoinRequestTakenEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnInvitedToClanEvent(object[] param)
        {
            OnInvitedToClanEvent arg = (OnInvitedToClanEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnInvitedToClanEvent",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnInvitedToClanEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnClanNameChanged(object[] param)
        {
            OnClanNameChanged arg = (OnClanNameChanged)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnClanNameChanged",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnClanNameChanged)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnClanDescriptionChanged(object[] param)
        {
            string description = (string)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnClanDescriptionChanged",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(string)).ToBytes(description));
            User.SendResponce(responseMessage);
        }

        private void OnClanAvatarChanged(object[] param)
        {
            string avatarId = (string)param[0];
            byte[] avatarData = (byte[])param[1];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnClanAvatarChanged",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(string)).ToBytes(avatarId));
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(byte[])).ToBytes(avatarData));
            User.SendResponce(responseMessage);
        }

        private void OnKickedEvent(object[] param)
        {
            OnKickedEvent arg = (OnKickedEvent)param[0];
            Logger.Log($"[ClansRemoteEventListener] Sending OnKickedEvent to user");
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnKickedEvent",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnKickedEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
            Logger.Log($"[ClansRemoteEventListener] OnKickedEvent sent successfully");
        }

        private void OnKickedMember(object[] param)
        {
            OnKickedMemberEvent arg = (OnKickedMemberEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnKickedMember",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnKickedMemberEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnIncomingClanMessage(object[] param)
        {
            OnIncomingClanMessage arg = (OnIncomingClanMessage)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnIncomingClanMessage",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnIncomingClanMessage)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnClanTypeChanged(object[] param)
        {
            OnClanTypeChanged arg = (OnClanTypeChanged)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnClanTypeChanged",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnClanTypeChanged)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnRequestDeclined(object[] param)
        {
            OnRequestDeclined arg = (OnRequestDeclined)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnRequestDeclined",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnRequestDeclined)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnAssignedRoleEvent(object[] param)
        {
            OnAssignedRoleEvent arg = (OnAssignedRoleEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnAssignedRoleEvent",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnAssignedRoleEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnMemberJoinedToClan(object[] param)
        {
            OnMemberJoinedToClanEvent arg = (OnMemberJoinedToClanEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnMemberJoinedToClan",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnMemberJoinedToClanEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnJoinedToClan(object[] param)
        {
            OnJoinedToClanEvent arg = (OnJoinedToClanEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnJoinedToClan",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnJoinedToClanEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        private void OnInviteRequestAcceptedEvent(object[] param)
        {
            OnInviteRequestAcceptedEvent arg = (OnInviteRequestAcceptedEvent)param[0];
            ResponseMessage responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = "OnInviteRequestAccepted",
                    ListenerName = eventListenerName
                }
            };
            responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(typeof(OnInviteRequestAcceptedEvent)).ToBytes(arg));
            User.SendResponce(responseMessage);
        }

        public void SendEvent(string eventName, object[] param)
        {
            switch (eventName)
            {
                case "OnLeftFromClan":
                    OnLeftFromClan(param);
                    break;
                case "OnPlayerAttributesChanged":
                    OnPlayerAttributes(param);
                    break;
                case "OnJoinRequestTaken":
                    OnJoinRequestTaken(param);
                    break;
                case "OnInvitedToClanEvent":
                    OnInvitedToClanEvent(param);
                    break;
                case "OnClanNameChanged":
                    OnClanNameChanged(param);
                    break;
                case "OnClanDescriptionChanged":
                    OnClanDescriptionChanged(param);
                    break;
                case "OnClanAvatarChanged":
                    OnClanAvatarChanged(param);
                    break;
                case "OnKickedEvent":
                    OnKickedEvent(param);
                    break;
                case "OnKickedMember":
                    OnKickedMember(param);
                    break;
                case "OnIncomingClanMessage":
                    OnIncomingClanMessage(param);
                    break;
                case "OnClanTypeChanged":
                    OnClanTypeChanged(param);
                    break;
                case "OnRequestDeclined":
                    OnRequestDeclined(param);
                    break;
                case "OnAssignedRoleEvent":
                    OnAssignedRoleEvent(param);
                    break;
                case "OnMemberJoinedToClan":
                    OnMemberJoinedToClan(param);
                    break;
                case "OnJoinedToClan":
                    OnJoinedToClan(param);
                    break;
                case "OnInviteRequestAccepted":
                    OnInviteRequestAcceptedEvent(param);
                    break;
                default:
                    Console.WriteLine("Event error in " + eventListenerName + ": " + eventName);
                    break;
            }
        }
    }
}
