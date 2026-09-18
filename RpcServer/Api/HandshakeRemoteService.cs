using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.MongoDB;
using MongoDB.Bson;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.RpcServer.Security;

namespace StandRiseServer.RpcServer.Api
{
    public class HandshakeRemoteService : RpcClass
    {
        public HandshakeRemoteService(UserService user) : base(user)
        {
        }

        private void SendEmptyMessageResponse(string guid)
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

        private void SendWhitelistError(string guid)
        {
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
        }

        private static bool IsAlive(UserService service)
        {
            try { return service.IsSessionAlive(); }
            catch { return false; }
        }

        private bool TryCompleteHandshake(string playerId, Token val, string guid, out bool duplicateBlocked, bool secondaryChannel = false)
        {
            duplicateBlocked = false;

            if (!GameWhitelist.IsPlayerAllowed(val.playerId))
            {
                Logger.LogWarn($"[Handshake] Whitelist blocked player {playerId}");
                SendWhitelistError(guid);
                duplicateBlocked = true;
                return false;
            }

            // После onMatchmakingDone клиент открывает второй TCP — primary умирает.
            // Новый канал ОБЯЗАН стать primary, иначе 1–2 из 4 видят «ИГРА НАЙДЕНА».
            // Клиент при старте Allies/2v2 часто открывает ВТОРОЕ TCP-соединение и шлёт
            // обычный handshake (не encrypted). Раньше secondaryChannel=true ставился только
            // для encryptedHandshake, а plain handshake убивал живую лобби-сессию →
            // "отключило от сервера" сразу после запуска союзников.
            //
            // Если у игрока уже есть ЖИВАЯ primary-сессия — любое новое соединение
            // (plain или encrypted) считаем вторичным игровым каналом и НЕ трогаем primary.
            //
            // ВАЖНО: НЕ заменяем FlowEventSender для вторичного канала!
            // Флоу-события (onMatchmakingProgress/Done/Fail) всегда идут через primary
            // UserService, который остаётся живым на протяжении всей сессии.
            bool hasLivePrimary =
                StaticClasses.UserServices.TryGetValue(playerId, out UserService primaryService)
                && primaryService != null
                && !ReferenceEquals(primaryService, _user)
                && IsAlive(primaryService);

            if (MatchmakingManager.HasPendingMatch(playerId) && hasLivePrimary)
            {
                Logger.Log($"[Handshake] Pending confirm — secondary TCP, keep lobby primary for {playerId}");
                StaticClasses.SetMatchmakingFlowChannel(playerId, _user);
                StaticClasses.Users[_user.TcpClient] = playerId;
                StaticClasses.RegisterUserService(playerId, _user);
                UserService.CancelPendingDisconnect(playerId);
                try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }
                return true;
            }

            if (MatchmakingManager.HasPendingMatch(playerId))
            {
                Logger.Log($"[Handshake] Pending match confirm — promote this channel for {playerId}");
                StaticClasses.PromotePendingMatchPrimary(_user, playerId);
                try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }
                return true;
            }

            if ((secondaryChannel || hasLivePrimary) && hasLivePrimary)
            {
                Logger.Log($"[Handshake] Secondary game channel for player {playerId}: keeping primary FlowEventSender (viaEncrypted={secondaryChannel})");
                StaticClasses.Users[_user.TcpClient] = playerId;
                // Только в AllUserServices — НЕ трогаем UserServices/EventSenders (primary).
                StaticClasses.RegisterUserService(playerId, _user);
                UserService.CancelPendingDisconnect(playerId);
                try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }

                Logger.Debug("User Handshake Successful (Secondary Channel)!");
                return true;
            }

            // Если primary ещё в маппинге, но сокет уже мёртв — не «убиваем» его ради
            // Allies secondary с plain handshake: сначала пробуем secondary-путь, только если
            // новый канал помечен secondaryChannel ИЛИ primary явно мёртв.
            bool hasMappedPrimary =
                StaticClasses.UserServices.TryGetValue(playerId, out primaryService)
                && primaryService != null
                && !ReferenceEquals(primaryService, _user);

            if (hasMappedPrimary && secondaryChannel)
            {
                Logger.Log($"[Handshake] Secondary channel (mapped primary may be idle) for {playerId}: keep primary mappings");
                StaticClasses.Users[_user.TcpClient] = playerId;
                StaticClasses.RegisterUserService(playerId, _user);
                UserService.CancelPendingDisconnect(playerId);
                try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }
                Logger.Debug("User Handshake Successful (Secondary Channel)!");
                return true;
            }

            // Не убиваем живой primary, если сокет ещё Connected — это второй TCP
            // (Allies / Photon) или handshake во время поиска/confirm.
            if (StaticClasses.UserServices.TryGetValue(playerId, out UserService mappedKeep)
                && mappedKeep != null
                && !ReferenceEquals(mappedKeep, _user))
            {
                bool sockLive = false;
                try { sockLive = mappedKeep.TcpClient != null && mappedKeep.TcpClient.Connected; } catch { }
                // Только ЖИВОЙ primary держим. Если сокет мёртв, а игрок ещё в поиске/
                // «ИГРА НАЙДЕНА» — обязаны повысить этот handshake до primary, иначе
                // Done/Confirm уходят в мёртвый канал (1 из 4 видит уведомление).
                if (sockLive)
                {
                    Logger.Log($"[Handshake] Keep primary for {playerId} (sockLive=true searching={MatchmakingManager.IsSearching(playerId)})");
                    StaticClasses.Users[_user.TcpClient] = playerId;
                    StaticClasses.RegisterUserService(playerId, _user);
                    UserService.CancelPendingDisconnect(playerId);
                    try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }
                    return true;
                }
            }

            // Force-disconnect any existing old session for this player.
            // IMPORTANT: We MUST first remove UserServices/EventSenders mappings BEFORE
            // calling ForceDisconnect, because ForceDisconnect triggers SaveAndRemoveUser
            // which checks isCurrentSession (UserServices[playerId] == this). If we don't
            // remove the mapping first, SaveAndRemoveUser sees isCurrentSession=true and
            // removes the player from the lobby.
            // NOTE: We do NOT remove PlayersStatus — the new session inherits
            // the lobby association and matchmaking state from the old session.
            if (StaticClasses.UserServices.TryGetValue(playerId, out UserService oldService)
                && oldService != null
                && !ReferenceEquals(oldService, _user))
            {
                bool mmActive = MatchmakingManager.IsSearching(playerId) || MatchmakingManager.HasPendingMatch(playerId);
                bool oldLive = false;
                try { oldLive = oldService.IsSessionAlive(); } catch { }

                if (mmActive && oldLive)
                {
                    Logger.Log($"[Handshake] MM-active: keep live primary for {playerId}");
                    StaticClasses.Users[_user.TcpClient] = playerId;
                    StaticClasses.RegisterUserService(playerId, _user);
                    UserService.CancelPendingDisconnect(playerId);
                    try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }
                    return true;
                }

                Logger.LogWarn($"[Handshake] Replacing session for player {playerId} (mmActive={mmActive} oldLive={oldLive})");

                PlayerSessionRegistry.Release(playerId, oldService);
                StaticClasses.UnregisterUserService(playerId, oldService);
                StaticClasses.EventSenders.TryRemove(playerId, out _);
                StaticClasses.UserServices.TryRemove(playerId, out _);
                try
                {
                    if (oldService.TcpClient != null)
                        StaticClasses.Users.TryRemove(oldService.TcpClient, out _);
                }
                catch { }

                try { oldService.ForceDisconnect(); } catch { }
            }

            if (!PlayerSessionRegistry.TryAcquire(playerId, _user))
            {
                Logger.LogWarn($"[Handshake] Session acquire failed for player {playerId}");
                SendDuplicateSessionError(guid);
                duplicateBlocked = true;
                return false;
            }

            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string _))
            {
                Logger.LogWarn($"[Handshake] Player {playerId} already tracked for this socket. Resetting state anyway.");
            }

            _user.InitEventSenders(playerId);
            StaticClasses.DisconnectOtherPlayerSessions(playerId, _user);

            // Cancel grace period if player reconnected during disconnect grace
            UserService.CancelPendingDisconnect(playerId);
            // Allies: после secondary reconnect снова показать «ИГРА НАЙДЕНА»
            try { MatchmakingManager.ResendPendingMatchFound(playerId); } catch { }

            // Preserve matchmaking queue entry if player was searching
            bool wasInQueue = MatchmakingManager.IsSearching(playerId);
            string existingLobbyId = "";
            string existingLobbyName = "";
            if (StaticClasses.PlayersStatus.TryGetValue(playerId, out var existingStatus) && existingStatus.playInGame != null)
            {
                existingLobbyId = existingStatus.playInGame.lobbyId ?? "";
                existingLobbyName = existingStatus.playInGame.lobbyName ?? "";
            }

            if (!wasInQueue)
                MatchmakingManager.RemoveFromQueue(playerId);

            PlayInGame playPresence;
            if (wasInQueue)
            {
                playPresence = new RpcServer.PlayInGame
                {
                    gameCode = "standoff2",
                    gameVersion = string.IsNullOrWhiteSpace(val.gameVersion) ? StaticClasses.DefaultGameVersion : val.gameVersion,
                    lobbyId = existingLobbyId,
                    lobbyName = existingLobbyName,
                    photonGame = null
                };
            }
            else
            {
                playPresence = new RpcServer.PlayInGame
                {
                    gameCode = "standoff2",
                    gameVersion = string.IsNullOrWhiteSpace(val.gameVersion) ? StaticClasses.DefaultGameVersion : val.gameVersion,
                    lobbyId = "",
                    photonGame = null
                };
            }

            var status = new RpcServer.PlayerStatus
            {
                onlineStatus = RpcServer.PlayerStatus.OnlineStatus.StateOnline,
                playInGame = playPresence
            };
            StaticClasses.PlayersStatus[playerId] = status;
            BoltMainDatabaseProvider.Instance.SetPlayerStatus(val.playerId, status);
            StaticClasses.Users[_user.TcpClient] = playerId;
            Logger.Debug("User Handshake Successful (State Reset)!");
            return true;
        }

        public void ProtoHandshake(BinaryValue[] value, string guid)
        {
            FromByteMethod from = new FromByteMethod(typeof(Handshake));
            Handshake Val = (Handshake)from.FromBytes(value[0]);
            string ticket = Val.Ticket;
            if (StaticClasses.CiphertextToTokenMap.TryGetValue(ticket, out string realToken))
            {
                ticket = realToken;
            }
            if (AuthSessionStore.TryGet(ticket, out Token val))
            {
                string playerId = val.playerId.ToString();
                if (!TryCompleteHandshake(playerId, val, guid, out bool duplicateBlocked))
                {
                    if (duplicateBlocked)
                    {
                        _user.ForceDisconnect();
                    }
                    return;
                }

                SendEmptyMessageResponse(guid);
            }
            else
            {
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Exception = new Axlebolt.RpcSupport.Protobuf.Exception
                        {
                            Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                            Code = 2003
                        }
                    }
                });
            }
        }

        public async Task EncryptedHandshake(BinaryValue[] value, string guid)
        {
            try
            {
                Handshake Val = null;
                byte[] data = value[0].One.ToByteArray();
                Logger.Log($"[Handshake] Raw data length: {data.Length}, first 64 bytes: {BitConverter.ToString(data.Take(Math.Min(64, data.Length)).ToArray()).Replace("-", " ")}");

                byte[] key = _user.SessionAesKey ?? Encoding.ASCII.GetBytes("key_abcdefghijkl");
                byte[] IV = _user.SessionAesIV ?? Encoding.ASCII.GetBytes("iv_abcdefghijklm");
                Logger.Debug($"[Handshake] Decrypting handshake. Key length: {key.Length}, IV length: {IV.Length}");

                byte[] handshakeBytes = null;
                try
                {
                    byte[] handshake = await Utils.DecryptByteAsync(data, key, IV);
                    handshakeBytes = handshake;
                    Val = Handshake.Parser.ParseFrom(handshake);
                    Logger.Log("[Handshake] Decrypted successfully with session key");
                }
                catch (System.Exception ex)
                {
                    Logger.Log($"[Handshake] Decryption with session key failed: {ex.Message}. Trying fallback default key...");
                    try
                    {
                        byte[] defaultKey = Encoding.ASCII.GetBytes("key_abcdefghijkl");
                        byte[] defaultIV = Encoding.ASCII.GetBytes("iv_abcdefghijklm");
                        byte[] handshake = await Utils.DecryptByteAsync(data, defaultKey, defaultIV);
                        handshakeBytes = handshake;
                        Val = Handshake.Parser.ParseFrom(handshake);
                        Logger.Log("[Handshake] Decrypted successfully with default keys");
                    }
                    catch (System.Exception fallbackEx)
                    {
                        Logger.Log($"[Handshake] Decryption with default key also failed: {fallbackEx.Message}");
                        try
                        {
                            handshakeBytes = data;
                            Val = Handshake.Parser.ParseFrom(data);
                            Logger.Log("[Handshake] Parsed as raw proto instead");
                        }
                        catch
                        {
                            Logger.Log("[Handshake] Raw proto parse also failed. Attempting to extract ticket from raw bytes...");
                            try
                            {
                                string extractedTicket = TryExtractTicketFromRawBytes(data);
                            if (!string.IsNullOrEmpty(extractedTicket))
                                    {
                                        Logger.Log($"[Handshake] Extracted ticket from raw bytes: '{extractedTicket}'");
                                        Val = new Handshake { Ticket = extractedTicket };
                                        handshakeBytes = data;
                                    }
                                    else if (!string.IsNullOrEmpty(_user.LastIssuedAuthToken) && (DateTime.UtcNow - _user.LastIssuedAuthAtUtc).TotalSeconds < 30)
                                    {
                                        Logger.Log($"[Handshake] Using last issued auth token as fallback: '{_user.LastIssuedAuthToken}'");
                                        Val = new Handshake { Ticket = _user.LastIssuedAuthToken };
                                        handshakeBytes = data;
                                    }
                                    else
                                    {
                                        Logger.Log("[Handshake] Could not extract ticket from raw bytes. Sending error.");
                                        SendError(guid, 2003);
                                        return;
                                    }
                            }
                            catch
                            {
                                Logger.Log("[Handshake] All decryption/parse methods failed. Sending error.");
                                SendError(guid, 2003);
                                return;
                            }
                        }
                    }
                }

                if (Val == null)
                {
                    Logger.LogWarn("[Handshake] Parsed Handshake message is null!");
                    SendError(guid, 2003);
                    return;
                }
                byte[] rawTicket = null;
                Logger.Log($"[Handshake] Parsed Handshake ticket: '{Val.Ticket}' (Length: {Val.Ticket?.Length ?? 0})");
                if (Val.Ticket != null)
                {
                    try
                    {
                        byte[] utf8Bytes = Encoding.UTF8.GetBytes(Val.Ticket);
                        Logger.Log($"[Handshake] Parsed Ticket UTF8 Hex: {BitConverter.ToString(utf8Bytes).Replace("-", " ")}");
                    }
                    catch (System.Exception ex)
                    {
                        Logger.Error($"[Handshake] Failed to get ticket UTF8 bytes: {ex.Message}");
                    }
                }
                if (handshakeBytes != null)
                {
                    try
                    {
                        // Extract raw bytes of field 1 (ticket) from decrypted handshake
                        if (handshakeBytes.Length > 2 && handshakeBytes[0] == 0x0A)
                        {
                            int len = 0;
                            int shift = 0;
                            int idx = 1;
                            while (idx < handshakeBytes.Length)
                            {
                                byte b = handshakeBytes[idx++];
                                len |= (b & 0x7F) << shift;
                                if ((b & 0x80) == 0) break;
                                shift += 7;
                            }
                            if (idx + len <= handshakeBytes.Length)
                            {
                                rawTicket = new byte[len];
                                Buffer.BlockCopy(handshakeBytes, idx, rawTicket, 0, len);
                            }
                        }
                        if (rawTicket != null)
                        {
                            Logger.Log($"[Handshake] Raw ticket bytes HEX: {BitConverter.ToString(rawTicket).Replace("-", " ")}");
                            string rawTicketString = Encoding.UTF8.GetString(rawTicket);
                            Logger.Log($"[Handshake] Raw ticket string: '{rawTicketString}'");
                        }
                        else
                        {
                            Logger.Log("[Handshake] Could not extract raw ticket bytes from handshakeBytes");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Logger.Error($"[Handshake] Error parsing raw ticket bytes: {ex.Message}");
                    }
                }
                if (string.IsNullOrEmpty(Val.Ticket))
                {
                    SendError(guid, 2003);
                    return;
                }

                string ticket = Val.Ticket;
                if (TryResolveCorruptedTicket(ticket, rawTicket, out string resolvedToken))
                {
                    if (!string.Equals(ticket, resolvedToken, StringComparison.Ordinal))
                    {
                        Logger.Log($"[Handshake] Resolved ticket to real token: '{ticket}' -> '{resolvedToken}'");
                    }
                    ticket = resolvedToken;
                }

                Logger.Log($"[Handshake] Registered tokens: {StaticClasses.Tokens.Count}");
                if (AuthSessionStore.TryGet(ticket, out Token val))
                {
                    string playerId = val.playerId.ToString();
                    if (!TryCompleteHandshake(playerId, val, guid, out bool duplicateBlocked, secondaryChannel: true))
                    {
                        if (duplicateBlocked)
                        {
                            _user.ForceDisconnect();
                        }
                        return;
                    }

                    Logger.Debug("User Handshake Successful (Encrypted, State Reset)!");
                    SendEmptyMessageResponse(guid);
                }
                else
                {
                    if (!string.IsNullOrEmpty(_user.LastIssuedAuthToken) && (DateTime.UtcNow - _user.LastIssuedAuthAtUtc).TotalSeconds < 60)
                    {
                        Logger.Log($"[Handshake] Ticket '{ticket}' not in Tokens. Falling back to LastIssuedAuthToken: '{_user.LastIssuedAuthToken}'");
                        if (AuthSessionStore.TryGet(_user.LastIssuedAuthToken, out Token fallbackVal))
                        {
                            string playerId = fallbackVal.playerId.ToString();
                            if (!TryCompleteHandshake(playerId, fallbackVal, guid, out bool duplicateBlocked, secondaryChannel: true))
                            {
                                if (duplicateBlocked)
                                {
                                    _user.ForceDisconnect();
                                }
                                return;
                            }

                            Logger.Debug("User Handshake Successful via fallback token!");
                            SendEmptyMessageResponse(guid);
                            return;
                        }
                    }

                    Logger.Log($"[Handshake] Ticket '{ticket}' not found in any token store. Sending error.");
                    SendError(guid, 2003);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private static IEnumerable<string> EnumerateTicketCandidates(string ticket, byte[] rawTicket)
        {
            if (!string.IsNullOrEmpty(ticket))
            {
                yield return ticket;
                yield return ticket.TrimEnd('\0');
                yield return ticket.Replace("\0", string.Empty);

                int nullIndex = ticket.IndexOf('\0');
                if (nullIndex > 0)
                {
                    yield return ticket.Substring(0, nullIndex);
                }

                byte[] ticketUtf8 = Encoding.UTF8.GetBytes(ticket);
                yield return Convert.ToBase64String(ticketUtf8);
                yield return BitConverter.ToString(ticketUtf8);
                yield return BitConverter.ToString(ticketUtf8).Replace("-", string.Empty);
            }

            if (rawTicket != null && rawTicket.Length > 0)
            {
                yield return Encoding.UTF8.GetString(rawTicket);
                yield return Encoding.Latin1.GetString(rawTicket);
                yield return Encoding.ASCII.GetString(rawTicket);
                yield return Convert.ToBase64String(rawTicket);
                yield return BitConverter.ToString(rawTicket);
                yield return BitConverter.ToString(rawTicket).Replace("-", string.Empty);
            }
        }

        private static bool TryMapTicket(string candidate, out string resolvedToken)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                resolvedToken = null;
                return false;
            }

            if (StaticClasses.CiphertextToTokenMap.TryGetValue(candidate, out resolvedToken))
            {
                return true;
            }

            resolvedToken = null;
            return false;
        }

        private bool TryResolveCorruptedTicket(string ticket, byte[] rawTicket, out string resolvedToken)
        {
            foreach (string candidate in EnumerateTicketCandidates(ticket, rawTicket))
            {
                if (TryMapTicket(candidate, out resolvedToken))
                {
                    return true;
                }
            }

            resolvedToken = null;
            return false;
        }

        private static string TryExtractTicketFromRawBytes(byte[] data)
        {
            if (data == null || data.Length < 4)
                return null;

            for (int i = 0; i < data.Length - 3; i++)
            {
                if (data[i] == 0x0A)
                {
                    int len = 0;
                    int shift = 0;
                    int idx = i + 1;
                    while (idx < data.Length)
                    {
                        byte b = data[idx++];
                        len |= (b & 0x7F) << shift;
                        if ((b & 0x80) == 0) break;
                        shift += 7;
                    }
                    if (idx + len <= data.Length && len >= 8 && len <= 64)
                    {
                        string candidate = Encoding.UTF8.GetString(data, idx, len);
                        if (!string.IsNullOrWhiteSpace(candidate) && !candidate.Any(c => char.IsControl(c) && c != '\0'))
                        {
                            return candidate.TrimEnd('\0');
                        }
                    }
                }
            }

            string allText = Encoding.ASCII.GetString(data);
            var hexMatch = System.Text.RegularExpressions.Regex.Match(allText, @"[0-9a-f]{32,64}");
            if (hexMatch.Success)
            {
                return hexMatch.Value;
            }

            return null;
        }

        public override async Task InvokeAsync(RpcRequest request)
        {
            string methodName = request.MethodName.ToLowerInvariant();
            switch (methodName)
            {
                case "encryptedhandshake":
                case "encryptedhandshake2":
                    await EncryptedHandshake(request.Params.ToArray(), request.Id);
                    break;
                case "protohandshake":
                case "protohandshake2":
                case "handshake":
                case "handshake2":
                case "auth":
                    ProtoHandshake(request.Params.ToArray(), request.Id);
                    break;
                case "logout":
                case "logout2":
                    SendEmptyMessageResponse(request.Id);
                    break;
                default:
                    MethodNotFound(request);
                    break;
            }
        }

        public override void Invoke(RpcRequest request)
        {
            InvokeAsync(request).Wait();
        }
    }
}
