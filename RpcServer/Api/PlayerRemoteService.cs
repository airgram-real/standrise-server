using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.MongoDB.Main;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StandRiseServer.RpcServer.Api
{
    public class PlayerRemoteService : RpcClass
    {
        public PlayerRemoteService(UserService user) : base(user)
        { }
        protected void SetPlayerAvatar(BinaryValue[] values, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                BoltGameDatabaseProvider boltGame = BoltGameDatabaseProvider.Instance;
                FromByteMethod from = new FromByteMethod(typeof(byte[]));
                byte[] Avatarforset = (byte[])from.FromBytes(values[0]);
                int[] rt = new int[Avatarforset.Length];
                rt = Avatarforset.Select(i => (int)i).ToArray();
                string avatarId = boltGame.FindOrCreateAvatar(rt);
                boltMain.SetPlayerAvatar(ObjectId.Parse(Id), avatarId);
                _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse{ Id = guid, Return = new ToByteMethod(typeof(string)).ToBytes(avatarId) } });
                return;
            }
            _user.SendResponce(new ResponseMessage
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
            });
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

        protected void GetCurrentPlayer(BinaryValue[] values, string guid, string methodName)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                PlayerDocument playerDocument = boltMain.GetPlayerDocument(ObjectId.Parse(Id));

                var protoPlayer = new Axlebolt.Bolt.Protobuf.Player
                {
                    Id = playerDocument._id.ToString(),
                    Uid = playerDocument.uid,
                    Name = playerDocument.name,
                    AvatarId = playerDocument.avatarId,
                    TimeInGame = playerDocument.timeInGame,
                    RegistrationDate = playerDocument.createDate.MillisecondsSinceEpoch,
                    PlayerStatus = StaticClasses.PlayersStatus.ContainsKey(Id) 
                        ? StaticClasses.PlayersStatus[Id].GetPlayerStatusProto() 
                        : new PlayerStatus().GetPlayerStatusProto(),
                    TesterRole = playerDocument.testerRole ?? "",
                };

                var attributes = new Axlebolt.Bolt.Protobuf.Attributes();
                var clanDoc = boltMain.GetPlayerClanDocument(playerDocument);
                if (clanDoc != null)
                {
                    // Use the same snake_case attribute keys as FriendHelper.GetClanAttributes
                    // so the client renders clan info identically whether it is looking at
                    // the current player or someone else. Previously only PascalCase keys
                    // were emitted here, which caused the ClanInfoView to be blank on the
                    // own profile even when a clan was joined.
                    string clanId = clanDoc._id.ToString();
                    string clanName = clanDoc.name ?? string.Empty;
                    string clanTag = clanDoc.tag ?? string.Empty;
                    string clanAvatarId = StandRiseServer.MongoDB.Main.ClanDocument.NormalizeAvatarId(clanDoc.avatarId);
                    int roleId = clanDoc.GetMemberRoleId(playerDocument._id.ToString());

                    attributes.Map.Add("clan_id",        new Axlebolt.Bolt.Protobuf.Attribute { Type = Axlebolt.Bolt.Protobuf.PropertyType.String, String = clanId });
                    attributes.Map.Add("clan_name",      new Axlebolt.Bolt.Protobuf.Attribute { Type = Axlebolt.Bolt.Protobuf.PropertyType.String, String = clanName });
                    attributes.Map.Add("clan_tag",       new Axlebolt.Bolt.Protobuf.Attribute { Type = Axlebolt.Bolt.Protobuf.PropertyType.String, String = clanTag });
                    attributes.Map.Add("clan_avatar_id", new Axlebolt.Bolt.Protobuf.Attribute { Type = Axlebolt.Bolt.Protobuf.PropertyType.String, String = clanAvatarId });
                    attributes.Map.Add("clan_role_id",   new Axlebolt.Bolt.Protobuf.Attribute { Type = Axlebolt.Bolt.Protobuf.PropertyType.Int,    Int = roleId });

                    // Keep the historical PascalCase keys around for any client code that
                    // may still look them up by the old names.
                    attributes.Map.Add("ClanId", new Axlebolt.Bolt.Protobuf.Attribute { Type = Axlebolt.Bolt.Protobuf.PropertyType.String, String = clanId });
                    attributes.Map.Add("Tag",    new Axlebolt.Bolt.Protobuf.Attribute { Type = Axlebolt.Bolt.Protobuf.PropertyType.String, String = clanTag });
                    attributes.Map.Add("RoleId", new Axlebolt.Bolt.Protobuf.Attribute { Type = Axlebolt.Bolt.Protobuf.PropertyType.Int,    Int = roleId });
                }
                protoPlayer.Attributes = attributes;

                if (playerDocument.isBanned)
                {
                    protoPlayer.Bans.Add(new Axlebolt.Bolt.Protobuf.PlayerBan
                    {
                        BanCode = int.TryParse(playerDocument.banCode, out int code) ? code : 0,
                        Message = playerDocument.banReason ?? "Banned"
                    });
                }

                if (methodName.Equals("getPlayer2", System.StringComparison.OrdinalIgnoreCase) || methodName.Equals("getPlayerById3", System.StringComparison.OrdinalIgnoreCase))
                {
                    byte[] payload = protoPlayer.ToByteArray();
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

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(Axlebolt.Bolt.Protobuf.Player)).ToBytes(protoPlayer)
                    }
                });
                return;
            }
            _user.SendResponce(new ResponseMessage
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
            });
        }

        protected void SetPlayerName(BinaryValue[] values, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                FromByteMethod fromByte = new FromByteMethod(typeof(string));
                string name = (string)fromByte.FromBytes(values[0]);
                boltMain.SetPlayerName(ObjectId.Parse(Id), name);
                
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new BinaryValue
                        {
                            IsNull = true
                        }
                    }
                });
                return;
            }
            _user.SendResponce(new ResponseMessage
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
            });
        }
        protected void SetAwayStatus(BinaryValue[] values, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                PlayerStatus status = StaticClasses.PlayersStatus.GetOrAdd(Id, _ => new PlayerStatus());
                status.onlineStatus = PlayerStatus.OnlineStatus.StateAway;
                boltMain.SetPlayerStatus(ObjectId.Parse(Id), status);
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new BinaryValue
                        {
                            IsNull = true
                        }
                    }
                });
                return;
            }
            _user.SendResponce(new ResponseMessage
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
            });
        }
        protected void SetOnlineStatus(BinaryValue[] values, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                PlayerStatus status = StaticClasses.PlayersStatus.GetOrAdd(Id, _ => new PlayerStatus());
                status.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
                boltMain.SetPlayerStatus(ObjectId.Parse(Id), status);
                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new BinaryValue
                        {
                            IsNull = true
                        }
                    }
                });
                return;
            }
            _user.SendResponce(new ResponseMessage
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
            });
        }
        protected void BanMe(BinaryValue[] values, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                FromByteMethod fromByte1 = new FromByteMethod(typeof(string));
                FromByteMethod fromByte2 = new FromByteMethod(typeof(int));
                string banReason = (string)fromByte1.FromBytes(values[0]);
                int banCode = (int)fromByte2.FromBytes(values[1]);
                boltMain.BanPlayer(ObjectId.Parse(Id), banReason, banCode);
                Logger.LogWarn($"[BanMe] Player {Id} banned in-game. Code: {banCode}, Reason: {banReason}");

                var playerDoc = boltMain.GetPlayerDocument(ObjectId.Parse(Id));
                StaticClasses.KickWithBan(Id, banReason, banCode, playerDoc?.uid ?? Id);
                return;
            }
            _user.SendResponce(new ResponseMessage
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
            });
        }
        protected void SetPlayerFirebaseToken(BinaryValue[] values, string guid)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { IsNull = true }
                }
            });
        }

        private void GetPlayerStatus(BinaryValue[] values, string guid)
        {
            try
            {
                string targetId = null;
                if (values != null && values.Length > 0 && !values[0].IsNull)
                {
                    targetId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                }

                if (string.IsNullOrWhiteSpace(targetId))
                {
                    StaticClasses.Users.TryGetValue(_user.TcpClient, out targetId);
                }

                if (string.IsNullOrWhiteSpace(targetId))
                {
                    SendError(guid, 401);
                    return;
                }

                BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                PlayerDocument playerDocument = boltMain.GetPlayerDocument(global::MongoDB.Bson.ObjectId.Parse(targetId));
                if (playerDocument == null)
                {
                    SendError(guid, 404);
                    return;
                }

                Axlebolt.Bolt.Protobuf.PlayerStatus protoStatus = StaticClasses.PlayersStatus.ContainsKey(targetId)
                    ? StaticClasses.PlayersStatus[targetId].GetPlayerStatusProto()
                    : new PlayerStatus().GetPlayerStatusProto();

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new ToByteMethod(typeof(Axlebolt.Bolt.Protobuf.PlayerStatus)).ToBytes(protoStatus)
                    }
                });
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private void SetPlayerSettings(BinaryValue[] values, string guid)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue
                    {
                        IsNull = false,
                        One = ByteString.CopyFrom(new byte[] { 10, 0 })
                    }
                }
            });
        }

        protected void GetPlayerSettings(BinaryValue[] values, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                ByteString payload = ByteString.Empty;

                if (values != null && values.Length == 1)
                {
                    payload = ByteString.CopyFrom(new byte[] { 10, 0 });
                }

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new BinaryValue
                        {
                            IsNull = false,
                            One = payload
                        }
                    }
                });
                return;
            }
            
            _user.SendResponce(new ResponseMessage
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
            });
        }
        public override void Invoke(RpcRequest request)
        {
            string methodName = request.MethodName.ToLowerInvariant();
            switch (methodName)
            {
                case "setplayeravatar":
                case "setplayeravatar2":
                    SetPlayerAvatar(request.Params.ToArray(), request.Id);
                    break;
                case "getplayer":
                case "getplayer2":
                case "getcurrentplayer":
                case "getplayerbyid3":
                    GetCurrentPlayer(request.Params.ToArray(), request.Id, request.MethodName);
                    break;
                case "getplayerstatus":
                    GetPlayerStatus(request.Params.ToArray(), request.Id);
                    break;
                case "setplayersettings":
                case "setplayersettings2":
                    SetPlayerSettings(request.Params.ToArray(), request.Id);
                    break;
                case "setplayername":
                case "setplayername2":
                    SetPlayerName(request.Params.ToArray(), request.Id);
                    break;
                case "setawaystatus":
                case "setawaystatus2":
                    SetAwayStatus(request.Params.ToArray(), request.Id);
                    break;
                case "setonlinestatus":
                case "setonlinestatus2":
                    SetOnlineStatus(request.Params.ToArray(), request.Id);
                    break;
                case "banme":
                    BanMe(request.Params.ToArray(), request.Id);
                    break;
                case "setplayerfirebasetoken":
                case "setplayerfirebasetoken2":
                    SetPlayerFirebaseToken(request.Params.ToArray(), request.Id);
                    break;
                case "getplayersettings":
                case "getplayersettings2":
                    GetPlayerSettings(request.Params.ToArray(), request.Id);
                    break;
                default:
                    MethodNotFound(request);
                    break;
            }
        }
    }
    public static class PlayerServiceHelper
    {
        public static Axlebolt.Bolt.Protobuf.PlayerStatus GetPlayerStatusProto(this PlayerStatus bsonElements)
        {
            return PlayerStatus.GetByDocument(bsonElements);
        }
    }
}
