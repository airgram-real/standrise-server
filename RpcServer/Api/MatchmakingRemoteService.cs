using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ProtoDictionary = Axlebolt.Bolt.Protobuf.Dictionary;
using ProtoEnum = Axlebolt.RpcSupport.Protobuf.Enum;
using RpcException = Axlebolt.RpcSupport.Protobuf.Exception;

namespace StandRiseServer.RpcServer.Api
{
    public class MatchmakingRemoteService : RpcClass
    {
        private const int CodeUnauthorized = 401;
        private const int CodeLobbyNotFound = 5001;
        private const int CodeJoinForbidden = 5005;
        private const int CodeLobbyFull = 5006;
        private const int CodeBadRequest = 400;
        private const int CodeInternalError = 500;
        private const int CodeGameServerNotFound = 6001;
        private const string DefaultPhotonServerId = "default";
        private static string DefaultPhotonServerIp => RegionIpHelper.GetRegionIp();
        private const int DefaultPhotonServerPort = MainServerConfig.PhotonGamePort;
        private const string DefaultGameVersion = "0.17.0";

        public MatchmakingRemoteService(UserService user) : base(user) { }

        public override void Invoke(RpcRequest request)
        {
            string method = (request.MethodName ?? string.Empty).Trim();
            Logger.Debug("[Matchmaking] " + method + " params=" + request.Params.Count);

            try
            {
                switch (method.ToLowerInvariant())
                {
                    case "createlobby":
                    case "createlobby2":
                    case "createcustomlobby":
                        CreateLobby(request, false, method.Equals("createlobby2", StringComparison.OrdinalIgnoreCase));
                        return;
                    case "createlobbywithspectators":
                    case "createlobbywithspectators2":
                    case "createcustomlobbywithspectators":
                        CreateLobby(request, true, method.Equals("createlobbywithspectators2", StringComparison.OrdinalIgnoreCase));
                        return;

                    case "joinlobby":
                    case "joinlobby2":
                    case "joincustomlobby":
                    case "joinlobby_simple":
                        JoinLobby(request, LobbyPlayerType.Member, method.Equals("joinlobby2", StringComparison.OrdinalIgnoreCase));
                        return;
                    case "joinlobbyas":
                    case "joinlobbyas2":
                    case "joincustomlobbyas":
                    case "joinlobby_as":
                        JoinLobby(request, LobbyPlayerType.Any, method.Equals("joinlobbyas2", StringComparison.OrdinalIgnoreCase));
                        return;

                    case "leavelobby":
                    case "leavelobby2":
                    case "leavecustomlobby":
                        LeaveLobby(request);
                        return;

                    case "invitetolobby":
                    case "inviteplayertolobby":
                    case "inviteplayertolobby2":
                    case "inviteplayertocustomlobby":
                    case "inviteplayertolobbyas":
                    case "inviteplayertolobbyas2":
                    case "inviteplayertocustomlobbyas":
                        InvitePlayerToLobby(request);
                        return;

                    case "revokeplayerinvitationtolobby":
                    case "revokeplayerinvitationtolobby2":
                    case "revokeplayerinvitationtocustomlobby":
                    case "revokeinvitetolobby":
                        RevokePlayerInvitationToLobby(request);
                        return;

                    case "refuseinvitationtolobby":
                    case "refuseinvitationtolobby2":
                    case "refuseinvitationtocustomlobby":
                    case "refuseinvitetolobby":
                        RefuseInvitationToLobby(request);
                        return;

                    case "kickplayerfromlobby":
                    case "kickplayerfromlobby2":
                    case "kickplayerfromcustomlobby":
                        KickPlayerFromLobby(request);
                        return;

                    case "getinvitestolobby":
                    case "getinvitestolobby2":
                    case "getcustomlobbyinvites":
                        GetInvitesToLobby(request);
                        return;

                    case "setlobbydata":
                    case "setlobbydata2":
                    case "setcustomlobbydata":
                        SetLobbyData(request);
                        return;
                    case "deletelobbydata":
                    case "deletelobbydata2":
                    case "deletecustomlobbydata":
                        DeleteLobbyData(request);
                        return;

                    case "setlobbyjoinable":
                    case "setlobbyjoinable2":
                        SetLobbyJoinable(request);
                        return;
                    case "setlobbytype":
                    case "setlobbytype2":
                    case "setcustomlobbytype":
                        SetLobbyType(request);
                        return;
                    case "setlobbyname":
                    case "setlobbyname2":
                    case "setcustomlobbyname":
                        SetLobbyName(request);
                        return;
                    case "setlobbymaxmembers":
                    case "setlobbymaxmembers2":
                        SetLobbyMaxMembers(request);
                        return;
                    case "setlobbymaxspectators":
                    case "setlobbymaxspectators2":
                        SetLobbyMaxSpectators(request);
                        return;

                    case "setlobbyowner":
                    case "setlobbyowner2":
                        SetLobbyOwner(request);
                        return;
                    case "getlobbyowner":
                    case "getlobbyowner2":
                        GetLobbyOwner(request);
                        return;

                    case "setlobbyphotongame":
                    case "setlobbyphotongame2":
                        SetLobbyPhotonGame(request);
                        return;
                    case "getlobbyphotongame":
                    case "getlobbyphotongame2":
                    case "getcustomlobbyphotongame":
                        GetLobbyPhotonGame(request);
                        return;
                    case "setlobbygameserver":
                    case "setlobbygameserver2":
                    case "setgameserver":
                        SetLobbyGameServer(request);
                        return;
                    case "getlobbygameserver":
                    case "getlobbygameserver2":
                    case "getcustomlobbygameserver":
                        GetLobbyGameServer(request);
                        return;

                    case "sendlobbychatmsg":
                    case "sendlobbychatmsg2":
                    case "sendlobbychatmessage":
                        SendLobbyChatMsg(request);
                        return;

                    case "changelobbyplayertype":
                    case "changelobbyplayertype2":
                    case "changelobbyotherplayertype":
                    case "changelobbyotherplayertype2":
                    case "setlobbyplayertype":
                    case "setplayerlobbytype":
                        SetLobbyPlayerType(request);
                        return;

                    case "getlobby":
                    case "getlobby2":
                    case "getlobbybyid":
                    case "getcustomlobby":
                    case "getcustomlobbybyid":
                        GetLobby(request, method.Equals("getlobby2", StringComparison.OrdinalIgnoreCase));
                        return;
                    case "getlobbymembers":
                    case "getlobbymembers2":
                    case "getcustomlobbymembers":
                        GetLobbyMembers(request, method.Equals("getlobbymembers2", StringComparison.OrdinalIgnoreCase));
                        return;
                    case "getlobbies":
                    case "findlobbies":
                    case "searchlobbies":
                    case "getpubliclobbies":
                    case "findcustomlobbies":
                    case "searchcustomlobbies":
                    case "getcustomlobbies":
                    case "getlobbylist":
                    case "searchlobby":
                    case "requestlobbylist":
                    case "requestlobbylist2":
                        SearchLobbies(request, method.Equals("requestlobbylist2", StringComparison.OrdinalIgnoreCase));
                        return;

                    case "getgameserverplayers":
                    case "getgameserverplayers2":
                        GetGameServerPlayers(request);
                        return;
                    case "getgameserverdetails":
                    case "getgameserverdetails2":
                        GetGameServerDetails(request, method.Equals("getgameserverdetails2", StringComparison.OrdinalIgnoreCase));
                        return;
                    case "requestinternetserverlist":
                    case "requestinternetserverlist2":
                        RequestInternetServerList(request);
                        return;

                    case "startgame":
                    case "startgame2":
                        StartGame(request);
                        return;

                    case "start":
                    case "find":
                    case "findmatch":
                    case "findmatchmaking":
                    case "startmatchmaking":
                    case "matchmake":
                        FindMatchmaking(request);
                        return;

                    case "cancel":
                    case "cancelmatchmaking":
                    case "stopmatchmaking":
                        CancelMatchmaking(request);
                        return;

                    case "confirm":
                    case "confirmmatch":
                        ConfirmMatch(request);
                        return;

                    case "abandon":
                    case "abandonmatch":
                    case "leavematch":
                        AbandonMatch(request);
                        return;

                    case "reconnect":
                    case "reconnectmatch":
                        ReconnectMatch(request);
                        return;

                    default:
                        MethodNotFound(request);
                        return;
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error("[Matchmaking] " + method + " failed: " + ex);
                SendErrorResponse(request.Id, CodeInternalError, ex.Message);
            }
        }

        private void CreateLobby(RpcRequest request, bool withSpectators, bool wrappedResponse)
        {
            if (!TryGetCurrentPlayer(out string playerId, out PlayerDocument player))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "player_not_authenticated");
                return;
            }

            PlayerStatus status = GetStatus(playerId);
            string activeLobbyId = status.playInGame?.lobbyId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(activeLobbyId))
            {
                if (StaticClasses.Lobbies.ContainsKey(activeLobbyId))
                {
                    LeaveLobbyInternal(playerId, player, true, true);
                    status = GetStatus(playerId);
                }
                else
                {
                    status.playInGame.lobbyId = string.Empty;
                    status.playInGame.lobbyName = string.Empty;
                    status.playInGame.photonGame = null;
                    SaveStatus(playerId, status);
                }
            }

            string name = player.name + " Lobby";
            LobbyType lobbyType = LobbyType.Public;
            int maxMembers = 5;
            int maxSpectators = withSpectators ? 5 : 0;

            if (TryRead(request, 0, out CreateLobbyWithSpectatorsRequest csReq))
            {
                name = !string.IsNullOrWhiteSpace(csReq.Name) ? csReq.Name : name;
                // Клиент почти никогда явно не шлёт LobbyType при обычном "Создать лобби" —
                // поле остаётся 0 (Private) просто как дефолт протобафа, а не как осознанный выбор.
                // Поэтому 0 трактуем как "не указано" -> открытое (Public), а не закрытое.
                lobbyType = csReq.LobbyType != 0 ? csReq.LobbyType : LobbyType.Public;
                // MaxMembers == 0 значит "клиент не передал значение", а не "лимит 1".
                // Раньше Clamp(0, 1, 20) давал 1 и лобби создавалось на одного человека.
                maxMembers = csReq.MaxMembers > 0 ? Clamp(csReq.MaxMembers, 1, 20) : maxMembers;
                maxSpectators = csReq.MaxSpectators > 0 ? Clamp(csReq.MaxSpectators, 0, 20) : maxSpectators;
            }
            else if (TryRead(request, 0, out CreateLobbyRequest cReq))
            {
                name = !string.IsNullOrWhiteSpace(cReq.Name) ? cReq.Name : name;
                lobbyType = cReq.LobbyType != 0 ? cReq.LobbyType : LobbyType.Public;
                maxMembers = cReq.MaxMembers > 0 ? Clamp(cReq.MaxMembers, 1, 20) : maxMembers;
            }
            else
            {
                name = ReadString(request, 0, name);
                int parsedType = ReadInt(request, 1, -1);
                lobbyType = parsedType >= 0 ? (LobbyType)parsedType : LobbyType.Public;
                if (withSpectators)
                {
                    maxMembers = Clamp(ReadInt(request, 2, 5), 1, 20);
                    maxSpectators = Clamp(ReadInt(request, 3, 5), 0, 20);
                }
                else
                {
                    maxMembers = Clamp(ReadInt(request, 2, 5), 1, 20);
                }
            }

            bool joinable = true;

            BoltLobby lobby = new BoltLobby(
                Guid.NewGuid().ToString("N"),
                playerId,
                name,
                ConvertLobbyType(lobbyType),
                joinable,
                maxMembers,
                maxSpectators,
                new ConcurrentDictionary<string, string>(),
                new List<BoltFriend> { player.GetBoltFriend() },
                new List<BoltFriend>(),
                new List<BoltFriend>(),
                new List<BoltFriend>(),
                CreateDefaultGameServer(),
                null);

            lobby.Data["custom"] = "true";
            lobby.Data["createdAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            lobby.Data["owner"] = playerId;
            EnsureCustomLobbyDefaults(lobby);
            ApplyLobbyDataToPhotonGame(lobby);

            StaticClasses.Lobbies[lobby.Id] = lobby;
            status.playInGame ??= new PlayInGame
            {
                gameCode = "standoff2",
                gameVersion = DefaultGameVersion,
                lobbyId = string.Empty,
                lobbyName = string.Empty
            };
            status.playInGame.lobbyId = lobby.Id;
            status.playInGame.lobbyName = lobby.Name;
            status.playInGame.photonGame = lobby.PhotonGame;
            status.playInGame.gameVersion = string.IsNullOrWhiteSpace(status.playInGame.gameVersion)
                ? DefaultGameVersion : status.playInGame.gameVersion;
            SaveStatus(playerId, status);

            RepairLobbyOwnership(lobby);
            StaticClasses.Lobbies[lobby.Id] = lobby;
            Logger.Debug("[Matchmaking] lobby created id=" + lobby.Id + " owner=" + playerId);
            BroadcastLobbyInitialSync(lobby);
            try
            {
                PlayerFriend selfFriend = player.GetPlayerFriend(playerId);
                SendMatchmakingPlayerEvent(playerId, "onNewPlayerJoinedLobby", selfFriend);
            }
            catch (System.Exception ex)
            {
                Logger.LogWarn("[Matchmaking] createLobby self sync failed: " + ex.Message);
            }
            Lobby lobbyResponse = lobby.ToOriginal(playerId);
            if (wrappedResponse)
            {
                SendWrappedMessageResponse(request.Id, 1, lobbyResponse);
                return;
            }

            SendReturn(request.Id, lobbyResponse);
        }

