using System.Linq;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer.Api;

namespace StandRiseServer.RpcServer
{
    /// <summary>Личное сообщение от системного аккаунта (сохранение + push онлайн).</summary>
    public static class GameChatNotify
    {
        public static void SendFriendMessage(string senderPlayerId, string receiverPlayerId, string message)
        {
            if (string.IsNullOrWhiteSpace(senderPlayerId) || string.IsNullOrWhiteSpace(receiverPlayerId))
                return;
            message = (message ?? "").Trim();
            if (message.Length == 0) return;

            long timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            BoltMainDatabaseProvider.Instance.SaveChatMessage(senderPlayerId, receiverPlayerId, message, timestamp);

            var userMessage = new UserMessage
            {
                SenderId = senderPlayerId,
                Message = message,
                Timestamp = timestamp,
                IsRead = false
            };

            if (StaticClasses.TryGetEventSenders(receiverPlayerId, out var receiverSenders))
            {
                var msgListener = receiverSenders.FirstOrDefault(s => s is ChatRemoteEventListener);
                msgListener?.SendEvent("onMsgFromFriend", new object[] { userMessage });
            }
        }
    }
}
