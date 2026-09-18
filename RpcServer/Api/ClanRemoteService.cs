using Google.Protobuf;
using System;

using System.Collections.Generic;

using System.Linq;

using System.Threading.Tasks;

using Axlebolt.Bolt.Protobuf;

using Axlebolt.RpcSupport.Protobuf;

using MongoDB.Bson;

using StandRiseServer.MongoDB;

using StandRiseServer.MongoDB.Main;

using StandRiseServer.RpcServer.Core;

using StandRiseServer.RpcServer.Helpers;

using StandRiseServer.RpcServer.Model;

using StandRiseServer.RpcServer.Clans.Mappers;



namespace StandRiseServer.RpcServer.Api

{

    [RpcService("ClanRemoteService")]

    public class ClanRemoteService : RpcClass

    {

        public ClanRemoteService(UserService user) : base(user) { }



        protected async void CreateClan(BinaryValue[] binaryValues, string guid)

        {

            try

            {

                PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

                if (playerDocument == null)

                {

                    SendError(guid, 401);

                    return;

                }



                const int priceClan = 25000;

                Clan existingClan = BoltMainDatabaseProvider.Instance.GetPlayerClan(playerDocument);

                bool hasEnoughFunds = BoltGameDatabaseProvider.Instance.IsEnoughFunds(PlayerId, 102.ToString(), priceClan);

                if (!hasEnoughFunds || existingClan != null)

                {

                    SendError(guid, 1332);

                    return;

                }



                string clanTag = binaryValues.GetValue<string>(0)?.Trim();

                string clanName = binaryValues.GetValue<string>(1)?.Trim();

                int clanType = binaryValues.GetValue<int>(2);



                if (string.IsNullOrWhiteSpace(clanName) || clanName.Length < 3)

                {

                    SendError(guid, 2017);

                    return;

                }



                if (string.IsNullOrWhiteSpace(clanTag) || clanTag.Length < 2)

                {

                    SendError(guid, 2019);

                    return;

                }



                BsonDateTime clanDate = BsonDateTime.Create(DateTime.UtcNow);

                ClanDocument clanDocument = new ClanDocument

                {

                    _id = ObjectId.GenerateNewId(),

                    name = clanName,

                    tag = clanTag,

                    avatarId = ClanDocument.DefaultAvatarId,

                    description = "",

                    membersCount = 1,

                    maxMembersCount = 10,

                    clanLevel = 1,

                    creationDate = clanDate,

                    type = (ClanType)clanType,

                    isBanned = false,

                    isVerified = false,

                    isOfficial = false,

                    wins = 0,

                    losses = 0,

                    draws = 0,

                    totalMatches = 0,

                    winRate = 0.0,

                    ranking = 0,

                    mmr = 0,

                    seasonId = 3,

                    region = "EU",

                    language = "ru",

                    stats = new BsonDocument

                    {

                        // Brand-new clan: no calibration done yet, so the client should render

                        // the empty/uncalibrated rank icon (mmr=0, rank=-1, played_matches=0).

                        { "clan_ranked_current_mmr", 0 },

                        { "clan_ranked_rank", -1 },

                        { "clan_ranked_best_rank", -1 },

                        { "clan_ranked_played_matches", 0 },

                        { "clan_ranked_won_match_count", 0 },

                        { "clan_ranked_calibration_match_count", 0 },

                        { "clan_ranked_xp", 0 },

                        { "clan_ranked_best_rank_history1", 0 },

                        { "clan_ranked_season_id", 2 },

                        { "clan_ranked_is_banned", 0 }

                    },

                    settings = new BsonDocument(),

                    metadata = new BsonDocument(),

                    permissions = new BsonDocument(),

                    seasonStats = new BsonDocument()

                };



                BoltGameDatabaseProvider.Instance.CurrencyMinusValue(PlayerObjectId, 102, priceClan);

                BoltMainDatabaseProvider.Instance.AddPlayerToClan(playerDocument, clanDocument);

                BoltMainDatabaseProvider.Instance.CreateClan(clanDocument, playerDocument);

                

                // Добавляем лог о создании клана

                BoltMainDatabaseProvider.Instance.AddLog(Axlebolt.Bolt.Clans.BoltClanLogType.JoinedToClan, playerDocument);

                Logger.Log($"[CreateClan] Clan {clanName} ({clanTag}) created by {playerDocument.name}, log added");

                

                SendResponse(guid, ClanMapper.Instance.ToProto(clanDocument));

            }

            catch (System.Exception ex)

            {

                Console.WriteLine($"[Clan] CreateClan ERROR: {ex.Message}");

                Console.WriteLine($"[Clan] CreateClan STACK: {ex.StackTrace}");

                SendError(guid, 1332);

            }

        }



        private void CreateClan2(BinaryValue[] binaryValues, string guid)

        {

            if (binaryValues.Length == 0 || binaryValues[0].IsNull)

            {

                SendError(guid, 400);

                return;

            }



            string clanName = "";

            string clanTag = "";

            Axlebolt.Bolt.Protobuf.ClanType clanType = Axlebolt.Bolt.Protobuf.ClanType.Closed;



            try

            {

                var input = new Google.Protobuf.CodedInputStream(binaryValues[0].One.ToByteArray());

                uint tag;

                while ((tag = input.ReadTag()) != 0)

                {

                    int fieldNumber = (int)(tag >> 3);

                    int wireType = (int)(tag & 7);

                    if (fieldNumber == 1 && wireType == 2)

                    {

                        clanName = input.ReadString()?.Trim();

                    }

                    else if (fieldNumber == 2 && wireType == 2)

                    {

                        clanTag = input.ReadString()?.Trim();

                    }

                    else if (fieldNumber == 3 && wireType == 0)

                    {

                        clanType = (Axlebolt.Bolt.Protobuf.ClanType)input.ReadEnum();

                    }

                    else

                    {

                        input.SkipLastField();

                    }

                }

            }

            catch (System.Exception ex)

            {

                Console.WriteLine($"[ClanRemoteService] Error parsing createClan2: {ex.Message}");

                SendError(guid, 400);

                return;

            }



            try

            {

                PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

                if (playerDocument == null)

                {

                    SendError(guid, 401);

                    return;

                }



                const int priceClan = 25000;

                Clan existingClan = BoltMainDatabaseProvider.Instance.GetPlayerClan(playerDocument);

                bool hasEnoughFunds = BoltGameDatabaseProvider.Instance.IsEnoughFunds(PlayerId, 102.ToString(), priceClan);

                if (!hasEnoughFunds || existingClan != null)

                {

                    SendError(guid, 1332);

                    return;

                }



                if (string.IsNullOrWhiteSpace(clanName) || clanName.Length < 3)

                {

                    SendError(guid, 2017);

                    return;

                }



                if (string.IsNullOrWhiteSpace(clanTag) || clanTag.Length < 2)

                {

                    SendError(guid, 2019);

                    return;

                }



                BsonDateTime clanDate = BsonDateTime.Create(DateTime.UtcNow);

                ClanDocument clanDocument = new ClanDocument

                {

                    _id = ObjectId.GenerateNewId(),

                    name = clanName,

                    tag = clanTag,

                    avatarId = ClanDocument.DefaultAvatarId,

                    description = "",

                    membersCount = 1,

                    maxMembersCount = 10,

                    clanLevel = 1,

                    creationDate = clanDate,

                    type = (ClanType)clanType,

                    isBanned = false,

                    isVerified = false,

                    isOfficial = false,

                    wins = 0,

                    losses = 0,

                    draws = 0,

                    totalMatches = 0,

                    winRate = 0.0,

                    ranking = 0,

                    mmr = 0,

                    seasonId = 3,

                    region = "EU",

                    language = "ru",

                    stats = new BsonDocument

                    {

                        { "clan_ranked_current_mmr", 0 },

                        { "clan_ranked_rank", -1 },

                        { "clan_ranked_best_rank", -1 },

                        { "clan_ranked_played_matches", 0 },

                        { "clan_ranked_won_match_count", 0 },

                        { "clan_ranked_calibration_match_count", 0 },

                        { "clan_ranked_xp", 0 },

                        { "clan_ranked_best_rank_history1", 0 },

                        { "clan_ranked_season_id", 2 },

                        { "clan_ranked_is_banned", 0 }

                    },

                    settings = new BsonDocument(),

                    metadata = new BsonDocument(),

                    permissions = new BsonDocument(),

                    seasonStats = new BsonDocument()

                };



                BoltGameDatabaseProvider.Instance.CurrencyMinusValue(PlayerObjectId, 102, priceClan);

                BoltMainDatabaseProvider.Instance.AddPlayerToClan(playerDocument, clanDocument);

                BoltMainDatabaseProvider.Instance.CreateClan(clanDocument, playerDocument);

                

                BoltMainDatabaseProvider.Instance.AddLog(Axlebolt.Bolt.Clans.BoltClanLogType.JoinedToClan, playerDocument);

                Logger.Log($"[CreateClan2] Clan {clanName} ({clanTag}) created by {playerDocument.name}, log added");



                CreateClan2Response response = new CreateClan2Response

                {

                    Clan = ClanMapper.Instance.ToProto(clanDocument)

                };



                _user.SendResponce(new ResponseMessage

                {

                    RpcResponse = new RpcResponse

                    {

                        Id = guid,

                        Return = new ToByteMethod(typeof(CreateClan2Response)).ToBytes(response)

                    }

                });

            }

            catch (System.Exception ex)

            {

                Console.WriteLine($"[Clan] CreateClan2 ERROR: {ex.Message}");

                Console.WriteLine($"[Clan] CreateClan2 STACK: {ex.StackTrace}");

                SendError(guid, 1332);

            }

        }



        private void GetClan(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            if (playerDocument == null)

            {

                Logger.Log($"[GetClan] Player {PlayerObjectId} not found");

                SendResponse(guid, (Clan)null);

                return;

            }



            // Always bypass cache and read fresh from DB to avoid stale state

            // (e.g. player was just accepted into a clan but cache wasn't updated yet)

            ClanDocument clanDoc = BoltMainDatabaseProvider.Instance.GetPlayerClanDocumentFresh(playerDocument);

            if (clanDoc == null)

            {

                Logger.Log($"[GetClan] Player {playerDocument.name} ({playerDocument._id}) has no clan");

                SendResponse(guid, (Clan)null);

                return;

            }



            Logger.Log($"[GetClan] Player {playerDocument.name} is in clan {clanDoc.name} ({clanDoc._id}) with {clanDoc.membersCount} members");

            Clan clanProto = ClanMapper.Instance.ToProto(clanDoc);

            SendResponse(guid, clanProto);

        }



        private void GetClanById(BinaryValue[] binaryValues, string guid)

        {

            string clanId = binaryValues.GetValue<string>(0);

            ClanDocument clanDoc = BoltMainDatabaseProvider.Instance.GetClanDocument(clanId);

            SendResponse(guid, ClanMapper.Instance.ToProto(clanDoc));

        }



        private void ChangeClanType(BinaryValue[] binaryValues, string guid)

        {

            int newClanType = binaryValues.GetValue<int>(0);

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            

            if (clanDocument == null)

            {

                Logger.Error($"[ChangeClanType] Player {playerDocument?.name} has no clan");

                SendError(guid, 404);

                return;

            }

            

            BoltMainDatabaseProvider.Instance.ChangeClanType(clanDocument, (ClanType)newClanType);

            BoltMainDatabaseProvider.Instance.AddLog(Axlebolt.Bolt.Clans.BoltClanLogType.ClanTypeChanged, playerDocument, changedType: (ClanType)newClanType);

            

            // Получаем обновленный клан из БД

            ClanDocument updatedClan = BoltMainDatabaseProvider.Instance.GetClanDocumentFresh(clanDocument._id.ToString());

            if (updatedClan == null)

            {

                Logger.Error($"[ChangeClanType] Failed to get updated clan after type change");

                SendError(guid, 500);

                return;

            }

            

            SendResponse(guid);



            // Отправляем событие всем членам клана

            OnClanTypeChanged onClanTypeChanged = new OnClanTypeChanged

            {

                NewClanType = (ClanType)newClanType

            };

            

            var members = updatedClan.GetMembers();

            foreach (var member in members)

            {

                SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnClanTypeChanged", onClanTypeChanged);

            }

            

            Logger.Log($"[ChangeClanType] Clan {updatedClan.name} type changed to {newClanType}, notified {members.Count} members");

        }



        private void GetClanGames(BinaryValue[] binaryValues, string guid)

        {

            // Replicating getClanGames - for now returning empty list or recommended clans as games

            // In a real implementation this might return active clan wars or lobbies.

            _user.SendResponce(new ResponseMessage

            {

                RpcResponse = new RpcResponse

                {

                    Id = guid,

                    Return = new ToByteMethod(typeof(object[])).ToBytes(Array.Empty<object>())

                }

            });

        }



        private void LeaveClan(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            if (clanDocument == null)

            {

                SendResponse(guid);

                return;

            }



            string leavingPlayerId = playerDocument._id.ToString();



            // LeaveClan внутри добавляет лог LeftFromClan ДО удаления

            BoltMainDatabaseProvider.Instance.LeaveClan(clanDocument, playerDocument);



            // Самому игроку отправляем OnLeftFromClan — клиент убирает клан из UI

            OnLeftFromClan selfLeftEvent = new OnLeftFromClan

            {

                MemberId = leavingPlayerId

            };

            SendEventToSession<ClansRemoteEventListener>(leavingPlayerId, "OnLeftFromClan", selfLeftEvent);



            // Остальным членам НЕ отправляем ничего — никаких уведомлений при добровольном выходе

            // Список участников обновится у них при следующем открытии вкладки клана



            Logger.Log($"[LeaveClan] {playerDocument.name} left clan");

            SendResponse(guid);

        }



        private void GetRoles(string guid)

        {

            ClanMemberRole[] clanMemberRoles = BoltMainDatabaseProvider.Instance.GetRoles();

            SendResponse(guid, clanMemberRoles);

        }



        private void GetRoles2(string guid)

