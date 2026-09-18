using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer.Api;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StandRiseServer.MongoDB.Game;

namespace StandRiseServer.RpcServer
{
    public class UserService
    {
        private static int NextSessionId = 0;
        private readonly int _sessionId;
        private readonly object StateLock = new object();

        private readonly Stopwatch _test = new Stopwatch();

        private bool clientConnected = false;

        private Dictionary<string, RpcClass> GetClass = new Dictionary<string, RpcClass>(StringComparer.OrdinalIgnoreCase);

        // Grace period: players stay in matchmaking queue for 30s after disconnect
        private static readonly ConcurrentDictionary<string, Timer> PendingDisconnects = new ConcurrentDictionary<string, Timer>();
        private const int ReconnectGraceSeconds = 30;

        public static void CancelPendingDisconnect(string playerId)
        {
            if (PendingDisconnects.TryRemove(playerId, out var timer))
            {
                timer.Dispose();
                Logger.Log($"[Matchmaking] Grace period cancelled for player {playerId} (reconnected)");
            }
        }

        public static void ForceRemoveFromQueue(string playerId)
        {
            if (PendingDisconnects.TryRemove(playerId, out var timer))
                timer.Dispose();

            BoltMainDatabaseProvider.Instance.SetPlayerStatus(ObjectId.Parse(playerId), new PlayerStatus { onlineStatus = PlayerStatus.OnlineStatus.StateOffline, playInGame = null });
            MatchmakingManager.RemoveFromQueue(playerId);
        }

        public static void ScheduleGracefulDisconnect(string playerId)
        {
            CancelPendingDisconnect(playerId);
            var timer = new Timer(_ =>
            {
                try
                {
                    // Если игрок уже снова онлайн (EventSenders есть) — это реконнект Allies/secondary,
                    // очередь и pending confirm НЕ трогаем.
                    if (StaticClasses.EventSenders.ContainsKey(playerId))
                    {
                        CancelPendingDisconnect(playerId);
                        Logger.Log($"[Matchmaking] Grace expired but player {playerId} is live — keep queue/pending");
                        return;
                    }
                    ForceRemoveFromQueue(playerId);
                    Logger.Log($"[Matchmaking] Player {playerId} removed from queue after {ReconnectGraceSeconds}s grace period");
                }
                catch (System.Exception cleanupEx)
                {
                    Logger.Error($"[Matchmaking] Grace period cleanup error: {cleanupEx}");
                }
            }, null, ReconnectGraceSeconds * 1000, System.Threading.Timeout.Infinite);
            PendingDisconnects[playerId] = timer;
        }
        
        public int Timeout { get; set; } = 11000;
        public UserService(TcpClient client)
        {
            if (client == null)
            {
                throw new NullReferenceException("tcpClient");
            }
            _sessionId = Interlocked.Increment(ref NextSessionId);
            this.TcpClient = client;
            this._requestes = new ConcurrentQueue<RpcRequest>();
            _reponses = new ConcurrentQueue<ResponseMessage>();
            clientConnected = true;
            Init();
            RpcTrafficLog.ClientConnected(this);
        }

        public int SessionId
        {
            get { return _sessionId; }
        }

        public string RemoteAddress
        {
            get
            {
                try
                {
                    return TcpClient != null && TcpClient.Client != null && TcpClient.Client.RemoteEndPoint != null ? TcpClient.Client.RemoteEndPoint.ToString() : "unknown";
                }
                catch
                {
                    return "unknown";
                }
            }
        }

        public string CurrentPlayerId
        {
            get
            {
                if (TcpClient != null && StaticClasses.Users.TryGetValue(TcpClient, out string playerId))
                {
                    return playerId;
                }
                return "none";
            }
        }

