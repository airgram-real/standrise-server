using Axlebolt.RpcSupport.Protobuf;
using Axlebolt.Bolt.Protobuf;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer.Helpers;
using MongoDB.Bson;

namespace StandRiseServer.RpcServer.Api
{
    public class TestAuthRemoteService : RpcClass
    {
        private const string GoogleTokenInfoEndpoint = "https://oauth2.googleapis.com/tokeninfo";

        public TestAuthRemoteService(UserService user) : base(user) { }

        private void SendAuthError(string guid, int code = 2003)
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

        private void SendTicket(string guid, string ticket)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new ToByteMethod(typeof(string)).ToBytes(ticket ?? string.Empty)
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
                Logger.Log($"[TestAuth] No test account for token '{authCodeOrToken}', creating new player...");
                ObjectId objId = boltMain.CreateTestAccountWithPlayer(
                    "TA_" + new Random().Next(1000, 10000).ToString(),
                    (boltMain.GetLastId() + 1).ToString(),
                    authCodeOrToken,
                    1);

                DateTime centuryBegin = new DateTime(2001, 9, 11);
                string ticket = Utils.MD5(authCodeOrToken + (DateTime.Now.Ticks - centuryBegin.Ticks));
                AuthSessionStore.Register(ticket, new Token
                {
                    playerId = objId,
                    gameCode = string.IsNullOrWhiteSpace(gameId) ? "standoff2" : gameId,
                    gameVersion = string.IsNullOrWhiteSpace(gameVersion) ? "0.17.0" : gameVersion
                });
                Logger.Log($"[TestAuth] Created new player {objId} for token, ticket issued.");
                return ticket;
            }
        }

        private async Task<string> ResolveGoogleAndCreateTicket(string authCode, string gameId, string gameVersion)
        {
            string googleId = await ResolveGoogleIdAsync(authCode);
            if (string.IsNullOrEmpty(googleId))
                return null;

            BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
            string ticket = Utils.MD5(googleId + (DateTime.Now.Ticks - new DateTime(2001, 9, 11).Ticks));

            try
            {
                GoogleAccountDocument googleAccountDocument = boltMain.GetGoogleAccount(googleId);
                PlayerDocument playerDocument = null;
                try
                {
                    playerDocument = boltMain.GetPlayerDocument(ObjectId.Parse(googleAccountDocument.playerId));
                }
                catch (InvalidOperationException)
                {
                    boltMain.DeleteGoogleAccount(googleAccountDocument._id);
                    throw new AccountNotFoundException();
                }

                if (playerDocument.isBanned)
                {
                    Logger.LogWarn($"[TestAuth] Player {playerDocument.uid} is BANNED!");
                    return null;
                }

                AuthSessionStore.Register(ticket, new Token
                {
                    playerId = playerDocument._id,
                    gameCode = string.IsNullOrWhiteSpace(gameId) ? "standoff2" : gameId,
                    gameVersion = string.IsNullOrWhiteSpace(gameVersion) ? "0.17.0" : gameVersion
                });
                return ticket;
            }
            catch (AccountNotFoundException)
            {
                ObjectId objId = await boltMain.CreateGoogleAccountWithPlayerAsync(
                    "GP_" + new Random().Next(1000, 10000).ToString(),
                    (boltMain.GetLastId() + 1).ToString(),
                    googleId, 1,
                    new Verification(),
                    new MongoDB.Main.DeviceInfo { DeviceId = "unknown", DeviceModel = "unknown" },
                    "Creating player via TestAuth");

                AuthSessionStore.Register(ticket, new Token
                {
                    playerId = objId,
                    gameCode = string.IsNullOrWhiteSpace(gameId) ? "standoff2" : gameId,
                    gameVersion = string.IsNullOrWhiteSpace(gameVersion) ? "0.17.0" : gameVersion
                });
                return ticket;
            }
        }

        private async Task<string> ResolveGoogleIdAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length < 8)
                return null;

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                    HttpResponseMessage response = await httpClient.PostAsync(
                        GoogleTokenInfoEndpoint,
                        new FormUrlEncodedContent(new Dictionary<string, string> { ["id_token"] = token }));
                    if (response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync();
                        JObject json = JObject.Parse(body);
                        return json.Value<string>("sub");
                    }

                    response = await httpClient.PostAsync(
                        GoogleTokenInfoEndpoint,
                        new FormUrlEncodedContent(new Dictionary<string, string> { ["access_token"] = token }));
                    if (response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync();
                        JObject json = JObject.Parse(body);
                        return json.Value<string>("sub");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[TestAuth] Google resolve failed: {ex.Message}");
            }
            return null;
        }

        protected void Auth(BinaryValue[] values, string guid)
        {
            if (values == null || values.Length == 0)
            {
                SendAuthError(guid, 400);
                return;
            }

            try
            {
                FromByteMethod fromByteMethod = new FromByteMethod(typeof(AuthToken));
                AuthToken authToken = (AuthToken)fromByteMethod.FromBytes(values[0]);
                string ticket = CreateTicketFromTestAccount(authToken?.AuthCode, authToken?.GameId, authToken?.GameVersion);
                if (ticket == null)
                {
                    SendAuthError(guid);
                    return;
                }
                SendTicket(guid, ticket);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendAuthError(guid, 500);
            }
        }

        protected void EncryptedAuth(BinaryValue[] values, string guid)
        {
            if (values == null || values.Length == 0 || values[0] == null || values[0].IsNull || values[0].One == null)
            {
                SendAuthError(guid, 400);
                return;
            }

            try
            {
                string gameId = string.Empty;
                string gameVersion = string.Empty;
                string token = string.Empty;

                CodedInputStream outer = new CodedInputStream(values[0].One.ToByteArray());
                uint tag;
                while ((tag = outer.ReadTag()) != 0)
                {
                    switch (tag)
                    {
                        case 10:
                            ByteString authTestBytes = outer.ReadBytes();
                            CodedInputStream inner = new CodedInputStream(authTestBytes.ToByteArray());
                            uint innerTag;
                            while ((innerTag = inner.ReadTag()) != 0)
                            {
                                switch (innerTag)
                                {
                                    case 10:
                                        gameId = inner.ReadString();
                                        break;
                                    case 18:
                                        gameVersion = inner.ReadString();
                                        break;
                                    case 24:
                                        inner.ReadEnum();
                                        break;
                                    case 34:
                                        token = inner.ReadString();
                                        break;
                                    case 42:
                                        inner.ReadString();
                                        break;
                                    case 48:
                                        inner.ReadEnum();
                                        break;
                                    default:
                                        inner.SkipLastField();
                                        break;
                                }
                            }
                            break;
                        default:
                            outer.SkipLastField();
                            break;
                    }
                }

                string ticket = CreateTicketFromTestAccount(token, gameId, gameVersion);
                if (ticket == null)
                {
                    SendAuthError(guid);
                    return;
                }

                SendTicket(guid, ticket);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendAuthError(guid, 500);
            }
        }

        protected async Task ProtoNightAuthAsync(BinaryValue[] values, string guid)
        {
            if (values == null || values.Length == 0)
            {
                SendAuthError(guid, 400);
                return;
            }

            try
            {
                string authCode = string.Empty;
                string gameId = string.Empty;
                string gameVersion = string.Empty;

                if (values[0] != null && values[0].One != null)
                {
                    CodedInputStream outer = new CodedInputStream(values[0].One.ToByteArray());
                    uint tag;
                    while ((tag = outer.ReadTag()) != 0)
                    {
                        switch (tag)
                        {
                            case 10:
                                authCode = outer.ReadString();
                                break;
                            case 18:
                                gameId = outer.ReadString();
                                break;
                            case 26:
                                gameVersion = outer.ReadString();
                                break;
                            default:
                                outer.SkipLastField();
                                break;
                        }
                    }
                }

                Logger.Log($"[TestAuth] protoNightAuth: authCode={authCode?.Substring(0, Math.Min(20, authCode?.Length ?? 0))}..., gameId={gameId}, gameVersion={gameVersion}");

                string ticket = await ResolveGoogleAndCreateTicket(authCode, gameId, gameVersion);
                if (ticket == null)
                {
                    Logger.Log($"[TestAuth] protoNightAuth: Google resolve failed, trying test account fallback");
                    ticket = CreateTicketFromTestAccount(authCode, gameId, gameVersion);
                }

                if (ticket == null)
                {
                    Logger.Log($"[TestAuth] protoNightAuth: all auth methods failed");
                    SendAuthError(guid);
                    return;
                }
                _user.LastIssuedAuthToken = ticket;
                _user.LastIssuedAuthAtUtc = DateTime.UtcNow;
                SendTicket(guid, ticket);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendAuthError(guid, 500);
            }
        }

        protected async Task ValidateLoginAsync(BinaryValue[] values, string guid)
        {
            if (values == null || values.Length == 0)
            {
                SendAuthError(guid, 400);
                return;
            }

            try
            {
                string authCode = string.Empty;
                string gameId = string.Empty;
                string gameVersion = string.Empty;

                if (values[0] != null && values[0].One != null)
                {
                    CodedInputStream outer = new CodedInputStream(values[0].One.ToByteArray());
                    uint tag;
                    while ((tag = outer.ReadTag()) != 0)
                    {
                        switch (tag)
                        {
                            case 10:
                                authCode = outer.ReadString();
                                break;
                            case 18:
                                gameId = outer.ReadString();
                                break;
                            case 26:
                                gameVersion = outer.ReadString();
                                break;
                            default:
                                outer.SkipLastField();
                                break;
                        }
                    }
                }

                Logger.Log($"[TestAuth] validateLogin: authCode={authCode?.Substring(0, Math.Min(20, authCode?.Length ?? 0))}..., gameId={gameId}, gameVersion={gameVersion}");

                string ticket = await ResolveGoogleAndCreateTicket(authCode, gameId, gameVersion);
                if (ticket == null)
                {
                    Logger.Log($"[TestAuth] validateLogin: Google resolve failed, trying test account fallback");
                    ticket = CreateTicketFromTestAccount(authCode, gameId, gameVersion);
                }

                if (ticket == null)
                {
                    Logger.Log($"[TestAuth] validateLogin: all auth methods failed");
                    SendAuthError(guid);
                    return;
                }
                _user.LastIssuedAuthToken = ticket;
                _user.LastIssuedAuthAtUtc = DateTime.UtcNow;
                SendTicket(guid, ticket);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendAuthError(guid, 500);
            }
        }

        public override async Task InvokeAsync(RpcRequest request)
        {
            string methodName = request.MethodName ?? string.Empty;
            if (methodName.Equals("protoAuth", StringComparison.OrdinalIgnoreCase)) Auth(request.Params.ToArray(), request.Id);
            else if (methodName.Equals("protoNightAuth", StringComparison.OrdinalIgnoreCase)) await ProtoNightAuthAsync(request.Params.ToArray(), request.Id);
            else if (methodName.Equals("validateLogin", StringComparison.OrdinalIgnoreCase)) await ValidateLoginAsync(request.Params.ToArray(), request.Id);
            else if (methodName.Equals("encryptedAuth", StringComparison.OrdinalIgnoreCase)) EncryptedAuth(request.Params.ToArray(), request.Id);
            else MethodNotFound(request);
        }

        public override void Invoke(RpcRequest request)
        {
            throw new NotImplementedException();
        }
    }
}