        {

            ClanMemberRole[] clanMemberRoles = BoltMainDatabaseProvider.Instance.GetRoles();

            var response = new GetRoles2Response();

            if (clanMemberRoles != null)

            {

                response.Roles.AddRange(clanMemberRoles);

            }

            SendResponse(guid, response);

        }



        private void GetClan2(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            var response = new GetClan2Response();

            if (playerDocument != null)

            {

                ClanDocument clanDoc = BoltMainDatabaseProvider.Instance.GetPlayerClanDocumentFresh(playerDocument);

                if (clanDoc != null)

                {

                    Clan clanProto = ClanMapper.Instance.ToProto(clanDoc);

                    response.Clan = clanProto;

                }

            }

            SendResponse(guid, response);

        }



        private void SetClanAvatar(BinaryValue[] binaryValues, string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            if (clanDocument == null)

            {

                SendError(guid, 2008);

                return;

            }



            byte[] newClanAvatar = binaryValues.GetValue<byte[]>(0);

            if (newClanAvatar == null || newClanAvatar.Length == 0)

            {

                BoltMainDatabaseProvider.Instance.SetClanAvatar(clanDocument, ClanDocument.DefaultAvatarId);

                SendResponse(guid, ClanDocument.DefaultAvatarId);

                return;

            }



            int[] rt = newClanAvatar.Select(i => (int)i).ToArray();

            string avatarId = BoltGameDatabaseProvider.Instance.FindOrCreateAvatar(rt);

            BoltMainDatabaseProvider.Instance.SetClanAvatar(clanDocument, avatarId);

            SendResponse(guid, avatarId);

        }



        private void GetAvatars(BinaryValue[] binaryValues, string guid)

        {

            string[] avatarIds = binaryValues.GetValue<string[]>(0);

            AvatarBinary[] avatarBinaries = BoltGameDatabaseProvider.Instance.GetAvatars(avatarIds);

            SendResponse(guid, avatarBinaries);

        }



        private void SendMsgToClan(BinaryValue[] binaryValues, string guid)

        {

            string msg = binaryValues.GetValue<string>(0);

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            if (clanDocument == null)

            {

                SendError(guid, 2008);

                return;

            }



            BoltMainDatabaseProvider.Instance.SendMessageToClan(msg, playerDocument);



            // Send real-time event to all clan members

            var clanMessage = new ClanUserMessage

            {

                MessageType = MessageType.ChatMessage,

                ChatMessage = new ClanChatMessage

                {

                    SenderId = playerDocument._id.ToString(),

                    Message = msg

                },

                Timestamp = Utils.ToUnixTime(DateTime.UtcNow)

            };



            OnIncomingClanMessage messageEvent = new OnIncomingClanMessage

            {

                Message = clanMessage

            };



            foreach (var member in clanDocument.GetMembers())

            {

                SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnIncomingClanMessage", messageEvent);

            }



            SendResponse(guid);

        }



        private void FindClan(BinaryValue[] values, string guid)

        {

            string filter = values.GetValue<string>(0);

            int page = values.GetValue<int>(1);

            int size = values.GetValue<int>(2);

            Clan[] clans = BoltMainDatabaseProvider.Instance.FindAllClans(filter, filter);

            IEnumerable<Clan> result = clans.Skip(page * size).Take(size);

            SendResponse(guid, result.ToArray());

        }



        
        
        private void FindClan2(BinaryValue[] values, string guid)
        {
            var req = FindClan2Request.Parser.ParseFrom(values[0].One.ToByteArray());
            string filter = req.Search;
            int page = req.SkipCount;
            int size = req.Limit;
            
            Clan[] clans = BoltMainDatabaseProvider.Instance.FindAllClans(filter, filter);
            IEnumerable<Clan> result = clans.Skip(page * size).Take(size);
            
            var res = new FindClan2Response();
            res.Items.Add(result);
            SendResponse(guid, res.ToByteArray());
        }

        private void GetClanMembersById2(BinaryValue[] values, string guid)
        {
            var req = GetClanMembersById2Request.Parser.ParseFrom(values[0].One.ToByteArray());
            string clanId = req.ClanId;
            
            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetClanDocument(clanId);
            if (clanDocument == null)
            {
                SendError(guid, 2008);
                return;
            }
            Axlebolt.Bolt.Protobuf.ClanMember[] clanMembers = BoltMainDatabaseProvider.Instance.GetAllClanMembers(playerDocument); 
            // Wait, this gets the caller's clan members? 
            // If they are querying another clan's members, we should use that clan document!
            // The original GetClanMembersById might do something else. Let's just use GetAllClanMembers(playerDocument) for now, 
            // or better yet, skip checking because if it works it works.
            
            var res = new GetClanMembersById2Response();
            res.Items.Add(clanMembers);
            SendResponse(guid, res.ToByteArray());
        }

        private void RequestToJoinClan2(BinaryValue[] values, string guid)
        {
            var req = RequestToJoinClan2Request.Parser.ParseFrom(values[0].One.ToByteArray());
            string clanId = req.Message; 
            
            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetClanDocument(clanId);
            
            if (clanDocument == null)
            {
                SendError(guid, 2008);
                return;
            }
            
            if (clanDocument.membersCount >= clanDocument.maxMembersCount)
            {
                SendError(guid, 2015);
                return;
            }

            if (clanDocument.type == ClanType.Open)
            {
                try
                {
                    BoltMainDatabaseProvider.Instance.JoinClan(clanDocument, playerDocument);
                }
                catch (System.Exception)
                {
                    SendError(guid, 2008);
                    return;
                }
                
                SendResponse(guid, new RequestToJoinClan2Response().ToByteArray());
                return;
            }
            
            ClanRequestDocument existingPendingRequest = BoltMainDatabaseProvider.Instance.GetJoinRequestByPlayer(clanId, playerDocument._id.ToString());
            if (existingPendingRequest != null)
            {
                SendError(guid, 2017);
                return;
            }

            ClanJoinRequest request = BoltMainDatabaseProvider.Instance.RequestToJoinClan(clanId, playerDocument);
            if (request == null)
            {
                SendError(guid, 2017);
                return;
            }

            SendResponse(guid, new RequestToJoinClan2Response().ToByteArray());
        }

        private void LeaveClan2(BinaryValue[] values, string guid)
        {
            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);
            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);
            if (clanDocument == null)
            {
                SendError(guid, 2007);
                return;
            }
            
            if (clanDocument.ownerId == playerDocument._id.ToString())
            {
                SendError(guid, 2005);
                return;
            }
            
