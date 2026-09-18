using System;
using System.Collections.Generic;
using System.Linq;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.RpcServer;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// Payload onInventoryChanged (KHDDLNOBDHH): repeated InventoryItem added=1, removed=2.
    /// </summary>
    public class OnInventoryChangedEvent : IMessage<OnInventoryChangedEvent>
    {
        public MessageDescriptor Descriptor => null;
        public Google.Protobuf.Collections.RepeatedField<InventoryItem> Added { get; } =
            new Google.Protobuf.Collections.RepeatedField<InventoryItem>();
        public Google.Protobuf.Collections.RepeatedField<InventoryItem> Removed { get; } =
            new Google.Protobuf.Collections.RepeatedField<InventoryItem>();

        public static MessageParser<OnInventoryChangedEvent> Parser =
            new MessageParser<OnInventoryChangedEvent>(() => new OnInventoryChangedEvent());
        private static readonly FieldCodec<InventoryItem> _addedCodec = FieldCodec.ForMessage(10, InventoryItem.Parser);
        private static readonly FieldCodec<InventoryItem> _removedCodec = FieldCodec.ForMessage(18, InventoryItem.Parser);

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10) Added.AddEntriesFrom(input, _addedCodec);
                else if (tag == 18) Removed.AddEntriesFrom(input, _removedCodec);
                else input.SkipLastField();
            }
        }

        public void MergeFrom(OnInventoryChangedEvent other)
        {
            if (other == null) return;
            Added.Add(other.Added);
            Removed.Add(other.Removed);
        }

        public void WriteTo(CodedOutputStream output)
        {
            Added.WriteTo(output, _addedCodec);
            Removed.WriteTo(output, _removedCodec);
        }

        public int CalculateSize() => Added.CalculateSize(_addedCodec) + Removed.CalculateSize(_removedCodec);

        public OnInventoryChangedEvent Clone()
        {
            var e = new OnInventoryChangedEvent();
            e.Added.Add(Added);
            e.Removed.Add(Removed);
            return e;
        }

        public bool Equals(OnInventoryChangedEvent other) =>
            other != null && Added.Equals(other.Added) && Removed.Equals(other.Removed);
    }

    public class InventoryRemoteEventListener : IEventSender
    {
        private UserService User { get; set; }
        public string eventListenerName { get; set; } = "InventoryRemoteEventListener";

        public InventoryRemoteEventListener(UserService user)
        {
            User = user;
        }

        public void SendEvent(string eventName, object[] param)
        {
            if (eventName != "onInventoryChanged" && eventName != "onInventoryUpdated")
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
                if (item is IMessage message)
                    responseMessage.EventResponse.Params.Add(new BinaryValue { IsNull = false, One = message.ToByteString() });
                else
                    responseMessage.EventResponse.Params.Add(new BinaryValue { IsNull = true });
            }

            User.SendResponce(responseMessage);
        }

        public static bool ClearPlayerAndNotify(ObjectId playerOid)
        {
            var removed = new List<InventoryItem>();
            try
            {
                var doc = BoltGameDatabaseProvider.Instance.GetPlayerInventoryDocument(playerOid);
                if (doc?.InventoryItems != null)
                {
                    foreach (var element in doc.InventoryItems.Elements)
                    {
                        try
                        {
                            var item = element.ToInventory();
                            if (item != null) removed.Add(item);
                        }
                        catch { }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogWarn("[Inventory] Snapshot before clear failed: " + ex.Message);
            }

            bool ok = BoltGameDatabaseProvider.Instance.ClearPlayerInventory(playerOid);
            PushRemoved(playerOid.ToString(), removed);
            return ok;
        }

        public static void PushRemoved(string playerId, IEnumerable<InventoryItem> removed)
        {
            PushDelta(playerId, null, removed);
        }

        public static void PushDelta(string playerId, IEnumerable<InventoryItem> added, IEnumerable<InventoryItem> removed)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;

            var evt = new OnInventoryChangedEvent();
            if (added != null)
            {
                foreach (var item in added)
                {
                    if (item != null) evt.Added.Add(item);
                }
            }
            if (removed != null)
            {
                foreach (var item in removed)
                {
                    if (item != null) evt.Removed.Add(item);
                }
            }
            if (evt.Added.Count == 0 && evt.Removed.Count == 0) return;

            try
            {
                if (StaticClasses.TryGetEventSenders(playerId, out var senders) && senders != null)
                {
                    var registered = senders.FirstOrDefault(s => s is InventoryRemoteEventListener);
                    if (registered != null)
                    {
                        registered.SendEvent("onInventoryChanged", new object[] { evt });
                        Logger.Log($"[Inventory] Pushed onInventoryChanged to {playerId} (added={evt.Added.Count}, removed={evt.Removed.Count})");
                        return;
                    }
                }

                var live = StaticClasses.ResolveEventDeliveryService(playerId);
                if (live != null)
                {
                    var listener = new InventoryRemoteEventListener(live);
                    listener.SendEvent("onInventoryChanged", new object[] { evt });
                    Logger.Log($"[Inventory] Pushed onInventoryChanged (live fallback) to {playerId}");
                    return;
                }

                Logger.LogWarn($"[Inventory] InventoryRemoteEventListener not found for {playerId}");
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Inventory] PushDelta failed for {playerId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Мгновенный UI голды/серебра.
        /// Dump 0.17 OfferWallRemoteEventListener:
        ///   onOfferWallRewarded(CurrencyAmount[]) — obsolete
        ///   onOfferWallRewardedEvent({ repeated CurrencyAmount field=1 })
        /// RewardInfo (items=1, currencies=2) — НЕ тот wire, клиент не видит дельту.
        /// InventoryRemoteEventListener.onInventoryUpdated(PlayerInventory) — есть в dump.
        /// </summary>
        public static void PushCurrencyBalance(string playerId, int currencyId, double newAbsoluteBalance)
        {
            PushCurrencyReward(playerId, currencyId, delta: 0, newAbsoluteBalance: newAbsoluteBalance);
        }

        public static void PushCurrencyReward(string playerId, int currencyId, double delta, double newAbsoluteBalance = -1)
        {
            if (string.IsNullOrWhiteSpace(playerId) || currencyId <= 0) return;
            try
            {
                int deltaInt = (int)Math.Round(Math.Max(0, delta));
                var amount = new CurrencyAmount
                {
                    CurrencyId = currencyId,
                    Value = deltaInt,
                    OldValue = deltaInt
                };

                // onOfferWallRewardedEvent: field1 = repeated CurrencyAmount (не RewardInfo!).
                byte[] offerEventBytes = null;
                if (deltaInt > 0)
                {
                    using (var ms = new System.IO.MemoryStream())
                    {
                        var cos = new Google.Protobuf.CodedOutputStream(ms);
                        cos.WriteRawTag(10);
                        cos.WriteMessage(amount);
                        cos.Flush();
                        offerEventBytes = ms.ToArray();
                    }
                }

                var inv = new PlayerInventory();
                try
                {
                    var doc = BoltGameDatabaseProvider.Instance.GetPlayerInventoryDocument(ObjectId.Parse(playerId));
                    if (doc?.Currencies != null)
                    {
                        foreach (var el in doc.Currencies.Elements)
                        {
                            if (!int.TryParse(el.Name, out int cid) || cid <= 0) continue;
                            double bal = el.Value.IsDouble ? el.Value.AsDouble
                                : el.Value.IsInt32 ? el.Value.AsInt32
                                : el.Value.ToDouble();
                            if (cid == currencyId && newAbsoluteBalance >= 0)
                                bal = newAbsoluteBalance;
                            inv.Currencies.Add(new CurrencyAmount
                            {
                                CurrencyId = cid,
                                Value = (float)bal,
                                OldValue = (int)Math.Round(bal)
                            });
                        }
                    }
                }
                catch { }

                if (inv.Currencies.Count == 0 && newAbsoluteBalance >= 0)
                {
                    inv.Currencies.Add(new CurrencyAmount
                    {
                        CurrencyId = currencyId,
                        Value = (float)newAbsoluteBalance,
                        OldValue = (int)Math.Round(newAbsoluteBalance)
                    });
                }

                void Deliver(UserService svc)
                {
                    if (svc == null || !svc.IsSessionAlive()) return;

                    if (deltaInt > 0)
                    {
                        try
                        {
                            // CurrencyAmount[] — точная сигнатура obsolete onOfferWallRewarded.
                            var arrBin = new ToByteMethod(typeof(CurrencyAmount[])).ToBytes(new[] { amount });
                            svc.SendResponce(new ResponseMessage
                            {
                                EventResponse = new EventResponse
                                {
                                    ListenerName = "OfferWallRemoteEventListener",
                                    EventName = "onOfferWallRewarded",
                                    Params = { arrBin }
                                }
                            });
                        }
                        catch { }

                        if (offerEventBytes != null)
                        {
                            try
                            {
                                svc.SendResponce(new ResponseMessage
                                {
                                    EventResponse = new EventResponse
                                    {
                                        ListenerName = "OfferWallRemoteEventListener",
                                        EventName = "onOfferWallRewardedEvent",
                                        Params = { new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(offerEventBytes) } }
                                    }
                                });
                            }
                            catch { }
                        }

                        try
                        {
                            var arrBin = new ToByteMethod(typeof(CurrencyAmount[])).ToBytes(new[] { amount });
                            svc.SendResponce(new ResponseMessage
                            {
                                EventResponse = new EventResponse
                                {
                                    ListenerName = "AdsRemoteEventListener",
                                    EventName = "onAdRewarded",
                                    Params = { arrBin }
                                }
                            });
                        }
                        catch { }
                    }

                    try
                    {
                        var listener = new InventoryRemoteEventListener(svc);
                        listener.SendEvent("onInventoryUpdated", new object[] { inv });
                    }
                    catch { }
                }

                int n = 0;
                var primary = StaticClasses.ResolveEventDeliveryService(playerId);
                if (primary != null)
                {
                    Deliver(primary);
                    n++;
                }
                if (StaticClasses.AllUserServices.TryGetValue(playerId, out var all) && all != null)
                {
                    List<UserService> snap;
                    lock (all) { snap = all.ToList(); }
                    foreach (var s in snap)
                    {
                        Deliver(s);
                        n++;
                    }
                }
                Logger.Log($"[Currency] Pushed CurrencyAmount[] delta={deltaInt} abs={newAbsoluteBalance:0.##} currency={currencyId} → {playerId} sockets~{n}");
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Currency] PushCurrencyReward failed for {playerId}: {ex.Message}");
            }
        }
    }
}
