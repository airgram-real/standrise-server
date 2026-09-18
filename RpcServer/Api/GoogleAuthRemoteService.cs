using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using MongoDB.Bson;
using Newtonsoft.Json;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using StandRiseServer.RpcServer.Helpers;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;

namespace StandRiseServer.RpcServer.Api
{
    public class GoogleAuthRemoteService : RpcClass
    {
        private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string GoogleTokenInfoEndpoint = "https://oauth2.googleapis.com/tokeninfo";
        // Значения по умолчанию одного проекта Google (projectdraw-database, 296120477706).
        // Любое из них перекрывается секцией GoogleOAuth в local.settings.json без пересборки.
        private const string AndroidClientId = "296120477706-keofquf3k3kjlrpr0gu59g981mqd5sv0.apps.googleusercontent.com";
        private const string AndroidClientSecret = "";
        private const string AndroidRedirectUri = "";
        // iOS-клиент из GoogleService-Info.plist (CLIENT_ID / REVERSED_CLIENT_ID).
        // Раньше здесь стоял client_id ЧУЖОГО проекта (902562451035) — токен с iOS не проходил
        // проверку аудитории, и логин падал с RPCEXCEPTION 5555.
        private const string IosClientId = "296120477706-s43ml49ua6nnmve4vkavna3apthos5mj.apps.googleusercontent.com";
        private const string IosClientSecret = "";
        private const string IosRedirectUri = "com.googleusercontent.apps.296120477706-s43ml49ua6nnmve4vkavna3apthos5mj:/oauth2redirect";
        private const string DefaultClientId = "296120477706-ugmpjoqn2rk7mpak48kairket34e9r4n.apps.googleusercontent.com";
        private const string DefaultClientSecret = "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";
        private const string DefaultRedirectUri = "";

        private sealed class OAuthClient
        {
            public string Name;
            public string ClientId;
            public string ClientSecret;
            public string RedirectUri;
        }

        private static string Pick(string configured, string fallback)
        {
            return string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        }

        private static LocalServerConfig.GoogleOAuthSettings OAuthConfig
        {
            get
            {
                try { return LocalServerConfig.Current?.GoogleOAuth ?? new LocalServerConfig.GoogleOAuthSettings(); }
                catch { return new LocalServerConfig.GoogleOAuthSettings(); }
            }
        }

        private static OAuthClient WebClient()
        {
            var c = OAuthConfig.Web ?? new LocalServerConfig.GoogleOAuthClient();
            return new OAuthClient
            {
                Name = "web",
                ClientId = Pick(c.ClientId, DefaultClientId),
                ClientSecret = Pick(c.ClientSecret, DefaultClientSecret),
                RedirectUri = Pick(c.RedirectUri, DefaultRedirectUri)
            };
        }

        private static OAuthClient AndroidClient()
        {
            var c = OAuthConfig.Android ?? new LocalServerConfig.GoogleOAuthClient();
            return new OAuthClient
            {
                Name = "android",
                ClientId = Pick(c.ClientId, AndroidClientId),
                ClientSecret = Pick(c.ClientSecret, AndroidClientSecret),
                RedirectUri = Pick(c.RedirectUri, AndroidRedirectUri)
            };
        }

        private static OAuthClient IosClient()
        {
            var c = OAuthConfig.Ios ?? new LocalServerConfig.GoogleOAuthClient();
            return new OAuthClient
            {
                Name = "ios",
                ClientId = Pick(c.ClientId, IosClientId),
                ClientSecret = Pick(c.ClientSecret, IosClientSecret),
                RedirectUri = Pick(c.RedirectUri, IosRedirectUri)
            };
        }

        /// <summary>
        /// Имя платформы из protobuf может приходить как Ios / IOS / iPhone / Apple, а иногда
        /// вообще Unknown. Сравниваем по строке, чтобы не зависеть от имён членов enum.
        /// </summary>
        private static bool IsIosPlatform(Platform platform)
        {
            string p = platform.ToString();
            if (string.IsNullOrWhiteSpace(p)) return false;
            p = p.ToLowerInvariant();
            return p.Contains("ios") || p.Contains("iphone") || p.Contains("ipad") || p.Contains("apple");
        }

