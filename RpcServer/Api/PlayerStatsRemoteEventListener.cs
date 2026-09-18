using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.RpcServer;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// Payload события onStatsUpdatedEvent в клиенте 0.17: { repeated PlayerStat stats = 1 }.
    /// </summary>
    public class OnStatsUpdatedEvent : IMessage<OnStatsUpdatedEvent>
    {
        public MessageDescriptor Descriptor => null;
        public Google.Protobuf.Collections.RepeatedField<PlayerStat> Stats { get; } =
            new Google.Protobuf.Collections.RepeatedField<PlayerStat>();

        public static MessageParser<OnStatsUpdatedEvent> Parser =
            new MessageParser<OnStatsUpdatedEvent>(() => new OnStatsUpdatedEvent());
        private static readonly FieldCodec<PlayerStat> _statsCodec = FieldCodec.ForMessage(10, PlayerStat.Parser);

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10) Stats.AddEntriesFrom(input, _statsCodec);
                else input.SkipLastField();
            }
        }

        public void MergeFrom(OnStatsUpdatedEvent other)
        {
            if (other == null) return;
            Stats.Add(other.Stats);
        }

        public void WriteTo(CodedOutputStream output) => Stats.WriteTo(output, _statsCodec);
        public int CalculateSize() => Stats.CalculateSize(_statsCodec);
        public OnStatsUpdatedEvent Clone()
        {
            var e = new OnStatsUpdatedEvent();
            e.Stats.Add(Stats);
            return e;
        }
        public bool Equals(OnStatsUpdatedEvent other) => other != null && Stats.Equals(other.Stats);
    }

    /// <summary>
    /// Клиент 0.17 кэширует getCurrentStats; onStatsUpdatedEvent/onStatsUpdated мержат дельту в профиль.
    /// </summary>
    public class PlayerStatsRemoteEventListener : IEventSender
    {
        private UserService User { get; set; }

        public string eventListenerName { get; set; } = "PlayerStatsRemoteEventListener";

        public PlayerStatsRemoteEventListener(UserService user)
        {
            User = user;
        }

        public void SendEvent(string eventName, object[] param)
        {
            if (eventName != "onStatsUpdatedEvent" && eventName != "onStatsUpdated")
            {
                Logger.LogWarn("Event error in " + eventListenerName + ": " + eventName);
                return;
            }

            var responseMessage = new ResponseMessage
            {
                EventResponse = new EventResponse
                {
                    EventName = eventName,
                    ListenerName = eventListenerName
                }
            };

            foreach (object item in param ?? Array.Empty<object>())
            {
                if (item == null)
                {
                    responseMessage.EventResponse.Params.Add(new BinaryValue { IsNull = true });
                    continue;
                }

                if (item is IMessage message)
                {
                    responseMessage.EventResponse.Params.Add(new BinaryValue { IsNull = false, One = message.ToByteString() });
                    continue;
                }

                responseMessage.EventResponse.Params.Add(
                    ProtoReflectionUtils.CreateToByteMethod(item.GetType()).ToBytes(item));
            }

            User.SendResponce(responseMessage);
        }

        /// <summary>Мгновенное обновление профиля: полный снимок ranked/allies/level статов.</summary>
        public static void PushProfileUpdate(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            var stats = PlayerStatsRemoteService.BuildClientPushSnapshot(playerId);
            PushStats(playerId, stats);
        }

        public static void PushStats(string playerId, IEnumerable<PlayerStat> stats)
        {
            if (string.IsNullOrWhiteSpace(playerId) || stats == null) return;

            var list = stats.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Name)).ToList();
            if (list.Count == 0) return;

            var evt = new OnStatsUpdatedEvent();
            evt.Stats.Add(list);
            var legacyArray = list.ToArray();

            try
            {
                int delivered = 0;
                var sentSockets = new HashSet<int>();

                void Deliver(UserService svc)
                {
                    if (svc?.TcpClient == null || !svc.IsSessionAlive()) return;
                    int key = svc.TcpClient.GetHashCode();
                    if (!sentSockets.Add(key)) return;

                    var listener = new PlayerStatsRemoteEventListener(svc);
                    listener.SendEvent("onStatsUpdatedEvent", new object[] { evt });
                    listener.SendEvent("onStatsUpdated", new object[] { legacyArray });
                    delivered++;
                }

                if (StaticClasses.UserServices.TryGetValue(playerId, out var primary))
                    Deliver(primary);

                if (StaticClasses.AllUserServices.TryGetValue(playerId, out var all) && all != null)
                {
                    List<UserService> snapshot;
                    lock (all) { snapshot = all.ToList(); }
                    foreach (var svc in snapshot)
                        Deliver(svc);
                }

                if (delivered == 0
                    && StaticClasses.EventSenders.TryGetValue(playerId, out var senders)
                    && senders != null)
                {
                    var sender = senders.FirstOrDefault(s => s is PlayerStatsRemoteEventListener);
                    if (sender != null)
                    {
                        sender.SendEvent("onStatsUpdatedEvent", new object[] { evt });
                        sender.SendEvent("onStatsUpdated", new object[] { legacyArray });
                        delivered++;
                    }
                }

                if (delivered == 0)
                    Logger.LogWarn($"[Stats] PlayerStatsRemoteEventListener not found for {playerId}");
                else
                    Logger.Log($"[Stats] Pushed stats update to {playerId} ({list.Count} stats, sockets={delivered})");
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Stats] PushStats failed for {playerId}: {ex.Message}");
            }
        }

        public static void PushInt(string playerId, params (string name, int value)[] stats)
        {
            if (stats == null || stats.Length == 0) return;
            PushStats(playerId, stats.Select(s => new PlayerStat
            {
                Name = s.name,
                Type = StatDefType.Int,
                IntValue = s.value,
                LongValue = s.value
            }));
        }
    }
}