        private void JoinLobby(RpcRequest request, LobbyPlayerType fallbackType, bool wrappedResponse)
        {
            if (!TryGetCurrentPlayer(out string playerId, out PlayerDocument player))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "player_not_authenticated");
                return;
            }

            string lobbyId = string.Empty;
            LobbyPlayerType playerType = fallbackType;

            if (request.Params.Count >= 1)
            {
                if (TryRead(request, 0, out JoinLobbyAsRequest joinAsRequest))
                {
                    lobbyId = joinAsRequest.LobbyId;
                    playerType = joinAsRequest.PlayerType == LobbyPlayerType.Any ? LobbyPlayerType.Member : joinAsRequest.PlayerType;
                }
                else if (TryRead(request, 0, out JoinLobbyRequest joinRequest))
                {
                    lobbyId = joinRequest.LobbyId;
                    playerType = LobbyPlayerType.Member;
                }
                else
                {
                    lobbyId = ReadString(request, 0, string.Empty);
                    playerType = ReadEnum(request, 1, fallbackType == LobbyPlayerType.Any ? LobbyPlayerType.Member : fallbackType);
                }
            }

            if (string.IsNullOrWhiteSpace(lobbyId) || !StaticClasses.Lobbies.TryGetValue(lobbyId, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeLobbyNotFound, "lobby_not_found");
                return;
            }

            if (playerType == LobbyPlayerType.Any) playerType = LobbyPlayerType.Member;
            if (!CanJoin(lobby, player, playerId, playerType))
            {
                Logger.Debug($"[JoinLobby] CodeJoinForbidden: player={playerId} lobby={lobby.Id} type={lobby.Type} joinable={lobby.Joinable} playerType={playerType} invited={lobby.IsAnyInvited(playerId)} member={lobby.IsLobbyMember(playerId)} spect={lobby.IsLobbySpectator(playerId)} members={lobby.LobbyMembers.Length} max={lobby.MaxMembers}");
                SendErrorResponse(request.Id, CodeJoinForbidden, "lobby_join_forbidden");
                return;
            }

            if (lobby.IsMemberInvited(playerId))
                playerType = LobbyPlayerType.Member;
            else if (lobby.IsSpectatorInvited(playerId))
                playerType = LobbyPlayerType.Spectator;

            if (playerType == LobbyPlayerType.Member && !lobby.IsLobbyMember(playerId) && lobby.LobbyMembers.Length >= lobby.MaxMembers)
            {
                SendErrorResponse(request.Id, CodeLobbyFull, "lobby_members_full");
                return;
            }

            if (playerType == LobbyPlayerType.Spectator && !lobby.IsLobbySpectator(playerId) && lobby.LobbySpectators.Length >= lobby.MaxSpectators)
            {
                SendErrorResponse(request.Id, CodeLobbyFull, "lobby_spectators_full");
                return;
            }

            PlayerStatus status = GetStatus(playerId);
            string oldLobbyId = status.playInGame?.lobbyId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(oldLobbyId) && oldLobbyId != lobby.Id)
                LeaveLobbyInternal(playerId, player, true, true);

            if (playerType == LobbyPlayerType.Spectator)
                lobby.AddLobbySpectator(player.GetBoltFriend());
            else
                lobby.AddLobbyMember(player.GetBoltFriend());

            StaticClasses.Lobbies[lobby.Id] = lobby;
            status = GetStatus(playerId);
            status.playInGame.lobbyId = lobby.Id;
            status.playInGame.lobbyName = lobby.Name;
            SaveStatus(playerId, status);

            PlayerFriend joinedFriend = player.GetPlayerFriend(playerId);
            BroadcastLobby(lobby, playerType == LobbyPlayerType.Spectator ? "onNewSpectatorJoinedLobby" : "onNewPlayerJoinedLobby", joinedFriend);

            Logger.Debug("[Matchmaking] lobby joined id=" + lobby.Id + " player=" + playerId);
            Lobby lobbyResponse = lobby.ToOriginal(playerId);
            if (wrappedResponse)
            {
                SendWrappedMessageResponse(request.Id, 1, lobbyResponse);
                return;
            }

            SendReturn(request.Id, lobbyResponse);
        }

        private void LeaveLobby(RpcRequest request)
        {
            if (!TryGetCurrentPlayer(out string playerId, out PlayerDocument player))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "player_not_authenticated");
                return;
            }

            LeaveLobbyInternal(playerId, player, true, true);
            SendOk(request.Id);
        }

        private void InvitePlayerToLobby(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string ownerId, out PlayerDocument owner, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            if (!IsLobbyOwner(lobby, ownerId))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "only_owner_can_invite");
                return;
            }

            string invitedPlayerId = string.Empty;
            LobbyPlayerType playerType = LobbyPlayerType.Member;

            if (TryRead(request, 0, out InvitePlayerToLobbyAsRequest inviteAsRequest))
            {
                invitedPlayerId = inviteAsRequest.InvitedPlayerId;
                playerType = inviteAsRequest.PlayerType;
            }
            else if (TryRead(request, 0, out InvitePlayerToLobbyRequest inviteRequest))
            {
                invitedPlayerId = inviteRequest.InvitedPlayerId;
            }
            else
            {
                invitedPlayerId = ReadString(request, 0, string.Empty);
                playerType = ReadEnum(request, 1, LobbyPlayerType.Member);
            }

            if (playerType == LobbyPlayerType.Any)
                playerType = LobbyPlayerType.Member;

            if (string.IsNullOrWhiteSpace(invitedPlayerId) || !TryGetPlayer(invitedPlayerId, out PlayerDocument invitedPlayerDoc))
            {
                SendErrorResponse(request.Id, CodeBadRequest, "invited_player_not_found");
                return;
            }

            if (playerType == LobbyPlayerType.Spectator)
                lobby.AddLobbySpectatorInvite(invitedPlayerDoc.GetBoltFriend());
            else
                lobby.AddLobbyMemberInvite(invitedPlayerDoc.GetBoltFriend());

            StaticClasses.Lobbies[lobby.Id] = lobby;

            PlayerFriend invitedForOwner = invitedPlayerDoc.GetPlayerFriend(ownerId);
            BroadcastLobby(lobby, playerType == LobbyPlayerType.Spectator ? "onNewSpectatorInvitedToLobby" : "onNewPlayerInvitedToLobby", ownerId, invitedForOwner);

            var lobbyInvite = new LobbyInvite
            {
                LobbyId = lobby.Id,
                InviteCreator = owner.GetPlayerFriend(invitedPlayerId),
                Date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PlayerType = playerType
            };
            PlayerFriend inviteSender = owner.GetPlayerFriend(invitedPlayerId);
            if (playerType == LobbyPlayerType.Spectator)
            {
                SendMatchmakingPlayerEvent(invitedPlayerId, "onReceivedSpectatorInviteToLobbyEvent", new OnReceivedSpectatorInviteToLobbyEvent { Invite = lobbyInvite });
                SendMatchmakingPlayerEvent(invitedPlayerId, "onReceivedSpectatorInviteToLobby", inviteSender, lobby.Id);
            }
            else
            {
                SendMatchmakingPlayerEvent(invitedPlayerId, "onReceivedInviteToLobbyEvent", new OnReceivedInviteToLobbyEvent { Invite = lobbyInvite });
                SendMatchmakingPlayerEvent(invitedPlayerId, "onReceivedInviteToLobby", inviteSender, lobby.Id);
            }

            // AFK-боты принимают инвайт сразу (для теста Allies party).
            AfkBotManager.TryAutoAcceptLobbyInvite(lobby.Id, invitedPlayerId, playerType);

            Logger.Debug("[Matchmaking] invite lobby=" + lobby.Id + " from=" + ownerId + " to=" + invitedPlayerId + " type=" + playerType);
            SendOk(request.Id);
        }

        private void RevokePlayerInvitationToLobby(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            if (!IsLobbyOwner(lobby, playerId))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "only_owner_can_revoke");
                return;
            }

            string revokedPlayerId = TryRead(request, 0, out RevokePlayerInvitationToLobbyRequest revokeReq)
                ? revokeReq.RevokedPlayerId
                : ReadString(request, 0, string.Empty);

            if (string.IsNullOrWhiteSpace(revokedPlayerId))
            {
                SendErrorResponse(request.Id, CodeBadRequest, "revoked_player_empty");
                return;
            }

            lobby.RemoveLobbyAnyInviteById(revokedPlayerId);
            StaticClasses.Lobbies[lobby.Id] = lobby;
            BroadcastLobby(lobby, "onRevokeInviteToLobby", playerId, revokedPlayerId);
            SendEvent(revokedPlayerId, "onRevokeInviteToLobby", playerId, revokedPlayerId);
            SendOk(request.Id);
        }

        private void RefuseInvitationToLobby(RpcRequest request)
        {
            if (!TryGetCurrentPlayer(out string playerId, out PlayerDocument player))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "player_not_authenticated");
                return;
            }

            string lobbyId = TryRead(request, 0, out RefuseInvitationToLobbyRequest refuseReq)
                ? refuseReq.LobbyId
                : ReadString(request, 0, string.Empty);

            if (string.IsNullOrWhiteSpace(lobbyId) || !StaticClasses.Lobbies.TryGetValue(lobbyId, out BoltLobby lobby))
            {
                SendOk(request.Id);
                return;
            }

            lobby.RemoveLobbyAnyInviteById(playerId);
            StaticClasses.Lobbies[lobby.Id] = lobby;
            BroadcastLobby(lobby, "onRefuseInviteToLobby", playerId, lobby.Id);
            SendOk(request.Id);
        }

        private void KickPlayerFromLobby(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string ownerId, out PlayerDocument owner, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            if (!IsLobbyOwner(lobby, ownerId))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "only_owner_can_kick");
                return;
            }

            string kickedPlayerId = TryRead(request, 0, out KickPlayerFromLobbyRequest kickReq)
                ? kickReq.KickedPlayerId
                : ReadString(request, 0, string.Empty);

            if (string.IsNullOrWhiteSpace(kickedPlayerId) || kickedPlayerId == ownerId)
            {
                SendErrorResponse(request.Id, CodeBadRequest, "kicked_player_invalid");
                return;
            }

            lobby.RemoveLobbyAnyById(kickedPlayerId);
            lobby.RemoveLobbyAnyInviteById(kickedPlayerId);
            StaticClasses.Lobbies[lobby.Id] = lobby;

            if (StaticClasses.PlayersStatus.TryGetValue(kickedPlayerId, out PlayerStatus kickedStatus) &&
                kickedStatus.playInGame != null && kickedStatus.playInGame.lobbyId == lobby.Id)
            {
                kickedStatus.playInGame.lobbyId = string.Empty;
                SaveStatus(kickedPlayerId, kickedStatus);
            }

            BroadcastLobby(lobby, "onPlayerKickedFromLobby", ownerId, kickedPlayerId);
            SendEvent(kickedPlayerId, "onPlayerKickedFromLobby", ownerId, kickedPlayerId);
            SendOk(request.Id);
        }

        private void GetInvitesToLobby(RpcRequest request)
        {
            if (!TryGetCurrentPlayer(out string playerId, out PlayerDocument player))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "player_not_authenticated");
                return;
            }

            var invites = new List<LobbyInvite>();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (BoltLobby lobby in StaticClasses.Lobbies.Values.ToArray())
            {
                LobbyPlayerType? playerType = null;
                if (lobby.IsMemberInvited(playerId)) playerType = LobbyPlayerType.Member;
                else if (lobby.IsSpectatorInvited(playerId)) playerType = LobbyPlayerType.Spectator;
                if (playerType == null) continue;

                PlayerFriend creator = TryGetPlayer(lobby.LobbyOwnerId, out PlayerDocument owner)
                    ? owner.GetPlayerFriend(playerId)
                    : new PlayerFriend { Player = FriendHelper.GetPlayer(string.Empty), RelationshipStatus = RelationshipStatus.None, LastRelationshipUpdate = 0L };

                invites.Add(new LobbyInvite
                {
                    LobbyId = lobby.Id,
                    InviteCreator = creator,
                    Date = now,
                    PlayerType = playerType.Value
                });
            }

            if (request.Params.Count == 0)
            {
                SendReturn(request.Id, invites.ToArray());
                return;
            }

            GetInvitesToLobbyResponse response = new GetInvitesToLobbyResponse();
            foreach (var invite in invites)
            {
                response.LobbyInvites.Add(invite);
            }

            SendReturn(request.Id, response);
        }

        private void SetLobbyData(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                Logger.Debug("[Matchmaking] setLobbyData ignored: current player has no active lobby");
                SendOk(request.Id);
                return;
            }

            try
            {
                ProtoDictionary dictionary = null;
                if (TryRead(request, 0, out SetLobbyDataRequest dataReq))
                    dictionary = dataReq.Data;
                else
                    dictionary = ReadMessage<ProtoDictionary>(request, 0, null);

                if (dictionary == null)
                {
                    SendOk(request.Id);
                    return;
                }

                Dictionary<string, string> data = new Dictionary<string, string>(
                    lobby.Data ?? new Dictionary<string, string>(),
                    StringComparer.Ordinal);

                ProtoDictionary changed = new ProtoDictionary();
                foreach (KeyValuePair<string, string> item in dictionary.Content)
                {
                    if (string.IsNullOrWhiteSpace(item.Key))
                        continue;

                    string value = item.Value ?? string.Empty;
                    if (string.IsNullOrEmpty(value))
                    {
                        if (IsProtectedLobbyDataKey(item.Key))
                            continue;
                        data.Remove(item.Key);
                        changed.Content[item.Key] = value;
                        continue;
                    }

                    if (item.Key.Equals("LobbyOptions", StringComparison.OrdinalIgnoreCase)
                        && (value == "{}" || value == "[]" || value == "null"))
                        continue;

                    data[item.Key] = value;
                    changed.Content[item.Key] = value;
                }

                lobby.Data = data;
                EnsureCustomLobbyDefaults(lobby);
                ApplyLobbyDataToPhotonGame(lobby);
                StaticClasses.Lobbies[lobby.Id] = lobby;
                if (changed.Content.Count > 0)
                {
                    BroadcastLobby(lobby, "onLobbyDataChanged", changed);
                    if (changed.Content.ContainsKey("LobbyOptions")
                        || changed.Content.ContainsKey("SearchingRegion")
                        || changed.Content.ContainsKey("region")
                        || changed.Content.ContainsKey("Region"))
                    {
                        BroadcastLobby(lobby, "onLobbyPhotonGameChanged", lobby.PhotonGame);
                    }
                }

                Logger.Debug("[Matchmaking] setLobbyData applied lobby=" + lobby.Id + " by=" + playerId
                    + " keys=" + string.Join(",", changed.Content.Keys));
                SendOk(request.Id);
            }
            catch (KeyNotFoundException ex)
            {
                Logger.Error("[Matchmaking] setLobbyData ignored KeyNotFound player=" + playerId + " lobby=" + lobby.Id + ": " + ex);
                SendOk(request.Id);
            }
            catch (System.Exception ex)
            {
                Logger.Error("[Matchmaking] setLobbyData ignored player=" + playerId + " lobby=" + lobby.Id + ": " + ex);
                SendOk(request.Id);
            }
        }

        private void DeleteLobbyData(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            string[] keys = Array.Empty<string>();
            if (TryRead(request, 0, out DeleteLobbyDataRequest delReq))
                keys = delReq.Keys.ToArray();
            else if (TryRead(request, 0, out string[] parsedKeys))
                keys = parsedKeys ?? Array.Empty<string>();

            if (lobby.Data != null)
            {
                foreach (string key in keys)
                    lobby.Data.Remove(key);
            }

            StaticClasses.Lobbies[lobby.Id] = lobby;
            var dict = new ProtoDictionary();
            foreach (string key in keys) dict.Content[key] = string.Empty;
            BroadcastLobby(lobby, "onLobbyDataChanged", dict);
            SendOk(request.Id);
        }

        private void SetLobbyJoinable(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                Logger.Debug("[Matchmaking] setLobbyJoinable ignored: current player has no active lobby");
                SendOk(request.Id);
                return;
            }

            if (!IsLobbyOwner(lobby, playerId))
            {
                Logger.Debug("[Matchmaking] setLobbyJoinable ignored for non-owner player=" + playerId + " lobby=" + lobby.Id);
                SendOk(request.Id);
                return;
            }

            bool joinable;
            if (TryRead(request, 0, out bool boolJoinable))
                joinable = boolJoinable;
            else if (TryRead(request, 0, out SetLobbyJoinableRequest joinableReq))
                joinable = joinableReq.Joinable;
            else
                joinable = true;

            lobby.Joinable = joinable;
            StaticClasses.Lobbies[lobby.Id] = lobby;
            Logger.Debug("[Matchmaking] setLobbyJoinable applied player=" + playerId +
                " lobby=" + lobby.Id +
                " joinable=" + joinable +
                " type=" + lobby.Type +
                " members=" + lobby.LobbyMembers.Length +
                " gs=" + DescribeGameServer(lobby.GameServer) +
                " pg=" + DescribePhotonGame(lobby.PhotonGame));
            BroadcastLobby(lobby, "onLobbyJoinableChanged", joinable);
            SendOk(request.Id);
        }

        private void SetLobbyType(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                Logger.Debug("[Matchmaking] setLobbyType ignored: current player has no active lobby");
                SendOk(request.Id);
                return;
            }

            if (!IsLobbyOwner(lobby, playerId))
            {
                Logger.Debug("[Matchmaking] setLobbyType ignored for non-owner player=" + playerId + " lobby=" + lobby.Id);
                SendOk(request.Id);
                return;
            }

            try
            {
                LobbyType type;
                if (TryReadEnum(request, 0, out LobbyType enumType))
                    type = enumType;
                else if (TryRead(request, 0, out SetLobbyTypeRequest typeReq))
                    type = typeReq.LobbyType;
                else
                    type = lobby.Type switch
                    {
                        BoltLobby.LobbyType.Public => LobbyType.Public,
                        BoltLobby.LobbyType.FriendsOnly => LobbyType.FriendsOnly,
                        BoltLobby.LobbyType.Invisible => LobbyType.Invisible,
                        _ => LobbyType.Private
                    };

                lobby.Type = ConvertLobbyType(type);
                StaticClasses.Lobbies[lobby.Id] = lobby;
                Logger.Debug("[Matchmaking] setLobbyType applied player=" + playerId +
                    " lobby=" + lobby.Id +
                    " type=" + type +
                    " internalType=" + lobby.Type);
                BroadcastLobby(lobby, "onLobbyTypeChanged", type);
                SendOk(request.Id);
            }
            catch (KeyNotFoundException ex)
            {
                Logger.Error("[Matchmaking] setLobbyType ignored KeyNotFound player=" + playerId + " lobby=" + lobby.Id + ": " + ex);
                SendOk(request.Id);
            }
            catch (System.Exception ex)
            {
                Logger.Error("[Matchmaking] setLobbyType ignored player=" + playerId + " lobby=" + lobby.Id + ": " + ex);
                SendOk(request.Id);
            }
        }

        private void SetLobbyName(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            if (!IsLobbyOwner(lobby, playerId))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "only_owner_can_change_name");
                return;
            }

            string name;
            if (TryRead(request, 0, out SetLobbyNameRequest nameReq))
                name = nameReq.Name;
            else
                name = ReadString(request, 0, lobby.Name);

            lobby.Name = string.IsNullOrWhiteSpace(name) ? lobby.Name : name.Trim();
            StaticClasses.Lobbies[lobby.Id] = lobby;
            BroadcastLobby(lobby, "onLobbyNameChanged", lobby.Name);
            SendOk(request.Id);
        }

        private void SetLobbyMaxMembers(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            if (!IsLobbyOwner(lobby, playerId))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "only_owner_can_change_max_members");
                return;
            }

            int maxMembers;
            if (TryRead(request, 0, out SetLobbyMaxMembersRequest membersReq))
                maxMembers = membersReq.MaxMembers;
            else
                maxMembers = ReadInt(request, 0, lobby.MaxMembers);

            lobby.MaxMembers = Clamp(maxMembers, Math.Max(1, lobby.LobbyMembers.Length), 20);
            StaticClasses.Lobbies[lobby.Id] = lobby;
            BroadcastLobby(lobby, "onLobbyMaxMembersChanged", (byte)lobby.MaxMembers);
            SendOk(request.Id);
        }

        private void SetLobbyMaxSpectators(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            if (!IsLobbyOwner(lobby, playerId))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "only_owner_can_change_max_spectators");
                return;
            }

            int maxSpectators;
            if (TryRead(request, 0, out SetLobbyMaxSpectatorsRequest specReq))
                maxSpectators = specReq.MaxSpectators;
            else
                maxSpectators = ReadInt(request, 0, lobby.MaxSpectators);

            lobby.MaxSpectators = Clamp(maxSpectators, lobby.LobbySpectators.Length, 20);
            StaticClasses.Lobbies[lobby.Id] = lobby;
            BroadcastLobby(lobby, "onLobbyMaxSpectatorsChanged", (byte)lobby.MaxSpectators);
            SendOk(request.Id);
        }

        private void SetLobbyOwner(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            if (!IsLobbyOwner(lobby, playerId))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "only_owner_can_change_owner");
                return;
            }

            string newOwnerId;
            if (TryRead(request, 0, out SetLobbyOwnerRequest ownerReq))
                newOwnerId = ownerReq.PlayerId;
            else
                newOwnerId = ReadString(request, 0, string.Empty);

            if (!lobby.IsLobbyAny(newOwnerId))
            {
                SendErrorResponse(request.Id, CodeBadRequest, "new_owner_not_in_lobby");
                return;
            }

            if (lobby.IsLobbySpectator(newOwnerId)) lobby.ChangeMemberType(newOwnerId, LobbyPlayerType.Member);
            lobby.SetOwner(newOwnerId);
            StaticClasses.Lobbies[lobby.Id] = lobby;
            BroadcastLobby(lobby, "onLobbyOwnerChanged", newOwnerId);
            SendOk(request.Id);
        }

        private void GetLobbyOwner(RpcRequest request)
        {
            string lobbyId = string.Empty;

            if (TryRead(request, 0, out GetLobbyOwnerRequest ownerReq))
                lobbyId = ownerReq.LobbyId;
            else
                lobbyId = ReadString(request, 0, string.Empty);

            if (string.IsNullOrWhiteSpace(lobbyId) &&
                TryGetCurrentLobby(out string curPlayerId, out _, out BoltLobby curLobby))
                lobbyId = curLobby.Id;

            if (!string.IsNullOrWhiteSpace(lobbyId) &&
                StaticClasses.Lobbies.TryGetValue(lobbyId, out BoltLobby lobby) &&
                TryGetPlayer(lobby.LobbyOwnerId, out PlayerDocument owner))
            {
                SendReturn(request.Id, FriendHelper.GetPlayer(owner));
                return;
            }

            SendErrorResponse(request.Id, CodeLobbyNotFound, "owner_not_found");
        }

        private void SetLobbyPhotonGame(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            Axlebolt.Bolt.Protobuf.PhotonGame photonGame = null;

            if (TryRead(request, 0, out SetLobbyPhotonGameRequest pgReq))
                photonGame = pgReq.PhotonGame;
            else
                photonGame = ReadMessage<Axlebolt.Bolt.Protobuf.PhotonGame>(request, 0, null);

            if (photonGame == null)
            {
                SendErrorResponse(request.Id, CodeBadRequest, "photon_game_required");
                return;
            }

            lobby.PhotonGame = new PhotonGame
            {
                region = photonGame.Region ?? string.Empty,
                roomId = photonGame.RoomId ?? string.Empty,
                appVersion = photonGame.AppVersion ?? string.Empty,
                customProperties = photonGame.CustomProperties.ToDictionary(item => item.Key, item => item.Value)
            };

            if (!string.IsNullOrWhiteSpace(photonGame.Region))
            {
                lobby.Data ??= new Dictionary<string, string>();
                lobby.Data["region"] = NormalizeRegion(photonGame.Region);
            }

            StaticClasses.Lobbies[lobby.Id] = lobby;
            UpdatePhotonStatus(lobby);
            BroadcastLobby(lobby, "onLobbyPhotonGameChanged", photonGame);
            SendOk(request.Id);
        }

        private void GetLobbyPhotonGame(RpcRequest request)
        {
            string lobbyId = string.Empty;

            if (TryRead(request, 0, out GetLobbyPhotonGameRequest pgReq))
                lobbyId = pgReq.LobbyId;
            else
                lobbyId = ReadString(request, 0, string.Empty);

            Logger.Log($"[Matchmaking] GetLobbyPhotonGame RPC from {PlayerId}: lobbyId_param='{lobbyId}'");

            if (string.IsNullOrWhiteSpace(lobbyId) &&
                TryGetCurrentLobby(out _, out _, out BoltLobby curLobby))
            {
                lobbyId = curLobby.Id;
                Logger.Log($"[Matchmaking] GetLobbyPhotonGame: resolved lobbyId from current status = '{lobbyId}'");
            }

            if (!string.IsNullOrWhiteSpace(lobbyId) &&
                StaticClasses.Lobbies.TryGetValue(lobbyId, out BoltLobby lobby) &&
                lobby.PhotonGame != null)
            {
                Logger.Log($"[Matchmaking] GetLobbyPhotonGame: returning PhotonGame for lobby '{lobbyId}' (region={lobby.PhotonGame.region}, roomId={lobby.PhotonGame.roomId})");
                SendReturn(request.Id, PhotonGame.GetByDocument(lobby.PhotonGame));
                return;
            }

            Logger.Log($"[Matchmaking] GetLobbyPhotonGame: FAILED - lobbyId='{lobbyId}', lobbies.ContainsKey={StaticClasses.Lobbies.ContainsKey(lobbyId)}");
            SendReturn(request.Id, new Axlebolt.Bolt.Protobuf.PhotonGame());
        }

        private void SetLobbyGameServer(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            GameServer server = null;
            if (TryRead(request, 0, out SetLobbyGameServerRequest gsReq))
                server = gsReq.GameServer;
            else if (TryRead(request, 0, out GameServerDetails details) && details.GameServer != null)
                server = details.GameServer;
            else
                server = ReadMessage<GameServer>(request, 0, null);

            if (server != null)
            {
                lobby.GameServer = server;
                StaticClasses.Lobbies[lobby.Id] = lobby;
                BroadcastLobby(lobby, "onLobbyGameServerChanged", server);
            }

            SendOk(request.Id);
        }

        private void GetLobbyGameServer(RpcRequest request)
        {
            string lobbyId = string.Empty;

            if (TryRead(request, 0, out GetLobbyGameServerRequest gsReq))
                lobbyId = gsReq.LobbyId;
            else
                lobbyId = ReadString(request, 0, string.Empty);

            if (string.IsNullOrWhiteSpace(lobbyId) &&
                TryGetCurrentLobby(out _, out _, out BoltLobby curLobby))
                lobbyId = curLobby.Id;

            if (!string.IsNullOrWhiteSpace(lobbyId) &&
                StaticClasses.Lobbies.TryGetValue(lobbyId, out BoltLobby lobby) &&
                lobby.GameServer != null)
            {
                SendReturn(request.Id, NormalizeGameServer(lobby.GameServer));
                return;
            }

            SendReturn(request.Id, CreateDefaultGameServer());
        }

        private void SendLobbyChatMsg(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            string message;
            if (TryRead(request, 0, out SendLobbyChatMsgRequest chatReq))
                message = chatReq.Message;
            else
                message = ReadString(request, 0, string.Empty);

            if (string.IsNullOrWhiteSpace(message))
            {
                SendOk(request.Id);
                return;
            }

            if (message.Length > 256) message = message.Substring(0, 256);
            BroadcastLobby(lobby, "onLobbyChatMessage", playerId, message);
            SendOk(request.Id);
        }

        private void SetLobbyPlayerType(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string actorId, out PlayerDocument actor, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            string targetId = actorId;
            LobbyPlayerType type = LobbyPlayerType.Member;

            if (TryRead(request, 0, out ChangeLobbyOtherPlayerTypeRequest otherReq))
            {
                targetId = otherReq.PlayerId;
                type = ResolveLobbyPlayerType(otherReq.PlayerType, request, 1);
            }
            else if (TryRead(request, 0, out ChangeLobbyPlayerTypeRequest selfReq))
            {
                type = ResolveLobbyPlayerType(selfReq.PlayerType, request, 1);
            }
            else if (request.Params.Count >= 2)
            {
                targetId = ReadString(request, 0, actorId);
                type = ReadLobbyPlayerType(request, 1, LobbyPlayerType.Member);
            }
            else if (request.Params.Count == 1)
            {
                type = ReadLobbyPlayerType(request, 0, LobbyPlayerType.Member);
            }

            if (type == LobbyPlayerType.Any)
                type = LobbyPlayerType.Member;

            if (targetId != actorId && !IsLobbyOwner(lobby, actorId))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "only_owner_can_move_other_player");
                return;
            }

            if (type == LobbyPlayerType.Member && !lobby.IsLobbyMember(targetId) && lobby.LobbyMembers.Length >= lobby.MaxMembers)
            {
                SendErrorResponse(request.Id, CodeLobbyFull, "lobby_members_full");
                return;
            }

            if (type == LobbyPlayerType.Spectator && !lobby.IsLobbySpectator(targetId))
            {
                if (lobby.MaxSpectators <= lobby.LobbySpectators.Length)
                {
                    int grown = lobby.LobbySpectators.Length + 1;
                    lobby.MaxSpectators = grown;
                    BroadcastLobby(lobby, "onLobbyMaxSpectatorsChanged", (byte)lobby.MaxSpectators);
                }
            }

            lobby.ChangeMemberType(targetId, type);
            StaticClasses.Lobbies[lobby.Id] = lobby;
            BroadcastLobby(lobby, "onLobbyPlayerTypeChanged", targetId, type);
            SendOk(request.Id);
        }

        private void GetLobby(RpcRequest request, bool wrappedResponse)
        {
            string lobbyId = string.Empty;

            if (TryRead(request, 0, out GetLobbyRequest getReq))
                lobbyId = getReq.LobbyId;
            else if (TryRead(request, 0, out JoinLobbyRequest joinReq))
                lobbyId = joinReq.LobbyId;
            else
                lobbyId = ReadString(request, 0, string.Empty);

            if (string.IsNullOrWhiteSpace(lobbyId) &&
                TryGetCurrentLobby(out string curPlayerId, out _, out BoltLobby curLobby))
            {
                Lobby lobbyResponse = curLobby.ToOriginal(curPlayerId);
                if (wrappedResponse)
                {
                    SendWrappedMessageResponse(request.Id, 1, lobbyResponse);
                    return;
                }
                SendReturn(request.Id, lobbyResponse);
                return;
            }

            if (!string.IsNullOrWhiteSpace(lobbyId) &&
                StaticClasses.Lobbies.TryGetValue(lobbyId, out BoltLobby lobby))
            {
                string playerId = PlayerId ?? string.Empty;
                Lobby lobbyResponse = lobby.ToOriginal(playerId);
                if (wrappedResponse)
                {
                    SendWrappedMessageResponse(request.Id, 1, lobbyResponse);
                    return;
                }
                SendReturn(request.Id, lobbyResponse);
                return;
            }

            string pid = PlayerId;
            if (!string.IsNullOrWhiteSpace(pid))
            {
                PlayerStatus status = GetStatus(pid);
                if (status.playInGame != null && !string.IsNullOrWhiteSpace(status.playInGame.lobbyId))
                {
                    status.playInGame.lobbyId = string.Empty;
                    status.playInGame.photonGame = null;
                    SaveStatus(pid, status);
                }
            }

            SendErrorResponse(request.Id, CodeLobbyNotFound, "lobby_not_found");
        }

        private void GetLobbyMembers(RpcRequest request, bool wrappedResponse)
        {
            string lobbyId = string.Empty;

            if (TryRead(request, 0, out GetLobbyMembersRequest membersReq))
                lobbyId = membersReq.LobbyId;
            else
                lobbyId = ReadString(request, 0, string.Empty);

            if (string.IsNullOrWhiteSpace(lobbyId) &&
                TryGetCurrentLobby(out _, out _, out BoltLobby curLobby))
                lobbyId = curLobby.Id;

            if (!string.IsNullOrWhiteSpace(lobbyId) &&
                StaticClasses.Lobbies.TryGetValue(lobbyId, out BoltLobby lobby))
            {
                string[] memberIds = lobby.LobbyMembers.Select(f => f.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
                Dictionary<string, PlayerDocument> docs = memberIds.Length > 0
                    ? BoltMainDatabaseProvider.Instance.GetPlayerDocumentsByIds(memberIds)
                    : new Dictionary<string, PlayerDocument>();
                Player[] members = lobby.LobbyMembers
                    .Select(f => docs.TryGetValue(f.Id, out PlayerDocument pd) ? FriendHelper.GetPlayer(pd) : null)
                    .Where(p => p != null)
                    .ToArray();
                if (wrappedResponse)
                {
                    SendWrappedRepeatedResponse(request.Id, 1, members);
                    return;
                }
                SendReturn(request.Id, members);
                return;
            }

            if (wrappedResponse)
            {
                SendWrappedRepeatedResponse(request.Id, 1, Array.Empty<Player>());
                return;
            }
            SendReturn(request.Id, Array.Empty<Player>());
        }

        private void SearchLobbies(RpcRequest request, bool wrappedResponse)
        {
            string playerId = PlayerId ?? string.Empty;
            Filter[] filters = Array.Empty<Filter>();

            if (TryRead(request, 0, out RequestLobbyListRequest lobbyListReq))
            {
                filters = lobbyListReq.Filters.ToArray();
            }
            else if (TryRead(request, 0, out SearchLobbyRequest searchReq))
            {
                filters = searchReq.Filters.ToArray();
            }
            else if (TryRead(request, 0, out Filter[] parsedFilters))
            {
                filters = parsedFilters ?? Array.Empty<Filter>();
            }

            BoltLobby[] matchedLobbies = StaticClasses.Lobbies.Values
                .Where(lobby => lobby.Type == BoltLobby.LobbyType.Public && lobby.Joinable)
                .Where(lobby => filters.All(filter => MatchFilter(lobby, filter)))
                .OrderByDescending(lobby => lobby.Data != null &&
                    lobby.Data.TryGetValue("createdAt", out string createdAt) ? createdAt : string.Empty)
                .Take(100)
                .ToArray();

            var allIds = new HashSet<string>();
            foreach (var lobby in matchedLobbies)
                BoltLobby.CollectAllPlayerIds(lobby, allIds);

            string[] idArray = allIds.Count > 0 ? allIds.ToArray() : Array.Empty<string>();
            Dictionary<string, PlayerDocument> sharedDocs = idArray.Length > 0
                ? BoltMainDatabaseProvider.Instance.GetPlayerDocumentsByIds(idArray)
                : new Dictionary<string, PlayerDocument>();

            Dictionary<string, PlayerFriend> sharedFriends = string.IsNullOrWhiteSpace(playerId)
                ? new Dictionary<string, PlayerFriend>()
                : FriendHelper.GetPlayerFriends(playerId)
                    .Where(f => !string.IsNullOrWhiteSpace(f?.Player?.Id))
                    .GroupBy(f => f.Player.Id)
                    .ToDictionary(g => g.Key, g => g.First());

            Lobby[] lobbies = matchedLobbies
                .Select(lobby => lobby.ToOriginalBatch(playerId, sharedDocs, sharedFriends))
                .ToArray();

            string method = (request.MethodName ?? string.Empty).ToLowerInvariant();
            if (method == "requestlobbylist2")
            {
                SendWrappedRepeatedResponse(request.Id, 1, lobbies);
            }
            else if (method == "requestlobbylist")
            {
                var response = new RequestLobbyListResponse();
                response.Lobbies.AddRange(lobbies);
                SendReturn(request.Id, response);
            }
            else
            {
                if (wrappedResponse)
                {
                    SendWrappedRepeatedResponse(request.Id, 1, lobbies);
                    return;
                }
                var response = new SearchLobbyResponse();
                response.Lobbies.AddRange(lobbies);
                SendReturn(request.Id, response);
            }
        }

        private void GetGameServerPlayers(RpcRequest request)
        {
            string gameServerId = string.Empty;
            if (TryRead(request, 0, out GetGameServerPlayersRequest gspReq))
                gameServerId = gspReq.GameServerId;
            else
                gameServerId = ReadString(request, 0, string.Empty);

            BoltFriend[] allMembers = StaticClasses.Lobbies.Values
                .Where(l => l.GameServer != null && l.GameServer.Id == gameServerId)
                .SelectMany(l => l.LobbyMembers)
                .ToArray();
            string[] pIds = allMembers.Select(f => f.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
            Dictionary<string, PlayerDocument> gsDocs = pIds.Length > 0
                ? BoltMainDatabaseProvider.Instance.GetPlayerDocumentsByIds(pIds)
                : new Dictionary<string, PlayerDocument>();
            Player[] players = allMembers
                .Select(f => gsDocs.TryGetValue(f.Id, out PlayerDocument pd) ? FriendHelper.GetPlayer(pd) : null)
                .Where(p => p != null)
                .Distinct()
                .ToArray();

            var response = new GetGameServerPlayersResponse();
            response.Players.AddRange(players);
            SendReturn(request.Id, response);
        }

        private void GetGameServerDetails(RpcRequest request, bool wrappedResponse)
        {
            string gameServerId = string.Empty;
            if (TryRead(request, 0, out GetGameServerDetailsRequest gsdReq))
                gameServerId = gsdReq.GameServerId;
            else
                gameServerId = ReadString(request, 0, string.Empty);

            BoltLobby lobby = StaticClasses.Lobbies.Values
                .FirstOrDefault(l => l.GameServer != null && l.GameServer.Id == gameServerId);

            if (lobby != null)
            {
                GameServer server = NormalizeGameServer(lobby.GameServer);
                var details = new GameServerDetails
                {
                    Id = server.Id,
                    GameServer = server,
                    CurrentPlayers = lobby.LobbyMembers.Length,
                    MaxPlayers = lobby.MaxMembers,
                    Version = DefaultGameVersion,
                    SuccessfulResponse = true
                };
                if (lobby.Data != null)
                {
                    lobby.Data.TryGetValue("map", out string map);
                    details.Map = map ?? string.Empty;
                }
                if (wrappedResponse)
                {
                    SendWrappedMessageResponse(request.Id, 1, details);
                    return;
                }
                SendReturn(request.Id, details);
                return;
            }

            var emptyDetails = CreateDefaultGameServerDetails();
            if (wrappedResponse)
            {
                SendWrappedMessageResponse(request.Id, 1, emptyDetails);
                return;
            }
            SendReturn(request.Id, emptyDetails);
        }

        private void RequestInternetServerList(RpcRequest request)
        {
            string map = string.Empty;
            byte freePlayerSlots = 0;
            byte maxPlayers = 0;
            bool withPassword = false;

            if (TryRead(request, 0, out RequestInternetServerListRequest rislReq))
            {
                map = rislReq.Map ?? string.Empty;
                withPassword = rislReq.WithPassword;
            }
            else if (request.Params.Count >= 4)
            {
                map = ReadString(request, 0, string.Empty);
                freePlayerSlots = (byte)ReadInt(request, 1, 0);
                maxPlayers = (byte)ReadInt(request, 2, 0);
                withPassword = ReadBool(request, 3, false);
            }
            else if (request.Params.Count >= 1)
            {
                map = ReadString(request, 0, string.Empty);
            }

            var servers = StaticClasses.Lobbies.Values
                .Where(l => l.Type == BoltLobby.LobbyType.Public &&
                            l.Joinable &&
                    l.GameServer != null &&
                            (string.IsNullOrWhiteSpace(map) ||
                                (l.Data != null && l.Data.TryGetValue("map", out string lobbyMap) && lobbyMap == map)))
                .Select(l =>
                {
                    string lobbyMap = string.Empty;
                    l.Data?.TryGetValue("map", out lobbyMap);
                    GameServer server = NormalizeGameServer(l.GameServer);
                    return new GameServerDetails
                    {
                        Id = server.Id,
                        GameServer = server,
                        Map = lobbyMap ?? string.Empty,
                        CurrentPlayers = l.LobbyMembers.Length,
                        MaxPlayers = l.MaxMembers,
                        RequirePassword = false,
                        Version = DefaultGameVersion,
                        SuccessfulResponse = true,
                        DoNotRefresh = false
                    };
                })
                .ToArray();

            var response = new RequestInternetServerListResponse();
            if (servers.Length == 0)
            {
                response.Servers.Add(CreateDefaultGameServerDetails(map));
                SendReturn(request.Id, response);
                return;
            }
            response.Servers.AddRange(servers);
            SendReturn(request.Id, response);
        }

        private void StartGame(RpcRequest request)
        {
            if (!TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "lobby_not_found");
                return;
            }

            if (!IsLobbyOwner(lobby, playerId))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "only_owner_can_start_game");
                return;
            }

            if (lobby.LobbyMembers.Length < 1)
            {
                SendErrorResponse(request.Id, CodeBadRequest, "not_enough_players");
                return;
            }

            ApplyLobbyDataToPhotonGame(lobby);

            if (lobby.PhotonGame == null)
            {
                lobby.PhotonGame = new PhotonGame
                {
                    region = BoltMainDatabaseProvider.MainServerRegion,
                    roomId = lobby.Id,
                    appVersion = DefaultGameVersion,
                    customProperties = new System.Collections.Generic.Dictionary<string, string>()
                };
                StaticClasses.Lobbies[lobby.Id] = lobby;
                Logger.Debug("[Matchmaking] startGame: created fallback PhotonGame for lobby=" + lobby.Id);
            }

            if (string.IsNullOrWhiteSpace(lobby.PhotonGame.roomId))
            {
                lobby.PhotonGame.roomId = lobby.Id;
            }

            lobby.GameServer = new GameServer
            {
                Id = DefaultPhotonServerId,
                Ip = StaticClasses.PublicIp,
                Port = 5056
            };

            int lobbyGameMode = ResolveLobbyGameMode(lobby.Data);
            string lobbyModeName = lobbyGameMode switch
            {
                0 => "DeathMatch",
                1 => "Defuse",
                2 => "ArmsRace",
                3 => "Training",
                4 => "SniperDuel",
                5 => "Ranked2v2",
                6 => "RankedDefuse",
                7 => "Escalation",
                8 => "Ranked2v2",
                9 => "SniperDuel",
                11 => "ClanRankedDefuse",
                30 => "Arcade",
                _ => "Defuse"
            };
            string lobbyPhotonMode = lobbyGameMode switch
            {
                5 => "Ranked2v2",
                6 => "RankedDefuse",
                8 => "Ranked2v2",
                11 => "RankedDefuse",
                _ => lobbyModeName
            };
            int lobbyMaxPlayers = lobbyGameMode switch
            {
                5 => AlliesMatchmakingConfig.GetRequiredPlayers(),
                8 => AlliesMatchmakingConfig.GetRequiredPlayers(),
                6 => 10,
                11 => 5,
                _ => Math.Max(lobby.MaxMembers, lobby.LobbyMembers.Length)
            };
            int lobbyRoundsCount = lobbyGameMode switch
            {
                5 => 9,
                8 => 9,
                6 => 15,
                11 => 15,
                _ => 0
            };

            lobby.PhotonGame.customProperties["C0"] = lobbyPhotonMode;
            if (!string.IsNullOrWhiteSpace(ResolvePhotonMap(lobby.Data)))
            {
                lobby.PhotonGame.customProperties["C1"] = ResolvePhotonMap(lobby.Data);
            }
            lobby.PhotonGame.customProperties["max_players"] = lobbyMaxPlayers.ToString();
            lobby.PhotonGame.customProperties["game_mode"] = lobbyModeName;
            if (lobbyMaxPlayers == 2 || lobbyMaxPlayers == 4)
            {
                lobby.PhotonGame.customProperties["team_size"] = (lobbyMaxPlayers / 2).ToString();
            }
            if (lobbyRoundsCount > 0)
            {
                lobby.PhotonGame.customProperties["round_count"] = lobbyRoundsCount.ToString();
            }

            StaticClasses.Lobbies[lobby.Id] = lobby;

            foreach (var member in lobby.LobbyMembers)
            {
                MatchmakingManager.AbandonPendingForPlayer(member.Id);
                MatchmakingManager.RemoveFromQueue(member.Id);
            }

            UpdatePhotonStatus(lobby);

            var photonGameEvent = PhotonGame.GetByDocument(lobby.PhotonGame);
            Logger.Debug("[Matchmaking] start game lobby=" + lobby.Id + " roomId=" + lobby.PhotonGame.roomId + " photonMode=" + lobbyPhotonMode + " maxPlayers=" + lobbyMaxPlayers);
            if (lobby.LobbySpectators.Length > 0)
            {
                string spectIds = string.Join(",", lobby.LobbySpectators
                    .Select(s => s.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct());
                if (!string.IsNullOrEmpty(spectIds))
                    lobby.PhotonGame.customProperties["spectator_ids"] = spectIds;
            }

            foreach (string memberId in lobby.LobbyMembers.Select(m => m.Id).Distinct())
                SendFlowEventToPlayer(memberId, "onGameStarted", photonGameEvent);
            foreach (string spectId in lobby.LobbySpectators.Select(s => s.Id).Distinct())
                SendFlowEventToPlayer(spectId, "onGameStarted", photonGameEvent);

            SendOk(request.Id);
        }

        private static void SendFlowEventToPlayer(string playerId, string eventName, params object[] args)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(playerId)) return;
                if (!StaticClasses.EventSenders.TryGetValue(playerId, out List<IEventSender> eventSenders)) return;
                var sender = eventSenders.FirstOrDefault(s => s is MatchmakingFlowEventSender);
                sender?.SendEvent(eventName, args);
            }
            catch (System.Exception ex)
            {
                Logger.Error("[Matchmaking] SendFlowEventToPlayer failed event=" + eventName + " player=" + playerId + ": " + ex);
            }
        }

        private static int ResolveLobbyGameMode(IDictionary<string, string> data)
        {
            if (data == null) return 1;

            string modeStr = null;
            foreach (string key in new[] { "gameMode", "GameMode", "mode", "Mode", "game_mode" })
            {
                if (data.TryGetValue(key, out string val) && !string.IsNullOrWhiteSpace(val))
                {
                    modeStr = val.Trim();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(modeStr)) return 1;

            if (int.TryParse(modeStr, out int modeInt)) return modeInt;

            string modeLower = modeStr.ToLowerInvariant();
            if (modeLower.Contains("allies") || modeLower.Contains("2v2") || modeLower.Contains("2x2") || modeLower.Contains("\u0441\u043e\u044e\u0437\u043d\u0438\u043a"))
                return 5;
            if (modeLower.Contains("rankeddefuse") || modeLower.Contains("competitive"))
                return 6;
            if (modeLower.Contains("ranked") || modeLower.Contains("clanranked"))
                return 11;
            if (modeLower.Contains("deathmatch"))
                return 0;
            if (modeLower.Contains("armsrace"))
                return 2;
            if (modeLower.Contains("sniper"))
                return 4;
            if (modeLower.Contains("escalation"))
                return 7;
            if (modeLower.Contains("arcade"))
                return 30;
            if (modeLower.Contains("training") || modeLower.Contains("practice"))
                return 3;

            return 1;
        }

        private void FindMatchmaking(RpcRequest request)
        {
            try
            {
                if (!TryGetCurrentPlayer(out string playerId, out PlayerDocument player))
                {
                    SendErrorResponse(request.Id, CodeUnauthorized, "player_not_authenticated");
                    return;
                }

                string profile = "";
                string condition = "";

                if (TryRead(request, 0, out Axlebolt.Bolt.Protobuf.MatchmakingRequest mmReq))
                {
                    profile = mmReq.Profile ?? "";
                    condition = mmReq.Filter?.Condition ?? "";
                }
                else
                {
                    profile = ReadString(request, 0, "");
                    condition = ReadString(request, 1, "");
                }

                var availableRegions = BoltMainDatabaseProvider.Instance.GetAvailableRegionLocations();
                string region = ResolveMatchmakingRegion(profile, condition, availableRegions);

                string profileLower = profile.ToLowerInvariant();
                string conditionLower = condition.ToLowerInvariant();
                string profileNormalized = NormalizeMatchmakingToken(profileLower);
                string conditionNormalized = NormalizeMatchmakingToken(conditionLower);

                int gameMode = ResolveGameMode(profileLower, conditionLower, profileNormalized, conditionNormalized);
                Logger.Debug($"[Matchmaking] ResolveGameMode profile='{profile}' condition='{condition}' -> gameMode={gameMode}");

                int minLevel = GetMinLevelForMode(profileNormalized + " " + conditionNormalized);
                if (minLevel > 0)
                {
                    int playerLevel = Convert.ToInt32(BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "level_id"));
                    if (playerLevel < 1) playerLevel = 1;
                    if (playerLevel < minLevel)
                    {
                        // Без этого лога отказ по уровню выглядел как немой "RPCEXCEPTION 5005"
                        // у клиента (start -> cancel -> переподключение), причину было не видно.
                        Logger.Log($"[Matchmaking] start REJECTED: player {playerId} level {playerLevel} < required {minLevel} (mode from '{profile}')");
                        SendErrorResponse(request.Id, CodeJoinForbidden, $"level_too_low_required_{minLevel}");
                        return;
                    }
                }

                List<string> partyMembers = new List<string> { playerId };
                PlayerStatus status = GetStatus(playerId);
                string activeLobbyId = status.playInGame?.lobbyId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(activeLobbyId))
                {
                    if (StaticClasses.Lobbies.TryGetValue(activeLobbyId, out BoltLobby lobby)
                        && lobby.IsLobbyMember(playerId)
                        && lobby.LobbyMembers != null
                        && lobby.LobbyMembers.Length >= 2)
                    {
                        // Поиск из меню (сolo) не должен тащить «призрачное» лобби на 1 человека.
                        // В очередь группой — только если в лобби реально 2+ игрока.
                        partyMembers = lobby.LobbyMembers.Select(m => m.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        if (partyMembers.Count == 0) partyMembers = new List<string> { playerId };
                    }
                    else
                    {
                        try
                        {
                            status.playInGame ??= new PlayInGame { gameCode = string.Empty, gameVersion = StaticClasses.GetPlayerGameVersion(playerId) };
                            status.playInGame.lobbyId = string.Empty;
                            status.playInGame.lobbyName = string.Empty;
                            status.playInGame.photonGame = null;
                            SaveStatus(playerId, status);
                        }
                        catch { }
                    }
                }

                if (gameMode == 5 || gameMode == 8)
                {
                    int alliesMax = AlliesMatchmakingConfig.GetMaxPartySize();
                    if (partyMembers.Count > alliesMax)
                    {
                        Logger.LogWarn($"[Matchmaking] Allies start: party={partyMembers.Count} > max {alliesMax} ({AlliesMatchmakingConfig.ModeLabel()}); solo for {playerId}");
                        partyMembers = new List<string> { playerId };
                    }
                }

                if ((gameMode == 5 || gameMode == 8) && partyMembers.Count > 2)
                {
                    // Сбрасываем привязку к матч-лобби на 4 человек только если ушли в соло выше.
                    // Для party==4 оставляем состав.
                }
                if ((gameMode == 5 || gameMode == 8) && partyMembers.Count == 1)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(activeLobbyId)
                            && StaticClasses.Lobbies.TryGetValue(activeLobbyId, out BoltLobby bloated)
                            && bloated != null
                            && (bloated.Name ?? "").IndexOf("Match", StringComparison.OrdinalIgnoreCase) >= 0
                            && bloated.LobbyMembers != null
                            && bloated.LobbyMembers.Length > 2)
                        {
                            status.playInGame ??= new PlayInGame { gameCode = string.Empty, gameVersion = StaticClasses.GetPlayerGameVersion(playerId) };
                            status.playInGame.lobbyId = string.Empty;
                            status.playInGame.lobbyName = string.Empty;
                            status.playInGame.photonGame = null;
                            SaveStatus(playerId, status);
                            try { bloated.RemoveLobbyAny(player.GetBoltFriend()); } catch { }
                        }
                    }
                    catch { }
                }

                // Сбрасываем зависший Photon-state (иначе 32748 / PhotonStateException
                // и кнопки Отключиться/Переподключиться после прошлого матча).
                foreach (var memberId in partyMembers.Distinct())
                {
                    try
                    {
                        var st = GetStatus(memberId);
                        if (st.playInGame != null)
                        {
                            st.playInGame.photonGame = null;
                            if (!MatchmakingManager.IsSearching(memberId))
                            {
                                // не трогаем lobbyId отрядного лобби
                            }
                            SaveStatus(memberId, st);
                        }
                    }
                    catch { }
                }

                MatchmakingManager.AbandonPendingForPlayer(playerId);

                if (gameMode == 5 || gameMode == 6 || gameMode == 8)
                {
                    foreach (string memberId in partyMembers.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(memberId) || !ObjectId.TryParse(memberId, out ObjectId memberOid))
                            continue;
                        if (!GameWhitelist.IsRankedQueueAllowed(memberOid))
                        {
                            Logger.Log($"[Matchmaking] start REJECTED: ranked locked, not whitelisted player={memberId} mode={gameMode}");
                            SendErrorResponse(request.Id, CodeJoinForbidden, "ranked_queue_closed");
                            return;
                        }
                    }
                }

                StaticClasses.SetMatchmakingFlowChannel(playerId, _user);
                StaticClasses.RegisterUserService(playerId, _user);
                if (StaticClasses.TryGetPrimaryUserService(playerId, out UserService mappedPrimary)
                    && mappedPrimary != null && mappedPrimary.IsSessionAlive()
                    && !ReferenceEquals(mappedPrimary, _user))
                {
                    Logger.Log($"[Matchmaking] start on secondary TCP for {playerId}, keep lobby primary");
                }
                else
                {
                    StaticClasses.DisconnectOtherPlayerSessions(playerId, _user);
                    try { _user.InitEventSenders(playerId); } catch (System.Exception ex)
                    {
                        Logger.LogWarn("[Matchmaking] start InitEventSenders: " + ex.Message);
                    }
                }

                var (success, error) = MatchmakingManager.AddToQueue(playerId, gameMode, region, condition, partyMembers);
                if (!success)
                {
                    SendErrorResponse(request.Id, CodeBadRequest, error);
                    Logger.LogWarn($"[Matchmaking] start REJECTED 400 for {playerId}: {error}");
                    return;
                }

                MatchmakingManager.SendProgressToPlayer(playerId);
                SendOk(request.Id);
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Matchmaking] FindMatchmaking error: {ex}");
                SendErrorResponse(request.Id, CodeBadRequest, "matchmaking_error");
            }
        }

        private static string NormalizeMatchmakingToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .Replace(" ", string.Empty)
                .Replace(".", string.Empty);
        }

        private static int ResolveGameMode(string profileLower, string conditionLower, string profileNormalized, string conditionNormalized)
        {
            if (string.IsNullOrWhiteSpace(profileNormalized) && string.IsNullOrWhiteSpace(conditionNormalized))
                return 3;

            if (profileNormalized.Contains("deathmatch") || conditionNormalized.Contains("deathmatch"))
                return 0;

            if (profileNormalized.Contains("training") || profileNormalized.Contains("practice") ||
                conditionNormalized.Contains("training") || conditionNormalized.Contains("practice") ||
                profileLower.Contains("\u0442\u0440\u0435\u043D") || conditionLower.Contains("\u0442\u0440\u0435\u043D"))
                return 3;

            if (profileNormalized.Contains("allies") || profileNormalized.Contains("2v2") || profileNormalized.Contains("2x2") ||
                conditionNormalized.Contains("allies") || conditionNormalized.Contains("2v2") || conditionNormalized.Contains("2x2") ||
                profileLower.Contains("\u0441\u043e\u044e\u0437\u043d\u0438\u043a") || conditionLower.Contains("\u0441\u043e\u044e\u0437\u043d\u0438\u043a"))
                return 5;

            if (profileNormalized.Contains("clanrankeddefuse") || profileNormalized.Contains("clanranked") || profileNormalized.Contains("clanbattle") ||
                conditionNormalized.Contains("clanrankeddefuse") || conditionNormalized.Contains("clanranked") || conditionNormalized.Contains("clanbattle"))
                return 11;

            if (profileNormalized.Contains("rankeddefuse") || profileNormalized.Contains("competitive") ||
                conditionNormalized.Contains("rankeddefuse") || conditionNormalized.Contains("competitive") ||
                profileLower.Contains("\u0441\u043E\u0440\u0435\u0432\u043D\u043E\u0432") || conditionLower.Contains("\u0441\u043E\u0440\u0435\u0432\u043D\u043E\u0432"))
                return 6;

            if (profileNormalized.Contains("ranked") || conditionNormalized.Contains("ranked"))
                return 6;

            if (profileNormalized.Contains("defuse") || conditionNormalized.Contains("defuse"))
                return 1;

            if (profileNormalized.Contains("armsrace") || conditionNormalized.Contains("armsrace"))
                return 2;

            if (profileNormalized.Contains("sniperduel") || conditionNormalized.Contains("sniperduel"))
                return 4;

            if (profileNormalized.Contains("escalation") || conditionNormalized.Contains("escalation"))
                return 7;

            if (profileNormalized.Contains("arcade") || conditionNormalized.Contains("arcade"))
                return 30;

            if (string.IsNullOrWhiteSpace(profileNormalized) &&
                (conditionNormalized.Contains("sandstone") || conditionNormalized.Contains("rust") ||
                 conditionNormalized.Contains("province") || conditionNormalized.Contains("sakura") ||
                 conditionNormalized.Contains("zone9")))
                return 3;

            return 0;
        }

        private void CancelMatchmaking(RpcRequest request)
        {
            if (!TryGetCurrentPlayer(out string playerId, out PlayerDocument player))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "player_not_authenticated");
                return;
            }

            Logger.Log($"[Matchmaking] CancelMatchmaking by player {playerId}");
            StaticClasses.PromoteMatchmakingPrimary(_user, playerId);

            // Сохраняем lobbyId — иначе после отмены поиска игрок вылетает из лобби союзников.
            // НО: матч-лобби на 3–4 игроков (после FinalizeMatch) сохранять нельзя —
            // следующий Allies start даёт party>2 → RPC 400.
            string keepLobbyId = string.Empty;
            string keepLobbyName = string.Empty;
            try
            {
                var status = GetStatus(playerId);
                keepLobbyId = status.playInGame?.lobbyId ?? string.Empty;
                keepLobbyName = status.playInGame?.lobbyName ?? string.Empty;
                if (!string.IsNullOrEmpty(keepLobbyId)
                    && StaticClasses.Lobbies.TryGetValue(keepLobbyId, out BoltLobby keepLobby))
                {
                    int members = keepLobby.LobbyMembers?.Length ?? 0;
                    bool isMatchLobby = (keepLobby.Name ?? "").IndexOf("Match", StringComparison.OrdinalIgnoreCase) >= 0
                        || (keepLobbyName.IndexOf("Ranked2v2", StringComparison.OrdinalIgnoreCase) >= 0)
                        || (keepLobbyName.IndexOf("Match", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (isMatchLobby || members > 2)
                    {
                        Logger.Log($"[Matchmaking] Cancel: dropping match/bloated lobby {keepLobbyId} (members={members}, name='{keepLobby.Name}')");
                        try { keepLobby.RemoveLobbyAny(player.GetBoltFriend()); } catch { }
                        if ((keepLobby.LobbyMembers?.Length ?? 0) == 0)
                            StaticClasses.Lobbies.TryRemove(keepLobbyId, out _);
                        keepLobbyId = string.Empty;
                        keepLobbyName = string.Empty;
                    }
                }
            }
            catch { }

            MatchmakingManager.AbandonPendingForPlayer(playerId);
            MatchmakingManager.RemoveFromQueue(playerId, preserveLobbyId: keepLobbyId, preserveLobbyName: keepLobbyName);

            try
            {
                var status = GetStatus(playerId);
                if (status.playInGame != null)
                {
                    status.playInGame.lobbyId = keepLobbyId;
                    status.playInGame.lobbyName = keepLobbyName;
                    status.playInGame.photonGame = null;
                    status.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
                    SaveStatus(playerId, status);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error("[Matchmaking] CancelMatchmaking status cleanup failed: " + ex.Message);
            }

            SendOk(request.Id);
        }

        private void ConfirmMatch(RpcRequest request)
        {
            if (!TryGetCurrentPlayer(out string playerId, out PlayerDocument player))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "player_not_authenticated");
                return;
            }

            Logger.Log($"[Matchmaking] ConfirmMatch by playerId={playerId}");
            // Запоминаем TCP confirm для matchmaking-событий; лобби-primary не трогаем.
            if (_user != null && _user.IsSessionAlive())
            {
                StaticClasses.SetMatchmakingFlowChannel(playerId, _user);
                StaticClasses.RegisterUserService(playerId, _user);
            }
            else
            {
                StaticClasses.PromoteLiveFlowSession(playerId);
            }
            MatchmakingManager.ConfirmPlayer(playerId);
            SendOk(request.Id);
        }

        private void AbandonMatch(RpcRequest request)
        {
            if (!TryGetCurrentPlayer(out string playerId, out PlayerDocument player))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "player_not_authenticated");
                return;
            }

            bool wasPendingAllies = MatchmakingManager.HasPendingMatch(playerId);

            PlayerStatus status = GetStatus(playerId);
            string gameMode = "ranked";
            string lobbyName = status.playInGame?.lobbyName ?? string.Empty;
            if (wasPendingAllies
                || lobbyName.Contains("2v2", StringComparison.OrdinalIgnoreCase)
                || lobbyName.Contains("Allies", StringComparison.OrdinalIgnoreCase)
                || lobbyName.Contains("Ranked2v2", StringComparison.OrdinalIgnoreCase))
            {
                gameMode = "allies";
            }

            // Штраф только если игрок УЖЕ подключился к Photon-игре (lobbyId != null
            // и в StaticClasses.Lobbies). Если он ещё на экране подтверждения — это не
            // считается покиданием, иначе клиент получит PhotonStateException при попытке
            // отключиться от Photon-клиента, в который он не входил.
            bool wasInPhotonGame = !string.IsNullOrEmpty(status.playInGame?.lobbyId) &&
                                   StaticClasses.Lobbies.ContainsKey(status.playInGame.lobbyId);
            if (wasInPhotonGame)
            {
                PlayerStatsManager.AddLoss(playerId, gameMode);
            }

            // Убираем из pending-матча (если в нём) — корректно отменяет для остальных.
            // НЕ вызываем RemoveFromQueue, потому что для pending-матча нужна другая логика
            // (см. AbandonPendingForPlayer).
            MatchmakingManager.AbandonPendingForPlayer(playerId);
            MatchmakingManager.RemoveFromQueue(playerId);
            try { LeaveLobbyInternal(playerId, player, false, true); } catch { }

            status = GetStatus(playerId);
            if (status.playInGame != null)
            {
                status.playInGame.lobbyId = string.Empty;
                status.playInGame.photonGame = null;
                status.playInGame.lobbyName = string.Empty;
                status.playInGame.gameCode = string.Empty;
                status.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
                SaveStatus(playerId, status);
            }

            SendOk(request.Id);
        }

        private void ReconnectMatch(RpcRequest request)
        {
            if (!TryGetCurrentPlayer(out string playerId, out PlayerDocument player))
            {
                SendErrorResponse(request.Id, CodeUnauthorized, "player_not_authenticated");
                return;
            }

            PlayerStatus status = GetStatus(playerId);
            string lobbyId = status.playInGame?.lobbyId ?? string.Empty;

            if (!string.IsNullOrEmpty(lobbyId) && StaticClasses.Lobbies.TryGetValue(lobbyId, out BoltLobby lobby))
            {
                SendReturn(request.Id, lobby.ToOriginal(playerId));
                return;
            }

            SendErrorResponse(request.Id, CodeLobbyNotFound, "no_active_match_to_reconnect");
        }

        private static string GetActiveGameMode(BoltLobby lobby)
        {
            if (lobby?.PhotonGame?.customProperties == null) return string.Empty;
            foreach (string key in new[] { "gameMode", "GameMode", "mode", "Mode", "game_mode" })
            {
                if (lobby.PhotonGame.customProperties.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return string.Empty;
        }

private static int GetMinLevelForMode(string mode)
        {
            return 0;
        }

        private bool LeaveLobbyInternal(string playerId, PlayerDocument player, bool notify, bool clearStatus)
        {
            PlayerStatus status = GetStatus(playerId);
            string lobbyId = status.playInGame?.lobbyId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(lobbyId) || !StaticClasses.Lobbies.TryGetValue(lobbyId, out BoltLobby lobby))
            {
                if (clearStatus && status.playInGame != null)
                {
                    status.playInGame.lobbyId = string.Empty;
                    status.playInGame.photonGame = null;
                    status.playInGame.lobbyName = string.Empty;
                    status.playInGame.gameCode = string.Empty;
                    status.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
                    SaveStatus(playerId, status);
                }
                return false;
            }

            lobby.RemoveLobbyAnyById(playerId);
            lobby.RemoveLobbyAnyInviteById(playerId);

            if (clearStatus && status.playInGame != null)
            {
                status.playInGame.lobbyId = string.Empty;
                status.playInGame.photonGame = null;
                SaveStatus(playerId, status);
            }

            if (lobby.LobbyMembers.Length == 0 && lobby.LobbySpectators.Length == 0)
            {
                StaticClasses.Lobbies.TryRemove(lobby.Id, out _);
                return true;
            }

            if (string.IsNullOrWhiteSpace(lobby.LobbyOwnerId))
                lobby.SetOwner(lobby.LobbyMembers.Concat(lobby.LobbySpectators).First().Id);

            StaticClasses.Lobbies[lobby.Id] = lobby;

            if (notify)
            {
                BroadcastLobby(lobby, "onPlayerLeftLobby", playerId);
                BroadcastLobby(lobby, "onLobbyOwnerChanged", lobby.LobbyOwnerId);
            }

            return true;
        }

        private bool TryGetCurrentLobby(out string playerId, out PlayerDocument player, out BoltLobby lobby)
        {
            lobby = null;
            if (!TryGetCurrentPlayer(out playerId, out player)) return false;
            PlayerStatus status = GetStatus(playerId);
            string lobbyId = status.playInGame?.lobbyId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(lobbyId))
            {
                if (TryReattachPlayerLobby(playerId, player, out lobby))
                    return true;
                return false;
            }

            if (!StaticClasses.Lobbies.TryGetValue(lobbyId, out lobby))
            {
                if (TryReattachPlayerLobby(playerId, player, out lobby))
                    return true;
                ClearStaleLobbyReference(playerId, status);
                lobby = null;
                return false;
            }

            if (!lobby.IsLobbyAny(playerId))
            {
                if (TryReattachPlayerLobby(playerId, player, out lobby))
                    return true;
                Logger.Debug("[Matchmaking] stale lobby ref player=" + playerId + " lobby=" + lobbyId + " — clearing");
                ClearStaleLobbyReference(playerId, status);
                lobby = null;
                return false;
            }

            EnsureCustomLobbyDefaults(lobby);
            RepairLobbyOwnership(lobby);
            StaticClasses.Lobbies[lobby.Id] = lobby;
            return true;
        }

        private bool TryReattachPlayerLobby(string playerId, PlayerDocument player, out BoltLobby lobby)
        {
            lobby = null;
            if (string.IsNullOrWhiteSpace(playerId))
                return false;

            foreach (KeyValuePair<string, BoltLobby> entry in StaticClasses.Lobbies)
            {
                BoltLobby candidate = entry.Value;
                if (candidate == null || !candidate.IsLobbyAny(playerId))
                    continue;

                lobby = candidate;
                PlayerStatus status = GetStatus(playerId);
                status.playInGame ??= new PlayInGame { gameCode = "standoff2", gameVersion = DefaultGameVersion, lobbyId = string.Empty };
                status.playInGame.lobbyId = lobby.Id;
                status.playInGame.lobbyName = lobby.Name;
                SaveStatus(playerId, status);
                EnsureCustomLobbyDefaults(lobby);
                RepairLobbyOwnership(lobby);
                StaticClasses.Lobbies[lobby.Id] = lobby;
                Logger.Debug("[Matchmaking] reattached lobby id=" + lobby.Id + " for player=" + playerId);
                return true;
            }

            return false;
        }

        private void ClearStaleLobbyReference(string playerId, PlayerStatus status)
        {
            if (status?.playInGame == null) return;
            status.playInGame.lobbyId = string.Empty;
            status.playInGame.lobbyName = string.Empty;
            status.playInGame.photonGame = null;
            SaveStatus(playerId, status);
        }

        private static bool IsProtectedLobbyDataKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            return key.Equals("gameMode", StringComparison.OrdinalIgnoreCase)
                || key.Equals("GameMode", StringComparison.OrdinalIgnoreCase)
                || key.Equals("mode", StringComparison.OrdinalIgnoreCase)
                || key.Equals("map", StringComparison.OrdinalIgnoreCase)
                || key.Equals("level", StringComparison.OrdinalIgnoreCase)
                || key.Equals("levelName", StringComparison.OrdinalIgnoreCase)
                || key.Equals("region", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Region", StringComparison.OrdinalIgnoreCase)
                || key.Equals("serverRegion", StringComparison.OrdinalIgnoreCase)
                || key.Equals("SearchingRegion", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureCustomLobbyDefaults(BoltLobby lobby)
        {
            if (lobby?.Data == null) return;
            string region = BoltMainDatabaseProvider.MainServerRegion;
            void Def(string key, string value)
            {
                if (!lobby.Data.TryGetValue(key, out string cur) || string.IsNullOrWhiteSpace(cur))
                    lobby.Data[key] = value;
            }

            Def("gameMode", "1");
            Def("GameMode", "1");
            Def("mode", "1");
            Def("map", "sandstone");
            Def("level", "sandstone");
            Def("levelName", "sandstone");
            Def("region", region);
            Def("Region", region);
            Def("serverRegion", region);
            Def("SearchingRegion", region);
        }

        private bool TryGetCurrentPlayer(out string playerId, out PlayerDocument player)
        {
            player = null;
            playerId = PlayerId;
            if (string.IsNullOrWhiteSpace(playerId)) return false;
            return TryGetPlayer(playerId, out player);
        }

        private bool TryGetPlayer(string playerId, out PlayerDocument player)
        {
            player = null;
            if (!ObjectId.TryParse(playerId, out ObjectId objectId)) return false;
            try
            {
                player = BoltMainDatabaseProvider.Instance.GetPlayerDocument(objectId);
                return player != null;
            }
            catch { return false; }
        }

        private PlayerStatus GetStatus(string playerId)
        {
            PlayerStatus status = StaticClasses.PlayersStatus.GetOrAdd(playerId, _ => new PlayerStatus());
            if (status.playInGame == null)
            {
                status.playInGame = new PlayInGame
                {
                    gameCode = "standoff2",
                    gameVersion = DefaultGameVersion,
                    lobbyId = string.Empty
                };
            }
            return status;
        }

        private void SaveStatus(string playerId, PlayerStatus status)
        {
            StaticClasses.PlayersStatus[playerId] = status;
            if (ObjectId.TryParse(playerId, out ObjectId objectId))
                BoltMainDatabaseProvider.Instance.SetPlayerStatus(objectId, status);
        }

        private static GameServer CreateDefaultGameServer()
        {
            return new GameServer
            {
                Id = DefaultPhotonServerId,
                Ip = DefaultPhotonServerIp,
                Port = DefaultPhotonServerPort
            };
        }

        private static GameServer NormalizeGameServer(GameServer server)
        {
            if (server == null)
                return CreateDefaultGameServer();

            return new GameServer
            {
                Id = string.IsNullOrWhiteSpace(server.Id) ? DefaultPhotonServerId : server.Id,
                Ip = string.IsNullOrWhiteSpace(server.Ip) ? DefaultPhotonServerIp : server.Ip,
                Port = server.Port <= 0 ? DefaultPhotonServerPort : server.Port
            };
        }

        private static GameServerDetails CreateDefaultGameServerDetails(string map = "")
        {
            GameServer server = CreateDefaultGameServer();
            return new GameServerDetails
            {
                Id = server.Id,
                GameServer = server,
                Map = map ?? string.Empty,
                CurrentPlayers = 0,
                MaxPlayers = 5,
                RequirePassword = false,
                Version = DefaultGameVersion,
                SuccessfulResponse = true,
                DoNotRefresh = false
            };
        }

        private static string DescribeGameServer(GameServer server)
        {
            if (server == null) return "null";
            return (server.Id ?? string.Empty) + "/" + (server.Ip ?? string.Empty) + ":" + server.Port;
        }

        private static string DescribePhotonGame(PhotonGame photonGame)
        {
            if (photonGame == null) return "null";
            return (photonGame.region ?? string.Empty) + "/" +
                (photonGame.roomId ?? string.Empty) + "/" +
                (photonGame.appVersion ?? string.Empty);
        }

        private bool CanJoin(BoltLobby lobby, PlayerDocument player, string playerId, LobbyPlayerType type)
        {
            if (lobby == null || player == null) { Logger.Debug($"[CanJoin] FAIL: lobby={lobby != null}, player={player != null}"); return false; }
            if (lobby.IsLobbyAny(playerId)) return true;
            if (type == LobbyPlayerType.Spectator && lobby.MaxSpectators <= 0) { Logger.Debug($"[CanJoin] FAIL: spectator but MaxSpectators={lobby.MaxSpectators}"); return false; }
            if (lobby.IsMemberInvited(playerId)) return true;
            if (lobby.IsSpectatorInvited(playerId)) return true;
            if (lobby.Joinable) return true;
            return false;
        }

        private bool IsLobbyOwner(BoltLobby lobby, string playerId)
        {
            if (lobby == null || string.IsNullOrWhiteSpace(playerId))
                return false;
            RepairLobbyOwnership(lobby);
            if (BoltLobby.IdEquals(lobby.LobbyOwnerId, playerId))
                return true;
            if (lobby.Data != null && lobby.Data.TryGetValue("owner", out string dataOwner)
                && BoltLobby.IdEquals(dataOwner, playerId))
                return true;
            if (lobby.LobbyMembers.Length == 1 && lobby.IsLobbyMember(playerId))
                return true;
            return false;
        }

        private static void RepairLobbyOwnership(BoltLobby lobby)
        {
            if (lobby == null)
                return;

            if (lobby.Data != null && lobby.Data.TryGetValue("owner", out string dataOwner)
                && !string.IsNullOrWhiteSpace(dataOwner)
                && lobby.IsLobbyMember(dataOwner))
            {
                lobby.SetOwner(dataOwner);
            }
            else if (lobby.LobbyMembers.Length == 1)
            {
                lobby.SetOwner(lobby.LobbyMembers[0].Id);
                if (lobby.Data != null)
                    lobby.Data["owner"] = lobby.LobbyMembers[0].Id;
            }
            else if (!string.IsNullOrWhiteSpace(lobby.LobbyOwnerId) && !lobby.IsLobbyAny(lobby.LobbyOwnerId))
            {
                if (lobby.LobbyMembers.Length > 0)
                {
                    lobby.SetOwner(lobby.LobbyMembers[0].Id);
                    if (lobby.Data != null)
                        lobby.Data["owner"] = lobby.LobbyMembers[0].Id;
                }
            }
        }

        private void BroadcastLobbyInitialSync(BoltLobby lobby)
        {
            if (lobby?.Data == null)
                return;

            ProtoDictionary changed = new ProtoDictionary();
            foreach (KeyValuePair<string, string> kv in lobby.Data)
                changed.Content[kv.Key] = kv.Value ?? string.Empty;

            BroadcastLobby(lobby, "onLobbyDataChanged", changed);
            if (lobby.PhotonGame != null)
                BroadcastLobby(lobby, "onLobbyPhotonGameChanged", lobby.PhotonGame);
            BroadcastLobby(lobby, "onLobbyJoinableChanged", lobby.Joinable);
            LobbyType clientType = lobby.Type switch
            {
                BoltLobby.LobbyType.Public => LobbyType.Public,
                BoltLobby.LobbyType.FriendsOnly => LobbyType.FriendsOnly,
                BoltLobby.LobbyType.Invisible => LobbyType.Invisible,
                _ => LobbyType.Private
            };
            BroadcastLobby(lobby, "onLobbyTypeChanged", clientType);
            BroadcastLobby(lobby, "onLobbyOwnerChanged", lobby.LobbyOwnerId);
        }

        private static BoltLobby.LobbyType ConvertLobbyType(LobbyType type)
        {
            return type switch
            {
                LobbyType.FriendsOnly => BoltLobby.LobbyType.FriendsOnly,
                LobbyType.Public => BoltLobby.LobbyType.Public,
                LobbyType.Invisible => BoltLobby.LobbyType.Invisible,
                _ => BoltLobby.LobbyType.Private
            };
        }

        private bool MatchFilter(BoltLobby lobby, Filter filter)
        {
            if (filter == null || string.IsNullOrWhiteSpace(filter.Name)) return true;
            string key = filter.Name;
            string value = GetLobbyFilterValue(lobby, key);

            if (!string.IsNullOrWhiteSpace(filter.StringValue))
                return CompareString(value, filter.StringValue, filter.Comparison);

            if (int.TryParse(value, out int intValue))
                return CompareNumber(intValue, filter.IntValue, filter.Comparison);

            if (float.TryParse(value, out float floatValue))
                return CompareNumber(floatValue, filter.FloatValue, filter.Comparison);

            return true;
        }

        private string GetLobbyFilterValue(BoltLobby lobby, string key)
        {
            if (key.Equals("id", StringComparison.OrdinalIgnoreCase)) return lobby.Id;
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase)) return lobby.Name;
            if (key.Equals("lobbyType", StringComparison.OrdinalIgnoreCase) || key.Equals("type", StringComparison.OrdinalIgnoreCase))
                return ((int)lobby.Type).ToString();
            if (key.Equals("members", StringComparison.OrdinalIgnoreCase) || key.Equals("currentPlayers", StringComparison.OrdinalIgnoreCase))
                return lobby.LobbyMembers.Length.ToString();
            if (key.Equals("spectators", StringComparison.OrdinalIgnoreCase))
                return lobby.LobbySpectators.Length.ToString();
            if (key.Equals("maxMembers", StringComparison.OrdinalIgnoreCase) || key.Equals("maxPlayers", StringComparison.OrdinalIgnoreCase))
                return lobby.MaxMembers.ToString();
            if (key.Equals("maxSpectators", StringComparison.OrdinalIgnoreCase))
                return lobby.MaxSpectators.ToString();
            if (lobby.Data != null && lobby.Data.TryGetValue(key, out string dataValue))
                return dataValue ?? string.Empty;
            return string.Empty;
        }

        private bool CompareString(string source, string expected, Comparison comparison)
        {
            source ??= string.Empty;
            expected ??= string.Empty;
            int equals = string.Compare(source, expected, StringComparison.OrdinalIgnoreCase);
            return comparison switch
            {
                Comparison.Equal => equals == 0,
                Comparison.NotEqual => equals != 0,
                Comparison.LessThan => equals < 0,
                Comparison.EqualToOrLessThan => equals <= 0,
                Comparison.GreaterThan => equals > 0,
                Comparison.EqualToOrGreaterThan => equals >= 0,
                _ => source.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0
            };
        }

        private bool CompareNumber(double source, double expected, Comparison comparison)
        {
            return comparison switch
            {
                Comparison.Equal => Math.Abs(source - expected) < 0.0001,
                Comparison.NotEqual => Math.Abs(source - expected) >= 0.0001,
                Comparison.LessThan => source < expected,
                Comparison.EqualToOrLessThan => source <= expected,
                Comparison.GreaterThan => source > expected,
                Comparison.EqualToOrGreaterThan => source >= expected,
                _ => true
            };
        }

        private void UpdatePhotonStatus(BoltLobby lobby)
        {
            if (lobby.PhotonGame != null && lobby.Data != null && lobby.Data.Count > 0)
            {
                lobby.PhotonGame.customProperties ??= new System.Collections.Generic.Dictionary<string, string>();
                foreach (var kv in lobby.Data)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                    lobby.PhotonGame.customProperties[kv.Key] = kv.Value;
                }

                if (TryGetDataValue(lobby.Data, out string dataRegion, "region", "Region", "serverRegion", "photonRegion"))
                {
                    lobby.PhotonGame.region = NormalizeRegion(dataRegion);
                }
            }

            foreach (string playerId in lobby.LobbyMembers
                .Concat(lobby.LobbySpectators)
                .Select(player => player.Id)
                .Distinct())
            {
                PlayerStatus status = GetStatus(playerId);
                status.playInGame.lobbyId = lobby.Id;
                status.playInGame.lobbyName = lobby.Name;
                status.playInGame.photonGame = lobby.PhotonGame;
                SaveStatus(playerId, status);
            }
        }

        private static void BroadcastStatusToFriends(string playerId, PlayerStatus status)
        {
            try
            {
                var protoStatus = status.GetPlayerStatusProto();
                var friends = StandRiseServer.MongoDB.Main.FriendHelper.GetPlayerFriends(playerId);
                if (friends == null) return;
                foreach (var friend in friends)
                {
                    if (friend?.Player == null) continue;
                    if (friend.RelationshipStatus != Axlebolt.Bolt.Protobuf.RelationshipStatus.Friend) continue;
                    string friendId = friend.Player.Id;
                    if (string.IsNullOrWhiteSpace(friendId)) continue;
                    if (StaticClasses.EventSenders.TryGetValue(friendId, out var senders))
                    {
                        var friendsSender = senders.FirstOrDefault(s => s is FriendsRemoteEventSender);
                        friendsSender?.SendEvent("onPlayerStatusChanged", new object[] { playerId, protoStatus });
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[Matchmaking] BroadcastStatusToFriends error: {ex.Message}");
            }
        }

        private void BroadcastLobby(BoltLobby lobby, string eventName, params object[] args)
        {
            foreach (string playerId in lobby.LobbyMembers
                .Concat(lobby.LobbySpectators)
                .Select(player => player.Id)
                .Distinct())
            {
                SendEvent(playerId, eventName, args);
            }
        }

        private static void SendEvent(string playerId, string eventName, params object[] args)
        {
            SendMatchmakingPlayerEvent(playerId, eventName, args);
        }

        /// <summary>Шлёт lobby/matchmaking events на все живые TCP-сессии игрока (primary + search).</summary>
        private static void SendMatchmakingPlayerEvent(string playerId, string eventName, params object[] args)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(playerId))
                    return;

                HashSet<IEventSender> dispatched = new HashSet<IEventSender>();
                foreach (KeyValuePair<string, List<IEventSender>> kvp in StaticClasses.EventSenders)
                {
                    if (!BoltLobby.IdEquals(kvp.Key, playerId))
                        continue;
                    foreach (IEventSender sender in kvp.Value)
                    {
                        if (sender is MatchmakingEventSender && dispatched.Add(sender))
                            sender.SendEvent(eventName, args);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error("[Matchmaking] SendEvent failed event=" + eventName + " player=" + playerId + ": " + ex);
            }
        }

        private static void ApplyLobbyDataToPhotonGame(BoltLobby lobby)
        {
            if (lobby?.Data == null || lobby.Data.Count == 0)
            {
                return;
            }

            lobby.PhotonGame ??= new PhotonGame
            {
                region = BoltMainDatabaseProvider.MainServerRegion,
                roomId = string.Empty,
                appVersion = DefaultGameVersion,
                customProperties = new System.Collections.Generic.Dictionary<string, string>()
            };

            lobby.PhotonGame.customProperties ??= new System.Collections.Generic.Dictionary<string, string>();

            if (TryGetDataValue(lobby.Data, out string region, "region", "Region", "serverRegion", "photonRegion"))
            {
                lobby.PhotonGame.region = NormalizeRegion(region);
            }
            else if (string.IsNullOrWhiteSpace(lobby.PhotonGame.region))
            {
                lobby.PhotonGame.region = BoltMainDatabaseProvider.MainServerRegion;
            }

            if (string.IsNullOrWhiteSpace(lobby.PhotonGame.roomId))
            {
                lobby.PhotonGame.roomId = lobby.Id;
            }

            if (string.IsNullOrWhiteSpace(lobby.PhotonGame.appVersion))
            {
                lobby.PhotonGame.appVersion = DefaultGameVersion;
            }

            string photonMode = ResolvePhotonMode(lobby.Data);
            string photonMap = ResolvePhotonMap(lobby.Data);
            lobby.PhotonGame.customProperties["C0"] = photonMode;
            lobby.PhotonGame.customProperties["C1"] = photonMap;
            lobby.PhotonGame.customProperties["region"] = lobby.PhotonGame.region;

            foreach (var kv in lobby.Data)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null)
                {
                    continue;
                }

                if (IsClientLobbyOptionKey(kv.Key))
                {
                    continue;
                }

                lobby.PhotonGame.customProperties[kv.Key] = kv.Value;
            }
        }

        private static string ResolvePhotonMode(IDictionary<string, string> data)
        {
            if (TryGetDataValue(data, out string mode, "mode", "gameMode", "GameMode", "C0", "C1") && !string.IsNullOrWhiteSpace(mode))
            {
                return mode.Trim();
            }

            if (data != null && data.TryGetValue("LobbyOptions", out string lobbyOptionsJson)
                && !string.IsNullOrWhiteSpace(lobbyOptionsJson))
            {
                string fromJson = TryParseGameModeFromLobbyOptions(lobbyOptionsJson);
                if (!string.IsNullOrWhiteSpace(fromJson))
                    return fromJson;
            }

            return "Defuse";
        }

        private static string TryParseGameModeFromLobbyOptions(string lobbyOptionsJson)
        {
            try
            {
                int idx = lobbyOptionsJson.IndexOf("GameMode", StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return string.Empty;
                int q1 = lobbyOptionsJson.IndexOf('"', idx + 8);
                if (q1 < 0)
                    return string.Empty;
                int q2 = lobbyOptionsJson.IndexOf('"', q1 + 1);
                if (q2 <= q1)
                    return string.Empty;
                return lobbyOptionsJson.Substring(q1 + 1, q2 - q1 - 1).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ResolvePhotonMap(IDictionary<string, string> data)
        {
            if (TryGetDataValue(data, out string map, "map", "level", "levelName", "C2") && !string.IsNullOrWhiteSpace(map))
            {
                return map.Trim();
            }

            return "sandstone";
        }

        private static bool IsClientLobbyOptionKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length < 2 || key[0] != 'C')
            {
                return false;
            }

            for (int i = 1; i < key.Length; i++)
            {
                if (!char.IsDigit(key[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetPhotonProperty(BoltLobby lobby, string key)
        {
            if (lobby?.PhotonGame?.customProperties == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return lobby.PhotonGame.customProperties.TryGetValue(key, out string value) ? value ?? string.Empty : string.Empty;
        }

        private static bool TryGetDataValue(IDictionary<string, string> data, out string value, params string[] keys)
        {
            value = string.Empty;
            if (data == null) return false;

            foreach (string key in keys)
            {
                if (data.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
                {
                    return true;
                }
            }

            foreach (var kv in data)
            {
                if (keys.Any(key => string.Equals(key, kv.Key, StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(kv.Value))
                {
                    value = kv.Value;
                    return true;
                }
            }

            return false;
        }

        private static string ResolveMatchmakingRegion(string profile, string condition, List<string> availableRegions)
        {
            // Один физический сервер — всегда msk, независимо от профиля/condition клиента.
            return BoltMainDatabaseProvider.MainServerRegion;
        }

        private static string ExtractRegionPrefix(string profile)
        {
            if (string.IsNullOrWhiteSpace(profile)) return null;
            int idx = profile.IndexOf('_');
            if (idx <= 0) return null;
            return profile.Substring(0, idx).Trim().ToLowerInvariant();
        }

        private static string NormalizeRegion(string region)
        {
            return BoltMainDatabaseProvider.MainServerRegion;
        }

        private T ReadMessage<T>(RpcRequest request, int index, T fallback) where T : IMessage<T>, new()
        {
            return TryRead(request, index, out T value) ? value : fallback;
        }

        private string ReadString(RpcRequest request, int index, string fallback)
        {
            return TryRead(request, index, out string value) ? value : fallback;
        }

        private int ReadInt(RpcRequest request, int index, int fallback)
        {
            return TryRead(request, index, out int value) ? value : fallback;
        }

        private bool ReadBool(RpcRequest request, int index, bool fallback)
        {
            return TryRead(request, index, out bool value) ? value : fallback;
        }

        private TEnum ReadEnum<TEnum>(RpcRequest request, int index, TEnum fallback) where TEnum : struct, System.Enum
        {
            return TryReadEnum(request, index, out TEnum value) ? value : fallback;
        }

        private LobbyPlayerType ReadLobbyPlayerType(RpcRequest request, int index, LobbyPlayerType fallback)
        {
            if (TryReadEnum(request, index, out LobbyPlayerType enumValue) && enumValue != LobbyPlayerType.Any)
                return enumValue;
            if (TryRead(request, index, out int intValue) && System.Enum.IsDefined(typeof(LobbyPlayerType), intValue))
                return (LobbyPlayerType)intValue;
            if (request != null && request.Params.Count > index && request.Params[index]?.One != null)
            {
                var wrapped = ChangeLobbyOtherPlayerTypeRequest.ParseLobbyPlayerTypeFromWrappedEnum(request.Params[index].One);
                if (wrapped != LobbyPlayerType.Any) return wrapped;
            }
            return fallback;
        }

        private LobbyPlayerType ResolveLobbyPlayerType(LobbyPlayerType fromMessage, RpcRequest request, int extraParamIndex)
        {
            if (fromMessage != LobbyPlayerType.Any) return fromMessage;
            if (request != null && request.Params.Count > extraParamIndex)
                return ReadLobbyPlayerType(request, extraParamIndex, LobbyPlayerType.Member);
            return LobbyPlayerType.Member;
        }

        private void SendWrappedMessageResponse(string responseId, int fieldNumber, IMessage message)
        {
            byte[] payload;
            using (var ms = new System.IO.MemoryStream())
            {
                var output = new CodedOutputStream(ms);
                output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
                output.WriteMessage(message);
                output.Flush();
                payload = ms.ToArray();
            }

            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = responseId,
                    Return = new BinaryValue
                    {
                        IsNull = false,
                        One = ByteString.CopyFrom(payload)
                    }
                }
            });
        }

        private void SendWrappedRepeatedResponse<T>(string responseId, int fieldNumber, IEnumerable<T> messages) where T : IMessage
        {
            byte[] payload;
            using (var ms = new System.IO.MemoryStream())
            {
                var output = new CodedOutputStream(ms);
                foreach (var message in messages ?? Enumerable.Empty<T>())
                {
                    if (message == null) continue;
                    output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
                    output.WriteMessage(message);
                }
                output.Flush();
                payload = ms.ToArray();
            }

            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = responseId,
                    Return = new BinaryValue
                    {
                        IsNull = false,
                        One = ByteString.CopyFrom(payload)
                    }
                }
            });
        }

        private bool TryRead<T>(RpcRequest request, int index, out T value)
        {
            value = default;
            if (request == null || request.Params.Count <= index ||
                request.Params[index] == null || request.Params[index].IsNull)
                return false;
            try
            {
                value = (T)new FromByteMethod(typeof(T)).FromBytes(request.Params[index]);
                return true;
            }
            catch { return false; }
        }

        private bool TryReadEnum<TEnum>(RpcRequest request, int index, out TEnum value) where TEnum : struct, System.Enum
        {
            value = default;
            if (request == null || request.Params.Count <= index ||
                request.Params[index] == null || request.Params[index].IsNull)
                return false;
            try
            {
                object parsed = new EnumFromByteMethod(typeof(TEnum)).FromBytes(request.Params[index]);
                if (parsed is TEnum enumValue)
                {
                    value = enumValue;
                    return true;
                }
            }
            catch
            {
                try
                {
                    ProtoEnum protoEnum = ProtoEnum.Parser.ParseFrom(request.Params[index].One);
                    value = (TEnum)System.Enum.ToObject(typeof(TEnum), protoEnum.Value);
                    return true;
                }
                catch { }
            }
            return false;
        }

        private void SendReturn<T>(string responseId, T value)
        {
            BinaryValue binaryValue;
            if (value is IMessage message)
                binaryValue = new BinaryValue { IsNull = false, One = message.ToByteString() };
            else
                binaryValue = ProtoReflectionUtils.CreateToByteMethod(typeof(T)).ToBytes(value);

            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = responseId,
                    Return = binaryValue
                }
            });
        }

        private void SendOk(string responseId)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = responseId,
                    Return = new BinaryValue { IsNull = false, One = ByteString.Empty }
                }
            });
        }

        private void SendErrorResponse(string responseId, int code, string reason)
        {
            RpcException exception = new RpcException
            {
                Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8),
                Code = code
            };
            if (!string.IsNullOrWhiteSpace(reason))
                exception.Property["reason"] = reason;
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = responseId,
                    Exception = exception
                }
            });
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        internal static PlayerDocument GetPlayerDocument(string playerId)
        {
            if (ObjectId.TryParse(playerId, out ObjectId objectId))
            {
                try { return BoltMainDatabaseProvider.Instance.GetPlayerDocument(objectId); }
                catch { }
            }
            return null;
        }

        public class MatchmakingEventSender : IEventSender
        {
            private readonly UserService User;
            public string eventListenerName { get; set; } = "MatchmakingRemoteEventListener";

            public MatchmakingEventSender(UserService user) { User = user; }

            public void SendEvent(string eventName, object[] param)
            {
                ResponseMessage responseMessage = new ResponseMessage
                {
                    EventResponse = new EventResponse
                    {
                        EventName = eventName,
                        ListenerName = eventListenerName
                    }
                };

                foreach (object item in param ?? Array.Empty<object>())
                {
                    if (item == null)
                    {
                        responseMessage.EventResponse.Params.Add(new BinaryValue { IsNull = true });
                        continue;
                    }

                    Type type = item.GetType();
                    if (item is IMessage message)
                        responseMessage.EventResponse.Params.Add(new BinaryValue { IsNull = false, One = message.ToByteString() });
                    else
                        responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(type).ToBytes(item));
                }

                User.SendResponce(responseMessage);
            }
        }

        // Отдельный sender для ПОИСКОВОГО флоу (onMatchmakingProgress / onMatchmakingDone /
        // onPlayersConfirmed / onMatchmakingFail). Клиент слушает эти события через интерфейс
        // MatchmakingRemoteListener (имя "MatchmakingRemoteListener"), а НЕ через
        // MatchmakingRemoteEventListener, который отвечает за события лобби. Раньше все
        // события поиска уходили с именем лобби-листенера, клиент их не получал - поиск
        // висел на "Подключение" и падал с ошибкой, экран подтверждения не показывался.
        public class MatchmakingFlowEventSender : IEventSender
        {
            private readonly UserService User;
            public string BoundPlayerId { get; set; }
            public string eventListenerName { get; set; } = "MatchmakingRemoteListener";

            public MatchmakingFlowEventSender(UserService user) { User = user; }

            /// <summary>Один TCP, без fan-out — для Done/Confirmation (0.17 ломается от дубля).</summary>
            public void SendEventDirect(string eventName, object[] param)
            {
                if (User?.TcpClient == null || !User.IsSessionAlive()) return;
                string ep = "unknown";
                try { ep = User.TcpClient.Client?.RemoteEndPoint?.ToString() ?? "null"; } catch { }
                Logger.Debug($"[FlowEvent] DIRECT {eventName} via {eventListenerName} to {ep} player={BoundPlayerId}");
                User.SendResponce(BuildFlowResponseMessage(eventName, param));
            }

            private ResponseMessage BuildFlowResponseMessage(string eventName, object[] param)
            {
                var responseMessage = new ResponseMessage
                {
                    EventResponse = new EventResponse
                    {
                        EventName = eventName,
                        ListenerName = eventListenerName
                    }
                };
                foreach (object item in param ?? Array.Empty<object>())
                {
                    if (item == null)
                    {
                        responseMessage.EventResponse.Params.Add(new BinaryValue { IsNull = true });
                        continue;
                    }
                    Type type = item.GetType();
                    if (item is IMessage message)
                        responseMessage.EventResponse.Params.Add(new BinaryValue { IsNull = false, One = message.ToByteString() });
                    else
                        responseMessage.EventResponse.Params.Add(ProtoReflectionUtils.CreateToByteMethod(type).ToBytes(item));
                }
                return responseMessage;
            }

            public void SendEvent(string eventName, object[] param)
            {
                string userEndPoint = "unknown";
                try { userEndPoint = User?.TcpClient?.Client?.RemoteEndPoint?.ToString() ?? "null"; } catch { }

                ResponseMessage responseMessage = BuildFlowResponseMessage(eventName, param);

                string playerId = BoundPlayerId;
                if (string.IsNullOrEmpty(playerId))
                {
                    try { StaticClasses.Users.TryGetValue(User?.TcpClient, out playerId); } catch { }
                }

                List<UserService> allServices = null;
                if (!string.IsNullOrEmpty(playerId))
                    StaticClasses.TryGetAllUserServices(playerId, out allServices);

                // Done/Fail/Confirmed/GameStarted — один канал (не fan-out).
                // Progress и Done на search-TCP (куда пришёл start), иначе клиент висит в поиске.
                // Дубль Done на оба TCP ломает confirm UI 0.17.
                var progressEvt = param?.OfType<Axlebolt.Bolt.Matchmaking.Protobuf.OnMatchmakingProgressEvent>().FirstOrDefault();
                // Всегда один TCP: fan-out x2 → UI confirm на одном сокете, RPC на другом.
                bool singleChannelOnly = string.Equals(eventName, "onMatchmakingDone", StringComparison.Ordinal)
                    || string.Equals(eventName, "onMatchmakingFail", StringComparison.Ordinal)
                    || string.Equals(eventName, "onGameStarted", StringComparison.Ordinal)
                    || string.Equals(eventName, "onPlayersConfirmed", StringComparison.Ordinal)
                    || string.Equals(eventName, "onMatchmakingProgress", StringComparison.Ordinal);

                // start/confirm на search-TCP — Confirmation/Done туда же (PRIMARY не слушает во время поиска).
                bool preferSearchChannel =
                    string.Equals(eventName, "onMatchmakingDone", StringComparison.Ordinal)
                    || string.Equals(eventName, "onMatchmakingFail", StringComparison.Ordinal)
                    || string.Equals(eventName, "onPlayersConfirmed", StringComparison.Ordinal)
                    || (progressEvt != null && (
                        progressEvt.State == Axlebolt.Bolt.Matchmaking.Protobuf.MatchmakingProgressState.Matchmaking
                        || progressEvt.State == Axlebolt.Bolt.Matchmaking.Protobuf.MatchmakingProgressState.Confirmation
                        || progressEvt.State == Axlebolt.Bolt.Matchmaking.Protobuf.MatchmakingProgressState.NotConfirmed));

                if (allServices != null && allServices.Count > 0)
                {
                    List<UserService> snapshot;
                    lock (allServices) { snapshot = allServices.ToList(); }

                    var sent = new HashSet<int>();
                    int delivered = 0;

                    UserService preferred = null;
                    if (!string.IsNullOrEmpty(playerId))
                        preferred = StaticClasses.ResolveMatchmakingEventChannel(playerId, preferSearchChannel);

                    if (preferred?.TcpClient != null)
                    {
                        int key = preferred.TcpClient.GetHashCode();
                        if (sent.Add(key))
                        {
                            string ep = "unknown";
                            try { ep = preferred.TcpClient.Client?.RemoteEndPoint?.ToString() ?? "null"; } catch { }
                            string channelLabel = "FALLBACK";
                            if (!string.IsNullOrEmpty(playerId))
                            {
                                if (StaticClasses.TryGetMatchmakingFlowChannel(playerId, out UserService search)
                                    && ReferenceEquals(preferred, search))
                                    channelLabel = "SEARCH";
                                else if (StaticClasses.UserServices.TryGetValue(playerId, out UserService prim)
                                    && ReferenceEquals(preferred, prim))
                                    channelLabel = "PRIMARY";
                            }
                            Logger.Debug($"[FlowEvent] Sending {eventName} via {eventListenerName} {channelLabel} to {ep} player={playerId}");
                            try { preferred.SendResponce(responseMessage); delivered++; }
                            catch (System.Exception ex) { Logger.Error($"[FlowEvent] {channelLabel} {eventName} failed: {ex.Message}"); }
                        }
                    }

                    if (!singleChannelOnly)
                    {
                        foreach (var service in snapshot)
                        {
                            if (service?.TcpClient == null || !service.TcpClient.Connected) continue;
                            int key = service.TcpClient.GetHashCode();
                            if (!sent.Add(key)) continue;
                            string ep = "unknown";
                            try { ep = service.TcpClient.Client?.RemoteEndPoint?.ToString() ?? "null"; } catch { }
                            Logger.Debug($"[FlowEvent] Sending {eventName} via {eventListenerName} to {ep} player={playerId}");
                            try { service.SendResponce(responseMessage); delivered++; }
                            catch (System.Exception ex) { Logger.Error($"[FlowEvent] Failed to send {eventName} to {ep}: {ex.Message}"); }
                        }
                    }
                    else if (delivered == 0)
                    {
                        foreach (var service in snapshot)
                        {
                            if (service?.TcpClient == null || !service.IsSessionAlive()) continue;
                            int key = service.TcpClient.GetHashCode();
                            if (!sent.Add(key)) continue;
                            try
                            {
                                service.SendResponce(responseMessage);
                                delivered++;
                                if (!string.IsNullOrEmpty(playerId))
                                    StaticClasses.SetMatchmakingFlowChannel(playerId, service);
                            }
                            catch (System.Exception ex) { Logger.Error($"[FlowEvent] Fallback {eventName} failed: {ex.Message}"); }
                            break;
                        }
                    }
                    if (delivered == 0)
                        Logger.Error($"[FlowEvent] NO live channel for player={playerId} event={eventName}");
                    else
                        Logger.Log($"[FlowEvent] delivered {eventName} x{delivered} player={playerId} singleChannel={singleChannelOnly}");
                }
                else if (User?.TcpClient != null && User.TcpClient.Connected)
                {
                    Logger.Debug($"[FlowEvent] Sending {eventName} via {eventListenerName} to {userEndPoint} (fallback User) player={playerId}");
                    User.SendResponce(responseMessage);
                }
                else
                {
                    Logger.Error($"[FlowEvent] Dropped {eventName}: no live services for player={playerId}");
                }
            }
        }
    }
}