        private void Init()
        {
            GetClass.Add("HelloRemoteService", new HelloRemoteService(this));
            GetClass.Add("HandshakeRemoteService", new HandshakeRemoteService(this));
            GetClass.Add("GoogleAuthRemoteService", new GoogleAuthRemoteService(this));
            GetClass.Add("PlayerRemoteService", new PlayerRemoteService(this));
            GetClass.Add("GameSettingsRemoteService", new GameSettingsRemoteService(this));
            GetClass.Add("PlayerStatsRemoteService", new PlayerStatsRemoteService(this));
            GetClass.Add("StorageRemoteService", new StorageRemoteService(this));
            GetClass.Add("InventoryRemoteService", new InventoryRemoteService(this));
            GetClass.Add("MatchmakingRemoteService", new MatchmakingRemoteService(this));
            GetClass.Add("FriendsRemoteService", new FriendsRemoteService(this));
            GetClass.Add("AvatarRemoteService", new AvatarRemoteService(this));
            GetClass.Add("TestAuthRemoteService", new TestAuthRemoteService(this));
            GetClass.Add("GameServerStatsRemoteService", new GameServerStatsRemoteService(this));
            GetClass.Add("GameServerRemoteService", new GameServerRemoteService(this));
            GetClass.Add("GameServerPlayerRemoteService", new GameServerPlayerRemoteService(this));
            GetClass.Add("GameEventRemoteService", new GameEventRemoteService(this));
            GetClass.Add("ChatRemoteService", new ChatRemoteService(this));
            GetClass.Add("MarketplaceRemoteService", new MarketplaceRemoteService(this));
            GetClass.Add("ClanRemoteService", new ClanRemoteService(this));
            GetClass.Add("BoltRemoteService", new BoltRemoteService(this));
            GetClass.Add("GlobalChatRemoteService", new GlobalChatRemoteService(this));
            GetClass.Add("ClanMessagesRemoteService", new ClanMessagesRemoteService(this));
            GetClass.Add("AnalyticsRemoteService", new AnalyticsRemoteService(this));
            GetClass.Add("OffersRemoteService", new OffersRemoteService(this));
            GetClass.Add("RateGameRemoteService", new RateGameRemoteService(this));
            GetClass.Add("MatchesRemoteService", new MatchesRemoteService(this));
            GetClass.Add("ClanStatsRemoteService", new ClanStatsRemoteService(this));
            GetClass.Add("ClanMemberStatsRemoteService", new ClanMemberStatsRemoteService(this));
            GetClass.Add("NewsRemoteService", new NewsRemoteService(this));
            GetClass.Add("LeaderboardRemoteService", new LeaderboardRemoteService(this));
            GetClass.Add("SocialRemoteService", new SocialRemoteService(this));
            GetClass.Add("SeasonRemoteService", new SeasonRemoteService(this));
            GetClass.Add("GameSeasonRemoteService", new GameSeasonRemoteService(this));
            GetClass.Add("SeasonalStatsRemoteService", new SeasonalStatsRemoteService(this));
            GetClass.Add("NewsFeedRemoteService", new NewsFeedRemoteService(this));
            GetClass.Add("GameAnnouncementRemoteService", new GameAnnouncementRemoteService(this));
            GetClass.Add("InAppRemoteService", new InAppRemoteService(this));
            GetClass.Add("GoogleInAppRemoteService", new GoogleInAppRemoteService(this));
            GetClass.Add("AdsRemoteService", new AdsRemoteService(this));
            GetClass.Add("AppStoreInAppRemoteService", new AppStoreInAppRemoteService(this));
            GetClass.Add("AmazonInAppRemoteService", new AmazonInAppRemoteService(this));
            GetClass.Add("AppGalleryInAppRemoteService", new AppGalleryInAppRemoteService(this));
            GetClass.Add("GameServerClanStatsRemoteService", new GSClanStatsRemoteService(this));
            GetClass.Add("GSClanStatsRemoteService", new GSClanStatsRemoteService(this));

            // Stubs for missing services
            GetClass.Add("AchievementRemoteService", new AchievementRemoteService(this));
            GetClass.Add("CrashLogRemoteService", new CrashLogRemoteService(this));
            GetClass.Add("DlcRemoteService", new DlcRemoteService(this));
            GetClass.Add("GameServerGameEventRemoteService", new GameServerGameEventRemoteService(this));
            GetClass.Add("GameServerInventoryRemoteService", new GameServerInventoryRemoteService(this));
            GetClass.Add("GdprRemoteService", new GdprRemoteService(this));
            GetClass.Add("GroupRemoteService", new GroupRemoteService(this));
            GetClass.Add("AppleIdAuthRemoteService", new AppleIdAuthRemoteService(this));
            GetClass.Add("HuaweiAuthRemoteService", new HuaweiAuthRemoteService(this));
            GetClass.Add("FacebookAuthRemoteService", new FacebookAuthRemoteService(this));
            GetClass.Add("GameCenterAuthRemoteService", new GameCenterAuthRemoteService(this));
            GetClass.Add("XiaomiAuthRemoteService", new XiaomiAuthRemoteService(this));
            GetClass.Add("SystemMessagesRemoteService", new SystemMessagesRemoteService(this));
            GetClass.Add("TournamentsRemoteService", new TournamentsRemoteService(this));
            GetClass.Add("UgcRemoteService", new UgcRemoteService(this));
            GetClass.Add("PromoRemoteService", new PromoRemoteService(this));
            
            var accountLink = new AccountLinkRemoteService(this);
            GetClass.Add("AccountLinkRemoteService", accountLink);
            GetClass.Add("144", accountLink);
        }
        public bool IsSessionAlive()
        {
            try
            {
                return clientConnected && TcpClient != null && TcpClient.Connected;
            }
            catch
            {
                return false;
            }
        }

