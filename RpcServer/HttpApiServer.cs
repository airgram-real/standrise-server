using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer.Api;
using StandRiseServer.MongoDB.Game;
using Axlebolt.Bolt.Protobuf;
using MongoDB.Driver;

namespace StandRiseServer.RpcServer
{
    public class HttpApiServer
    {
        private readonly System.Net.Sockets.TcpListener _listener;
        private readonly int _port;
        private static readonly object MatchRewardLock = new object();
        private static readonly HashSet<string> RewardedMatches = new HashSet<string>();
        private static readonly HashSet<string> FinishedMatches = new HashSet<string>();

        public HttpApiServer(int port)
        {
            _port = port;
            _listener = new System.Net.Sockets.TcpListener(IPAddress.Any, port);
        }

        public async Task Start()
        {
            _listener.Start();
            Console.WriteLine($"[HTTP API] Started on port {_port} (TcpListener, no admin/urlacl required)");

            while (true)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => ServeClient(client));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HTTP API] Accept Error: {ex.Message}");
                }
            }
        }

        private sealed class MiniQuery
        {
            private readonly Dictionary<string, string> _d;
            public MiniQuery(Dictionary<string, string> d) { _d = d; }
            public string this[string key]
            {
                get { return key != null && _d.TryGetValue(key, out var v) ? v : null; }
            }
        }

        private sealed class MiniUrl
        {
            public readonly string AbsolutePath;
            public MiniUrl(string absolutePath) { AbsolutePath = absolutePath; }
        }

        private sealed class MiniRequest
        {
            public string HttpMethod;
            public MiniUrl Url;
            public MiniQuery QueryString;
            public MemoryStream InputStream = new MemoryStream();
        }

        private sealed class MiniResponse
        {
            public int StatusCode = 200;
            public string ContentType;
            public MemoryStream Body = new MemoryStream();
            public Stream OutputStream => Body;
            public void Close() { }
        }

        private sealed class MiniContext
        {
            public readonly MiniRequest Request = new MiniRequest();
            public readonly MiniResponse Response = new MiniResponse();
        }

        private async Task ServeClient(System.Net.Sockets.TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var context = new MiniContext();
                try
                {
                    await ParseRequestAsync(stream, context.Request);
                    if (string.IsNullOrEmpty(context.Request.HttpMethod))
                    {
                        context.Response.StatusCode = 400;
                    }
                    else
                    {
                        await HandleRequest(context);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HTTP API] Request Error: {ex.Message}");
                    context.Response.StatusCode = 500;
                }
                finally
                {
                    context.Response.Close();
                }
                await SendResponseAsync(stream, context.Response);
            }
        }

        private static async Task<string> ReadLineAsync(Stream stream)
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            int b;
            while ((b = await stream.ReadAsync(one, 0, 1)) > 0)
            {
                char c = (char)one[0];
                if (c == '\n') break;
                if (c != '\r') sb.Append(c);
            }
            return sb.ToString();
        }

        private static async Task ParseRequestAsync(Stream stream, MiniRequest req)
        {
            string requestLine = await ReadLineAsync(stream);
            if (string.IsNullOrEmpty(requestLine)) return;

            var parts = requestLine.Split(' ');
            if (parts.Length < 2) return;

            req.HttpMethod = parts[0].ToUpperInvariant();
            string target = parts[1];

            string pathOnly = target;
            string queryPart = null;
            int q = target.IndexOf('?');
            if (q >= 0)
            {
                pathOnly = target.Substring(0, q);
                queryPart = target.Substring(q + 1);
            }
            req.Url = new MiniUrl(pathOnly);

            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(queryPart))
            {
                foreach (var pair in queryPart.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    if (eq < 0) query[Uri.UnescapeDataString(pair)] = "";
                    else query[Uri.UnescapeDataString(pair.Substring(0, eq))] = Uri.UnescapeDataString(pair.Substring(eq + 1));
                }
            }
            req.QueryString = new MiniQuery(query);

            int contentLength = 0;
            string header;
            while (!string.IsNullOrWhiteSpace(header = await ReadLineAsync(stream)))
            {
                int ci = header.IndexOf(':');
                if (ci <= 0) continue;
                string name = header.Substring(0, ci).Trim();
                string value = header.Substring(ci + 1).Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(value, out contentLength);
            }

            if (contentLength > 0 && contentLength <= 100 * 1024 * 1024)
            {
                var body = new byte[contentLength];
                int read = 0;
                while (read < contentLength)
                {
                    int n = await stream.ReadAsync(body, read, contentLength - read);
                    if (n <= 0) break;
                    read += n;
                }
                req.InputStream = new MemoryStream(body, 0, read, writable: false);
            }
        }

        private static async Task SendResponseAsync(Stream stream, MiniResponse resp)
        {
            byte[] body = resp.Body.ToArray();
            string statusText = resp.StatusCode switch
            {
                200 => "OK",
                400 => "Bad Request",
                404 => "Not Found",
                500 => "Internal Server Error",
                _ => "OK"
            };
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(resp.StatusCode).Append(' ').Append(statusText).Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            if (!string.IsNullOrEmpty(resp.ContentType))
                sb.Append("Content-Type: ").Append(resp.ContentType).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n");

            byte[] head = Encoding.UTF8.GetBytes(sb.ToString());
            await stream.WriteAsync(head, 0, head.Length);
            if (body.Length > 0) await stream.WriteAsync(body, 0, body.Length);
            await stream.FlushAsync();
        }

        private async Task HandleRequest(MiniContext context)
        {
            try
            {
                string path = context.Request.Url.AbsolutePath;

                if (context.Request.HttpMethod == "POST" && path == "/api/player/status")
                {
                    using var reader = new StreamReader(context.Request.InputStream);
                    string json = await reader.ReadToEndAsync();
                    var data = JsonSerializer.Deserialize<PlayerStatusUpdate>(json);

                    if (data != null && !string.IsNullOrEmpty(data.PlayerId))
                    {
                        UpdatePlayerStatus(data);
                        context.Response.StatusCode = 200;
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else if (context.Request.HttpMethod == "POST" && path == "/api/player/validate")
                {
                    // Всегда одобряем подключение игроков, чтобы избежать ошибки "matchmaking validation failed"
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    using var writer = new StreamWriter(context.Response.OutputStream);
                    await writer.WriteAsync("{\"Valid\":true}");
                }
                else if (context.Request.HttpMethod == "GET" && path == "/api/gameevent/challenges")
                {
                    string playerId = context.Request.QueryString["PlayerId"];
                    if (!string.IsNullOrEmpty(playerId))
                    {
                        var challenges = GetChallengesJson(playerId);
                        context.Response.StatusCode = 200;
                        using var writer = new StreamWriter(context.Response.OutputStream);
                        await writer.WriteAsync(challenges);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else if (context.Request.HttpMethod == "GET" && path == "/api/player/ranked-info")
                {
                    string playerId = context.Request.QueryString["PlayerId"];
                    string mode = context.Request.QueryString["Mode"];
                    if (!string.IsNullOrEmpty(playerId))
                    {
                        var info = GetPlayerRankedInfoJson(playerId, mode);
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "application/json";
                        using var writer = new StreamWriter(context.Response.OutputStream);
                        await writer.WriteAsync(info);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else if (context.Request.HttpMethod == "GET" && path == "/api/match/afk-bots")
                {
                    // Photon-плагин: клиент часто не прокидывает afk_bots в room props.
                    string roomId = context.Request.QueryString["roomId"]
                        ?? context.Request.QueryString["RoomId"]
                        ?? "";
                    var props = StandRiseServer.RpcServer.Api.MatchmakingManager.GetRoomRankedProps(roomId);
                    int bots = props.AfkBots > 0 ? props.AfkBots : StandRiseServer.RpcServer.Api.MatchmakingManager.GetRoomAfkBots(roomId);
                    int maxPlayers = props.MaxPlayers;
                    int teamSize = props.TeamSize;
                    string safeRoom = (roomId ?? "").Replace("\\", "").Replace("\"", "");
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    using (var writer = new StreamWriter(context.Response.OutputStream))
                        await writer.WriteAsync("{\"afk_bots\":" + bots + ",\"max_players\":" + maxPlayers + ",\"team_size\":" + teamSize + ",\"round_count\":" + props.RoundCount + ",\"roomId\":\"" + safeRoom + "\"}");
                }
                else if (context.Request.HttpMethod == "POST" && path == "/api/gameevent/progress")
                {
                    using var reader = new StreamReader(context.Request.InputStream);
                    string json = await reader.ReadToEndAsync();
                    var data = JsonSerializer.Deserialize<MissionProgressUpdate>(json);

                    if (data != null && !string.IsNullOrEmpty(data.PlayerId))
                    {
                        UpdateMissionProgress(data);
                        context.Response.StatusCode = 200;
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else if (context.Request.HttpMethod == "POST" && path == "/api/match/finished")
                {
                    // Вызывается Photon-плагином из OnCloseGame (серверное, не подделываемое
                    // клиентом событие) - выдаёт игрокам "кредиты" на пост-матч дроп
                    // (RECIPE_DROP_IN_GAME / RECIPE_GOOD_GAME_* и т.д.), чтобы это нельзя было
                    // задюпать простым спамом exchangeInventoryItems. См. InventoryDupeProtection.
                    using var reader = new StreamReader(context.Request.InputStream);
                    string json = await reader.ReadToEndAsync();
                    var data = JsonSerializer.Deserialize<MatchFinished>(json);

                    if (data != null && !string.IsNullOrEmpty(data.MatchId) && data.PlayerIds != null && data.PlayerIds.Count > 0)
                    {
                        bool isNewMatch;
                        lock (MatchRewardLock)
                        {
                            isNewMatch = FinishedMatches.Add(data.MatchId);
                        }

                        if (isNewMatch)
                        {
                            foreach (var playerId in data.PlayerIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
                            {
                                StandRiseServer.RpcServer.Security.InventoryDupeProtection.GrantPostMatchDropCredits(playerId);
                            }
                            try
                            {
                                MatchmakingManager.ClearFinishedMatchStatus(data.PlayerIds, data.MatchId, data.MatchId);
                            }
                            catch (Exception clearEx)
                            {
                                Console.WriteLine($"[HTTP API] ClearFinishedMatchStatus on finished failed: {clearEx.Message}");
                            }
                        }
                        context.Response.StatusCode = 200;
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else if (context.Request.HttpMethod == "POST" && path == "/api/match/result")
                {
                    using var reader = new StreamReader(context.Request.InputStream);
                    string json = await reader.ReadToEndAsync();
                    var data = JsonSerializer.Deserialize<MatchResult>(json);

                    if (data != null && !string.IsNullOrEmpty(data.MatchId))
                    {
                        await ProcessMatchResult(data);
                        context.Response.StatusCode = 200;
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else if (context.Request.HttpMethod == "POST" && path == "/api/news/upload")
                {
                    string filename = context.Request.QueryString["filename"];
                    if (string.IsNullOrWhiteSpace(filename))
                    {
                        context.Response.StatusCode = 400;
                        using var w = new StreamWriter(context.Response.OutputStream);
                        await w.WriteAsync("{\"error\":\"filename query param required\"}");
                        return;
                    }

                    using var ms = new MemoryStream();
                    await context.Request.InputStream.CopyToAsync(ms);
                    byte[] imageBytes = ms.ToArray();

                    BoltMainDatabaseProvider.Instance.WriteGlobalFile(filename, imageBytes);
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    using var writer = new StreamWriter(context.Response.OutputStream);
                    await writer.WriteAsync($"{{\"ok\":true,\"filename\":\"{filename}\",\"size\":{imageBytes.Length}}}");
                }
                else if (context.Request.HttpMethod == "POST" && path == "/api/news/add")
                {
                    using var reader = new StreamReader(context.Request.InputStream);
                    string json = await reader.ReadToEndAsync();
                    var data = JsonSerializer.Deserialize<NewsAddRequest>(json);

                    string definitionId = !string.IsNullOrWhiteSpace(data?.DefinitionId)
                        ? data.DefinitionId
                        : data?.ImageUrl;

                    if (data == null || string.IsNullOrWhiteSpace(definitionId))
                    {
                        context.Response.StatusCode = 400;
                        using var w = new StreamWriter(context.Response.OutputStream);
                        await w.WriteAsync("{\"error\":\"definitionId or imageUrl required\"}");
                        return;
                    }

                    BoltMainDatabaseProvider.Instance.AddNewsFeedItem(definitionId, data.PlayerId ?? "", data.ItemText ?? "", data.GameVersion ?? "", data.Link ?? "");
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    using var writer2 = new StreamWriter(context.Response.OutputStream);
                    await writer2.WriteAsync("{\"ok\":true}");
                }
                else if (context.Request.HttpMethod == "GET" && path == "/api/news/image")
                {
                    string filename = context.Request.QueryString["filename"];
                    if (string.IsNullOrWhiteSpace(filename))
                    {
                        context.Response.StatusCode = 400;
                        return;
                    }
                    byte[] imageData = BoltMainDatabaseProvider.Instance.ReadGlobalFile(filename);
                    if (imageData == null || imageData.Length == 0)
                    {
                        context.Response.StatusCode = 404;
                        return;
                    }
                    string ext = System.IO.Path.GetExtension(filename).ToLower();
                    context.Response.ContentType = ext switch
                    {
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".png" => "image/png",
                        ".gif" => "image/gif",
                        ".webp" => "image/webp",
                        _ => "application/octet-stream"
                    };
                    context.Response.StatusCode = 200;
                    await context.Response.OutputStream.WriteAsync(imageData, 0, imageData.Length);
                }
                else if (context.Request.HttpMethod == "GET" && path == "/api/news/list")
                {
                    var items = BoltMainDatabaseProvider.Instance.GetNewsFeedItems(0, 50);
                    var result = items.Select(i => new {
                        id = i._id.ToString(),
                        definitionId = i.definitionId,
                        playerId = i.playerId,
                        itemText = i.itemText,
                        imageUrl = i.imageUrl,
                        gameVersion = i.gameVersion,
                        timestamp = i.timestamp
                    });
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    using var writer3 = new StreamWriter(context.Response.OutputStream);
                    await writer3.WriteAsync(JsonSerializer.Serialize(result));
                }
                else if (context.Request.HttpMethod == "GET" && path == "/api/market/upgrader")
                {
                    int limit = 200;
                    int.TryParse(context.Request.QueryString["limit"], out limit);
                    var up = await MarketUpgraderApi.CatalogAsync(limit);
                    context.Response.StatusCode = up.Status;
                    context.Response.ContentType = "application/json";
                    using var upWriter = new StreamWriter(context.Response.OutputStream);
                    await upWriter.WriteAsync(up.Json);
                }
                else if (context.Request.HttpMethod == "GET" && path == "/api/market/upgrader/inventory")
                {
                    string pid = context.Request.QueryString["playerId"] ?? context.Request.QueryString["id"];
                    var inv = await MarketUpgraderApi.InventoryAsync(pid);
                    context.Response.StatusCode = inv.Status;
                    context.Response.ContentType = "application/json";
                    using var invWriter = new StreamWriter(context.Response.OutputStream);
                    await invWriter.WriteAsync(inv.Json);
                }
                else if (context.Request.HttpMethod == "POST" && path == "/api/market/upgrader/bet")
                {
                    using var betReader = new StreamReader(context.Request.InputStream);
                    string betBody = await betReader.ReadToEndAsync();
                    string pid = context.Request.QueryString["playerId"] ?? context.Request.QueryString["id"] ?? "";
                    var bet = await MarketUpgraderApi.BetAsync(pid, betBody);
                    context.Response.StatusCode = bet.Status;
                    context.Response.ContentType = "application/json";
                    using var betWriter = new StreamWriter(context.Response.OutputStream);
                    await betWriter.WriteAsync(bet.Json);
                }
                else if (path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase))
                {
                    // Админ-API для сайта. Слушает только локально; наружу его
                    // проксирует ProjectRework.Web после проверки Telegram initData
                    // и списка админов бота. Вся логика выдачи — в AdminApi поверх
                    // методов бота, второй реализации нет.
                    string adminBody = "";
                    if (context.Request.HttpMethod == "POST")
                    {
                        using var adminReader = new StreamReader(context.Request.InputStream);
                        adminBody = await adminReader.ReadToEndAsync();
                    }
                    var adminResult = await AdminApi.HandleAsync(
                        context.Request.HttpMethod,
                        path,
                        name => context.Request.QueryString[name],
                        adminBody);
                    context.Response.StatusCode = adminResult.Status;
                    context.Response.ContentType = "application/json";
                    using var adminWriter = new StreamWriter(context.Response.OutputStream);
                    await adminWriter.WriteAsync(adminResult.Json);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTTP API] Request Error: {ex.Message}");
                context.Response.StatusCode = 500;
            }
            finally
            {
                context.Response.Close();
            }
        }

        private static string MapGameMode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Training";
            switch (raw)
            {
                case "DeathMatch":
                case "Defuse":
                case "ArmsRace":
                case "Training":
                case "SniperDuel":
                case "RankedDefuse":
                case "Ranked2v2":
                case "Escalation":
                case "MadSanta":
                case "Arcade":
                case "Assault":
                case "ClanRankedDefuse":
                case "GiftHunt":
                case "SpiritOfChristmas":
                case "SoulHunt":
                case "SnowballRumble":
                case "SniperBattle":
                case "GunpointRace":
                    return raw;
            }
            var mapped = raw.ToLowerInvariant() switch
            {
                "sniper" => "SniperDuel",
                "ranked" => "RankedDefuse",
                "rankeddefuse" => "RankedDefuse",
                "ranked2v2" => "Ranked2v2",
                "allies" => "Ranked2v2",
                "duel" => "SniperDuel",
                "yokai" => "Arcade",
                "deathmatch" => "DeathMatch",
                "defuse" => "Defuse",
                "armsrace" => "ArmsRace",
                "training" => "Training",
                "escalation" => "Escalation",
                _ => raw
            };
            return mapped;
        }

        private static bool IsMatchLobby(BoltLobby lobby)
        {
            if (lobby == null) return false;
            string name = lobby.Name ?? "";
            return name.IndexOf("Match", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("RankedDefuse", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Ranked2v2", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdatePlayerStatus(PlayerStatusUpdate data)
        {
            try
            {
                if (!StaticClasses.PlayersStatus.TryGetValue(data.PlayerId, out var status))
                {
                    status = new PlayerStatus();
                    StaticClasses.PlayersStatus[data.PlayerId] = status;
                }

                if (data.IsLeaving)
                {
                    status.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;

                    string restoredLobbyId = null;
                    BoltLobby restoredLobby = null;
                    foreach (var kvp in StaticClasses.Lobbies)
                    {
                        if (kvp.Value.IsLobbyAny(data.PlayerId))
                        {
                            restoredLobbyId = kvp.Key;
                            restoredLobby = kvp.Value;
                            break;
                        }
                    }

                    if (restoredLobby != null && !IsMatchLobby(restoredLobby))
                    {
                        if (status.playInGame == null)
                            status.playInGame = new PlayInGame { gameCode = "standoff2", gameVersion = StaticClasses.DefaultGameVersion };
                        else
                        {
                            status.playInGame.gameCode = "standoff2";
                            status.playInGame.gameVersion = StaticClasses.DefaultGameVersion;
                        }
                        status.playInGame.lobbyId = restoredLobbyId;
                        status.playInGame.lobbyName = restoredLobby.Name ?? string.Empty;
                        status.playInGame.photonGame = null;
                    }
                    else
                    {
                        if (status.playInGame == null)
                            status.playInGame = new PlayInGame { gameCode = "standoff2", gameVersion = StaticClasses.DefaultGameVersion };
                        status.playInGame.lobbyId = string.Empty;
                        status.playInGame.lobbyName = string.Empty;
                        status.playInGame.photonGame = null;
                    }
                }
                else
                {
                    string mappedMode = MapGameMode(data.Mode);

                    status.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
                    if (status.playInGame == null)
                    {
                        status.playInGame = new PlayInGame { gameCode = "standoff2", gameVersion = StaticClasses.DefaultGameVersion };
                    }
                    else
                    {
                        status.playInGame.gameCode = "standoff2";
                        status.playInGame.gameVersion = StaticClasses.DefaultGameVersion;
                    }

                    string existingLobbyId = status.playInGame.lobbyId;
                    bool hasActiveLobby = !string.IsNullOrWhiteSpace(existingLobbyId)
                                          && StaticClasses.Lobbies.ContainsKey(existingLobbyId);
                    string rid = data.RoomId ?? "";
                    bool rankedRoom = rid.StartsWith("RankedDefuse_", StringComparison.OrdinalIgnoreCase)
                        || rid.StartsWith("Ranked2v2_", StringComparison.OrdinalIgnoreCase)
                        || rid.StartsWith("ClanRanked", StringComparison.OrdinalIgnoreCase);
                    bool liveRankedLobby = false;
                    if (rankedRoom)
                    {
                        foreach (var kv in StaticClasses.Lobbies)
                        {
                            string lobbyRoom = kv.Value?.PhotonGame?.roomId ?? "";
                            if (string.Equals(lobbyRoom, rid, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(kv.Key, rid, StringComparison.OrdinalIgnoreCase))
                            {
                                liveRankedLobby = true;
                                break;
                            }
                        }
                    }
                    if (!hasActiveLobby)
                    {
                        status.playInGame.lobbyId = data.RoomId;
                    }
                    status.playInGame.lobbyName = mappedMode;

                    if (rankedRoom && !liveRankedLobby)
                    {
                        status.playInGame.photonGame = null;
                        status.playInGame.lobbyId = string.Empty;
                        status.playInGame.lobbyName = string.Empty;
                    }
                    else
                    {
                    status.playInGame.photonGame = new PhotonGame
                    {
                        region = data.Region?.ToLower() ?? BoltMainDatabaseProvider.MainServerRegion,
                        roomId = data.RoomId,
                        appVersion = StaticClasses.DefaultGameVersion,
                        customProperties = new Dictionary<string, string>
                        {
                            { "C0", mappedMode },
                            { "C1", "sandstone" },
                            { "game_mode", mappedMode }
                        }
                    };
                    }
                }

                BoltMainDatabaseProvider.Instance.SetPlayerStatus(ObjectId.Parse(data.PlayerId), status);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var friends = await BoltGameDatabaseProvider.Instance.GetPlayerFriends(data.PlayerId);
                        foreach (var friendDoc in friends)
                        {
                            string friendId = friendDoc.playerInitiatorId == data.PlayerId ? friendDoc.playerId : friendDoc.playerInitiatorId;
                            if (StaticClasses.EventSenders.TryGetValue(friendId, out var senders))
                            {
                                var friendsSender = senders.FirstOrDefault(s => s is FriendsRemoteEventSender);
                                friendsSender?.SendEvent("onPlayerStatusChanged", new object[] { data.PlayerId, status.GetPlayerStatusProto() });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[HTTP API] Broadcast Status Error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTTP API] Update Status Error: {ex.Message}");
            }
        }

        private static string ResolvePlayerIdForStats(string rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId)) return rawId;
            rawId = rawId.Trim();
            if (ObjectId.TryParse(rawId, out _)) return rawId;
            try
            {
                var byUid = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(rawId);
                if (byUid != null && byUid.Length > 0)
                    return byUid[0]._id.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTTP API] ResolvePlayerIdForStats({rawId}) failed: {ex.Message}");
            }
            // Фоллбек: поиск по display name / uid.
            try
            {
                var byName = BoltMainDatabaseProvider.Instance.FindPlayersByUidOrName(rawId, 1);
                if (byName != null && byName.Length > 0)
                    return byName[0]._id.ToString();
            }
            catch { }
            return rawId;
        }

        private string GetPlayerRankedInfoJson(string playerId, string mode)
        {
            try
            {
                var db = BoltGameDatabaseProvider.Instance;
                playerId = ResolvePlayerIdForStats(playerId);
                float mmr = 0f;
                int playedMatches = 0;
                string modeLower = (mode ?? "").ToLowerInvariant();

                if (modeLower.Contains("2v2") || modeLower.Contains("allies") || mode == "Ranked2v2")
                {
                    mmr = (float)db.GetPlayerStat(playerId, "ranked_2v2_current_mmr");
                    playedMatches = (int)db.GetPlayerStat(playerId, "ranked_2v2_played_matches");
                }
                else
                {
                    mmr = (float)db.GetPlayerStat(playerId, "ranked_current_mmr");
                    playedMatches = (int)db.GetPlayerStat(playerId, "ranked_played_matches");
                }

                if (mmr <= 0f) mmr = 1000f;
                string rankKey = modeLower.Contains("2v2") || modeLower.Contains("allies")
                    ? "ranked_2v2_rank" : "ranked_rank";
                int storedRank = (int)db.GetPlayerStat(playerId, rankKey);
                int rank;
                if (playedMatches >= AlliesMmrSystem.CalibrationMatches)
                {
                    rank = AlliesMmrSystem.RankFromMmr(mmr);
                    if (storedRank >= 0 && storedRank != rank)
                    {
                        db.SetPlayerStat(playerId, rankKey, rank);
                        string altKey = rankKey.Contains("2v2") ? "allies_rank" : "ranked_current_rank";
                        db.SetPlayerStat(playerId, altKey, rank);
                    }
                }
                else if (storedRank >= 0)
                    rank = Math.Clamp(storedRank, 0, 16);
                else
                    rank = -1;
                rank = Math.Clamp(rank, -1, AlliesMmrSystem.RankThresholds.Length - 1);
                float targetMmr = rank >= 0 ? AlliesMmrSystem.TargetMmrForRankBar(rank) : mmr;
                // Для scoreboard/FinalPlayers не отдаём -1 (пустая иконка ранга).
                // Калибровка: показываем ранг от MMR (минимум 1), прогресс калибровки — отдельно.
                int clientRank = rank >= 0
                    ? StandRiseServer.MongoDB.Main.PlayerStats.ClientRankDisplay.ToClientRankFromMmr(mmr, rank)
                    : Math.Max(1, StandRiseServer.MongoDB.Main.PlayerStats.ClientRankDisplay.ToClientRank(
                        AlliesMmrSystem.RankFromMmr(mmr)));
                if (clientRank > 17) clientRank = 17;
                if (clientRank < 1) clientRank = 1;
                int calibrationMatchesPlayed = Math.Clamp(playedMatches, 0, 10);

                string uid = "";
                string name = "";
                string avatarId = "";
                string clanTag = "";
                string clanName = "";
                try
                {
                    if (ObjectId.TryParse(playerId, out ObjectId oid))
                    {
                        var profile = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);
                        if (profile != null)
                        {
                            uid = profile.uid ?? "";
                            name = profile.name ?? "";
                            avatarId = profile.avatarId ?? "";
                            try
                            {
                                var clanDoc = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(profile);
                                if (clanDoc != null)
                                {
                                    clanTag = clanDoc.tag ?? "";
                                    clanName = clanDoc.name ?? "";
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                var data = new Dictionary<string, object>
                {
                    ["mmr"] = mmr,
                    ["target_mmr"] = targetMmr,
                    ["current_rank"] = clientRank,
                    ["calibration_match_count"] = 10,
                    ["calibration_matches_played"] = calibrationMatchesPlayed,
                    ["uid"] = uid ?? "",
                    ["name"] = name ?? "",
                    // Ник для Photon scoreboard: uid если это не ObjectId, иначе name.
                    ["nickname"] = PreferDisplayNick(uid, name),
                    ["avatar"] = avatarId ?? "",
                    ["avatarId"] = avatarId ?? "",
                    ["clan_tag"] = clanTag ?? "",
                    ["clan_name"] = clanName ?? ""
                };
                return JsonSerializer.Serialize(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTTP API] GetPlayerRankedInfo Error: {ex.Message}");
                return "{\"mmr\":1000.0,\"target_mmr\":1000.0,\"current_rank\":-1,\"calibration_match_count\":10,\"calibration_matches_played\":0}";
            }
        }

        private static string PreferDisplayNick(string uid, string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && !LooksLikeMongoObjectId(name))
                return name.Trim();
            if (!string.IsNullOrWhiteSpace(uid) && !LooksLikeMongoObjectId(uid) && uid.IndexOf('_') < 0)
                return uid.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
            return uid ?? "";
        }

        private static bool LooksLikeMongoObjectId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 24) return false;
            for (int i = 0; i < 24; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private string GetChallengesJson(string playerId)
        {
            try
            {
                var db = BoltGameDatabaseProvider.Instance;
                var challengesCol = db.GetDatabase.GetCollection<GameEventChallengeDocument>("game_event_challenge");
                var progressCol = db.GetDatabase.GetCollection<PlayerGameEventProgressDocument>("game_event_progress");

                var activeEvent = db.GetActiveGameEvent();
                string eventId = activeEvent != null ? activeEvent.code : "dragon_rise";

                var challenges = challengesCol.Find(x => x.EventId == eventId).ToList();
                var progress = progressCol.Find(x => x.PlayerId == playerId && x.EventId == eventId).FirstOrDefault();

                var resultList = new List<object>();
                foreach (var c in challenges)
                {
                    int currentPts = 0;
                    if (progress != null && progress.ChallengeProgress.TryGetValue(c.Id, out var p))
                    {
                        currentPts = p.ToInt32();
                    }

                    resultList.Add(new {
                        gameEventChallengeId = c.Id,
                        code = c.Code,
                        action = c.Action,
                        targetPoints = c.TargetPoints,
                        currentPoints = currentPts,
                        type = c.Type,
                        localizedTitle = new { @default = string.IsNullOrWhiteSpace(c.Title) ? c.Code : c.Title }
                    });
                }
                return JsonSerializer.Serialize(resultList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTTP API] GetChallenges Error: {ex.Message}");
                return "[]";
            }
        }

        private void UpdateMissionProgress(MissionProgressUpdate data)
        {
            try
            {
                var db = BoltGameDatabaseProvider.Instance;
                var challengesCol = db.GetDatabase.GetCollection<GameEventChallengeDocument>("game_event_challenge");
                var progressCol = db.GetDatabase.GetCollection<PlayerGameEventProgressDocument>("game_event_progress");

                // Раньше брали только .FirstOrDefault() — а под один и тот же action (например
                // "get_kills") подходит сразу НЕСКОЛЬКО квестов одновременно (недельный "Get 10 kills"
                // и ежедневные "Get 1 kill"/"Get 3 kills"). Обновлялся только первый найденный,
                // остальные навсегда оставались 0/N. Теперь обновляем ВСЕ совпадающие квесты.
                //
                // Квесты хранят Action как настоящий JSON под клиентский класс GameMission
                // (см. Axlebolt.Standoff.Missions.Configuration.GameMission — клиент делает
                // JsonUtility.FromJson прямо на это поле, простая строка там ловит "Invalid Mission").
                // Экшены от плагина приходят как "get_kills"/"get_kills_<Режим>"/"get_headshots_<Режим>" —
                // разбираем префикс и режим, и матчим и по содержимому JSON (IsKilled/IsHeadShot),
                // и по полю GameMode внутри этого же JSON (нет GameMode = квест без привязки к режиму).
                bool isHeadshotEvent = data.ChallengeId == "get_headshots" || data.ChallengeId.StartsWith("get_headshots_");
                bool isKillEvent = !isHeadshotEvent && (data.ChallengeId == "get_kills" || data.ChallengeId.StartsWith("get_kills_"));
                string eventMode = null;
                if (isKillEvent || isHeadshotEvent)
                {
                    string prefix = isHeadshotEvent ? "get_headshots_" : "get_kills_";
                    if (data.ChallengeId.Length > prefix.Length)
                        eventMode = data.ChallengeId.Substring(prefix.Length);
                }

                var challenges = challengesCol.Find(x => true).ToList().Where(x =>
                    x.Id == data.ChallengeId || x.Code == data.ChallengeId || x.Action == data.ChallengeId ||
                    ((isHeadshotEvent || isKillEvent) &&
                        x.Action != null &&
                        (isHeadshotEvent ? x.Action.Contains("\"IsHeadShot\"") : (x.Action.Contains("\"IsKilled\"") && !x.Action.Contains("\"IsHeadShot\""))) &&
                        ChallengeMatchesMode(x.Action, eventMode))
                ).ToList();
                if (challenges.Count == 0) return;

                int addPoints = Math.Max(0, data.Points);

                foreach (var eventGroup in challenges.GroupBy(c => c.EventId))
                {
                    string eventId = eventGroup.Key;
                    var progress = progressCol.Find(x => x.PlayerId == data.PlayerId && x.EventId == eventId).FirstOrDefault();
                    if (progress == null)
                    {
                        progress = new PlayerGameEventProgressDocument
                        {
                            PlayerId = data.PlayerId,
                            EventId = eventId,
                            Points = 0,
                            Levels = new BsonDocument { { "free", 1 }, { "premium", 0 } },
                            ChallengeProgress = new BsonDocument(),
                            UpdateDate = BsonDateTime.Create(DateTime.UtcNow)
                        };
                    }

                    foreach (var challenge in eventGroup)
                    {
                        int currentPoints = progress.ChallengeProgress.TryGetValue(challenge.Id, out var p) ? p.ToInt32() : 0;
                        int newPoints = Math.Max(0, currentPoints + addPoints);
                        progress.ChallengeProgress[challenge.Id] = newPoints;

                        bool wasCompleted = currentPoints >= challenge.TargetPoints;
                        bool isCompleted = newPoints >= challenge.TargetPoints;

                        if (isCompleted && !wasCompleted)
                        {
                            progress.Points += challenge.EventPoints;
                            // Тот же баг, что в GameEventRemoteService: уровни идут шагом 10 очков
                            // (см. hotwinterparty2023_full.json), а тут делили на 100.
                            progress.Levels["free"] = Math.Max(1, progress.Points / 10 + 1);
                        }

                        Console.WriteLine($"[HTTP API] Mission Progress: Player={data.PlayerId}, Challenge={challenge.Id} ({challenge.Action}), TotalPts={newPoints}/{challenge.TargetPoints}");
                    }

                    progress.UpdateDate = BsonDateTime.Create(DateTime.UtcNow);
                    progressCol.ReplaceOne(x => x.Id == progress.Id, progress, new global::MongoDB.Driver.ReplaceOptions { IsUpsert = true });

                    // Прогресс из матча приходит не через обычный RPC (клиент вызывает его сам из меню),
                    // а через плагин Photon -> этот HTTP-эндпоинт. Из-за этого клиенту никогда не
                    // прилетало событие onGamePassChanged, на которое подписан HUD квестов в катке —
                    // прогресс писался в базу, но UI об этом не узнавал. Пушим то же событие вручную,
                    // как это делает GameEventRemoteService.SendOnGamePassChangedEvent для обычного RPC-пути.
                    var levelsDict = new Dictionary<string, int>
                    {
                        { "free", progress.Levels.TryGetValue("free", out var freeLvl) ? freeLvl.ToInt32() : 1 },
                        { "premium", progress.Levels.TryGetValue("premium", out var premLvl) ? premLvl.ToInt32() : 0 }
                    };
                    SendGamePassChangedEventToPlayer(data.PlayerId, eventId, eventId, progress.Points, levelsDict);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTTP API] Mission Progress Error: {ex.Message}");
            }
        }

        // Достаёт поле "GameMode" из JSON в Action квеста (см. GameMission.GameMode на клиенте) и
        // сравнивает с режимом, в котором реально произошло событие. Если у квеста GameMode не
        // задан — считаем, что он не привязан к конкретному режиму, и засчитываем всегда.
        private static bool ChallengeMatchesMode(string actionJson, string eventMode)
        {
            if (string.IsNullOrWhiteSpace(actionJson)) return true;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(actionJson);
                if (doc.RootElement.TryGetProperty("GameMode", out var gm) && gm.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    string challengeMode = gm.GetString();
                    if (string.IsNullOrWhiteSpace(challengeMode)) return true;
                    if (string.IsNullOrWhiteSpace(eventMode)) return false;
                    return string.Equals(challengeMode, eventMode, StringComparison.OrdinalIgnoreCase);
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        private void SendGamePassChangedEventToPlayer(string playerId, string challengeId, string eventId, int points, Dictionary<string, int> levels)
        {
            try
            {
                if (!StandRiseServer.RpcServer.StaticClasses.UserServices.TryGetValue(playerId, out var userService) || userService == null)
                    return;

                var evt = new OnGamePassChangedEvent
                {
                    EventId = string.IsNullOrWhiteSpace(eventId) ? challengeId ?? string.Empty : eventId,
                    Points = points
                };

                if (levels != null)
                {
                    foreach (var kv in levels)
                    {
                        evt.Levels[kv.Key] = kv.Value;
                    }
                }

                userService.SendResponce(new Axlebolt.RpcSupport.Protobuf.ResponseMessage
                {
                    EventResponse = new Axlebolt.RpcSupport.Protobuf.EventResponse
                    {
                        ListenerName = "GameEventRemoteEventListener",
                        EventName = "onGamePassChanged",
                        Params = { new StandRiseServer.RpcServer.ToByteMethod(typeof(OnGamePassChangedEvent)).ToBytes(evt) }
                    }
                });

                Console.WriteLine($"[HTTP API] Pushed onGamePassChanged to player {playerId} (in-match progress).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTTP API] SendGamePassChangedEventToPlayer error: {ex.Message}");
            }
        }

        private static string ResolvePlayerDisplayName(string playerId)
        {
            try
            {
                if (ObjectId.TryParse(playerId, out ObjectId oid))
                {
                    var doc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);
                    if (doc != null)
                    {
                        if (!string.IsNullOrWhiteSpace(doc.uid)) return doc.uid;
                        if (!string.IsNullOrWhiteSpace(doc.name)) return doc.name;
                    }
                }
            }
            catch { }
            return playerId ?? "";
        }

        private async Task ProcessMatchResult(MatchResult data)
        {
            try
            {
                Console.WriteLine($"[HTTP API] Processing match result for {data.MatchId}");

                if (data.Scores == null || data.Scores.Count == 0)
                {
                    Console.WriteLine($"[HTTP API] Match result {data.MatchId} has no player scores; skipped.");
                    await Task.CompletedTask;
                    return;
                }

                bool incomplete = !data.HadLiveRound && !data.IsGiveUp;
                bool zeroZero = data.TrScore == 0 && data.CtScore == 0 && data.WinnerTeam == 0 && !data.IsGiveUp;
                if (incomplete || zeroZero)
                {
                    Console.WriteLine($"[HTTP API] Match {data.MatchId} not scored (incomplete={incomplete} 0-0={zeroZero} giveUp={data.IsGiveUp}). MMR skipped.");
                    // Историю всё равно пишем, если есть игроки с K/D или give-up —
                    // иначе UI пустой после отменённых/частичных ranked матчей.
                    string skipMode = PlayerStatsManager.NormalizeRankedGameMode(data.Mode);
                    if (skipMode == "allies" || skipMode == "ranked")
                    {
                        var skipSnaps = new Dictionary<string, RankedMatchHistoryService.RankSnapshot>(StringComparer.OrdinalIgnoreCase);
                        foreach (var sc in data.Scores.Where(s => !string.IsNullOrWhiteSpace(s.PlayerId))
                                                      .GroupBy(s => s.PlayerId).Select(g => g.First()))
                        {
                            string rid = ResolvePlayerIdForStats(sc.PlayerId);
                            if (!skipSnaps.ContainsKey(rid))
                                skipSnaps[rid] = RankedMatchHistoryService.CaptureSnapshot(rid, skipMode);
                            if (!skipSnaps.ContainsKey(sc.PlayerId))
                                skipSnaps[sc.PlayerId] = skipSnaps[rid];
                        }
                        RankedMatchHistoryService.SaveFromMatchResult(data, skipSnaps, skipMode);
                        Console.WriteLine($"[HTTP API] History saved (no MMR) for {data.MatchId} mode={skipMode} players={data.Scores.Count}");
                    }
                    try
                    {
                        var skipIds = data.Scores?.Select(s => s.PlayerId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                        if (skipIds != null && skipIds.Count > 0)
                            MatchmakingManager.ClearFinishedMatchStatus(skipIds, data.RoomId, data.MatchId);
                    }
                    catch (Exception skipClearEx)
                    {
                        Console.WriteLine($"[HTTP API] ClearFinishedMatchStatus (incomplete) failed: {skipClearEx.Message}");
                    }
                    await Task.CompletedTask;
                    return;
                }

                lock (MatchRewardLock)
                {
                    if (!RewardedMatches.Add(data.MatchId))
                    {
                        Console.WriteLine($"[HTTP API] Match result {data.MatchId} already rewarded; ensuring history only.");
                        try
                        {
                            string dupMode = PlayerStatsManager.NormalizeRankedGameMode(data.Mode);
                            if (dupMode == "allies" || dupMode == "ranked")
                            {
                                var dupSnaps = new Dictionary<string, RankedMatchHistoryService.RankSnapshot>(StringComparer.OrdinalIgnoreCase);
                                foreach (var sc in data.Scores.Where(s => !string.IsNullOrWhiteSpace(s.PlayerId))
                                                              .GroupBy(s => s.PlayerId).Select(g => g.First()))
                                {
                                    string rid = ResolvePlayerIdForStats(sc.PlayerId);
                                    if (!dupSnaps.ContainsKey(rid))
                                        dupSnaps[rid] = RankedMatchHistoryService.CaptureSnapshot(rid, dupMode);
                                    if (!dupSnaps.ContainsKey(sc.PlayerId))
                                        dupSnaps[sc.PlayerId] = dupSnaps[rid];
                                }
                                RankedMatchHistoryService.SaveFromMatchResult(data, dupSnaps, dupMode);
                            }
                        }
                        catch (Exception dupEx)
                        {
                            Console.WriteLine($"[HTTP API] Duplicate history save failed: {dupEx.Message}");
                        }
                        return;
                    }
                }

                string rankedMode = PlayerStatsManager.NormalizeRankedGameMode(data.Mode);
                var preRankSnapshots = new Dictionary<string, RankedMatchHistoryService.RankSnapshot>(StringComparer.OrdinalIgnoreCase);
                if (rankedMode == "allies" || rankedMode == "ranked")
                {
                    foreach (var score in data.Scores.Where(s => !string.IsNullOrWhiteSpace(s.PlayerId)).GroupBy(s => s.PlayerId).Select(g => g.First()))
                    {
                        string resolvedId = ResolvePlayerIdForStats(score.PlayerId);
                        if (!preRankSnapshots.ContainsKey(resolvedId))
                            preRankSnapshots[resolvedId] = RankedMatchHistoryService.CaptureSnapshot(resolvedId, rankedMode);
                        // Дублируем под сырым id: история резолвит его своим методом.
                        if (!preRankSnapshots.ContainsKey(score.PlayerId))
                            preRankSnapshots[score.PlayerId] = preRankSnapshots[resolvedId];
                    }
                }

                var db = BoltGameDatabaseProvider.Instance;
                var challengesCol = db.GetDatabase.GetCollection<GameEventChallengeDocument>("game_event_challenge");
                var progressCol = db.GetDatabase.GetCollection<PlayerGameEventProgressDocument>("game_event_progress");
                
                var activeEvent = db.GetActiveGameEvent();
                string eventId = activeEvent != null ? activeEvent.code : "hotwinterparty2023";
                
                var activeChallenges = challengesCol.Find(x => x.EventId == eventId).ToList();

                foreach (var score in data.Scores.Where(s => !string.IsNullOrWhiteSpace(s.PlayerId)).GroupBy(s => s.PlayerId).Select(g => g.First()))
                {
                    string resolvedId = ResolvePlayerIdForStats(score.PlayerId);
                    // TEAM_T=1, TEAM_CT=2. WinnerTeam совпадает с actor.team (без инверсии).
                    bool isWinner = data.WinnerTeam != 0 && score.Team == data.WinnerTeam;
                    if (!data.IsGiveUp && (data.TrScore > 0 || data.CtScore > 0) && score.Team > 0)
                    {
                        bool byScore = (score.Team == 1 && data.TrScore > data.CtScore)
                            || (score.Team == 2 && data.CtScore > data.TrScore);
                        if (data.WinnerTeam != 0 && isWinner != byScore)
                            Console.WriteLine($"[HTTP API] winner mismatch player={score.PlayerId} team={score.Team} WinnerTeam={data.WinnerTeam} byScore={byScore} → score wins");
                        isWinner = byScore;
                    }
                    // XP/drop для ranked — через StoreStats; здесь только MMR и история.
                    if (rankedMode != "allies" && rankedMode != "ranked")
                    {
                        float xpReward = isWinner ? 100f : 50f;
                        PlayerStatsManager.AddExperience(resolvedId, xpReward);
                        Console.WriteLine($"[HTTP API] Match XP: player={score.PlayerId}->{resolvedId}, match={data.MatchId}, xp={xpReward}");
                    }

                    try
                    {
                        if (rankedMode == "allies")
                        {
                            float avgScore = 1f;
                            try
                            {
                                var scores = data.Scores.Where(s => !string.IsNullOrWhiteSpace(s.PlayerId)).Select(s => s.Score).ToList();
                                if (scores.Count > 0) avgScore = (float)scores.Average();
                            }
                            catch { avgScore = Math.Max(1f, score.Score); }

                            PlayerStatsManager.ApplyAlliesMatchMmr(
                                resolvedId,
                                isWinner,
                                data.TrScore,
                                data.CtScore,
                                score.Team,
                                score.Kills,
                                score.Deaths,
                                score.Assists,
                                score.Score,
                                avgScore);

                            if (score.Kills > 0) db.IncrementPlayerStat(resolvedId, "allies_kills", score.Kills);
                            if (score.Deaths > 0) db.IncrementPlayerStat(resolvedId, "allies_deaths", score.Deaths);
                            if (score.Assists > 0) db.IncrementPlayerStat(resolvedId, "allies_assists", score.Assists);
                            if (score.Kills > 0) db.IncrementPlayerStat(resolvedId, "ranked2v2_kills", score.Kills);
                            if (score.Deaths > 0) db.IncrementPlayerStat(resolvedId, "ranked2v2_deaths", score.Deaths);
                            if (score.Assists > 0) db.IncrementPlayerStat(resolvedId, "ranked2v2_assists", score.Assists);
                        }
                        else if (rankedMode == "ranked")
                        {
                            PlayerStatsManager.ApplyRankedMatchOutcome(resolvedId, rankedMode, isWinner);
                        }
                    }
                    catch (Exception mmrEx)
                    {
                        Console.WriteLine($"[HTTP API] MMR update failed for {resolvedId}: {mmrEx.Message}");
                    }

                    // Mission progress
                    foreach (var c in activeChallenges)
                    {
                        if (c.Action == "play_match" || (c.Action == "win_match" && isWinner))
                        {
                            UpdateMissionProgress(new MissionProgressUpdate { PlayerId = resolvedId, ChallengeId = c.Id, Points = 1 });
                        }
                    }
                }

                if (rankedMode == "allies" || rankedMode == "ranked")
                {
                    var rewardsByPlayer = new Dictionary<string, PlayerStatsRemoteService.MatchRewardResult>(StringComparer.OrdinalIgnoreCase);
                    foreach (var score in data.Scores.Where(s => !string.IsNullOrWhiteSpace(s.PlayerId)).GroupBy(s => s.PlayerId).Select(g => g.First()))
                    {
                        string resolvedId = ResolvePlayerIdForStats(score.PlayerId);
                        bool won = data.WinnerTeam != 0 && score.Team == data.WinnerTeam;
                        if (!data.IsGiveUp && (data.TrScore > 0 || data.CtScore > 0) && score.Team > 0)
                        {
                            won = (score.Team == 1 && data.TrScore > data.CtScore)
                                || (score.Team == 2 && data.CtScore > data.TrScore);
                        }
                        try
                        {
                            var reward = PlayerStatsRemoteService.GrantPostMatchRewardPublic(resolvedId, data.Mode ?? rankedMode, won);
                            rewardsByPlayer[resolvedId] = reward;
                            PlayerStatsRemoteService.NotifyPostMatchRewards(resolvedId, reward);
                            try { StandRiseServer.RpcServer.Security.InventoryDupeProtection.GrantPostMatchDropCredits(resolvedId); }
                            catch { }
                        }
                        catch (Exception rewardEx)
                        {
                            Console.WriteLine($"[HTTP API] Post-match reward failed for {resolvedId}: {rewardEx.Message}");
                        }
                    }
                    RankedMatchHistoryService.SaveFromMatchResult(data, preRankSnapshots, rankedMode, rewardsByPlayer);
                }
                else
                {
                    try
                    {
                        RankedMatchHistoryService.SaveFromMatchResult(data, preRankSnapshots, rankedMode);
                        Console.WriteLine($"[HTTP API] Casual match history saved for {data.MatchId} mode={rankedMode}");
                    }
                    catch (Exception histEx)
                    {
                        Console.WriteLine($"[HTTP API] Casual history save failed: {histEx.Message}");
                    }
                }

                try
                {
                    var allPlayerIds = data.Scores?.Select(s => s.PlayerId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                    if (allPlayerIds != null && allPlayerIds.Count > 0)
                    {
                        AfkBotManager.ReleaseBotsFromMatch(allPlayerIds);
                        MatchmakingManager.ClearFinishedMatchStatus(allPlayerIds, data.RoomId, data.MatchId);
                    }
                }
                catch (Exception botEx)
                {
                    Console.WriteLine($"[HTTP API] AFK bot release failed: {botEx.Message}");
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HTTP API] Process Match Result Error: {ex.Message}");
            }
        }

        public class PlayerStatusUpdate
        {
            public string PlayerId { get; set; }
            public string RoomId { get; set; }
            public string Region { get; set; }
            public string Mode { get; set; }
            public bool IsLeaving { get; set; }
        }

        public class MatchFinished
        {
            public string MatchId { get; set; }
            public List<string> PlayerIds { get; set; }
        }

        public class MissionProgressUpdate
        {
            public string PlayerId { get; set; }
            public string ChallengeId { get; set; }
            public int Points { get; set; }
        }

        public class MatchResult
        {
            public string MatchId { get; set; }
            public string RoomId { get; set; }
            /// <summary>Карта матча (room prop C1 у Photon-плагина). Нужна истории матчей:
            /// из id комнаты (Ranked2v2_&lt;guid&gt;) название карты не вытащить.</summary>
            public string Map { get; set; }
            public int WinnerTeam { get; set; }
            public string Mode { get; set; }
            public int TrScore { get; set; }
            public int CtScore { get; set; }
            public bool IsGiveUp { get; set; }
            public bool HadLiveRound { get; set; }
            public List<PlayerScore> Scores { get; set; }
        }

        public class PlayerScore
        {
            public string PlayerId { get; set; }
            public int Kills { get; set; }
            public int Deaths { get; set; }
            public int Assists { get; set; }
            public int Score { get; set; }
            public int Team { get; set; }
        }

        public class NewsAddRequest
        {
            public string DefinitionId { get; set; }
            public string ImageUrl { get; set; }
            public string ItemText { get; set; }
            public string PlayerId { get; set; }
            public string GameVersion { get; set; }
            public string Link { get; set; }
        }
    }
}