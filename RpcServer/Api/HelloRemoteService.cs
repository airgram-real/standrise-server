using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer.Security;

namespace StandRiseServer.RpcServer.Api
{
    public class HelloRemoteService : RpcClass
    {
        private static readonly byte[] AesKey = Encoding.ASCII.GetBytes("key_abcdefghijkl");
        private static readonly byte[] AesIV = Encoding.ASCII.GetBytes("iv_abcdefghijklm");

        public HelloRemoteService(UserService user) : base(user)
        {
        }

        public override async Task InvokeAsync(RpcRequest request)
        {
            string methodName = request.MethodName.ToLowerInvariant();
            switch (methodName)
            {
                case "hello":
                    await Hello(request);
                    break;
                case "ping":
                    Ping(request.Id);
                    break;
                default:
                    Logger.Debug($"[HelloRemoteService] Unknown method: {request.MethodName}");
                    MethodNotFound(request);
                    break;
            }
        }

        public override void Invoke(RpcRequest request)
        {
            InvokeAsync(request).Wait();
        }

        private void Ping(string guid)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.Empty }
                }
            });
        }

        private async Task Hello(RpcRequest request)
        {
            Logger.Debug("[HelloRemoteService] hello - RSA key exchange");

            try
            {
                BinaryValue[] values = request.Params.ToArray();
                if (values == null || values.Length == 0 || values[0] == null || values[0].One == null)
                {
                    Logger.LogWarn("[HelloRemoteService] No params");
                    SendEncryptedEmptyResponse(request.Id);
                    return;
                }

                byte[] encryptedData = values[0].One.ToByteArray();
                Logger.Debug($"[HelloRemoteService] Encrypted data: {encryptedData.Length} bytes");

                byte[] decrypted;
                try
                {
                    decrypted = await Utils.DecryptByteAsync(encryptedData, AesKey, AesIV);
                    int dumpLen = Math.Min(decrypted.Length, 20);
                    string hex = BitConverter.ToString(decrypted, 0, dumpLen).Replace("-", " ");
                    Logger.Debug($"[HelloRemoteService] AES decrypted: {decrypted.Length} bytes, hex: {hex}");
                }
                catch (System.Exception ex)
                {
                    Logger.Error("[HelloRemoteService] AES decrypt failed: " + ex.Message);
                    SendEncryptedEmptyResponse(request.Id);
                    return;
                }

                if (!TryParseHelloPayload(decrypted, out byte[] modulusRaw, out byte[] exponentRaw, out byte[] extraData))
                {
                    Logger.LogWarn("[HelloRemoteService] Failed to parse hello payload");
                    SendEncryptedEmptyResponse(request.Id);
                    return;
                }

                byte[] modulus = NormalizeRsaModulus(modulusRaw);
                byte[] exponent = NormalizeRsaExponent(exponentRaw);

                Logger.Debug($"[HelloRemoteService] Parsed - modulus: {modulus?.Length ?? 0}B, exponent: {exponent?.Length ?? 0}B, extra: {extraData?.Length ?? 0}B");

                if (modulus != null && modulus.Length > 0 && exponent != null && exponent.Length > 0)
                {
                    RSAParameters rsaParams = new RSAParameters
                    {
                        Modulus = modulus,
                        Exponent = exponent
                    };

                    byte[] sessionKey = new byte[16];
                    using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                    {
                        rng.GetBytes(sessionKey);
                    }

                    _user.SessionAesKey = sessionKey;
                    _user.SessionAesIV = NormalizeIv(extraData);
                    _user.ClientRsaParameters = rsaParams;

                    byte[] encryptedPayload;
                    using (RSA rsa = RSA.Create())
                    {
                        rsa.ImportParameters(rsaParams);
                        encryptedPayload = rsa.Encrypt(sessionKey, RSAEncryptionPadding.Pkcs1);
                    }

                    Logger.Debug($"[HelloRemoteService] RSA encrypted {sessionKey.Length}B -> {encryptedPayload.Length}B");

                    byte[] responseProto = BuildProtobufBytes(1, encryptedPayload);
                    byte[] encryptedResponse = Utils.EncryptByte(responseProto, AesKey, AesIV);

                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = request.Id,
                            Return = new BinaryValue
                            {
                                IsNull = false,
                                One = ByteString.CopyFrom(encryptedResponse)
                            }
                        }
                    });
                    Logger.Debug("[HelloRemoteService] RSA key exchange response sent (AES encrypted)");
                    return;
                }

                try
                {
                    Handshake handshake = Handshake.Parser.ParseFrom(decrypted);
                    if (!string.IsNullOrEmpty(handshake.Ticket) && handshake.Ticket.Length < 200 && IsAsciiPrintable(handshake.Ticket))
                    {
                        await DoHandshakeAuth(handshake.Ticket, request.Id);
                        return;
                    }
                }
                catch
                {
                }

                Logger.LogWarn("[HelloRemoteService] Could not parse decrypted data");
                SendEncryptedEmptyResponse(request.Id);
            }
            catch (System.Exception ex)
            {
                Logger.Error("[HelloRemoteService] Error: " + ex.Message);
                Logger.Exception(ex);
                SendEncryptedEmptyResponse(request.Id);
            }
        }

        private static bool IsAlive(UserService service)
        {
            try { return service.IsSessionAlive(); }
            catch { return false; }
        }

        private async Task DoHandshakeAuth(string ticket, string requestId)
        {
            if (AuthSessionStore.TryGet(ticket, out Token token))
            {
                string playerId = token.playerId.ToString();

                if (MatchmakingManager.HasPendingMatch(playerId))
                {
                    Logger.Log($"[HelloRemoteService] Pending match confirm — promote encrypted channel for {playerId}");
                    StaticClasses.PromotePendingMatchPrimary(_user, playerId);
                    try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }
                    SendEncryptedEmptyResponse(requestId);
                    return;
                }

                // Этот сервис обслуживает ВТОРОЙ (игровой/encrypted) канал клиента.
                // Если основная сессия лобби ЖИВА - не убиваем её и не трогаем очередь
                // поиска: клиент открывает этот канал в момент старта матча, и разрыв
                // основной сессии давал "Переподключение к серверу" сразу после "Поиск".
                if (StaticClasses.UserServices.TryGetValue(playerId, out UserService primaryService)
                    && primaryService != null
                    && !ReferenceEquals(primaryService, _user)
                    && IsAlive(primaryService))
                {
                    Logger.Log($"[HelloRemoteService] Secondary game channel for player {playerId}: keeping existing lobby session");
                    StaticClasses.Users[_user.TcpClient] = playerId;
                    StaticClasses.RegisterUserService(playerId, _user);
                    try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }
                    Logger.Debug("[HelloRemoteService] Auth OK (Secondary Channel) for " + playerId);
                    SendEncryptedEmptyResponse(requestId);
                    return;
                }

                if (StaticClasses.UserServices.TryGetValue(playerId, out UserService mappedKeep)
                    && mappedKeep != null
                    && !ReferenceEquals(mappedKeep, _user))
                {
                    bool sockLive = false;
                    try { sockLive = mappedKeep.TcpClient != null && mappedKeep.TcpClient.Connected; } catch { }
                    if (sockLive)
                    {
                        Logger.Log($"[HelloRemoteService] Keep primary for {playerId} (sockLive=true searching={MatchmakingManager.IsSearching(playerId)})");
                        StaticClasses.Users[_user.TcpClient] = playerId;
                        StaticClasses.RegisterUserService(playerId, _user);
                        UserService.CancelPendingDisconnect(playerId);
                        try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }
                        SendEncryptedEmptyResponse(requestId);
                        return;
                    }
                }

                // Force-disconnect any existing old session for this player.
                // IMPORTANT: Unbind mappings FIRST, then ForceDisconnect.
                if (StaticClasses.UserServices.TryGetValue(playerId, out UserService oldService)
                    && oldService != null
                    && !ReferenceEquals(oldService, _user))
                {
                    bool mmActive = MatchmakingManager.IsSearching(playerId) || MatchmakingManager.HasPendingMatch(playerId);
                    bool oldLive = false;
                    try { oldLive = oldService.IsSessionAlive(); } catch { }

                    if (mmActive && oldLive)
                    {
                        Logger.Log($"[HelloRemoteService] MM-active: keep live primary for {playerId}");
                        StaticClasses.Users[_user.TcpClient] = playerId;
                        StaticClasses.RegisterUserService(playerId, _user);
                        try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }
                        SendEncryptedEmptyResponse(requestId);
                        return;
                    }

                    Logger.LogWarn($"[HelloRemoteService] Replacing session for {playerId} (mmActive={mmActive})");
                    Security.PlayerSessionRegistry.Release(playerId, oldService);
                    StaticClasses.UnregisterUserService(playerId, oldService);
                    StaticClasses.EventSenders.TryRemove(playerId, out _);
                    StaticClasses.UserServices.TryRemove(playerId, out _);
                    try
                    {
                        if (oldService.TcpClient != null)
                            StaticClasses.Users.TryRemove(oldService.TcpClient, out _);
                    }
                    catch { }
                    if (!mmActive && oldLive)
                    {
                        try { oldService.ForceDisconnect(); } catch { }
                    }
                }

                if (!Security.PlayerSessionRegistry.TryAcquire(playerId, _user))
                {
                    SendDuplicateSessionError(requestId);
                    _user.ForceDisconnect();
                    return;
                }

                _user.InitEventSenders(playerId);

                UserService.CancelPendingDisconnect(playerId);
                try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }

                bool wasInQueue = MatchmakingManager.IsSearching(playerId);
                string existingLobbyId = "";
                if (StaticClasses.PlayersStatus.TryGetValue(playerId, out var existingStatus) && existingStatus.playInGame != null)
                    existingLobbyId = existingStatus.playInGame.lobbyId;

                if (!wasInQueue)
                    MatchmakingManager.RemoveFromQueue(playerId);

                var status = new PlayerStatus
                {
                    onlineStatus = PlayerStatus.OnlineStatus.StateOnline,
                    playInGame = new PlayInGame
                    {
                        gameCode = "standoff2",
                        gameVersion = string.IsNullOrWhiteSpace(token.gameVersion) ? StaticClasses.DefaultGameVersion : token.gameVersion,
                        lobbyId = wasInQueue ? existingLobbyId : "",
                        photonGame = null
                    }
                };
                StaticClasses.PlayersStatus[playerId] = status;
                BoltMainDatabaseProvider.Instance.SetPlayerStatus(token.playerId, status);

                StaticClasses.Users[_user.TcpClient] = playerId;
                Logger.Debug("[HelloRemoteService] Auth OK for " + playerId);
                SendEncryptedEmptyResponse(requestId);
                return;
            }

            Logger.LogWarn("[HelloRemoteService] Ticket not found: " + ticket);
            SendError(requestId, 2003);
        }

        private static bool TryParseHelloPayload(byte[] decrypted, out byte[] modulus, out byte[] exponent, out byte[] extraData)
        {
            modulus = null;
            exponent = null;
            extraData = null;

            if (decrypted == null || decrypted.Length == 0)
            {
                return false;
            }

            int pos = 0;
            while (pos < decrypted.Length)
            {
                byte tagByte = decrypted[pos++];
                int fieldNumber = tagByte >> 3;
                int wireType = tagByte & 0x07;

                if (wireType == 2)
                {
                    int length = 0;
                    int shift = 0;
                    while (pos < decrypted.Length)
                    {
                        byte b = decrypted[pos++];
                        length |= (b & 0x7F) << shift;
                        if ((b & 0x80) == 0)
                        {
                            break;
                        }
                        shift += 7;
                    }

                    if (length < 0 || pos + length > decrypted.Length)
                    {
                        return false;
                    }

                    byte[] fieldData = new byte[length];
                    Buffer.BlockCopy(decrypted, pos, fieldData, 0, length);
                    pos += length;

                    if (fieldNumber == 1)
                    {
                        modulus = fieldData;
                    }
                    else if (fieldNumber == 2)
                    {
                        exponent = fieldData;
                    }
                    else if (fieldNumber == 3)
                    {
                        extraData = fieldData;
                    }
                }
                else if (wireType == 0)
                {
                    while (pos < decrypted.Length && (decrypted[pos++] & 0x80) != 0)
                    {
                    }
                }
                else
                {
                    return false;
                }
            }

            return modulus != null && exponent != null;
        }

        private static byte[] NormalizeRsaModulus(byte[] modulus)
        {
            if (modulus == null || modulus.Length == 0)
            {
                return null;
            }

            int start = 0;
            while (start < modulus.Length - 1 && modulus[start] == 0)
            {
                start++;
            }

            int len = modulus.Length - start;
            if (len <= 0)
            {
                return null;
            }

            byte[] trimmed = new byte[len];
            Buffer.BlockCopy(modulus, start, trimmed, 0, len);

            int[] valid = new[] { 64, 96, 128, 192, 256, 384, 512 };
            for (int i = 0; i < valid.Length; i++)
            {
                if (trimmed.Length == valid[i])
                {
                    return trimmed;
                }
            }

            int target = 0;
            for (int i = 0; i < valid.Length; i++)
            {
                if (trimmed.Length < valid[i])
                {
                    target = valid[i];
                    break;
                }
            }

            if (target == 0)
            {
                return trimmed;
            }

            byte[] normalized = new byte[target];
            Buffer.BlockCopy(trimmed, 0, normalized, target - trimmed.Length, trimmed.Length);
            return normalized;
        }

        private static byte[] NormalizeRsaExponent(byte[] exponent)
        {
            if (exponent == null || exponent.Length == 0)
            {
                return null;
            }

            int start = 0;
            while (start < exponent.Length - 1 && exponent[start] == 0)
            {
                start++;
            }

            int len = exponent.Length - start;
            if (len <= 0 || len > 8)
            {
                return null;
            }

            byte[] normalized = new byte[len];
            Buffer.BlockCopy(exponent, start, normalized, 0, len);
            return normalized;
        }

        private static byte[] NormalizeIv(byte[] iv)
        {
            if (iv == null || iv.Length == 0)
            {
                return (byte[])AesIV.Clone();
            }

            if (iv.Length == 16)
            {
                return iv;
            }

            byte[] normalized = new byte[16];
            int copyLen = Math.Min(iv.Length, 16);
            Buffer.BlockCopy(iv, 0, normalized, 0, copyLen);
            if (copyLen < 16)
            {
                Buffer.BlockCopy(AesIV, copyLen, normalized, copyLen, 16 - copyLen);
            }

            return normalized;
        }

        private static byte[] BuildProtobufBytes(int fieldNumber, byte[] data)
        {
            using System.IO.MemoryStream ms = new System.IO.MemoryStream();
            ms.WriteByte((byte)((fieldNumber << 3) | 2));
            int len = data.Length;
            while (len > 127)
            {
                ms.WriteByte((byte)(0x80 | (len & 0x7F)));
                len >>= 7;
            }
            ms.WriteByte((byte)len);
            ms.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        private static bool IsAsciiPrintable(string s)
        {
            foreach (char c in s)
            {
                if (c < 32 || c > 126)
                {
                    return false;
                }
            }
            return true;
        }

        private void SendEncryptedEmptyResponse(string guid)
        {
            byte[] encrypted = Utils.EncryptByte(Array.Empty<byte>(), AesKey, AesIV);
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue
                    {
                        IsNull = false,
                        One = ByteString.CopyFrom(encrypted)
                    }
                }
            });
        }

        private void SendDuplicateSessionError(string guid)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                    {
                        Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                        Code = PlayerSessionRegistry.DuplicateSessionErrorCode
                    }
                }
            });
        }
    }
}