        /// <summary>
        /// Порядок клиентов для обмена authorization_code. Первым идёт клиент платформы,
        /// дальше — запасные: игра может запрашивать серверный код как на web-клиент
        /// (Android requestServerAuthCode), так и на клиент своей платформы (iOS).
        /// </summary>
        private static List<OAuthClient> BuildExchangeCandidates(Platform platform)
        {
            var list = new List<OAuthClient>();
            if (IsIosPlatform(platform))
            {
                list.Add(IosClient());
                list.Add(WebClient());
            }
            else
            {
                list.Add(WebClient());
                list.Add(AndroidClient());
                list.Add(IosClient());
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<OAuthClient>();
            foreach (var c in list)
            {
                if (c == null || string.IsNullOrWhiteSpace(c.ClientId)) continue;
                if (!seen.Add(c.ClientId)) continue;
                result.Add(c);
            }
            return result;
        }

        /// <summary>Все client_id проекта, чьи токены мы принимаем.</summary>
        private static List<string> AcceptedClientIds()
        {
            var ids = new List<string>();
            void add(string v) { if (!string.IsNullOrWhiteSpace(v)) ids.Add(v.Trim()); }
            add(WebClient().ClientId);
            add(AndroidClient().ClientId);
            add(IosClient().ClientId);
            add(DefaultClientId);
            add(AndroidClientId);
            add(IosClientId);
            var extra = OAuthConfig.ExtraAcceptedClientIds;
            if (extra != null) foreach (var e in extra) add(e);
            return ids;
        }

        public GoogleAuthRemoteService(UserService user) : base(user)
        {
        }

        private void SendAuthError(string guid, int code = 5555)
        {
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
        private string CreateTicketFromTestAccount(string authCodeOrToken, string gameId, string gameVersion)
        {
            if (string.IsNullOrWhiteSpace(authCodeOrToken))
                return null;

            BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
            try
            {
                TestAccountDocument testAccountDocument = boltMain.GetTestAccount(authCodeOrToken);
                if (testAccountDocument == null || string.IsNullOrWhiteSpace(testAccountDocument.playerId))
                    return null;

                DateTime centuryBegin = new DateTime(2001, 9, 11);
                string ticket = Utils.MD5(authCodeOrToken + (DateTime.Now.Ticks - centuryBegin.Ticks));
                AuthSessionStore.Register(ticket, new Token
                {
                    playerId = ObjectId.Parse(testAccountDocument.playerId),
                    gameCode = string.IsNullOrWhiteSpace(gameId) ? "standoff2" : gameId,
                    gameVersion = string.IsNullOrWhiteSpace(gameVersion) ? "0.17.0" : gameVersion
                });
                return ticket;
            }
            catch (AccountNotFoundException)
            {
                Logger.Log($"[GoogleAuth] No test account for token '{authCodeOrToken}', creating new player...");
                int parsedGameId = 1;
                if (!string.IsNullOrEmpty(gameId))
                    int.TryParse(gameId, out parsedGameId);
                ObjectId objId = boltMain.CreateTestAccountWithPlayer(
                    "TA_" + new Random().Next(1000, 10000).ToString(),
                    (boltMain.GetLastId() + 1).ToString(),
                    authCodeOrToken,
                    parsedGameId);

                DateTime centuryBegin = new DateTime(2001, 9, 11);
                string ticket = Utils.MD5(authCodeOrToken + (DateTime.Now.Ticks - centuryBegin.Ticks));
                AuthSessionStore.Register(ticket, new Token
                {
                    playerId = objId,
                    gameCode = string.IsNullOrWhiteSpace(gameId) ? "standoff2" : gameId,
                    gameVersion = string.IsNullOrWhiteSpace(gameVersion) ? "0.17.0" : gameVersion
                });
                Logger.Log($"[GoogleAuth] Created new player {objId} for token, ticket issued.");
                return ticket;
            }
        }
        private static string Dec(string str)
        {
            char[] characters = str.ToCharArray();
            string result = "";
            for (int i = 0; i < characters.Length; i++)
            {
                result += Char.ToString((char)(characters[i] ^ 0x023));
            }
            return result;
        }
        public override async Task InvokeAsync(RpcRequest request)
        {
            string methodName = request.MethodName.ToLowerInvariant();
            switch (methodName)
            {
                case "encryptedauth":
                    await GoogleEncryptedAuth(request.Params.ToArray(), request.Id, false);
                    break;
                case "encryptedauth2":
                    await GoogleEncryptedAuth(request.Params.ToArray(), request.Id, true);
                    break;
                case "protoauthsecured":
                case "googleprotoauthsecure":
                case "auth":
                case "googleauth":
                    await GoogleProtoAuthSecure(request.Params.ToArray(), request.Id);
                    break;
                default:
                    MethodNotFound(request);
                    break;
            }
        }
        public async Task GoogleEncryptedAuth(BinaryValue[] value1, string guid, bool preferBinaryTicket)
        {
            try
            {
                Logger.Log($"[GoogleAuth] GoogleEncryptedAuth called with guid: {guid}. Parameters count: {value1?.Length ?? 0}");
                BinaryValue[] value2 = new BinaryValue[] { new BinaryValue { IsNull = false } };
                byte[] re = null;
                bool decrypted = false;
                byte[] rawBytes = value1[0].One.ToByteArray();
                byte[] key = Encoding.ASCII.GetBytes("key_abcdefghijkl");
                byte[] IV = Encoding.ASCII.GetBytes("iv_abcdefghijklm");

                string[] keysToTry = new string[]
                {
                    _user.SessionAesKey != null ? Encoding.ASCII.GetString(_user.SessionAesKey) : null,
                    "key_abcdefghijkl"
                };
                string[] ivsToTry = new string[]
                {
                    _user.SessionAesIV != null ? Encoding.ASCII.GetString(_user.SessionAesIV) : null,
                    "iv_abcdefghijklm"
                };

                for (int i = 0; i < keysToTry.Length && !decrypted; i++)
                {
                    if (rawBytes.Length % 16 != 0)
                    {
                        Logger.Log($"[GoogleAuth] Raw data length {rawBytes.Length} is not a multiple of 16, skipping AES decryption");
                        break;
                    }
                    try
                    {
                        byte[] k = Encoding.ASCII.GetBytes(keysToTry[i]);
                        byte[] v = Encoding.ASCII.GetBytes(ivsToTry[i]);
                        re = Utils.DecryptByte(rawBytes, k, v);
                        Logger.Log($"[GoogleAuth] Successfully decrypted parameters (attempt {i}). Decrypted length: {re?.Length ?? 0}");
                        key = k;
                        IV = v;
                        decrypted = true;
                    }
                    catch (System.Exception ex)
                    {
                        Logger.Log($"[GoogleAuth] Decryption attempt {i} failed: {ex.Message}");
                    }
                }

                if (!decrypted || re == null)
                {
                    Logger.Log($"[GoogleAuth] Raw bytes length: {rawBytes.Length}, first 64 bytes: {BitConverter.ToString(rawBytes, 0, Math.Min(64, rawBytes.Length))}");

                    string extractedAuthCode = null;
                    string extractedGameId = null;
                    string extractedGameVersion = null;

                    try
                    {
                        using (var ms = new MemoryStream(rawBytes))
                        {
                            CodedInputStream input = new CodedInputStream(ms);
                            uint tag;
                            while ((tag = input.ReadTag()) != 0)
                            {
                                if (tag == 10)
                                {
                                    CodedInputStream inner = new CodedInputStream(input.ReadBytes().ToByteArray());
                                    uint innerTag;
                                    while ((innerTag = inner.ReadTag()) != 0)
                                    {
                                        if (innerTag == 10) extractedAuthCode = inner.ReadString();
                                        else if (innerTag == 18) extractedGameId = inner.ReadString();
                                        else if (innerTag == 26) extractedGameVersion = inner.ReadString();
                                        else inner.SkipLastField();
                                    }
                                }
                                else
                                {
                                    input.SkipLastField();
                                }
                            }
                        }
                        Logger.Log($"[GoogleAuth] Raw proto extracted: authCode={extractedAuthCode}, gameId={extractedGameId}, gameVersion={extractedGameVersion}");
                    }
                    catch (System.Exception ex)
                    {
                        Logger.Log($"[GoogleAuth] Raw proto parse failed: {ex.Message}");
                    }

                    if (string.IsNullOrEmpty(extractedAuthCode))
                    {
                        string fallbackTicket1 = CreateTicketFromTestAccount(rawBytes.Length > 0 ? BitConverter.ToString(rawBytes, 0, Math.Min(16, rawBytes.Length)).Replace("-", "").ToLower() : "default", null, null);
                        if (!string.IsNullOrEmpty(fallbackTicket1))
                        {
                            Logger.Log($"[GoogleAuth] Test account fallback succeeded, ticket issued.");
                            _user.LastIssuedAuthToken = fallbackTicket1;
                            _user.LastIssuedAuthAtUtc = DateTime.UtcNow;
                            _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new ToByteMethod(typeof(string)).ToBytes(fallbackTicket1) } });
                            return;
                        }
                        Logger.Error("[GoogleAuth] Could not extract auth code and test account fallback failed.");
                        SendAuthError(guid);
                        return;
                    }

                    re = null;

                    string fallbackGameId = extractedGameId;
                    string fallbackGameVersion = extractedGameVersion;
                    string fallbackTicket2 = CreateTicketFromTestAccount(extractedAuthCode, fallbackGameId, fallbackGameVersion);
                    if (string.IsNullOrEmpty(fallbackTicket2))
                    {
                        fallbackTicket2 = CreateTicketFromTestAccount("dev_" + (extractedAuthCode ?? "fallback").GetHashCode().ToString("x"), fallbackGameId, fallbackGameVersion);
                    }
                    if (!string.IsNullOrEmpty(fallbackTicket2))
                    {
                        Logger.Log($"[GoogleAuth] Fallback auth succeeded for authCode '{extractedAuthCode}', ticket issued.");
                        _user.LastIssuedAuthToken = fallbackTicket2;
                        _user.LastIssuedAuthAtUtc = DateTime.UtcNow;
                        byte[] respBytes = new ToByteMethod(typeof(string)).ToBytes(fallbackTicket2).One.ToByteArray();
                        byte[] encKey = Encoding.ASCII.GetBytes("key_abcdefghijkl");
                        byte[] encIV = Encoding.ASCII.GetBytes("iv_abcdefghijklm");
                        byte[] encResp = Utils.EncryptByte(respBytes, encKey, encIV);
                        _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(encResp) } } });
                        return;
                    }
                    Logger.Error($"[GoogleAuth] Fallback auth failed for authCode '{extractedAuthCode}'.");
                    SendAuthError(guid);
                    return;
                }

                byte[] clientModulus = null;
                byte[] clientExponent = null;
                try
                {
                    using (var ms = new MemoryStream(re))
                    {
                        CodedInputStream input = new CodedInputStream(ms);
                        uint tag;
                        while ((tag = input.ReadTag()) != 0)
                        {
                            if (tag == 18) // appVerification (tag 2)
                            {
                                byte[] verificationBytes = input.ReadBytes().ToByteArray();
                                using (var msVer = new MemoryStream(verificationBytes))
                                {
                                    CodedInputStream verInput = new CodedInputStream(msVer);
                                    uint verTag;
                                    while ((verTag = verInput.ReadTag()) != 0)
                                    {
                                        if (verTag == 58) // Modulus (tag 7)
                                        {
                                            clientModulus = verInput.ReadBytes().ToByteArray();
                                        }
                                        else if (verTag == 66) // Exponent (tag 8)
                                        {
                                            clientExponent = verInput.ReadBytes().ToByteArray();
                                        }
                                        else
                                        {
                                            verInput.SkipLastField();
                                        }
                                    }
                                }
                            }
                            else
                            {
                                input.SkipLastField();
                            }
                        }
                    }
                    if (clientModulus != null)
                    {
                        Logger.Log($"[GoogleAuth] Extracted client's public key from verification payload (Modulus length: {clientModulus.Length}B).");
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.Error($"[GoogleAuth] Failed to extract client's public key: {ex.Message}");
                }

                value2[0].One = ByteString.CopyFrom(re, 0, re.Length);
                FromByteMethod from = new FromByteMethod(typeof(GoogleAuthRequest));
                GoogleAuthRequest Val = (GoogleAuthRequest)from.FromBytes(value2[0]);
                Logger.Log($"[GoogleAuth] Parsed GoogleAuthRequest. GameVersion: {Val?.AuthGoogle?.GameVersion}, GameId: {Val?.AuthGoogle?.GameId}, AuthCode: {Val?.AuthGoogle?.AuthCode}, Platform: {Val?.AuthGoogle?.Platform}");
                
                GoogleUserOutputData googleUser = await ResolveGoogleUserAsync(Val.AuthGoogle.AuthCode, Val.AuthGoogle.Platform);
                if (googleUser == null)
                {
                    Logger.Error("[GoogleAuth] Google did not confirm this auth payload. Sending auth error.");
                    SendAuthError(guid);
                    return;
                }

                string id = googleUser.id;
                Logger.Log($"[GoogleAuth] Google user retrieved: ID={id}, Email={googleUser.email}");
                if (string.IsNullOrEmpty(id))
                {
                    Logger.Error("[GoogleAuth] Google user ID is null or empty!");
                    throw new System.Exception("Google user ID is empty");
                }

                BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                HashDocument asd = null;
                try
                {
                    Logger.Log($"[GoogleAuth] Accessing database Main to check account for ID: {id}");
                    asd = boltMain.GetHashDocument(Val.AuthGoogle.GameVersion, Val.AuthGoogle.GameId);
                    Logger.Log($"[GoogleAuth] Got HashDocument for game version: {Val.AuthGoogle.GameVersion}");
                    GoogleAccountDocument googleAccountDocument = boltMain.GetGoogleAccount(id);
                    Logger.Log($"[GoogleAuth] Google account document: PlayerId={googleAccountDocument?.playerId}");
                    DevicesInfoDocument devicesInfoDocument = null;
                    try
                    {
                        devicesInfoDocument = boltMain.GetDeviceInfoDocument(Val.DeviceInfo.DeviceId);
                        Logger.Log($"[GoogleAuth] Device info found: {devicesInfoDocument?.DeviceId}");
                    }
                    catch (DeviceInfoNotFoundException)
                    {
                        Logger.Log("[GoogleAuth] Device info not found. Creating new device info...");
                        devicesInfoDocument = boltMain.CreateDeviceInfo(new MongoDB.Main.DeviceInfo { DeviceId = Val.DeviceInfo.DeviceId, DeviceModel = Val.DeviceInfo.DeviceModel });
                    }
                    PlayerDocument playerDocument = null;
                    try 
                    { 
                        playerDocument = boltMain.GetPlayerDocument(ObjectId.Parse(googleAccountDocument.playerId)); 
                        Logger.Log($"[GoogleAuth] Player document retrieved: UID={playerDocument?.uid}, ID={playerDocument?._id}");
                    }
                    catch (InvalidOperationException)
                    {
                        Logger.LogWarn($"[GoogleEncryptedAuth] Account {id} points to missing player {googleAccountDocument.playerId}. Deleting corrupted account.");
                        boltMain.DeleteGoogleAccount(googleAccountDocument._id);
                        throw new AccountNotFoundException(); 
                    }
                    if (playerDocument.isBanned)
                    {
                        Logger.LogWarn($"[GoogleAuth] Player {playerDocument.uid} is BANNED! Sending ban response.");
                        ResponseMessage tests2 = new ResponseMessage
                        {
                            RpcResponse = new RpcResponse
                            {
                                Id = guid,
                                Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                                {
                                    Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                                    Code = 9999
                                }
                            }
                        };
                        tests2.RpcResponse.Exception.Property.Add("uid", playerDocument.uid);
                        tests2.RpcResponse.Exception.Property.Add("banCode", playerDocument.banCode);
                        tests2.RpcResponse.Exception.Property.Add("reason", playerDocument.banReason);
                        _user.SendResponce(tests2);
                        return;
                    }


                    DateTime centuryBegin2 = new DateTime(2001, 9, 11);
                    string newtoken2 = Utils.MD5(id + (DateTime.Now.Ticks - centuryBegin2.Ticks));
                    Logger.Log($"[GoogleAuth] Generated token for existing player: {newtoken2}");

                    boltMain.UpdateAccountDeviceInfo(id, new MongoDB.Main.DeviceInfo { DeviceId = Val.DeviceInfo.DeviceId, DeviceModel = Val.DeviceInfo.DeviceModel });
                    boltMain.UpdateAccountVerificationInfo(id, new Verification { ApkHash = Val.AppVerification.ApkHash, IsRooted = Val.AppVerification.IsRooted, Signature = Val.AppVerification.Signature });
                    AuthSessionStore.Register(newtoken2, new Token { playerId = playerDocument._id, gameVersion = Val.AuthGoogle.GameVersion, gameCode = Val.AuthGoogle.GameId });
                    _user.LastIssuedAuthToken = newtoken2;
                    _user.LastIssuedAuthAtUtc = DateTime.UtcNow;
                    BinaryValue responseValue = new ToByteMethod(typeof(GoogleAuthResponse)).ToBytes(CreateGoogleAuthResponse(newtoken2, preferBinaryTicket, clientModulus, clientExponent));
                    byte[] encrypted = Utils.EncryptByte(responseValue.One.ToByteArray(), key, IV);
                    responseValue.One = ByteString.CopyFrom(encrypted);

                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Return = responseValue
                        }
                    });
                    Logger.Log("[GoogleAuth] Sent success response (token generated for existing player).");
                    return;
                }
                catch (AccountNotFoundException)
                {
                    Logger.Log($"[GoogleAuth] AccountNotFoundException for ID: {id}. Creating new google account...");
                    DateTime centuryBegin2 = new DateTime(2001, 9, 11);
                    string newtoken2 = Utils.MD5(id + (DateTime.Now.Ticks - centuryBegin2.Ticks));
                    ObjectId objId = await boltMain.CreateGoogleAccountWithPlayerAsync("GP_" + new Random().Next(1000, 10000).ToString(), (boltMain.GetLastId() + 1).ToString(), id, 1, new Verification { ApkHash = Val.AppVerification.ApkHash, IsRooted = Val.AppVerification.IsRooted, Signature = Val.AppVerification.Signature }, new MongoDB.Main.DeviceInfo { DeviceId = Val.DeviceInfo.DeviceId, DeviceModel = Val.DeviceInfo.DeviceModel }, "Creating player");
                    Logger.Log($"[GoogleAuth] Generated token for new player: {newtoken2}. PlayerObjId: {objId}");

                    AuthSessionStore.Register(newtoken2, new Token { playerId = objId, gameVersion = Val.AuthGoogle.GameVersion, gameCode = Val.AuthGoogle.GameId });
                    _user.LastIssuedAuthToken = newtoken2;
                    _user.LastIssuedAuthAtUtc = DateTime.UtcNow;

                    BinaryValue responseValue = new ToByteMethod(typeof(GoogleAuthResponse)).ToBytes(CreateGoogleAuthResponse(newtoken2, preferBinaryTicket, clientModulus, clientExponent));
                    byte[] encrypted = Utils.EncryptByte(responseValue.One.ToByteArray(), key, IV);
                    responseValue.One = ByteString.CopyFrom(encrypted);

                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Return = responseValue
                        }
                    });
                    Logger.Log("[GoogleAuth] Sent success response (token generated for new player).");
                    return;
                }
            }
            
            catch (System.Exception ex4)
            {
                Logger.Exception(ex4);
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                        {
                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                            Code = 5555
                        }
                    }
                });
                return;
            }
        }
        private class Infos
        {
            public string DeviceModel { get; set; }
            public string DeviceId { get; set; }
            public string Hash { get; set; }
            public bool IsRooted { get; set; }
        }
        public async Task GoogleProtoAuthSecure(BinaryValue[] value, string guid)
        {
            try
            { 
                Console.WriteLine($"GoogleProtoAuthSecure received {value.Length} parameters");
                
                if (value.Length < 2)
                {
                    Console.WriteLine($"ERROR: Expected at least 2 parameters, got {value.Length}");
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
                
                AuthGoogle authGoogle = (AuthGoogle)new FromByteMethod(typeof(AuthGoogle)).FromBytes(value[0]);
                Console.WriteLine("authGoogle " + authGoogle.AuthCode);
                AppVerification verification = (AppVerification)new FromByteMethod(typeof(AppVerification)).FromBytes(value[1]);
                
                Console.WriteLine("DEBUG APK HASH: " + verification.ApkHash);
                Console.WriteLine("DEBUG APK SIGNATURE: " + verification.Signature);
                
                Infos verificationStr;
                string clientIp = _user.TcpClient.Client.RemoteEndPoint.ToString().Split(':')[0];

                if (value.Length >= 3)
                {
                    string info = Dec((string)new FromByteMethod(typeof(string)).FromBytes(value[2]));
                    verificationStr = JsonConvert.DeserializeObject<Infos>(info);
                }
                else
                {
                    verificationStr = new Infos
                    {
                        Hash = verification.ApkHash,
                        IsRooted = verification.IsRooted,
                        DeviceId = !string.IsNullOrEmpty(verification.Signature) ? verification.Signature : clientIp,
                        DeviceModel = "Unknown"
                    };
                    Console.WriteLine($"Using alignment mode with DeviceId: {verificationStr.DeviceId}");
                }

                var googleUser = await ResolveGoogleUserAsync(authGoogle.AuthCode, authGoogle.Platform);
                if (googleUser != null)
                {
                    string id = googleUser.id;
                    Console.WriteLine("ID: " + id);
                    if (!string.IsNullOrEmpty(id))
                    {
                        BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                        HashDocument asd = null;
                        try
                        {
                            try
                            {
                                asd = boltMain.GetHashDocument(authGoogle.GameVersion, authGoogle.GameId);
                            }
                            catch (HashDocumentNotFoundException)
                            {
                                Logger.LogWarn($"[GoogleProtoAuthSecure] Unknown version {authGoogle.GameVersion} for game {authGoogle.GameId} from account {id}");
                                _user.SendResponce(new ResponseMessage
                                {
                                    RpcResponse = new RpcResponse
                                    {
                                        Id = guid,
                                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                                        {
                                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                                            Code = 8888
                                        }
                                    }
                                });
                                return;
                            }
                            GoogleAccountDocument googleAccountDocument = boltMain.GetGoogleAccount(id);
                            DevicesInfoDocument devicesInfoDocument = null;
                            try
                            {
                                devicesInfoDocument = boltMain.GetDeviceInfoDocument(verificationStr.DeviceId);
                            }
                            catch (DeviceInfoNotFoundException)
                            {
                                devicesInfoDocument = boltMain.CreateDeviceInfo(new MongoDB.Main.DeviceInfo { DeviceId = verificationStr.DeviceId, DeviceModel = verificationStr.DeviceModel });
                            }
                            PlayerDocument playerDocument = null;
                            try 
                            { 
                                playerDocument = boltMain.GetPlayerDocument(ObjectId.Parse(googleAccountDocument.playerId)); 
                            }
                            catch (InvalidOperationException)
                            {
                                Logger.LogWarn($"[GoogleProtoAuthSecure] Account {id} points to missing player {googleAccountDocument.playerId}. Deleting corrupted account.");
                                boltMain.DeleteGoogleAccount(googleAccountDocument._id);
                                throw new AccountNotFoundException(); 
                            }
                            if (playerDocument.isBanned)
                            {
                                ResponseMessage tests2 = new ResponseMessage
                                {
                                    RpcResponse = new RpcResponse
                                    {
                                        Id = guid,
                                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                                        {
                                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                                            Code = 9999
                                        }
                                    }
                                };
                                tests2.RpcResponse.Exception.Property.Add("uid", playerDocument.uid);
                                tests2.RpcResponse.Exception.Property.Add("banCode", playerDocument.banCode);
                                tests2.RpcResponse.Exception.Property.Add("reason", playerDocument.banReason);
                                _user.SendResponce(tests2);
                                return;
                            }


                            DateTime centuryBegin2 = new DateTime(2001, 9, 11);
                            string newtoken2 = Utils.MD5(id + (DateTime.Now.Ticks - centuryBegin2.Ticks));

                            boltMain.UpdateAccountDeviceInfo(id, new MongoDB.Main.DeviceInfo { DeviceId = verificationStr.DeviceId, DeviceModel = verificationStr.DeviceModel });
                            boltMain.UpdateAccountVerificationInfo(id, new Verification { ApkHash = verificationStr.Hash, IsRooted = verificationStr.IsRooted, Signature = verification.ApkHash });
                            AuthSessionStore.Register(newtoken2, new Token { playerId = playerDocument._id, gameVersion = authGoogle.GameVersion, gameCode = authGoogle.GameId });
                            
                            BinaryValue valuee = new ToByteMethod(typeof(GoogleAuthResponse)).ToBytes(CreateGoogleAuthResponse(newtoken2, false));
                            _user.SendResponce(new ResponseMessage
                            {
                                RpcResponse = new RpcResponse
                                {
                                    Id = guid,
                                    Return = valuee
                                }
                            });
                            return;
                        }
                        catch (AccountNotFoundException)
                        {
                            DateTime centuryBegin2 = new DateTime(2001, 9, 11);
                            string newtoken2 = Utils.MD5(id + (DateTime.Now.Ticks - centuryBegin2.Ticks));
                            ObjectId objId = await boltMain.CreateGoogleAccountWithPlayerAsync("GP_" + new Random().Next(1000, 10000).ToString(), (boltMain.GetLastId() + 1).ToString(), id, 1, new Verification { ApkHash = verificationStr.Hash, IsRooted = verificationStr.IsRooted, Signature = verification.ApkHash }, new MongoDB.Main.DeviceInfo { DeviceId = verificationStr.DeviceId, DeviceModel = verificationStr.DeviceModel }, "Creating player");

                            AuthSessionStore.Register(newtoken2, new Token { playerId = objId, gameVersion = authGoogle.GameVersion, gameCode = authGoogle.GameId });
                            
                            BinaryValue valuee = new ToByteMethod(typeof(GoogleAuthResponse)).ToBytes(CreateGoogleAuthResponse(newtoken2, false));
                            _user.SendResponce(new ResponseMessage
                            {
                                RpcResponse = new RpcResponse
                                {
                                    Id = guid,
                                    Return = valuee
                                }
                            });
                            return;
                        }

                    }
                }
                else
                {
                    Logger.Error("[GoogleProtoAuthSecure] Google did not confirm this auth payload.");
                    SendAuthError(guid);
                    return;
                }
            }

            catch (System.Exception ex4)
            {
                Logger.Exception(ex4);
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                        {
                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                            Code = 5555
                        }
                    }
                });
                return;
            }
        }
        private static byte[] HexToBytes(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        private GoogleAuthResponse CreateGoogleAuthResponse(string token, bool preferBinaryTicket, byte[] clientModulus = null, byte[] clientExponent = null)
        {
            if (!preferBinaryTicket)
            {
                Logger.Log("[GoogleAuth] Sending plaintext token field.");
                return new GoogleAuthResponse
                {
                    Token = token
                };
            }

            byte[] rsaModulus = clientModulus;
            byte[] rsaExponent = clientExponent;
            
            bool hasClientRsa = false;
            RSAParameters rsaParams = default;
            
            if (rsaModulus != null && rsaModulus.Length > 0)
            {
                rsaParams = new RSAParameters
                {
                    Modulus = rsaModulus,
                    Exponent = rsaExponent ?? new byte[] { 1, 0, 1 }
                };
                hasClientRsa = true;
                Logger.Log($"[GoogleAuth] Using client public key from verification payload (Modulus={rsaModulus.Length}B).");
            }
            else if (_user.ClientRsaParameters.HasValue)
            {
                rsaParams = _user.ClientRsaParameters.Value;
                hasClientRsa = true;
                Logger.Log("[GoogleAuth] Client public key from verification not found. Falling back to cached Hello parameters.");
            }

            if (!hasClientRsa)
            {
                Logger.Log("[GoogleAuth] Client RSA key not found. Falling back to plaintext token field.");
                return new GoogleAuthResponse
                {
                    Token = token
                };
            }

            try
            {
                byte[] tokenBytes = Encoding.UTF8.GetBytes(token);

                byte[] encryptedToken = null;
                int attempts = 0;
                while (attempts < 500)
                {
                    using (var rsa = new RSACryptoServiceProvider())
                    {
                        rsa.ImportParameters(rsaParams);
                        encryptedToken = rsa.Encrypt(tokenBytes, false);
                    }
                    if (encryptedToken != null && encryptedToken.Length == 128 && encryptedToken[0] >= 0x01 && encryptedToken[0] <= 0x7F)
                    {
                        Logger.Log($"[GoogleAuth] Safe encryptedToken found on attempt {attempts + 1}. First byte: {encryptedToken[0]:X2}");
                        break;
                    }
                    attempts++;
                }

                if (encryptedToken == null || encryptedToken.Length != 128 || encryptedToken[0] < 0x01 || encryptedToken[0] > 0x7F)
                {
                    Logger.Error("[GoogleAuth] Failed to generate safe encryptedToken after 500 attempts!");
                }

                RegisterCorruptedTicketMappings(token, encryptedToken);
                Logger.Log($"[GoogleAuth] Sending Token + RSA-encrypted tokenBinary. Binary length: {encryptedToken.Length}");
                return new GoogleAuthResponse
                {
                    Token = token,
                    TokenBinary = ByteString.CopyFrom(encryptedToken)
                };
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[GoogleAuth] tokenBinary build failed: {ex.Message}. Falling back to plaintext token.");
                return new GoogleAuthResponse
                {
                    Token = token
                };
            }
        }

        private static void RegisterCorruptedTicketMappings(string token, byte[] encryptedToken)
        {
            if (StaticClasses.CiphertextToTokenMap.Count > 50000)
            {
                StaticClasses.CiphertextToTokenMap.Clear();
            }

            foreach (string candidate in BuildCorruptedTicketCandidates(encryptedToken))
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                StaticClasses.CiphertextToTokenMap[candidate] = token;
            }
        }

        private static IEnumerable<string> BuildCorruptedTicketCandidates(byte[] encryptedToken)
        {
            if (encryptedToken == null || encryptedToken.Length == 0)
            {
                yield break;
            }

            string utf8 = Encoding.UTF8.GetString(encryptedToken);
            string latin1 = Encoding.Latin1.GetString(encryptedToken);
            string ascii = Encoding.ASCII.GetString(encryptedToken);

            yield return utf8;
            yield return latin1;
            yield return ascii;
            yield return Convert.ToBase64String(encryptedToken);
            yield return BitConverter.ToString(encryptedToken);
            yield return BitConverter.ToString(encryptedToken).Replace("-", string.Empty);

            foreach (string source in new[] { utf8, latin1, ascii })
            {
                if (string.IsNullOrEmpty(source))
                {
                    continue;
                }

                int nullIndex = source.IndexOf('\0');
                if (nullIndex > 0)
                {
                    yield return source.Substring(0, nullIndex);
                }

                string withoutNulls = source.Replace("\0", string.Empty);
                if (!ReferenceEquals(withoutNulls, source))
                {
                    yield return withoutNulls;
                }
            }
        }

        private async Task<GoogleUserOutputData> ResolveGoogleUserAsync(string authCodeOrToken, Platform platform)
        {
            string normalized = NormalizeTokenCandidate(authCodeOrToken);
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            GoogleUserOutputData tokenInfoUser = await QueryTokenInfoUserAsync("id_token", normalized, platform);
            if (tokenInfoUser != null)
            {
                return tokenInfoUser;
            }

            tokenInfoUser = await QueryTokenInfoUserAsync("access_token", normalized, platform);
            if (tokenInfoUser != null)
            {
                return tokenInfoUser;
            }

            GooglePlusAccessToken exchanged = await ExchangeAuthorizationCodeAsync(normalized, platform);
            if (exchanged == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(exchanged.id_token))
            {
                tokenInfoUser = await QueryTokenInfoUserAsync("id_token", exchanged.id_token, platform);
                if (tokenInfoUser != null)
                {
                    return tokenInfoUser;
                }
            }

            if (!string.IsNullOrWhiteSpace(exchanged.access_token))
            {
                tokenInfoUser = await QueryTokenInfoUserAsync("access_token", exchanged.access_token, platform);
                if (tokenInfoUser != null)
                {
                    return tokenInfoUser;
                }

                return await Google2.getgoogleplususerdataSer(exchanged.access_token);
            }

            return null;
        }

        private async Task<GooglePlusAccessToken> ExchangeAuthorizationCodeAsync(string authCode, Platform platform)
        {
            var candidates = BuildExchangeCandidates(platform);
            if (candidates.Count == 0)
            {
                Logger.Error($"[GoogleAuth] No Google client_id configured for platform {platform}");
                return null;
            }

            string lastError = "";
            foreach (var client in candidates)
            {
                var form = new Dictionary<string, string>
                {
                    ["code"] = authCode,
                    ["client_id"] = client.ClientId,
                    ["grant_type"] = "authorization_code"
                };

                if (!string.IsNullOrWhiteSpace(client.ClientSecret))
                {
                    form["client_secret"] = client.ClientSecret;
                }

                if (!string.IsNullOrWhiteSpace(client.RedirectUri))
                {
                    form["redirect_uri"] = client.RedirectUri;
                }

                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        httpClient.Timeout = TimeSpan.FromSeconds(15);
                        HttpResponseMessage httpResponse = await httpClient.PostAsync(GoogleTokenEndpoint, new FormUrlEncodedContent(form));
                        string responseFromServer = await httpResponse.Content.ReadAsStringAsync();
                        if (httpResponse.IsSuccessStatusCode)
                        {
                            Logger.Log($"[GoogleAuth] Token exchange OK via '{client.Name}' client ({client.ClientId}) for platform {platform}");
                            return JsonConvert.DeserializeObject<GooglePlusAccessToken>(responseFromServer);
                        }

                        lastError = $"{httpResponse.StatusCode}: {responseFromServer}";
                        Logger.LogWarn($"[GoogleAuth] Token exchange via '{client.Name}' ({client.ClientId}) failed. {lastError}");
                    }
                }
                catch (System.Exception ex)
                {
                    lastError = ex.Message;
                    Logger.LogWarn($"[GoogleAuth] Token exchange via '{client.Name}' threw: {ex.Message}");
                }
            }

            Logger.Error($"[GoogleAuth] Token exchange failed on all {candidates.Count} clients for platform {platform}. Last: {lastError}");
            return null;
        }

        private async Task<GoogleUserOutputData> QueryTokenInfoUserAsync(string parameterName, string token, Platform platform)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                HttpResponseMessage response = await httpClient.PostAsync(
                    GoogleTokenInfoEndpoint,
                    new FormUrlEncodedContent(new Dictionary<string, string> { [parameterName] = token }));
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string body = await response.Content.ReadAsStringAsync();
                JObject json = JObject.Parse(body);
                string googleId = json.Value<string>("sub");
                string audience = json.Value<string>("aud");
                if (string.IsNullOrWhiteSpace(googleId) || !IsAudienceAccepted(platform, audience))
                {
                    Logger.Error($"[GoogleAuth] Tokeninfo rejected. sub={googleId}, aud={audience}, platform={platform}");
                    return null;
                }

                return new GoogleUserOutputData
                {
                    id = googleId,
                    email = json.Value<string>("email") ?? string.Empty,
                    name = json.Value<string>("name") ?? string.Empty,
                    given_name = json.Value<string>("given_name") ?? string.Empty,
                    picture = json.Value<string>("picture") ?? string.Empty
                };
            }
        }

        private static (string clientId, string clientSecret, string redirectUri) ResolveClientCredentials(Platform platform)
        {
            // Первый кандидат из списка платформы: на Android серверный код выдаётся на
            // web-клиент (requestServerAuthCode), на iOS — на iOS-клиент проекта.
            var candidates = BuildExchangeCandidates(platform);
            if (candidates.Count == 0) return (DefaultClientId, DefaultClientSecret, DefaultRedirectUri);
            var first = candidates[0];
            return (first.ClientId, first.ClientSecret, first.RedirectUri);
        }

        /// <summary>
        /// Аудитория токена. Принимаем ЛЮБОЙ client_id нашего проекта, а не только клиент
        /// текущей платформы: клиент иногда присылает Platform=Unknown, а id_token с iPhone
        /// всегда подписан на iOS-клиент. Раньше здесь был client_id чужого проекта, поэтому
        /// вход с iOS отваливался с 5555.
        /// </summary>
        private static bool IsAudienceAccepted(Platform platform, string audience)
        {
            if (string.IsNullOrWhiteSpace(audience))
            {
                return false;
            }

            string aud = audience.Trim();
            foreach (string id in AcceptedClientIds())
            {
                if (string.Equals(aud, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string NormalizeTokenCandidate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmed = value.Trim();
            if (trimmed.Length < 8 || trimmed.Length > 12288)
            {
                return null;
            }

            foreach (char ch in trimmed)
            {
                if (char.IsControl(ch))
                {
                    return null;
                }
            }

            return trimmed;
        }

        public override void Invoke(RpcRequest request)
        {
            throw new NotImplementedException();
        }

        public class GoogleUserOutputData
        {
            public string id { get; set; }
            public string name { get; set; }
            public string given_name { get; set; }
            public string email { get; set; }
            public string picture { get; set; }
        }
        public class Google2
        {
            public static async Task<GoogleUserOutputData> getgoogleplususerdataSer(string access_token)
            {
                GoogleUserOutputData result = null;
                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);
                        var urlProfile = "https://www.googleapis.com/oauth2/v1/userinfo?access_token=" + access_token;

                        HttpResponseMessage output = await client.GetAsync(urlProfile);

                        if (output.IsSuccessStatusCode)
                        {
                            string outputData = await output.Content.ReadAsStringAsync();
                            GoogleUserOutputData serStatus = JsonConvert.DeserializeObject<GoogleUserOutputData>(outputData);

                            if (serStatus != null)
                            {
                                return serStatus;
                            }
                        }
                        else
                        {
                            string errorBody = await output.Content.ReadAsStringAsync();
                            Logger.Error($"[Google2] Failed to get user info. Status: {output.StatusCode}, Body: {errorBody}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.Error($"[Google2] Exception getting user info: {ex.Message}");
                    return result;
                }
                return result;
            }
        }
        public class GooglePlusAccessToken
        {
            public string access_token { get; set; }
            public string token_type { get; set; }
            public int expires_in { get; set; }
            public string id_token { get; set; }
            public string refresh_token { get; set; }
        }
    }

}