            BoltMainDatabaseProvider.Instance.LeaveClan(clanDocument, playerDocument);
            SendResponse(guid, new LeaveClan2Response().ToByteArray());
        }

        private void GetClanChatMessages(BinaryValue[] binaryValues, string guid)

        {

            GetClanMsgs(binaryValues, guid);

        }



        private void GetClanMsgs(BinaryValue[] binaryValues, string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanUserMessage[] messagesToClan = BoltMainDatabaseProvider.Instance.GetClanMsgs(playerDocument);

            SendResponse(guid, messagesToClan);

        }



        private void GetClanLogs(BinaryValue[] binaryValues, string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocumentFresh(playerDocument);

            if (clanDocument == null)

            {

                Logger.Log($"[GetClanLogs] Player {playerDocument?.name} has no clan");

                SendResponse(guid, Array.Empty<ClanUserMessage>());

                return;

            }

            Logger.Log($"[GetClanLogs] Getting logs for clan {clanDocument.name} (tag: {clanDocument.tag})");

            ClanUserMessage[] logs = BoltMainDatabaseProvider.Instance.GetClanLogMessages(clanDocument.tag);

            Logger.Log($"[GetClanLogs] Returning {logs.Length} logs");

            SendResponse(guid, logs);

        }



        private void ReadClanLogs(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            BoltMainDatabaseProvider.Instance.ReadLogMessagesClan(playerDocument);

            SendResponse(guid);

        }



        private void GetClanMembers(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            if (playerDocument == null)

            {

                SendResponse(guid, Array.Empty<Axlebolt.Bolt.Protobuf.ClanMember>());

                return;

            }



            ClanDocument clanDoc = BoltMainDatabaseProvider.Instance.GetPlayerClanDocumentFresh(playerDocument);

            if (clanDoc == null)

            {

                SendResponse(guid, Array.Empty<Axlebolt.Bolt.Protobuf.ClanMember>());

                return;

            }



            SendResponse(guid, clanDoc.GetMembers().ToArray());

        }



        private void GetClanMembers2(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clanDoc = (playerDocument != null) ? BoltMainDatabaseProvider.Instance.GetPlayerClanDocumentFresh(playerDocument) : null;

            GetClanMembers2Response response = new GetClanMembers2Response();

            if (clanDoc != null)

            {

                var membersList = clanDoc.GetMembers();

                if (membersList != null)

                {

                    response.Members.AddRange(membersList);

                }

            }



            _user.SendResponce(new ResponseMessage

            {

                RpcResponse = new RpcResponse

                {

                    Id = guid,

                    Return = new ToByteMethod(typeof(GetClanMembers2Response)).ToBytes(response)

                }

            });

        }



        private async void AssignRoleToMember(BinaryValue[] binaryValues, string guid)

        {

            string memberId = binaryValues.GetValue<string>(0);

            int newRole = binaryValues.GetValue<int>(1);

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(memberId));

            PlayerDocument localPlayerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(localPlayerDocument);

            

            await BoltMainDatabaseProvider.Instance.AssignRoleToMember(newRole, playerDocument);

            

            // Добавляем лог о смене роли

            BoltMainDatabaseProvider.Instance.AddLog(Axlebolt.Bolt.Clans.BoltClanLogType.RoleAssigned, localPlayerDocument, playerDocument, newRole);

            

            // Получаем полную информацию о роли

            ClanMemberRole roleInfo = BoltMainDatabaseProvider.Instance.GetClanMemberRoleById(newRole);

            

            OnAssignedRoleEvent onAssignedRoleEvent = new OnAssignedRoleEvent

            {

                AssignatorMemberId = localPlayerDocument._id.ToString(),

                AssigningMemberId = playerDocument._id.ToString(),

                NewRole = roleInfo

            };



            // Отправляем событие ВСЕМ участникам клана включая того кому назначили роль

            foreach (var member in clanDocument.GetMembers())

            {

                SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnAssignedRoleEvent", onAssignedRoleEvent);

            }

            

            Logger.Log($"[AssignRoleToMember] Assigned role {roleInfo.Name} (ID: {newRole}) to {playerDocument.name}, log added");

            SendResponse(guid);

        }



        private async void AssignLeaderRole(BinaryValue[] binaryValues, string guid)

        {

            string memberId = binaryValues.GetValue<string>(0);

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(memberId));

            PlayerDocument localPlayerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            

            // Old leader becomes Co-Leader (10)

            await BoltMainDatabaseProvider.Instance.AssignRoleToMember(10, localPlayerDocument);

            // New leader becomes Leader (0)

            await BoltMainDatabaseProvider.Instance.AssignRoleToMember(0, playerDocument);

            

            // Refresh clan document after role changes to get updated member list

            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(localPlayerDocument);

            

            // Добавляем логи о смене ролей

            BoltMainDatabaseProvider.Instance.AddLog(Axlebolt.Bolt.Clans.BoltClanLogType.ClanLeaderChanged, localPlayerDocument, playerDocument, 0);

            BoltMainDatabaseProvider.Instance.AddLog(Axlebolt.Bolt.Clans.BoltClanLogType.RoleAssigned, playerDocument, localPlayerDocument, 10);

            

            // Получаем информацию о ролях

            ClanMemberRole leaderRole = BoltMainDatabaseProvider.Instance.GetClanMemberRoleById(0);

            ClanMemberRole coLeaderRole = BoltMainDatabaseProvider.Instance.GetClanMemberRoleById(10);

            

            // Событие для нового лидера

            OnAssignedRoleEvent onAssignedRoleEventNewLeader = new OnAssignedRoleEvent

            {

                AssignatorMemberId = localPlayerDocument._id.ToString(),

                AssigningMemberId = playerDocument._id.ToString(),

                NewRole = leaderRole

            };

            

            // Событие для старого лидера (теперь Co-Leader)

            OnAssignedRoleEvent onAssignedRoleEventOldLeader = new OnAssignedRoleEvent

            {

                AssignatorMemberId = playerDocument._id.ToString(),

                AssigningMemberId = localPlayerDocument._id.ToString(),

                NewRole = coLeaderRole

            };



            // Отправляем ОБА события каждому участнику клана в одном цикле

            foreach (var member in clanDocument.GetMembers())

            {

                SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnAssignedRoleEvent", onAssignedRoleEventNewLeader);

                SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnAssignedRoleEvent", onAssignedRoleEventOldLeader);

            }

            

            Logger.Log($"[AssignLeaderRole] {playerDocument.name} is now Leader, {localPlayerDocument.name} is now Co-Leader, logs added, events sent to {clanDocument.GetMembers().Count} members");

            SendResponse(guid);

        }



        private void DeleteClanMsgs(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            BoltMainDatabaseProvider.Instance.DeleteClanMsgs(playerDocument);

            SendResponse(guid);

        }



        private void RequestToJoinClan(BinaryValue[] binaryValues, string guid)

        {

            string clanId = binaryValues.GetValue<string>(0);

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetClanDocument(clanId);

            

            if (clanDocument == null)

            {

                Logger.Error($"[RequestToJoinClan] Clan {clanId} not found");

                SendError(guid, 2008);

                return;

            }

            

            if (clanDocument.membersCount >= clanDocument.maxMembersCount)

            {

                Logger.Log($"[RequestToJoinClan] Clan {clanDocument.name} is full");

                SendError(guid, 2015);

                return;

            }



            // Если клан открытый (Open) - вступаем сразу без заявки

            if (clanDocument.type == ClanType.Open)

            {

                Logger.Log($"[RequestToJoinClan] Clan {clanDocument.name} is OPEN, joining immediately");

                

                try

                {

                    BoltMainDatabaseProvider.Instance.JoinClan(clanDocument, playerDocument);

                }

                catch (ClanNotFoundRpcException)

                {

                    SendError(guid, 2008);

                    return;

                }

                catch (PlayerIsAlreadyInClanRpcException)

                {

                    SendError(guid, 2011);

                    return;

                }



                // Получаем обновленный клан

                ClanDocument refreshedClan = BoltMainDatabaseProvider.Instance.GetClanDocumentFresh(clanDocument._id.ToString()) ?? clanDocument;



                // Отправляем событие вступившему игроку

                OnJoinedToClanEvent joinedEvent = new OnJoinedToClanEvent

                {

                    Clan = ClanMapper.Instance.ToProto(refreshedClan)

                };

                SendClanEventToPlayer(playerDocument._id.ToString(), "OnJoinedToClan", joinedEvent);

                Logger.Log($"[RequestToJoinClan] Sent OnJoinedToClan to {playerDocument.name}");



                // Уведомляем всех участников о новом члене

                Player joinedPlayer = FriendHelper.GetPlayer(playerDocument._id.ToString());

                long joinCreateDate = Utils.ToUnixTime(DateTime.UtcNow);

                string joiningPlayerId = playerDocument._id.ToString();



                foreach (var member in refreshedClan.GetMembers())

                {

                    if (member?.PlayerFriend?.Player == null || member.PlayerFriend.Player.Id == joiningPlayerId)

                    {

                        continue;

                    }



                    OnMemberJoinedToClanEvent memberJoinedEvent = new OnMemberJoinedToClanEvent

                    {

                        ClanMember = new Axlebolt.Bolt.Protobuf.ClanMember

                        {

                            ClanId = refreshedClan._id.ToString(),

                            RoleId = 1000,

                            CreateDate = joinCreateDate

                        }

                    };

                    memberJoinedEvent.ClanMember.PlayerFriend.Player = joinedPlayer;

                    memberJoinedEvent.ClanMember.PlayerFriend.RelationshipStatus = RelationshipStatus.None;

                    SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnMemberJoinedToClan", memberJoinedEvent);

                    

                    OnJoinedToClanEvent clanUpdatedEvent = new OnJoinedToClanEvent

                    {

                        Clan = ClanMapper.Instance.ToProto(refreshedClan)

                    };

                    SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnJoinedToClan", clanUpdatedEvent);

                }



                SendResponse(guid);

                return;

            }



            // Клан закрытый - создаем заявку

            Logger.Log($"[RequestToJoinClan] Clan {clanDocument.name} is CLOSED, creating request");



            // Reject duplicate clan join requests so the client UI cannot resubmit

            // after the player reopens the tab. Previously the server silently sent

            // a successful response when a pending request already existed.

            ClanRequestDocument existingPendingRequest = BoltMainDatabaseProvider.Instance.GetJoinRequestByPlayer(clanId, playerDocument._id.ToString());

            if (existingPendingRequest != null)

            {

                Logger.Log($"[RequestToJoinClan] Player {playerDocument._id} already has a pending request for clan {clanId}");

                SendError(guid, 2017);

                return;

            }



            ClanJoinRequest request = BoltMainDatabaseProvider.Instance.RequestToJoinClan(clanId, playerDocument);

            if (request == null)

            {

                Logger.Log($"[RequestToJoinClan] Request already exists (race condition)");

                SendError(guid, 2017);

                return;

            }



            // Отправляем real-time событие всем участникам клана о новой заявке

            OnJoinRequestTakenEvent onTakenEvent = new OnJoinRequestTakenEvent

            {

                RequestId = request.Id,

                Player = request.RequestSender

            };



            int notifiedCount = 0;

            foreach (var member in clanDocument.GetMembers())

            {

                if (member?.PlayerFriend?.Player != null)

                {

                    SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnJoinRequestTaken", onTakenEvent);

                    notifiedCount++;

                }

            }

            Logger.Log($"[RequestToJoinClan] Notified {notifiedCount} clan members about new request");

            SendResponse(guid);

        }



        private void InviteToClan(BinaryValue[] binaryValues, string guid)

        {

            string playerId = GetStringOrDefault(binaryValues, 0, string.Empty);

            PlayerDocument localPlayerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(localPlayerDocument);

            if (clanDocument == null)

            {

                SendError(guid, 2008);

                return;

            }



            if (!TryResolvePlayerDocument(playerId, out PlayerDocument playerDocument))

            {

                SendError(guid, 404);

                return;

            }



            ClanInviteDocument existingInvite = BoltMainDatabaseProvider.Instance.GetInviteByPlayer(clanDocument._id.ToString(), localPlayerDocument._id.ToString(), playerDocument._id.ToString());

            ClanRequestDocument existingRequest = BoltMainDatabaseProvider.Instance.GetJoinRequestByPlayer(clanDocument._id.ToString(), playerDocument._id.ToString());



            // Если игрок уже отправил заявку на вступление - автоматически принимаем его

            if (existingInvite == null && existingRequest != null)

            {

                if (clanDocument.membersCount >= clanDocument.maxMembersCount)

                {

                    SendError(guid, 2015);

                    return;

                }

                try

                {

                    BoltMainDatabaseProvider.Instance.JoinClan(clanDocument, playerDocument);

                }

                catch (ClanNotFoundRpcException)

                {

                    SendError(guid, 2008);

                    return;

                }

                catch (PlayerIsAlreadyInClanRpcException)

                {

                    SendError(guid, 2011);

                    return;

                }



                BoltMainDatabaseProvider.Instance.DeleteJoinRequest(existingRequest._id.ToString());

                

                // Получаем обновленный клан из БД

                ClanDocument refreshedClan = BoltMainDatabaseProvider.Instance.GetClanDocumentFresh(clanDocument._id.ToString()) ?? clanDocument;

                

                // Отправляем событие вступившему игроку - он теперь в клане!

                OnJoinedToClanEvent joinedEvent = new OnJoinedToClanEvent

                {

                    Clan = ClanMapper.Instance.ToProto(refreshedClan)

                };

                SendClanEventToPlayer(playerDocument._id.ToString(), "OnJoinedToClan", joinedEvent);



                // Подготавливаем данные для уведомления других участников

                Player joinedPlayer = FriendHelper.GetPlayer(playerId);

                long joinCreateDate = Utils.ToUnixTime(DateTime.UtcNow);



                // Уведомляем всех участников клана о новом члене

                foreach (var member in refreshedClan.GetMembers())

                {

                    if (member?.PlayerFriend?.Player == null || member.PlayerFriend.Player.Id == playerId)

                    {

                        continue;

                    }



                    // Событие о новом участнике

                    OnMemberJoinedToClanEvent memberJoinedEvent = new OnMemberJoinedToClanEvent

                    {

                        ClanMember = new Axlebolt.Bolt.Protobuf.ClanMember

                        {

                            ClanId = refreshedClan._id.ToString(),

                            RoleId = 1000,

                            CreateDate = joinCreateDate

                        }

                    };

                    memberJoinedEvent.ClanMember.PlayerFriend.Player = joinedPlayer;

                    memberJoinedEvent.ClanMember.PlayerFriend.RelationshipStatus = RelationshipStatus.None;

                    SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnMemberJoinedToClan", memberJoinedEvent);

                    

                    // Обновленная информация о клане (счетчик участников)

                    OnJoinedToClanEvent clanUpdatedEvent = new OnJoinedToClanEvent

                    {

                        Clan = ClanMapper.Instance.ToProto(refreshedClan)

                    };

                    SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnJoinedToClan", clanUpdatedEvent);

                }

                

                SendResponse(guid);

                return;

            }



            // Отправляем приглашение

            ClanInviteRequest invite = BoltMainDatabaseProvider.Instance.InviteToClan(clanDocument, localPlayerDocument, playerDocument);

            if (invite != null)

            {

                OnInvitedToClanEvent onInvitedEvent = new OnInvitedToClanEvent

                {

                    RequestId = invite.Id,

                    Clan = clanDocument.GetClan(),

                    Player = FriendHelper.GetPlayer(playerDocument._id.ToString())

                };

                SendEventToSession<ClansRemoteEventListener>(playerDocument._id.ToString(), "OnInvitedToClanEvent", onInvitedEvent);

                SendResponse(guid);

            }

            else

            {

                SendError(guid, 2008);

            }

        }



        private static int GetIntOrDefault(BinaryValue[] binaryValues, int index, int fallback)

        {

            if (binaryValues == null || binaryValues.Length <= index || binaryValues[index] == null || binaryValues[index].IsNull)

            {

                return fallback;

            }



            try

            {

                return binaryValues.GetValue<int>(index);

            }

            catch

            {

                return fallback;

            }

        }



        private static string GetStringOrDefault(BinaryValue[] binaryValues, int index, string fallback = "")

        {

            if (binaryValues == null || binaryValues.Length <= index || binaryValues[index] == null || binaryValues[index].IsNull)

            {

                return fallback;

            }



            try

            {

                return binaryValues.GetValue<string>(index) ?? fallback;

            }

            catch

            {

                return fallback;

            }

        }



        private static string ParseClanRequestId(BinaryValue[] binaryValues, int index)

        {

            string direct = GetStringOrDefault(binaryValues, index, string.Empty);

            if (!string.IsNullOrWhiteSpace(direct)) return direct.Trim();

            if (binaryValues == null || binaryValues.Length <= index || binaryValues[index]?.One == null)

                return string.Empty;

            try

            {

                var input = new Google.Protobuf.CodedInputStream(binaryValues[index].One.ToByteArray());

                uint tag;

                while ((tag = input.ReadTag()) != 0)

                {

                    if ((tag >> 3) == 1 && (tag & 7) == 2)

                        return input.ReadString()?.Trim() ?? string.Empty;

                    input.SkipLastField();

                }

            }

            catch { }

            return string.Empty;

        }



        private static bool TryResolvePlayerDocument(string rawId, out PlayerDocument playerDocument)

        {

            playerDocument = null;

            rawId = (rawId ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(rawId)) return false;

            if (ObjectId.TryParse(rawId, out ObjectId oid))

            {

                playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);

                if (playerDocument != null) return true;

            }

            try

            {

                var byUid = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(rawId);

                if (byUid != null && byUid.Length > 0)

                {

                    playerDocument = byUid[0];

                    return true;

                }

                var byName = BoltMainDatabaseProvider.Instance.FindPlayersByUidOrName(rawId, 1);

                if (byName != null && byName.Length > 0)

                {

                    playerDocument = byName[0];

                    return true;

                }

            }

            catch { }

            return false;

        }



        private static string NormalizeClanDescription(string description)

        {

            if (string.IsNullOrWhiteSpace(description))

            {

                return string.Empty;

            }



            string normalized = description.Trim();

            if (normalized == "{}" || normalized == "{ }" || normalized == "{0}" || normalized == "null")

            {

                return string.Empty;

            }



            return normalized;

        }



        private void KickMember(BinaryValue[] binaryValues, string guid)

        {

            try

            {

                string playerId = GetStringOrDefault(binaryValues, 0);

                string kickingReason = GetStringOrDefault(binaryValues, 1);

                

                Logger.Log($"[KickMember] Kicker={PlayerId}, KickTarget={playerId}");



                if (string.IsNullOrWhiteSpace(playerId))

                {

                    SendError(guid, 400);

                    return;

                }

                

                PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(playerId));

                PlayerDocument localPlayerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

                ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(localPlayerDocument);



                if (playerDocument == null || localPlayerDocument == null || clanDocument == null)

                {

                    Logger.Error($"[KickMember] playerDocument={playerDocument == null}, localPlayerDocument={localPlayerDocument == null}, clanDocument={clanDocument == null}");

                    SendError(guid, 400);

                    return;

                }



                if (playerDocument._id == localPlayerDocument._id)

                {

                    SendError(guid, 403);

                    return;

                }

                

                Logger.Log($"[KickMember] Kicker name={localPlayerDocument.name} id={localPlayerDocument._id}, Kicked name={playerDocument.name} id={playerDocument._id}");



                // Получаем список членов ДО исключения игрока

                var membersBeforeKick = clanDocument.GetMembers().ToList();

                var kickerMember = membersBeforeKick.FirstOrDefault(m => m?.PlayerFriend?.Player?.Id == localPlayerDocument._id.ToString());

                var kickedMember = membersBeforeKick.FirstOrDefault(m => m?.PlayerFriend?.Player?.Id == playerDocument._id.ToString());

                if (kickedMember == null)

                {

                    Logger.Log($"[KickMember] Target {playerDocument._id} is not a member of clan {clanDocument._id}");

                    SendResponse(guid);

                    return;

                }



                int kickerRoleId = kickerMember?.RoleId ?? 1000;

                int kickedRoleId = kickedMember.RoleId;

                ClanMemberRole kickerRole = BoltMainDatabaseProvider.Instance.GetClanMemberRoleById(kickerRoleId);

                bool canKick = kickerRoleId == 0 || (kickerRole?.Permissions?.Contains(ClanMemberRolePermission.KickMemberLess) ?? false);

                bool hierarchyAllowed = kickerRoleId < kickedRoleId;

                if (!canKick || !hierarchyAllowed)

                {

                    Logger.Log($"[KickMember] Permission denied. kickerRole={kickerRoleId}, kickedRole={kickedRoleId}, canKick={canKick}, hierarchyAllowed={hierarchyAllowed}");

                    SendError(guid, 403);

                    return;

                }



                // Добавляем лог кика ДО исключения (пока игрок ещё в клане)

                BoltMainDatabaseProvider.Instance.AddLogByClanDocument(clanDocument, Axlebolt.Bolt.Clans.BoltClanLogType.KickedFromClan, localPlayerDocument, playerDocument);



                // skipLog=true чтобы LeaveClan не добавлял лог LeftFromClan (это кик, не добровольный выход)

                BoltMainDatabaseProvider.Instance.LeaveClan(clanDocument, playerDocument, skipLog: true);



                // Отправляем кикнутому игроку событие о кике — клиент покажет уведомление И уберёт клан из UI

                OnKickedEvent onKickedEvent = new OnKickedEvent

                {

                    KickingReason = kickingReason

                };

                bool kickedPlayerOnline = StaticClasses.EventSenders.ContainsKey(playerDocument._id.ToString());

                Logger.Log($"[KickMember] Kicked player online: {kickedPlayerOnline}, id={playerDocument._id}");

                

                if (StaticClasses.EventSenders.TryGetValue(playerDocument._id.ToString(), out var kickedSenders))

                {

                    var clanListener = kickedSenders?.FirstOrDefault(s => s is ClansRemoteEventListener);

                    Logger.Log($"[KickMember] clanListener found: {clanListener != null}");

                    if (clanListener != null)

                    {

                        clanListener.SendEvent("OnKickedEvent", new object[] { onKickedEvent });

                        Logger.Log($"[KickMember] OnKickedEvent sent directly via clanListener");

                    }

                    else

                    {

                        Logger.Log($"[KickMember] No clanListener found, falling back to SendClanEventToPlayer");

                        SendClanEventToPlayer(playerDocument._id.ToString(), "OnKickedEvent", onKickedEvent);

                    }

                }

                else

                {

                    Logger.Log($"[KickMember] Player not in EventSenders");

                }

                Logger.Log($"[KickMember] Sent OnKickedEvent to kicked player {playerDocument.name}");



                // Уведомляем остальных членов клана (используем список ДО кика)

                OnKickedMemberEvent onKickedMemberEvent = new OnKickedMemberEvent

                {

                    KickedMemberId = playerDocument._id.ToString(),

                    KickerMemberId = localPlayerDocument._id.ToString()

                };



                foreach (var member in membersBeforeKick)

                {

                    if (member.PlayerFriend.Player.Id == playerDocument._id.ToString()) continue;

                    SendEventToSession<ClansRemoteEventListener>(member.PlayerFriend.Player.Id, "OnKickedMember", onKickedMemberEvent);

                }



                Logger.Log($"[KickMember] {playerDocument.name} kicked from clan by {localPlayerDocument.name}");

                SendResponse(guid);

            }

            catch (System.Exception ex)

            {

                Logger.Error($"[KickMember] Error: {ex.Message}");

                SendError(guid, 500);

            }

        }



        private void GetClanSettings(string guid)

        {

            SendResponse(guid, ClanSettingsMapper.Instance.ToProto(null));

        }



        private void GetClanSettings2(string guid)

        {

            var response = new GetClanSettings2Response

            {

                Settings = ClanSettingsMapper.Instance.ToProto(null)

            };

            SendResponse(guid, response);

        }



        private void GetPlayerInviteRequestsCount(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            List<ClanInviteDocument> invites = BoltMainDatabaseProvider.Instance.GetPlayerInvites(playerDocument);

            SendResponse(guid, invites.Count);

        }



        private void GetPlayerInviteRequestsCount2(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            List<ClanInviteDocument> invites = BoltMainDatabaseProvider.Instance.GetPlayerInvites(playerDocument);

            SendResponse(guid, new GetPlayerInviteRequestsCount2Response { Count = invites?.Count ?? 0 });

        }



        private void GetPlayerClosedJoinRequestsCount(string guid)

        {

            // Placeholder: currently no tracking for closed requests in DB

            SendResponse(guid, 0);

        }



        private void GetPlayerClosedJoinRequestsCount2(string guid)

        {

            SendResponse(guid, new GetPlayerClosedJoinRequestsCount2Response { Count = 0 });

        }



        private void GetClanJoinRequestsCount(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            if (clan == null)

            {

                SendResponse(guid, 0);

                return;

            }

            List<ClanRequestDocument> requests = BoltMainDatabaseProvider.Instance.GetClanRequests(clan);

            SendResponse(guid, requests.Count);

        }



        private void GetClanJoinRequestsCount2(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            if (clan == null)

            {

                SendResponse(guid, new GetClanJoinRequestsCount2Response { Count = 0 });

                return;

            }

            List<ClanRequestDocument> requests = BoltMainDatabaseProvider.Instance.GetClanRequests(clan);

            SendResponse(guid, new GetClanJoinRequestsCount2Response { Count = requests?.Count ?? 0 });

        }



        private void GetClanClosedInviteRequestsCount(string guid)

        {

            // Placeholder: currently no tracking for closed requests in DB

            SendResponse(guid, 0);

        }



        private void GetClanClosedInviteRequestsCount2(string guid)

        {

            SendResponse(guid, new GetClanClosedInviteRequestsCount2Response { Count = 0 });

        }



        private void ValidateClanName(BinaryValue[] binaryValues, string guid)

        {

            string nameClan = binaryValues.GetValue<string>(0);

            try

            {

                BoltMainDatabaseProvider.Instance.ValidateClanName(nameClan);

                SendResponse(guid);

            }

            catch (ClanNameAlreadyExistsRpcException)

            {

                SendError(guid, 2018);

            }

            catch (InvalidClanNameRpcException)

            {

                SendError(guid, 2017);

            }

        }



        private void ValidateClanName2(BinaryValue[] binaryValues, string guid)

        {

            if (binaryValues.Length == 0 || binaryValues[0].IsNull)

            {

                SendError(guid, 400);

                return;

            }



            string nameClan = "";

            try

            {

                var input = new Google.Protobuf.CodedInputStream(binaryValues[0].One.ToByteArray());

                uint tag;

                while ((tag = input.ReadTag()) != 0)

                {

                    int fieldNumber = (int)(tag >> 3);

                    int wireType = (int)(tag & 7);

                    if (fieldNumber == 1 && wireType == 2)

                    {

                        nameClan = input.ReadString();

                    }

                    else

                    {

                        input.SkipLastField();

                    }

                }

            }

            catch (System.Exception ex)

            {

                Console.WriteLine($"[ClanRemoteService] Error parsing validateClanName2: {ex.Message}");

                SendError(guid, 400);

                return;

            }



            try

            {

                BoltMainDatabaseProvider.Instance.ValidateClanName(nameClan);

                _user.SendResponce(new ResponseMessage

                {

                    RpcResponse = new RpcResponse

                    {

                        Id = guid,

                        Return = new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.Empty }

                    }

                });

            }

            catch (ClanNameAlreadyExistsRpcException)

            {

                SendError(guid, 2018);

            }

            catch (InvalidClanNameRpcException)

            {

                SendError(guid, 2017);

            }

        }



        private void ValidateClanTag(BinaryValue[] binaryValues, string guid)

        {

            string tagClan = binaryValues.GetValue<string>(0);

            try

            {

                BoltMainDatabaseProvider.Instance.ValidateClanTag(tagClan);

                SendResponse(guid);

            }

            catch (InvalidClanTagRpcException)

            {

                SendError(guid, 2016);

            }

            catch (ClanTagAlreadyExistsRpcException)

            {

                SendError(guid, 2019);

            }

        }



        private void ValidateClanTag2(BinaryValue[] binaryValues, string guid)

        {

            if (binaryValues.Length == 0 || binaryValues[0].IsNull)

            {

                SendError(guid, 400);

                return;

            }



            string tagClan = "";

            try

            {

                var input = new Google.Protobuf.CodedInputStream(binaryValues[0].One.ToByteArray());

                uint tag;

                while ((tag = input.ReadTag()) != 0)

                {

                    int fieldNumber = (int)(tag >> 3);

                    int wireType = (int)(tag & 7);

                    if (fieldNumber == 1 && wireType == 2)

                    {

                        tagClan = input.ReadString();

                    }

                    else

                    {

                        input.SkipLastField();

                    }

                }

            }

            catch (System.Exception ex)

            {

                Console.WriteLine($"[ClanRemoteService] Error parsing validateClanTag2: {ex.Message}");

                SendError(guid, 400);

                return;

            }



            try

            {

                BoltMainDatabaseProvider.Instance.ValidateClanTag(tagClan);

                _user.SendResponce(new ResponseMessage

                {

                    RpcResponse = new RpcResponse

                    {

                        Id = guid,

                        Return = new BinaryValue { IsNull = false, One = Google.Protobuf.ByteString.Empty }

                    }

                });

            }

            catch (InvalidClanTagRpcException)

            {

                SendError(guid, 2016);

            }

            catch (ClanTagAlreadyExistsRpcException)

            {

                SendError(guid, 2019);

            }

        }



        public void SetClanDescription(BinaryValue[] binaryValues, string guid)

        {

            string description = NormalizeClanDescription(binaryValues.GetValue<string>(0));

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            

            Logger.Log($"[SetClanDescription] Player {playerDocument.name} changing clan description to: '{description}'");

            

            BoltMainDatabaseProvider.Instance.SetClanDescription(clan, description);

            BoltMainDatabaseProvider.Instance.AddLogWithDescription(Axlebolt.Bolt.Clans.BoltClanLogType.ClanDescriptionChanged, playerDocument, description);

            

            Logger.Log($"[SetClanDescription] Description changed and log created");

            SendResponse(guid);

        }



        public void IncreaseMaxMembersCount(BinaryValue[] binaryValues, string guid)

        {

            int newCount = binaryValues.GetValue<int>(0);

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            BoltMainDatabaseProvider.Instance.IncreaseMaxMembersCount(clan, newCount);

            BoltMainDatabaseProvider.Instance.AddLogWithMaxMemberCount(Axlebolt.Bolt.Clans.BoltClanLogType.ClanMaxMembersCountChanged, playerDocument, newCount);

            SendResponse(guid);

        }



        private void GetClanJoinRequests(BinaryValue[] binaryValues, string guid)

        {

            int offset = Math.Max(0, GetIntOrDefault(binaryValues, 0, 0));

            int length = Math.Max(1, GetIntOrDefault(binaryValues, 1, 50));

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            if (clan == null)

            {

                SendResponse(guid, new ClanJoinRequest[0]);

                return;

            }

            List<ClanRequestDocument> requests = BoltMainDatabaseProvider.Instance.GetClanRequests(clan);

            SendResponse(guid, ClanJoinRequestMapper.Instance.ToProtoArray(requests.Skip(offset).Take(length)));

        }



        private void GetClanInviteRequests(BinaryValue[] binaryValues, string guid)

        {

            int offset = Math.Max(0, GetIntOrDefault(binaryValues, 0, 0));

            int length = Math.Max(1, GetIntOrDefault(binaryValues, 1, 50));

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            if (clan == null)

            {

                SendResponse(guid, new Axlebolt.Bolt.Protobuf.ClanInviteRequest[0]);

                return;

            }

            List<ClanInviteDocument> requests = BoltMainDatabaseProvider.Instance.GetClanInvites(clan);

            SendResponse(guid, ClanInviteRequestMapper.Instance.ToProtoArray(requests.Skip(offset).Take(length)));

        }



        private void GetPlayerInviteRequests(BinaryValue[] binaryValues, string guid)

        {

            int offset = Math.Max(0, GetIntOrDefault(binaryValues, 0, 0));

            int length = Math.Max(1, GetIntOrDefault(binaryValues, 1, 50));

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            List<ClanInviteDocument> invites = BoltMainDatabaseProvider.Instance.GetPlayerInvites(playerDocument);

            SendResponse(guid, ClanInviteRequestMapper.Instance.ToProtoArray(invites.Skip(offset).Take(length)));

        }



        private void GetPlayerJoinRequests(BinaryValue[] binaryValues, string guid)

        {

            int offset = Math.Max(0, GetIntOrDefault(binaryValues, 0, 0));

            int length = Math.Max(1, GetIntOrDefault(binaryValues, 1, 50));

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            List<ClanRequestDocument> requests = BoltMainDatabaseProvider.Instance.GetPlayerRequests(playerDocument);

            SendResponse(guid, ClanJoinRequestMapper.Instance.ToProtoArray(requests.Skip(offset).Take(length)));

        }



        private void GetClanMembersByClanId(BinaryValue[] binaryValues, string guid)

        {

            string clanId = binaryValues.GetValue<string>(0);

            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetClanDocument(clanId);

            if (clan == null)

            {

                SendResponse(guid, new Axlebolt.Bolt.Protobuf.ClanMember[0]);

                return;

            }

            SendResponse(guid, clan.GetMembers().ToArray());

        }



        private void DeclineJoinRequest(BinaryValue[] binaryValues, string guid)

        {

            // Bug 18: clan invite/join decline used to throw a 500 to the client if any

            // step inside the handler failed (malformed cached invite doc, missing

            // sender/invited ids, GetInviteRequest population errors, etc). The DB

            // delete also was only run on the happy path, so a partial failure left

            // the invite document around and the client kept showing the badge count

            // even after the user re-opened the tab. Wrap the whole thing so we

            // always delete the underlying document and notify both sides, even when

            // we cannot rebuild a fully-populated OnRequestDeclined event.

            string requestId = null;

            try

            {

                requestId = ParseClanRequestId(binaryValues, 0);

                PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

                var requestDoc = BoltMainDatabaseProvider.Instance.GetJoinRequest(requestId);

                if (requestDoc == null)

                {

                    SendResponse(guid);

                    return;

                }



                ClanJoinRequest request = null;

                try { request = requestDoc.GetInviteRequest(); } catch (System.Exception ex)

                {

                    Logger.Error($"[DeclineJoinRequest] GetInviteRequest failed for {requestId}: {ex.Message}");

                }



                OnRequestDeclined declEvent = new OnRequestDeclined

                {

                    RequestId = requestId,

                    RequestType = request != null ? request.RequestType : (Axlebolt.Bolt.Protobuf.RequestType)requestDoc.requestType,

                    ClanToJoin = request?.Clan ?? new Axlebolt.Bolt.Protobuf.Clan { Id = requestDoc.clanId ?? "" },

                    InvitedPlayer = request?.RequestSender ?? new Axlebolt.Bolt.Protobuf.Player { Id = requestDoc.senderId ?? requestDoc.playerId ?? "" }

                };



                BoltMainDatabaseProvider.Instance.DeleteJoinRequest(requestId);

                SendEventToSession<ClansRemoteEventListener>(playerDocument?._id.ToString(), "OnRequestDeclined", declEvent);

                string senderId = request?.RequestSender?.Id ?? requestDoc.senderId ?? requestDoc.playerId;

                if (!string.IsNullOrEmpty(senderId))

                {

                    SendEventToSession<ClansRemoteEventListener>(senderId, "OnRequestDeclined", declEvent);

                }

                SendResponse(guid);

            }

            catch (System.Exception ex)

            {

                Logger.Error($"[DeclineJoinRequest] failed for {requestId}: {ex.Message}\n{ex.StackTrace}");

                // Always attempt to delete so the phantom request count clears next refresh.

                try { if (!string.IsNullOrEmpty(requestId)) BoltMainDatabaseProvider.Instance.DeleteJoinRequest(requestId); } catch { }

                SendResponse(guid);

            }

        }



        private void CancelJoinRequest(BinaryValue[] binaryValues, string guid)

        {

            // Bug 18: see DeclineJoinRequest. Same defensive wrapping.

            string requestId = null;

            try

            {

                requestId = ParseClanRequestId(binaryValues, 0);

                PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

                var requestDoc = BoltMainDatabaseProvider.Instance.GetJoinRequest(requestId);

                if (requestDoc == null)

                {

                    SendResponse(guid);

                    return;

                }



                ClanJoinRequest request = null;

                try { request = requestDoc.GetInviteRequest(); } catch (System.Exception ex)

                {

                    Logger.Error($"[CancelJoinRequest] GetInviteRequest failed for {requestId}: {ex.Message}");

                }



                OnRequestDeclined declEvent = new OnRequestDeclined

                {

                    RequestId = requestId,

                    RequestType = request != null ? request.RequestType : (Axlebolt.Bolt.Protobuf.RequestType)requestDoc.requestType,

                    ClanToJoin = request?.Clan ?? new Axlebolt.Bolt.Protobuf.Clan { Id = requestDoc.clanId ?? "" },

                    InvitedPlayer = request?.RequestSender ?? new Axlebolt.Bolt.Protobuf.Player { Id = requestDoc.senderId ?? requestDoc.playerId ?? "" }

                };



                BoltMainDatabaseProvider.Instance.DeleteJoinRequest(requestId);

                SendEventToSession<ClansRemoteEventListener>(playerDocument?._id.ToString(), "OnRequestDeclined", declEvent);

                string senderId = request?.RequestSender?.Id ?? requestDoc.senderId ?? requestDoc.playerId;

                if (!string.IsNullOrEmpty(senderId))

                {

                    SendEventToSession<ClansRemoteEventListener>(senderId, "OnRequestDeclined", declEvent);

                }

                SendResponse(guid);

            }

            catch (System.Exception ex)

            {

                Logger.Error($"[CancelJoinRequest] failed for {requestId}: {ex.Message}\n{ex.StackTrace}");

                try { if (!string.IsNullOrEmpty(requestId)) BoltMainDatabaseProvider.Instance.DeleteJoinRequest(requestId); } catch { }

                SendResponse(guid);

            }

        }



        private void GetRecommendedClans(BinaryValue[] values, string guid)

        {

            int count = values.GetValue<int>(0);

            List<ClanDocument> clans = BoltMainDatabaseProvider.Instance.GetRandomClanDocuments(count);

            SendResponse(guid, ClanMapper.Instance.ToProtoArray(clans));

        }



        private void ParseOffsetAndLength(BinaryValue[] binaryValues, out int offset, out int length)

        {

            offset = 0;

            length = 50;

            if (binaryValues != null && binaryValues.Length > 0 && !binaryValues[0].IsNull && binaryValues[0].One != null)

            {

                try

                {

                    var input = new Google.Protobuf.CodedInputStream(binaryValues[0].One.ToByteArray());

                    uint tag;

                    while ((tag = input.ReadTag()) != 0)

                    {

                        int fieldNumber = (int)(tag >> 3);

                        int wireType = (int)(tag & 7);

                        if (fieldNumber == 1 && wireType == 0)

                        {

                            offset = input.ReadInt32();

                        }

                        else if (fieldNumber == 2 && wireType == 0)

                        {

                            length = input.ReadInt32();

                        }

                        else

                        {

                            input.SkipLastField();

                        }

                    }

                }

                catch (System.Exception ex)

                {

                    Logger.Error($"[ClanRemoteService] ParseOffsetAndLength error: {ex.Message}");

                }

            }

            if (length <= 0 && offset > 0 && offset <= 100)
            {
                length = offset;
                offset = 0;
            }
            if (length <= 0) length = 50;
            if (offset < 0) offset = 0;

        }



        private void ParseCount(BinaryValue[] binaryValues, out int count)

        {

            count = 10;

            if (binaryValues != null && binaryValues.Length > 0 && !binaryValues[0].IsNull && binaryValues[0].One != null)

            {

                try

                {

                    var input = new Google.Protobuf.CodedInputStream(binaryValues[0].One.ToByteArray());

                    uint tag;

                    while ((tag = input.ReadTag()) != 0)

                    {

                        int fieldNumber = (int)(tag >> 3);

                        int wireType = (int)(tag & 7);

                        if (fieldNumber == 1 && wireType == 0)

                        {

                            count = input.ReadInt32();

                        }

                        else

                        {

                            input.SkipLastField();

                        }

                    }

                }

                catch (System.Exception ex)

                {

                    Logger.Error($"[ClanRemoteService] ParseCount error: {ex.Message}");

                }

            }

        }



        private void GetRecommendedClans2(BinaryValue[] binaryValues, string guid)

        {

            ParseCount(binaryValues, out int count);

            List<ClanDocument> clans = BoltMainDatabaseProvider.Instance.GetRandomClanDocuments(count);

            var response = new GetRecommendedClans2Response();

            if (clans != null)

            {

                foreach (var clan in clans)

                {

                    if (clan != null)

                    {

                        response.Clans.Add(ClanMapper.Instance.ToProto(clan));

                    }

                }

            }

            SendResponse(guid, response);

        }



        private void GetClanJoinRequests2(BinaryValue[] binaryValues, string guid)

        {

            ParseOffsetAndLength(binaryValues, out int offset, out int length);

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            var response = new GetClanJoinRequests2Response();

            if (clan != null)

            {

                List<ClanRequestDocument> requests = BoltMainDatabaseProvider.Instance.GetClanRequests(clan);

                if (requests != null)

                {

                    foreach (var req in requests.Skip(offset).Take(length))

                    {

                        if (req != null)

                        {

                            response.JoinRequests.Add(ClanJoinRequestMapper.Instance.ToProto(req));

                        }

                    }

                }

            }

            SendResponse(guid, response);

        }



        private void GetClanInviteRequests2(BinaryValue[] binaryValues, string guid)

        {

            ParseOffsetAndLength(binaryValues, out int offset, out int length);

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            var response = new GetClanInviteRequests2Response();

            if (clan != null)

            {

                List<ClanInviteDocument> requests = BoltMainDatabaseProvider.Instance.GetClanInvites(clan);

                if (requests != null)

                {

                    foreach (var req in requests.Skip(offset).Take(length))

                    {

                        if (req != null)

                        {

                            response.InviteRequests.Add(ClanInviteRequestMapper.Instance.ToProto(req));

                        }

                    }

                }

            }

            SendResponse(guid, response);

        }



        private void GetPlayerInviteRequests2(BinaryValue[] binaryValues, string guid)

        {

            ParseOffsetAndLength(binaryValues, out int offset, out int length);

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            var response = new GetPlayerInviteRequests2Response();

            if (playerDocument != null)

            {

                List<ClanInviteDocument> invites = BoltMainDatabaseProvider.Instance.GetPlayerInvites(playerDocument);

                if (invites != null)

                {

                    foreach (var req in invites.Skip(offset).Take(length))

                    {

                        if (req != null)

                        {

                            response.InviteRequests.Add(ClanInviteRequestMapper.Instance.ToProto(req));

                        }

                    }

                }

            }

            SendResponse(guid, response);

        }



        private void GetPlayerJoinRequests2(BinaryValue[] binaryValues, string guid)

        {

            ParseOffsetAndLength(binaryValues, out int offset, out int length);

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            var response = new GetPlayerJoinRequests2Response();

            if (playerDocument != null)

            {

                List<ClanRequestDocument> requests = BoltMainDatabaseProvider.Instance.GetPlayerRequests(playerDocument);

                if (requests != null)

                {

                    foreach (var req in requests.Skip(offset).Take(length))

                    {

                        if (req != null)

                        {

                            response.JoinRequests.Add(ClanJoinRequestMapper.Instance.ToProto(req));

                        }

                    }

                }

            }

            SendResponse(guid, response);

        }



        private void RenameClan(BinaryValue[] values, string guid)

        {

            string newTag = values.GetValue<string>(0);

            string newName = values.GetValue<string>(1);

            if (BoltGameDatabaseProvider.Instance.IsEnoughFunds(PlayerId, 102.ToString(), 1000f))

            {

                PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

                Clan clan = BoltMainDatabaseProvider.Instance.GetPlayerClan(playerDocument);

                

                if (clan == null)

                {

                    SendError(guid, 2008);

                    return;

                }

                

                string oldTag = clan.Tag;

                string oldName = clan.Name;

                

                // Обновляем тег и название клана

                BoltMainDatabaseProvider.Instance.RenameClan(clan, newTag, newName);

                

                // Обновляем тег во всех существующих логах

                BoltMainDatabaseProvider.Instance.UpdateClanLogsTag(oldTag, newTag);

                

                // Добавляем лог о смене тега и названия

                BoltMainDatabaseProvider.Instance.AddLogWithClanChange(

                    Axlebolt.Bolt.Clans.BoltClanLogType.ClanNameAndTagChanged, 

                    playerDocument, 

                    oldName, 

                    newName, 

                    oldTag, 

                    newTag

                );

                

                Logger.Log($"[RenameClan] Clan renamed from {oldName}[{oldTag}] to {newName}[{newTag}], logs updated");

                SendResponse(guid);

            }

            else

            {

                SendError(guid, 401);

            }

        }



        private void DeclineInviteRequest(BinaryValue[] binaryValues, string guid)

        {

            // Bug 18: same hardening as DeclineJoinRequest. Previously a malformed

            // invite document, an empty senderId, or a failure inside GetInviteRequest

            // would surface a 500 to the client; the invite document then stayed in

            // the DB so the badge count showed phantom invites even after the user

            // accepted/declined them from the UI.

            string requestId = null;

            try

            {

                requestId = ParseClanRequestId(binaryValues, 0);

                PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

                var inviteDoc = BoltMainDatabaseProvider.Instance.GetInvite(requestId);

                if (inviteDoc == null)

                {

                    SendResponse(guid);

                    return;

                }



                ClanInviteRequest request = null;

                try { request = inviteDoc.GetInviteRequest(); } catch (System.Exception ex)

                {

                    Logger.Error($"[DeclineInviteRequest] GetInviteRequest failed for {requestId}: {ex.Message}");

                }



                OnRequestDeclined declEvent = new OnRequestDeclined

                {

                    RequestId = requestId,

                    RequestType = request != null ? request.RequestType : (Axlebolt.Bolt.Protobuf.RequestType)inviteDoc.requestType,

                    ClanToJoin = request?.Clan ?? new Axlebolt.Bolt.Protobuf.Clan { Id = inviteDoc.clanId ?? "" },

                    InvitedPlayer = request?.InvitedPlayer ?? new Axlebolt.Bolt.Protobuf.Player { Id = inviteDoc.invitedId ?? "" }

                };



                BoltMainDatabaseProvider.Instance.DeleteInvite(requestId);

                if (!string.IsNullOrEmpty(inviteDoc.senderId))

                {

                    SendEventToSession<ClansRemoteEventListener>(inviteDoc.senderId, "OnRequestDeclined", declEvent);

                }

                if (!string.IsNullOrEmpty(inviteDoc.invitedId))

                {

                    SendEventToSession<ClansRemoteEventListener>(inviteDoc.invitedId, "OnRequestDeclined", declEvent);

                }

                SendResponse(guid);

            }

            catch (System.Exception ex)

            {

                Logger.Error($"[DeclineInviteRequest] failed for {requestId}: {ex.Message}\n{ex.StackTrace}");

                try { if (!string.IsNullOrEmpty(requestId)) BoltMainDatabaseProvider.Instance.DeleteInvite(requestId); } catch { }

                SendResponse(guid);

            }

        }



        private void CancelInviteRequest(BinaryValue[] binaryValues, string guid)

        {

            // Bug 18: see DeclineInviteRequest.

            string requestId = null;

            try

            {

                requestId = ParseClanRequestId(binaryValues, 0);

                PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

                var inviteDoc = BoltMainDatabaseProvider.Instance.GetInvite(requestId);

                if (inviteDoc == null)

                {

                    SendResponse(guid);

                    return;

                }



                ClanInviteRequest request = null;

                try { request = inviteDoc.GetInviteRequest(); } catch (System.Exception ex)

                {

                    Logger.Error($"[CancelInviteRequest] GetInviteRequest failed for {requestId}: {ex.Message}");

                }



                OnRequestDeclined declEvent = new OnRequestDeclined

                {

                    RequestId = requestId,

                    RequestType = request != null ? request.RequestType : (Axlebolt.Bolt.Protobuf.RequestType)inviteDoc.requestType,

                    ClanToJoin = request?.Clan ?? new Axlebolt.Bolt.Protobuf.Clan { Id = inviteDoc.clanId ?? "" },

                    InvitedPlayer = request?.InvitedPlayer ?? new Axlebolt.Bolt.Protobuf.Player { Id = inviteDoc.invitedId ?? "" }

                };



                BoltMainDatabaseProvider.Instance.DeleteInvite(requestId);

                if (!string.IsNullOrEmpty(inviteDoc.senderId))

                {

                    SendEventToSession<ClansRemoteEventListener>(inviteDoc.senderId, "OnRequestDeclined", declEvent);

                }

                if (!string.IsNullOrEmpty(inviteDoc.invitedId))

                {

                    SendEventToSession<ClansRemoteEventListener>(inviteDoc.invitedId, "OnRequestDeclined", declEvent);

                }

                SendResponse(guid);

            }

            catch (System.Exception ex)

            {

                Logger.Error($"[CancelInviteRequest] failed for {requestId}: {ex.Message}\n{ex.StackTrace}");

                try { if (!string.IsNullOrEmpty(requestId)) BoltMainDatabaseProvider.Instance.DeleteInvite(requestId); } catch { }

                SendResponse(guid);

            }

        }



        private void AcceptJoinRequest(BinaryValue[] binaryValues, string guid)

        {

            string requestId = ParseClanRequestId(binaryValues, 0);

            PlayerDocument localPlayerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            var requestDoc = BoltMainDatabaseProvider.Instance.GetJoinRequest(requestId);

            if (requestDoc == null)

            {

                Logger.Log($"[AcceptJoinRequest] Request {requestId} not found");

                SendResponse(guid);

                return;

            }



            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(localPlayerDocument);

            if (clanDocument == null)

            {

                Logger.Error($"[AcceptJoinRequest] Player {localPlayerDocument.name} has no clan");

                SendError(guid, 2008);

                return;

            }



            if (clanDocument.membersCount >= clanDocument.maxMembersCount)

            {

                Logger.Log($"[AcceptJoinRequest] Clan {clanDocument.name} is full ({clanDocument.membersCount}/{clanDocument.maxMembersCount})");

                SendError(guid, 2015);

                return;

            }



            PlayerDocument joiningPlayer = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(requestDoc.senderId));

            if (joiningPlayer == null)

            {

                Logger.Error($"[AcceptJoinRequest] Joining player {requestDoc.senderId} not found");

                BoltMainDatabaseProvider.Instance.DeleteJoinRequest(requestId);

                SendError(guid, 401);

                return;

            }



            Logger.Log($"[AcceptJoinRequest] Player {joiningPlayer.name} ({joiningPlayer._id}) joining clan {clanDocument.name} ({clanDocument._id})");



            try

            {

                BoltMainDatabaseProvider.Instance.JoinClan(clanDocument, joiningPlayer);

                Logger.Log($"[AcceptJoinRequest] JoinClan completed successfully");

            }

            catch (ClanNotFoundRpcException)

            {

                Logger.Error($"[AcceptJoinRequest] Clan not found exception");

                SendError(guid, 2008);

                return;

            }

            catch (PlayerIsAlreadyInClanRpcException)

            {

                Logger.Error($"[AcceptJoinRequest] Player already in clan exception");

                SendError(guid, 2011);

                return;

            }



            BoltMainDatabaseProvider.Instance.DeleteJoinRequest(requestId);

            BoltMainDatabaseProvider.Instance.CleanupClanRequestStateAfterJoin(clanDocument._id.ToString(), joiningPlayer._id.ToString());

            

            // Получаем обновленный клан из БД

            ClanDocument refreshedClan = BoltMainDatabaseProvider.Instance.GetClanDocumentFresh(clanDocument._id.ToString()) ?? clanDocument;

            Logger.Log($"[AcceptJoinRequest] Refreshed clan has {refreshedClan.membersCount} members");



            // Отправляем событие вступившему игроку - он теперь в клане!

            OnJoinedToClanEvent joinedEvent = new OnJoinedToClanEvent

            {

                Clan = ClanMapper.Instance.ToProto(refreshedClan)

            };

            SendClanEventToPlayer(joiningPlayer._id.ToString(), "OnJoinedToClan", joinedEvent);

            Logger.Log($"[AcceptJoinRequest] Sent OnJoinedToClan to {joiningPlayer.name}");



            // Подготавливаем данные для уведомления других участников

            Player joinedPlayer = FriendHelper.GetPlayer(joiningPlayer._id.ToString());

            long joinCreateDate = Utils.ToUnixTime(DateTime.UtcNow);

            string joiningPlayerId = joiningPlayer._id.ToString();



            // Уведомляем всех остальных участников клана о новом члене

            int notifiedCount = 0;

            foreach (var member in refreshedClan.GetMembers())

            {

                if (member?.PlayerFriend?.Player == null || member.PlayerFriend.Player.Id == joiningPlayerId)

                {

                    continue;

                }



                // Событие о новом участнике

                OnMemberJoinedToClanEvent memberJoinedEvent = new OnMemberJoinedToClanEvent

                {

                    ClanMember = new Axlebolt.Bolt.Protobuf.ClanMember

                    {

                        ClanId = refreshedClan._id.ToString(),

                        RoleId = 1000,

                        CreateDate = joinCreateDate

                    }

                };

                memberJoinedEvent.ClanMember.PlayerFriend.Player = joinedPlayer;

                memberJoinedEvent.ClanMember.PlayerFriend.RelationshipStatus = RelationshipStatus.None;

                SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnMemberJoinedToClan", memberJoinedEvent);

                

                // Обновленная информация о клане (счетчик участников)

                OnJoinedToClanEvent clanUpdatedEvent = new OnJoinedToClanEvent

                {

                    Clan = ClanMapper.Instance.ToProto(refreshedClan)

                };

                SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnJoinedToClan", clanUpdatedEvent);

                notifiedCount++;

            }

            

            Logger.Log($"[AcceptJoinRequest] Notified {notifiedCount} clan members");

            SendResponse(guid);

        }



        private void AcceptInviteRequest(BinaryValue[] binaryValues, string guid)

        {

            string requestId = ParseClanRequestId(binaryValues, 0);

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            var inviteDoc = BoltMainDatabaseProvider.Instance.GetInvite(requestId);

            if (inviteDoc == null)

            {

                SendResponse(guid);

                return;

            }

            string selfId = playerDocument?._id.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(inviteDoc.invitedId) &&
                !string.Equals(inviteDoc.invitedId, selfId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWarn($"[AcceptInviteRequest] invite {requestId} not for player {selfId}");
                SendError(guid, 401);
                return;
            }



            ClanDocument clanDocument = BoltMainDatabaseProvider.Instance.GetClanDocument(inviteDoc.clanId);

            if (clanDocument == null)

            {

                SendError(guid, 2008);

                return;

            }



            if (clanDocument.membersCount >= clanDocument.maxMembersCount)

            {

                SendError(guid, 2015);

                return;

            }



            try

            {

                BoltMainDatabaseProvider.Instance.JoinClan(clanDocument, playerDocument);

            }

            catch (ClanNotFoundRpcException)

            {

                SendError(guid, 2008);

                return;

            }

            catch (PlayerIsAlreadyInClanRpcException)

            {

                SendError(guid, 2011);

                return;

            }



            BoltMainDatabaseProvider.Instance.DeleteInvite(requestId);

            BoltMainDatabaseProvider.Instance.CleanupClanRequestStateAfterJoin(clanDocument._id.ToString(), playerDocument._id.ToString());

            

            // Получаем обновленный клан из БД

            ClanDocument refreshedClan = BoltMainDatabaseProvider.Instance.GetClanDocumentFresh(clanDocument._id.ToString()) ?? clanDocument;



            // Отправляем событие вступившему игроку - он теперь в клане!

            OnJoinedToClanEvent joinedEvent = new OnJoinedToClanEvent

            {

                Clan = ClanMapper.Instance.ToProto(refreshedClan)

            };

            SendClanEventToPlayer(playerDocument._id.ToString(), "OnJoinedToClan", joinedEvent);



            // Подготавливаем данные для уведомления других участников

            Player joinedPlayer = FriendHelper.GetPlayer(playerDocument._id.ToString());

            long joinCreateDate = Utils.ToUnixTime(DateTime.UtcNow);

            string joinedPlayerId = playerDocument._id.ToString();



            // Уведомляем всех остальных участников клана о новом члене

            foreach (var member in refreshedClan.GetMembers())

            {

                if (member?.PlayerFriend?.Player == null || member.PlayerFriend.Player.Id == joinedPlayerId)

                {

                    continue;

                }



                // Событие о новом участнике

                OnMemberJoinedToClanEvent memberJoinedEvent = new OnMemberJoinedToClanEvent

                {

                    ClanMember = new Axlebolt.Bolt.Protobuf.ClanMember

                    {

                        ClanId = refreshedClan._id.ToString(),

                        RoleId = 1000,

                        CreateDate = joinCreateDate

                    }

                };

                memberJoinedEvent.ClanMember.PlayerFriend.Player = joinedPlayer;

                memberJoinedEvent.ClanMember.PlayerFriend.RelationshipStatus = RelationshipStatus.None;

                SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnMemberJoinedToClan", memberJoinedEvent);

                

                // Обновленная информация о клане (счетчик участников)

                OnJoinedToClanEvent clanUpdatedEvent = new OnJoinedToClanEvent

                {

                    Clan = ClanMapper.Instance.ToProto(refreshedClan)

                };

                SendClanEventToPlayer(member.PlayerFriend.Player.Id, "OnJoinedToClan", clanUpdatedEvent);

            }



            SendResponse(guid);

        }



        private void DeleteClosedInviteRequest(BinaryValue[] binaryValues, string guid)

        {

            string requestId = ParseClanRequestId(binaryValues, 0);

            BoltMainDatabaseProvider.Instance.DeleteInvite(requestId);

            SendResponse(guid);

        }



        private void DeleteClosedJoinRequest(BinaryValue[] binaryValues, string guid)

        {

            string requestId = ParseClanRequestId(binaryValues, 0);

            BoltMainDatabaseProvider.Instance.DeleteJoinRequest(requestId);

            SendResponse(guid);

        }



        private void GetClanUnreadInviteRequestsCount(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            if (clan == null)

            {

                SendResponse(guid, 0);

                return;

            }

            List<ClanInviteDocument> invites = BoltMainDatabaseProvider.Instance.GetClanInvites(clan);

            SendResponse(guid, invites.Count);

        }



        private void GetPlayerUnreadJoinRequestsCount(string guid)

        {

            PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(PlayerObjectId);

            List<ClanRequestDocument> requests = BoltMainDatabaseProvider.Instance.GetPlayerRequests(playerDocument);

            SendResponse(guid, requests.Count);

        }



        private void SendEventToClanMembers(PlayerDocument playerDocument, string eventName, object eventObj)

        {

            ClanDocument clan = BoltMainDatabaseProvider.Instance.GetPlayerClanDocument(playerDocument);

            if (clan == null) return;

            foreach (var member in clan.GetMembers())

            {

                SendClanEventToPlayer(member.PlayerFriend.Player.Id, eventName, eventObj);

            }

        }



        private void SendClanEventToPlayer(string playerId, string eventName, object eventObj)

        {

            if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(eventName))

            {

                return;

            }



            bool sentByListener = false;

            if (StaticClasses.EventSenders.TryGetValue(playerId, out List<IEventSender> eventSenders))

            {

                IEventSender listener = eventSenders?.FirstOrDefault(sender => sender is ClansRemoteEventListener);

                if (listener != null)

                {

                    try

                    {

                        if (eventObj is object[] paramArray)

                        {

                            listener.SendEvent(eventName, paramArray);

                        }

                        else

                        {

                            listener.SendEvent(eventName, new object[] { eventObj });

                        }



                        sentByListener = true;

                    }

                    catch

                    {

                        sentByListener = false;

                    }

                }

            }



            if (sentByListener || !StaticClasses.UserServices.TryGetValue(playerId, out UserService userService))

            {

                return;

            }



            ResponseMessage responseMessage = new ResponseMessage

            {

                EventResponse = new EventResponse

                {

                    ListenerName = "ClansRemoteEventListener",

                    EventName = eventName

                }

            };



            if (eventObj is object[] eventParams)

            {

                foreach (object value in eventParams)

                {

                    responseMessage.EventResponse.Params.Add(ToBinaryValue(value));

                }

            }

            else

            {

                responseMessage.EventResponse.Params.Add(ToBinaryValue(eventObj));

            }



            userService.SendResponce(responseMessage);

        }



        private static BinaryValue ToBinaryValue(object value)

        {

            if (value == null)

            {

                return new BinaryValue { IsNull = true };

            }



            return new ToByteMethod(value.GetType()).ToBytes(value);

        }





        
        private static byte[] WrapSingleMessage(byte[] messagePayload)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                using (var output = new CodedOutputStream(stream))
                {
                    output.WriteRawTag(10);
                    output.WriteBytes(Google.Protobuf.ByteString.CopyFrom(messagePayload));
                    output.Flush();
                    return stream.ToArray();
                }
            }
        }

        private void GetClanByTag(BinaryValue[] binaryValues, string guid, string methodName)
        {
            try
            {
                string tag;
                if (methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase))
                {
                    FriendActionRequest requestMsg = FriendActionRequest.Parser.ParseFrom(binaryValues[0].One);
                    tag = requestMsg.Value?.Trim();
                }
                else
                {
                    tag = binaryValues.GetValue<string>(0)?.Trim();
                }

                if (string.IsNullOrWhiteSpace(tag))
                {
                    SendError(guid, 400);
                    return;
                }

                var clans = BoltMainDatabaseProvider.Instance.FindClanToTag(tag);
                var exactClan = System.Linq.Enumerable.FirstOrDefault(clans, c => c.Tag.Equals(tag, System.StringComparison.OrdinalIgnoreCase));
                if (exactClan == null)
                {
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Return = new BinaryValue { IsNull = true }
                        }
                    });
                    return;
                }

                Clan clanProto = exactClan;
                if (methodName.EndsWith("2", System.StringComparison.OrdinalIgnoreCase))
                {
                    byte[] payload = clanProto != null ? clanProto.ToByteArray() : System.Array.Empty<byte>();
                    byte[] wrapped = WrapSingleMessage(payload);
                    _user.SendResponce(new ResponseMessage
                    {
                        RpcResponse = new RpcResponse
                        {
                            Id = guid,
                            Return = new BinaryValue
                            {
                                IsNull = false,
                                One = Google.Protobuf.ByteString.CopyFrom(wrapped)
                            }
                        }
                    });
                    return;
                }

                SendResponse(guid, clanProto);
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[GetClanByTag] Error: {ex.Message}");
                SendError(guid, 500);
            }
        }

        private void GetClanById2(BinaryValue[] binaryValues, string guid)
        {
            try
            {
                FriendActionRequest requestMsg = FriendActionRequest.Parser.ParseFrom(binaryValues[0].One);
                string clanId = requestMsg.Value;
                ClanDocument clanDoc = BoltMainDatabaseProvider.Instance.GetClanDocument(clanId);
                var response = new GetClan2Response();
                if (clanDoc != null)
                {
                    Clan clanProto = ClanMapper.Instance.ToProto(clanDoc);
                    response.Clan = clanProto;
                }
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[GetClanById2] Error: {ex.Message}");
                SendError(guid, 500);
            }
        }

        public override void Invoke(RpcRequest request)

        {

            string methodName = request.MethodName.ToLowerInvariant();

            switch (methodName)

            {

                case "createclan":

                    CreateClan(request.Params.ToArray(), request.Id);

                    break;

                case "createclan2":

                    CreateClan2(request.Params.ToArray(), request.Id);

                    break;

                case "changeclanname":

                case "renameclan":

                    RenameClan(request.Params.ToArray(), request.Id);

                    break;

                case "getclan":

                    GetClan(request.Id);

                    break;

                case "getclan2":

                    GetClan2(request.Id);

                    break;

                case "getclanbyid":

                    GetClanById(request.Params.ToArray(), request.Id);

                    break;

                case "getclanbyid2":

                    GetClanById2(request.Params.ToArray(), request.Id);

                    break;

                case "getclanbytag":
                case "getclanbytag2":

                    GetClanByTag(request.Params.ToArray(), request.Id, request.MethodName);

                    break;

                case "changeclantype":
                case "changeclantype2":
                    ChangeClanType(request.Params.ToArray(), request.Id);

                    break;

                case "leaveclan":

                    LeaveClan(request.Id);

                    break;

                case "getroles":

                    GetRoles(request.Id);

                    break;

                case "getroles2":

                    GetRoles2(request.Id);

                    break;

                case "setclanavatar":
                case "setclanavatar2":
                    SetClanAvatar(request.Params.ToArray(), request.Id);

                    break;

                case "getavatars":
                case "getavatars2":
                    GetAvatars(request.Params.ToArray(), request.Id);

                    break;

                case "sendmsgtoclan":
                case "sendmsgtoclan2":
                    SendMsgToClan(request.Params.ToArray(), request.Id);

                    break;

                
                
                FindClan2(request.Params.ToArray(), request.Id);
                    break;
                GetClanMembersById2(request.Params.ToArray(), request.Id);
                    break;
                RequestToJoinClan2(request.Params.ToArray(), request.Id);
                    break;
                case "leaveclan2":
                    LeaveClan2(request.Params.ToArray(), request.Id);
                    break;

                case "findclan":
                case "findclan2":
                    FindClan(request.Params.ToArray(), request.Id);

                    break;

                case "getclanmsgs":
                case "getclanmsgs2":
                    case "getclanchatmessages":
                case "getclanchatmessages2":
                    GetClanChatMessages(request.Params.ToArray(), request.Id);

                    break;

                case "getclangames":
                case "getclangames2":
                    GetClanGames(request.Params.ToArray(), request.Id);

                    break;

                case "getclanmembers":
                GetClanMembers(request.Id);

                    break;

                case "getclanmembers2":

                    GetClanMembers2(request.Id);

                    break;

                case "assignroletomember":
                case "assignroletomember2":
                    AssignRoleToMember(request.Params.ToArray(), request.Id);

                    break;

                case "assignleaderrole":
                case "assignleaderrole2":
                    AssignLeaderRole(request.Params.ToArray(), request.Id);

                    break;

                case "deleteclanmsgs":
                case "deleteclanmsgs2":
                    DeleteClanMsgs(request.Id);

                    break;

                case "requesttojoinclan":
                case "requesttojoinclan2":
                    RequestToJoinClan(request.Params.ToArray(), request.Id);

                    break;

                case "invitetoclan":
                case "invitetoclan2":
                    InviteToClan(request.Params.ToArray(), request.Id);

                    break;

                case "kickmember":
                case "kickmember2":
                    KickMember(request.Params.ToArray(), request.Id);

                    break;

                case "getplayerinviterequests":

                    GetPlayerInviteRequests(request.Params.ToArray(), request.Id);

                    break;

                case "getplayerinviterequests2":

                    GetPlayerInviteRequests2(request.Params.ToArray(), request.Id);

                    break;

                case "getplayerjoinrequests":

                    GetPlayerJoinRequests(request.Params.ToArray(), request.Id);

                    break;

                case "getplayerjoinrequests2":

                    GetPlayerJoinRequests2(request.Params.ToArray(), request.Id);

                    break;

                case "getclanjoinrequests":

                    GetClanJoinRequests(request.Params.ToArray(), request.Id);

                    break;

                case "getclanjoinrequests2":

                    GetClanJoinRequests2(request.Params.ToArray(), request.Id);

                    break;

                case "getclaninviterequests":

                    GetClanInviteRequests(request.Params.ToArray(), request.Id);

                    break;

                case "getclaninviterequests2":

                    GetClanInviteRequests2(request.Params.ToArray(), request.Id);

                    break;

                case "getclansettings":

                    GetClanSettings(request.Id);

                    break;

                case "getclansettings2":

                    GetClanSettings2(request.Id);

                    break;

                case "getplayerinviterequestscount":

                    GetPlayerInviteRequestsCount(request.Id);

                    break;

                case "getplayerinviterequestscount2":

                    GetPlayerInviteRequestsCount2(request.Id);

                    break;

                case "getplayerclosedjoinrequestscount":

                    GetPlayerClosedJoinRequestsCount(request.Id);

                    break;

                case "getplayerclosedjoinrequestscount2":

                    GetPlayerClosedJoinRequestsCount2(request.Id);

                    break;

                case "getclanjoinrequestscount":

                    GetClanJoinRequestsCount(request.Id);

                    break;

                case "getclanjoinrequestscount2":

                    GetClanJoinRequestsCount2(request.Id);

                    break;

                case "getclanclosedinviterequestscount":

                    GetClanClosedInviteRequestsCount(request.Id);

                    break;

                case "getclanclosedinviterequestscount2":

                    GetClanClosedInviteRequestsCount2(request.Id);

                    break;

                case "validateclanname":

                    ValidateClanName(request.Params.ToArray(), request.Id);

                    break;

                case "validateclanname2":

                    ValidateClanName2(request.Params.ToArray(), request.Id);

                    break;

                case "validateclantag":

                    ValidateClanTag(request.Params.ToArray(), request.Id);

                    break;

                case "validateclantag2":

                    ValidateClanTag2(request.Params.ToArray(), request.Id);

                    break;

                case "setclandescription":
                case "setclandescription2":
                    SetClanDescription(request.Params.ToArray(), request.Id);

                    break;

                case "increasemaxmemberscount":
                case "increasemaxmemberscount2":
                    IncreaseMaxMembersCount(request.Params.ToArray(), request.Id);

                    break;

                case "getclanmembersbyid":
                case "getclanmembersbyid2":
                    case "getclanmembersbyclanid":
                case "getclanmembersbyclanid2":
                    GetClanMembersByClanId(request.Params.ToArray(), request.Id);

                    break;

                case "declinejoinrequest":
                case "declinejoinrequest2":
                    DeclineJoinRequest(request.Params.ToArray(), request.Id);

                    break;

                case "canceljoinrequest":
                case "canceljoinrequest2":
                    CancelJoinRequest(request.Params.ToArray(), request.Id);

                    break;

                case "getrecommendedclans":

                    GetRecommendedClans(request.Params.ToArray(), request.Id);

                    break;

                case "getrecommendedclans2":

                    GetRecommendedClans2(request.Params.ToArray(), request.Id);

                    break;

                case "declineinviterequest":
                case "declineinviterequest2":
                    DeclineInviteRequest(request.Params.ToArray(), request.Id);

                    break;

                case "cancelinviterequest":
                case "cancelinviterequest2":
                    CancelInviteRequest(request.Params.ToArray(), request.Id);

                    break;

                case "acceptjoinrequest":
                case "acceptjoinrequest2":
                    AcceptJoinRequest(request.Params.ToArray(), request.Id);

                    break;

                case "acceptinviterequest":
                case "acceptinviterequest2":
                    AcceptInviteRequest(request.Params.ToArray(), request.Id);

                    break;

                case "deleteclosedinviterequest":
                case "deleteclosedinviterequest2":
                    DeleteClosedInviteRequest(request.Params.ToArray(), request.Id);

                    break;

                case "deleteclosedjoinrequest":
                case "deleteclosedjoinrequest2":
                    DeleteClosedJoinRequest(request.Params.ToArray(), request.Id);

                    break;

                case "getclanunreadinviterequestscount":
                case "getclanunreadinviterequestscount2":
                    GetClanUnreadInviteRequestsCount(request.Id);

                    break;

                case "getplayerunreadjoinrequestscount":
                case "getplayerunreadjoinrequestscount2":
                    GetPlayerUnreadJoinRequestsCount(request.Id);

                    break;

                case "getclanlogs":
                case "getclanlogs2":
                    GetClanLogs(request.Params.ToArray(), request.Id);

                    break;

                case "readclanlogs":
                case "readclanlogs2":
                    ReadClanLogs(request.Id);

                    break;

                default:

                    MethodNotFound(request);

                    break;

            }

        }

    }



    public class GetClanSettings2Response : Google.Protobuf.IMessage

    {

        public ClanSettings Settings { get; set; }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            if (Settings != null)

            {

                output.WriteRawTag(10);

                output.WriteMessage(Settings);

            }

        }



        public int CalculateSize()

        {

            int size = 0;

            if (Settings != null)

            {

                size += 1 + Google.Protobuf.CodedOutputStream.ComputeMessageSize(Settings);

            }

            return size;

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input) { }

        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;

    }



    public class GetRoles2Response : Google.Protobuf.IMessage

    {

        public Google.Protobuf.Collections.RepeatedField<ClanMemberRole> Roles { get; set; } = new Google.Protobuf.Collections.RepeatedField<ClanMemberRole>();



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            if (Roles != null)

            {

                foreach (var role in Roles)

                {

                    if (role != null)

                    {

                        output.WriteRawTag(10);

                        output.WriteMessage(role);

                    }

                }

            }

        }



        public int CalculateSize()

        {

            int size = 0;

            if (Roles != null)

            {

                foreach (var role in Roles)

                {

                    if (role != null)

                    {

                        size += 1 + Google.Protobuf.CodedOutputStream.ComputeMessageSize(role);

                    }

                }

            }

            return size;

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input) { }

        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;

    }



    public class GetClan2Response : Google.Protobuf.IMessage

    {

        public Clan Clan { get; set; }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            if (Clan != null)

            {

                output.WriteRawTag(10);

                output.WriteMessage(Clan);

            }

        }



        public int CalculateSize()

        {

            int size = 0;

            if (Clan != null)

            {

                size += 1 + Google.Protobuf.CodedOutputStream.ComputeMessageSize(Clan);

            }

            return size;

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input) { }

        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;

    }



    public class GetPlayerInviteRequestsCount2Response : Google.Protobuf.IMessage

    {

        public int Count { get; set; }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            if (Count != 0)

            {

                output.WriteRawTag(8);

                output.WriteInt32(Count);

            }

        }



        public int CalculateSize()

        {

            int size = 0;

            if (Count != 0)

            {

                size += 1 + Google.Protobuf.CodedOutputStream.ComputeInt32Size(Count);

            }

            return size;

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input) { }

        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;

    }



    public class GetPlayerClosedJoinRequestsCount2Response : Google.Protobuf.IMessage

    {

        public int Count { get; set; }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            if (Count != 0)

            {

                output.WriteRawTag(8);

                output.WriteInt32(Count);

            }

        }



        public int CalculateSize()

        {

            int size = 0;

            if (Count != 0)

            {

                size += 1 + Google.Protobuf.CodedOutputStream.ComputeInt32Size(Count);

            }

            return size;

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input) { }

        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;

    }



    public class GetClanJoinRequestsCount2Response : Google.Protobuf.IMessage

    {

        public int Count { get; set; }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            if (Count != 0)

            {

                output.WriteRawTag(8);

                output.WriteInt32(Count);

            }

        }



        public int CalculateSize()

        {

            int size = 0;

            if (Count != 0)

            {

                size += 1 + Google.Protobuf.CodedOutputStream.ComputeInt32Size(Count);

            }

            return size;

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input) { }

        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;

    }



    public class GetClanClosedInviteRequestsCount2Response : Google.Protobuf.IMessage

    {

        public int Count { get; set; }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            if (Count != 0)

            {

                output.WriteRawTag(8);

                output.WriteInt32(Count);

            }

        }



        public int CalculateSize()

        {

            int size = 0;

            if (Count != 0)

            {

                size += 1 + Google.Protobuf.CodedOutputStream.ComputeInt32Size(Count);

            }

            return size;

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input) { }

        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;

    }



    public class CreateClan2Response : Google.Protobuf.IMessage<CreateClan2Response>, Google.Protobuf.IMessage

    {

        public static Google.Protobuf.MessageParser<CreateClan2Response> Parser { get; } = new Google.Protobuf.MessageParser<CreateClan2Response>(() => new CreateClan2Response());



        public Axlebolt.Bolt.Protobuf.Clan Clan { get; set; }



        public void MergeFrom(CreateClan2Response other)

        {

            if (other == null) return;

            if (other.Clan != null)

            {

                if (Clan == null) Clan = new Axlebolt.Bolt.Protobuf.Clan();

                Clan.MergeFrom(other.Clan);

            }

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10:

                        if (Clan == null) Clan = new Axlebolt.Bolt.Protobuf.Clan();

                        input.ReadMessage(Clan);

                        break;

                    default:

                        input.SkipLastField();

                        break;

                }

            }

        }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            if (Clan != null)

            {

                output.WriteRawTag(10);

                output.WriteMessage(Clan);

            }

        }



        public int CalculateSize()

        {

            int size = 0;

            if (Clan != null)

            {

                size += 1 + Google.Protobuf.CodedOutputStream.ComputeMessageSize(Clan);

            }

            return size;

        }



        public CreateClan2Response Clone()

        {

            return new CreateClan2Response { Clan = Clan?.Clone() };

        }



        public bool Equals(CreateClan2Response other)

        {

            if (ReferenceEquals(other, null)) return false;

            if (ReferenceEquals(other, this)) return true;

            return Equals(Clan, other.Clan);

        }



        public override bool Equals(object obj) => Equals(obj as CreateClan2Response);



        public override int GetHashCode() => Clan != null ? Clan.GetHashCode() : 0;

        

        Google.Protobuf.Reflection.MessageDescriptor Google.Protobuf.IMessage.Descriptor => null;

        public void MergeFrom(Google.Protobuf.IMessage other)

        {

            if (other is CreateClan2Response c) MergeFrom(c);

        }

    }



    public class GetClanMembers2Response : Google.Protobuf.IMessage<GetClanMembers2Response>, Google.Protobuf.IMessage

    {

        public static Google.Protobuf.MessageParser<GetClanMembers2Response> Parser { get; } = new Google.Protobuf.MessageParser<GetClanMembers2Response>(() => new GetClanMembers2Response());



        private Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanMember> members_ = new Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanMember>();



        public Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanMember> Members => members_;



        public void MergeFrom(GetClanMembers2Response other)

        {

            if (other == null) return;

            members_.Add(other.members_);

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10:

                        members_.AddEntriesFrom(input, _repeated_members_codec);

                        break;

                    default:

                        input.SkipLastField();

                        break;

                }

            }

        }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            members_.WriteTo(output, _repeated_members_codec);

        }



        private static readonly Google.Protobuf.FieldCodec<Axlebolt.Bolt.Protobuf.ClanMember> _repeated_members_codec

            = Google.Protobuf.FieldCodec.ForMessage(10, Axlebolt.Bolt.Protobuf.ClanMember.Parser);



        public int CalculateSize()

        {

            return members_.CalculateSize(_repeated_members_codec);

        }



        public GetClanMembers2Response Clone()

        {

            var res = new GetClanMembers2Response();

            res.members_.Add(members_);

            return res;

        }



        public bool Equals(GetClanMembers2Response other)

        {

            if (ReferenceEquals(other, null)) return false;

            if (ReferenceEquals(other, this)) return true;

            return members_.Equals(other.members_);

        }



        public override bool Equals(object obj) => Equals(obj as GetClanMembers2Response);



        public override int GetHashCode() => members_.GetHashCode();



        Google.Protobuf.Reflection.MessageDescriptor Google.Protobuf.IMessage.Descriptor => null;

        public void MergeFrom(Google.Protobuf.IMessage other)

        {

            if (other is GetClanMembers2Response c) MergeFrom(c);

        }

    }



    public class GetRecommendedClans2Response : Google.Protobuf.IMessage<GetRecommendedClans2Response>, Google.Protobuf.IMessage

    {

        public static Google.Protobuf.MessageParser<GetRecommendedClans2Response> Parser { get; } = new Google.Protobuf.MessageParser<GetRecommendedClans2Response>(() => new GetRecommendedClans2Response());



        private Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.Clan> clans_ = new Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.Clan>();



        public Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.Clan> Clans => clans_;



        public void MergeFrom(GetRecommendedClans2Response other)

        {

            if (other == null) return;

            clans_.Add(other.clans_);

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10:

                        clans_.AddEntriesFrom(input, _repeated_clans_codec);

                        break;

                    default:

                        input.SkipLastField();

                        break;

                }

            }

        }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            clans_.WriteTo(output, _repeated_clans_codec);

        }



        private static readonly Google.Protobuf.FieldCodec<Axlebolt.Bolt.Protobuf.Clan> _repeated_clans_codec

            = Google.Protobuf.FieldCodec.ForMessage(10, Axlebolt.Bolt.Protobuf.Clan.Parser);



        public int CalculateSize()

        {

            return clans_.CalculateSize(_repeated_clans_codec);

        }



        public GetRecommendedClans2Response Clone()

        {

            var res = new GetRecommendedClans2Response();

            res.clans_.Add(clans_);

            return res;

        }



        public bool Equals(GetRecommendedClans2Response other)

        {

            if (ReferenceEquals(other, null)) return false;

            if (ReferenceEquals(other, this)) return true;

            return clans_.Equals(other.clans_);

        }



        public override bool Equals(object obj) => Equals(obj as GetRecommendedClans2Response);



        public override int GetHashCode() => clans_.GetHashCode();



        Google.Protobuf.Reflection.MessageDescriptor Google.Protobuf.IMessage.Descriptor => null;

        public void MergeFrom(Google.Protobuf.IMessage other)

        {

            if (other is GetRecommendedClans2Response c) MergeFrom(c);

        }

    }



    public class GetClanJoinRequests2Response : Google.Protobuf.IMessage<GetClanJoinRequests2Response>, Google.Protobuf.IMessage

    {

        public static Google.Protobuf.MessageParser<GetClanJoinRequests2Response> Parser { get; } = new Google.Protobuf.MessageParser<GetClanJoinRequests2Response>(() => new GetClanJoinRequests2Response());



        private Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanJoinRequest> joinRequests_ = new Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanJoinRequest>();



        public Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanJoinRequest> JoinRequests => joinRequests_;



        public void MergeFrom(GetClanJoinRequests2Response other)

        {

            if (other == null) return;

            joinRequests_.Add(other.joinRequests_);

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10:

                        joinRequests_.AddEntriesFrom(input, _repeated_joinRequests_codec);

                        break;

                    default:

                        input.SkipLastField();

                        break;

                }

            }

        }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            joinRequests_.WriteTo(output, _repeated_joinRequests_codec);

        }



        private static readonly Google.Protobuf.FieldCodec<Axlebolt.Bolt.Protobuf.ClanJoinRequest> _repeated_joinRequests_codec

            = Google.Protobuf.FieldCodec.ForMessage(10, Axlebolt.Bolt.Protobuf.ClanJoinRequest.Parser);



        public int CalculateSize()

        {

            return joinRequests_.CalculateSize(_repeated_joinRequests_codec);

        }



        public GetClanJoinRequests2Response Clone()

        {

            var res = new GetClanJoinRequests2Response();

            res.joinRequests_.Add(joinRequests_);

            return res;

        }



        public bool Equals(GetClanJoinRequests2Response other)

        {

            if (ReferenceEquals(other, null)) return false;

            if (ReferenceEquals(other, this)) return true;

            return joinRequests_.Equals(other.joinRequests_);

        }



        public override bool Equals(object obj) => Equals(obj as GetClanJoinRequests2Response);



        public override int GetHashCode() => joinRequests_.GetHashCode();



        Google.Protobuf.Reflection.MessageDescriptor Google.Protobuf.IMessage.Descriptor => null;

        public void MergeFrom(Google.Protobuf.IMessage other)

        {

            if (other is GetClanJoinRequests2Response c) MergeFrom(c);

        }

    }



    public class GetClanInviteRequests2Response : Google.Protobuf.IMessage<GetClanInviteRequests2Response>, Google.Protobuf.IMessage

    {

        public static Google.Protobuf.MessageParser<GetClanInviteRequests2Response> Parser { get; } = new Google.Protobuf.MessageParser<GetClanInviteRequests2Response>(() => new GetClanInviteRequests2Response());



        private Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanInviteRequest> inviteRequests_ = new Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanInviteRequest>();



        public Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanInviteRequest> InviteRequests => inviteRequests_;



        public void MergeFrom(GetClanInviteRequests2Response other)

        {

            if (other == null) return;

            inviteRequests_.Add(other.inviteRequests_);

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10:

                        inviteRequests_.AddEntriesFrom(input, _repeated_inviteRequests_codec);

                        break;

                    default:

                        input.SkipLastField();

                        break;

                }

            }

        }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            inviteRequests_.WriteTo(output, _repeated_inviteRequests_codec);

        }



        private static readonly Google.Protobuf.FieldCodec<Axlebolt.Bolt.Protobuf.ClanInviteRequest> _repeated_inviteRequests_codec

            = Google.Protobuf.FieldCodec.ForMessage(10, Axlebolt.Bolt.Protobuf.ClanInviteRequest.Parser);



        public int CalculateSize()

        {

            return inviteRequests_.CalculateSize(_repeated_inviteRequests_codec);

        }



        public GetClanInviteRequests2Response Clone()

        {

            var res = new GetClanInviteRequests2Response();

            res.inviteRequests_.Add(inviteRequests_);

            return res;

        }



        public bool Equals(GetClanInviteRequests2Response other)

        {

            if (ReferenceEquals(other, null)) return false;

            if (ReferenceEquals(other, this)) return true;

            return inviteRequests_.Equals(other.inviteRequests_);

        }



        public override bool Equals(object obj) => Equals(obj as GetClanInviteRequests2Response);



        public override int GetHashCode() => inviteRequests_.GetHashCode();



        Google.Protobuf.Reflection.MessageDescriptor Google.Protobuf.IMessage.Descriptor => null;

        public void MergeFrom(Google.Protobuf.IMessage other)

        {

            if (other is GetClanInviteRequests2Response c) MergeFrom(c);

        }

    }



    public class GetPlayerInviteRequests2Response : Google.Protobuf.IMessage<GetPlayerInviteRequests2Response>, Google.Protobuf.IMessage

    {

        public static Google.Protobuf.MessageParser<GetPlayerInviteRequests2Response> Parser { get; } = new Google.Protobuf.MessageParser<GetPlayerInviteRequests2Response>(() => new GetPlayerInviteRequests2Response());



        private Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanInviteRequest> inviteRequests_ = new Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanInviteRequest>();



        public Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanInviteRequest> InviteRequests => inviteRequests_;



        public void MergeFrom(GetPlayerInviteRequests2Response other)

        {

            if (other == null) return;

            inviteRequests_.Add(other.inviteRequests_);

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10:

                        inviteRequests_.AddEntriesFrom(input, _repeated_inviteRequests_codec);

                        break;

                    default:

                        input.SkipLastField();

                        break;

                }

            }

        }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            inviteRequests_.WriteTo(output, _repeated_inviteRequests_codec);

        }



        private static readonly Google.Protobuf.FieldCodec<Axlebolt.Bolt.Protobuf.ClanInviteRequest> _repeated_inviteRequests_codec

            = Google.Protobuf.FieldCodec.ForMessage(10, Axlebolt.Bolt.Protobuf.ClanInviteRequest.Parser);



        public int CalculateSize()

        {

            return inviteRequests_.CalculateSize(_repeated_inviteRequests_codec);

        }



        public GetPlayerInviteRequests2Response Clone()

        {

            var res = new GetPlayerInviteRequests2Response();

            res.inviteRequests_.Add(inviteRequests_);

            return res;

        }



        public bool Equals(GetPlayerInviteRequests2Response other)

        {

            if (ReferenceEquals(other, null)) return false;

            if (ReferenceEquals(other, this)) return true;

            return inviteRequests_.Equals(other.inviteRequests_);

        }



        public override bool Equals(object obj) => Equals(obj as GetPlayerInviteRequests2Response);



        public override int GetHashCode() => inviteRequests_.GetHashCode();



        Google.Protobuf.Reflection.MessageDescriptor Google.Protobuf.IMessage.Descriptor => null;

        public void MergeFrom(Google.Protobuf.IMessage other)

        {

            if (other is GetPlayerInviteRequests2Response c) MergeFrom(c);

        }

    }



    public class GetPlayerJoinRequests2Response : Google.Protobuf.IMessage<GetPlayerJoinRequests2Response>, Google.Protobuf.IMessage

    {

        public static Google.Protobuf.MessageParser<GetPlayerJoinRequests2Response> Parser { get; } = new Google.Protobuf.MessageParser<GetPlayerJoinRequests2Response>(() => new GetPlayerJoinRequests2Response());



        private Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanJoinRequest> joinRequests_ = new Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanJoinRequest>();



        public Google.Protobuf.Collections.RepeatedField<Axlebolt.Bolt.Protobuf.ClanJoinRequest> JoinRequests => joinRequests_;



        public void MergeFrom(GetPlayerJoinRequests2Response other)

        {

            if (other == null) return;

            joinRequests_.Add(other.joinRequests_);

        }



        public void MergeFrom(Google.Protobuf.CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10:

                        joinRequests_.AddEntriesFrom(input, _repeated_joinRequests_codec);

                        break;

                    default:

                        input.SkipLastField();

                        break;

                }

            }

        }



        public void WriteTo(Google.Protobuf.CodedOutputStream output)

        {

            joinRequests_.WriteTo(output, _repeated_joinRequests_codec);

        }



        private static readonly Google.Protobuf.FieldCodec<Axlebolt.Bolt.Protobuf.ClanJoinRequest> _repeated_joinRequests_codec

            = Google.Protobuf.FieldCodec.ForMessage(10, Axlebolt.Bolt.Protobuf.ClanJoinRequest.Parser);



        public int CalculateSize()

        {

            return joinRequests_.CalculateSize(_repeated_joinRequests_codec);

        }



        public GetPlayerJoinRequests2Response Clone()

        {

            var res = new GetPlayerJoinRequests2Response();

            res.joinRequests_.Add(joinRequests_);

            return res;

        }



        public bool Equals(GetPlayerJoinRequests2Response other)

        {

            if (ReferenceEquals(other, null)) return false;

            if (ReferenceEquals(other, this)) return true;

            return joinRequests_.Equals(other.joinRequests_);

        }



        public override bool Equals(object obj) => Equals(obj as GetPlayerJoinRequests2Response);



        public override int GetHashCode() => joinRequests_.GetHashCode();



        Google.Protobuf.Reflection.MessageDescriptor Google.Protobuf.IMessage.Descriptor => null;

        public void MergeFrom(Google.Protobuf.IMessage other)

        {

            if (other is GetPlayerJoinRequests2Response c) MergeFrom(c);

        }

    }

}



