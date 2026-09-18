using System;
using System.Collections.Generic;
using System.Linq;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.MongoDB;
using RpcException = Axlebolt.RpcSupport.Protobuf.Exception;
using SysException = System.Exception;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// Broadcasts marketplace events to all subscribed players in real-time.
    /// </summary>
    public static class MarketplaceBroadcaster
    {
        /// <summary>
        /// Broadcast onTradeRequestOpened + onTradeUpdated to all players subscribed to this itemDefinitionId.
        /// Called when a new sale request is created.
        /// </summary>
        public static void BroadcastSaleOpened(OpenRequest openRequest, Trade trade, string excludePlayerId = null)
        {
            try
            {
                OnTradeRequestOpenedEvent tradeEvent = new OnTradeRequestOpenedEvent { Request = openRequest };
                OnTradeUpdatedEvent tradeUpdated = new OnTradeUpdatedEvent { Trade = trade };

                BinaryValue valTrade = new ToByteMethod(typeof(OnTradeRequestOpenedEvent)).ToBytes(tradeEvent);
                BinaryValue valUpdated = new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdated);

                foreach (var kv in StaticClasses.Subscribes)
                {
                    string pid = kv.Key;
                    int subItemId = kv.Value;
                    if (pid == excludePlayerId) continue;
                    if (subItemId != openRequest.ItemDefinitionId) continue;

                    if (StaticClasses.UserServices.TryGetValue(pid, out UserService us))
                    {
                        try
                        {
                            us.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeRequestOpened", Params = { valTrade } } });
                            us.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeUpdated", Params = { valUpdated } } });
                        }
                        catch { }
                    }
                }

                // Also broadcast onTradeUpdated to players on the trades list page
                BroadcastTradeUpdated(trade, excludePlayerId);
            }
            catch (SysException ex)
            {
                Console.WriteLine($"[MarketplaceBroadcaster] BroadcastSaleOpened error: {ex.Message}");
            }
        }

        /// <summary>
        /// Broadcast onTradeRequestClosed + onTradeUpdated to all players subscribed to this itemDefinitionId.
        /// Called when a sale request is closed (bought or cancelled).
        /// </summary>
        public static void BroadcastSaleClosed(ClosedRequest closedRequest, Trade trade, string excludePlayerId = null)
        {
            try
            {
                OnTradeRequestClosedEvent tradeEvent = new OnTradeRequestClosedEvent { Request = closedRequest };
                OnTradeUpdatedEvent tradeUpdated = new OnTradeUpdatedEvent { Trade = trade };

                BinaryValue valClosed = new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(tradeEvent);
                BinaryValue valUpdated = new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdated);

                foreach (var kv in StaticClasses.Subscribes)
                {
                    string pid = kv.Key;
                    int subItemId = kv.Value;
                    if (pid == excludePlayerId) continue;
                    if (subItemId != closedRequest.ItemDefinitionId) continue;

                    if (StaticClasses.UserServices.TryGetValue(pid, out UserService us))
                    {
                        try
                        {
                            us.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeRequestClosed", Params = { valClosed } } });
                            us.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeUpdated", Params = { valUpdated } } });
                        }
                        catch { }
                    }
                }

                BroadcastTradeUpdated(trade, excludePlayerId);
            }
            catch (SysException ex)
            {
                Console.WriteLine($"[MarketplaceBroadcaster] BroadcastSaleClosed error: {ex.Message}");
            }
        }

        /// <summary>
        /// Broadcast onTradeUpdated to all players on the trades list page.
        /// </summary>
        public static void BroadcastTradeUpdated(Trade trade, string excludePlayerId = null)
        {
            try
            {
                OnTradeUpdatedEvent tradeUpdated = new OnTradeUpdatedEvent { Trade = trade };
                BinaryValue val = new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdated);

                foreach (string pid in StaticClasses.SubscribesTrades.Keys)
                {
                    if (pid == excludePlayerId) continue;
                    if (StaticClasses.UserServices.TryGetValue(pid, out UserService us))
                    {
                        try { us.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeUpdated", Params = { val } } }); }
                        catch { }
                    }
                }
            }
            catch (SysException ex)
            {
                Console.WriteLine($"[MarketplaceBroadcaster] BroadcastTradeUpdated error: {ex.Message}");
            }
        }

        /// <summary>
        /// Send onPlayerRequestOpened to a specific player.
        /// </summary>
        public static void SendPlayerRequestOpened(string playerId, OpenRequest openRequest)
        {
            if (!StaticClasses.UserServices.TryGetValue(playerId, out UserService us)) return;
            try
            {
                OnPlayerRequestOpenedEvent ev = new OnPlayerRequestOpenedEvent { Request = openRequest };
                us.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onPlayerRequestOpened", Params = { new ToByteMethod(typeof(OnPlayerRequestOpenedEvent)).ToBytes(ev) } } });
            }
            catch { }
        }

        /// <summary>
        /// Send onPlayerRequestClosed to a specific player.
        /// </summary>
        public static void SendPlayerRequestClosed(string playerId, ClosedRequest closedRequest, Axlebolt.Bolt.Protobuf.InventoryItem item = null)
        {
            if (!StaticClasses.UserServices.TryGetValue(playerId, out UserService us)) return;
            try
            {
                OnPlayerRequestClosedEvent ev = new OnPlayerRequestClosedEvent { Request = closedRequest };
                if (item != null) ev.Item = item;
                us.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onPlayerRequestClosed", Params = { new ToByteMethod(typeof(OnPlayerRequestClosedEvent)).ToBytes(ev) } } });
            }
            catch { }
        }
    }
}
