using Axlebolt.RpcSupport.Protobuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer;
using Google.Protobuf;
using MongoDB.Bson;
using Axlebolt.Bolt.Protobuf;
using Google.Protobuf.Collections;
using StandRiseServer.MongoDB.Game;
using MongoDB.Driver;
using FriendHelper = StandRiseServer.MongoDB.Main.FriendHelper;
using StandRiseServer.RpcServer.Security;

namespace StandRiseServer.RpcServer.Api
{
    public class MarketplaceRemoteService : RpcClass
    {
        public MarketplaceRemoteService(UserService user) : base(user)
        {
        }

        protected async void getTrades(BinaryValue[] values, string guid, string methodName = null)
        {
            try
            {
                GetTradesArgs args = (GetTradesArgs)new FromByteMethod(typeof(GetTradesArgs)).FromBytes(values[0]);

                RepeatedField<int> items = args.ItemDefinitionIds;
                Trade[] trades = new Trade[items.Count];

                var BoltGame = BoltGameDatabaseProvider.Instance;
                var saleStats = BoltGame.GetMarketplaceStatsForItems(items);
                // Aggregate purchase request stats too so the marketplace browse
                // view shows the same "purchase requests" badge that is visible
                // when opening the same item from the inventory.
                var purchaseStats = BoltGame.GetPurchaseStatsForItems(items);

                for (int i = 0; i < trades.Length; i++)
                {
                    trades[i] = new Trade();
                    trades[i].Id = items[i];

                    // SECURITY: Hide items whose collection is blocked from the market.
                    bool hidden = IsCollectionHidden(items[i]);

                    if (!hidden && saleStats.TryGetValue(items[i], out var s))
                    {
                        trades[i].SalesCount = s.Count;
                        trades[i].SalesPrice = s.MinPrice;
                    }
                    else
                    {
                        trades[i].SalesCount = 0;
                        trades[i].SalesPrice = 0.0f;
                    }

                    if (!hidden && purchaseStats.TryGetValue(items[i], out var p))
                    {
                        trades[i].PurchasesCount = p.Count;
                        trades[i].PurchasesPrice = p.MaxPrice;
                    }
                    else
                    {
                        trades[i].PurchasesCount = 0;
                        trades[i].PurchasesPrice = 0.0f;
                    }
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = (methodName != null && methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase)) ? new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(WrapRepeatedMessages(trades)) } : new ToByteMethod(typeof(Trade[])).ToBytes(trades)
                    }
                });
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in getTrades: {ex}");
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                        { 
                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                            Code = 500
                        }
                    }
                });
            }
        }

        protected async void getTrade(BinaryValue[] values, string guid, string methodName = null)
        {
            try
            {
                GetTradeArgs args = (GetTradeArgs)new FromByteMethod(typeof(GetTradeArgs)).FromBytes(values[0]);

                Trade trade = new Trade();
                trade.Id = args.Id;

                var BoltGame = BoltGameDatabaseProvider.Instance;
                var activeSaleRequests = BoltGame.GetActiveMarketplaceRequestsByItem(args.Id, 0, 100);
                var activePurchaseRequests = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = args.Id });

                // SECURITY: Hide items whose collection is blocked from the market.
                bool hidden = IsCollectionHidden(args.Id);

                trade.SalesCount = hidden ? 0 : activeSaleRequests.Count;
                trade.SalesPrice = (!hidden && activeSaleRequests.Count > 0) ? activeSaleRequests.Min(r => r.price) : 0.0f;
                trade.PurchasesCount = hidden ? 0 : activePurchaseRequests.Sum(r => r.Quantity);
                trade.PurchasesPrice = (!hidden && activePurchaseRequests.Length > 0) ? activePurchaseRequests.Max(r => r.Price) : 0.0f;

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = (methodName != null && methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase)) ? new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(WrapMessageResponse(trade)) } : new ToByteMethod(typeof(Trade)).ToBytes(trade)
                    }
                });
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in getTrade: {ex}");
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                        { 
                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                            Code = 500
                        }
                    }
                });
            }
        }

        private static byte[] WrapSingleMessage(byte[] messagePayload)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                using (var output = new CodedOutputStream(stream))
                {
                    output.WriteRawTag(10);
                    output.WriteBytes(ByteString.CopyFrom(messagePayload));
                    output.Flush();
                    return stream.ToArray();
                }
            }
        }

        
        private byte[] WrapMessageResponse(Google.Protobuf.IMessage message)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                var output = new Google.Protobuf.CodedOutputStream(stream);
                output.WriteRawTag(10);
                output.WriteMessage(message);
                output.Flush();
                return stream.ToArray();
            }
        }

        private byte[] WrapRepeatedMessages<T>(System.Collections.Generic.IEnumerable<T> messages) where T : Google.Protobuf.IMessage
        {
            using (var stream = new System.IO.MemoryStream())
            {
                var output = new Google.Protobuf.CodedOutputStream(stream);
                foreach (var msg in messages)
                {
                    output.WriteRawTag(10);
                    output.WriteMessage(msg);
                }
                output.Flush();
                return stream.ToArray();
            }
        }

        public async void getMarketplaceSettings(BinaryValue[] values, string guid, string methodName)
        {
            try
            {
                string playerId = null;
                StaticClasses.Users.TryGetValue(_user.TcpClient, out playerId);
                MarketplaceSettings settings = await BoltGameDatabaseProvider.Instance.GetMarketplaceSettingsForPlayerAsync(playerId);

                if (methodName.Equals("getMarketplaceSettings2", System.StringComparison.OrdinalIgnoreCase))
                {
                    byte[] payload = settings.ToByteArray();
                    byte[] wrapped = WrapSingleMessage(payload);
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Return = new BinaryValue
                            {
                                IsNull = false,
                                One = ByteString.CopyFrom(wrapped)
                            }
                        }
                    });
                    return;
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = (methodName != null && methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase)) ? new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(WrapMessageResponse(settings)) } : new ToByteMethod(typeof(MarketplaceSettings)).ToBytes(settings)
                    }
                });
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in getMarketplaceSettings: {ex}");
                // Не отдаём 500 — клиент рулетки/магазина показывает "ошибка подключения к магазину".
                var fallback = new MarketplaceSettings
                {
                    CommissionPercent = 0.1f,
                    MinCommission = 0.03f,
                    CurrencyId = 102,
                    Enabled = true
                };
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(MarketplaceSettings)).ToBytes(fallback)
                    }
                });
            }
        }

        public void getPlayerProcessingRequests(BinaryValue[] values, string guid, string methodName)
        {
            if (methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase))
            {
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new BinaryValue
                        {
                            IsNull = false,
                            One = ByteString.Empty
                        }
                    }
                });
                return;
            }

            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
            {
                var processingRequests = BoltGameDatabaseProvider.Instance.GetPlayerMarketplaceRequests(playerId, MarketplaceRequestStatus.Processing);
                
                ProcessingRequest[] requests = new ProcessingRequest[processingRequests.Count];
                for (int i = 0; i < processingRequests.Count; i++)
                {
                    var req = processingRequests[i];
                    requests[i] = new ProcessingRequest();
                    requests[i].Id = req._id.ToString();
                    requests[i].ItemDefinitionId = req.itemDefinitionId;
                    requests[i].Quantity = req.quantity;
                    requests[i].State = ProcessingState.Creating;
                    requests[i].Price = req.price;
                    requests[i].Type = (Axlebolt.Bolt.Protobuf.MarketRequestType)req.type;
                    requests[i].SaleRequestId = req._id.ToString();
                    requests[i].CreateDate = req.createDate.Ticks;
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(ProcessingRequest[])).ToBytes(requests)
                    }
                });
            }
            else
            {
                ProcessingRequest[] requests = new ProcessingRequest[0];
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(ProcessingRequest[])).ToBytes(requests)
                    }
                });
            }
            return;
        }


        public async void getPlayerOpenRequests(BinaryValue[] values, string guid, string methodName)
        {
            if (methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase))
            {
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new BinaryValue
                        {
                            IsNull = false,
                            One = ByteString.Empty
                        }
                    }
                });
                return;
            }

            try
            {
                if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    var activeRequests = BoltGameDatabaseProvider.Instance.GetPlayerMarketplaceRequests(playerId, MarketplaceRequestStatus.Active);
                    
                    OpenRequest[] requests = new OpenRequest[activeRequests.Count];

                    // PRE-FETCH player once to avoid N queries for N listings
                    PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(playerId));
                    Axlebolt.Bolt.Protobuf.Player myPlayer = FriendHelper.GetPlayer(playerId);

                    Axlebolt.Bolt.Protobuf.Player player = new Axlebolt.Bolt.Protobuf.Player();
                    if (myPlayer != null && playerDocument != null)
                    {
                        player.Id = myPlayer.Id;
                        player.Uid = myPlayer.Uid;
                        player.Name = myPlayer.Name;
                        player.AvatarId = myPlayer.AvatarId;
                        player.TimeInGame = playerDocument.timeInGame;
                        player.RegistrationDate = playerDocument.createDate.MillisecondsSinceEpoch;
                        Axlebolt.Bolt.Protobuf.PlayerStatus playerStatus = new Axlebolt.Bolt.Protobuf.PlayerStatus();
                        playerStatus.PlayerId = myPlayer.Id;
                        player.PlayerStatus = playerStatus;
                    }

                    for (int i = 0; i < activeRequests.Count; i++)
                    {
                        var req = activeRequests[i];
                        
                        requests[i] = new OpenRequest();
                        requests[i].Id = req._id.ToString();
                        requests[i].Creator = player;
                        requests[i].ItemDefinitionId = req.itemDefinitionId;
                        requests[i].Price = req.price;
                        requests[i].CreateDate = req.createDate.Ticks;
                        requests[i].Type = (Axlebolt.Bolt.Protobuf.MarketRequestType)req.type;
                        requests[i].Quantity = req.quantity;

                        // Р˜РЎРџР РђР’Р›Р•РќР˜Р•: РљРѕРїРёСЂСѓРµРј СЃРІРѕР№СЃС‚РІР° (РЅР°РєР»РµР№РєРё, stattrack Рё С‚.Рґ.)
                        if (req.properties != null)
                        {
                            for (int j = 0; j < 5; j++)
                            {
                                if (req.properties.TryGetValue($"sticker_{j}", out var stickerValue))
                                {
                                    requests[i].Properties.Add($"sticker_{j}", new Axlebolt.Bolt.Protobuf.InventoryItemProperty
                                    {
                                        Type = Axlebolt.Bolt.Protobuf.PropertyType.Int,
                                        IntValue = stickerValue.AsInt32
                                    });
                                }
                            }
                            if (req.properties.TryGetValue("stattrack_value", out var stattrackValue))
                            {
                                requests[i].Properties.Add("stattrack_value", new Axlebolt.Bolt.Protobuf.InventoryItemProperty
                                {
                                    Type = Axlebolt.Bolt.Protobuf.PropertyType.Int,
                                    IntValue = stattrackValue.AsInt32
                                });
                            }
                        }
                    }

                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Return = (methodName != null && methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase)) ? new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(WrapRepeatedMessages(requests)) } : new ToByteMethod(typeof(OpenRequest[])).ToBytes(requests)
                        }
                    });
                }
                else
                {
                    OpenRequest[] requests = new OpenRequest[0];
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Return = (methodName != null && methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase)) ? new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(WrapRepeatedMessages(requests)) } : new ToByteMethod(typeof(OpenRequest[])).ToBytes(requests)
                        }
                    });
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in getPlayerOpenRequests: {ex}");
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                        { 
                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                            Code = 500
                        }
                    }
                });
            }
        }

        protected async void createSaleRequest(BinaryValue[] values, string guid, string methodName = null)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                            { 
                                Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                                Code = 401
                            }
                        }
                    });
                    return;
                }

                // SECURITY: Check if player is banned from marketplace
                if (StaticClasses.MarketplaceBans.TryGetValue(playerId, out DateTime banExpiry))
                {
                    if (DateTime.UtcNow < banExpiry)
                    {
                        Console.WriteLine($"[MarketplaceRemoteService] BLOCKED: Banned player {playerId} attempted marketplace request. Ban expires: {banExpiry}");
                        _user.SendResponce(new ResponseMessage
                        {
                            RpcResponse = new RpcResponse
                            {
                                Id = guid,
                                Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                                { 
                                    Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                                    Code = 403 // Forbidden
                                }
                            }
                        });
                        return;
                    }
                    else
                    {
                        // Ban expired, remove it
                        StaticClasses.MarketplaceBans.TryRemove(playerId, out _);
                    }
                }

                // SECURITY: Rate limiting - max 3 requests per second
                DateTime now = DateTime.UtcNow;
                if (StaticClasses.MarketplaceRateLimits.TryGetValue(playerId, out var rateLimit))
                {
                    // Check if within 1 second window
                    if ((now - rateLimit.timestamp).TotalSeconds < 1)
                    {
                        int newCount = rateLimit.count + 1;
                        
                        // If 3+ requests in 1 second, REJECT but DON'T BAN
                        if (newCount >= 3)
                        {
                            // Р’СЂРµРјРµРЅРЅР°СЏ Р±Р»РѕРєРёСЂРѕРІРєР° РјР°СЂРєРµС‚РїР»РµР№СЃР° (РЅРµ Р±Р°РЅ Р°РєРєР°СѓРЅС‚Р°)
                            DateTime blockUntil = DateTime.UtcNow.AddMinutes(5);
                            StaticClasses.MarketplaceBans.TryAdd(playerId, blockUntil);
                            
                            Console.WriteLine($"[MarketplaceRemoteService] вљ пёЏ Rate limit exceeded for player {playerId}: {newCount} requests in 1 second. Marketplace blocked for 5 minutes.");
                            
                            _user.SendResponce(new ResponseMessage
                            {
                                RpcResponse = new RpcResponse
                                {
                                    Id = guid,
                                    Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                                    { 
                                        Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                                        Code = 429 // Too Many Requests
                                    }
                                }
                            });
                            return;
                        }
                        
                        // Update count
                        StaticClasses.MarketplaceRateLimits[playerId] = (rateLimit.timestamp, newCount);
                    }
                    else
                    {
                        // Reset window
                        StaticClasses.MarketplaceRateLimits[playerId] = (now, 1);
                    }
                }
                else
                {
                    // First request
                    StaticClasses.MarketplaceRateLimits.TryAdd(playerId, (now, 1));
                }

                CreateSaleRequestArgs args = (CreateSaleRequestArgs)new FromByteMethod(typeof(CreateSaleRequestArgs)).FromBytes(values[0]);

                var BoltGame = BoltGameDatabaseProvider.Instance;
                PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(playerId));
                Axlebolt.Bolt.Protobuf.Player myPlayer = FriendHelper.GetPlayer(playerId);

                BsonDocument fullItemData = BoltGame.GetFullItemFromPlayerInventory(ObjectId.Parse(playerId), args.ItemId);
                if (fullItemData == null)
                {
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                            { 
                                Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                                Code = 404
                            }
                        }
                    });
                    return;
                }

                int itemDefinitionId = fullItemData.GetValue("itemDefinitionId").AsInt32;

                // SECURITY: Global market close + per-collection block (admin "Рынок" panel).
                string itemCollection = ResolveCollectionForItem(itemDefinitionId);
                if (!BoltGameDatabaseProvider.Instance.IsMarketAccessibleToPlayer(playerId))
                {
                    Console.WriteLine($"[MarketplaceRemoteService] REJECTED Sale from {playerId}: market is closed (на учёт).");
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                            {
                                Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                                Code = 403
                            }
                        }
                    });
                    return;
                }

                if (!string.IsNullOrEmpty(itemCollection)
                    && BoltGameDatabaseProvider.Instance.GetHiddenMarketCollections().Contains(itemCollection, StringComparer.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[MarketplaceRemoteService] REJECTED Sale from {playerId}: collection '{itemCollection}' is hidden from market.");
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                            {
                                Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                                Code = 403
                            }
                        }
                    });
                    return;
                }

                // SECURITY: Validate price range (0.01 - 1,000,000)
                if (float.IsNaN(args.Price) || float.IsInfinity(args.Price) || args.Price < 0.03f || args.Price > 1000000.0f)
                {
                    Console.WriteLine($"[MarketplaceRemoteService] REJECTED Sale Request from {playerId}: Invalid price {args.Price}");
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                            { 
                                Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                                Code = 400
                            }
                        }
                    });
                    return;
                }

                // SECURITY: ATOMIC OPERATION - Take the item from inventory first.
                // If multiple requests come in for the same item ID, only one will succeed.
                if (!BoltGame.RemoveItemIfOwned(ObjectId.Parse(playerId), args.ItemId, itemDefinitionId))
                {
                    Console.WriteLine($"[MarketplaceRemoteService] REJECTED Sale Request from {playerId}: Item {args.ItemId} not owned or already listed.");
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                            { 
                                Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                                Code = 404
                            }
                        }
                    });
                    return;
                }

                ObjectId requestId = BoltGame.CreateMarketplaceRequest(playerId, args.ItemId, itemDefinitionId, args.Price, fullItemData);

                OnTradeRequestOpenedEvent evn = new OnTradeRequestOpenedEvent();
                OnPlayerRequestOpenedEvent evp = new OnPlayerRequestOpenedEvent();
                OnTradeUpdatedEvent onTradeUpdatedEvent = new OnTradeUpdatedEvent();

                var activeRequests = BoltGame.GetActiveMarketplaceRequestsByItem(itemDefinitionId, 0, 100);
                var activePurchaseRequests = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = itemDefinitionId });
                
                Trade trade = new Trade();
                trade.Id = itemDefinitionId;
                trade.SalesCount = activeRequests.Count;
                trade.SalesPrice = activeRequests.Count > 0 ? activeRequests.Min(r => r.price) : args.Price;
                trade.PurchasesPrice = activePurchaseRequests.Length > 0 ? activePurchaseRequests.Max(r => r.Price) : 0;
                trade.PurchasesCount = activePurchaseRequests.Sum(r => r.Quantity);

                onTradeUpdatedEvent.Trade = trade;

                Axlebolt.Bolt.Protobuf.Player player = new Axlebolt.Bolt.Protobuf.Player();
                player.Id = myPlayer.Id;
                player.Uid = myPlayer.Uid;
                player.Name = myPlayer.Name;
                player.AvatarId = myPlayer.AvatarId;
                player.TimeInGame = playerDocument.timeInGame;
                player.RegistrationDate = playerDocument.createDate.MillisecondsSinceEpoch;
                Axlebolt.Bolt.Protobuf.PlayerStatus playerStatus = new Axlebolt.Bolt.Protobuf.PlayerStatus();
                playerStatus.PlayerId = myPlayer.Id;
                player.PlayerStatus = playerStatus;

                OpenRequest openRequest = new OpenRequest();
                openRequest.Id = requestId.ToString();
                openRequest.Creator = player;
                openRequest.ItemDefinitionId = itemDefinitionId;
                openRequest.Price = args.Price;
                openRequest.CreateDate = DateTime.Now.Ticks;
                openRequest.Type = Axlebolt.Bolt.Protobuf.MarketRequestType.SaleRequest;
                openRequest.Quantity = 1;
                
                // STICKER FIX: Add item properties (stickers, stattrack, etc.) to OpenRequest
                if (fullItemData != null && (fullItemData.Contains("properties") || fullItemData.Contains("Properties")))
                {
                    BsonDocument propertiesDoc = null;
                    if (fullItemData.Contains("properties"))
                    {
                        propertiesDoc = fullItemData["properties"].AsBsonDocument;
                    }
                    else if (fullItemData.Contains("Properties"))
                    {
                        propertiesDoc = fullItemData["Properties"].AsBsonDocument;
                    }

                    if (propertiesDoc != null)
                    {
                        // Copy stickers
                        for (int j = 0; j < 5; j++)
                        {
                            if (propertiesDoc.TryGetValue($"sticker_{j}", out var stickerValue))
                            {
                                openRequest.Properties.Add($"sticker_{j}", new Axlebolt.Bolt.Protobuf.InventoryItemProperty
                                {
                                    Type = Axlebolt.Bolt.Protobuf.PropertyType.Int,
                                    IntValue = stickerValue.AsInt32
                                });
                            }
                        }
                        // Copy stattrack
                        if (propertiesDoc.TryGetValue("stattrack_value", out var stattrackValue))
                        {
                            openRequest.Properties.Add("stattrack_value", new Axlebolt.Bolt.Protobuf.InventoryItemProperty
                            {
                                Type = Axlebolt.Bolt.Protobuf.PropertyType.Int,
                                IntValue = stattrackValue.AsInt32
                            });
                        }
                    }
                }

                evn.Request = openRequest;
                evp.Request = openRequest.Clone();

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(string)).ToBytes(requestId.ToString())
                    }
                });

                BinaryValue val = new ToByteMethod(typeof(OnTradeRequestOpenedEvent)).ToBytes(evn);
                BinaryValue val2 = new ToByteMethod(typeof(OnPlayerRequestOpenedEvent)).ToBytes(evp);
                BinaryValue val3 = new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(onTradeUpdatedEvent);

                EventResponse resp = new EventResponse()
                {
                    ListenerName = "MarketplaceRemoteEventListener",
                    EventName = "onTradeRequestOpened"
                };
                EventResponse resp2 = new EventResponse()
                {
                    ListenerName = "MarketplaceRemoteEventListener",
                    EventName = "onPlayerRequestOpened"
                };
                EventResponse resp3 = new EventResponse()
                {
                    ListenerName = "MarketplaceRemoteEventListener",
                    EventName = "onTradeUpdated"
                };
                
                resp.Params.Add(val);
                resp2.Params.Add(val2);
                resp3.Params.Add(val3);

                _user.SendResponce(new ResponseMessage { EventResponse = resp });
                _user.SendResponce(new ResponseMessage { EventResponse = resp2 });
                _user.SendResponce(new ResponseMessage { EventResponse = resp3 });

                // REAL-TIME UPDATE: Broadcast to all connected users
                Console.WriteLine($"[MarketplaceRemoteService] About to broadcast sale request for item {itemDefinitionId} by player {playerId}");
                BroadcastMarketplaceEvent(resp, playerId);
                BroadcastMarketplaceEvent(resp3, playerId);
                
                Console.WriteLine($"[MarketplaceRemoteService] Completed broadcasting sale request for item {itemDefinitionId} to all users");

                // AUTO-MATCHING: Try to match with existing purchase requests
                await TryMatchSaleWithPurchaseRequestsAndNotify(itemDefinitionId, args.Price, requestId.ToString(), playerId);
            }
            catch (System.Exception ex)
            {
                 Console.WriteLine($"[MarketplaceRemoteService] Error in createSaleRequest: {ex}");
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                        { 
                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                            Code = 500
                        }
                    }
                });
            }
        }

        protected async void createPurchaseRequestBySale(BinaryValue[] values, string guid, string methodName = null)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string buyerId))
                {
                    _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
                    return;
                }

                // SECURITY: Global market close (admin "Рынок" panel) blocks direct buy too.
                if (!BoltGameDatabaseProvider.Instance.IsMarketAccessibleToPlayer(buyerId))
                {
                    Console.WriteLine($"[MarketplaceRemoteService] REJECTED BuyBySale from {buyerId}: market is closed (на учёт).");
                    _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 403 } } });
                    return;
                }

                CreatePurchaseBySaleArgs args = (CreatePurchaseBySaleArgs)new FromByteMethod(typeof(CreatePurchaseBySaleArgs)).FromBytes(values[0]);
                string requestHash = $"purchase_{args.SaleId}_{buyerId}";
                if (MarketplaceRequestGuard.IsDuplicate(requestHash))
                {
                    Logger.LogWarn($"[SECURITY] Duplicate marketplace purchase blocked: sale={args.SaleId} buyer={buyerId}");
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                            {
                                Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                                Code = 409
                            }
                        }
                    });
                    return;
                }

                var BoltGame = BoltGameDatabaseProvider.Instance;
                var player = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(buyerId));

                // SECURITY: ATOMIC OPERATION - Consolidated logic in DB provider.
                var result = await BoltGame.CreatePurchaseRequestBySale(args, player);

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(string)).ToBytes(args.SaleId)
                    }
                });

                // Try to get seller info for Partner and buyer info for Creator
                string sellerId = result.SellerId;
                
                PlayerDocument sellerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(sellerId));
                Axlebolt.Bolt.Protobuf.Player sellerPlayer = FriendHelper.GetPlayer(sellerId);
                Axlebolt.Bolt.Protobuf.Player sellerpb = new Axlebolt.Bolt.Protobuf.Player
                {
                    Id = sellerPlayer.Id,
                    Uid = sellerPlayer.Uid,
                    Name = sellerPlayer.Name,
                    AvatarId = sellerPlayer.AvatarId,
                    TimeInGame = sellerDocument.timeInGame,
                    RegistrationDate = sellerDocument.createDate.MillisecondsSinceEpoch,
                    PlayerStatus = new Axlebolt.Bolt.Protobuf.PlayerStatus { PlayerId = sellerPlayer.Id }
                };

                PlayerDocument buyerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(buyerId));
                Axlebolt.Bolt.Protobuf.Player buyerPlayer = FriendHelper.GetPlayer(buyerId);
                Axlebolt.Bolt.Protobuf.Player buyerpb = new Axlebolt.Bolt.Protobuf.Player
                {
                    Id = buyerPlayer.Id,
                    Uid = buyerPlayer.Uid,
                    Name = buyerPlayer.Name,
                    AvatarId = buyerPlayer.AvatarId,
                    TimeInGame = buyerDocument.timeInGame,
                    RegistrationDate = buyerDocument.createDate.MillisecondsSinceEpoch,
                    PlayerStatus = new Axlebolt.Bolt.Protobuf.PlayerStatus { PlayerId = buyerPlayer.Id }
                };

                // Prepare and send events
                // CommissionPercent СѓР¶Рµ РІ РїСЂРѕС†РµРЅС‚Р°С… (0.17 = 17%), РЅРµ РЅСѓР¶РЅРѕ РґРµР»РёС‚СЊ РЅР° 100
                float sellerAmount = result.Price * (1f - BoltGame.GetMarketplaceSettingsAsync().Result.CommissionPercent);

                ClosedRequest sellerClosedRequest = new ClosedRequest
                {
                    Id = args.SaleId,
                    OriginId = args.SaleId,
                    Creator = sellerpb,
                    ItemDefinitionId = result.ItemDefinitionId,
                    Price = sellerAmount,
                    CreateDate = DateTime.UtcNow.Ticks,
                    CloseDate = DateTime.UtcNow.Ticks,
                    Type = Axlebolt.Bolt.Protobuf.MarketRequestType.SaleRequest,
                    Partner = buyerpb,
                    PartnerRequestId = "",
                    Reason = ClosingReason.SuccessTransaction,
                    Quantity = 1
                };

                ClosedRequest buyerClosedRequest = new ClosedRequest
                {
                    Id = args.SaleId,
                    OriginId = args.SaleId,
                    Creator = buyerpb,
                    ItemDefinitionId = result.ItemDefinitionId,
                    Price = result.Price,
                    CreateDate = DateTime.UtcNow.Ticks,
                    CloseDate = DateTime.UtcNow.Ticks,
                    Type = Axlebolt.Bolt.Protobuf.MarketRequestType.PurchaseRequest,
                    Partner = sellerpb,
                    PartnerRequestId = args.SaleId,
                    Reason = ClosingReason.SuccessTransaction,
                    Quantity = 1
                };

                Axlebolt.Bolt.Protobuf.InventoryItem purchasedItem = new Axlebolt.Bolt.Protobuf.InventoryItem();
                purchasedItem.Date = DateTime.UtcNow.Ticks;
                purchasedItem.ItemDefinitionId = result.ItemDefinitionId;
                purchasedItem.Quantity = 1;
                purchasedItem.Id = result.AssignedItemId; // The actual ID that was assigned to the item
                purchasedItem.Flags = 0;

                if (result.Properties != null && result.Properties.ElementCount > 0)
                {
                    foreach (var prop in result.Properties)
                    {
                        var itemProp = new Axlebolt.Bolt.Protobuf.InventoryItemProperty();
                        
                        if (prop.Value.IsInt32)
                        {
                            itemProp.Type = Axlebolt.Bolt.Protobuf.PropertyType.Int;
                            itemProp.IntValue = prop.Value.AsInt32;
                        }
                        else if (prop.Value.IsString)
                        {
                            itemProp.Type = Axlebolt.Bolt.Protobuf.PropertyType.String;
                            itemProp.StringValue = prop.Value.AsString;
                        }
                        else if (prop.Value.IsDouble)
                        {
                            itemProp.Type = Axlebolt.Bolt.Protobuf.PropertyType.Float;
                            itemProp.FloatValue = (float)prop.Value.AsDouble;
                        }
                        
                        purchasedItem.Properties.Add(prop.Name, itemProp);

                        // Bug 20: also mirror the item's properties onto the closed
                        // requests so the live purchase-history row shows StatTrak /
                        // stickers immediately (without waiting for a re-fetch). The
                        // persisted ClosedRequestDocument was already populated from
                        // the same source.
                        sellerClosedRequest.Properties.Add(prop.Name, itemProp.Clone());
                        buyerClosedRequest.Properties.Add(prop.Name, itemProp.Clone());
                    }
                }

                OnTradeRequestClosedEvent tradeClosedEvent = new OnTradeRequestClosedEvent { Request = sellerClosedRequest };
                OnPlayerRequestClosedEvent buyerEvent = new OnPlayerRequestClosedEvent { Request = buyerClosedRequest, Item = purchasedItem };
                OnTradeUpdatedEvent tradeUpdatedEvent = new OnTradeUpdatedEvent();

                var activeRequests = BoltGame.GetActiveMarketplaceRequestsByItem(result.ItemDefinitionId, 0, 100);
                var activePurchaseRequests = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = result.ItemDefinitionId });
                
                tradeUpdatedEvent.Trade = new Trade
                {
                    Id = result.ItemDefinitionId,
                    SalesCount = activeRequests.Count,
                    SalesPrice = activeRequests.Count > 0 ? activeRequests.Min(r => r.price) : 0,
                    PurchasesPrice = activePurchaseRequests.Length > 0 ? activePurchaseRequests.Max(r => r.Price) : 0,
                    PurchasesCount = activePurchaseRequests.Sum(r => r.Quantity)
                };

                _user.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeRequestClosed", Params = { new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(tradeClosedEvent) } } });
                _user.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onPlayerRequestClosed", Params = { new ToByteMethod(typeof(OnPlayerRequestClosedEvent)).ToBytes(buyerEvent) } } });
                _user.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeUpdated", Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdatedEvent) } } });

                // REAL-TIME UPDATE: Broadcast trade closed and updated events to all users
                EventResponse tradeClosedResp = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeRequestClosed", Params = { new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(tradeClosedEvent) } };
                EventResponse tradeUpdatedResp = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeUpdated", Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdatedEvent) } };
                BroadcastMarketplaceEvent(tradeClosedResp, buyerId);
                BroadcastMarketplaceEvent(tradeUpdatedResp, buyerId);

                // Try to send notification to seller
                Console.WriteLine($"[MarketplaceRemoteService] Attempting to notify seller {sellerId}");
                
                if (StaticClasses.UserServices.TryGetValue(sellerId, out UserService sellerUserService))
                {
                    Console.WriteLine($"[MarketplaceRemoteService] Seller {sellerId} is online, sending notifications");
                    
                    // Р˜РЎРџР РђР’Р›Р•РќР˜Р•: Р”РѕР±Р°РІР»СЏРµРј Item РІ СЃРѕР±С‹С‚РёРµ РїСЂРѕРґР°РІС†Р°, С‡С‚РѕР±С‹ СѓРІРµРґРѕРјР»РµРЅРёРµ РїРѕРєР°Р·С‹РІР°Р»РѕСЃСЊ
                    OnPlayerRequestClosedEvent sellerEvent = new OnPlayerRequestClosedEvent 
                    { 
                        Request = sellerClosedRequest,
                        Item = purchasedItem // Р”РѕР±Р°РІР»СЏРµРј РїСЂРѕРґР°РЅРЅС‹Р№ РїСЂРµРґРјРµС‚
                    };
                    
                    try
                    {
                        // РћС‚РїСЂР°РІР»СЏРµРј onPlayerRequestClosed РїСЂРѕРґР°РІС†Сѓ
                        sellerUserService.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onPlayerRequestClosed", Params = { new ToByteMethod(typeof(OnPlayerRequestClosedEvent)).ToBytes(sellerEvent) } } });
                        Console.WriteLine($"[MarketplaceRemoteService] вњ… Sent onPlayerRequestClosed to seller {sellerId}");
                        
                        // Р˜РЎРџР РђР’Р›Р•РќР˜Р•: РўР°РєР¶Рµ РѕС‚РїСЂР°РІР»СЏРµРј onTradeRequestClosed РїСЂРѕРґР°РІС†Сѓ, С‡С‚РѕР±С‹ РѕР±РЅРѕРІРёС‚СЊ UI Рё СѓР±СЂР°С‚СЊ РєРЅРѕРїРєСѓ РѕС‚РјРµРЅС‹
                        sellerUserService.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeRequestClosed", Params = { new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(tradeClosedEvent) } } });
                        Console.WriteLine($"[MarketplaceRemoteService] вњ… Sent onTradeRequestClosed to seller {sellerId}");
                    }
                    catch (System.Exception ex)
                    {
                        Console.WriteLine($"[MarketplaceRemoteService] вќЊ Failed to send notification to seller {sellerId}: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"[MarketplaceRemoteService] вљ пёЏ Seller {sellerId} is offline, cannot send real-time notification");
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in createPurchaseRequestBySale: {ex.Message}");
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                        { 
                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                            Code = ex.Message.Contains("not found") || ex.Message.Contains("already processed") ? 404 : 500
                        }
                    }
                });
            }
        }

        protected async void cancelRequest(BinaryValue[] values, string guid, string methodName = null)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
                    return;
                }

                CancelRequestArgs args = (CancelRequestArgs)new FromByteMethod(typeof(CancelRequestArgs)).FromBytes(values[0]);
                var BoltGame = BoltGameDatabaseProvider.Instance;
                var player = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(playerId));

                // SECURITY: ATOMIC OPERATION - Consolidated logic in DB provider.
                var result = await BoltGame.CancelRequest(args, player);
                if (result == null) throw new System.Exception("Request not found or already cancelled");

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(string)).ToBytes(args.Id)
                    }
                });

                // Prepare and send events
                PlayerDocument playerDocument = player;
                Axlebolt.Bolt.Protobuf.Player myPlayer = FriendHelper.GetPlayer(playerId);
                Axlebolt.Bolt.Protobuf.Player pbPlayer = new Axlebolt.Bolt.Protobuf.Player
                {
                    Id = myPlayer.Id,
                    Uid = myPlayer.Uid,
                    Name = myPlayer.Name,
                    AvatarId = myPlayer.AvatarId,
                    TimeInGame = playerDocument.timeInGame,
                    RegistrationDate = playerDocument.createDate.MillisecondsSinceEpoch,
                    PlayerStatus = new Axlebolt.Bolt.Protobuf.PlayerStatus { PlayerId = myPlayer.Id }
                };

                ClosedRequest closedRequest = new ClosedRequest
                {
                    Id = args.Id,
                    OriginId = args.Id,
                    Creator = pbPlayer,
                    ItemDefinitionId = result.ItemDefinitionId,
                    Price = result.Price,
                    CreateDate = DateTime.UtcNow.Ticks, // Simplified
                    CloseDate = DateTime.UtcNow.Ticks,
                    Type = (Axlebolt.Bolt.Protobuf.MarketRequestType)result.RequestType,
                    Partner = pbPlayer.Clone(),
                    PartnerRequestId = playerId,
                    Reason = ClosingReason.Cancelled,
                    Quantity = result.Quantity
                };

                Console.WriteLine($"[MarketplaceRemoteService] вњ… Request cancelled: ID={args.Id}, ItemDefId={result.ItemDefinitionId}, PlayerId={playerId}");

                OnTradeRequestClosedEvent tradeClosedEvent = new OnTradeRequestClosedEvent { Request = closedRequest };
                OnPlayerRequestClosedEvent playerClosedEvent = new OnPlayerRequestClosedEvent { Request = closedRequest.Clone() };
                if (result.ReturnedItem != null)
                {
                    playerClosedEvent.Item = new Axlebolt.Bolt.Protobuf.InventoryItem
                    {
                        Date = result.ReturnedItem.date.ToUniversalTime().Ticks,
                        ItemDefinitionId = result.ReturnedItem.itemDefinitionId,
                        Quantity = result.ReturnedItem.quantity,
                        Id = (int)result.ReturnedItem.id,
                        Flags = 0
                    };
                    
                    // Р˜РЎРџР РђР’Р›Р•РќР˜Р•: РљРѕРїРёСЂСѓРµРј СЃРІРѕР№СЃС‚РІР° (РЅР°РєР»РµР№РєРё, stattrack Рё С‚.Рґ.) РёР· РІРѕР·РІСЂР°С‰РµРЅРЅРѕРіРѕ РїСЂРµРґРјРµС‚Р°
                    if (result.ReturnedItem.Properties != null)
                    {
                        foreach (var prop in result.ReturnedItem.Properties)
                        {
                            playerClosedEvent.Item.Properties.Add(prop.Name, new Axlebolt.Bolt.Protobuf.InventoryItemProperty
                            {
                                Type = (Axlebolt.Bolt.Protobuf.PropertyType)prop.Type,
                                IntValue = prop.Type == BoltInventoryItemProperty.PropertyType.Int ? Convert.ToInt32(prop.Value) : 0,
                                FloatValue = prop.Type == BoltInventoryItemProperty.PropertyType.Float ? Convert.ToSingle(prop.Value) : 0,
                                BooleanValue = prop.Type == BoltInventoryItemProperty.PropertyType.Boolean ? Convert.ToBoolean(prop.Value) : false,
                                StringValue = prop.Type == BoltInventoryItemProperty.PropertyType.String ? prop.Value : ""
                            });
                        }
                    }
                }

                var activeRequests = BoltGame.GetActiveMarketplaceRequestsByItem(result.ItemDefinitionId, 0, 100);
                var activePurchaseRequests = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = result.ItemDefinitionId });
                
                Trade trade = new Trade
                {
                    Id = result.ItemDefinitionId,
                    SalesCount = activeRequests.Count,
                    SalesPrice = activeRequests.Count > 0 ? activeRequests.Min(r => r.price) : 0,
                    PurchasesPrice = activePurchaseRequests.Length > 0 ? activePurchaseRequests.Max(r => r.Price) : 0,
                    PurchasesCount = activePurchaseRequests.Sum(r => r.Quantity)
                };

                Console.WriteLine($"[MarketplaceRemoteService] Trade updated after cancel: ItemDefId={result.ItemDefinitionId}, SalesCount={trade.SalesCount}, PurchasesCount={trade.PurchasesCount}");

                OnTradeUpdatedEvent tradeUpdatedEvent = new OnTradeUpdatedEvent { Trade = trade };

                _user.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeRequestClosed", Params = { new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(tradeClosedEvent) } } });
                _user.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onPlayerRequestClosed", Params = { new ToByteMethod(typeof(OnPlayerRequestClosedEvent)).ToBytes(playerClosedEvent) } } });
                _user.SendResponce(new ResponseMessage { EventResponse = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeUpdated", Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdatedEvent) } } });

                // REAL-TIME UPDATE: Broadcast cancel events to all users
                Console.WriteLine($"[MarketplaceRemoteService] Broadcasting cancel event for request ID={args.Id} to all users except {playerId}");
                EventResponse tradeClosedResp = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeRequestClosed", Params = { new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(tradeClosedEvent) } };
                EventResponse tradeUpdatedResp = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeUpdated", Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdatedEvent) } };
                BroadcastMarketplaceEvent(tradeClosedResp, playerId);
                BroadcastMarketplaceEvent(tradeUpdatedResp, playerId);
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in cancelRequest: {ex.Message}");
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                        { 
                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                            Code = ex.Message.Contains("not found") ? 404 : 500
                        }
                    }
                });
            }
        }

        protected async void getTradeOpenSaleRequests(BinaryValue[] values, string guid, string methodName = null)
        {
            try
            {
                GetTradeOpenSaleRequestsArgs args = (GetTradeOpenSaleRequestsArgs)new FromByteMethod(typeof(GetTradeOpenSaleRequestsArgs)).FromBytes(values[0]);
                var BoltGame = BoltGameDatabaseProvider.Instance;
                OpenRequest[] validRequests = await BoltGame.GetTradeOpenSaleRequests(args);

                // SECURITY: Drop offers whose collection is hidden from the market.
                validRequests = validRequests.Where(r => !IsCollectionHidden(r.ItemDefinitionId)).ToArray();

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = (methodName != null && methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase)) ? new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(WrapRepeatedMessages(validRequests)) } : new ToByteMethod(typeof(OpenRequest[])).ToBytes(validRequests)
                    }
                });
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in getTradeOpenSaleRequests: {ex}");
                _user.SendResponce(new ResponseMessage 
                { 
                    RpcResponse = new RpcResponse 
                    { 
                        Id = guid, 
                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                        { 
                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                            Code = 500 
                        } 
                    } 
                });
            }
        }

        protected async void getPlayerClosedRequests(BinaryValue[] values, string guid, string methodName = null)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    throw new System.Exception("Player not found");
                }

                GetClosedRequestsArgs args = (GetClosedRequestsArgs)new FromByteMethod(typeof(GetClosedRequestsArgs)).FromBytes(values[0]);
                var playerDoc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(playerId));
                
                if (playerDoc == null)
                {
                    throw new System.Exception("Player document not found");
                }
                
                var closedRequests = await BoltGameDatabaseProvider.Instance.GetPlayerClosedRequests(args, playerDoc);
                
                if (closedRequests == null)
                {
                    closedRequests = new ClosedRequest[0];
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(ClosedRequest[])).ToBytes(closedRequests)
                    }
                });
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in getPlayerClosedRequests: {ex}");
                try
                {
                    if (_user != null)
                    {
                        _user.SendResponce(new ResponseMessage { RpcResponse = { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } });
                    }
                }
                catch (System.Exception sendEx)
                {
                    Console.WriteLine($"[MarketplaceRemoteService] Error sending error response: {sendEx}");
                }
            }
        }

        protected async void getPlayerClosedRequestsCount(BinaryValue[] values, string guid, string methodName = null)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    throw new System.Exception("Player not found");
                }

                GetClosedRequestsCountArgs args = (GetClosedRequestsCountArgs)new FromByteMethod(typeof(GetClosedRequestsCountArgs)).FromBytes(values[0]);
                var playerDoc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(playerId));

                int count = await BoltGameDatabaseProvider.Instance.GetPlayerClosedRequestsCountAsync(args, playerDoc);

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(int)).ToBytes(count)
                    }
                });
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in getPlayerClosedRequestsCount: {ex}");
                try
                {
                    if (_user != null)
                    {
                        _user.SendResponce(new ResponseMessage { RpcResponse = { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 500 } } });
                    }
                }
                catch (System.Exception sendEx)
                {
                    Console.WriteLine($"[MarketplaceRemoteService] Error sending error response: {sendEx}");
                }
            }
        }

        protected async void createSale(BinaryValue[] values, string guid, string methodName = null)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                CreateSaleRequestArgs request;
                if (values != null && values.Length > 0 && values[0].One != null)
                {
                    Console.WriteLine($"[MarketplaceRemoteService] createSale received bytes: {BitConverter.ToString(values[0].One.ToByteArray())}");
                    request = new CreateSaleRequestArgs();
                    request.MergeFrom(new Google.Protobuf.CodedInputStream(values[0].One.ToByteArray()));
                    Console.WriteLine($"[MarketplaceRemoteService] Parsed from bytes: ItemId={request.ItemId}, Price={request.Price}");
                }
                else
                {
                    Console.WriteLine($"[MarketplaceRemoteService] createSale values[0].One is null");
                    request = (CreateSaleRequestArgs)new FromByteMethod(typeof(CreateSaleRequestArgs)).FromBytes(values[0]);
                }
                
                var BoltGame = BoltGameDatabaseProvider.Instance;
                BsonDocument fullItemData = BoltGame.GetFullItemFromPlayerInventory(ObjectId.Parse(playerId), request.ItemId);
                if (fullItemData == null)
                {
                    SendError(guid, 404);
                    return;
                }

                int itemDefinitionId = fullItemData.GetValue("itemDefinitionId").AsInt32;

                if (!BoltGame.RemoveItemIfOwned(ObjectId.Parse(playerId), request.ItemId, itemDefinitionId))
                {
                    SendError(guid, 404);
                    return;
                }

                // SECURITY: Validate price range (0.01 - 1,000,000)
                if (float.IsNaN(request.Price) || float.IsInfinity(request.Price) || request.Price < 0.03f || request.Price > 1000000.0f)
                {
                    SendError(guid, 400);
                    return;
                }

                ObjectId id = BoltGame.CreateMarketplaceRequest(playerId, request.ItemId, itemDefinitionId, request.Price, fullItemData);

                CreateSaleResponse response = new CreateSaleResponse { RequestId = id.ToString() };

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = (methodName != null && methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase)) ? new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(WrapMessageResponse(response)) } : new ToByteMethod(typeof(CreateSaleResponse)).ToBytes(response)
                    }
                });

                // Trigger events
                TriggerSaleEvents(playerId, id, itemDefinitionId, request.Price, fullItemData);
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in createSale: {ex}");
                SendError(guid, 500);
            }
        }

        private async void TriggerSaleEvents(string playerId, ObjectId requestId, int itemDefinitionId, float price, BsonDocument fullItemData)
        {
            try
            {
                var BoltGame = BoltGameDatabaseProvider.Instance;
                PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(playerId));
                Axlebolt.Bolt.Protobuf.Player myPlayer = FriendHelper.GetPlayer(playerId);

                OnTradeRequestOpenedEvent evn = new OnTradeRequestOpenedEvent();
                OnPlayerRequestOpenedEvent evp = new OnPlayerRequestOpenedEvent();
                OnTradeUpdatedEvent onTradeUpdatedEvent = new OnTradeUpdatedEvent();

                var activeRequests = BoltGame.GetActiveMarketplaceRequestsByItem(itemDefinitionId, 0, 100);
                var activePurchaseRequests = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = itemDefinitionId });
                
                Trade trade = new Trade();
                trade.Id = itemDefinitionId;
                trade.SalesCount = activeRequests.Count;
                trade.SalesPrice = activeRequests.Count > 0 ? activeRequests.Min(r => r.price) : price;
                trade.PurchasesPrice = activePurchaseRequests.Length > 0 ? activePurchaseRequests.Max(r => r.Price) : 0;
                trade.PurchasesCount = activePurchaseRequests.Sum(r => r.Quantity);

                onTradeUpdatedEvent.Trade = trade;

                Axlebolt.Bolt.Protobuf.Player player = new Axlebolt.Bolt.Protobuf.Player();
                player.Id = myPlayer.Id;
                player.Uid = myPlayer.Uid;
                player.Name = myPlayer.Name;
                player.AvatarId = myPlayer.AvatarId;
                player.TimeInGame = playerDocument.timeInGame;
                player.RegistrationDate = playerDocument.createDate.MillisecondsSinceEpoch;
                Axlebolt.Bolt.Protobuf.PlayerStatus playerStatus = new Axlebolt.Bolt.Protobuf.PlayerStatus();
                playerStatus.PlayerId = myPlayer.Id;
                player.PlayerStatus = playerStatus;

                OpenRequest openRequest = new OpenRequest();
                openRequest.Id = requestId.ToString();
                openRequest.Creator = player;
                openRequest.ItemDefinitionId = itemDefinitionId;
                openRequest.Price = price;
                openRequest.CreateDate = DateTime.Now.Ticks;
                openRequest.Type = Axlebolt.Bolt.Protobuf.MarketRequestType.SaleRequest;
                openRequest.Quantity = 1;
                
                // STICKER FIX: Add item properties (stickers, stattrack, etc.) to OpenRequest
                if (fullItemData != null && (fullItemData.Contains("properties") || fullItemData.Contains("Properties")))
                {
                    BsonDocument propertiesDoc = null;
                    if (fullItemData.Contains("properties"))
                    {
                        propertiesDoc = fullItemData["properties"].AsBsonDocument;
                    }
                    else if (fullItemData.Contains("Properties"))
                    {
                        propertiesDoc = fullItemData["Properties"].AsBsonDocument;
                    }

                    if (propertiesDoc != null)
                    {
                        // Copy stickers
                        for (int j = 0; j < 5; j++)
                        {
                            if (propertiesDoc.TryGetValue($"sticker_{j}", out var stickerValue))
                            {
                                openRequest.Properties.Add($"sticker_{j}", new Axlebolt.Bolt.Protobuf.InventoryItemProperty
                                {
                                    Type = Axlebolt.Bolt.Protobuf.PropertyType.Int,
                                    IntValue = stickerValue.AsInt32
                                });
                            }
                        }
                        // Copy stattrack
                        if (propertiesDoc.TryGetValue("stattrack_value", out var stattrackValue))
                        {
                            openRequest.Properties.Add("stattrack_value", new Axlebolt.Bolt.Protobuf.InventoryItemProperty
                            {
                                Type = Axlebolt.Bolt.Protobuf.PropertyType.Int,
                                IntValue = stattrackValue.AsInt32
                            });
                        }
                    }
                }

                evn.Request = openRequest;
                evp.Request = openRequest.Clone();

                BinaryValue val = new ToByteMethod(typeof(OnTradeRequestOpenedEvent)).ToBytes(evn);
                BinaryValue val2 = new ToByteMethod(typeof(OnPlayerRequestOpenedEvent)).ToBytes(evp);
                BinaryValue val3 = new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(onTradeUpdatedEvent);

                EventResponse resp = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeRequestOpened" };
                EventResponse resp2 = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onPlayerRequestOpened" };
                EventResponse resp3 = new EventResponse { ListenerName = "MarketplaceRemoteEventListener", EventName = "onTradeUpdated" };
                
                resp.Params.Add(val);
                resp2.Params.Add(val2);
                resp3.Params.Add(val3);

                // Send to current user
                _user.SendResponce(new ResponseMessage { EventResponse = resp });
                _user.SendResponce(new ResponseMessage { EventResponse = resp2 });
                _user.SendResponce(new ResponseMessage { EventResponse = resp3 });

                // REAL-TIME UPDATE: Broadcast to all connected users
                BroadcastMarketplaceEvent(resp, playerId);
                BroadcastMarketplaceEvent(resp3, playerId);
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in TriggerSaleEvents: {ex}");
            }
        }

        private async Task TryMatchSaleWithPurchaseRequestsAndNotify(int itemDefinitionId, float salePrice, string saleId, string sellerId)
        {
            try
            {
                var BoltGame = BoltGameDatabaseProvider.Instance;
                var database = BoltGame.GetDatabase;
                var collection = database.GetCollection<BsonDocument>("marketplace_request");

                // Find purchase requests that match: same item, maxPrice >= salePrice, active
                var filter = Builders<BsonDocument>.Filter.Eq("itemDefinitionId", itemDefinitionId) &
                             Builders<BsonDocument>.Filter.Gte("price", salePrice) &
                             Builders<BsonDocument>.Filter.Eq("status", (int)MarketplaceRequestStatus.Active) &
                             Builders<BsonDocument>.Filter.Eq("type", (int)Axlebolt.Bolt.Protobuf.MarketRequestType.PurchaseRequest);

                var purchaseRequests = await collection.Find(filter)
                    .Sort(Builders<BsonDocument>.Sort.Descending("price")) // Highest price first
                    .Limit(1)
                    .ToListAsync();

                if (purchaseRequests.Count() > 0)
                {
                    var purchaseDoc = purchaseRequests[0];
                    string buyerId = purchaseDoc["playerId"].AsString;

                    Console.WriteLine($"[MarketplaceRemoteService] рџ”„ Auto-matching sale {saleId} with purchase request from buyer {buyerId}");

                    try
                    {
                        // Execute the purchase
                        var args = new CreatePurchaseBySaleArgs { SaleId = saleId };
                        var player = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(buyerId));

                        var result = await BoltGame.CreatePurchaseRequestBySale(args, player);
                        
                        // РљР Р˜РўР˜Р§РќРћ: РЈРјРµРЅСЊС€Р°РµРј РєРѕР»РёС‡РµСЃС‚РІРѕ РІ Р·Р°РїСЂРѕСЃРµ РЅР° РїРѕРєСѓРїРєСѓ РїРѕСЃР»Рµ СѓСЃРїРµС€РЅРѕР№ РїРѕРєСѓРїРєРё
                        ObjectId purchaseRequestId = purchaseDoc["_id"].AsObjectId;
                        bool decremented = await BoltGame.DecrementPurchaseRequestQuantity(purchaseRequestId);
                        Console.WriteLine($"[MarketplaceRemoteService] рџ”Ѕ Decremented purchase request {purchaseRequestId} quantity after auto-match: {decremented}");

                        // Get seller and buyer info
                        PlayerDocument sellerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(sellerId));
                        Axlebolt.Bolt.Protobuf.Player sellerPlayer = FriendHelper.GetPlayer(sellerId);
                        Axlebolt.Bolt.Protobuf.Player sellerpb = new Axlebolt.Bolt.Protobuf.Player
                        {
                            Id = sellerPlayer.Id,
                            Uid = sellerPlayer.Uid,
                            Name = sellerPlayer.Name,
                            AvatarId = sellerPlayer.AvatarId,
                            TimeInGame = sellerDocument.timeInGame,
                            RegistrationDate = sellerDocument.createDate.MillisecondsSinceEpoch,
                            PlayerStatus = new Axlebolt.Bolt.Protobuf.PlayerStatus { PlayerId = sellerPlayer.Id }
                        };

                        PlayerDocument buyerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(buyerId));
                        Axlebolt.Bolt.Protobuf.Player buyerPlayer = FriendHelper.GetPlayer(buyerId);
                        Axlebolt.Bolt.Protobuf.Player buyerpb = new Axlebolt.Bolt.Protobuf.Player
                        {
                            Id = buyerPlayer.Id,
                            Uid = buyerPlayer.Uid,
                            Name = buyerPlayer.Name,
                            AvatarId = buyerPlayer.AvatarId,
                            TimeInGame = buyerDocument.timeInGame,
                            RegistrationDate = buyerDocument.createDate.MillisecondsSinceEpoch,
                            PlayerStatus = new Axlebolt.Bolt.Protobuf.PlayerStatus { PlayerId = buyerPlayer.Id }
                        };

                        // Create purchased item
                        var purchasedItem = new Axlebolt.Bolt.Protobuf.InventoryItem
                        {
                            Date = DateTime.UtcNow.Ticks,
                            ItemDefinitionId = result.ItemDefinitionId,
                            Quantity = 1,
                            Id = result.AssignedItemId,
                            Flags = 0
                        };

                        // Copy properties
                        if (result.Properties != null && result.Properties.ElementCount > 0)
                        {
                            foreach (var prop in result.Properties)
                            {
                                var itemProp = new Axlebolt.Bolt.Protobuf.InventoryItemProperty();
                                if (prop.Value.IsInt32)
                                {
                                    itemProp.Type = Axlebolt.Bolt.Protobuf.PropertyType.Int;
                                    itemProp.IntValue = prop.Value.AsInt32;
                                }
                                else if (prop.Value.IsString)
                                {
                                    itemProp.Type = Axlebolt.Bolt.Protobuf.PropertyType.String;
                                    itemProp.StringValue = prop.Value.AsString;
                                }
                                else if (prop.Value.IsDouble)
                                {
                                    itemProp.Type = Axlebolt.Bolt.Protobuf.PropertyType.Float;
                                    itemProp.FloatValue = (float)prop.Value.AsDouble;
                                }
                                purchasedItem.Properties.Add(prop.Name, itemProp);
                            }
                        }

                        float sellerAmount = result.Price * (1f - BoltGame.GetMarketplaceSettingsAsync().Result.CommissionPercent);

                        // Create closed requests for both parties
                        ClosedRequest sellerClosedRequest = new ClosedRequest
                        {
                            Id = saleId,
                            OriginId = saleId,
                            Creator = sellerpb,
                            ItemDefinitionId = result.ItemDefinitionId,
                            Price = sellerAmount,
                            CreateDate = DateTime.UtcNow.Ticks,
                            CloseDate = DateTime.UtcNow.Ticks,
                            Type = Axlebolt.Bolt.Protobuf.MarketRequestType.SaleRequest,
                            Partner = buyerpb,
                            PartnerRequestId = purchaseDoc["_id"].ToString(),
                            Reason = ClosingReason.SuccessTransaction,
                            Quantity = 1
                        };

                        ClosedRequest buyerClosedRequest = new ClosedRequest
                        {
                            Id = purchaseDoc["_id"].ToString(),
                            OriginId = purchaseDoc["_id"].ToString(),
                            Creator = buyerpb,
                            ItemDefinitionId = result.ItemDefinitionId,
                            Price = result.Price,
                            CreateDate = DateTime.UtcNow.Ticks,
                            CloseDate = DateTime.UtcNow.Ticks,
                            Type = Axlebolt.Bolt.Protobuf.MarketRequestType.PurchaseRequest,
                            Partner = sellerpb,
                            PartnerRequestId = saleId,
                            Reason = ClosingReason.SuccessTransaction,
                            Quantity = 1
                        };

                        // Bug 20: mirror item properties onto the closed-request events
                        // so the live purchase/sale history rows show StatTrak / stickers.
                        foreach (var liveProp in purchasedItem.Properties)
                        {
                            sellerClosedRequest.Properties.Add(liveProp.Key, liveProp.Value.Clone());
                            buyerClosedRequest.Properties.Add(liveProp.Key, liveProp.Value.Clone());
                        }

                        // Send notification to SELLER (current user)
                        OnPlayerRequestClosedEvent sellerEvent = new OnPlayerRequestClosedEvent { Request = sellerClosedRequest, Item = purchasedItem };
                        _user.SendResponce(new ResponseMessage 
                        { 
                            EventResponse = new EventResponse 
                            { 
                                ListenerName = "MarketplaceRemoteEventListener", 
                                EventName = "onPlayerRequestClosed", 
                                Params = { new ToByteMethod(typeof(OnPlayerRequestClosedEvent)).ToBytes(sellerEvent) } 
                            } 
                        });
                        Console.WriteLine($"[MarketplaceRemoteService] вњ… Sent onPlayerRequestClosed to seller {sellerId}");

                        // Send notification to BUYER
                        if (StaticClasses.UserServices.TryGetValue(buyerId, out UserService buyerUserService))
                        {
                            OnPlayerRequestClosedEvent buyerEvent = new OnPlayerRequestClosedEvent { Request = buyerClosedRequest, Item = purchasedItem };
                            buyerUserService.SendResponce(new ResponseMessage 
                            { 
                                EventResponse = new EventResponse 
                                { 
                                    ListenerName = "MarketplaceRemoteEventListener", 
                                    EventName = "onPlayerRequestClosed", 
                                    Params = { new ToByteMethod(typeof(OnPlayerRequestClosedEvent)).ToBytes(buyerEvent) } 
                                } 
                            });
                            Console.WriteLine($"[MarketplaceRemoteService] вњ… Sent onPlayerRequestClosed to buyer {buyerId}");
                        }
                        else
                        {
                            Console.WriteLine($"[MarketplaceRemoteService] вљ пёЏ Buyer {buyerId} is offline, cannot send real-time notification");
                        }

                        // Broadcast onTradeRequestClosed to all users
                        OnTradeRequestClosedEvent tradeClosedEvent = new OnTradeRequestClosedEvent { Request = sellerClosedRequest };
                        EventResponse tradeClosedResp = new EventResponse 
                        { 
                            ListenerName = "MarketplaceRemoteEventListener", 
                            EventName = "onTradeRequestClosed", 
                            Params = { new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(tradeClosedEvent) } 
                        };
                        BroadcastMarketplaceEvent(tradeClosedResp, null);

                        // Broadcast onTradeUpdated
                        var activeRequests = BoltGame.GetActiveMarketplaceRequestsByItem(result.ItemDefinitionId, 0, 100);
                        var activePurchaseRequests = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = result.ItemDefinitionId });
                        
                        OnTradeUpdatedEvent tradeUpdatedEvent = new OnTradeUpdatedEvent
                        {
                            Trade = new Trade
                            {
                                Id = result.ItemDefinitionId,
                                SalesCount = activeRequests.Count,
                                SalesPrice = activeRequests.Count > 0 ? activeRequests.Min(r => r.price) : 0,
                                PurchasesCount = activePurchaseRequests.Sum(r => r.Quantity),
                                PurchasesPrice = activePurchaseRequests.Length > 0 ? activePurchaseRequests.Max(r => r.Price) : 0
                            }
                        };

                        EventResponse tradeUpdatedResp = new EventResponse 
                        { 
                            ListenerName = "MarketplaceRemoteEventListener", 
                            EventName = "onTradeUpdated", 
                            Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdatedEvent) } 
                        };
                        BroadcastMarketplaceEvent(tradeUpdatedResp, null);

                        // Р˜РЎРџР РђР’Р›Р•РќР˜Р•: РџСЂРѕРІРµСЂСЏРµРј, Р±С‹Р» Р»Рё Р·Р°РїСЂРѕСЃ РЅР° РїРѕРєСѓРїРєСѓ РїРѕР»РЅРѕСЃС‚СЊСЋ РІС‹РїРѕР»РЅРµРЅ (СѓРґР°Р»РµРЅ РёР· Р‘Р”)
                        var updatedPurchaseRequest = BoltGame.GetMarketplaceRequest(purchaseRequestId);
                        if (updatedPurchaseRequest == null)
                        {
                            // Р—Р°РїСЂРѕСЃ РїРѕР»РЅРѕСЃС‚СЊСЋ РІС‹РїРѕР»РЅРµРЅ - РѕС‚РїСЂР°РІР»СЏРµРј onTradeRequestClosed
                            var purchaseClosedRequest = new ClosedRequest
                            {
                                Id = purchaseRequestId.ToString(),
                                OriginId = purchaseRequestId.ToString(),
                                Creator = buyerpb,
                                ItemDefinitionId = result.ItemDefinitionId,
                                Price = result.Price,
                                CreateDate = DateTime.UtcNow.Ticks,
                                CloseDate = DateTime.UtcNow.Ticks,
                                Type = Axlebolt.Bolt.Protobuf.MarketRequestType.PurchaseRequest,
                                Partner = sellerpb,
                                PartnerRequestId = saleId,
                                Reason = ClosingReason.SuccessTransaction,
                                Quantity = 1
                            };
                            
                            Console.WriteLine($"[MarketplaceRemoteService] рџ”” Closing purchase request: ID={purchaseRequestId}, ItemDefId={result.ItemDefinitionId}, BuyerId={buyerId}, Price={result.Price}");
                            
                            OnTradeRequestClosedEvent purchaseClosedEvent = new OnTradeRequestClosedEvent { Request = purchaseClosedRequest };
                            EventResponse purchaseClosedResp = new EventResponse 
                            { 
                                ListenerName = "MarketplaceRemoteEventListener", 
                                EventName = "onTradeRequestClosed", 
                                Params = { new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(purchaseClosedEvent) } 
                            };
                            BroadcastMarketplaceEvent(purchaseClosedResp, null);
                            Console.WriteLine($"[MarketplaceRemoteService] вњ… Broadcasted onTradeRequestClosed for completed purchase request {purchaseRequestId} to ALL users");
                            
                            // РћС‚РїСЂР°РІР»СЏРµРј РѕР±РЅРѕРІР»РµРЅРЅС‹Р№ onTradeUpdated СЃ РЅРѕРІС‹Рј РєРѕР»РёС‡РµСЃС‚РІРѕРј Р·Р°РїСЂРѕСЃРѕРІ РЅР° РїРѕРєСѓРїРєСѓ
                            var finalActivePurchaseRequests = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = result.ItemDefinitionId });
                            OnTradeUpdatedEvent finalTradeUpdate = new OnTradeUpdatedEvent
                            {
                                Trade = new Trade
                                {
                                    Id = result.ItemDefinitionId,
                                    SalesCount = activeRequests.Count,
                                    SalesPrice = activeRequests.Count > 0 ? activeRequests.Min(r => r.price) : 0,
                                    PurchasesCount = finalActivePurchaseRequests.Sum(r => r.Quantity),
                                    PurchasesPrice = finalActivePurchaseRequests.Length > 0 ? finalActivePurchaseRequests.Max(r => r.Price) : 0
                                }
                            };
                            EventResponse finalTradeUpdateResp = new EventResponse 
                            { 
                                ListenerName = "MarketplaceRemoteEventListener", 
                                EventName = "onTradeUpdated", 
                                Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(finalTradeUpdate) } 
                            };
                            BroadcastMarketplaceEvent(finalTradeUpdateResp, null);
                        }
                        else
                        {
                            // Р—Р°РїСЂРѕСЃ С‡Р°СЃС‚РёС‡РЅРѕ РІС‹РїРѕР»РЅРµРЅ - РѕС‚РїСЂР°РІР»СЏРµРј РѕР±РЅРѕРІР»РµРЅРЅС‹Р№ onTradeUpdated
                            Console.WriteLine($"[MarketplaceRemoteService] вњ… Purchase request {purchaseRequestId} partially completed, remaining quantity: {updatedPurchaseRequest.quantity}");
                            
                            var activePurchaseRequestsAfterUpdate = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = result.ItemDefinitionId });
                            
                            OnTradeUpdatedEvent tradeUpdatedEventAfterPartial = new OnTradeUpdatedEvent
                            {
                                Trade = new Trade
                                {
                                    Id = result.ItemDefinitionId,
                                    SalesCount = activeRequests.Count,
                                    SalesPrice = activeRequests.Count > 0 ? activeRequests.Min(r => r.price) : 0,
                                    PurchasesCount = activePurchaseRequestsAfterUpdate.Sum(r => r.Quantity),
                                    PurchasesPrice = activePurchaseRequestsAfterUpdate.Length > 0 ? activePurchaseRequestsAfterUpdate.Max(r => r.Price) : 0
                                }
                            };

                            EventResponse tradeUpdatedRespAfterPartial = new EventResponse 
                            { 
                                ListenerName = "MarketplaceRemoteEventListener", 
                                EventName = "onTradeUpdated", 
                                Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdatedEventAfterPartial) } 
                            };
                            BroadcastMarketplaceEvent(tradeUpdatedRespAfterPartial, null);
                            Console.WriteLine($"[MarketplaceRemoteService] вњ… Broadcasted onTradeUpdated after partial purchase completion: PurchasesCount={activePurchaseRequestsAfterUpdate.Sum(r => r.Quantity)}");
                        }

                        Console.WriteLine($"[MarketplaceRemoteService] вњ… Auto-match completed: sale {saleId} matched with purchase from {buyerId}");
                    }
                    catch (System.Exception ex)
                    {
                        Console.WriteLine($"[MarketplaceRemoteService] вќЊ Failed to match sale {saleId} with purchase request: {ex.Message}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] вќЊ Error in TryMatchSaleWithPurchaseRequestsAndNotify: {ex.Message}");
            }
        }

        private void BroadcastMarketplaceEvent(EventResponse eventResponse, string excludePlayerId = null)
        {
            if (eventResponse == null)
            {
                return;
            }

            foreach (var kvp in StaticClasses.EventSenders)
            {
                string playerId = kvp.Key;
                if (!string.IsNullOrEmpty(excludePlayerId) && playerId == excludePlayerId)
                {
                    continue;
                }

                if (kvp.Value == null || !kvp.Value.Any(sender => sender is MarketplaceRemoteEventListener))
                {
                    continue;
                }

                if (!StaticClasses.UserServices.TryGetValue(playerId, out UserService userService))
                {
                    continue;
                }

                try
                {
                    EventResponse copiedEvent = new EventResponse
                    {
                        ListenerName = eventResponse.ListenerName,
                        EventName = eventResponse.EventName
                    };
                    copiedEvent.Params.Add(eventResponse.Params);
                    userService.SendResponce(new ResponseMessage { EventResponse = copiedEvent });
                }
                catch
                {
                }
            }
        }

        protected async void createPurchaseRequest(BinaryValue[] values, string guid, string methodName = null)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string buyerId))
                {
                    SendError(guid, 401);
                    return;
                }

                // SECURITY: Global market close (admin "Рынок" panel) blocks buying too.
                if (!BoltGameDatabaseProvider.Instance.IsMarketAccessibleToPlayer(buyerId))
                {
                    Console.WriteLine($"[MarketplaceRemoteService] REJECTED Purchase from {buyerId}: market is closed (на учёт).");
                    SendError(guid, 403);
                    return;
                }

                CreatePurchaseRequestArgs args;
                if (values != null && values.Length > 0 && values[0].One != null)
                {
                    args = new CreatePurchaseRequestArgs();
                    args.MergeFrom(new Google.Protobuf.CodedInputStream(values[0].One.ToByteArray()));
                }
                else
                {
                    args = (CreatePurchaseRequestArgs)new FromByteMethod(typeof(CreatePurchaseRequestArgs)).FromBytes(values[0]);
                }
                var BoltGame = BoltGameDatabaseProvider.Instance;

                // Validate price
                if (float.IsNaN(args.Price) || float.IsInfinity(args.Price) || args.Price < 0.03f || args.Price > 1000000.0f)
                {
                    Console.WriteLine($"[MarketplaceRemoteService] REJECTED Purchase Request from {buyerId}: Invalid price {args.Price}");
                    SendError(guid, 400);
                    return;
                }

                // Validate quantity
                if (args.Quantity < 1 || args.Quantity > 200)
                {
                    Console.WriteLine($"[MarketplaceRemoteService] REJECTED Purchase Request from {buyerId}: Invalid quantity {args.Quantity}");
                    SendError(guid, 400);
                    return;
                }

                // Create purchase request (will auto-match with existing sales)
                var requestId = await BoltGame.CreatePurchaseRequestAsync(buyerId, args.ItemDefinitionId, args.Price, args.Quantity);

                // Get the created request
                var createdRequest = BoltGame.GetMarketplaceRequest(requestId);
                
                if (createdRequest == null)
                {
                    SendError(guid, 500);
                    return;
                }

                // Convert to proto
                var purchaseRequest = await createdRequest.ToProto();

                // Send RPC response
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(string)).ToBytes(requestId.ToString())
                    }
                });

                // Always send "opened" event first (even if it will be immediately closed)
                var onRequestOpenedEvent = new OnPlayerRequestOpenedEvent { Request = purchaseRequest };
                _user.SendResponce(new ResponseMessage 
                { 
                    EventResponse = new EventResponse 
                    { 
                        ListenerName = "MarketplaceRemoteEventListener", 
                        EventName = "onPlayerRequestOpened", 
                        Params = { new ToByteMethod(typeof(OnPlayerRequestOpenedEvent)).ToBytes(onRequestOpenedEvent) } 
                    } 
                });
                Console.WriteLine($"[MarketplaceRemoteService] вњ… Sent onPlayerRequestOpened to buyer {buyerId} for request {requestId}");

                // Try to match with existing sales
                var matchedSaleIds = new List<string>();
                
                // AUTO-MATCHING: Find sales that match the purchase request criteria
                var activeSales = BoltGame.GetActiveMarketplaceRequestsByItem(args.ItemDefinitionId, 0, args.Quantity);
                foreach (var sale in activeSales)
                {
                    // Match if sale price <= purchase price (buyer willing to pay this much)
                    if (sale.price <= args.Price && matchedSaleIds.Count < args.Quantity)
                    {
                        matchedSaleIds.Add(sale._id.ToString());
                        Console.WriteLine($"[MarketplaceRemoteService] рџЋЇ Matched sale {sale._id} (price: {sale.price}) with purchase request {requestId} (max price: {args.Price})");
                    }
                }
                
                Console.WriteLine($"[MarketplaceRemoteService] Found {matchedSaleIds.Count} matching sales for purchase request {requestId}");
                
                // Process matched sales AFTER sending opened event
                if (matchedSaleIds.Count() > 0)
                {
                    Console.WriteLine($"[MarketplaceRemoteService] Purchase request {requestId} auto-matched with {matchedSaleIds.Count()} sales at creation time");
                    
                    var player = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(buyerId));
                    int purchasedCount = 0;
                    int remainingQuantity = args.Quantity;
                    
                    // Execute purchases for each matched sale WITH 150ms DELAY
                    foreach (var saleId in matchedSaleIds)
                    {
                        try
                        {
                            // 150ms Р·Р°РґРµСЂР¶РєР° РјРµР¶РґСѓ РїРѕРєСѓРїРєР°РјРё
                            if (purchasedCount > 0)
                            {
                                await Task.Delay(150);
                            }
                            
                            // Execute the purchase using the database method
                            var saleArgs = new CreatePurchaseBySaleArgs { SaleId = saleId };
                            var purchaseResult = await BoltGame.CreatePurchaseRequestBySale(saleArgs, player);
                            
                            // РљР Р˜РўР˜Р§РќРћ: РЈРјРµРЅСЊС€Р°РµРј РєРѕР»РёС‡РµСЃС‚РІРѕ РІ Р·Р°РїСЂРѕСЃРµ РЅР° РїРѕРєСѓРїРєСѓ РїРѕСЃР»Рµ СѓСЃРїРµС€РЅРѕР№ РїРѕРєСѓРїРєРё
                            bool decremented = await BoltGame.DecrementPurchaseRequestQuantity(requestId);
                            Console.WriteLine($"[MarketplaceRemoteService] рџ”Ѕ Decremented purchase request {requestId} quantity: {decremented}");
                            
                            purchasedCount++;
                            remainingQuantity--;
                            
                            Console.WriteLine($"[MarketplaceRemoteService] рџ›’ Purchased {purchasedCount}/{matchedSaleIds.Count} - Remaining in request: {remainingQuantity}");
                            
                            // Get seller info
                            string sellerId = purchaseResult.SellerId;
                            PlayerDocument sellerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(sellerId));
                            Axlebolt.Bolt.Protobuf.Player sellerPlayer = FriendHelper.GetPlayer(sellerId);
                            Axlebolt.Bolt.Protobuf.Player sellerpb = new Axlebolt.Bolt.Protobuf.Player
                            {
                                Id = sellerPlayer.Id,
                                Uid = sellerPlayer.Uid,
                                Name = sellerPlayer.Name,
                                AvatarId = sellerPlayer.AvatarId,
                                TimeInGame = sellerDocument.timeInGame,
                                RegistrationDate = sellerDocument.createDate.MillisecondsSinceEpoch,
                                PlayerStatus = new Axlebolt.Bolt.Protobuf.PlayerStatus { PlayerId = sellerPlayer.Id }
                            };
                            
                            // Send notification to buyer for THIS purchase
                            var buyerClosedRequest = new ClosedRequest
                            {
                                Id = requestId.ToString(), // Use the purchase request ID
                                OriginId = requestId.ToString(),
                                Creator = FriendHelper.GetPlayer(buyerId),
                                ItemDefinitionId = purchaseResult.ItemDefinitionId,
                                Price = purchaseResult.Price,
                                CreateDate = DateTime.UtcNow.Ticks,
                                CloseDate = DateTime.UtcNow.Ticks,
                                Type = Axlebolt.Bolt.Protobuf.MarketRequestType.PurchaseRequest,
                                Partner = sellerpb,
                                PartnerRequestId = saleId,
                                Reason = ClosingReason.SuccessTransaction,
                                Quantity = 1
                            };
                            
                            var purchasedItem = new Axlebolt.Bolt.Protobuf.InventoryItem
                            {
                                Date = DateTime.UtcNow.Ticks,
                                ItemDefinitionId = purchaseResult.ItemDefinitionId,
                                Quantity = 1,
                                Id = purchaseResult.AssignedItemId,
                                Flags = 0
                            };
                            
                            // Copy properties if available
                            if (purchaseResult.Properties != null && purchaseResult.Properties.ElementCount > 0)
                            {
                                foreach (var prop in purchaseResult.Properties)
                                {
                                    var itemProp = new Axlebolt.Bolt.Protobuf.InventoryItemProperty();
                                    if (prop.Value.IsInt32)
                                    {
                                        itemProp.Type = Axlebolt.Bolt.Protobuf.PropertyType.Int;
                                        itemProp.IntValue = prop.Value.AsInt32;
                                    }
                                    else if (prop.Value.IsString)
                                    {
                                        itemProp.Type = Axlebolt.Bolt.Protobuf.PropertyType.String;
                                        itemProp.StringValue = prop.Value.AsString;
                                    }
                                    else if (prop.Value.IsDouble)
                                    {
                                        itemProp.Type = Axlebolt.Bolt.Protobuf.PropertyType.Float;
                                        itemProp.FloatValue = (float)prop.Value.AsDouble;
                                    }
                                    purchasedItem.Properties.Add(prop.Name, itemProp);

                                    // Bug 20: mirror onto buyer's closed-request so the
                                    // live "purchase history" entry shows StatTrak / stickers.
                                    buyerClosedRequest.Properties.Add(prop.Name, itemProp.Clone());
                                }
                            }
                            
                            var buyerEvent = new OnPlayerRequestClosedEvent { Request = buyerClosedRequest, Item = purchasedItem };
                            _user.SendResponce(new ResponseMessage 
                            { 
                                EventResponse = new EventResponse 
                                { 
                                    ListenerName = "MarketplaceRemoteEventListener", 
                                    EventName = "onPlayerRequestClosed", 
                                    Params = { new ToByteMethod(typeof(OnPlayerRequestClosedEvent)).ToBytes(buyerEvent) } 
                                } 
                            });
                            
                            // Р˜РЎРџР РђР’Р›Р•РќР˜Р•: РћС‚РїСЂР°РІР»СЏРµРј СѓРІРµРґРѕРјР»РµРЅРёРµ РџР РћР”РђР’Р¦РЈ
                            if (StaticClasses.UserServices.TryGetValue(sellerId, out UserService sellerUserService))
                            {
                                float sellerAmount = purchaseResult.Price * (1f - BoltGame.GetMarketplaceSettingsAsync().Result.CommissionPercent);
                                
                                var sellerClosedRequest = new ClosedRequest
                                {
                                    Id = saleId,
                                    OriginId = saleId,
                                    Creator = sellerpb,
                                    ItemDefinitionId = purchaseResult.ItemDefinitionId,
                                    Price = sellerAmount,
                                    CreateDate = DateTime.UtcNow.Ticks,
                                    CloseDate = DateTime.UtcNow.Ticks,
                                    Type = Axlebolt.Bolt.Protobuf.MarketRequestType.SaleRequest,
                                    Partner = FriendHelper.GetPlayer(buyerId),
                                    PartnerRequestId = requestId.ToString(),
                                    Reason = ClosingReason.SuccessTransaction,
                                    Quantity = 1
                                };
                                
                                var sellerEvent = new OnPlayerRequestClosedEvent { Request = sellerClosedRequest, Item = purchasedItem };
                                sellerUserService.SendResponce(new ResponseMessage 
                                { 
                                    EventResponse = new EventResponse 
                                    { 
                                        ListenerName = "MarketplaceRemoteEventListener", 
                                        EventName = "onPlayerRequestClosed", 
                                        Params = { new ToByteMethod(typeof(OnPlayerRequestClosedEvent)).ToBytes(sellerEvent) } 
                                    } 
                                });
                                
                                // РљР Р˜РўР˜Р§РќРћ: РћС‚РїСЂР°РІР»СЏРµРј РїСЂРѕРґР°РІС†Сѓ РўРђРљР–Р• onTradeRequestClosed С‡С‚РѕР±С‹ СѓР±СЂР°С‚СЊ РёР· "РўРѕР»СЊРєРѕ РјРѕРё Р·Р°РїСЂРѕСЃС‹"
                                var sellerTradeClosedEvent = new OnTradeRequestClosedEvent { Request = sellerClosedRequest };
                                sellerUserService.SendResponce(new ResponseMessage 
                                { 
                                    EventResponse = new EventResponse 
                                    { 
                                        ListenerName = "MarketplaceRemoteEventListener", 
                                        EventName = "onTradeRequestClosed", 
                                        Params = { new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(sellerTradeClosedEvent) } 
                                    } 
                                });
                                
                                Console.WriteLine($"[MarketplaceRemoteService] вњ… Sent onPlayerRequestClosed AND onTradeRequestClosed to seller {sellerId} - Р·Р°РїСЂРѕСЃ РґРѕР»Р¶РµРЅ РёСЃС‡РµР·РЅСѓС‚СЊ");
                            }
                            else
                            {
                                Console.WriteLine($"[MarketplaceRemoteService] вљ пёЏ Seller {sellerId} is offline, cannot send real-time notification");
                            }
                            
                            // Р˜РЎРџР РђР’Р›Р•РќР˜Р•: РћС‚РїСЂР°РІР»СЏРµРј onTradeRequestClosed РІСЃРµРј РїРѕР»СЊР·РѕРІР°С‚РµР»СЏРј РґР»СЏ Р—РђРџР РћРЎРђ РќРђ РџР РћР”РђР–РЈ
                            var saleClosedRequest = new ClosedRequest
                            {
                                Id = saleId,
                                OriginId = saleId,
                                Creator = sellerpb,
                                ItemDefinitionId = purchaseResult.ItemDefinitionId,
                                Price = purchaseResult.Price,
                                CreateDate = DateTime.UtcNow.Ticks,
                                CloseDate = DateTime.UtcNow.Ticks,
                                Type = Axlebolt.Bolt.Protobuf.MarketRequestType.SaleRequest,
                                Partner = FriendHelper.GetPlayer(buyerId),
                                PartnerRequestId = requestId.ToString(),
                                Reason = ClosingReason.SuccessTransaction,
                                Quantity = 1
                            };
                            
                            OnTradeRequestClosedEvent saleClosedEvent = new OnTradeRequestClosedEvent { Request = saleClosedRequest };
                            EventResponse saleClosedResp = new EventResponse 
                            { 
                                ListenerName = "MarketplaceRemoteEventListener", 
                                EventName = "onTradeRequestClosed", 
                                Params = { new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(saleClosedEvent) } 
                            };
                            
                            // РћС‚РїСЂР°РІР»СЏРµРј РїРѕРєСѓРїР°С‚РµР»СЋ РЅР°РїСЂСЏРјСѓСЋ С‡С‚РѕР±С‹ СѓР±СЂР°С‚СЊ РїСЂРµРґР»РѕР¶РµРЅРёРµ РёР· СЃРїРёСЃРєР°
                            _user.SendResponce(new ResponseMessage { EventResponse = saleClosedResp });
                            
                            // РћС‚РїСЂР°РІР»СЏРµРј РІСЃРµРј РѕСЃС‚Р°Р»СЊРЅС‹Рј РїРѕР»СЊР·РѕРІР°С‚РµР»СЏРј
                            BroadcastMarketplaceEvent(saleClosedResp, buyerId);
                            
                            Console.WriteLine($"[MarketplaceRemoteService] рџ“ў Broadcasted sale closure for {saleId} to all users - РїСЂРµРґР»РѕР¶РµРЅРёРµ РґРѕР»Р¶РЅРѕ РёСЃС‡РµР·РЅСѓС‚СЊ");
                            
                            // РљР Р˜РўР˜Р§РќРћ: РћР‘РќРћР’Р›Р•РќР˜Р• РЎР§Р•РўР§Р˜РљРђ "X РџР Р•Р”Р›РћР–Р•РќР˜Р™" - РѕС‚РїСЂР°РІР»СЏРµРј onTradeUpdated РїРѕСЃР»Рµ РљРђР–Р”РћР™ РїРѕРєСѓРїРєРё
                            var activeSalesNow = BoltGame.GetActiveMarketplaceRequestsByItem(purchaseResult.ItemDefinitionId, 0, 100);
                            var activePurchasesNow = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = purchaseResult.ItemDefinitionId });
                            
                            var tradeUpdateEvent = new OnTradeUpdatedEvent
                            {
                                Trade = new Trade
                                {
                                    Id = purchaseResult.ItemDefinitionId,
                                    SalesCount = activeSalesNow.Count,
                                    SalesPrice = activeSalesNow.Count > 0 ? activeSalesNow.Min(r => r.price) : 0,
                                    PurchasesCount = activePurchasesNow.Sum(r => r.Quantity),
                                    PurchasesPrice = activePurchasesNow.Length > 0 ? activePurchasesNow.Max(r => r.Price) : 0
                                }
                            };
                            
                            EventResponse tradeUpdateResp = new EventResponse 
                            { 
                                ListenerName = "MarketplaceRemoteEventListener", 
                                EventName = "onTradeUpdated", 
                                Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdateEvent) } 
                            };
                            
                            // РћС‚РїСЂР°РІР»СЏРµРј РїРѕРєСѓРїР°С‚РµР»СЋ
                            _user.SendResponce(new ResponseMessage { EventResponse = tradeUpdateResp });
                            
                            // РћС‚РїСЂР°РІР»СЏРµРј РІСЃРµРј РѕСЃС‚Р°Р»СЊРЅС‹Рј
                            BroadcastMarketplaceEvent(tradeUpdateResp, buyerId);
                            
                            Console.WriteLine($"[MarketplaceRemoteService] рџ”„ UPDATED TRADE COUNTER: {activeSalesNow.Count} sales remaining (was {activeSalesNow.Count + 1})");
                            
                            // РћР‘РќРћР’Р›Р•РќР˜Р• РЎР§Р•РўР§Р˜РљРђ: Р•СЃР»Рё РµС‰Рµ РѕСЃС‚Р°Р»РёСЃСЊ РїСЂРµРґРјРµС‚С‹ РґР»СЏ РїРѕРєСѓРїРєРё, РѕС‚РїСЂР°РІР»СЏРµРј РѕР±РЅРѕРІР»РµРЅРЅС‹Р№ Р·Р°РїСЂРѕСЃ
                            if (remainingQuantity > 0)
                            {
                                var updatedRequestDoc = BoltGame.GetMarketplaceRequest(requestId);
                                if (updatedRequestDoc != null)
                                {
                                    var updatedPurchaseRequest = await updatedRequestDoc.ToProto();
                                    // РљРѕР»РёС‡РµСЃС‚РІРѕ Р±РµСЂРµС‚СЃСЏ РёР· Р±Р°Р·С‹ РґР°РЅРЅС‹С…, РЅРµ РЅСѓР¶РЅРѕ РїРµСЂРµРѕРїСЂРµРґРµР»СЏС‚СЊ
                                    
                                    var onPlayerRequestOpenedEvent = new OnPlayerRequestOpenedEvent { Request = updatedPurchaseRequest };
                                    _user.SendResponce(new ResponseMessage 
                                    { 
                                        EventResponse = new EventResponse 
                                        { 
                                            ListenerName = "MarketplaceRemoteEventListener", 
                                            EventName = "onPlayerRequestOpened", 
                                            Params = { new ToByteMethod(typeof(OnPlayerRequestOpenedEvent)).ToBytes(onPlayerRequestOpenedEvent) } 
                                        } 
                                    });
                                    Console.WriteLine($"[MarketplaceRemoteService] рџ“Љ Updated purchase request counter: {updatedRequestDoc.quantity} items remaining");
                                }
                            }
                            
                            Console.WriteLine($"[MarketplaceRemoteService] вњ… Auto-matched purchase completed: buyer={buyerId}, seller={purchaseResult.SellerId}, item={purchaseResult.ItemDefinitionId}, price={purchaseResult.Price}");
                        }
                        catch (System.Exception ex)
                        {
                            Console.WriteLine($"[MarketplaceRemoteService] Failed to process auto-matched purchase {saleId}: {ex.Message}");
                        }
                    }
                    
                    Console.WriteLine($"[MarketplaceRemoteService] вњ… Purchase request {requestId} processed {purchasedCount}/{matchedSaleIds.Count()} auto-matched sales");
                    
                    // Р˜РЎРџР РђР’Р›Р•РќР˜Р•: РџСЂРѕРІРµСЂСЏРµРј, Р±С‹Р» Р»Рё Р·Р°РїСЂРѕСЃ РЅР° РїРѕРєСѓРїРєСѓ РїРѕР»РЅРѕСЃС‚СЊСЋ РІС‹РїРѕР»РЅРµРЅ
                    var updatedRequest = BoltGame.GetMarketplaceRequest(requestId);
                    if (updatedRequest == null || (int)updatedRequest.status == (int)MarketplaceRequestStatus.Sold || remainingQuantity == 0)
                    {
                        // Р—Р°РїСЂРѕСЃ РїРѕР»РЅРѕСЃС‚СЊСЋ РІС‹РїРѕР»РЅРµРЅ - РѕС‚РїСЂР°РІР»СЏРµРј onTradeRequestClosed С‡С‚РѕР±С‹ СѓР±СЂР°С‚СЊ С‚Р°Р±Р»РёС‡РєСѓ
                        var purchaseClosedRequest = new ClosedRequest
                        {
                            Id = requestId.ToString(),
                            OriginId = requestId.ToString(),
                            Creator = FriendHelper.GetPlayer(buyerId),
                            ItemDefinitionId = args.ItemDefinitionId,
                            Price = args.Price,
                            CreateDate = DateTime.UtcNow.Ticks,
                            CloseDate = DateTime.UtcNow.Ticks,
                            Type = Axlebolt.Bolt.Protobuf.MarketRequestType.PurchaseRequest,
                            Partner = FriendHelper.GetPlayer(buyerId),
                            PartnerRequestId = "",
                            Reason = ClosingReason.SuccessTransaction,
                            Quantity = purchasedCount
                        };
                        
                        OnTradeRequestClosedEvent purchaseClosedEvent = new OnTradeRequestClosedEvent { Request = purchaseClosedRequest };
                        EventResponse purchaseClosedResp = new EventResponse 
                        { 
                            ListenerName = "MarketplaceRemoteEventListener", 
                            EventName = "onTradeRequestClosed", 
                            Params = { new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(purchaseClosedEvent) } 
                        };
                        // Bug 14: previously the purchase-closed event was first broadcast to
                        // every connected user (null exclusion) and then sent again directly
                        // to the buyer. That double send caused the buyer's purchase history
                        // panel to log "you bought from X" three times instead of once
                        // (sale-closed + 2x purchase-closed). Exclude the buyer from the
                        // broadcast so the direct send below is the only copy they receive.
                        BroadcastMarketplaceEvent(purchaseClosedResp, buyerId);

                        // РћС‚РїСЂР°РІР»СЏРµРј РїРѕРєСѓРїР°С‚РµР»СЋ С‚РѕР¶Рµ С‡С‚РѕР±С‹ СѓР±СЂР°С‚СЊ С‚Р°Р±Р»РёС‡РєСѓ "Р’Р°С€ Р·Р°РїСЂРѕСЃ РЅР° РїРѕРєСѓРїРєСѓ"
                        _user.SendResponce(new ResponseMessage 
                        { 
                            EventResponse = new EventResponse 
                            { 
                                ListenerName = "MarketplaceRemoteEventListener", 
                                EventName = "onTradeRequestClosed", 
                                Params = { new ToByteMethod(typeof(OnTradeRequestClosedEvent)).ToBytes(purchaseClosedEvent) } 
                            } 
                        });
                        
                        Console.WriteLine($"[MarketplaceRemoteService] вњ… Broadcasted onTradeRequestClosed for fully completed purchase request {requestId} - С‚Р°Р±Р»РёС‡РєР° РґРѕР»Р¶РЅР° РёСЃС‡РµР·РЅСѓС‚СЊ");
                        
                        // РљР Р˜РўР˜Р§РќРћ: РћС‚РїСЂР°РІР»СЏРµРј onTradeUpdated С‡С‚РѕР±С‹ РѕР±РЅРѕРІРёС‚СЊ СЃС‡РµС‚С‡РёРє "Р—Р°РїСЂРѕСЃРѕРІ РЅР° РїРѕРєСѓРїРєСѓ X С€С‚."
                        var finalActivePurchaseRequests = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = args.ItemDefinitionId });
                        var finalActiveSaleRequests = BoltGame.GetActiveMarketplaceRequestsByItem(args.ItemDefinitionId, 0, 100);
                        
                        OnTradeUpdatedEvent finalTradeUpdate = new OnTradeUpdatedEvent
                        {
                            Trade = new Trade
                            {
                                Id = args.ItemDefinitionId,
                                SalesCount = finalActiveSaleRequests.Count,
                                SalesPrice = finalActiveSaleRequests.Count > 0 ? finalActiveSaleRequests.Min(r => r.price) : 0,
                                PurchasesCount = finalActivePurchaseRequests.Sum(r => r.Quantity),
                                PurchasesPrice = finalActivePurchaseRequests.Length > 0 ? finalActivePurchaseRequests.Max(r => r.Price) : 0
                            }
                        };
                        
                        EventResponse finalTradeUpdateResp = new EventResponse 
                        { 
                            ListenerName = "MarketplaceRemoteEventListener", 
                            EventName = "onTradeUpdated", 
                            Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(finalTradeUpdate) } 
                        };
                        
                        // РћС‚РїСЂР°РІР»СЏРµРј РїРѕРєСѓРїР°С‚РµР»СЋ
                        _user.SendResponce(new ResponseMessage { EventResponse = finalTradeUpdateResp });
                        
                        // РћС‚РїСЂР°РІР»СЏРµРј РІСЃРµРј РѕСЃС‚Р°Р»СЊРЅС‹Рј
                        BroadcastMarketplaceEvent(finalTradeUpdateResp, buyerId);
                        
                        Console.WriteLine($"[MarketplaceRemoteService] рџ”„ FINAL UPDATE: Purchases count = {finalActivePurchaseRequests.Sum(r => r.Quantity)} - С‚Р°Р±Р»РёС‡РєР° 'Р—Р°РїСЂРѕСЃРѕРІ РЅР° РїРѕРєСѓРїРєСѓ' РґРѕР»Р¶РЅР° РѕР±РЅРѕРІРёС‚СЊСЃС‡");
                    }
                    else
                    {
                        // Р—Р°РїСЂРѕСЃ С‡Р°СЃС‚РёС‡РЅРѕ РІС‹РїРѕР»РЅРµРЅ - РѕС‚РїСЂР°РІР»СЏРµРј onTradeUpdated СЃ РѕР±РЅРѕРІР»РµРЅРЅС‹Рј РєРѕР»РёС‡РµСЃС‚РІРѕРј
                        var activePurchaseRequests = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = args.ItemDefinitionId });
                        var activeSaleRequests = BoltGame.GetActiveMarketplaceRequestsByItem(args.ItemDefinitionId, 0, 100);
                        
                        // Р˜РЎРџР РђР’Р›Р•РќР˜Р•: РћС‚РїСЂР°РІР»СЏРµРј onTradeRequestOpened РґР»СЏ С‡Р°СЃС‚РёС‡РЅРѕ РІС‹РїРѕР»РЅРµРЅРЅРѕРіРѕ Р·Р°РїСЂРѕСЃР° Р’РЎР•Р” (РІРєР»СЋС‡Р°СЏ РїРѕРєСѓРїР°С‚РµР»СЏ)
                        var updatedPurchaseRequest = await updatedRequest.ToProto();
                        
                        // РЎРЅР°С‡Р°Р»Р° РѕС‚РїСЂР°РІР»СЏРµРј РїРѕРєСѓРїР°С‚РµР»СЋ onPlayerRequestOpened СЃ РѕР±РЅРѕРІР»РµРЅРЅС‹Рј Р·Р°РїСЂРѕСЃРѕРј
                        var onPlayerRequestOpenedEvent = new OnPlayerRequestOpenedEvent { Request = updatedPurchaseRequest };
                        _user.SendResponce(new ResponseMessage 
                        { 
                            EventResponse = new EventResponse 
                            { 
                                ListenerName = "MarketplaceRemoteEventListener", 
                                EventName = "onPlayerRequestOpened", 
                                Params = { new ToByteMethod(typeof(OnPlayerRequestOpenedEvent)).ToBytes(onPlayerRequestOpenedEvent) } 
                            } 
                        });
                        Console.WriteLine($"[MarketplaceRemoteService] вњ… Sent updated onPlayerRequestOpened to buyer {buyerId} for partially completed request {requestId}");
                        
                        // Р—Р°С‚РµРј РѕС‚РїСЂР°РІР»СЏРµРј РІСЃРµРј (РєСЂРѕРјРµ РїРѕРєСѓРїР°С‚РµР»СЏ) onTradeRequestOpened
                        EventResponse broadcastOpenedEvent = new EventResponse 
                        { 
                            ListenerName = "MarketplaceRemoteEventListener", 
                            EventName = "onTradeRequestOpened", 
                            Params = { new ToByteMethod(typeof(OnTradeRequestOpenedEvent)).ToBytes(new OnTradeRequestOpenedEvent { Request = updatedPurchaseRequest }) } 
                        };
                        BroadcastMarketplaceEvent(broadcastOpenedEvent, buyerId);
                        Console.WriteLine($"[MarketplaceRemoteService] вњ… Broadcasted onTradeRequestOpened for partially completed purchase request {requestId}");
                        
                        OnTradeUpdatedEvent tradeUpdatedEvent = new OnTradeUpdatedEvent
                        {
                            Trade = new Trade
                            {
                                Id = args.ItemDefinitionId,
                                SalesCount = activeSaleRequests.Count,
                                SalesPrice = activeSaleRequests.Count > 0 ? activeSaleRequests.Min(r => r.price) : 0,
                                PurchasesCount = activePurchaseRequests.Sum(r => r.Quantity),
                                PurchasesPrice = activePurchaseRequests.Length > 0 ? activePurchaseRequests.Max(r => r.Price) : 0
                            }
                        };

                        EventResponse tradeUpdatedBroadcast = new EventResponse 
                        { 
                            ListenerName = "MarketplaceRemoteEventListener", 
                            EventName = "onTradeUpdated", 
                            Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdatedEvent) } 
                        };
                        BroadcastMarketplaceEvent(tradeUpdatedBroadcast, null);
                        Console.WriteLine($"[MarketplaceRemoteService] вњ… Broadcasted onTradeUpdated for partially completed purchase request {requestId}, remaining quantity: {updatedRequest.quantity}");
                    }
                }
                else
                {
                    // No matches - broadcast to all users
                    EventResponse broadcastEvent = new EventResponse 
                    { 
                        ListenerName = "MarketplaceRemoteEventListener", 
                        EventName = "onTradeRequestOpened", 
                        Params = { new ToByteMethod(typeof(OnTradeRequestOpenedEvent)).ToBytes(new OnTradeRequestOpenedEvent { Request = purchaseRequest }) } 
                    };
                    BroadcastMarketplaceEvent(broadcastEvent, buyerId);

                    // Send onTradeUpdated to update purchase counts
                    var activePurchaseRequests = await BoltGame.GetTradeOpenPurchaseRequests(new GetTradeOpenPurchaseRequestsArgs { Id = args.ItemDefinitionId });
                    var activeSaleRequests = BoltGame.GetActiveMarketplaceRequestsByItem(args.ItemDefinitionId, 0, 100);
                    
                    OnTradeUpdatedEvent tradeUpdatedEvent = new OnTradeUpdatedEvent
                    {
                        Trade = new Trade
                        {
                            Id = args.ItemDefinitionId,
                            SalesCount = activeSaleRequests.Count,
                            SalesPrice = activeSaleRequests.Count > 0 ? activeSaleRequests.Min(r => r.price) : 0,
                            PurchasesCount = activePurchaseRequests.Sum(r => r.Quantity),
                            PurchasesPrice = activePurchaseRequests.Length > 0 ? activePurchaseRequests.Max(r => r.Price) : 0
                        }
                    };

                    _user.SendResponce(new ResponseMessage 
                    { 
                        EventResponse = new EventResponse 
                        { 
                            ListenerName = "MarketplaceRemoteEventListener", 
                            EventName = "onTradeUpdated", 
                            Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdatedEvent) } 
                        } 
                    });

                    EventResponse tradeUpdatedBroadcast = new EventResponse 
                    { 
                        ListenerName = "MarketplaceRemoteEventListener", 
                        EventName = "onTradeUpdated", 
                        Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeUpdatedEvent) } 
                    };
                    BroadcastMarketplaceEvent(tradeUpdatedBroadcast, buyerId);

                    Console.WriteLine($"[MarketplaceRemoteService] вњ… Purchase request created: {requestId} for item {args.ItemDefinitionId} at max price {args.Price} x{args.Quantity}");
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in createPurchaseRequest: {ex.Message}");
                SendError(guid, ex.Message.Contains("Not enough funds") ? 402 : 500);
            }
        }

        protected async void getTradeOpenPurchaseRequests(BinaryValue[] values, string guid, string methodName = null)
        {
            try
            {
                GetTradeOpenPurchaseRequestsArgs args = (GetTradeOpenPurchaseRequestsArgs)new FromByteMethod(typeof(GetTradeOpenPurchaseRequestsArgs)).FromBytes(values[0]);
                var BoltGame = BoltGameDatabaseProvider.Instance;
                
                // Get purchase requests from database
                OpenRequest[] requests = await BoltGame.GetTradeOpenPurchaseRequests(args);
                
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = (methodName != null && methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase)) ? new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.CopyFrom(WrapRepeatedMessages(requests)) } : new ToByteMethod(typeof(OpenRequest[])).ToBytes(requests)
                    }
                });
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in getTradeOpenPurchaseRequests: {ex}");
                SendError(guid, 500);
            }
        }

        public override void Invoke(RpcRequest request)
        {
            if (string.IsNullOrEmpty(request.MethodName)) return;
            string method = request.MethodName.ToLower();

            if (method.StartsWith("gettrades")) getTrades(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("gettradeopensalerequests")) getTradeOpenSaleRequests(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("gettradeopenpurchaserequests")) getTradeOpenPurchaseRequests(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("gettrade")) getTrade(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("getplayerclosedrequestscount")) getPlayerClosedRequestsCount(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("getplayerclosedrequests")) getPlayerClosedRequests(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("createsalerequest")) createSaleRequest(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("createsale")) createSale(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("createpurchaserequestbysale")) createPurchaseRequestBySale(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("createpurchaserequest")) createPurchaseRequest(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("cancelrequest")) cancelRequest(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("getmarketplacesettings")) getMarketplaceSettings(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("getplayerprocessingrequest")) getPlayerProcessingRequests(request.Params.ToArray(), request.Id, request.MethodName);
            else if (method.StartsWith("getplayeropenrequests")) getPlayerOpenRequests(request.Params.ToArray(), request.Id, request.MethodName);
            else MethodNotFound(request);
        }

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in MarketplaceRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                    { 
                        Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                        Code = code
                    }
                }
            });
        }

        private void GetTrades2(BinaryValue[] values, string guid)
        {
            try
            {
                GetTradesArgs args = (GetTradesArgs)new FromByteMethod(typeof(GetTradesArgs)).FromBytes(values[0]);

                RepeatedField<int> items = args.ItemDefinitionIds;
                var BoltGame = BoltGameDatabaseProvider.Instance;
                var saleStats = BoltGame.GetMarketplaceStatsForItems(items);
                var purchaseStats = BoltGame.GetPurchaseStatsForItems(items);

                var response = new GetTrades2Response();
                for (int i = 0; i < items.Count; i++)
                {
                    var trade = new Trade();
                    trade.Id = items[i];

                    if (saleStats.TryGetValue(items[i], out var s))
                    {
                        trade.SalesCount = s.Count;
                        trade.SalesPrice = s.MinPrice;
                    }
                    else
                    {
                        trade.SalesCount = 0;
                        trade.SalesPrice = 0.0f;
                    }

                    if (purchaseStats.TryGetValue(items[i], out var p))
                    {
                        trade.PurchasesCount = p.Count;
                        trade.PurchasesPrice = p.MaxPrice;
                    }
                    else
                    {
                        trade.PurchasesCount = 0;
                        trade.PurchasesPrice = 0.0f;
                    }
                    response.Trades.Add(trade);
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(GetTrades2Response)).ToBytes(response)
                    }
                });
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[MarketplaceRemoteService] Error in GetTrades2: {ex}");
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception 
                        { 
                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), 
                            Code = 500
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Resolves the marketable collection a definition belongs to, using the
        /// inventory catalogue's properties.collection field. Returns an empty
        /// string when the item has no collection (e.g. weapons) or is unknown.
        /// </summary>
        private static string ResolveCollectionForItem(int itemDefinitionId)
        {
            try
            {
                var def = InventoryCatalogueLoader.Instance.GetByKey(itemDefinitionId);
                if (def == null || def.properties == null || !def.properties.TryGetValue("collection", out var value))
                {
                    return string.Empty;
                }
                return value?.AsString ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// True when the given item definition's collection is hidden from the market
        /// by the admin "Рынок" panel (SetCollectionHidden). Weapons/items without a
        /// collection are never blocked.
        /// </summary>
        private static bool IsCollectionHidden(int itemDefinitionId)
        {
            try
            {
                string collection = ResolveCollectionForItem(itemDefinitionId);
                if (string.IsNullOrEmpty(collection)) return false;
                return BoltGameDatabaseProvider.Instance.GetHiddenMarketCollections()
                    .Contains(collection, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Мгновенный пуш статуса рынка всем онлайн-игрокам (без перезахода).
        /// APK 0.17: onSettings / onSystemSettings есть на MarketplaceRemoteEventListener.
        /// </summary>
        public static void NotifyMarketAccessChangedToAll()
        {
            int pushed = 0;
            foreach (var kvp in StaticClasses.UserServices.ToArray())
            {
                string playerId = kvp.Key;
                UserService user = kvp.Value;
                if (string.IsNullOrEmpty(playerId) || user == null) continue;
                try
                {
                    var settings = BoltGameDatabaseProvider.Instance
                        .GetMarketplaceSettingsForPlayerAsync(playerId)
                        .GetAwaiter().GetResult();
                    if (settings == null) continue;

                    var settingsBytes = new ToByteMethod(typeof(MarketplaceSettings)).ToBytes(settings);

                    // Dump 0.17: только onTradeUpdated / request events — onSettings НЕТ.
                    try
                    {
                        var tradeEvt = new OnTradeUpdatedEvent();
                        user.SendResponce(new ResponseMessage
                        {
                            EventResponse = new EventResponse
                            {
                                ListenerName = "MarketplaceRemoteEventListener",
                                EventName = "onTradeUpdated",
                                Params = { new ToByteMethod(typeof(OnTradeUpdatedEvent)).ToBytes(tradeEvt) }
                            }
                        });
                    }
                    catch { }

                    // Клиент часто рефетчит settings после закрытия request.
                    try
                    {
                        user.SendResponce(new ResponseMessage
                        {
                            EventResponse = new EventResponse
                            {
                                ListenerName = "MarketplaceRemoteEventListener",
                                EventName = "onPlayerRequestClosed",
                                Params = { settingsBytes }
                            }
                        });
                    }
                    catch { }

                    try { GameSettingsRemoteService.InvalidateCache(); } catch { }

                    pushed++;
                    Logger.Log($"[MarketAdmin] pushed settings Enabled={settings.Enabled} → {playerId}");
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarn($"[MarketAdmin] push to {playerId} failed: {ex.Message}");
                }
            }
            Logger.Log($"[MarketAdmin] NotifyMarketAccessChangedToAll pushed={pushed}");
        }

        /// <summary>Явный вызов из админки после toggle — для логов.</summary>
        public static void ForcePushMarketStateNow()
        {
            NotifyMarketAccessChangedToAll();
        }
    }

    public class GetTrades2Response : IMessage
    {
        public Google.Protobuf.Collections.RepeatedField<Trade> Trades { get; set; } = new Google.Protobuf.Collections.RepeatedField<Trade>();

        public void WriteTo(CodedOutputStream output)
        {
            if (Trades != null)
            {
                foreach (var trade in Trades)
                {
                    if (trade != null)
                    {
                        output.WriteRawTag(10);
                        output.WriteMessage(trade);
                    }
                }
            }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (Trades != null)
            {
                foreach (var trade in Trades)
                {
                    if (trade != null)
                    {
                        size += 1 + CodedOutputStream.ComputeMessageSize(trade);
                    }
                }
            }
            return size;
        }

        public void MergeFrom(CodedInputStream input) { }
        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;
    }

    public class GetTrade2Response : IMessage
    {
        public Trade Trade { get; set; }

        public void WriteTo(CodedOutputStream output)
        {
            if (Trade != null)
            {
                output.WriteRawTag(10);
                output.WriteMessage(Trade);
            }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (Trade != null)
            {
                size += 1 + CodedOutputStream.ComputeMessageSize(Trade);
            }
            return size;
        }

        public void MergeFrom(CodedInputStream input) { }
        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;
    }

    public class GetTradeOpenSaleRequests2Response : IMessage
    {
        public Google.Protobuf.Collections.RepeatedField<OpenRequest> Requests { get; set; } = new Google.Protobuf.Collections.RepeatedField<OpenRequest>();

        public void WriteTo(CodedOutputStream output)
        {
            if (Requests != null)
            {
                foreach (var req in Requests)
                {
                    if (req != null)
                    {
                        output.WriteRawTag(10);
                        output.WriteMessage(req);
                    }
                }
            }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (Requests != null)
            {
                foreach (var req in Requests)
                {
                    if (req != null)
                    {
                        size += 1 + CodedOutputStream.ComputeMessageSize(req);
                    }
                }
            }
            return size;
        }

        public void MergeFrom(CodedInputStream input) { }
        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;
    }

    public class GetTradeOpenPurchaseRequests2Response : IMessage
    {
        public Google.Protobuf.Collections.RepeatedField<OpenRequest> Requests { get; set; } = new Google.Protobuf.Collections.RepeatedField<OpenRequest>();

        public void WriteTo(CodedOutputStream output)
        {
            if (Requests != null)
            {
                foreach (var req in Requests)
                {
                    if (req != null)
                    {
                        output.WriteRawTag(10);
                        output.WriteMessage(req);
                    }
                }
            }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (Requests != null)
            {
                foreach (var req in Requests)
                {
                    if (req != null)
                    {
                        size += 1 + CodedOutputStream.ComputeMessageSize(req);
                    }
                }
            }
            return size;
        }

        public void MergeFrom(CodedInputStream input) { }
        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;
    }
}

