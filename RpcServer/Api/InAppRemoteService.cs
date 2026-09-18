using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using System;
using System.Linq;
using System.Text;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using MongoDB.Bson;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// Приватный сервер: все Google Play / App Store / Amazon / AppGallery IAP-каналы
    /// обрезаны. Клиент никогда не должен уходить в биллинг — покупки спинов/пасса
    /// идут через buyInventoryItem (голда) или тихую выдачу здесь.
    /// </summary>
    [RpcService("InAppRemoteService")]
    public class InAppRemoteService : RpcClass
    {
        public InAppRemoteService(UserService user) : base(user) { }

        public override void Invoke(RpcRequest request)
        {
            string methodName = (request.MethodName ?? string.Empty).ToLowerInvariant();
            Console.WriteLine($"[InApp] InAppRemoteService.{methodName} params={request.Params?.Count ?? 0}");
            switch (methodName)
            {
                case "getsubscribedcreator":
                case "subscribecreator":
                case "unsubscribecreator":
                case "getproducts":
                case "getcatalog":
                case "getpurchases":
                case "queryinventory":
                case "getsku":
                case "getskus":
                case "issubscribed":
                case "acknowledgepurchasedeletemarkets":
                    // Пустой успешный proto — не IsNull (клиент читает как сбой магазина).
                    SendEmptyProtoResponse(request.Id);
                    break;
                case "getgoldpacks":
                    // Раньше отдавали URL Telegram как bytes → клиент «ошибка запроса».
                    SendEmptyProtoResponse(request.Id);
                    break;
                case "buyinapp":
                case "buyinapp2":
                case "purchase":
                case "buypurchase":
                    GoogleInAppRemoteService.HandleBuyStatic(_user, request);
                    break;
                default:
                    if (methodName.Contains("buy") || methodName.Contains("purchase") || methodName.Contains("spin") || methodName.Contains("pass"))
                        GoogleInAppRemoteService.HandleBuyStatic(_user, request);
                    else
                        SendEmptyProtoResponse(request.Id);
                    break;
            }
        }

        internal static void SendSuccessNull(UserService user, string guid)
        {
            user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { IsNull = true }
                }
            });
        }

        private void SendSuccessNull(string guid) => SendSuccessNull(_user, guid);

        internal static void SendEmptyProtoResponse(UserService user, string guid)
        {
            user.SendResponce(new ResponseMessage
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
        }

        private void SendEmptyProtoResponse(string guid)
        {
            SendEmptyProtoResponse(_user, guid);
        }
    }

    [RpcService("GoogleInAppRemoteService")]
    public class GoogleInAppRemoteService : RpcClass
    {
        public GoogleInAppRemoteService(UserService user) : base(user) { }

        public override void Invoke(RpcRequest request)
        {
            // Канал Google Play полностью заглушен: каталог пустой, buy → выдача за «бесплатно».
            string methodName = (request.MethodName ?? string.Empty).ToLowerInvariant();
            Console.WriteLine($"[InApp] GoogleInAppRemoteService.{methodName} params={request.Params?.Count ?? 0} (GP stub)");
            switch (methodName)
            {
                case "buyinapp":
                case "buyinapp2":
                case "purchase":
                case "buypurchase":
                    HandleBuyStatic(_user, request);
                    break;
                case "getgoldpacks":
                case "getproducts":
                case "getcatalog":
                case "getpurchases":
                case "queryinventory":
                case "acknowledgepurchasedeletemarkets":
                case "getsku":
                case "getskus":
                    InAppRemoteService.SendEmptyProtoResponse(_user, request.Id);
                    break;
                default:
                    if (methodName.Contains("buy") || methodName.Contains("purchase") || methodName.Contains("spin") || methodName.Contains("pass"))
                        HandleBuyStatic(_user, request);
                    else
                        InAppRemoteService.SendEmptyProtoResponse(_user, request.Id);
                    break;
            }
        }

        internal static void HandleBuyStatic(UserService user, RpcRequest request)
        {
            string hint = ExtractBuyHint(request);
            Console.WriteLine($"[InApp] HandleBuy hint='{hint}'");
            if (IsGoldPassHint(hint))
                GrantItemStatic(user, request.Id, 608, 1);
            else
                GrantSpinsStatic(user, request.Id);
        }

        private static string ExtractBuyHint(RpcRequest request)
        {
            if (request?.Params == null) return "";
            var sb = new StringBuilder();
            foreach (var p in request.Params)
            {
                if (p == null || p.IsNull || p.One == null || p.One.Length == 0) continue;
                var bytes = p.One.ToByteArray();
                try
                {
                    string s = Encoding.UTF8.GetString(bytes);
                    if (!string.IsNullOrWhiteSpace(s)) sb.Append(s).Append(' ');
                }
                catch { }
                // protobuf string field often has length prefix — also try skip first byte
                if (bytes.Length > 2 && bytes[0] < bytes.Length)
                {
                    try
                    {
                        string s2 = Encoding.UTF8.GetString(bytes, 1, Math.Min(bytes.Length - 1, 200));
                        if (s2.Any(char.IsLetter)) sb.Append(s2).Append(' ');
                    }
                    catch { }
                }
            }
            return sb.ToString().ToLowerInvariant();
        }

        private static bool IsGoldPassHint(string hint)
        {
            if (string.IsNullOrEmpty(hint)) return false;
            // Не трогаем "cursed"/"spin" — иначе покупка спинов станет Gold Pass.
            return hint.Contains("gold_pass") || hint.Contains("goldpass") || hint.Contains("battlepass")
                || hint.Contains("battle_pass") || hint.Contains("halloween2021_gold")
                || hint.Contains("\"608\"") || hint.Contains(".608") || hint.Contains("608_")
                || (hint.Contains("pass") && !hint.Contains("spin"));
        }

        internal static void GrantSpinsStatic(UserService user, string guid)
        {
            GrantItemStatic(user, guid, 201, 5);
        }

        internal static void GrantItemStatic(UserService user, string guid, int itemDefinitionId, int count)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(user.TcpClient, out string playerId))
                {
                    InAppRemoteService.SendSuccessNull(user, guid);
                    return;
                }

                var db = BoltGameDatabaseProvider.Instance;
                var oid = ObjectId.Parse(playerId);
                var inv = db.GetPlayerInventoryDocument(oid);
                int nextId = (inv.InventoryItems.ElementCount > 0)
                    ? inv.InventoryItems.Select(x => int.TryParse(x.Name, out int parsed) ? parsed : 0).Max() + 1
                    : 1;

                int grantCount = Math.Max(1, count);
                for (int i = 0; i < grantCount; i++)
                {
                    db.AddItemToPlayerInventoryDocument(oid, new BoltInventoryItem
                    {
                        itemDefinitionId = itemDefinitionId,
                        quantity = 1,
                        flags = 0,
                        date = BsonDateTime.Create(DateTime.UtcNow)
                    }, nextId + i);
                }

                Console.WriteLine($"[InApp] Granted {grantCount}x #{itemDefinitionId} to {playerId}");
                InAppRemoteService.SendSuccessNull(user, guid);
                return;
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[InApp] GrantItem error: {ex.Message}");
            }

            InAppRemoteService.SendSuccessNull(user, guid);
        }

        private void HandleBuyInApp(string guid) => GrantSpinsStatic(_user, guid);
    }

    [RpcService("AdsRemoteService")]
    public class AdsRemoteService : RpcClass
    {
        public AdsRemoteService(UserService user) : base(user) { }

        public override void Invoke(RpcRequest request)
        {
            string methodName = (request.MethodName ?? string.Empty).ToLowerInvariant();

            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
            {
                switch (methodName)
                {
                    case "rewardedvideocomplete":
                    case "showrewardedvideo":
                    case "onrewardedvideocomplete":
                    case "getreward":
                    case "reward":
                    case "claimreward":
                        try
                        {
                            var db = BoltGameDatabaseProvider.Instance;
                            var oid = ObjectId.Parse(playerId);
                            var inv = db.GetPlayerInventoryDocument(oid);
                            int nextId = (inv.InventoryItems.ElementCount > 0)
                                ? inv.InventoryItems.Select(x => int.TryParse(x.Name, out int parsed) ? parsed : 0).Max() + 1
                                : 1;
                            db.AddItemToPlayerInventoryDocument(oid, new BoltInventoryItem
                            {
                                itemDefinitionId = 201,
                                quantity = 1,
                                flags = 0,
                                date = BsonDateTime.Create(DateTime.UtcNow)
                            }, nextId);
                            Console.WriteLine($"[Ads] +1 spin#201 to {playerId}");
                        }
                        catch (System.Exception ex)
                        {
                            Console.WriteLine($"[Ads] Error rewarding player {playerId}: {ex.Message}");
                        }
                        InAppRemoteService.SendSuccessNull(_user, request.Id);
                        return;
                    case "isrewardedvideoavailable":
                    case "getadsstatus":
                    case "adsavailable":
                        InAppRemoteService.SendEmptyProtoResponse(_user, request.Id);
                        return;
                    default:
                        InAppRemoteService.SendSuccessNull(_user, request.Id);
                        return;
                }
            }
            InAppRemoteService.SendSuccessNull(_user, request.Id);
        }
    }

    [RpcService("AppStoreInAppRemoteService")]
    public class AppStoreInAppRemoteService : RpcClass
    {
        public AppStoreInAppRemoteService(UserService user) : base(user) { }

        public override void Invoke(RpcRequest request)
        {
            string m = (request.MethodName ?? "").ToLowerInvariant();
            if (m.Contains("buy") || m.Contains("purchase") || m.Contains("spin") || m.Contains("pass"))
                GoogleInAppRemoteService.HandleBuyStatic(_user, request);
            else
                InAppRemoteService.SendEmptyProtoResponse(_user, request.Id);
        }
    }

    [RpcService("AmazonInAppRemoteService")]
    public class AmazonInAppRemoteService : RpcClass
    {
        public AmazonInAppRemoteService(UserService user) : base(user) { }

        public override void Invoke(RpcRequest request)
        {
            string m = (request.MethodName ?? "").ToLowerInvariant();
            if (m.Contains("buy") || m.Contains("purchase") || m.Contains("spin") || m.Contains("pass"))
                GoogleInAppRemoteService.HandleBuyStatic(_user, request);
            else
                InAppRemoteService.SendEmptyProtoResponse(_user, request.Id);
        }
    }

    [RpcService("AppGalleryInAppRemoteService")]
    public class AppGalleryInAppRemoteService : RpcClass
    {
        public AppGalleryInAppRemoteService(UserService user) : base(user) { }

        public override void Invoke(RpcRequest request)
        {
            string m = (request.MethodName ?? "").ToLowerInvariant();
            if (m.Contains("buy") || m.Contains("purchase") || m.Contains("spin") || m.Contains("pass"))
                GoogleInAppRemoteService.HandleBuyStatic(_user, request);
            else
                InAppRemoteService.SendEmptyProtoResponse(_user, request.Id);
        }
    }
}