        public bool IsOnlineActive()
        {
            return IsSessionAlive() && PingPong() && StaticClasses.Users.ContainsKey(TcpClient);
        }

        public DateTime LastActivityUtc
        {
            get
            {
                return new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);
            }
        }

        public void InitEventSenders(string playerId)
        {
            _test.Start();

            StaticClasses.EventSenders[playerId] = new List<IEventSender>
            {
                new FriendsRemoteEventSender(this),
                new MatchmakingRemoteService.MatchmakingEventSender(this),
                new MatchmakingRemoteService.MatchmakingFlowEventSender(this) { BoundPlayerId = playerId },
                new MarketplaceRemoteEventListener(this),
                new ClansRemoteEventListener(this),
                new ClanMessagesRemoteEventListener(this),
                new GlobalChatEventListener(this),
                new ChatRemoteEventListener(this),
                new MatchesRemoteEventListener(this),
                new PlayerStatsRemoteEventListener(this),
                new InventoryRemoteEventListener(this)
            };
            StaticClasses.UserServices[playerId] = this;
            StaticClasses.RegisterUserService(playerId, this);
        }

        private long _lastActivityTicks = DateTime.UtcNow.Ticks;

        // Таймаут неактивности для МОНИТОРИНГА соединения. Раньше здесь было 60 секунд:
        // во время матча клиент не шлёт пинги на лобби-канал (игра идёт через Photon),
        // и сервер убивал живую сессию прямо посреди игры - у клиента появлялось
        // "Переподключение к серверу", а после возврата в лобби интерфейс зависал.
        // 15 минут с запасом покрывают любой матч.
        private const double MonitoringIdleTimeoutSeconds = 900;

        private bool PingPong()
        {
            long lastTicks = Interlocked.Read(ref _lastActivityTicks);
            return (DateTime.UtcNow - new DateTime(lastTicks)).TotalSeconds < 60;
        }

        private bool MonitoringUpdate()
        {
            lock (StateLock)
            {
                bool connected = TcpClient.Client != null && TcpClient.Client.Connected && clientConnected;
                long lastTicks = Interlocked.Read(ref _lastActivityTicks);
                bool activity = (DateTime.UtcNow - new DateTime(lastTicks)).TotalSeconds < MonitoringIdleTimeoutSeconds;

                if (!activity && Logger.LogDebug)
                {
                    Logger.Debug($"PingPong failed. Over {MonitoringIdleTimeoutSeconds} seconds without activity from client.");
                }

                return connected && activity;
            }
        }

        public async Task HandleClient()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                clientConnected = true;
                Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
                
                _ = Task.Run(() => ReceiveCheck());

