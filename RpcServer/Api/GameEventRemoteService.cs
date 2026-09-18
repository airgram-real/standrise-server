using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.RpcSupport.Protobuf;
using Google.Protobuf;
using MongoDB.Bson;
using MongoDB.Driver;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Data;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.MongoDB.Main;

namespace StandRiseServer.RpcServer.Api
{
    [RpcService("GameEventRemoteService")]
    public class GameEventRemoteService : RpcClass
    {
        private const string DefaultEventId = "HOT_WINTER_PARTY_2023";
        // Клиентский бейдж "★N" у квеста - однозначный слот, максимум одна цифра. При 10 он
        // визуально обрезается и показывает "1" вместо "10" - поэтому потолок 9, не 10.
        private const int DefaultChallengeEventPoints = 9;
        // Реальный шаг уровня БП (см. HotWinterParty2023.json: level1=0, level2=10, level3=20...).
        private const int PointsPerLevel = 10;

        // Батл-пасс длится ровно 40 дней = 6 недель (5 полных + хвост в 5 дней).
        internal const int PassDurationDays = 40;
        internal const int PassWeeks = 6;
        private const long DayMs = 86_400_000L;

        // Кэш ивента/квестов — иначе каждый логин парсит JSON и тащит всю Mongo-коллекцию.
        private static readonly object EventCacheLock = new object();
        private static BoltGameEventDocument CachedCursedSoulsEvent;
        private static DateTime CachedCursedSoulsAt = DateTime.MinValue;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime at, IReadOnlyList<GameEventChallengeDocument> list)> ChallengesCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime, IReadOnlyList<GameEventChallengeDocument>)>(StringComparer.OrdinalIgnoreCase);

        private static readonly object LevelsProtoCacheLock = new object();
        private static GamePass CachedFreePassTemplate;
        private static GamePass CachedGoldPassTemplate;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime at, GetCurrentGameEventsResponse resp)> PlayerEventsCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime, GetCurrentGameEventsResponse)>(StringComparer.Ordinal);

        public GameEventRemoteService(UserService user) : base(user) { }

        /// <summary>
        /// Единое окно батл-пасса и спин-рулетки. Старт берём из админки (spin_admin),
        /// длину держим фиксированной — 40 дней. Один источник правды для обложки ивента,
        /// страницы спина и прогресса игрока: раньше обложка считала остаток по DateUntil,
        /// а страница — по DurationDays/CurrentDay (который был захардкожен в 1), и числа
        /// расходились. Все значения в unix-МИЛЛИСЕКУНДАХ — клиент 0.17 читает именно так.
        /// </summary>
        internal static (long startMs, long endMs, int durationDays, int currentDay) GetPassWindow()
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var win = BoltGameDatabaseProvider.Instance.GetSpinWindowState();

            long startMs = win.startMs > 0 ? win.startMs : nowMs - DayMs;
            long fullEndMs = startMs + PassDurationDays * DayMs;

            // Админка может закрыть сезон раньше 40 дней — её край уважаем,
            // но продлевать пасс за 40 дней не даём.
            long endMs = (win.endMs > startMs && win.endMs < fullEndMs) ? win.endMs : fullEndMs;

            // Окно выключено или уже прошло → таймер 0, а не отрицательный.
            if (!win.active || endMs < nowMs)
                endMs = Math.Max(startMs + 1_000L, nowMs);

            int durationDays = (int)Math.Max(1, (endMs - startMs + DayMs - 1) / DayMs);
            if (durationDays > PassDurationDays) durationDays = PassDurationDays;

            int currentDay = (int)((nowMs - startMs) / DayMs) + 1;
            if (currentDay < 1) currentDay = 1;
            if (currentDay > durationDays) currentDay = durationDays;

            return (startMs, endMs, durationDays, currentDay);
        }

        /// <summary>Диапазон дней недели N (1..6) внутри 40-дневного пасса.</summary>
        internal static (int from, int to) GetWeekDayRange(int week)
        {
            if (week < 1) week = 1;
            if (week > PassWeeks) week = PassWeeks;
            int from = (week - 1) * 7 + 1;
            int to = Math.Min(week * 7, PassDurationDays);
            if (to < from) to = from;
            return (from, to);
        }

        /// <summary>Номер недели из кода квеста: "..._w3_quest_1" → 3. Нет метки — неделя 1.</summary>
        internal static int ParseChallengeWeek(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return 1;
            string lower = code.ToLowerInvariant();
            for (int week = PassWeeks; week >= 1; week--)
            {
                if (lower.Contains("_w" + week + "_") || lower.Contains("week" + week))
                    return week;
            }
            return 1;
        }

        public override async Task InvokeAsync(RpcRequest request)
        {
            string methodName = request.MethodName?.ToLowerInvariant() ?? string.Empty;
            switch (methodName)
            {
                case "savechallenge2":
                case "savechallengedefinition2":
                case "savechallengedefinition3":
                    await SaveChallenge2(request.Params.ToArray(), request.Id);
                    break;
                case "getallchallenges":
                    await GetAllChallenges(request.Params.ToArray(), request.Id);
                    break;
                case "getcurrentgameevents":
                case "getcurrentgameevents2":
                    await GetCurrentGameEvents(request.Params.ToArray(), request.Id);
                    break;
                case "setchallengeprogress":
                case "processchallenge":
                    await SetChallengeProgress(request.Params.ToArray(), request.Id);
                    break;
                case "getplayercurrentgameevents":
                case "getcachedplayergameevents":
                    await GetPlayerCurrentGameEvents(request.Params.ToArray(), request.Id);
                    break;
                case "getplayercurrentgameevents2":
                case "getcachedplayergameevents2":
                    await GetPlayerCurrentGameEvents2(request.Params.ToArray(), request.Id);
                    break;
                case "progressgameevent":
                    await ProgressGameEvent(request.Params.ToArray(), request.Id);
                    break;
                case "getplayergameeventsprogresses":
                    await GetPlayerGameEventsProgresses(request.Params.ToArray(), request.Id);
                    break;
                case "getcurrentchallenges":
                    await GetCurrentChallenges(request.Params.ToArray(), request.Id);
                    break;
                case "buyeventlevel":
                    await BuyEventLevel(request.Params.ToArray(), request.Id);
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

        private async Task<PlayerGameEventProgressDocument> GetOrCreateProgress(string playerId, string eventId)
        {
            var safeEventId = string.IsNullOrWhiteSpace(eventId) ? DefaultEventId : eventId.Trim();
            var collection = GetProgressCollection();

            var existing = await collection.Find(x => x.PlayerId == playerId && x.EventId == safeEventId).FirstOrDefaultAsync();
            if (existing != null)
            {
                return existing;
            }

            var created = new PlayerGameEventProgressDocument
            {
                PlayerId = playerId,
                EventId = safeEventId,
                Points = 0,
                Levels = new BsonDocument
                {
                    { "free", 1 },
                    { "premium", 0 }
                },
                ChallengeProgress = new BsonDocument(),
                UpdateDate = BsonDateTime.Create(DateTime.UtcNow)
            };

            await collection.InsertOneAsync(created);
            return created;
        }

        // Используется, когда мы на чтении (BuildCurrentGameEventsResponse) обнаруживаем, что
        // сохранённый progress.Levels["free"] разъехался с уровнем, реально посчитанным из
        // Points (см. комментарий в BuildCurrentGameEventsResponse), и молча чиним это в базе,
        // чтобы рассинхрон не всплывал заново при каждом запросе.
        private async Task PersistProgressLevels(string playerId, string eventId, PlayerGameEventProgressDocument progress)
        {
            try
            {
                var progressCollection = GetProgressCollection();
                await progressCollection.ReplaceOneAsync(
                    Builders<PlayerGameEventProgressDocument>.Filter.Eq(x => x.Id, progress.Id),
                    progress,
                    new ReplaceOptions { IsUpsert = true });
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[PersistProgressLevels] Failed to persist free level fix for player {playerId}, event {eventId}: {ex.Message}");
            }
        }

        private async Task<IReadOnlyList<GameEventChallengeDocument>> GetChallenges(string gameEventId)
        {
            var safeEventId = string.IsNullOrWhiteSpace(gameEventId) ? DefaultEventId : gameEventId.Trim();
            if (ChallengesCache.TryGetValue(safeEventId, out var cached) && (DateTime.UtcNow - cached.at).TotalSeconds < 60)
                return cached.list;

            var collection = GetChallengesCollection();

            // Нормализуем: SPIN_ROULETTE / CURSED_* → одни квесты CURSED_SOULS.
            string lookupId = safeEventId;
            if (lookupId.IndexOf("CURSED", StringComparison.OrdinalIgnoreCase) >= 0
                || lookupId.IndexOf("SPIN_ROULETTE", StringComparison.OrdinalIgnoreCase) >= 0
                || lookupId.IndexOf("HALLOWEEN", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lookupId = CursedSoulsEventId;
            }

            var filter = Builders<GameEventChallengeDocument>.Filter.Or(
                Builders<GameEventChallengeDocument>.Filter.Eq(x => x.EventId, lookupId),
                Builders<GameEventChallengeDocument>.Filter.Eq(x => x.EventId, safeEventId),
                Builders<GameEventChallengeDocument>.Filter.Eq(x => x.EventId, CursedSoulsEventId),
                Builders<GameEventChallengeDocument>.Filter.Eq(x => x.EventId, "CURSED_SOULS_SPIN_ROULETTE")
            );
            var result = await collection.Find(filter).ToListAsync();

            var defaults = BuildDefaultChallenges(lookupId);

            // Набор квестов пасса детерминирован: 6 недель по 3 задания + 3 ежедневных.
            // Раньше дефолты только ДОбавлялись по новому code, а старые строки с кривыми
            // названиями ("Do action '{...}' 5 times") оставались в базе и лезли в список.
            // Теперь дефолт — источник правды: сохраняем его поверх по code, а лишние строки
            // этого ивента в выдачу не берём (из базы не удаляем — прогресс по ним не теряется).
            var byCode = result
                .Where(x => !string.IsNullOrWhiteSpace(x.Code))
                .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var finalDocs = new List<GameEventChallengeDocument>(defaults.Count);
            foreach (var challenge in defaults)
            {
                if (byCode.TryGetValue(challenge.Code, out var existing))
                {
                    // Сохраняем _id существующей записи, чтобы не потерять прогресс игроков.
                    challenge.Id = existing.Id;
                    challenge.CreateDate = existing.CreateDate;
                }

                challenge.UpdateDate = BsonDateTime.Create(DateTime.UtcNow);
                await collection.ReplaceOneAsync(
                    Builders<GameEventChallengeDocument>.Filter.Eq(x => x.Id, challenge.Id),
                    challenge,
                    new ReplaceOptions { IsUpsert = true });
                finalDocs.Add(challenge);
            }

            IReadOnlyList<GameEventChallengeDocument> finalList = finalDocs;
            ChallengesCache[safeEventId] = (DateTime.UtcNow, finalList);
            foreach (var key in ChallengesCache.Keys.ToList())
            {
                if (!string.Equals(key, safeEventId, StringComparison.OrdinalIgnoreCase))
                    ChallengesCache.TryRemove(key, out _);
            }
            return finalList;
        }

        // Действия, которые реально понимает игровой сервер при начислении прогресса.
        // Новых типов не выдумываем — иначе квест никогда не сдвинется.
        private static string BuildMissionAction(string gameMode, bool headshot)
        {
            string mode = string.IsNullOrWhiteSpace(gameMode) ? "Defuse" : gameMode.Trim();
            string hit = headshot
                ? "\"IsKilled\":{\"Value\":true},\"IsHeadShot\":{\"Value\":true}"
                : "\"IsKilled\":{\"Value\":true}";
            return "{\"GameMode\":\"" + mode + "\",\"Action\":{\"Trigger\":{\"Type\":0,\"Hit\":{" + hit + "}}}}";
        }

        /// <summary>
        /// Детерминированный набор квестов пасса: 6 недель по 3 задания (недели режутся
        /// по 7 дней внутри 40-дневного сезона) + 3 ежедневных. Формулировки одинаковые
        /// по стилю и на одном языке — раньше в списке мешались "Weekly Grinder",
        /// "Первая кровь" и автогенерённые "Do action '{...}' 5 times".
        /// </summary>
        private static List<GameEventChallengeDocument> BuildDefaultChallenges(string safeEventId)
        {
            string prefix = safeEventId.Equals(CursedSoulsEventId, StringComparison.OrdinalIgnoreCase)
                ? "cursedsouls" : "hotwinterparty2023";

            var defaults = new List<GameEventChallengeDocument>();

            for (int week = 1; week <= PassWeeks; week++)
            {
                int killTarget = 20 + 10 * (week - 1);
                int headshotTarget = 8 + 4 * (week - 1);
                int sweepTarget = 45 + 20 * (week - 1);

                string weekMode = week <= 2 ? "Defuse" : (week <= 4 ? "RankedDefuse" : "DeathMatch");
                defaults.Add(new GameEventChallengeDocument
                {
                    EventId = safeEventId,
                    Code = $"{prefix}_w{week}_quest_1",
                    Action = BuildMissionAction(weekMode, headshot: false),
                    Type = "W",
                    TargetPoints = killTarget,
                    EventPoints = DefaultChallengeEventPoints,
                    KeyItemDefinitionId = 0,
                    Title = $"Неделя {week}: перестрелка",
                    Description = "Убей {0} противников"
                });
                defaults.Add(new GameEventChallengeDocument
                {
                    EventId = safeEventId,
                    Code = $"{prefix}_w{week}_quest_2",
                    Action = BuildMissionAction(weekMode, headshot: true),
                    Type = "W",
                    TargetPoints = headshotTarget,
                    EventPoints = DefaultChallengeEventPoints,
                    KeyItemDefinitionId = 0,
                    Title = $"Неделя {week}: точный выстрел",
                    Description = "Сделай {0} убийств в голову"
                });
                defaults.Add(new GameEventChallengeDocument
                {
                    EventId = safeEventId,
                    Code = $"{prefix}_w{week}_quest_3",
                    Action = BuildMissionAction(weekMode, headshot: false),
                    Type = "W",
                    TargetPoints = sweepTarget,
                    EventPoints = DefaultChallengeEventPoints,
                    KeyItemDefinitionId = 0,
                    Title = $"Неделя {week}: зачистка",
                    Description = "Убей {0} противников"
                });
            }

            defaults.Add(new GameEventChallengeDocument
            {
                EventId = safeEventId,
                Code = $"{prefix}_d1_quest_1",
                Action = BuildMissionAction("Defuse", headshot: false),
                Type = "D",
                TargetPoints = 5,
                EventPoints = DefaultChallengeEventPoints,
                KeyItemDefinitionId = 0,
                Title = "Ежедневное: разминка",
                Description = "Убей {0} противников за день"
            });
            defaults.Add(new GameEventChallengeDocument
            {
                EventId = safeEventId,
                Code = $"{prefix}_d1_quest_2",
                Action = BuildMissionAction("Defuse", headshot: true),
                Type = "D",
                TargetPoints = 2,
                EventPoints = DefaultChallengeEventPoints,
                KeyItemDefinitionId = 0,
                Title = "Ежедневное: в голову",
                Description = "Сделай {0} убийств в голову за день"
            });
            defaults.Add(new GameEventChallengeDocument
            {
                EventId = safeEventId,
                Code = $"{prefix}_d1_quest_3",
                Action = BuildMissionAction("RankedDefuse", headshot: false),
                Type = "D",
                TargetPoints = 10,
                EventPoints = DefaultChallengeEventPoints,
                KeyItemDefinitionId = 0,
                Title = "Ежедневное: серия",
                Description = "Убей {0} противников за день"
            });

            return defaults;
        }

        private async Task SaveChallenge2(BinaryValue[] values, string guid)
        {
            try
            {
                if (!TryParseChallengeDefinition(values, out var code, out var action, out int targetPoints, out int eventPoints, out int keyItemDefinitionId, out string title, out string description, out string challengeId, out bool requestStyle))
                {
                    SendError(guid, 400);
                    return;
                }

                string eventId = "dragon_rise";
                var doc = new GameEventChallengeDocument
                {
                    Id = string.IsNullOrWhiteSpace(challengeId) 
                        ? BuildChallengeId(eventId, code, action, targetPoints)
                        : challengeId.Trim(),
                    EventId = eventId,
                    Code = code.Trim(),
                    Action = action.Trim(),
                    Type = code.Trim(),
                    TargetPoints = targetPoints,
                    EventPoints = eventPoints > 0 ? eventPoints : DefaultChallengeEventPoints,
                    KeyItemDefinitionId = keyItemDefinitionId,
                    Title = string.IsNullOrWhiteSpace(title) ? $"{code.Trim()} x{targetPoints}" : title.Trim(),
                    Description = string.IsNullOrWhiteSpace(description) ? $"Do action '{action.Trim()}' {targetPoints} times" : description.Trim(),
                    UpdateDate = BsonDateTime.Create(DateTime.UtcNow),
                    CreateDate = BsonDateTime.Create(DateTime.UtcNow)
                };

                var collection = GetChallengesCollection();
                await collection.ReplaceOneAsync(
                    Builders<GameEventChallengeDocument>.Filter.Eq(x => x.Id, doc.Id),
                    doc,
                    new ReplaceOptions { IsUpsert = true });

                if (requestStyle)
                {
                    SendEmptyMessage(guid);
                }
                else
                {
                    SendResponse(guid);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetAllChallenges(BinaryValue[] values, string guid)
        {
            try
            {
                string gameEventId = ReadGameEventId(values);
                if (string.IsNullOrWhiteSpace(gameEventId))
                {
                    gameEventId = DefaultEventId;
                }

                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                Console.WriteLine($"[GameEvent] GetAllChallenges called for Player: {playerId}, EventId: {gameEventId}");

                var progress = await GetOrCreateProgress(playerId, gameEventId);
                var challenges = await GetChallenges(gameEventId);

                Console.WriteLine($"[GameEvent] Returning {challenges?.Count ?? 0} challenges for {gameEventId}");

                var response = new GetCurrentChallengesResponse();
                foreach (var x in challenges)
                {
                    int currentPoints = progress.ChallengeProgress.TryGetValue(x.Id, out var p) ? p.ToInt32() : 0;
                    response.Challenges.Add(ToCurrentChallengeProto(x, currentPoints));
                }

                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetCurrentChallenges(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                GetCurrentChallengesRequest request = StandRiseServer.RpcServer.Core.RpcRequestExtension.GetValue<GetCurrentChallengesRequest>(values, 0);

                string gameEventId = request?.GameEventId;
                if (string.IsNullOrWhiteSpace(gameEventId))
                {
                    gameEventId = ReadGameEventId(values);
                }
                if (string.IsNullOrWhiteSpace(gameEventId))
                {
                    gameEventId = DefaultEventId;
                }

                bool completedOnly = request?.Completed ?? false;
                
                Console.WriteLine($"[GameEvent] GetCurrentChallenges called for Player: {playerId}, EventId: {gameEventId}, CompletedOnly: {completedOnly}");

                var progress = await GetOrCreateProgress(playerId, gameEventId);
                var challenges = await GetChallenges(gameEventId);

                Console.WriteLine($"[GameEvent] Returning {challenges?.Count ?? 0} challenges for {gameEventId}");

                var response = new GetCurrentChallengesResponse();
                foreach (var x in challenges)
                {
                    int currentPoints = progress.ChallengeProgress.TryGetValue(x.Id, out var p) ? p.ToInt32() : 0;
                    bool completed = currentPoints >= x.TargetPoints;
                    // if (completedOnly && !completed)
                    // {
                    //     continue;
                    // }
                    response.Challenges.Add(ToCurrentChallengeProto(x, currentPoints));
                }

                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetCurrentGameEvents(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                string eventId = ReadGameEventId(values);
                Logger.Log($"[GameEvent] GetCurrentGameEvents: playerId={playerId} eventId='{eventId}'");
                var response = await BuildCurrentGameEventsResponse(playerId, eventId);
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetPlayerCurrentGameEvents(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                string eventId = ReadGameEventId(values);
                Logger.Log($"[GameEvent] GetPlayerCurrentGameEvents: playerId={playerId} eventId='{eventId}'");
                var response = await BuildCurrentGameEventsResponse(playerId, eventId);
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetPlayerCurrentGameEvents2(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                string eventId = ReadGameEventId(values);
                var baseResponse = await BuildCurrentGameEventsResponse(playerId, eventId);
                var response = new GetPlayerCurrentGameEvents2Response();
                if (baseResponse?.GameEvents != null)
                {
                    response.GameEvents.AddRange(baseResponse.GameEvents);
                }
                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private const string CursedSoulsEventId = "CURSED_SOULS";

        // Общая логика выбора активного события: явный код -> JSON-файлы (HotWinterParty2023 / CursedSouls) ->
        // база как подстраховка. Используется и для ответа клиенту, и для выдачи наград за уровни.
        private static BoltGameEventDocument ResolveGameEvent(string eventId)
        {
            var db = BoltGameDatabaseProvider.Instance;
            BoltGameEventDocument gameEvent = null;

            string id = eventId ?? string.Empty;
            bool isHotWinter = id.IndexOf("HOT_WINTER", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("HOTWINTER", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isCursedSouls = !isHotWinter && (
                string.IsNullOrWhiteSpace(id)
                || id.IndexOf("CURSED", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("SPIN_ROULETTE", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("HALLOWEEN", StringComparison.OrdinalIgnoreCase) >= 0
                || id.Equals(CursedSoulsEventId, StringComparison.OrdinalIgnoreCase)
                || id.Equals(DefaultEventId, StringComparison.OrdinalIgnoreCase));

            if (isCursedSouls)
            {
                lock (EventCacheLock)
                {
                    // После правки дат JSON не держим кэш 120с — иначе −18ч залипает.
                    if (CachedCursedSoulsEvent != null && (DateTime.UtcNow - CachedCursedSoulsAt).TotalSeconds < 15)
                        return CachedCursedSoulsEvent;
                }
            }

            try {
                string fileName = isCursedSouls ? "CursedSouls.json" : "HotWinterParty2023.json";
                string baseDir = System.AppDomain.CurrentDomain.BaseDirectory ?? "";
                var paths = new System.Collections.Generic.List<string>
                {
                    System.IO.Path.Combine(baseDir, fileName),
                    System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", fileName)),
                    System.IO.Path.Combine(@"C:\Users\Administrator\Desktop\test", fileName)
                };
                foreach (string path in paths)
                {
                    try
                    {
                        if (!System.IO.File.Exists(path)) continue;
                        string json = System.IO.File.ReadAllText(path);
                        gameEvent = global::MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BoltGameEventDocument>(json);
                        break;
                    }
                    catch { /* try next */ }
                }
                if (gameEvent == null)
                {
                    string alt = isCursedSouls ? "HotWinterParty2023.json" : "CursedSouls.json";
                    foreach (string path in paths.Select(p => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(p) ?? baseDir, alt)).Distinct())
                    {
                        try
                        {
                            if (!System.IO.File.Exists(path)) continue;
                            gameEvent = global::MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BoltGameEventDocument>(
                                System.IO.File.ReadAllText(path));
                            break;
                        }
                        catch { }
                    }
                }
            } catch (System.Exception ex) {
                Logger.Error($"Failed to load game event JSON for '{eventId}': " + ex.Message);
            }

            if (gameEvent == null && !string.IsNullOrWhiteSpace(eventId) && eventId != DefaultEventId)
            {
                gameEvent = db.GetGameEvent(eventId);
            }

            if (gameEvent == null)
            {
                gameEvent = db.GetActiveGameEvent();
            }

            if (isCursedSouls && gameEvent != null)
            {
                lock (EventCacheLock)
                {
                    CachedCursedSoulsEvent = gameEvent;
                    CachedCursedSoulsAt = DateTime.UtcNow;
                }
            }

            return gameEvent;
        }

        /// <summary>
        /// Free и premium — один прогресс уровня (как в клиенте: одна шкала, два столбца наград).
        /// Раньше квесты поднимали только free → premium застревал на 0 и медали «пропадали» (слоты с брелками).
        /// </summary>
        private static void SyncPassLevels(PlayerGameEventProgressDocument progress)
        {
            if (progress.Levels == null) progress.Levels = new BsonDocument();
            int free = progress.Levels.TryGetValue("free", out var f) ? Math.Max(0, f.ToInt32()) : 0;
            int prem = progress.Levels.TryGetValue("premium", out var p) ? Math.Max(0, p.ToInt32()) : 0;
            int lvl = Math.Max(free, prem);
            if (lvl < 1) lvl = 1;
            progress.Levels["free"] = lvl;
            // premium храним синхронно в БД; отображение Gold зависит от наличия #608/#613.
            progress.Levels["premium"] = lvl;
        }

        internal static bool PlayerOwnsGoldPass(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return false;
            try
            {
                var inv = BoltGameDatabaseProvider.Instance.GetPlayerInventoryDocument(ObjectId.Parse(playerId));
                if (inv?.InventoryItems == null) return false;
                foreach (var el in inv.InventoryItems)
                {
                    if (!el.Value.IsBsonDocument) continue;
                    var d = el.Value.AsBsonDocument;
                    if (!d.Contains("itemDefinitionId")) continue;
                    int def = d["itemDefinitionId"].ToInt32();
                    if (def == 608 || def == 613) return true;
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[GameEvent] PlayerOwnsGoldPass: {ex.Message}");
            }
            return false;
        }

        public static void InvalidatePlayerEventsCache()
        {
            PlayerEventsCache.Clear();
        }

        /// <summary>
        /// После выдачи уровней БП ботом — сразу начислить награды в инвентарь.
        /// </summary>
        /// <summary>После покупки/выдачи Gold Pass — premium-уровень = free, выдать все premium-награды до текущего уровня.</summary>
        public static void OnGoldPassAcquired(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            try
            {
                var collection = BoltGameDatabaseProvider.Instance.GetDatabase
                    .GetCollection<PlayerGameEventProgressDocument>("game_event_progress");
                var progress = collection.Find(x => x.PlayerId == playerId && x.EventId == CursedSoulsEventId).FirstOrDefault();
                if (progress == null)
                {
                    progress = new PlayerGameEventProgressDocument
                    {
                        PlayerId = playerId,
                        EventId = CursedSoulsEventId,
                        Levels = new BsonDocument { { "free", 1 }, { "premium", 1 } },
                        ClaimedLevels = new BsonDocument(),
                        Points = 0,
                        ChallengeProgress = new BsonDocument(),
                        UpdateDate = BsonDateTime.Create(DateTime.UtcNow)
                    };
                    collection.InsertOne(progress);
                }
                SyncPassLevels(progress);
                progress.ClaimedLevels ??= new BsonDocument();
                collection.ReplaceOne(
                    Builders<PlayerGameEventProgressDocument>.Filter.Eq(x => x.Id, progress.Id),
                    progress,
                    new ReplaceOptions { IsUpsert = true });
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[GameEvent] OnGoldPassAcquired sync: {ex.Message}");
            }

            try { InvalidatePlayerEventsCache(); } catch { }
            ForceGrantPendingLevelRewards(playerId);
        }

        public static void ForceGrantPendingLevelRewards(string playerId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(playerId)) return;
                var gameEvent = ResolveGameEvent(CursedSoulsEventId);
                if (gameEvent?.levels == null) return;

                var collection = BoltGameDatabaseProvider.Instance.GetDatabase
                    .GetCollection<PlayerGameEventProgressDocument>("game_event_progress");
                var progress = collection.Find(x => x.PlayerId == playerId && x.EventId == CursedSoulsEventId).FirstOrDefault();
                if (progress == null) return;

                progress.Levels ??= new BsonDocument();
                progress.ClaimedLevels ??= new BsonDocument();
                SyncPassLevels(progress);
                var levelsDict = BoltGameEventDocument.GetByDocument(gameEvent.levels);
                bool changed = false;

                foreach (string passCode in new[] { "free", "premium" })
                {
                    if (!levelsDict.TryGetValue(passCode, out var passLevels)) continue;
                    if (passCode == "premium" && !PlayerOwnsGoldPass(playerId)) continue;

                    int currentLevel = progress.Levels != null && progress.Levels.TryGetValue(passCode, out var lvl) ? lvl.ToInt32() : 0;
                    if (currentLevel <= 0) continue;
                    int claimedLevel = progress.ClaimedLevels != null && progress.ClaimedLevels.TryGetValue(passCode, out var cl) ? cl.ToInt32() : 0;
                    if (currentLevel <= claimedLevel) continue;

                    foreach (var kvp in passLevels.PassLevels.OrderBy(x => x.Key))
                    {
                        if (kvp.Key <= claimedLevel || kvp.Key > currentLevel) continue;
                        if (kvp.Value?.reward == null) continue;
                        BoltGameDatabaseProvider.Instance.GrantReward(playerId, kvp.Value.reward);
                        Logger.Log($"[GameEvent] ForceGranted BP reward: player={playerId} pass={passCode} level={kvp.Key}");
                    }

                    progress.ClaimedLevels ??= new BsonDocument();
                    progress.ClaimedLevels[passCode] = currentLevel;
                    changed = true;
                }

                if (changed)
                {
                    progress.UpdateDate = BsonDateTime.Create(DateTime.UtcNow);
                    collection.ReplaceOne(
                        Builders<PlayerGameEventProgressDocument>.Filter.Eq(x => x.Id, progress.Id),
                        progress,
                        new ReplaceOptions { IsUpsert = true });
                }

                InvalidatePlayerEventsCache();
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[GameEvent] ForceGrantPendingLevelRewards: {ex.Message}");
            }
        }

        private static void BumpPassLevels(PlayerGameEventProgressDocument progress, int delta = 1)
        {
            SyncPassLevels(progress);
            int lvl = progress.Levels["free"].ToInt32() + Math.Max(1, delta);
            progress.Levels["free"] = lvl;
            progress.Levels["premium"] = lvl;
        }

        private static void AddPassLevels(PlayerGameEventProgressDocument progress, int amount)
        {
            if (amount <= 0) return;
            SyncPassLevels(progress);
            int lvl = progress.Levels["free"].ToInt32() + amount;
            progress.Levels["free"] = lvl;
            progress.Levels["premium"] = lvl;
        }
        // но за которые награда ещё не была выдана (см. ClaimedLevels). Раньше нигде в живом
        // коде выдача наград вообще не вызывалась (GrantBpRewards в Program.cs — мёртвый код,
        // да ещё и смотрит на старое событие "new_year_madness_2020"), поэтому награды за
        // уровни БП, включая 1-й (медалька), никогда не попадали игрокам в инвентарь.
        private async Task GrantPendingLevelRewards(string playerId, PlayerGameEventProgressDocument progress, BoltGameEventDocument gameEvent)
        {
            if (gameEvent?.levels == null || gameEvent.levels.Count() == 0) return;

            var levelsDict = BoltGameEventDocument.GetByDocument(gameEvent.levels);
            bool changed = false;

            foreach (string passCode in new[] { "free", "premium" })
            {
                if (!levelsDict.TryGetValue(passCode, out var passLevels)) continue;

                // Gold-награды только при наличии Gold Pass (#608/#613).
                if (passCode == "premium" && !PlayerOwnsGoldPass(playerId)) continue;

                int currentLevel = progress.Levels.TryGetValue(passCode, out var lvl) ? lvl.ToInt32() : 0;
                if (currentLevel <= 0) continue;

                int claimedLevel = progress.ClaimedLevels.TryGetValue(passCode, out var cl) ? cl.ToInt32() : 0;
                if (currentLevel <= claimedLevel) continue;

                foreach (var kvp in passLevels.PassLevels.OrderBy(x => x.Key))
                {
                    if (kvp.Key <= claimedLevel || kvp.Key > currentLevel) continue;
                    if (kvp.Value?.reward == null) continue;

                    BoltGameDatabaseProvider.Instance.GrantReward(playerId, kvp.Value.reward);
                    Logger.Log($"[GameEvent] Granted BP reward: player={playerId} pass={passCode} level={kvp.Key}");
                }

                progress.ClaimedLevels[passCode] = currentLevel;
                changed = true;
            }

            if (changed)
            {
                progress.UpdateDate = BsonDateTime.Create(DateTime.UtcNow);
                var progressCollection = GetProgressCollection();
                await progressCollection.ReplaceOneAsync(
                    Builders<PlayerGameEventProgressDocument>.Filter.Eq(x => x.Id, progress.Id),
                    progress,
                    new ReplaceOptions { IsUpsert = true });
            }
        }

        public async Task<GetCurrentGameEventsResponse> BuildCurrentGameEventsResponse(string playerId, string eventId)
        {
            var response = new GetCurrentGameEventsResponse();

            if (eventId == "GLOBAL_LEVELS")
            {
                long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var dummyEvent = new CurrentGameEvent
                {
                    Id = "GLOBAL_LEVELS",
                    Code = "GLOBAL_LEVELS",
                    Points = 150000,
                    DateSince = nowUnix,
                    DateUntil = nowUnix + (3600L * 24 * 30),
                    DurationDays = 30,
                    CurrentDay = 1
                };
                
                var freePass = new GamePass { Id = "free", Code = "free", CurrentLevel = 50 };
                var goldPass = new GamePass { Id = "gold", Code = "gold", CurrentLevel = 50 };
                
                // Не 600 — огромный payload рвёт TCP у клиента.
                for (int i = 1; i <= 50; i++)
                {
                    var r1 = new RewardInfo();
                    r1.Currencies.Add(new CurrencyAmount { CurrencyId = 1, Value = 1 });
                    
                    var r2 = new RewardInfo();
                    r2.Currencies.Add(new CurrencyAmount { CurrencyId = 1, Value = 1 });
                    
                    freePass.Levels.Add(new GamePassLevel
                    {
                        Level = i,
                        MinPoints = i * 1000,
                        Reward = r1
                    });
                    goldPass.Levels.Add(new GamePassLevel
                    {
                        Level = i,
                        MinPoints = i * 1000,
                        Reward = r2
                    });
                }
                dummyEvent.GamePasses.Add(freePass);
                dummyEvent.GamePasses.Add(goldPass);
                
                response.GameEvents.Add(dummyEvent);
                return response;
            }

            BoltGameEventDocument gameEvent = ResolveGameEvent(eventId);

            if (gameEvent == null)
            {
                // Клиент 0.17 ждёт миллисекунды: раньше здесь отдавались секунды,
                // и на фолбэке таймер ивента показывал мусор.
                var fallbackWindow = GetPassWindow();
                response.GameEvents.Add(new CurrentGameEvent
                {
                    Id = "CURSED_SOULS",
                    Code = "CURSED_SOULS",
                    Points = 0,
                    DateSince = fallbackWindow.startMs,
                    DateUntil = fallbackWindow.endMs,
                    DurationDays = fallbackWindow.durationDays,
                    CurrentDay = fallbackWindow.currentDay,
                    GamePasses =
                    {
                        new GamePass { Id = "free", Code = "free", CurrentLevel = 1 },
                        new GamePass { Id = "gold", Code = "gold", CurrentLevel = 0, KeyItemDefinitionId = 608 }
                    }
                });
                return response;
            }

            var progress = await GetOrCreateProgress(playerId, CursedSoulsEventId);
            // Миграция: старые уровни могли лежать под code из JSON (HOT_WINTER_PARTY*).
            try
            {
                int cf = progress.Levels != null && progress.Levels.TryGetValue("free", out var cv) ? cv.ToInt32() : 0;
                if (cf <= 1)
                {
                    var progressCol = GetProgressCollection();
                    foreach (string legacyId in new[] { "HOT_WINTER_PARTY", "HOT_WINTER_PARTY_2023" })
                    {
                        var legacy = await progressCol.Find(x => x.PlayerId == playerId && x.EventId == legacyId).FirstOrDefaultAsync();
                        if (legacy?.Levels == null) continue;
                        int lf = legacy.Levels.TryGetValue("free", out var fv) ? fv.ToInt32() : 0;
                        if (lf <= cf) continue;
                        progress.Levels ??= new BsonDocument();
                        progress.Levels["free"] = lf;
                        progress.Levels["premium"] = Math.Max(lf, legacy.Levels.TryGetValue("premium", out var lp) ? lp.ToInt32() : 0);
                        progress.Points = legacy.Points;
                        await PersistProgressLevels(playerId, CursedSoulsEventId, progress);
                        Logger.Log($"[GameEvent] Migrated BP levels {lf} from '{legacyId}' → CURSED_SOULS for {playerId}");
                        break;
                    }
                }
            }
            catch (System.Exception migEx) { Logger.Error($"[GameEvent] BP migrate: {migEx.Message}"); }
            SyncPassLevels(progress);

            long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int freeLevel = progress.Levels.TryGetValue("free", out var free) ? Math.Max(1, free.ToInt32()) : 1;
            bool hasGoldPass = PlayerOwnsGoldPass(playerId);
            // Без #608 клиент должен видеть обычный (free) Pass; Gold CurrentLevel=0.
            int premiumLevel = hasGoldPass ? freeLevel : 0;
            // Клиент часто рисует шкалу из Points как суммарных; внутри уровня у нас 0..9.
            int within = progress.Points;
            if (within < 0 || within >= PointsPerLevel)
                within = ((within % PointsPerLevel) + PointsPerLevel) % PointsPerLevel;
            int displayPoints = (freeLevel - 1) * PointsPerLevel + within;
            // Клиент 0.17 считает DateSince/DateUntil в unix-MILLISECONDS.
            // Источник правды — GetPassWindow(): старт из админки, длина всегда 40 дней,
            // остаток никогда < 0. Обложка и страница спина считают из одних и тех же чисел.
            var passWindow = GetPassWindow();
            long startMs = passWindow.startMs;
            long endMs = passWindow.endMs;
            int durationDays = passWindow.durationDays;
            int currentDay = passWindow.currentDay;

            int goldKey = gameEvent.goldPassItem > 0 ? gameEvent.goldPassItem : 608;

            EnsurePassTemplates(gameEvent);

            // Не кэшируем после фикса дат — иначе клиент снова видит −18ч.
            PlayerEventsCache.Clear();

            // Клиент 0.17 ищет конкретные code пассов/ивентов — без них тайлы BP/рулетки пропадают.
            var currentEvent = new CurrentGameEvent
            {
                Id = "CURSED_SOULS",
                Code = "CURSED_SOULS",
                Points = displayPoints,
                DateSince = startMs,
                DateUntil = endMs,
                DurationDays = durationDays,
                CurrentDay = currentDay
            };
            // Только free + premium с Levels (Entity-per-item). Остальное — stub без Levels.
            currentEvent.GamePasses.Add(ClonePassTemplate(false, "free", freeLevel, 0));
            currentEvent.GamePasses.Add(ClonePassTemplate(true, "premium", premiumLevel, goldKey));
            currentEvent.GamePasses.Add(new GamePass { Id = "halloween2021_gold_pass", Code = "halloween2021_gold_pass", CurrentLevel = premiumLevel, KeyItemDefinitionId = goldKey });
            currentEvent.GamePasses.Add(new GamePass { Id = "gold", Code = "gold", CurrentLevel = premiumLevel, KeyItemDefinitionId = goldKey });
            currentEvent.GamePasses.Add(new GamePass { Id = "gold_pass", Code = "gold_pass", CurrentLevel = premiumLevel, KeyItemDefinitionId = goldKey });
            currentEvent.GamePasses.Add(new GamePass { Id = "free_pass", Code = "free_pass", CurrentLevel = freeLevel });
            Logger.Log($"[GameEvent] player={playerId} pass={(hasGoldPass ? "GOLD" : "FREE")} freeLvl={freeLevel} goldLvl={premiumLevel} pts={displayPoints}");
            response.GameEvents.Add(currentEvent);

            foreach (string alias in new[]
            {
                "HALLOWEEN_2021",
                "CURSED_SOULS_SPIN_ROULETTE",
                "HALLOWEEN_2021_SPIN_ROULETTE",
                "HOT_WINTER_PARTY_2023_SPIN_ROULETTE"
            })
            {
                response.GameEvents.Add(new CurrentGameEvent
                {
                    Id = alias,
                    Code = alias,
                    Points = displayPoints,
                    DateSince = startMs,
                    DateUntil = endMs,
                    DurationDays = durationDays,
                    CurrentDay = currentDay,
                    GamePasses =
                    {
                        new GamePass { Id = "free", Code = "free", CurrentLevel = freeLevel },
                        new GamePass { Id = "premium", Code = "premium", CurrentLevel = premiumLevel, KeyItemDefinitionId = goldKey },
                        new GamePass { Id = "halloween2021_gold_pass", Code = "halloween2021_gold_pass", CurrentLevel = premiumLevel, KeyItemDefinitionId = goldKey },
                        new GamePass { Id = "gold", Code = "gold", CurrentLevel = premiumLevel, KeyItemDefinitionId = goldKey }
                    }
                });
            }

            // Выдача наград BP — в фоне, не на пути логина.
            _ = Task.Run(async () =>
            {
                try { await GrantPendingLevelRewards(playerId, progress, gameEvent); }
                catch (System.Exception ex) { Logger.Error($"[GameEvent] deferred GrantPending: {ex.Message}"); }
            });

            return response;
        }

        // Public entry point used by GameServerGameEventRemoteService.getCurrentGameEventsByServer:
        // returns the authoritative active game-event list the client uses to enable the BP tile.
        public static async Task<GetCurrentGameEventsResponse> BuildByServerGameEvents(UserService user, string eventId)
        {
            if (user == null) return null;
            if (!StaticClasses.Users.TryGetValue(user.TcpClient, out string playerId)) return null;

            var svc = new GameEventRemoteService(user);
            var response = await svc.BuildCurrentGameEventsResponse(playerId, eventId);
            return response;
        }

        private static void EnsureRewardHasVisibleItems(RewardInfo reward)
        {
            if (reward == null) return;
            // Recipes-only слоты: не трогаем. Items нужны только для медалей/фолбэка без recipe.
            // Раньше сюда копировали skin id из recipe → клиент матчил Items и рисовал пустоту.
        }

        /// <summary>Прогрев JSON/пассов при старте сервера — первый логин не ждёт 30с.</summary>
        public static void WarmCaches()
        {
            try
            {
                lock (LevelsProtoCacheLock)
                {
                    CachedFreePassTemplate = null;
                    CachedGoldPassTemplate = null;
                }
                lock (EventCacheLock)
                {
                    CachedCursedSoulsEvent = null;
                    CachedCursedSoulsAt = DateTime.MinValue;
                }
                var evt = ResolveGameEvent("CURSED_SOULS");
                EnsurePassTemplates(evt);
                Console.WriteLine($"[GameEvent] WarmCaches OK freeLevels={CachedFreePassTemplate?.Levels?.Count ?? 0} goldLevels={CachedGoldPassTemplate?.Levels?.Count ?? 0}");
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[GameEvent] WarmCaches failed: {ex.Message}");
            }
        }

        private static void EnsurePassTemplates(BoltGameEventDocument gameEvent)
        {
            lock (LevelsProtoCacheLock)
            {
                if (CachedFreePassTemplate != null && CachedGoldPassTemplate != null) return;
                if (gameEvent?.levels == null) return;

                var levelsDict = BoltGameEventDocument.GetByDocument(gameEvent.levels);
                if (levelsDict.TryGetValue("free", out var freeLevels))
                {
                    CachedFreePassTemplate = new GamePass { Id = "free", Code = "free" };
                    CachedFreePassTemplate.Levels.AddRange(freeLevels.GetGamePassLevelsProto());
                }
                if (levelsDict.TryGetValue("premium", out var premLevels)
                    || levelsDict.TryGetValue("gold", out premLevels))
                {
                    CachedGoldPassTemplate = new GamePass { Id = "gold", Code = "gold" };
                    CachedGoldPassTemplate.Levels.AddRange(premLevels.GetGamePassLevelsProto());
                }
            }
        }

        private static GamePass ClonePassTemplate(bool premium, string id, int currentLevel, int keyItemDefinitionId)
        {
            GamePass src;
            lock (LevelsProtoCacheLock)
            {
                src = premium ? CachedGoldPassTemplate : CachedFreePassTemplate;
            }
            if (src == null)
            {
                return new GamePass
                {
                    Id = id,
                    Code = id,
                    CurrentLevel = currentLevel,
                    KeyItemDefinitionId = keyItemDefinitionId
                };
            }
            var pass = src.Clone();
            pass.Id = id;
            pass.Code = id;
            pass.CurrentLevel = currentLevel;
            if (keyItemDefinitionId > 0)
                pass.KeyItemDefinitionId = keyItemDefinitionId;
            return pass;
        }

        private static void LoadLevels(GamePass pass, string passCode, BoltGameEventDocument gameEvent)
        {
            if (pass == null) return;
            EnsurePassTemplates(gameEvent);
            bool premium = passCode.Equals("premium", StringComparison.OrdinalIgnoreCase)
                || passCode.Equals("gold", StringComparison.OrdinalIgnoreCase);
            var built = ClonePassTemplate(premium, pass.Id ?? passCode, pass.CurrentLevel, pass.KeyItemDefinitionId);
            pass.Levels.Clear();
            pass.Levels.AddRange(built.Levels);
        }

        private async Task SetChallengeProgress(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                ProgressChallengeRequest request = StandRiseServer.RpcServer.Core.RpcRequestExtension.GetValue<ProgressChallengeRequest>(values, 0);
                if (request == null || string.IsNullOrWhiteSpace(request.GameEventChallengeId))
                {
                    SendError(guid, 400);
                    return;
                }

                // Раньше тут был жёстко зашит eventId = "dragon_rise" — а наши реальные квесты
                // живут под "HOT_WINTER_PARTY_2023" (см. HotWinterParty2023.json). Из-за этого сервер всегда
                // искал квест не в том месте, никогда не находил, и клиент получал 404 —
                // отсюда "invalid mission, doesn't exist" при нажатии "Выполнить". Теперь ищем
                // квест по всей коллекции сразу, независимо от eventId, а eventId берём из
                // самого найденного квеста.
                var challengesCollection = GetChallengesCollection();
                GameEventChallengeDocument challenge = null;
                string challengeKey = request.GameEventChallengeId.Trim();
                if (ObjectId.TryParse(challengeKey, out _))
                {
                    challenge = await challengesCollection.Find(
                        Builders<GameEventChallengeDocument>.Filter.Eq(x => x.Id, challengeKey)).FirstOrDefaultAsync();
                }
                if (challenge == null)
                {
                    challenge = await challengesCollection.Find(
                        Builders<GameEventChallengeDocument>.Filter.Eq(x => x.Code, challengeKey)).FirstOrDefaultAsync();
                }

                if (challenge == null)
                {
                    SendError(guid, 404);
                    return;
                }

                string eventId = challenge.EventId;
                // Прогресс БП всегда в CURSED_SOULS (бот/UI), даже если квест в Mongo с другим EventId.
                if (string.IsNullOrWhiteSpace(eventId)
                    || eventId.IndexOf("CURSED", StringComparison.OrdinalIgnoreCase) >= 0
                    || eventId.IndexOf("HALLOWEEN", StringComparison.OrdinalIgnoreCase) >= 0
                    || eventId.IndexOf("HOT_WINTER", StringComparison.OrdinalIgnoreCase) >= 0
                    || eventId.IndexOf("SPIN", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    eventId = CursedSoulsEventId;
                }

                var progress = await GetOrCreateProgress(playerId, eventId);
                var currentPoints = progress.ChallengeProgress.TryGetValue(challenge.Id, out var currentValue)
                    ? currentValue.ToInt32()
                    : 0;

                int addPoints = Math.Max(0, request.Points);
                int updatedChallengePoints = Math.Max(0, currentPoints + addPoints);
                
                progress.ChallengeProgress[challenge.Id] = updatedChallengePoints;
                progress.UpdateDate = BsonDateTime.Create(DateTime.UtcNow);

                bool wasCompleted = currentPoints >= challenge.TargetPoints;
                bool isCompleted = updatedChallengePoints >= challenge.TargetPoints;

                int grantedEventPoints = 0;
                if (isCompleted && !wasCompleted)
                {
                    grantedEventPoints = challenge.EventPoints;
                    // ВАЖНО: Levels можно поднимать и напрямую покупкой (BuyEventLevel), без
                    // единого очка Points - т.е. Points и Levels НЕЗАВИСИМЫ по дизайну, и
                    // пересчитывать Levels из "суммарных Points за всё время" нельзя (это как
                    // раз и ломало пасс - см. историю правок). Вместо этого Points теперь хранит
                    // прогресс ТОЛЬКО внутри текущего уровня (0..PointsPerLevel-1): копим очки,
                    // и как только накопили PointsPerLevel - поднимаем Levels на 1 и переносим
                    // остаток дальше. Так Points никогда не убегает в тысячи и не требует
                    // никакого деления/остатка на отправке - шлём как есть.
                    // Если у игрока ещё лежит старое "убежавшее" значение Points (например
                    // 9252 с прошлых версий логики) - схлопываем его в границы одного уровня
                    // ОДИН раз, не трогая Levels, иначе while ниже поднимет уровень на сотни
                    // разом.
                    if (progress.Points < 0 || progress.Points >= PointsPerLevel)
                    {
                        progress.Points = ((progress.Points % PointsPerLevel) + PointsPerLevel) % PointsPerLevel;
                    }
                    progress.Points = Math.Max(0, progress.Points + challenge.EventPoints);
                    while (progress.Points >= PointsPerLevel)
                    {
                        progress.Points -= PointsPerLevel;
                        BumpPassLevels(progress, 1);
                    }
                }

                SyncPassLevels(progress);

                var progressCollection = GetProgressCollection();
                await progressCollection.ReplaceOneAsync(
                    Builders<PlayerGameEventProgressDocument>.Filter.Eq(x => x.Id, progress.Id),
                    progress,
                    new ReplaceOptions { IsUpsert = true });

                await GrantPendingLevelRewards(playerId, progress, ResolveGameEvent(eventId));

                var response = new ProgressChallengeResponse
                {
                    ChallengePoints = updatedChallengePoints,
                    Completed = isCompleted
                };

                int calculatedLevel = progress.Levels.TryGetValue("free", out var lvl) ? lvl.ToInt32() : 1;
                response.EventGamePassLevels["free"] = calculatedLevel;
                response.EventGamePassLevels["premium"] = calculatedLevel;

                if (isCompleted && !wasCompleted && challenge.EventPoints > 0)
                {
                    response.EventPoints = challenge.EventPoints;
                    
                    var levelsDict = new Dictionary<string, int>
                    {
                        { "free", calculatedLevel },
                        { "premium", calculatedLevel }
                    };
                    SendOnGamePassChangedEvent(challenge.Id, eventId, progress.Points, levelsDict, null);
                }

                SendResponse(guid, response);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task ProgressGameEvent(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                if (!TryReadProgressGameEventRequest(values, out ProgressGameEventRequest request))
                {
                    SendError(guid, 400);
                    return;
                }

                string eventId = string.IsNullOrWhiteSpace(request.GameEventId) ? DefaultEventId : request.GameEventId;
                var progress = await GetOrCreateProgress(playerId, eventId);

                int addPoints = Math.Max(0, request.Points);
                // Тот же принцип, что и в SetChallengeProgress: Points - это остаток ВНУТРИ
                // текущего уровня, а не пожизненная сумма. Копим и переносим избыток в Levels.
                if (progress.Points < 0 || progress.Points >= PointsPerLevel)
                {
                    progress.Points = ((progress.Points % PointsPerLevel) + PointsPerLevel) % PointsPerLevel;
                }
                progress.Points = Math.Max(0, progress.Points + addPoints);
                while (progress.Points >= PointsPerLevel)
                {
                    progress.Points -= PointsPerLevel;
                    BumpPassLevels(progress, 1);
                }
                SyncPassLevels(progress);
                progress.UpdateDate = BsonDateTime.Create(DateTime.UtcNow);

                var progressCollection = GetProgressCollection();
                await progressCollection.ReplaceOneAsync(
                    Builders<PlayerGameEventProgressDocument>.Filter.Eq(x => x.Id, progress.Id),
                    progress,
                    new ReplaceOptions { IsUpsert = true });

                await GrantPendingLevelRewards(playerId, progress, ResolveGameEvent(eventId));

                var response = new ProgressGameEventResponse
                {
                    Points = progress.Points
                };

                int calculatedLevel = progress.Levels.TryGetValue("free", out var lvl) ? lvl.ToInt32() : 1;
                response.Levels["free"] = calculatedLevel;
                response.Levels["premium"] = progress.Levels.TryGetValue("premium", out var prem) ? prem.ToInt32() : 0;

                SendResponse(guid, response);

                var levelsDict = new Dictionary<string, int>
                {
                    { "free", calculatedLevel },
                    { "premium", progress.Levels.TryGetValue("premium", out var prem2) ? prem2.ToInt32() : 0 }
                };
                SendOnGamePassChangedEvent(request.GameEventId, eventId, progress.Points, levelsDict, null);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task BuyEventLevel(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                if (!TryReadProgressGameEventRequest(values, out ProgressGameEventRequest request))
                {
                    SendError(guid, 400);
                    return;
                }

                var db = BoltGameDatabaseProvider.Instance;
                string eventId = string.IsNullOrWhiteSpace(request.GameEventId) ? DefaultEventId : request.GameEventId;

                if (!db.IsEnoughFunds(playerId, "102", 100))
                {
                    SendError(guid, 101);
                    return;
                }

                db.CurrencyMinusValue(ObjectId.Parse(playerId), 102, 100);

                var progress = await GetOrCreateProgress(playerId, eventId);
                BumpPassLevels(progress, 1);
                progress.UpdateDate = BsonDateTime.Create(DateTime.UtcNow);

                var progressCollection = GetProgressCollection();
                await progressCollection.ReplaceOneAsync(
                    Builders<PlayerGameEventProgressDocument>.Filter.Eq(x => x.Id, progress.Id),
                    progress,
                    new ReplaceOptions { IsUpsert = true });

                await GrantPendingLevelRewards(playerId, progress, ResolveGameEvent(eventId));

                var response = new ProgressGameEventResponse
                {
                    Points = progress.Points
                };

                int calculatedLevel = progress.Levels.TryGetValue("free", out var lvl) ? lvl.ToInt32() : 1;
                response.Levels["free"] = calculatedLevel;
                response.Levels["premium"] = calculatedLevel;

                SendResponse(guid, response);

                var levelsDict = new Dictionary<string, int>
                {
                    { "free", calculatedLevel },
                    { "premium", calculatedLevel }
                };
                SendOnGamePassChangedEvent(request.GameEventId, eventId, progress.Points, levelsDict, null);
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private async Task GetPlayerGameEventsProgresses(BinaryValue[] values, string guid)
        {
            try
            {
                if (!StaticClasses.Users.TryGetValue(_user.TcpClient, out string playerId))
                {
                    SendError(guid, 401);
                    return;
                }

                string gameEventId = string.Empty;
                if (values != null && values.Length > 0 && !values[0].IsNull)
                {
                    try
                    {
                        gameEventId = ReadGameEventId(values);
                    }
                    catch {}
                }

                if (string.IsNullOrWhiteSpace(gameEventId))
                {
                    gameEventId = DefaultEventId;
                }

                var progress = await GetOrCreateProgress(playerId, gameEventId);
                var challenges = await GetChallenges(gameEventId);

                var progressPayload = BuildGameEventProgressPayload(gameEventId, progress, challenges);
                var responseBytes = BuildPlayerGameEventsProgressesResponse(new[] { progressPayload });

                _user.SendResponce(new ResponseMessage
                {
                    RpcResponse = new RpcResponse
                    {
                        Id = guid,
                        Return = new BinaryValue
                        {
                            IsNull = false,
                            One = Google.Protobuf.ByteString.CopyFrom(responseBytes)
                        }
                    }
                });
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
                SendError(guid, 500);
            }
        }

        private static byte[] BuildPlayerGameEventsProgressesResponse(IEnumerable<byte[]> progresses)
        {
            using var stream = new MemoryStream();
            using var output = new CodedOutputStream(stream);
            foreach (var progress in progresses)
            {
                if (progress == null || progress.Length == 0)
                {
                    continue;
                }

                output.WriteRawTag(10);
                output.WriteBytes(ByteString.CopyFrom(progress));
            }

            output.Flush();
            return stream.ToArray();
        }

        private static byte[] BuildGameEventProgressPayload(
            string eventId,
            PlayerGameEventProgressDocument progress,
            IReadOnlyList<GameEventChallengeDocument> challenges)
        {
            var challengeTargets = challenges.ToDictionary(x => x.Id, x => x.TargetPoints, StringComparer.OrdinalIgnoreCase);
            // Раньше день считался от выдуманного since = now-14 → всегда «день 15».
            // Берём тот же день, что уходит в обложку ивента.
            var currentDay = GetPassWindow().currentDay;

            using var stream = new MemoryStream();
            using var output = new CodedOutputStream(stream);

            output.WriteRawTag(10);
            output.WriteString(eventId);

            output.WriteRawTag(16);
            output.WriteInt32(Math.Max(0, progress.Points));

            foreach (var level in progress.Levels.Elements)
            {
                output.WriteRawTag(26);
                output.WriteBytes(ByteString.CopyFrom(BuildGamePassProgressPayload(level.Name, level.Value.ToInt32())));
            }

            foreach (var challengeProgress in progress.ChallengeProgress.Elements)
            {
                var points = Math.Max(0, challengeProgress.Value.ToInt32());
                var completed = challengeTargets.TryGetValue(challengeProgress.Name, out var target) && points >= target;
                output.WriteRawTag(34);
                output.WriteBytes(ByteString.CopyFrom(BuildChallengeProgressPayload(challengeProgress.Name, points, completed)));
            }

            output.WriteRawTag(40);
            output.WriteInt32(currentDay);

            output.Flush();
            return stream.ToArray();
        }

        private static byte[] BuildGamePassProgressPayload(string id, int currentLevel)
        {
            using var stream = new MemoryStream();
            using var output = new CodedOutputStream(stream);
            output.WriteRawTag(10);
            output.WriteString(id ?? string.Empty);
            output.WriteRawTag(16);
            output.WriteInt32(Math.Max(0, currentLevel));
            output.Flush();
            return stream.ToArray();
        }

        private static byte[] BuildChallengeProgressPayload(string id, int points, bool completed)
        {
            using var stream = new MemoryStream();
            using var output = new CodedOutputStream(stream);
            output.WriteRawTag(10);
            output.WriteString(id ?? string.Empty);
            output.WriteRawTag(16);
            output.WriteInt32(Math.Max(0, points));
            output.WriteRawTag(24);
            output.WriteBool(completed);
            output.Flush();
            return stream.ToArray();
        }

        private IMongoCollection<PlayerGameEventProgressDocument> GetProgressCollection()
        {
            return BoltGameDatabaseProvider.Instance.GetDatabase.GetCollection<PlayerGameEventProgressDocument>("game_event_progress");
        }

        private IMongoCollection<GameEventChallengeDocument> GetChallengesCollection()
        {
            return BoltGameDatabaseProvider.Instance.GetDatabase.GetCollection<GameEventChallengeDocument>("game_event_challenge");
        }

        private static string BuildChallengeId(string eventId, string code, string action, int targetPoints)
        {
            var safeEvent = string.IsNullOrWhiteSpace(eventId) ? DefaultEventId : eventId.Trim();
            var safeCode = string.IsNullOrWhiteSpace(code) ? "challenge" : code.Trim().ToLowerInvariant().Replace(' ', '_');
            var safeAction = string.IsNullOrWhiteSpace(action) ? "action" : action.Trim().ToLowerInvariant().Replace(' ', '_');
            var safeTarget = Math.Max(1, targetPoints);
            return $"{safeEvent}:{safeCode}:{safeAction}:{safeTarget}";
        }

        private static CurrentChallenge ToCurrentChallengeProto(GameEventChallengeDocument challenge, int currentPoints)
        {
            // Неделя берётся из кода квеста (_w1_.._w6_) и разворачивается в дни пасса.
            // Раньше жёстко подставлялись только недели 1 и 2, поэтому все квесты
            // с 3-й недели и дальше показывались клиенту как «неделя 1».
            var week = ParseChallengeWeek(challenge.Code);
            var range = GetWeekDayRange(week);
            int fromDay = range.from;
            int toDay = range.to;

            return new CurrentChallenge
            {
                GameEventChallengeId = challenge.Id,
                Code = challenge.Code,
                Action = challenge.Action,
                Type = challenge.Type,
                TargetPoints = Math.Max(1, challenge.TargetPoints),
                CurrentPoints = Math.Max(0, currentPoints),
                EventPoints = Math.Max(0, challenge.EventPoints),
                KeyItemDefinitionId = Math.Max(0, challenge.KeyItemDefinitionId),
                DayRange = new DayRange { From = fromDay, To = toDay },
                LocalizedTitle = new LocalizedTitle
                {
                    Name = string.IsNullOrWhiteSpace(challenge.Title) ? $"{challenge.Code} x{challenge.TargetPoints}" : challenge.Title,
                    Description = string.IsNullOrWhiteSpace(challenge.Description) ? $"Do action '{challenge.Action}' {challenge.TargetPoints} times" : challenge.Description
                },
                Reward = new RewardInfo()
            };
        }

        private static string ReadGameEventId(BinaryValue[] values)
        {
            if (values == null || values.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                var plain = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                if (!string.IsNullOrWhiteSpace(plain))
                {
                    return plain;
                }
            }
            catch {}

            if (values[0]?.One == null)
            {
                return string.Empty;
            }

            try
            {
                using var stream = new MemoryStream(values[0].One.ToByteArray());
                var input = new CodedInputStream(stream);
                while (!input.IsAtEnd)
                {
                    uint tag = input.ReadTag();
                    if (tag == 0) break;
                    if (tag == 10)
                    {
                        string id = input.ReadString();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            return id;
                        }
                    }
                    else
                    {
                        input.SkipLastField();
                    }
                }
            }
            catch {}

            return string.Empty;
        }

        // Extracts an event id from a GameServerGameEventRemoteService (by-server) request.
        // The request wrapper (DLBKBPHNFAP) may carry the id as a plain string or nested in a
        // protobuf field; we default to the active event when it can't be determined.
        public static string ReadByServerEventId(BinaryValue[] values)
        {
            if (values == null || values.Length == 0 || values[0]?.One == null)
            {
                return DefaultEventId;
            }

            try
            {
                var plain = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                if (!string.IsNullOrWhiteSpace(plain))
                {
                    return plain;
                }
            }
            catch { }

            try
            {
                using var stream = new MemoryStream(values[0].One.ToByteArray());
                var input = new CodedInputStream(stream);
                while (!input.IsAtEnd)
                {
                    uint tag = input.ReadTag();
                    if (tag == 0) break;
                    if (tag == 10)
                    {
                        string id = input.ReadString();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            return id;
                        }
                    }
                    else
                    {
                        input.SkipLastField();
                    }
                }
            }
            catch { }

            return DefaultEventId;
        }

        private static bool TryParseChallengeDefinition(
            BinaryValue[] values,
            out string code,
            out string action,
            out int targetPoints,
            out int eventPoints,
            out int keyItemDefinitionId,
            out string title,
            out string description,
            out string challengeId,
            out bool requestStyle)
        {
            code = "challengeCode";
            action = "challengeAction";
            targetPoints = 1;
            eventPoints = 50;
            keyItemDefinitionId = 0;
            title = string.Empty;
            description = string.Empty;
            challengeId = string.Empty;
            requestStyle = false;

            if (values != null && values.Length >= 3)
            {
                try
                {
                    string pCode = (string)new FromByteMethod(typeof(string)).FromBytes(values[0]);
                    string pAction = (string)new FromByteMethod(typeof(string)).FromBytes(values[1]);
                    int pTarget = (int)new FromByteMethod(typeof(int)).FromBytes(values[2]);

                    if (!string.IsNullOrWhiteSpace(pCode) && !string.IsNullOrWhiteSpace(pAction) && pTarget > 0)
                    {
                        code = pCode;
                        action = pAction;
                        targetPoints = pTarget;

                        if (values.Length >= 4 && !values[3].IsNull)
                        {
                            try { eventPoints = (int)new FromByteMethod(typeof(int)).FromBytes(values[3]); } catch {}
                        }
                        if (values.Length >= 5 && !values[4].IsNull)
                        {
                            try { keyItemDefinitionId = (int)new FromByteMethod(typeof(int)).FromBytes(values[4]); } catch {}
                        }
                        if (values.Length >= 6 && !values[5].IsNull)
                        {
                            try { title = (string)new FromByteMethod(typeof(string)).FromBytes(values[5]); } catch {}
                        }
                        if (values.Length >= 7 && !values[6].IsNull)
                        {
                            try { description = (string)new FromByteMethod(typeof(string)).FromBytes(values[6]); } catch {}
                        }
                        if (values.Length >= 8 && !values[7].IsNull)
                        {
                            try { challengeId = (string)new FromByteMethod(typeof(string)).FromBytes(values[7]); } catch {}
                        }

                        return true;
                    }
                }
                catch {}
            }

            if (values == null || values.Length == 0 || values[0]?.One == null)
            {
                return false;
            }

            try
            {
                using var stream = new MemoryStream(values[0].One.ToByteArray());
                var input = new CodedInputStream(stream);
                while (!input.IsAtEnd)
                {
                    uint tag = input.ReadTag();
                    if (tag == 0) break;

                    switch (tag)
                    {
                        case 10: code = input.ReadString(); break;
                        case 18: action = input.ReadString(); break;
                        case 24: targetPoints = input.ReadInt32(); break;
                        case 32: eventPoints = input.ReadInt32(); break;
                        case 40: keyItemDefinitionId = input.ReadInt32(); break;
                        case 50: title = input.ReadString(); break;
                        case 58: description = input.ReadString(); break;
                        case 66: challengeId = input.ReadString(); break;
                        default: input.SkipLastField(); break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(action) && targetPoints > 0)
                {
                    requestStyle = true;
                    return true;
                }
            }
            catch {}

            return false;
        }

        private static bool TryReadProgressGameEventRequest(BinaryValue[] values, out ProgressGameEventRequest request)
        {
            request = null;

            try
            {
                request = StandRiseServer.RpcServer.Core.RpcRequestExtension.GetValue<ProgressGameEventRequest>(values, 0);
                if (request != null)
                {
                    return true;
                }
            }
            catch {}

            if (values == null || values.Length == 0 || values[0]?.One == null)
            {
                return false;
            }

            try
            {
                string eventId = string.Empty;
                int points = 0;

                using var stream = new MemoryStream(values[0].One.ToByteArray());
                var input = new CodedInputStream(stream);
                while (!input.IsAtEnd)
                {
                    uint tag = input.ReadTag();
                    if (tag == 0) break;

                    switch (tag)
                    {
                        case 10:
                            eventId = input.ReadString();
                            break;
                        case 16:
                            points = input.ReadInt32();
                            break;
                        default:
                            input.SkipLastField();
                            break;
                    }
                }

                request = new ProgressGameEventRequest
                {
                    GameEventId = eventId,
                    Points = points
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SendOnGamePassChangedEvent(string challengeId, string eventId, int points, Dictionary<string, int> levels, Reward reward)
        {
            try
            {
                var evt = new OnGamePassChangedEvent
                {
                    EventId = string.IsNullOrWhiteSpace(eventId) ? challengeId ?? string.Empty : eventId,
                    Points = points
                };

                if (levels != null)
                {
                    foreach (var kv in levels)
                    {
                        evt.Levels[kv.Key] = kv.Value;
                    }
                }

                if (reward != null && (reward.Items.Count > 0 || reward.Currencies.Count > 0))
                {
                    evt.Reward = reward;
                }

                _user.SendResponce(new ResponseMessage
                {
                    EventResponse = new EventResponse
                    {
                        ListenerName = "GameEventRemoteEventListener",
                        EventName = "onGamePassChanged",
                        Params = { new ToByteMethod(typeof(OnGamePassChangedEvent)).ToBytes(evt) }
                    }
                });
            }
            catch (System.Exception ex)
            {
                Logger.Exception(ex);
            }
        }

        private void SendResponse(string guid, IMessage response = null)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = response != null ? new BinaryValue { One = response.ToByteString() } : new BinaryValue { IsNull = true }
                }
            });
        }

        private void SendEmptyMessage(string guid)
        {
            _user.SendResponce(new ResponseMessage
            {
                RpcResponse = new RpcResponse
                {
                    Id = guid,
                    Return = new BinaryValue { One = ByteString.Empty }
                }
            });
        }

        private void SendError(string guid, int code)
        {
            if (code == 500) { System.Console.WriteLine($"\n[EXPLICIT 500] in GameEventRemoteService.cs for Request ID {guid}\n" + new System.Diagnostics.StackTrace(true).ToString()); }
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

    [Serializable]
    [global::MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    public sealed class GameEventChallengeDocument
    {
        [global::MongoDB.Bson.Serialization.Attributes.BsonId]
        [global::MongoDB.Bson.Serialization.Attributes.BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("eventId")]
        public string EventId { get; set; } = string.Empty;

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("code")]
        public string Code { get; set; } = string.Empty;

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("action")]
        public string Action { get; set; } = string.Empty;

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("type")]
        public string Type { get; set; } = string.Empty;

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("targetPoints")]
        public int TargetPoints { get; set; }

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("eventPoints")]
        public int EventPoints { get; set; } = 50;

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("keyItemDefinitionId")]
        public int KeyItemDefinitionId { get; set; }

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("title")]
        public string Title { get; set; } = string.Empty;

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("description")]
        public string Description { get; set; } = string.Empty;

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("createDate")]
        public BsonDateTime CreateDate { get; set; } = BsonDateTime.Create(DateTime.UtcNow);

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("updateDate")]
        public BsonDateTime UpdateDate { get; set; } = BsonDateTime.Create(DateTime.UtcNow);
    }

    [Serializable]
    [global::MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    public sealed class PlayerGameEventProgressDocument
    {
        [global::MongoDB.Bson.Serialization.Attributes.BsonId]
        [global::MongoDB.Bson.Serialization.Attributes.BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("playerId")]
        public string PlayerId { get; set; } = string.Empty;

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("eventId")]
        public string EventId { get; set; } = string.Empty;

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("points")]
        public int Points { get; set; }

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("levels")]
        public BsonDocument Levels { get; set; } = new BsonDocument();

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("challengeProgress")]
        public BsonDocument ChallengeProgress { get; set; } = new BsonDocument();

        // Максимальный уровень (по каждому пассу: free/premium), за который уже выданы награды.
        // Без этого трекинга награды либо не выдаются вообще, либо выдавались бы повторно
        // при каждом пересчёте уровня.
        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("claimedLevels")]
        public BsonDocument ClaimedLevels { get; set; } = new BsonDocument();

        [global::MongoDB.Bson.Serialization.Attributes.BsonElement("updateDate")]
        public BsonDateTime UpdateDate { get; set; } = BsonDateTime.Create(DateTime.UtcNow);
    }

    public class GetPlayerCurrentGameEvents2Response : Google.Protobuf.IMessage
    {
        public Google.Protobuf.Collections.RepeatedField<CurrentGameEvent> GameEvents { get; set; } = new Google.Protobuf.Collections.RepeatedField<CurrentGameEvent>();
        public long Timestamp { get; set; }

        public void WriteTo(CodedOutputStream output)
        {
            if (GameEvents != null)
            {
                foreach (var ev in GameEvents)
                {
                    if (ev != null)
                    {
                        output.WriteRawTag(10);
                        output.WriteMessage(ev);
                    }
                }
            }
            if (Timestamp != 0)
            {
                output.WriteRawTag(16);
                output.WriteInt64(Timestamp);
            }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (GameEvents != null)
            {
                foreach (var ev in GameEvents)
                {
                    if (ev != null)
                    {
                        size += 1 + CodedOutputStream.ComputeMessageSize(ev);
                    }
                }
            }
            if (Timestamp != 0)
            {
                size += 1 + CodedOutputStream.ComputeInt64Size(Timestamp);
            }
            return size;
        }

        public void MergeFrom(CodedInputStream input) { }
        public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null;
    }
}

