using System;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using StandRiseServer.MongoDB;
using MongoDB.Bson;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("GameServerPlayerRemoteService")]
    public class GameServerPlayerRemoteService : RpcClass
    {
        public GameServerPlayerRemoteService(UserService user) : base(user) { }

        public override async Task InvokeAsync(RpcRequest request)
        {
            switch (request.MethodName)
            {
                case "setPhotonGame":
                    await SetPhotonGame(request.Params.ToArray(), request.Id);
                    break;
                default:
                    MethodNotFound(request);
                    break;
            }
        }

        public override void Invoke(RpcRequest request)
        {
            _ = InvokeAsync(request);
        }

        private async Task SetPhotonGame(BinaryValue[] values, string guid)
        {
            try
            {
                string playerId = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                
                // Decode PhotonGame from BinaryValue parameter
                Axlebolt.Bolt.Protobuf.PhotonGame protoPhotonGame = null;
                if (!values[1].IsNull)
                {
                    protoPhotonGame = Axlebolt.Bolt.Protobuf.PhotonGame.Parser.ParseFrom(values[1].One.ToByteArray());
                }

                if (StaticClasses.PlayersStatus.TryGetValue(playerId, out var playerStatus))
                {
                    if (playerStatus.playInGame != null)
                    {
                        if (protoPhotonGame != null)
                        {
                            string rid = protoPhotonGame.RoomId ?? "";
                            bool rankedRoom = rid.StartsWith("RankedDefuse_", StringComparison.OrdinalIgnoreCase)
                                || rid.StartsWith("Ranked2v2_", StringComparison.OrdinalIgnoreCase);
                            bool liveLobby = false;
                            if (rankedRoom)
                            {
                                foreach (var kv in StaticClasses.Lobbies)
                                {
                                    string lobbyRoom = kv.Value?.PhotonGame?.roomId ?? "";
                                    if (string.Equals(lobbyRoom, rid, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(kv.Key, rid, StringComparison.OrdinalIgnoreCase))
                                    {
                                        liveLobby = true;
                                        break;
                                    }
                                }
                            }
                            if (rankedRoom && !liveLobby)
                            {
                                playerStatus.playInGame.photonGame = null;
                                playerStatus.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
                            }
                            else
                            {
                                playerStatus.playInGame.photonGame = new PhotonGame
                                {
                                    region = protoPhotonGame.Region,
                                    roomId = protoPhotonGame.RoomId,
                                    appVersion = protoPhotonGame.AppVersion
                                };
                                playerStatus.onlineStatus = PlayerStatus.OnlineStatus.StateBusy;
                            }
                        }
                        else
                        {
                            playerStatus.playInGame.photonGame = null;
                            playerStatus.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
                        }

                        // Persist to DB and update memory
                        BoltMainDatabaseProvider.Instance.SetPlayerStatus(ObjectId.Parse(playerId), playerStatus);
                        Console.WriteLine($"[GS] Updated Photon Game for Player {playerId}: RoomId={protoPhotonGame?.RoomId ?? "NULL"}");
                    }
                }
                
                SendResponse(guid);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private void SendResponse(string guid)
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

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in GameServerPlayerRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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
    }
}