                while (clientConnected)
                {
                    clientConnected = MonitoringUpdate();
                    if (!clientConnected)
                    {
                        Logger.Debug("Disconnect");
                        stopwatch.Reset();
                        CloseTcpClient();
                        SaveAndRemoveUser();
                        return;
                    }
                    
                    bool processed = false;
                    while (_reponses.TryDequeue(out ResponseMessage response))
                    {
                        await SendResponce(response).ConfigureAwait(false);
                        processed = true;
                    }
                    while (_requestes.TryDequeue(out RpcRequest request))
                    {
                        await GetAndSendResponse(request).ConfigureAwait(false);
                        while (_reponses.TryDequeue(out ResponseMessage requestResponse))
                        {
                            await SendResponce(requestResponse).ConfigureAwait(false);
                        }
                        processed = true;
                    }

                    if (!processed)
                        await _queueSignal.WaitAsync(10).ConfigureAwait(false);
                }
            }
            catch (System.IO.IOException)
            {
                stopwatch.Reset();
                CloseTcpClient();
            }
            catch (System.Net.Sockets.SocketException)
            {
                stopwatch.Reset();
                CloseTcpClient();
            }
            catch (ObjectDisposedException)
            {
                stopwatch.Reset();
                CloseTcpClient();
            }
            catch (System.Exception ex)
            {
                stopwatch.Reset();
                Console.WriteLine("Error occurred while handling client: {0}", ex.ToString());
                CloseTcpClient();
            }
            Logger.Debug("Disconnect");
            stopwatch.Reset();
            CloseTcpClient();
            SaveAndRemoveUser();
        }


        public async Task ReceiveCheck()
        {
            var stream = TcpClient.GetStream();
            byte[] headerBuffer = new byte[4];

            while (clientConnected)
            {
                try
                {
                    if (!clientConnected)
                    {
                        break;
                    }

                    await ReadExactAsync(stream, headerBuffer, 4).ConfigureAwait(false);
                    int dataSize = BitConverter.ToInt32(headerBuffer, 0);
                    RpcTrafficLog.PacketHeader(this, dataSize);

                    if (dataSize < 0 || dataSize >= 100000000)
                    {
                        RpcTrafficLog.PacketRejected(this, dataSize);
                        clientConnected = false;
                        break;
                    }

                    if (dataSize == 0)
                    {
                        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
                        await SendPongAsync().ConfigureAwait(false);
                        continue;
                    }

                    byte[] bodyBuffer = new byte[dataSize];
                    await ReadExactAsync(stream, bodyBuffer, dataSize).ConfigureAwait(false);
                    Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
                    RpcTrafficLog.PacketBody(this, bodyBuffer);

                    if (bodyBuffer.Length > 1)
                    {
                        ReceiveRequest(bodyBuffer);
                    }
                    else
                    {
                        await SendPongAsync().ConfigureAwait(false);
                    }
                }
                catch (System.Exception ex)
                {
                    if (!(ex is ObjectDisposedException) && !(ex is IOException))
                    {
                        RpcTrafficLog.ReceiveException(this, ex);
                    }
                    clientConnected = false;
                    break;
                }
            }
        }

        private async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            int totalRead = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }
                offset += read;
                totalRead += read;
            }
            if (count == 4)
            {
                Array.Reverse(buffer);
            }
            return totalRead;
        }

        private void CloseTcpClient()
        {
            RpcTrafficLog.ClientDisconnected(this, "Closing TcpClient");
            try
            {
                if (TcpClient.Connected) TcpClient.GetStream().Close();
            }
            catch (System.Exception msg)
            {
                Logger.Debug(msg);
            }
            finally
            {
                TcpClient.Close();
            }
        }
        private void SaveAndRemoveUser()
        {
            if (StaticClasses.Users.ContainsKey(TcpClient))
            {
                _test.Stop();
                BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                string playerId = StaticClasses.Users[TcpClient];
                bool isCurrentSession =
                    StaticClasses.UserServices.TryGetValue(playerId, out UserService boundService) &&
                    ReferenceEquals(boundService, this);

                if (!isCurrentSession)
                {
                    // Secondary TCP (Allies search): клиент закрывает его после onMatchmakingDone.
                    // На primary — только Done (один раз), без повторного Confirmation.
                    bool wasSearchFlow = StaticClasses.MatchmakingFlowChannels.TryGetValue(playerId, out UserService flowCh)
                        && ReferenceEquals(flowCh, this);
                    StaticClasses.UnregisterUserService(playerId, this);
                    StaticClasses.Users.TryRemove(TcpClient, out _);
                    if (wasSearchFlow && MatchmakingManager.HasPendingMatch(playerId))
                    {
                        if (StaticClasses.UserServices.TryGetValue(playerId, out UserService primary)
                            && primary != null && primary.IsSessionAlive())
                        {
                            StaticClasses.SetMatchmakingFlowChannel(playerId, primary);
                        }
                        try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }
                    }
                    return;
                }

                Security.PlayerSessionRegistry.Release(playerId, this);

                boltMain.AddPlayerTime(ObjectId.Parse(playerId), (int)_test.Elapsed.TotalSeconds);
                PlayerDocument playerDocument = boltMain.GetPlayerDocument(ObjectId.Parse(playerId));
                RpcServer.PlayerStatus stat = StaticClasses.PlayersStatus[playerId];
                if (stat.playInGame.lobbyId != "" && StaticClasses.Lobbies.TryGetValue(stat.playInGame.lobbyId, out BoltLobby lobby))
                {
                    lobby.RemoveLobbyAny(playerDocument.GetBoltFriend());
                    StaticClasses.Lobbies[stat.playInGame.lobbyId] = lobby;
                    if (lobby.LobbyMembers.Length == 0)
                    {
                        StaticClasses.Lobbies.TryRemove(lobby.Id, out _);
                    }
                    else if (lobby.LobbyOwnerId == playerId)
                    {
                        foreach (BoltFriend player in lobby.LobbyMembers)
                            if (StaticClasses.EventSenders.TryGetValue(player.Id, out List<IEventSender> eventSenders))
                            {
                                 eventSenders.FirstOrDefault(a => a is MatchmakingRemoteService.MatchmakingEventSender).SendEvent("onLobbyOwnerChanged", new object[] { lobby.LobbyOwnerId });
                            }
                        foreach (BoltFriend player in lobby.LobbySpectators)
                            if (StaticClasses.EventSenders.TryGetValue(player.Id, out List<IEventSender> eventSenders))
                            {
                                 eventSenders.FirstOrDefault(a => a is MatchmakingRemoteService.MatchmakingEventSender).SendEvent("onLobbyOwnerChanged", new object[] { lobby.LobbyOwnerId });
                            }
                        StaticClasses.Lobbies[lobby.Id].LobbyOwnerId = lobby.LobbyMembers.First().Id;
                    }
                    foreach (BoltFriend player in lobby.LobbyMembers)
                        if (StaticClasses.EventSenders.TryGetValue(player.Id, out List<IEventSender> eventSenders))
                        {
                             eventSenders.FirstOrDefault(a => a is MatchmakingRemoteService.MatchmakingEventSender).SendEvent("onPlayerLeftLobby", new object[] { playerId });
                        }
                    foreach (BoltFriend player in lobby.LobbySpectators)
                        if (StaticClasses.EventSenders.TryGetValue(player.Id, out List<IEventSender> eventSenders))
                        {
                             eventSenders.FirstOrDefault(a => a is MatchmakingRemoteService.MatchmakingEventSender).SendEvent("onPlayerLeftLobby", new object[] { playerId });
                        }
                }
                StaticClasses.Users.TryRemove(TcpClient, out _);
                if (StaticClasses.UserServices.TryGetValue(playerId, out var current) && ReferenceEquals(current, this))
                {
                    if (MatchmakingManager.IsSearching(playerId) || MatchmakingManager.HasPendingMatch(playerId))
                    {
                        StaticClasses.PromoteLiveFlowSession(playerId);
                        if (!StaticClasses.HasLiveFlowChannel(playerId))
                            StaticClasses.UserServices.TryRemove(playerId, out _);
                    }
                }
                if (StaticClasses.UserServices.TryGetValue(playerId, out current) && ReferenceEquals(current, this)
                    && !StaticClasses.HasLiveFlowChannel(playerId))
                {
                    ScheduleGracefulDisconnect(playerId);
                }
            }
        }

        public void ReceiveRequest(byte[] data)
        {
            try
            {
                RpcRequest request = RpcRequest.Parser.ParseFrom(data);
                RpcTrafficLog.Request(this, request, data == null ? 0 : data.Length);
                RpcTrafficLog.LogRawInbound(request.ServiceName + "." + request.MethodName, data);
                this._requestes.Enqueue(request);
                SignalQueue();
            }
            catch (System.Exception ex)
            {
                RpcTrafficLog.ParseError(this, data, ex);
            }
        }

        public static byte[] WrapHeader(byte[] body)
        {
            byte[] bytes = BitConverter.GetBytes(body.Length);
            Array.Reverse(bytes);
            
            byte[] result = new byte[4 + body.Length];
            Array.Copy(bytes, 0, result, 0, 4);
            Array.Copy(body, 0, result, 4, body.Length);
            return result;
        }

        private async Task SendResponce(ResponseMessage response)
        {
            byte[] body = response.ToByteArray();
            RpcTrafficLog.Response(this, response, body.Length);
            RpcTrafficLog.LogRawHex("->SERVER", body);
            byte[] array = UserService.WrapHeader(body);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using (var cts = new CancellationTokenSource(30000))
                {
                    await this.TcpClient.GetStream().WriteAsync(array, 0, array.Length, cts.Token).ConfigureAwait(false);
                }
                RpcTrafficLog.ResponseSent(this, response, array.Length);
            }
            catch (OperationCanceledException ex)
            {
                RpcTrafficLog.SendException(this, response, ex);
                clientConnected = false;
            }
            catch (System.IO.IOException ex)
            {
                RpcTrafficLog.SendException(this, response, ex);
                clientConnected = false;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                RpcTrafficLog.SendException(this, response, ex);
                clientConnected = false;
            }
            catch (ObjectDisposedException ex)
            {
                RpcTrafficLog.SendException(this, response, ex);
                clientConnected = false;
            }
            finally
            {
                _writeLock.Release();
            }
        }
        public long RequestTimeout { get; set; } = 10000L;
        public Action<ResponseMessage> ResponseInterceptor { get; set; }
        public void SendResponce(ResponseMessage request, CancellationToken ct = default(CancellationToken))
        {
            if (ResponseInterceptor != null)
            {
                ResponseInterceptor(request);
                return;
            }
            _reponses.Enqueue(request);
            SignalQueue();
            try
            {
                string ep = TcpClient?.Client?.RemoteEndPoint?.ToString() ?? "null";
                string eventName = request?.EventResponse?.EventName ?? request?.EventResponse?.ListenerName ?? "unknown";
                Logger.Debug($"[UserService] Enqueued response for {ep}, eventName={eventName}, queueLen={_reponses.Count}");
            }
            catch { }
        }

        private async Task GetAndSendResponse(RpcRequest request)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                RpcTrafficLog.DispatchStart(this, request);
                if (TrySendBanResponse(request))
                {
                    sw.Stop();
                    RpcTrafficLog.DispatchFinish(this, request, sw.ElapsedMilliseconds);
                    return;
                }

                if (GetClass.TryGetValue(request.ServiceName, out var handler2))
                {
                    await handler2.InvokeAsync(request).ConfigureAwait(false);
                    sw.Stop();
                    RpcTrafficLog.DispatchFinish(this, request, sw.ElapsedMilliseconds);
                }
                else
                {
                    sw.Stop();
                    if (ShouldDropUnknownService(request))
                    {
                        return;
                    }

                    RpcTrafficLog.ServiceNotFound(this, request);
                    SendServerError(request.Id, 404, "Service not found: " + request.ServiceName, null);
                }
            }
            catch (System.Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"\n[EXCEPTION] RPC 500 in {request?.ServiceName}.{request?.MethodName}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine();
                RpcTrafficLog.DispatchException(this, request, ex, sw.ElapsedMilliseconds);
                SendServerError(request == null ? string.Empty : request.Id, 500, "Unhandled server exception", ex);
            }
        }

        private DateTime _lastBanCheckUtc = DateTime.MinValue;
        private bool _lastBanCheckResult = false;

        private bool TrySendBanResponse(RpcRequest request)
        {
            if (request == null || !StaticClasses.Users.TryGetValue(TcpClient, out string playerId))
            {
                return false;
            }

            // Cache ban check for 30 seconds to avoid hammering MongoDB on every RPC
            DateTime now = DateTime.UtcNow;
            if ((now - _lastBanCheckUtc).TotalSeconds < 30)
            {
                return _lastBanCheckResult;
            }

            PlayerDocument playerDocument;
            try
            {
                playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(playerId));
            }
            catch
            {
                return false;
            }

            _lastBanCheckUtc = now;

            if (playerDocument == null || !playerDocument.isBanned)
            {
                _lastBanCheckResult = false;
                return false;
            }

            _lastBanCheckResult = true;

            var banResponse = new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = request.Id ?? string.Empty,
                    Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                    {
                        Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                        Code = 9999
                    }
                }
            };
            banResponse.RpcResponse.Exception.Property.Add("uid", playerDocument.uid ?? playerId);
            banResponse.RpcResponse.Exception.Property.Add("banCode", playerDocument.banCode ?? "1001");
            banResponse.RpcResponse.Exception.Property.Add("reason", playerDocument.banReason ?? "Banned");
            SendResponce(banResponse);

            _ = Task.Run(async () =>
            {
                await Task.Delay(1500).ConfigureAwait(false);
                ForceDisconnect();
            });

            return true;
        }

        private int _unknownServiceCount;
        private DateTime _unknownServiceWindowStartUtc = DateTime.MinValue;

        private bool ShouldDropUnknownService(RpcRequest request)
        {
            string serviceName = request == null ? string.Empty : request.ServiceName ?? string.Empty;
            DateTime now = DateTime.UtcNow;

            if ((now - _unknownServiceWindowStartUtc).TotalSeconds > 2)
            {
                _unknownServiceWindowStartUtc = now;
                _unknownServiceCount = 0;
            }

            _unknownServiceCount++;

            if (string.IsNullOrWhiteSpace(serviceName))
            {
                if (_unknownServiceCount > 20)
                {
                    ForceDisconnect();
                }
                return true;
            }

            if (_unknownServiceCount > 40)
            {
                Logger.LogWarn($"[SECURITY] Too many unknown RPC services from {RemoteAddress}; disconnecting. Last={serviceName}");
                ForceDisconnect();
                return true;
            }

            return false;
        }

        private void SendServerError(string id, int code, string reason, System.Exception ex)
        {
            var exception = new Axlebolt.RpcSupport.Protobuf.Exception
            {
                Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                Code = code
            };

            if (!string.IsNullOrEmpty(reason))
            {
                exception.Property["reason"] = reason;
            }

            if (ex != null)
            {
                exception.Property["type"] = ex.GetType().FullName;
                exception.Property["message"] = ex.Message ?? string.Empty;
                exception.Property["stack"] = ex.ToString();
            }

            _reponses.Enqueue(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = id ?? string.Empty,
                    Exception = exception
                }
            });
            SignalQueue();
        }

        private void SignalQueue()
        {
            try
            {
                _queueSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }

        public void ForceDisconnect()
        {
            clientConnected = false;
            try
            {
                if (StaticClasses.Users.TryGetValue(TcpClient, out string pid))
                {
                    StaticClasses.UnregisterUserService(pid, this);
                }
            }
            catch { }
            try
            {
                TcpClient?.Close();
            }
            catch
            {
            }
        }

        public void ReceivePing()
        {
            if (!this._pingReceived && Logger.LogDebug)
            {
                Logger.Debug("Ping received successfully!");
            }
            this._pingReceived = true;
        }
        private async Task SendPongAsync()
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                byte[] pongBytes = UserService.WrapHeader(new byte[] { 1 });
                using (var cts = new CancellationTokenSource(5000))
                {
                    await this.TcpClient.GetStream().WriteAsync(pongBytes, 0, pongBytes.Length, cts.Token).ConfigureAwait(false);
                }
                RpcTrafficLog.PongSent(this);
            }
            catch (System.Exception ex)
            {
                RpcTrafficLog.PongException(this, ex);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        
        private ConcurrentQueue<RpcRequest> _requestes;
        private ConcurrentQueue<ResponseMessage> _reponses;
        private readonly SemaphoreSlim _queueSignal = new SemaphoreSlim(0);
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private bool _pingReceived = true;
        private class ReceiveException : SocketException
        {
        }
        public TcpClient TcpClient { get; private set; }
        public string GameServerToken { get; set; }
        public byte[] SessionAesKey { get; set; }
        public byte[] SessionAesIV { get; set; }
        public System.Security.Cryptography.RSAParameters? ClientRsaParameters { get; set; }
        public string LastIssuedAuthToken { get; set; }
        public DateTime LastIssuedAuthAtUtc { get; set; }
    }

    internal static class RpcTrafficLog
    {
        public static void ClientConnected(UserService user)
        {
            Logger.Log("Client connected");
        }

        public static void ClientDisconnected(UserService user, string reason)
        {
            Logger.Log("Client disconnected");
        }

        public static void PacketHeader(UserService user, int size)
        {
            // Silent
        }

        public static void PacketBody(UserService user, byte[] body)
        {
            // Inbound raw bytes are logged per-RPC in LogRawInbound (avoids double logging).
        }

        public static void LogRawInbound(string label, byte[] body)
        {
            LogRawHex("<-CLIENT " + label, body);
        }

        public static void LogRawHex(string dir, byte[] body)
        {
            try
            {
                if (body == null || body.Length == 0) return;
                var sb = new System.Text.StringBuilder();
                sb.Append(System.DateTime.UtcNow.ToString("HH:mm:ss.fff")).Append(' ').Append(dir).Append(" len=").Append(body.Length).Append(" hex=");
                foreach (byte b in body) sb.Append(b.ToString("X2"));
                System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "traffic_hex.log"), sb.AppendLine().ToString());
            }
            catch { }
        }

        public static void PacketRejected(UserService user, int size)
        {
            Logger.Error("Packet rejected: bad size " + size);
        }

        public static void ReceiveException(UserService user, System.Exception ex)
        {
            Logger.Error("Receive error: " + ex.Message);
        }

        public static void ParseError(UserService user, byte[] body, System.Exception ex)
        {
            Logger.Error("Parse error: " + ex.Message);
        }

        public static void Request(UserService user, RpcRequest request, int rawSize)
        {
            if (Logger.LogDebug)
            {
                if (request.MethodName != "getCurrentChallenges")
                {
                    Logger.Debug("RPC: " + request.ServiceName + "." + request.MethodName);
                }
            }
        }

        public static void DispatchStart(UserService user, RpcRequest request)
        {
            // Silent
        }

        public static void DispatchFinish(UserService user, RpcRequest request, long elapsedMs)
        {
            // Silent
        }

        public static void DispatchException(UserService user, RpcRequest request, System.Exception ex, long elapsedMs)
        {
            string service = request == null ? "unknown" : request.ServiceName;
            string method = request == null ? "unknown" : request.MethodName;
            Logger.Error("RPC error: " + service + "." + method + " - " + ex.Message);
            Logger.Exception(ex);
        }

        public static void ServiceNotFound(UserService user, RpcRequest request)
        {
            Logger.Error("Service not found: " + request.ServiceName);
        }

        public static void Response(UserService user, ResponseMessage response, int bodySize)
        {
            // Silent
        }

        public static void ResponseSent(UserService user, ResponseMessage response, int packetSize)
        {
            // Silent
        }

        public static void SendException(UserService user, ResponseMessage response, System.Exception ex)
        {
            Logger.Error("Send error: " + ex.Message);
        }

        public static void PongSent(UserService user)
        {
            // Silent
        }

        public static void PongException(UserService user, System.Exception ex)
        {
            Logger.Error("Pong error: " + ex.Message);
        }
    }

}
