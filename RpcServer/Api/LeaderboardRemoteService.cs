using System;
using System.Collections.Generic;
using System.Linq;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using MongoDB.Bson;
using MongoDB.Driver;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using Axlebolt.Bolt.Protobuf;

namespace StandRiseServer.RpcServer.Api
{
    public sealed class ClientLeaderboardEntry : IMessage<ClientLeaderboardEntry>
    {
        public int Rank { get; set; } // Tag 1
        public int Points { get; set; } // Tag 2
        public int Position { get; set; } // Tag 3
        public int Value4 { get; set; } // Tag 4
        public bool IsSelf { get; set; } // Tag 5
        public Player Player { get; set; } // Tag 6

        public static MessageParser<ClientLeaderboardEntry> Parser { get; } = new MessageParser<ClientLeaderboardEntry>(() => new ClientLeaderboardEntry());
        public MessageDescriptor Descriptor => null;

        public void WriteTo(CodedOutputStream output)
        {
            if (Rank != 0) { output.WriteRawTag(8); output.WriteInt32(Rank); }
            if (Points != 0) { output.WriteRawTag(16); output.WriteInt32(Points); }
            if (Position != 0) { output.WriteRawTag(24); output.WriteInt32(Position); }
            if (Value4 != 0) { output.WriteRawTag(32); output.WriteInt32(Value4); }
            if (IsSelf) { output.WriteRawTag(40); output.WriteBool(IsSelf); }
            if (Player != null) { output.WriteRawTag(50); output.WriteMessage(Player); }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (Rank != 0) size += 1 + CodedOutputStream.ComputeInt32Size(Rank);
            if (Points != 0) size += 1 + CodedOutputStream.ComputeInt32Size(Points);
            if (Position != 0) size += 1 + CodedOutputStream.ComputeInt32Size(Position);
            if (Value4 != 0) size += 1 + CodedOutputStream.ComputeInt32Size(Value4);
            if (IsSelf) size += 2;
            if (Player != null) size += 1 + CodedOutputStream.ComputeMessageSize(Player);
            return size;
        }

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 8: Rank = input.ReadInt32(); break;
                    case 16: Points = input.ReadInt32(); break;
                    case 24: Position = input.ReadInt32(); break;
                    case 32: Value4 = input.ReadInt32(); break;
                    case 40: IsSelf = input.ReadBool(); break;
                    case 50: if (Player == null) Player = new Player(); input.ReadMessage(Player); break;
                    default: input.SkipLastField(); break;
                }
            }
        }

        public void MergeFrom(ClientLeaderboardEntry other)
        {
            if (other == null) return;
            if (other.Rank != 0) Rank = other.Rank;
            if (other.Points != 0) Points = other.Points;
            if (other.Position != 0) Position = other.Position;
            if (other.Value4 != 0) Value4 = other.Value4;
            if (other.IsSelf) IsSelf = other.IsSelf;
            if (other.Player != null)
            {
                if (Player == null) Player = new Player();
                Player.MergeFrom(other.Player);
            }
        }

        public ClientLeaderboardEntry Clone()
        {
            return new ClientLeaderboardEntry
            {
                Rank = Rank,
                Points = Points,
                Position = Position,
                Value4 = Value4,
                IsSelf = IsSelf,
                Player = Player?.Clone()
            };
        }

        public bool Equals(ClientLeaderboardEntry other)
        {
            if (other == null) return false;
            return Rank == other.Rank && Points == other.Points && Position == other.Position && Value4 == other.Value4 && IsSelf == other.IsSelf && Equals(Player, other.Player);
        }
    }

    public sealed class ClientPlayerRankResponse : IMessage<ClientPlayerRankResponse>
    {
        public ClientLeaderboardEntry Entry { get; set; } // Tag 1

        public static MessageParser<ClientPlayerRankResponse> Parser { get; } = new MessageParser<ClientPlayerRankResponse>(() => new ClientPlayerRankResponse());
        public MessageDescriptor Descriptor => null;

        public void WriteTo(CodedOutputStream output)
        {
            if (Entry != null) { output.WriteRawTag(10); output.WriteMessage(Entry); }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (Entry != null) size += 1 + CodedOutputStream.ComputeMessageSize(Entry);
            return size;
        }

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10)
                {
                    if (Entry == null) Entry = new ClientLeaderboardEntry();
                    input.ReadMessage(Entry);
                }
                else
                {
                    input.SkipLastField();
                }
            }
        }

        public void MergeFrom(ClientPlayerRankResponse other)
        {
            if (other == null) return;
            if (other.Entry != null)
            {
                if (Entry == null) Entry = new ClientLeaderboardEntry();
                Entry.MergeFrom(other.Entry);
            }
        }

        public ClientPlayerRankResponse Clone() => new ClientPlayerRankResponse { Entry = Entry?.Clone() };
        public bool Equals(ClientPlayerRankResponse other) => other != null && Equals(Entry, other.Entry);
    }

    public sealed class ClientPlayerLeaderboardResponse : IMessage<ClientPlayerLeaderboardResponse>
    {
        public Google.Protobuf.Collections.RepeatedField<ClientLeaderboardEntry> Entries { get; } = new Google.Protobuf.Collections.RepeatedField<ClientLeaderboardEntry>(); // Tag 1

        public static MessageParser<ClientPlayerLeaderboardResponse> Parser { get; } = new MessageParser<ClientPlayerLeaderboardResponse>(() => new ClientPlayerLeaderboardResponse());
        public MessageDescriptor Descriptor => null;

        private static readonly FieldCodec<ClientLeaderboardEntry> _entriesCodec = FieldCodec.ForMessage(10, ClientLeaderboardEntry.Parser);

        public void WriteTo(CodedOutputStream output)
        {
            Entries.WriteTo(output, _entriesCodec);
        }

        public int CalculateSize() => Entries.CalculateSize(_entriesCodec);

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10) Entries.AddEntriesFrom(input, _entriesCodec);
                else input.SkipLastField();
            }
        }

        public void MergeFrom(ClientPlayerLeaderboardResponse other)
        {
            if (other == null) return;
            Entries.Add(other.Entries);
        }

        public ClientPlayerLeaderboardResponse Clone()
        {
            var res = new ClientPlayerLeaderboardResponse();
            res.Entries.Add(Entries);
            return res;
        }

        public bool Equals(ClientPlayerLeaderboardResponse other) => other != null && Entries.Equals(other.Entries);
    }

    public class LeaderboardRemoteService : RpcClass
    {
        public LeaderboardRemoteService(UserService user) : base(user)
        {
        }

        private static List<PlayerStatsDocument> GetAllStatsDocuments()
        {
            try
            {
                var database = BoltGameDatabaseProvider.Instance.GetDatabase;
                var collection = database.GetCollection<PlayerStatsDocument>("stats");
                return collection.Find(new BsonDocument()).ToList();
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[Leaderboard] Error fetching stats documents: {ex.Message}");
                return new List<PlayerStatsDocument>();
            }
        }

        private static double ReadStatValue(PlayerStatsDocument stats, string statKey, string seasonId)
        {
            if (!string.IsNullOrWhiteSpace(seasonId))
            {
                var safeSeasonId = seasonId.Trim();
                if (stats.seasonStats != null && stats.seasonStats.TryGetValue(safeSeasonId, out var seasonValue) && seasonValue.IsBsonDocument)
                {
                    var seasonDocument = seasonValue.AsBsonDocument;
                    if (seasonDocument.TryGetValue(statKey, out var seasonStatValue))
                    {
                        return ToDouble(seasonStatValue);
                    }
                }
            }

            if (stats.stats != null && stats.stats.TryGetValue(statKey, out var statValue))
            {
                return ToDouble(statValue);
            }

            if (stats.seasonStats != null && stats.seasonStats.TryGetValue(statKey, out var seasonRootValue))
            {
                return ToDouble(seasonRootValue);
            }

            return 0;
        }

        private static double ToDouble(global::MongoDB.Bson.BsonValue value)
        {
            if (value.IsInt32) return value.AsInt32;
            if (value.IsInt64) return value.AsInt64;
            if (value.IsDouble) return value.AsDouble;
            if (value.IsDecimal128) return (double)value.AsDecimal128;
            if (value.IsString && double.TryParse(value.AsString, out var parsed))
            {
                return parsed;
            }
            return 0;
        }

        protected void GetLeaderboard(string guid)
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

        private void GetPlayerRank(BinaryValue[] values, string guid)
        {
            try
            {
                string currentPlayerId = PlayerId;
                if (string.IsNullOrEmpty(currentPlayerId))
                {
                    SendError(guid, 401);
                    return;
                }

                string leaderboardCode = "ranked_current_mmr";
                string targetPlayerId = currentPlayerId;

                if (values != null && values.Length > 0 && !values[0].IsNull)
                {
                    try
                    {
                        using (var input = new CodedInputStream(values[0].One.ToByteArray()))
                        {
                            uint tag;
                            while ((tag = input.ReadTag()) != 0)
                            {
                                if (tag == 10) leaderboardCode = input.ReadString()?.Trim() ?? "ranked_current_mmr";
                                else if (tag == 18) targetPlayerId = input.ReadString()?.Trim() ?? "";
                                else input.SkipLastField();
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Console.WriteLine($"[Leaderboard] getPlayerRank parse error: {ex.Message}");
                    }
                }

                if (string.IsNullOrEmpty(targetPlayerId))
                {
                    targetPlayerId = currentPlayerId;
                }

                var allDocs = GetAllStatsDocuments();
                var ordered = allDocs
                    .Select(x => new { PlayerId = x.playerId, Value = ReadStatValue(x, leaderboardCode, null) })
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.PlayerId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int rank = 0;
                double val = 0;
                int total = ordered.Count;

                double previousValue = double.NaN;
                int currentRank = 0;
                for (int i = 0; i < ordered.Count; i++)
                {
                    var entry = ordered[i];
                    if (i == 0 || Math.Abs(entry.Value - previousValue) > 0.000001)
                    {
                        currentRank = i + 1;
                        previousValue = entry.Value;
                    }

                    if (entry.PlayerId.Equals(targetPlayerId, StringComparison.OrdinalIgnoreCase))
                    {
                        rank = currentRank;
                        val = entry.Value;
                        break;
                    }
                }

                if (rank == 0)
                {
                    rank = total + 1;
                    val = 0;
                }

                var playerDoc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(global::MongoDB.Bson.ObjectId.Parse(targetPlayerId));
                var playerProto = new Player
                {
                    Id = targetPlayerId,
                    Uid = playerDoc?.uid ?? "",
                    Name = playerDoc?.name ?? "",
                    AvatarId = playerDoc?.avatarId ?? ""
                };

                var entryProto = new ClientLeaderboardEntry
                {
                    Rank = rank,
                    Points = (int)val,
                    Position = rank,
                    IsSelf = targetPlayerId.Equals(currentPlayerId, StringComparison.OrdinalIgnoreCase),
                    Player = playerProto
                };

                var response = new ClientPlayerRankResponse { Entry = entryProto };
                
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
            catch (System.Exception ex)
            {
                Console.WriteLine($"[Leaderboard] getPlayerRank error: {ex.Message}\n{ex.StackTrace}");
                SendError(guid, 500);
            }
        }

        private void GetPlayerLeaderboard(BinaryValue[] values, string guid)
        {
            try
            {
                string currentPlayerId = PlayerId;
                if (string.IsNullOrEmpty(currentPlayerId))
                {
                    SendError(guid, 401);
                    return;
                }

                string leaderboardCode = "ranked_current_mmr";
                int page = 0;
                int size = 20;

                if (values != null && values.Length > 0 && !values[0].IsNull)
                {
                    try
                    {
                        using (var input = new CodedInputStream(values[0].One.ToByteArray()))
                        {
                            uint tag;
                            while ((tag = input.ReadTag()) != 0)
                            {
                                if (tag == 10)
                                {
                                    leaderboardCode = input.ReadString()?.Trim() ?? "ranked_current_mmr";
                                }
                                else if (tag == 18)
                                {
                                    byte[] pageInfoBytes = input.ReadBytes().ToByteArray();
                                    using (var subInput = new CodedInputStream(pageInfoBytes))
                                    {
                                        uint subtag;
                                        while ((subtag = subInput.ReadTag()) != 0)
                                        {
                                            if (subtag == 8) page = subInput.ReadInt32();
                                            else if (subtag == 16) size = subInput.ReadInt32();
                                            else subInput.SkipLastField();
                                        }
                                    }
                                }
                                else
                                {
                                    input.SkipLastField();
                                }
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Console.WriteLine($"[Leaderboard] getPlayerLeaderBoard parse error: {ex.Message}");
                    }
                }

                var allDocs = GetAllStatsDocuments();
                var ordered = allDocs
                    .Select(x => new { PlayerId = x.playerId, Value = ReadStatValue(x, leaderboardCode, null) })
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.PlayerId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var pageEntries = ordered.Skip(page * size).Take(size).ToList();
                var response = new ClientPlayerLeaderboardResponse();

                var playerIds = pageEntries.Select(e => e.PlayerId).ToList();
                var playersDict = BoltMainDatabaseProvider.Instance.GetPlayerDocumentsByIds(playerIds);

                double previousValue = double.NaN;
                int currentRank = 0;
                for (int i = 0; i < ordered.Count; i++)
                {
                    var entry = ordered[i];
                    if (i == 0 || Math.Abs(entry.Value - previousValue) > 0.000001)
                    {
                        currentRank = i + 1;
                        previousValue = entry.Value;
                    }

                    if (playerIds.Contains(entry.PlayerId))
                    {
                        playersDict.TryGetValue(entry.PlayerId, out var playerDoc);
                        var playerProto = new Player
                        {
                            Id = entry.PlayerId,
                            Uid = playerDoc?.uid ?? "",
                            Name = playerDoc?.name ?? "",
                            AvatarId = playerDoc?.avatarId ?? ""
                        };

                        var entryProto = new ClientLeaderboardEntry
                        {
                            Rank = currentRank,
                            Points = (int)entry.Value,
                            Position = currentRank,
                            IsSelf = entry.PlayerId.Equals(currentPlayerId, StringComparison.OrdinalIgnoreCase),
                            Player = playerProto
                        };

                        response.Entries.Add(entryProto);
                    }
                }

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
            catch (System.Exception ex)
            {
                Console.WriteLine($"[Leaderboard] getPlayerLeaderboard error: {ex.Message}\n{ex.StackTrace}");
                SendError(guid, 500);
            }
        }

        private void GetClanRank(BinaryValue[] values, string guid)
        {
            var response = new ClientPlayerRankResponse();
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { IsNull = false, One = response.ToByteString() }
                }
            });
        }

        private void GetClanLeaderboard(BinaryValue[] values, string guid)
        {
            var response = new ClientPlayerLeaderboardResponse();
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { IsNull = false, One = response.ToByteString() }
                }
            });
        }

        public override void Invoke(RpcRequest request)
        {
            try
            {
                string methodName = request.MethodName;

                if (methodName.Equals("getLeaderboard", StringComparison.OrdinalIgnoreCase))
                {
                    GetLeaderboard(request.Id);
                }
                else if (methodName.Equals("getPlayerRank", StringComparison.OrdinalIgnoreCase))
                {
                    GetPlayerRank(request.Params.ToArray(), request.Id);
                }
                else if (methodName.Equals("getPlayerLeaderBoard", StringComparison.OrdinalIgnoreCase))
                {
                    GetPlayerLeaderboard(request.Params.ToArray(), request.Id);
                }
                else if (methodName.Equals("getClanRank", StringComparison.OrdinalIgnoreCase))
                {
                    GetClanRank(request.Params.ToArray(), request.Id);
                }
                else if (methodName.Equals("getClanLeaderBoard", StringComparison.OrdinalIgnoreCase))
                {
                    GetClanLeaderboard(request.Params.ToArray(), request.Id);
                }
                else
                {
                    MethodNotFound(request);
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[Leaderboard] Critical error in Invoke ({request.MethodName}): {ex.Message}\n{ex.StackTrace}");
                SendError(request.Id, 500);
            }
        }
    }
}
