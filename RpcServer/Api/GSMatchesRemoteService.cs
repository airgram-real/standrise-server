using System;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using StandRiseServer.MongoDB;
using MongoDB.Driver;
using MongoDB.Bson;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("GSMatchesRemoteService")]
    public class GSMatchesRemoteService : RpcClass
    {
        public GSMatchesRemoteService(UserService user) : base(user) { }

        public override async Task InvokeAsync(RpcRequest request)
        {
            switch (request.MethodName)
            {
                case "finishMatch":
                    await FinishMatch(request.Params.ToArray(), request.Id);
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

        private async Task FinishMatch(BinaryValue[] values, string guid)
        {
            try
            {
                var request = FinishMatchRequest.Parser.ParseFrom(values[0].One.ToByteArray());
                var random = new Random();
                
                foreach (var result in request.MatchResults)
                {
                    string playerId = result.PlayerId;
                    string mode = PlayerStatsManager.NormalizeRankedGameMode(result.GameMode);

                    if (result.IsWin)
                    {
                        PlayerStatsManager.AddWin(playerId, mode);
                    }
                    else
                    {
                        PlayerStatsManager.AddLoss(playerId, mode);
                    }

                    // Bug 9: award XP so the player's level progresses after every match.
                    // Wins yield more XP than losses so the level/xp display visibly
                    // moves on the post-match screen and the lobby header. AddExperience
                    // also handles level-up rollover internally.
                    float xpReward = result.IsWin ? 100f : 50f;
                    PlayerStatsManager.AddExperience(playerId, xpReward);

                    // Выдаем награды после матча: 50-150 голды и 100-300 серебра
                    int goldReward = random.Next(50, 151); // 50-150
                    int silverReward = random.Next(100, 301); // 100-300
                    
                    // Добавляем валюту к инвентарю игрока
                    // 102 = Gold, 101 = Silver
                    await AddCurrencyToPlayer(playerId, 102, goldReward);
                    await AddCurrencyToPlayer(playerId, 101, silverReward);
                    
                    Logger.Log($"[FinishMatch] Player {playerId} received rewards: {goldReward} gold, {silverReward} silver");
                }
                
                // Bug 9: notify every player in the match that it has ended,
                // so their client navigates to the post-match results screen.
                // We construct a minimal FinishedMatch payload (matchId + state +
                // start/finish dates); per-player stats are sent via the existing
                // stats persistence path. Without this broadcast the client stays
                // stuck on the gameplay HUD and never opens the results/drops/chat tab.
                try
                {
                    string matchId = System.Guid.NewGuid().ToString();
                    long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var finishedMatch = new FinishedMatch
                    {
                        MatchId = matchId,
                        FinishDate = now,
                        StartDate = now,
                    };
                    MatchHistoryBuilder.StampForClient(finishedMatch);
                    var evt = new OnMatchFinishedEvent { Match = finishedMatch };

                    foreach (var result in request.MatchResults)
                    {
                        SendEventToSession<MatchesRemoteEventListener>(result.PlayerId, "onMatchFinished", new object[] { evt });
                    }
                    Logger.Log($"[FinishMatch] Broadcast onMatchFinished to {request.MatchResults.Count} players for match {matchId}");
                }
                catch (System.Exception broadcastEx)
                {
                    Logger.Log($"[FinishMatch] onMatchFinished broadcast failed: {broadcastEx.Message}");
                }

                var response = new FinishMatchResponse();

                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task AddCurrencyToPlayer(string playerId, int currencyId, int amount)
        {
            try
            {
                var database = BoltGameDatabaseProvider.Instance.GetDatabase;
                var collection = database.GetCollection<BsonDocument>("playerInventory");
                
                var filter = Builders<BsonDocument>.Filter.Eq("playerId", playerId);
                var update = Builders<BsonDocument>.Update.Inc($"currencies.{currencyId}.value", amount);
                
                await collection.UpdateOneAsync(filter, update);
            }
            catch (System.Exception ex)
            {
                Logger.Log($"[AddCurrencyToPlayer] Error adding currency to player {playerId}: {ex.Message}");
            }
        }

        private void SendResponse(string guid, IMessage response)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { One = response.ToByteString() }
                }
            });
        }

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in GSMatchesRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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
