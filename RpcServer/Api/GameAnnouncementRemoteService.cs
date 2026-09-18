using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("GameAnnouncementRemoteService")]
    public class GameAnnouncementRemoteService : RpcClass
    {
        public GameAnnouncementRemoteService(UserService user) : base(user)
        {
        }

        private static ResponseMessage Unauthorized(string guid)
        {
            return new ResponseMessage
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
            };
        }

        private static byte[] BuildAnnouncementItemBytes(StandRiseServer.MongoDB.Main.GameAnnouncementDocument doc, string serverIp)
        {
            using MemoryStream ms = new MemoryStream();
            CodedOutputStream output = new CodedOutputStream(ms);

            output.WriteRawTag(10);
            output.WriteString(doc.code ?? string.Empty);

            output.WriteRawTag(18);
            output.WriteString(doc.title ?? string.Empty);

            output.WriteRawTag(26);
            output.WriteString(doc.text ?? string.Empty);

            string imageUrl = doc.imageUrl ?? string.Empty;
            if (imageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                // Внутренний файловый API картинок (глобальное хранилище) работает по http —
                // не «апгрейдим» его в https, иначе клиент не скачает картинку.
                if (!imageUrl.Contains("/api/news/image", StringComparison.OrdinalIgnoreCase) &&
                    Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri uri))
                {
                    if (uri.HostNameType != UriHostNameType.IPv4 && uri.HostNameType != UriHostNameType.IPv6 && uri.Host != "localhost" && uri.Host != "127.0.0.1")
                    {
                        imageUrl = "https://" + imageUrl.Substring(7);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(imageUrl) && !imageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                imageUrl = $"http://{serverIp}/{imageUrl}";
            }

            output.WriteRawTag(34);
            output.WriteString(imageUrl);

            output.WriteRawTag(40);
            output.WriteInt64(doc.timestamp);

            output.WriteRawTag(64);
            output.WriteBool(doc.active);

            output.Flush();
            return ms.ToArray();
        }

        private static byte[] BuildAnnouncementsResponseBytes(System.Collections.Generic.List<StandRiseServer.MongoDB.Main.GameAnnouncementDocument> docs, string serverIp)
        {
            using MemoryStream ms = new MemoryStream();
            CodedOutputStream output = new CodedOutputStream(ms);

            if (docs == null || docs.Count == 0)
            {
                docs = new System.Collections.Generic.List<StandRiseServer.MongoDB.Main.GameAnnouncementDocument>
                {
                    new StandRiseServer.MongoDB.Main.GameAnnouncementDocument
                    {
                        code = "announcement_welcome",
                        title = "Welcome to StandRise",
                        text = "Welcome to StandRise",
                        imageUrl = "https://github.com/solocheatso-source/MirageLite/blob/main/maxresdefault.jpg",
                        active = true,
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                };
            }

            foreach (var doc in docs)
            {
                byte[] item = BuildAnnouncementItemBytes(doc, serverIp);
                output.WriteRawTag(10);
                output.WriteBytes(ByteString.CopyFrom(item));
            }

            output.Flush();
            return ms.ToArray();
        }

        private static bool IsVersionCompatible(string playerVersion, string newsVersion)
        {
            if (string.IsNullOrWhiteSpace(newsVersion))
                return true; // No version restriction

            if (string.IsNullOrWhiteSpace(playerVersion))
                return false;

            string cleanPlayer = CleanVersionString(playerVersion);
            string cleanNews = CleanVersionString(newsVersion);

            if (Version.TryParse(cleanPlayer, out Version pVer) && Version.TryParse(cleanNews, out Version nVer))
            {
                int pMajor = pVer.Major;
                int pMinor = pVer.Minor;
                int pBuild = pVer.Build >= 0 ? pVer.Build : 0;

                int nMajor = nVer.Major;
                int nMinor = nVer.Minor;
                int nBuild = nVer.Build >= 0 ? nVer.Build : 0;

                return pMajor == nMajor && pMinor == nMinor && pBuild == nBuild;
            }

            return cleanPlayer.Equals(cleanNews, StringComparison.OrdinalIgnoreCase) || 
                   cleanPlayer.StartsWith(cleanNews, StringComparison.OrdinalIgnoreCase) ||
                   cleanNews.StartsWith(cleanPlayer, StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanVersionString(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (char c in version)
            {
                if (char.IsDigit(c) || c == '.')
                {
                    sb.Append(c);
                }
                else if (c == '_' || c == '-')
                {
                    break;
                }
            }
            return sb.ToString().TrimEnd('.');
        }

        private void SendAnnouncementsMessage(string guid, string playerId)
        {
            string serverIp = "localhost";
            try
            {
                if (_user?.TcpClient?.Client?.LocalEndPoint is System.Net.IPEndPoint localEndPoint)
                {
                    serverIp = localEndPoint.Address.ToString();
                }
            }
            catch { }

            var docs = StandRiseServer.MongoDB.BoltMainDatabaseProvider.Instance.GetGameAnnouncements();
            string playerVersion = StaticClasses.GetPlayerGameVersion(playerId);
            var filteredDocs = docs.Where(doc => IsVersionCompatible(playerVersion, doc.gameVersion)).ToList();

            // Клиентские popup: timestamp конца акции. Держим unix-ms now+30д
            // (секунды как ms дают дату 1970 и ломают таймер).
            long endTsMs = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds();
            foreach (var doc in filteredDocs)
            {
                if (doc == null) continue;
                doc.timestamp = endTsMs;
            }

            byte[] payload = BuildAnnouncementsResponseBytes(filteredDocs, serverIp);

            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue
                    {
                        IsNull = false,
                        One = ByteString.CopyFrom(payload)
                    }
                }
            });
        }

        protected void GetAnnouncements(BinaryValue[] values, string guid)
        {
            if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
            {
                _user.SendResponce(Unauthorized(guid));
                return;
            }

            SendAnnouncementsMessage(guid, playerId);
        }

        public override void Invoke(RpcRequest request)
        {
            string methodName = (request.MethodName ?? string.Empty).ToLowerInvariant();
            switch (methodName)
            {
                case "getannouncements":
                case "getgameannouncements":
                case "getallannouncements":
                    GetAnnouncements(request.Params.ToArray(), request.Id);
                    break;
                default:
                    Logger.LogWarn($"[GameAnnouncement] Unknown method: {request.MethodName}");
                    MethodNotFound(request);
                    break;
            }
        }
    }
}
