using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.MongoDB.Game;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.MongoDB.Game;

namespace StandRiseServer.RpcServer
{
    public class EnumMapper<TP, TO>
    {
        private static readonly Dictionary<TP, TO> OriginalMap;

        private static readonly Dictionary<TO, TP> ProtoMap;

        static EnumMapper()
        {
            Type protoType = typeof(TP);
            Type originalType = typeof(TO);
            OriginalMap = Enum.GetValues(typeof(TO)).Cast<TO>().ToDictionary((TO tp) => (TP)Enum.Parse(protoType, tp.ToString()));
            ProtoMap = Enum.GetValues(typeof(TP)).Cast<TP>().ToDictionary((TP tp) => (TO)Enum.Parse(originalType, tp.ToString()));
        }

        public static TO ToOriginal(TP proto)
        {
            return OriginalMap[proto];
        }

        public static TP ToProto(TO original)
        {
            return ProtoMap[original];
        }

        public static TO[] ToOriginal(TP[] proto)
        {
            return proto.Select(ToOriginal).ToArray();
        }

        public static TP[] ToProto(TO[] original)
        {
            return original.Select(ToProto).ToArray();
        }
    }
    public class Token { public ObjectId playerId; public string gameCode; public string gameVersion; }
    
    [Serializable]
    public class PlayerStatus
    {
        public PlayInGame playInGame { get; set; } = null;
        public OnlineStatus onlineStatus { get; set; } = OnlineStatus.StateOffline;
        public static Axlebolt.Bolt.Protobuf.PlayerStatus GetByDocument(PlayerStatus bsonElements)
        {
            return new Axlebolt.Bolt.Protobuf.PlayerStatus { OnlineStatus = (Axlebolt.Bolt.Protobuf.OnlineStatus)bsonElements.onlineStatus, PlayInGame = bsonElements.playInGame != null ? PlayInGame.GetByDocument(bsonElements.playInGame) : null };
        }
        public enum OnlineStatus
        {
            StateOffline,
            StateOnline,
            StateBusy,
            StateAway,
            StateSnooze,
            StateLookingToTrade,
            StateLookingToPlay
        }
    }
    [Serializable]
    public class PlayInGame
    {
        public string gameCode { get; set; }
        public string gameVersion { get; set; }
        public string lobbyId { get; set; }
        public string lobbyName { get; set; }
        public PhotonGame photonGame { get; set; }
        public static Axlebolt.Bolt.Protobuf.PlayInGame GetByDocument(PlayInGame bsonElements)
        {
            return new Axlebolt.Bolt.Protobuf.PlayInGame { GameCode = bsonElements.gameCode, GameVersion = bsonElements.gameVersion, LobbyId = bsonElements.lobbyId, LobbyName = bsonElements.lobbyName ?? string.Empty, PhotonGame = bsonElements.photonGame != null ? PhotonGame.GetByDocument(bsonElements.photonGame) : null };
        }
    }
    [Serializable]
    public class PhotonGame
    {
        public string region { get; set; }
        public string roomId { get; set; }
        public string appVersion { get; set; }
        public Dictionary<string,string> customProperties { get; set; }
        public static Axlebolt.Bolt.Protobuf.PhotonGame GetByDocument(PhotonGame bsonElements)
        {
            var game = new Axlebolt.Bolt.Protobuf.PhotonGame { Region = bsonElements.region, RoomId = bsonElements.roomId, AppVersion = bsonElements.appVersion };
            if (bsonElements.customProperties != null)
            {
                game.CustomProperties.Add(bsonElements.customProperties);
            }
            return game;
        }
    }
        public static class StaticClasses
        {
            // Внешний IP сервера. Dns.GetHostEntry возвращает ВНУТРЕННИЙ IP VPS, который
            // недостижим с клиентских устройств: пинг региона падал -> "Ошибка 5005" на
            // этапе "Подключение" ещё до постановки в очередь поиска.
            private static string _publicIp = "84.21.173.237";
            public static string PublicIp
            {
                get
                {
                    if (LocalServerConfig.IsLoaded && !string.IsNullOrWhiteSpace(LocalServerConfig.Current.PublicIp))
                    {
                        return LocalServerConfig.Current.PublicIp.Trim();
                    }
                    if (_publicIp == "127.0.0.1")
                    {
                        try
                        {
                            using (var wc = new System.Net.WebClient())
                            {
                                _publicIp = "127.0.0.1";
                            }
                        }
                        catch
                        {
                            try
                            {
                                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                                foreach (var ip in host.AddressList)
                                {
                                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                    {
                                        _publicIp = ip.ToString();
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                _publicIp = "127.0.0.1";
                            }
                        }
                    }
                    return _publicIp;
                }
            }

            public static ConcurrentDictionary<string, BoltLobby> Lobbies = new ConcurrentDictionary<string, BoltLobby>();
            public static ConcurrentDictionary<string, List<IEventSender>> EventSenders = new ConcurrentDictionary<string, List<IEventSender>>();

            /// <summary>EventSenders / UserServices могут быть под разным регистром ObjectId.</summary>
            public static bool TryGetEventSenders(string playerId, out List<IEventSender> senders)
            {
                senders = null;
                if (string.IsNullOrWhiteSpace(playerId)) return false;
                foreach (KeyValuePair<string, List<IEventSender>> kvp in EventSenders)
                {
                    if (BoltLobby.IdEquals(kvp.Key, playerId))
                    {
                        senders = kvp.Value;
                        return true;
                    }
                }
                return EventSenders.TryGetValue(playerId, out senders);
            }

            public static bool TryGetPrimaryUserService(string playerId, out UserService service)
            {
                service = null;
                if (string.IsNullOrWhiteSpace(playerId)) return false;
                foreach (KeyValuePair<string, UserService> kvp in UserServices)
                {
                    if (BoltLobby.IdEquals(kvp.Key, playerId))
                    {
                        service = kvp.Value;
                        return true;
                    }
                }
                return UserServices.TryGetValue(playerId, out service);
            }

            /// <summary>Живой TCP для пушей/наград/onMatchFinished (не мёртвый search после матча).</summary>
            public static UserService ResolveEventDeliveryService(string playerId)
            {
                if (string.IsNullOrWhiteSpace(playerId)) return null;
                if (TryGetPrimaryUserService(playerId, out UserService mapped)
                    && mapped != null && mapped.IsSessionAlive())
                    return mapped;
                if (AllUserServices.TryGetValue(playerId, out List<UserService> list) && list != null)
                {
                    lock (list)
                    {
                        UserService alive = list.LastOrDefault(s => s != null && s.IsSessionAlive());
                        if (alive != null) return alive;
                    }
                }
                if (TryGetMatchmakingFlowChannel(playerId, out UserService flow)
                    && flow != null && flow.IsSessionAlive())
                    return flow;
                return null;
            }

            public static bool TryGetAllUserServices(string playerId, out List<UserService> services)
            {
                services = null;
                if (string.IsNullOrWhiteSpace(playerId)) return false;
                foreach (KeyValuePair<string, List<UserService>> kvp in AllUserServices)
                {
                    if (BoltLobby.IdEquals(kvp.Key, playerId))
                    {
                        services = kvp.Value;
                        return true;
                    }
                }
                return AllUserServices.TryGetValue(playerId, out services);
            }
            public static ConcurrentDictionary<string, Token> Tokens = new ConcurrentDictionary<string, Token>();
            public static ConcurrentDictionary<string, string> CiphertextToTokenMap = new ConcurrentDictionary<string, string>();
        public static ConcurrentDictionary<string, PlayerStatus> PlayersStatus = new ConcurrentDictionary<string, PlayerStatus>();
        public static ConcurrentDictionary<TcpClient, string> Users = new ConcurrentDictionary<TcpClient, string>();
        public static List<TcpClient> GameServerUsers = new List<TcpClient>();
        public static ConcurrentDictionary<string, UserService> UserServices = new ConcurrentDictionary<string, UserService>();
        public static ConcurrentDictionary<string, List<UserService>> AllUserServices = new ConcurrentDictionary<string, List<UserService>>();
        /// <summary>Канал, с которого игрок нажал start/confirm — сюда шлём Done/UI, не убивая main TCP.</summary>
        public static ConcurrentDictionary<string, UserService> MatchmakingFlowChannels = new ConcurrentDictionary<string, UserService>();
        public const string DefaultGameVersion = "0.17.0";

        public static string GetPlayerGameVersion(string playerId)
        {
            if (!string.IsNullOrWhiteSpace(playerId) &&
                PlayersStatus.TryGetValue(playerId, out PlayerStatus status) &&
                !string.IsNullOrWhiteSpace(status?.playInGame?.gameVersion))
            {
                string v = status.playInGame.gameVersion.Trim();
                if (v.StartsWith("0.20", StringComparison.Ordinal) || v.StartsWith("0.22", StringComparison.Ordinal))
                    return DefaultGameVersion;
                return v;
            }

            return DefaultGameVersion;
        }
            
            public static void RegisterUserService(string playerId, UserService service)
            {
                var list = AllUserServices.GetOrAdd(playerId, _ => new List<UserService>());
                lock (list)
                {
                    if (!list.Contains(service))
                        list.Add(service);
                }
            }

            /// <summary>
            /// Один игрок — один «главный» TCP. Лишние сокеты дают «Отошёл», confirm на другом канале.
            /// </summary>
            public static void DisconnectOtherPlayerSessions(string playerId, UserService keep)
            {
                if (string.IsNullOrWhiteSpace(playerId) || keep == null) return;
                if (!AllUserServices.TryGetValue(playerId, out List<UserService> list) || list == null) return;
                List<UserService> snapshot;
                lock (list) { snapshot = list.ToList(); }
                foreach (UserService svc in snapshot)
                {
                    if (svc == null || ReferenceEquals(svc, keep)) continue;
                    try
                    {
                        Logger.Log($"[Session] Closing stale/extra TCP for {playerId} keep={keep.RemoteAddress} drop={svc.RemoteAddress}");
                        svc.ForceDisconnect();
                    }
                    catch { }
                }
            }

            public static void UnregisterUserService(string playerId, UserService service)
            {
                if (AllUserServices.TryGetValue(playerId, out var list))
                {
                    lock (list)
                    {
                        list.Remove(service);
                    }
                }
            }

            /// <summary>Есть ли живой TCP для matchmaking flow-событий.</summary>
            public static bool HasLiveFlowChannel(string playerId)
            {
                if (string.IsNullOrWhiteSpace(playerId)) return false;
                if (AllUserServices.TryGetValue(playerId, out var list))
                {
                    lock (list)
                    {
                        if (list.Any(s => s != null && s.IsSessionAlive())) return true;
                    }
                }
                return UserServices.TryGetValue(playerId, out var primary) && primary != null && primary.IsSessionAlive();
            }

            /// <summary>
            /// Primary TCP умер (Allies confirm) — поднять живой secondary в primary,
            /// чтобы Done/Confirm снова доходили.
            /// </summary>
            public static bool PromoteLiveFlowSession(string playerId)
            {
                if (string.IsNullOrWhiteSpace(playerId)) return false;
                if (UserServices.TryGetValue(playerId, out var currentPrimary)
                    && currentPrimary != null && currentPrimary.IsSessionAlive())
                    return true;
                if (TryGetMatchmakingFlowChannel(playerId, out var flowChannel)
                    && flowChannel != null && flowChannel.IsSessionAlive())
                {
                    try
                    {
                        flowChannel.InitEventSenders(playerId);
                        Logger.Log($"[Session] Promoted matchmaking flow channel for {playerId}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarn($"[Session] PromoteLiveFlowSession (flow) failed for {playerId}: {ex.Message}");
                    }
                }
                UserService live = null;
                if (AllUserServices.TryGetValue(playerId, out var list))
                {
                    lock (list)
                    {
                        live = list.LastOrDefault(s => s != null && s.IsSessionAlive());
                    }
                }
                if (live == null) return false;
                if (UserServices.TryGetValue(playerId, out var cur) && ReferenceEquals(cur, live) && cur.IsSessionAlive())
                    return true;
                try
                {
                    live.InitEventSenders(playerId);
                    Logger.Log($"[Session] Promoted live flow session for {playerId}");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.LogWarn($"[Session] PromoteLiveFlowSession failed for {playerId}: {ex.Message}");
                    return false;
                }
            }

            /// <summary>
            /// Клиент после onMatchmakingDone открывает второй TCP и рвёт primary.
            /// Поднимаем новый канал в primary, чтобы Confirm UI и ConfirmMatch доходили.
            /// </summary>
            /// <summary>
            /// НЕ убивает main TCP — только запоминает канал для matchmaking-событий.
            /// PromotePendingMatchPrimary рвёт лобби-сессию → серый статус / нет UI.
            /// </summary>
            public static void SetMatchmakingFlowChannel(string playerId, UserService channel)
            {
                if (string.IsNullOrWhiteSpace(playerId) || channel == null) return;
                MatchmakingFlowChannels[playerId] = channel;
                Logger.Log($"[Session] Matchmaking flow channel set for {playerId}");
            }

            public static void ClearMatchmakingFlowChannel(string playerId)
            {
                if (string.IsNullOrWhiteSpace(playerId)) return;
                MatchmakingFlowChannels.TryRemove(playerId, out _);
            }

            public static bool TryGetMatchmakingFlowChannel(string playerId, out UserService channel)
            {
                channel = null;
                if (string.IsNullOrWhiteSpace(playerId)) return false;
                if (MatchmakingFlowChannels.TryGetValue(playerId, out channel)
                    && channel != null && channel.IsSessionAlive())
                    return true;
                channel = null;
                return false;
            }

            /// <summary>
            /// Куда слать progress/done/confirm.
            /// preferSearchChannel=true: search-TCP (start), иначе PRIMARY — единый канал для UI.
            /// </summary>
            public static UserService ResolveMatchmakingEventChannel(string playerId, bool preferSearchChannel = false)
            {
                if (string.IsNullOrWhiteSpace(playerId)) return null;
                if (preferSearchChannel
                    && TryGetMatchmakingFlowChannel(playerId, out var search)
                    && search?.IsSessionAlive() == true)
                    return search;
                if (UserServices.TryGetValue(playerId, out var primary) && primary?.IsSessionAlive() == true)
                    return primary;
                if (TryGetMatchmakingFlowChannel(playerId, out var flow) && flow?.IsSessionAlive() == true)
                    return flow;
                if (AllUserServices.TryGetValue(playerId, out var list))
                {
                    lock (list)
                    {
                        return list.FirstOrDefault(s => s != null && s.IsSessionAlive());
                    }
                }
                return null;
            }

            public static void PromoteMatchmakingPrimary(UserService incoming, string playerId)
            {
                SetMatchmakingFlowChannel(playerId, incoming);
            }

            public static void PromotePendingMatchPrimary(UserService incoming, string playerId)
            {
                if (incoming == null || string.IsNullOrWhiteSpace(playerId)) return;

                if (UserServices.TryGetValue(playerId, out var old) && old != null && !ReferenceEquals(old, incoming))
                {
                    Security.PlayerSessionRegistry.Release(playerId, old);
                    UnregisterUserService(playerId, old);
                    EventSenders.TryRemove(playerId, out _);
                    UserServices.TryRemove(playerId, out _);
                    try
                    {
                        if (old.TcpClient != null)
                            Users.TryRemove(old.TcpClient, out _);
                    }
                    catch { }
                }

                Users[incoming.TcpClient] = playerId;
                RegisterUserService(playerId, incoming);
                incoming.InitEventSenders(playerId);
                DisconnectOtherPlayerSessions(playerId, incoming);
                UserService.CancelPendingDisconnect(playerId);
                Security.PlayerSessionRegistry.TryAcquire(playerId, incoming);
                Logger.Log($"[Session] Promoted pending-match primary for {playerId}");
            }

            // Marketplace subscriptions
            public static ConcurrentDictionary<string, int> Subscribes = new ConcurrentDictionary<string, int>();
            public static ConcurrentDictionary<string, byte> SubscribesTrades = new ConcurrentDictionary<string, byte>();
            
            // Global chat subscriptions
            public static ConcurrentDictionary<string, int> SubscribesGlobalChat = new ConcurrentDictionary<string, int>();
            
            // Marketplace bans and rate limits
            public static ConcurrentDictionary<string, DateTime> MarketplaceBans = new ConcurrentDictionary<string, DateTime>();
            public static ConcurrentDictionary<string, (DateTime timestamp, int count)> MarketplaceRateLimits = new ConcurrentDictionary<string, (DateTime timestamp, int count)>();

            public static void KickWithBan(string playerId, string banReason, int banCode, string uid)
            {
                if (UserServices.TryGetValue(playerId, out var userService))
                {
                    try
                    {
                        var banMsg = new Axlebolt.RpcSupport.Protobuf.ResponseMessage
                        {
                            RpcResponse = new Axlebolt.RpcSupport.Protobuf.RpcResponse
                            {
                                Id = string.Empty,
                                Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                                {
                                    Id = BitConverter.ToInt64(System.Guid.NewGuid().ToByteArray(), 8),
                                    Code = 9999
                                }
                            }
                        };
                        banMsg.RpcResponse.Exception.Property.Add("uid", uid);
                        banMsg.RpcResponse.Exception.Property.Add("banCode", banCode.ToString());
                        banMsg.RpcResponse.Exception.Property.Add("reason", banReason);
                        userService.SendResponce(banMsg);
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(3000).ConfigureAwait(false);
                            userService.ForceDisconnect();
                        });
                    }
                    catch { }
                }
            }
        }
    }
