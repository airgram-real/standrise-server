using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("NewsFeedRemoteService")]
    public class NewsFeedRemoteService : RpcClass
    {
        public NewsFeedRemoteService(UserService user) : base(user) { }

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

        private Item[] ReadItems(int from, int count, string playerId)
        {
            var items = new List<Item>();

            try
            {
                var db = BoltMainDatabaseProvider.Instance;
                string playerVersion = StaticClasses.GetPlayerGameVersion(playerId);
                
                // Fetch more items to filter in memory
                List<NewsFeedDocument> docs = db.GetNewsFeedItems(0, 500);

                var filteredDocs = docs.Where(doc => IsVersionCompatible(playerVersion, doc.gameVersion)).ToList();
                var paginatedDocs = filteredDocs.Skip(from).Take(count > 0 ? count : 50);

                foreach (var doc in paginatedDocs)
                {
                    items.Add(new Item
                    {
                        Id = doc._id.ToString(),
                        DefinitionId = doc.definitionId ?? string.Empty,
                        PlayerId = doc.playerId ?? string.Empty,
                        ItemText = doc.itemText ?? string.Empty,
                        // Клиентские промо-карточки считают timestamp концом акции.
                        Timestamp = Math.Max(doc.timestamp, DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds())
                    });
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[NewsFeed] GetItems error: {ex.Message}");
            }

            return items.ToArray();
        }

        private void SendBinary(string guid, BinaryValue value)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = value
                }
            });
        }

        protected void GetItems(BinaryValue[] values, string guid)
        {
            if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
            {
                _user.SendResponce(Unauthorized(guid));
                return;
            }

            int from = 0;
            int count = 50;
            bool wrapperResponse = true;

            if (values != null && values.Length >= 2)
            {
                try
                {
                    from = (int)new FromByteMethod(typeof(int)).FromBytes(values[0]);
                    count = (int)new FromByteMethod(typeof(int)).FromBytes(values[1]);
                    if (count <= 0) count = 50;
                    wrapperResponse = false;
                }
                catch
                {
                    wrapperResponse = true;
                }
            }

            if (wrapperResponse && values != null && values.Length > 0 && values[0] != null && !values[0].IsNull)
            {
                try
                {
                    var req = GetItemsRequest.Parser.ParseFrom(values[0].One);
                    from = req.From;
                    count = req.Count > 0 ? req.Count : 50;
                }
                catch { }
            }

            Item[] items = ReadItems(from, count, playerId);
            if (!wrapperResponse)
            {
                SendBinary(guid, new ToByteMethod(typeof(Item[])).ToBytes(items));
                return;
            }

            var response = new GetItemsResponse();
            foreach (var item in items)
            {
                response.Items.Add(item);
            }

            SendBinary(guid, new BinaryValue { IsNull = false, One = response.ToByteString() });
        }

        protected void SendNews(BinaryValue[] values, string guid)
        {
            if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
            {
                _user.SendResponce(Unauthorized(guid));
                return;
            }

            try
            {
                string definitionId = string.Empty;

                if (values != null && values.Length > 0 && values[0] != null && !values[0].IsNull)
                {
                    try
                    {
                        var req = SendNewsRequest.Parser.ParseFrom(values[0].One);
                        definitionId = req.NewsFeedItemDefinitionId ?? string.Empty;
                    }
                    catch
                    {
                        try { definitionId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]); } catch { }
                    }
                }

                if (!string.IsNullOrWhiteSpace(definitionId))
                {
                    BoltMainDatabaseProvider.Instance.AddNewsFeedItem(definitionId, playerId);
                    Logger.Debug($"[NewsFeed] SendNews: player={playerId} definitionId={definitionId}");
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[NewsFeed] SendNews error: {ex.Message}");
            }

            var response = new SendNewsResponse();
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue
                    {
                        IsNull = false,
                        One = response.ToByteString()
                    }
                }
            });
        }

        public override void Invoke(RpcRequest request)
        {
            string methodName = (request.MethodName ?? string.Empty).ToLowerInvariant();

            switch (methodName)
            {
                case "getitems":
                case "getitems2":
                case "getnews":
                case "getfeed":
                case "getnewsfeed":
                    GetItems(request.Params.ToArray(), request.Id);
                    break;
                case "sendnews":
                    SendNews(request.Params.ToArray(), request.Id);
                    break;
                default:
                    Logger.LogWarn($"[NewsFeed] Unknown method: {request.MethodName}");
                    MethodNotFound(request);
                    break;
            }
        }
    }
}

