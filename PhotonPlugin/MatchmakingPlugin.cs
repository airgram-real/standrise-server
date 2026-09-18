using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Reflection;
using Photon.Hive.Plugin;

namespace Axlebolt.Standoff.PhotonPlugin
{
    public class MatchmakingPlugin : PluginBase
    {
        private const string BuildMarker = "warmup-all-joined-20260917-04";
        /// <summary>Ожидание подключения всех людей (союзники/соревновательный) — до 30 мин.</summary>
        private const int RankedHumanJoinWaitMs = 30 * 60 * 1000;
        private readonly string pluginName;
        private static readonly object logLock = new object();
        private bool _halftimeSwapDone = false;
        private bool _matchFinalizing = false;
        private bool _matchResultReported = false;
        private bool _serverOwnedFinalizationStarted = false;
        private bool _rankAssignmentTransitionScheduled = false;
        public MatchmakingPlugin() : this("MatchmakingPlugin")
        {
        }

        public MatchmakingPlugin(string pluginName)
        {
            this.pluginName = pluginName;
        }

        public override string Name => pluginName;

        private const byte EVT_CHANGE_TEAM_REQUEST = 21;
        private const byte EVT_CHANGE_TEAM_RESPONSE = 20;
        private const byte EVT_REBALANCE = 22;
        private const byte EVT_INIT_VOTING_REQUEST = 100;
        private const byte EVT_VOTE_REQUEST = 101;
        private bool _joinCancelScheduled = false;
        private bool _joinWaitTickerStarted = false;
        private bool _joinWaitActive = false;
        private long _joinDeadlineUnixMs = 0;
        private bool _lobbyAborted = false;
        private bool _hadLiveRound = false;
        private readonly ConcurrentDictionary<string, TrackedPlayerScore> _trackedPlayers =
            new ConcurrentDictionary<string, TrackedPlayerScore>(StringComparer.OrdinalIgnoreCase);
        private long _warmupStartedAtMs = 0;
        private bool _warmupTimerArmed = false;
        private bool _roundStartArmed = false;
        private int _activeVoteType = 0;
        private byte _surrenderTeam = 0;
        private int _seenTrScore = 0;
        private int _seenCtScore = 0;
        private int _lastLiveTrScore = 0;
        private int _lastLiveCtScore = 0;
        private byte _voteTeam = 0;
        private int _voteInitiatorActor = 0;
        private readonly HashSet<int> _voteYesActors = new HashSet<int>();
        private readonly HashSet<int> _voteNoActors = new HashSet<int>();
        private long _voteStartedAtMs = 0;
        // Фиксированный конец голосования в room Time — не сбрасывать при каждом Publish.
        private double _voteEndRoomTime = 0;
        private byte _c2BeforePause = 0;
        private bool _pauseActive = false;
        private bool _pausePending = false;
        private byte _pendingPauseTeam = 0;
        private long _pendingPauseEndUnixMs = 0;
        private bool _voteApplyScheduled = false;
        private bool _voteInitUiSent = false;
        // Кого предлагают кикнуть (UserId из поля 1 события). Пусто — это не кик.
        private string _voteKickTargetUserId = "";
        private readonly HashSet<byte> _teamsTookPause = new HashSet<byte>();
        private const int TacticalPauseDurationMs = 60000;
        // Сколько минимум висит плашка голосования до применения результата.
        private const int VoteMinVisibleMs = 5000;
        // Голосование живёт ровно столько, сколько мы объявили клиенту в vote_request (20с).
        private const int VoteTimeoutMs = 20000;
        // Как часто перепроверяем «набралось / истекло», пока голосование идёт.
        private const int VoteRecheckMs = 1000;
        // Сдача: держим экран начисления звания/уровня дольше штатных 13с.
        // Комнату НЕ закрываем киком, пока клиент сам не выйдет с FinalState —
        // RemoveActor раньше времени = «переподключение» без наград.
        private const int GiveUpFinalStateDelayMs = 35000;
        // Не кикаем во время экрана результата — иначе «Переподключиться» / «Отключиться».
        // Клиент сам уходит по «Продолжить»; страховка через 5 минут.
        private const int GiveUpRoomCloseDelayMs = 300000;
        private const int GiveUpForceLeaveMs = 22000;
        // Матч закрыт (сдача / штатное завершение): реконнект в комнату запрещён.
        private bool _rejoinBlocked = false;
        private bool _roomCloseScheduled = false;
        // Финализация запущена сдачей: экран звания/уровня держим дольше,
        // а комнату закрываем только после того, как клиент сам всё покажет.
        private bool _giveUpFinalization = false;

        // Вики: Master 1700, Elite 1800, Legend 2100 (internal 14/15/16 → client 15/16/17).
        private static readonly int[] RankMmrThresholds =
        {
            250, 300, 400, 500,
            600, 700, 800, 900,
            1000, 1100, 1200, 1300,
            1400, 1600, 1700, 1800, 2100
        };
        private const int EliteMmrFloor = 1800;

        private const byte TEAM_TERRORISTS = 1;
        private const byte TEAM_TERVORISTS = 1;
        private const byte TEAM_CT = 2;
        private const byte TEAM_SPECTATOR = 3;
        private const byte TEAM_NONE = 0;
        private const bool DEBUG_FINISH_RANKED2V2_AFTER_ROUND = false;

        private const int ERROR_NONE = 0;
        private const int ERROR_TEAM_FULL = 1;
        private const int ERROR_ALREADY_IN_TEAM = 2;

        private const byte EVT_GAME_EVENT = 128; 
        private const string PROP_CHALLENGES = "challenges"; 

        private const byte DATA_KEY = 245;

        private string serverUrl = "http://127.0.0.1:2224";

        public override bool SetupInstance(IPluginHost host, Dictionary<string, string> config, out string errorMsg)
        {
            host.LogInfo("MatchmakingPlugin: SetupInstance called. Initializing team selection plugin.");
            if (config != null)
            {
                if (config.TryGetValue("ServerUrl", out string url))
                {
                    serverUrl = url;
                }
                else if (config.TryGetValue("ServerIp", out string ip))
                {
                    serverUrl = "http://" + ip + ":2224";
                }
            }
            // Config historically pointed at RPC :2223 â€” HttpApi is :2224.
            if (serverUrl != null && serverUrl.EndsWith(":2223", StringComparison.Ordinal))
                serverUrl = serverUrl.Substring(0, serverUrl.Length - 4) + "2224";
            host.LogInfo("MatchmakingPlugin: Using ServerUrl=" + serverUrl);
            return base.SetupInstance(host, config, out errorMsg);
        }

        public override void OnCreateGame(ICreateGameCallInfo info)
        {
            this.PluginHost.LogInfo($"MatchmakingPlugin: OnCreateGame - Room: {this.PluginHost.GameId}");
            LogToFile($"PLUGIN_BUILD_MARKER={BuildMarker}");

            ClampEmptyRoomTtl(info);
            TryFixRanked2v2C1(info);

            // SoulHunt / Santa: keep legacy 1v1 spawn flow from stripped plugin.
            if (IsSoulHunt() || IsSantaClaus())
            {
                try
                {
                    if (info.Request.ActorProperties == null)
                        info.Request.ActorProperties = new Hashtable();
                    info.Request.ActorProperties["team"] = TEAM_CT;
                }
                catch (Exception ex)
                {
                    this.PluginHost.LogError($"MatchmakingPlugin: Error pre-setting event creator team: {ex.Message}");
                }
                info.Continue();
                var creator = this.PluginHost.GameActors.FirstOrDefault();
                if (IsSoulHunt())
                {
                    if (creator != null) { AssignSoulHuntTeam(creator.ActorNr); StartSoulHuntMatchIfReady(); }
                }
                else if (IsSantaClaus())
                {
                    if (creator != null) { AssignSantaClausTeam(creator.ActorNr); StartSantaClausMatchIfReady(); }
                }
                return;
            }

            info.Continue();

            // Ranked room props (max_players, team_size, afk_bots) — клиент часто не прокидывает их в Photon.
            try { SeedRankedRoomPropsFromHttp(); } catch { }

            this.PluginHost.CreateTimer(() => {
                try {
                    foreach (var actor in this.PluginHost.GameActors)
                    {
                        if (!string.IsNullOrEmpty(actor.UserId))
                        {
                            FetchChallengesAndSetProperties(actor.UserId, actor.ActorNr);
                            FetchRankedInfoAndSetProperties(actor.UserId, actor.ActorNr);
                        }
                    }
                } catch (Exception ex) {
                    this.PluginHost.LogError("MatchmakingPlugin: Sync timer error: " + ex.Message);
                }
            }, 30000, 30000);
        }

        /// <summary>Читает afk_bots с RpcServer и пишет в GameProperties (+кэш), чтобы WarmUp не ждал 4 пира.</summary>
        private struct RankedRoomHttpProps
        {
            public int AfkBots;
            public int MaxPlayers;
            public int TeamSize;
            public int RoundCount;
        }

        private void SeedRankedRoomPropsFromHttp()
        {
            string roomId = this.PluginHost.GameId ?? "";
            if (string.IsNullOrEmpty(roomId)) return;
            if (!(roomId.StartsWith("Ranked2v2", StringComparison.OrdinalIgnoreCase)
                  || roomId.StartsWith("RankedDefuse", StringComparison.OrdinalIgnoreCase)
                  || roomId.StartsWith("ClanRanked", StringComparison.OrdinalIgnoreCase)))
                return;

            RankedRoomHttpProps http = FetchRankedRoomPropsHttp(roomId);
            if (http.MaxPlayers <= 0 && http.AfkBots <= 0) return;

            if (http.AfkBots > 0) _afkBotsCache[roomId] = http.AfkBots;
            try
            {
                Hashtable props = new Hashtable();
                if (http.MaxPlayers >= 2 && http.MaxPlayers <= 10)
                    props["max_players"] = http.MaxPlayers;
                if (http.TeamSize >= 1 && http.TeamSize <= 5)
                    props["team_size"] = http.TeamSize;
                if (http.AfkBots > 0)
                    props["afk_bots"] = http.AfkBots;
                if (http.RoundCount >= 8 && http.RoundCount <= 16)
                {
                    props["round_count"] = http.RoundCount;
                    props["max_rounds"] = http.RoundCount;
                    props["win_rounds"] = http.RoundCount;
                }
                if (props.Count == 0) return;
                this.PluginHost.SetProperties(0, props, null, true);
                LogToFile($"SeedRankedRoomPropsFromHttp room={roomId} max_players={http.MaxPlayers} team_size={http.TeamSize} afk_bots={http.AfkBots} round_count={http.RoundCount}");
            }
            catch (Exception ex)
            {
                LogToFile($"SeedRankedRoomPropsFromHttp SetProperties fail: {ex.Message}");
            }
        }

        private static int ParseJsonIntField(string json, string fieldName)
        {
            int idx = json.IndexOf("\"" + fieldName + "\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;
            int colon = json.IndexOf(':', idx);
            if (colon <= 0) return 0;
            int end = colon + 1;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == ' ' || json[end] == '-')) end++;
            int.TryParse(json.Substring(colon + 1, end - colon - 1).Trim(), out int val);
            return val;
        }

        private RankedRoomHttpProps FetchRankedRoomPropsHttp(string roomId)
        {
            var result = new RankedRoomHttpProps();
            try
            {
                string url = $"{serverUrl}/api/match/afk-bots?roomId={Uri.EscapeDataString(roomId)}";
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 1500;
                req.ReadWriteTimeout = 1500;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                using (var stream = resp.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    string json = reader.ReadToEnd();
                    result.AfkBots = Math.Max(0, Math.Min(ParseJsonIntField(json, "afk_bots"), 9));
                    result.MaxPlayers = ParseJsonIntField(json, "max_players");
                    result.TeamSize = ParseJsonIntField(json, "team_size");
                    result.RoundCount = ParseJsonIntField(json, "round_count");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"FetchRankedRoomPropsHttp fail: {ex.Message}");
            }
            return result;
        }

        private int FetchAfkBotsHttp(string roomId)
        {
            return FetchRankedRoomPropsHttp(roomId).AfkBots;
        }

        private void TryFixRanked2v2C1(ICreateGameCallInfo info)
        {
            try
            {
                string gameId = this.PluginHost.GameId ?? string.Empty;
                bool is2v2 = gameId.StartsWith("Ranked2v2", StringComparison.OrdinalIgnoreCase);
                if (info.Request.GameProperties == null) return;
                var reqProps = info.Request.GameProperties;
                object c0Obj = reqProps.ContainsKey("C0") ? reqProps["C0"] : null;
                string c0 = c0Obj as string ?? string.Empty;
                if (!is2v2 && !c0.Equals("Ranked2v2", StringComparison.OrdinalIgnoreCase)) return;
                object c1Obj = reqProps.ContainsKey("C1") ? reqProps["C1"] : null;
                string c1 = (c1Obj as string ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(c1) && c1.IndexOf("2x2", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    string fixedC1 = c1 + " 2x2";
                    reqProps["C1"] = fixedC1;
                    this.PluginHost.LogInfo($"MatchmakingPlugin: Ranked2v2 C1 fix '{c1}' -> '{fixedC1}'");
                }
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: TryFixRanked2v2C1 error: {ex.Message}");
            }
        }

        public override void BeforeJoin(IBeforeJoinGameCallInfo info)
        {
            this.PluginHost.LogInfo($"MatchmakingPlugin: BeforeJoin - PlayerId: {info.UserId}");
            info.Continue();
        }

        private void FetchChallengesAndSetProperties(string playerId, int actorNr = 0)
        {
            try
            {
                string url = $"{serverUrl}/api/gameevent/challenges?PlayerId={playerId}";
                
                var request = new HttpRequest
                {
                    Url = url,
                    Method = "GET",
                    Accept = "application/json",
                    Callback = (response, userState) => {
                        try
                        {
                            if (response.Status == HttpRequestQueueResult.Success && response.HttpCode == 200)
                            {
                                Hashtable props = new Hashtable();
                                props[PROP_CHALLENGES] = response.ResponseText;
                                
                                int targetActor = actorNr;
                                if (targetActor == 0)
                                {
                                    var actor = this.PluginHost.GameActors.FirstOrDefault(a => a.UserId == playerId);
                                    if (actor != null) targetActor = actor.ActorNr;
                                }

                                if (targetActor != 0)
                                {
                                    if (this.PluginHost.GameActors.Any(a => a.ActorNr == targetActor))
                                    {
                                        this.PluginHost.SetProperties(targetActor, props, null, true);
                                        this.PluginHost.LogInfo($"MatchmakingPlugin: Synced challenges for Actor {targetActor}");
                                    }
                                    else
                                    {
                                        this.PluginHost.LogWarning($"MatchmakingPlugin: Skipped sync challenges, Actor {targetActor} is no longer in room");
                                    }
                                }
                            }
                        }
                        catch (Exception callbackEx)
                        {
                            this.PluginHost.LogError($"MatchmakingPlugin: Error in FetchChallenges callback: {callbackEx.Message}");
                        }
                    }
                };
                this.PluginHost.HttpRequest(request);
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: Error in FetchChallengesAndSetProperties: {ex.Message}");
            }
        }

        private void FetchRankedInfoAndSetProperties(string playerId, int actorNr = 0, bool includeRankAssignmentResult = false)
        {
            try
            {
                string mode = "Training";
                if (this.PluginHost.GameProperties.ContainsKey("C0"))
                {
                    mode = this.PluginHost.GameProperties["C0"] as string ?? "Training";
                }

                string url = $"{serverUrl}/api/player/ranked-info?PlayerId={playerId}&Mode={mode}";
                LogToFile($"FetchRankedInfoAndSetProperties sending GET request to: {url}");
                
                var request = new HttpRequest
                {
                    Url = url,
                    Method = "GET",
                    Accept = "application/json",
                    Callback = (response, userState) => {
                        try
                        {
                            if (response.Status == HttpRequestQueueResult.Success && response.HttpCode == 200)
                            {
                                string json = response.ResponseText;
                                LogToFile($"FetchRankedInfo callback: json={json}");
                                
                                float mmr = ParseFloatFromJson(json, "mmr", 250f);
                                float targetMmr = ParseFloatFromJson(json, "target_mmr", mmr);
                                int currentRank = ParseIntFromJson(json, "current_rank", 0);
                                int calibrationMatchCount = ParseIntFromJson(json, "calibration_match_count", 10);
                                int calibrationMatchesPlayed = ParseIntFromJson(json, "calibration_matches_played", 0);
                                int targetActor = actorNr;
                                if (targetActor == 0)
                                {
                                    var actor = this.PluginHost.GameActors.FirstOrDefault(a => a.UserId == playerId);
                                    if (actor != null) targetActor = actor.ActorNr;
                                }

                                float oldMmr = GetActorFloatProperty(targetActor: targetActor, key: "mmr_start", defaultValue: -1f);
                                int oldRankStored = GetActorIntProperty(targetActor: targetActor, key: "rank_start", defaultValue: -999);
                                if (oldMmr < 0f)
                                    oldMmr = GetActorFloatProperty(targetActor: targetActor, key: "mmr", defaultValue: mmr);
                                if (oldRankStored < -1)
                                    oldRankStored = GetActorIntProperty(targetActor: targetActor, key: "current_rank", defaultValue: currentRank);
                                
                                Hashtable props = new Hashtable();
                                props["mmr"] = mmr;
                                props["target_mmr"] = targetMmr;
                                int rankInt = currentRank < 1 ? 1 : currentRank;
                                if (rankInt > 17) rankInt = 17;
                                props["current_rank"] = rankInt;
                                props["Rank"] = rankInt;
                                props["rank"] = rankInt;
                                // Ник для scoreboard: никогда не ObjectId.
                                string nickName = ParseJsonString(json, "name");
                                string nickUid = ParseJsonString(json, "uid");
                                string nickAlias = ParseJsonString(json, "nickname");
                                string nick = null;
                                foreach (var cand in new[] { nickName, nickAlias, nickUid })
                                {
                                    if (string.IsNullOrWhiteSpace(cand) || LooksLikeObjectId(cand)) continue;
                                    nick = cand.Trim();
                                    break;
                                }
                                if (string.IsNullOrWhiteSpace(nick))
                                    nick = "Player" + targetActor;
                                props[(byte)255] = nick;
                                string accountUid = nickUid;
                                if (!string.IsNullOrWhiteSpace(accountUid) && !LooksLikeObjectId(accountUid))
                                    props["uid"] = accountUid;
                                props["name"] = nick;
                                props["nickname"] = nick;
                                string clanTag = ParseJsonString(json, "clan_tag");
                                if (!string.IsNullOrWhiteSpace(clanTag))
                                    props["clan_tag"] = clanTag;
                                string clanName = ParseJsonString(json, "clan_name");
                                if (!string.IsNullOrWhiteSpace(clanName))
                                    props["clan_name"] = clanName;
                                // Не затираем avatar: клиент кладёт Byte[] скина. UUID-строка
                                // из HTTP ломает экран статистики после матча.
                                // Снимок MMR на входе в комнату — для дельты в финале (не перезаписывать).
                                if (GetActorFloatProperty(targetActor, "mmr_start", -1f) < 0f)
                                {
                                    props["mmr_start"] = mmr;
                                    props["rank_start"] = currentRank;
                                }

                                if (includeRankAssignmentResult)
                                {
                                    props["rank_assigment_result_prop"] = BuildRankAssignmentResultJson(
                                        targetActor,
                                        oldMmr,
                                        mmr,
                                        oldRankStored,
                                        currentRank,
                                        calibrationMatchCount,
                                        calibrationMatchesPlayed);
                                    props["rank_data"] = props["rank_assigment_result_prop"];
                                }

                                if (targetActor != 0)
                                {
                                    if (this.PluginHost.GameActors.Any(a => a.ActorNr == targetActor))
                                    {
                                        this.PluginHost.SetProperties(targetActor, props, null, true);
                                        LogToFile($"FetchRankedInfoAndSetProperties: Synced MMR={mmr}, Rank={currentRank} for Actor {targetActor}");
                                        if (includeRankAssignmentResult)
                                        {
                                            LogToFile($"FetchRankedInfoAndSetProperties: Injected rank_assigment_result_prop for Actor {targetActor}");
                                        }
                                    }
                                    else
                                    {
                                        LogToFile($"FetchRankedInfoAndSetProperties: Skipped sync, Actor {targetActor} is no longer in room");
                                    }
                                }
                            }
                            else
                            {
                                LogToFile($"FetchRankedInfo callback failed. Status: {response.Status}, HttpCode: {response.HttpCode}");
                            }
                        }
                        catch (Exception callbackEx)
                        {
                            this.PluginHost.LogError($"MatchmakingPlugin: Error in FetchRankedInfo callback: {callbackEx.Message}");
                        }
                    }
                };
                this.PluginHost.HttpRequest(request);
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: Error in FetchRankedInfoAndSetProperties: {ex.Message}");
            }
        }

        private int GetActorIntProperty(int targetActor, string key, int defaultValue)
        {
            try
            {
                var actor = this.PluginHost.GameActors.FirstOrDefault(a => a.ActorNr == targetActor);
                if (actor != null && actor.Properties != null && actor.Properties.TryGetValue(key, out object value) && value != null)
                {
                    return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch { }

            return defaultValue;
        }

        private float GetActorFloatProperty(int targetActor, string key, float defaultValue)
        {
            try
            {
                var actor = this.PluginHost.GameActors.FirstOrDefault(a => a.ActorNr == targetActor);
                if (actor != null && actor.Properties != null && actor.Properties.TryGetValue(key, out object value) && value != null)
                {
                    return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch { }

            return defaultValue;
        }

        private int GetActorTeamSize(int targetActor)
        {
            byte team = GetActorTeamWithFallback(targetActor);
            if (team == TEAM_NONE || team == TEAM_SPECTATOR)
            {
                return 1;
            }

            int count = this.PluginHost.GameActors.Count(a => a.IsActive && GetActorTeamWithFallback(a, a.ActorNr) == team);
            return Math.Max(1, count);
        }

        private int GetEnemyTeamSize(int targetActor)
        {
            byte team = GetActorTeamWithFallback(targetActor);
            byte enemyTeam = team == TEAM_CT ? TEAM_TERRORISTS : TEAM_CT;
            int count = this.PluginHost.GameActors.Count(a => a.IsActive && GetActorTeamWithFallback(a, a.ActorNr) == enemyTeam);
            return Math.Max(1, count);
        }

        private static int RankFromMmrValue(float mmr)
        {
            if (mmr < RankMmrThresholds[0]) return 0;
            int rank = 0;
            for (int i = 0; i < RankMmrThresholds.Length; i++)
            {
                if (mmr >= RankMmrThresholds[i]) rank = i;
                else break;
            }
            if (rank < 0) rank = 0;
            if (rank > RankMmrThresholds.Length - 1) rank = RankMmrThresholds.Length - 1;
            return rank;
        }

        /// <summary>
        /// Client RankNew: -1=калибровка, 1..16=Bronze..Elite, 17=Legend (internal+1).
        /// </summary>
        private static int ToClientRankIndex(int internalRank)
        {
            if (internalRank < 0) return internalRank;
            if (internalRank > RankMmrThresholds.Length - 1)
                return RankMmrThresholds.Length; // Legend = 17
            return internalRank + 1;
        }

        /// <summary>
        /// Team icons: current_rank уже client-id 1..17.
        /// </summary>
        private static int[] ToAssignmentRankIds(int[] clientOrInternalRanks)
        {
            if (clientOrInternalRanks == null || clientOrInternalRanks.Length == 0)
                return new[] { 1 };
            int[] result = new int[clientOrInternalRanks.Length];
            for (int i = 0; i < clientOrInternalRanks.Length; i++)
            {
                int r = clientOrInternalRanks[i];
                if (r < 0) { result[i] = 1; continue; }
                if (r > 17) r = 17;
                if (r == 0) r = 1;
                result[i] = r;
            }
            return result;
        }

        /// <summary>
        /// HTTP/Photon current_rank уже client 1..17. Internal 0..16 только если пришёл 0
        /// или значение совпало с MMR-расчётом как internal.
        /// </summary>
        private static int ClientRankFromMmr(int mmr, int fallbackClientRank)
        {
            if (mmr >= 2100) return 17;
            if (mmr >= EliteMmrFloor) return 16;
            if (mmr >= 1700) return 15;
            if (fallbackClientRank >= 1 && fallbackClientRank <= 17) return fallbackClientRank;
            return 1;
        }

        private static int NormalizeStoredRankToClient(int stored, int internalFromMmr, int mmrRounded)
        {
            // MMR — источник правды (1745 = Master 15, не Elite 16 из старого stored).
            if (mmrRounded >= 2100) return 17;
            if (mmrRounded >= EliteMmrFloor) return 16;
            if (mmrRounded >= 1700) return 15;
            if (stored >= 1 && stored <= 17) return stored;
            if (stored == 0) return ToClientRankIndex(internalFromMmr);
            if (stored < 0) return 1;
            return ToClientRankIndex(Math.Min(stored, RankMmrThresholds.Length - 1));
        }

        private static int RankBandMin(int rank)
        {
            if (rank < 0) rank = 0;
            if (rank > 16) rank = 16;
            return RankMmrThresholds[rank];
        }

        private static int RankBandMax(int rank)
        {
            if (rank < 0) rank = 0;
            if (rank > 16) rank = 16;
            if (rank >= RankMmrThresholds.Length - 1)
                return 2500; // Legend ceiling — зеркало RankDistribution[17]
            return RankMmrThresholds[rank + 1];
        }

        private string BuildRankAssignmentResultJson(int targetActor, float oldMmr, float newMmr, int oldRank, int newRank, int calibrationMatchCount, int calibrationMatchesPlayed)
        {
            int oldMmrRounded = Math.Max(0, (int)Math.Round(oldMmr, MidpointRounding.AwayFromZero));
            int newMmrRounded = Math.Max(0, (int)Math.Round(newMmr, MidpointRounding.AwayFromZero));
            int safeCalibrationCount = Math.Max(10, calibrationMatchCount);
            int safeCalibrationPlayed = Math.Max(0, Math.Min(safeCalibrationCount, calibrationMatchesPlayed));
            bool inCalibration = safeCalibrationPlayed < safeCalibrationCount;

            int rankOldFromMmr = RankFromMmrValue(oldMmrRounded);
            int rankNewFromMmr = RankFromMmrValue(newMmrRounded);
            if (newMmrRounded >= 2100)
                rankNewFromMmr = RankMmrThresholds.Length - 1;
            if (oldMmrRounded >= 2100)
                rankOldFromMmr = RankMmrThresholds.Length - 1;

            // newRank/oldRank из HTTP/Photon уже client-id (-1 / 1..17). НЕ делать +1 повторно.
            int displayedNew;
            int displayedOld;
            if (inCalibration)
            {
                displayedNew = -1;
                displayedOld = -1;
            }
            else
            {
                // 1745 → Master(15), не Elite(16). Elite только с 1800.
                displayedNew = ClientRankFromMmr(newMmrRounded, NormalizeStoredRankToClient(newRank, rankNewFromMmr, newMmrRounded));
                displayedOld = oldRank < 0
                    ? -1
                    : ClientRankFromMmr(oldMmrRounded, NormalizeStoredRankToClient(oldRank, rankOldFromMmr, oldMmrRounded));
            }
            int deltaMmr = newMmrRounded - oldMmrRounded;

            int fallbackRankForIcons = Math.Max(1, displayedNew > 0 ? displayedNew : displayedOld);
            int[] playerTeamRanks = GetTeamRanksForActor(targetActor, enemy: false, fallbackRank: fallbackRankForIcons);
            int[] enemyTeamRanks = GetTeamRanksForActor(targetActor, enemy: true, fallbackRank: fallbackRankForIcons);
            int safePlayers = Math.Max(1, playerTeamRanks.Length);
            int safeEnemies = Math.Max(1, enemyTeamRanks.Length);

            int roundColumns = GetAlliesRoundColumns();
            float[] mmrPerRound = SplitDeltaAcrossRounds(deltaMmr, roundColumns);
            int playerTeamSum = SumTeamMmr(targetActor, enemy: false, fallback: oldMmrRounded);
            int enemyTeamSum = SumTeamMmr(targetActor, enemy: true, fallback: oldMmrRounded);

            // Только поля RankAssigmentResult (dump 0.17). Без TargetMmr/RankName.
            StringBuilder builder = new StringBuilder(768);
            builder.Append("{");
            builder.Append("\"IsSuccessful\":true,");
            builder.Append("\"RankNew\":").Append(displayedNew).Append(",");
            builder.Append("\"RankOld\":").Append(displayedOld).Append(",");
            builder.Append("\"CalibrationMatchCount\":").Append(safeCalibrationCount).Append(",");
            builder.Append("\"CalibrationMatchesPlayed\":").Append(safeCalibrationPlayed).Append(",");
            builder.Append("\"Mmr\":").Append(newMmrRounded).Append(",");
            builder.Append("\"DeltaMmr\":").Append(deltaMmr).Append(",");
            builder.Append("\"MmrPerRound\":").Append(BuildFloatArrayJson(mmrPerRound)).Append(",");
            builder.Append("\"PlayersPerRound\":").Append(BuildRepeatedIntJson(safePlayers, roundColumns)).Append(",");
            builder.Append("\"EnemiesPerRound\":").Append(BuildRepeatedIntJson(safeEnemies, roundColumns)).Append(",");
            builder.Append("\"PlayerTeamSumMmr\":").Append(playerTeamSum).Append(",");
            builder.Append("\"EnemyTeamSumMmr\":").Append(enemyTeamSum).Append(",");
            builder.Append("\"PlayerTeamRanks\":").Append(BuildIntArrayJson(playerTeamRanks)).Append(",");
            builder.Append("\"EnemyTeamRanks\":").Append(BuildIntArrayJson(enemyTeamRanks));
            builder.Append("}");
            string json = builder.ToString();
            LogToFile($"RankAssignment actor={targetActor} mmr={oldMmrRounded}->{newMmrRounded} RankOld={displayedOld} RankNew={displayedNew} (bar=RankDistribution[RankNew]; Legend needs dist[17]=2500)");
            return json;
        }

        private int GetAlliesRoundColumns()
        {
            string mode = "";
            try
            {
                if (this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("C0"))
                    mode = this.PluginHost.GameProperties["C0"] as string ?? "";
            }
            catch { }
            if (mode == "Ranked2v2")
                return 8;
            return 10;
        }

        private static float[] SplitDeltaAcrossRounds(int deltaMmr, int rounds)
        {
            if (rounds < 1) rounds = 8;
            float[] values = new float[rounds];
            if (deltaMmr == 0)
                return values;
            float sign = deltaMmr >= 0 ? 1f : -1f;
            float abs = Math.Abs(deltaMmr);
            float baseEach = (float)Math.Floor(abs / rounds);
            float rem = abs - baseEach * rounds;
            for (int i = 0; i < rounds; i++)
            {
                float v = baseEach;
                if (i >= rounds - rem) v += 1f;
                values[i] = v * sign;
            }
            return values;
        }

        private int SumTeamMmr(int targetActor, bool enemy, int fallback)
        {
            byte actorTeam = GetActorTeamWithFallback(targetActor);
            if (actorTeam == TEAM_NONE || actorTeam == TEAM_SPECTATOR)
                return Math.Max(1, fallback);
            byte targetTeam = enemy
                ? (actorTeam == TEAM_CT ? TEAM_TERRORISTS : TEAM_CT)
                : actorTeam;
            int sum = 0;
            int n = 0;
            foreach (var actor in this.PluginHost.GameActors)
            {
                if (actor == null || !actor.IsActive) continue;
                if (GetActorTeamWithFallback(actor, actor.ActorNr) != targetTeam) continue;
                int mmr = (int)Math.Round(GetActorFloatProperty(actor.ActorNr, "mmr_start",
                    GetActorFloatProperty(actor.ActorNr, "mmr", fallback)));
                sum += Math.Max(0, mmr);
                n++;
            }
            return n == 0 ? Math.Max(1, fallback) : sum;
        }

        private static string BuildFloatArrayJson(float[] values)
        {
            if (values == null || values.Length == 0) return "[]";
            StringBuilder builder = new StringBuilder(values.Length * 8);
            builder.Append("[");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) builder.Append(",");
                builder.Append(values[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            builder.Append("]");
            return builder.ToString();
        }

        private static string BuildRepeatedIntJson(int value, int count)
        {
            if (count < 1) count = 1;
            StringBuilder builder = new StringBuilder(count * 4);
            builder.Append("[");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) builder.Append(",");
                builder.Append(value);
            }
            builder.Append("]");
            return builder.ToString();
        }

        private int[] GetTeamRanksForActor(int targetActor, bool enemy, int fallbackRank)
        {
            byte actorTeam = GetActorTeamWithFallback(targetActor);
            if (actorTeam == TEAM_NONE || actorTeam == TEAM_SPECTATOR)
            {
                return new[] { Math.Max(1, fallbackRank) };
            }

            byte targetTeam = enemy
                ? (actorTeam == TEAM_CT ? TEAM_TERRORISTS : TEAM_CT)
                : actorTeam;

            List<int> ranks = new List<int>();
            foreach (var actor in this.PluginHost.GameActors)
            {
                if (actor == null || !actor.IsActive)
                {
                    continue;
                }

                if (GetActorTeamWithFallback(actor, actor.ActorNr) != targetTeam)
                {
                    continue;
                }

                int rank = GetActorIntProperty(actor.ActorNr, "current_rank", fallbackRank);
                ranks.Add(Math.Max(1, rank));
            }

            if (ranks.Count == 0)
            {
                ranks.Add(Math.Max(1, fallbackRank));
            }

            return ranks.ToArray();
        }

        private static string BuildIntArrayJson(int[] values)
        {
            if (values == null || values.Length == 0)
            {
                return "[]";
            }

            StringBuilder builder = new StringBuilder(values.Length * 4 + 2);
            builder.Append("[");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }

                builder.Append(values[i]);
            }

            builder.Append("]");
            return builder.ToString();
        }

        private void SyncFinalRankAssignmentResults()
        {
            try
            {
                string mode = "";
                if (this.PluginHost.GameProperties.ContainsKey("C0"))
                {
                    mode = this.PluginHost.GameProperties["C0"] as string ?? "";
                }

                if (mode != "RankedDefuse" && mode != "Ranked2v2" && mode != "ClanRankedDefuse")
                {
                    LogToFile($"SyncFinalRankAssignmentResults skipped for non-ranked mode {mode}.");
                    return;
                }

                foreach (var actor in this.PluginHost.GameActors)
                {
                    if (!actor.IsActive || string.IsNullOrWhiteSpace(actor.UserId))
                    {
                        continue;
                    }

                    FetchRankedInfoAndSetProperties(actor.UserId, actor.ActorNr, true);
                }
            }
            catch (Exception ex)
            {
                LogToFile($"SyncFinalRankAssignmentResults error: {ex}");
            }
        }

        private float ParseFloatFromJson(string json, string key, float defaultValue)
        {
            try
            {
                string searchKey = "\"" + key + "\":";
                int index = json.IndexOf(searchKey);
                if (index == -1)
                {
                    searchKey = key + ":";
                    index = json.IndexOf(searchKey);
                }
                if (index != -1)
                {
                    int start = index + searchKey.Length;
                    int end = json.IndexOf(',', start);
                    if (end == -1) end = json.IndexOf('}', start);
                    if (end != -1)
                    {
                        string valStr = json.Substring(start, end - start).Trim().Replace("\"", "").Replace(":", "").Replace(" ", "");
                        return float.Parse(valStr, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        private int ParseIntFromJson(string json, string key, int defaultValue)
        {
            try
            {
                string searchKey = "\"" + key + "\":";
                int index = json.IndexOf(searchKey);
                if (index == -1)
                {
                    searchKey = key + ":";
                    index = json.IndexOf(searchKey);
                }
                if (index != -1)
                {
                    int start = index + searchKey.Length;
                    int end = json.IndexOf(',', start);
                    if (end == -1) end = json.IndexOf('}', start);
                    if (end != -1)
                    {
                        string valStr = json.Substring(start, end - start).Trim().Replace("\"", "").Replace(":", "").Replace(" ", "");
                        return int.Parse(valStr, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        private void LogToFile(string message)
        {
            this.PluginHost.LogInfo("MatchmakingPlugin: " + message);
            try
            {
                lock (logLock)
                {
                    string dllPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    string logPath = Path.Combine(dllPath, "plugin_debug.log");
                    string logLine = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                    File.AppendAllText(logPath, logLine);
                }
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError("MatchmakingPlugin: LogToFile failed: " + ex.Message);
            }
        }

        public override void OnJoin(IJoinGameCallInfo info)
        {
            LogToFile($"OnJoin Start: ActorNr={info.ActorNr}, UserId={info.UserId}");

            // Матч закрыт (сдача / финал): реконнект в него запрещён — сдавшийся не должен
            // вернуться в тот же бой.
            if (_rejoinBlocked)
            {
                LogToFile($"OnJoin rejected (match closed): UserId={info.UserId}");
                info.Fail("match_finished");
                return;
            }

            // Кто-то подключился (или переподключился) во время голосования — сразу отдаём
            // ему актуальное состояние, иначе у игроков разные экраны голосования.
            if (_activeVoteType != 0)
            {
                int joiningActorNr = info.ActorNr;
                this.PluginHost.CreateOneTimeTimer(() => {
                    try
                    {
                        if (_activeVoteType == 0) return;
                        PublishVoteRequest(false);
                        LogToFile($"Vote state resynced for joining actor={joiningActorNr}.");
                    }
                    catch (Exception ex) { LogToFile($"Vote resync on join error: {ex.Message}"); }
                }, 500);
            }

            if (IsSoulHunt())
            {
                AssignSoulHuntTeam(info.ActorNr);
                info.Continue();
                StartSoulHuntMatchIfReady();
                return;
            }
            if (IsSantaClaus())
            {
                AssignSantaClausTeam(info.ActorNr);
                info.Continue();
                StartSantaClausMatchIfReady();
                return;
            }

            FetchChallengesAndSetProperties(info.UserId, info.ActorNr);
            FetchRankedInfoAndSetProperties(info.UserId, info.ActorNr);

            string gameMode = "Training";
            if (this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("C0"))
            {
                gameMode = this.PluginHost.GameProperties["C0"] as string;
            }
            LogToFile($"OnJoin gameMode={gameMode}");
            bool isRankedGame = IsRankedMode(gameMode);

            bool joinAsSpectator = IsListedSpectator(info.UserId);
            if (!isRankedGame && joinAsSpectator)
            {
                try
                {
                    SetJoinTeamProperty(info, TEAM_SPECTATOR);
                    LogToFile($"OnJoin spectator: UserId={info.UserId} -> team {TEAM_SPECTATOR}");
                }
                catch (Exception ex)
                {
                    LogToFile($"OnJoin spectator setup error: {ex.Message}");
                }
            }
            if (isRankedGame && joinAsSpectator)
            {
                try
                {
                    SetJoinTeamProperty(info, TEAM_SPECTATOR);
                    LogToFile($"OnJoin ranked spectator: UserId={info.UserId}");
                }
                catch (Exception ex) { LogToFile($"OnJoin ranked spectator error: {ex.Message}"); }
            }

            if (isRankedGame)
            {
                try
                {
                    byte existingTeam = GetExistingRequestTeam(info);
                    LogToFile($"OnJoin existingTeam={existingTeam}");
                    
                    if (existingTeam == TEAM_NONE || existingTeam == TEAM_SPECTATOR)
                    {
                        byte chosenTeam = GetDeterministicTeam(info.ActorNr);
                        LogToFile($"OnJoin ranked: ActorNr={info.ActorNr} -> chosenTeam={chosenTeam} (Deterministic)");
                        SetJoinTeamProperty(info, chosenTeam);
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"OnJoin error in team calculation: {ex}");
                    this.PluginHost.LogError($"MatchmakingPlugin: Error setting default team in OnJoin: {ex}");
                }
            }
            else
            {
                LogToFile($"OnJoin: non-ranked mode {gameMode}; leaving team selection to client/default game flow.");
            }

            // Check actor state before Continue
            var actorBefore = this.PluginHost.GameActors.FirstOrDefault(a => a.ActorNr == info.ActorNr);
            LogToFile($"OnJoin actor before Continue exists: {actorBefore != null}");

            info.Continue();

            // Check actor state after Continue
            var actorAfter = this.PluginHost.GameActors.FirstOrDefault(a => a.ActorNr == info.ActorNr);
            LogToFile($"OnJoin actor after Continue exists: {actorAfter != null}");

            // Сразу ставим ник (не ObjectId) — async FetchRankedInfo может опоздать, и клиент
            // уже рисует PhotonPlayer.NickName = UserId.
            try
            {
                string earlyNick = FetchPlayerDisplayNameHttp(info.UserId);
                if (string.IsNullOrWhiteSpace(earlyNick) || LooksLikeObjectId(earlyNick))
                    earlyNick = "Player" + info.ActorNr;
                Hashtable nickProps = new Hashtable();
                nickProps[(byte)255] = earlyNick;
                nickProps["name"] = earlyNick;
                nickProps["nickname"] = earlyNick;
                this.PluginHost.SetProperties(info.ActorNr, nickProps, null, true);
                LogToFile($"OnJoin early nick actor={info.ActorNr} nick={earlyNick}");
            }
            catch (Exception nickEx)
            {
                LogToFile($"OnJoin early nick failed: {nickEx.Message}");
            }

            if (!isRankedGame && IsListedSpectator(info.UserId))
            {
                try
                {
                    Hashtable spectProps = new Hashtable();
                    spectProps["team"] = TEAM_SPECTATOR;
                    spectProps["isSpectator"] = true;
                    this.PluginHost.SetProperties(info.ActorNr, spectProps, null, true);
                    SendChangeTeamResponse(info.ActorNr, true, TEAM_NONE, TEAM_SPECTATOR, ERROR_NONE);
                }
                catch (Exception ex) { LogToFile($"OnJoin spectator props error: {ex.Message}"); }
            }

            if (isRankedGame)
            {
                byte forcedTeam = GetDeterministicTeam(info.ActorNr);
                LogToFile($"OnJoin ranked: Triggering ForceDoubleTeamAssignment for Actor {info.ActorNr} to team {forcedTeam}");
                ForceDoubleTeamAssignment(info.ActorNr, forcedTeam);
            }

            ReportStatus(info.UserId, this.PluginHost.GameId, false, gameMode);
            try
            {
                var joined = this.PluginHost.GameActors.FirstOrDefault(a => a != null && a.ActorNr == info.ActorNr);
                if (joined != null) RememberTrackedPlayer(joined);
                else if (!string.IsNullOrEmpty(info.UserId))
                    RememberTrackedPlayerId(info.UserId, info.ActorNr);
            }
            catch { }

            if (isRankedGame)
            {
                int loadedPlayers, needHumans, requiredPlayers, botSlots;
                bool ready = HasEnoughHumansForStart(out loadedPlayers, out needHumans, out requiredPlayers, out botSlots);
                LogToFile($"OnJoin Ranked start check: currentPlayers={loadedPlayers}/{requiredPlayers} needHumans={needHumans} bots={botSlots} max_players={ReadMaxPlayersProp()}");
                
                if (ready)
                {
                    _joinWaitActive = false;
                    LogToFile($"OnJoin Ranked start check: Enough humans. Setting MatchStarted + WarmUp. needHumans={needHumans}");
                    Hashtable props = new Hashtable();
                    props["MatchStarted"] = true;
                    props["S1"] = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                    props["C2"] = (byte)11;
                    props["RouteToGameStateId"] = (byte)0;
                    props["Time"] = 30.0;
                    props["WarmUpDuration"] = 30.0f;
                    props["WaitPlayersUi"] = "";
                    props["WaitPlayersTimeout"] = 0f;
                    this.PluginHost.SetProperties(0, props, null, true);
                    StartWarmUpTimer();
                }
                else
                {
                    int cancelMs = RankedHumanJoinWaitMs;
                    StartJoinWaitUi(cancelMs);
                    ScheduleCancelIfNotFull(needHumans, cancelMs);
                }
            }
            LogToFile("OnJoin End");
        }

        private void AbortIncompleteRoom()
        {
            try
            {
                _lobbyAborted = true;
                _joinWaitActive = false;
                _matchResultReported = true;
                _matchFinalizing = true;
                Hashtable fail = new Hashtable();
                fail["Code"] = 0;
                fail["Message"] = "RankedMatchmakingError/WaitingForPlayers";
                fail["RpcExceptionCode"] = 0;

                Hashtable props = new Hashtable();
                props["MatchStarted"] = true;
                props["C2"] = (byte)201;
                props["RouteToGameStateId"] = (byte)0;
                props["GameFailedErrorMsg"] = fail;
                props["AbortReason"] = "PlayerDidNotConnect";
                props["Time"] = GetCurrentRoomTime();
                props["MatchEnded"] = true;
                props["match_ended"] = true;
                props["AllowReconnect"] = false;
                props["IsMatchFinished"] = true;
                try { EnsureFinalPlayers(props); } catch { }
                try { EnsureFinalResultProperties(props); } catch { }
                this.PluginHost.SetProperties(0, props, null, true);
                LogToFile("AbortIncompleteRoom: stats C2=201 then 202, JoinTimeout, no MMR.");
                ScheduleFinalPayloadPreload(150);
                ScheduleFinalStateTransition(202, 1200, "join-timeout");
                ScheduleFinalStateTransition(204, 10000, "join-timeout-close");
            }
            catch (Exception ex)
            {
                LogToFile($"AbortIncompleteRoom error: {ex.Message}");
            }
        }

        private void StartJoinWaitUi(int delayMs)
        {
            if (delayMs < 5000) delayMs = 5000;
            // Каждый новый игрок в комнате продлевает окно — таймер не с 1-го, а с последнего join.
            _joinDeadlineUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + delayMs;
            _joinWaitActive = true;
            double remain = delayMs / 1000.0;
            try
            {
                Hashtable props = new Hashtable();
                props["C2"] = (byte)10;
                props["RouteToGameStateId"] = (byte)10;
                props["MatchStarted"] = false;
                // Не пишем Time: клиент рисует его как разминку (1:40), пока второй ещё не зашёл.
                props["WaitPlayersTimeout"] = remain;
                props["WaitPlayersUi"] = "WaitingForPlayers";
                props["AllowReconnect"] = false;
                props["_ShowReconnect"] = false;
                props["ShowReconnect"] = false;
                this.PluginHost.SetProperties(0, props, null, true);
            }
            catch (Exception ex)
            {
                LogToFile($"StartJoinWaitUi set props error: {ex.Message}");
            }

            if (_joinWaitTickerStarted) return;
            _joinWaitTickerStarted = true;
            LogToFile($"Join-wait (no warmup clock): {remain:0}s C2=10 until all humans join.");
            this.PluginHost.CreateTimer(() =>
            {
                try
                {
                    if (!_joinWaitActive || _lobbyAborted || _matchFinalizing) return;
                    byte c2 = GetCurrentRoomC2();
                    if (c2 >= 11) { _joinWaitActive = false; return; }
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    double left = Math.Max(0.0, (_joinDeadlineUnixMs - now) / 1000.0);
                    if (left <= 0.0)
                    {
                        int loadedPlayers, needHumans, requiredPlayers, botSlots;
                        bool readyNow = HasEnoughHumansForStart(out loadedPlayers, out needHumans, out requiredPlayers, out botSlots);
                        if (c2 == 10 && !_hadLiveRound && !readyNow && !_matchFinalizing && !_lobbyAborted)
                        {
                            LogToFile($"Join-wait expired: only {loadedPlayers}/{needHumans} humans. Abort.");
                            AbortIncompleteRoom();
                        }
                        _joinWaitActive = false;
                        return;
                    }
                    Hashtable tick = new Hashtable();
                    tick["WaitPlayersTimeout"] = left;
                    if (c2 != 10 && c2 < 11)
                    {
                        tick["C2"] = (byte)10;
                        tick["RouteToGameStateId"] = (byte)10;
                    }
                    this.PluginHost.SetProperties(0, tick, null, true);
                }
                catch (Exception ex)
                {
                    LogToFile($"Join-wait tick error: {ex.Message}");
                }
            }, 1000, 1000);
        }

        private int CountActiveHumansWithUid()
        {
            return this.PluginHost.GameActors.Count(a =>
            {
                if (a == null || !a.IsActive) return false;
                object uVal;
                if (a.Properties != null && a.Properties.TryGetValue("uid", out uVal) && uVal != null
                    && !string.IsNullOrWhiteSpace(uVal.ToString()))
                    return true;
                // UserId Photon уже есть до client uid — иначе Actor2 не считается → соло.
                if (!string.IsNullOrWhiteSpace(a.UserId))
                    return true;
                object photonUserId;
                if (a.Properties != null && a.Properties.TryGetValue((byte)253, out photonUserId)
                    && photonUserId != null && !string.IsNullOrWhiteSpace(photonUserId.ToString()))
                    return true;
                return false;
            });
        }

        private string ReadMaxPlayersProp()
        {
            try
            {
                if (this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("max_players"))
                    return this.PluginHost.GameProperties["max_players"]?.ToString() ?? "?";
            }
            catch { }
            return "?";
        }

        private int GetRankedRequiredPlayers()
        {
            string gameId = "";
            try { gameId = this.PluginHost.GameId ?? ""; } catch { }
            if (gameId.StartsWith("Ranked2v2", StringComparison.OrdinalIgnoreCase))
                return 2;

            // max_players / HTTP раньше hardcoded 10 — иначе Defuse ждёт 10 людей при afk_bots.
            if (this.PluginHost.GameProperties != null)
            {
                if (this.PluginHost.GameProperties.ContainsKey("max_players"))
                {
                    try
                    {
                        int mp = Convert.ToInt32(this.PluginHost.GameProperties["max_players"]);
                        if (mp >= 2 && mp <= 10) return mp;
                    }
                    catch { }
                }
                if (this.PluginHost.GameProperties.ContainsKey("team_size"))
                {
                    try
                    {
                        int ts = Convert.ToInt32(this.PluginHost.GameProperties["team_size"]);
                        if (ts == 1) return 2;
                        if (ts == 2) return 4;
                    }
                    catch { }
                }
            }
            try
            {
                string roomId = this.PluginHost.GameId ?? "";
                if (!string.IsNullOrEmpty(roomId))
                {
                    var http = FetchRankedRoomPropsHttp(roomId);
                    if (http.MaxPlayers >= 2 && http.MaxPlayers <= 10) return http.MaxPlayers;
                    if (http.TeamSize == 1) return 2;
                    if (http.TeamSize == 2) return 4;
                }
            }
            catch { }

            if (gameId.StartsWith("RankedDefuse", StringComparison.OrdinalIgnoreCase)
                || gameId.StartsWith("ClanRanked", StringComparison.OrdinalIgnoreCase))
                return 10;

            string mode = "";
            if (this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("C0"))
                mode = this.PluginHost.GameProperties["C0"] as string ?? "";
            if (mode == "Ranked2v2") return 2;
            if (mode == "RankedDefuse" || mode == "ClanRankedDefuse") return 10;
            if (!string.IsNullOrEmpty(mode) && mode.IndexOf("Ranked", StringComparison.OrdinalIgnoreCase) >= 0)
                return 2;
            if (!string.IsNullOrEmpty(gameId) && gameId.IndexOf("Ranked", StringComparison.OrdinalIgnoreCase) >= 0)
                return 2;
            return 2;
        }

        private bool IsRankedRoomMode()
        {
            try
            {
                string gameId = this.PluginHost.GameId ?? "";
                if (gameId.StartsWith("Ranked", StringComparison.OrdinalIgnoreCase)
                    || gameId.StartsWith("ClanRanked", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            string mode = "";
            try
            {
                if (this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("C0"))
                    mode = this.PluginHost.GameProperties["C0"] as string ?? "";
            }
            catch { }
            return mode == "RankedDefuse" || mode == "Ranked2v2" || mode == "ClanRankedDefuse"
                || (!string.IsNullOrEmpty(mode) && mode.IndexOf("Ranked", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool HasEnoughHumansForStart(out int loaded, out int needHumans, out int required, out int bots)
        {
            bool isRanked = IsRankedRoomMode();
            required = isRanked ? GetRankedRequiredPlayers() : 1;
            if (isRanked && required < 2) required = 2;
            bots = GetAfkBotSlots();
            needHumans = Math.Max(1, required - bots);
            if (isRanked && bots <= 0 && needHumans < 2) needHumans = 2;
            loaded = CountActiveHumansWithUid();
            return loaded >= needHumans;
        }

        private void ScheduleCancelIfNotFull(int requiredPlayers, int delayMs)
        {
            if (_joinCancelScheduled) return;
            _joinCancelScheduled = true;
            LogToFile($"Scheduling cancel-if-not-full in {delayMs}ms (need {requiredPlayers} humans, afk_bots={GetAfkBotSlots()}).");
            this.PluginHost.CreateOneTimeTimer(() =>
            {
                try
                {
                    byte c2 = GetCurrentRoomC2();
                    int active = CountActiveHumansWithUid();
                    int bots = GetAfkBotSlots();
                    int effectiveNeed = Math.Max(1, requiredPlayers);
                    // Только WaitingPlayers (C2=10). Разминку/раунд НЕ рвём и MMR не трогаем.
                    if (c2 == 10 && !_hadLiveRound && active < effectiveNeed && !_matchFinalizing && !_lobbyAborted)
                    {
                        LogToFile($"Cancel match: only {active}/{effectiveNeed} humans joined (afk_bots={bots}) waiting (C2={c2}). Abort without scoring.");
                        AbortIncompleteRoom();
                    }
                    else
                    {
                        LogToFile($"Cancel-if-not-full skipped: active={active}/{effectiveNeed} afk_bots={bots} C2={c2}");
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"Cancel-if-not-full error: {ex}");
                }
            }, delayMs);
        }

        /// <summary>Слоты AFK-ботов: room props, иначе HTTP к RpcServer (клиент часто не копирует afk_bots).</summary>
        private int GetAfkBotSlots()
        {
            try
            {
                if (this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("afk_bots"))
                {
                    var v = this.PluginHost.GameProperties["afk_bots"];
                    if (v != null && int.TryParse(v.ToString(), out int n) && n > 0)
                    {
                        string rid = this.PluginHost.GameId ?? "";
                        if (!string.IsNullOrEmpty(rid)) _afkBotsCache[rid] = Math.Min(n, 9);
                        return Math.Min(n, 9);
                    }
                }
            }
            catch { }

            try
            {
                string roomId = this.PluginHost.GameId ?? "";
                if (string.IsNullOrEmpty(roomId)) return 0;
                if (_afkBotsCache.TryGetValue(roomId, out int cached) && cached > 0)
                    return cached;

                int bots = FetchAfkBotsHttp(roomId);
                if (bots > 0)
                {
                    _afkBotsCache[roomId] = bots;
                    LogToFile($"GetAfkBotSlots HTTP room={roomId} afk_bots={bots}");
                    // Докидываем в room props, чтобы дальше не зависеть от HTTP.
                    try
                    {
                        Hashtable props = new Hashtable();
                        props["afk_bots"] = bots;
                        this.PluginHost.SetProperties(0, props, null, true);
                    }
                    catch { }
                }
                return bots;
            }
            catch (Exception ex)
            {
                LogToFile($"GetAfkBotSlots HTTP fail: {ex.Message}");
            }
            return 0;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _afkBotsCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private bool IsListedSpectator(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId) || this.PluginHost.GameProperties == null)
                return false;
            try
            {
                if (!this.PluginHost.GameProperties.ContainsKey("spectator_ids"))
                    return false;
                string raw = this.PluginHost.GameProperties["spectator_ids"] as string ?? "";
                if (string.IsNullOrWhiteSpace(raw)) return false;
                foreach (string part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (string.Equals(part.Trim(), userId, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private byte GetExistingRequestTeam(IJoinGameCallInfo info)
        {
            try
            {
                var typedInfo = info as ITypedCallInfo<IJoinGameRequest>;
                if (typedInfo != null && typedInfo.Request != null && typedInfo.Request.ActorProperties != null)
                {
                    if (typedInfo.Request.ActorProperties.ContainsKey("team"))
                    {
                        var teamObj = typedInfo.Request.ActorProperties["team"];
                        if (teamObj != null)
                        {
                            return Convert.ToByte(teamObj);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"GetExistingRequestTeam error: {ex}");
                this.PluginHost.LogError($"MatchmakingPlugin: Error in GetExistingRequestTeam: {ex}");
            }
            return TEAM_NONE;
        }

        private void SetJoinTeamProperty(IJoinGameCallInfo info, byte team)
        {
            try
            {
                var typedInfo = info as ITypedCallInfo<IJoinGameRequest>;
                if (typedInfo != null && typedInfo.Request != null)
                {
                    if (typedInfo.Request.ActorProperties == null)
                    {
                        typedInfo.Request.ActorProperties = new Hashtable();
                    }
                    typedInfo.Request.ActorProperties["team"] = team;
                    LogToFile($"SetJoinTeamProperty: Set join request team to {team} for Actor {info.ActorNr}");
                    this.PluginHost.LogInfo($"MatchmakingPlugin: Set join request team to {team} for Actor {info.ActorNr}");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"SetJoinTeamProperty error: {ex}");
                this.PluginHost.LogError($"MatchmakingPlugin: Error in SetJoinTeamProperty: {ex}");
            }
        }

        public override void OnLeave(ILeaveGameCallInfo info)
        {
            this.PluginHost.LogInfo($"MatchmakingPlugin: OnLeave - Actor: {info.ActorNr} PlayerId: {info.UserId}");
            try
            {
                var leaving = this.PluginHost.GameActors.FirstOrDefault(a => a != null && a.ActorNr == info.ActorNr);
                if (leaving != null) RememberTrackedPlayer(leaving);
                else if (!string.IsNullOrEmpty(info.UserId))
                    RememberTrackedPlayerId(info.UserId, info.ActorNr);
            }
            catch { }
            info.Continue();
            bool skipLeaveStatus = false;
            try
            {
                string leaveMode = this.PluginHost.GameProperties.ContainsKey("C0")
                    ? this.PluginHost.GameProperties["C0"] as string ?? ""
                    : "";
                byte leaveC2 = GetCurrentRoomC2();
                if (IsRankedMode(leaveMode) && leaveC2 >= 201 && leaveC2 <= 210)
                    skipLeaveStatus = true;
            }
            catch { }
            if (!skipLeaveStatus)
                ReportStatus(info.UserId, this.PluginHost.GameId, true, "");
            HandleLeaveDuringVote(info.ActorNr);

            try
            {
                string mode = this.PluginHost.GameProperties.ContainsKey("C0")
                    ? this.PluginHost.GameProperties["C0"] as string ?? ""
                    : "";
                if (!IsRankedMode(mode)) return;

                byte c2 = GetCurrentRoomC2();
                int required = GetRankedRequiredPlayers();
                int needHumans = required;
                int active = CountActiveHumansWithUid();
                if (c2 == 10 && !_hadLiveRound && active < needHumans && !_matchFinalizing && !_lobbyAborted)
                {
                    LogToFile($"OnLeave before WarmUp: {active}/{needHumans} humans left (full={required} bots={GetAfkBotSlots()}). Scheduling quick cancel.");
                    ScheduleCancelIfNotFull(needHumans, 5000);
                }
            }
            catch (Exception ex)
            {
                LogToFile($"OnLeave cancel-check error: {ex}");
            }
        }

        private void ReportStatus(string playerId, string roomId, bool isLeaving, string mode)
        {
            if (string.IsNullOrEmpty(playerId)) return;

            try
            {
                var request = new HttpRequest
                {
                    Url = $"{serverUrl}/api/player/status",
                    Method = "POST",
                    Accept = "application/json",
                    ContentType = "application/json",
                    DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"PlayerId\":\"" + playerId + "\", \"RoomId\":\"" + roomId + "\", \"IsLeaving\":" + isLeaving.ToString().ToLower() + ", \"Mode\":\"" + mode + "\"}")),
                    Callback = (response, userState) => {
                        if (response.Status != HttpRequestQueueResult.Success || response.HttpCode != 200)
                        {
                            this.PluginHost.LogWarning($"MatchmakingPlugin: ReportStatus failed for {playerId}. Status: {response.Status}, HttpCode: {response.HttpCode}");
                        }
                    }
                };

                this.PluginHost.HttpRequest(request);
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: Error reporting status: {ex}");
            }
        }

        public override void OnRaiseEvent(IRaiseEventCallInfo info)
        {
            byte ev = info.Request.EvCode;
            if (ev == EVT_INIT_VOTING_REQUEST || ev == EVT_VOTE_REQUEST)
            {
                HandleVoteEvent(info);
                return;
            }

            if (ev == EVT_GAME_EVENT)
            {
                HandleMissionEvent(info);
            }
            else if (ev == EVT_CHANGE_TEAM_REQUEST)
            {
                HandleChangeTeamRequest(info);
                return;
            }

            info.Continue();
        }

        private static Dictionary<byte, object> ToPhotonEventData(object data)
        {
            var result = new Dictionary<byte, object>();
            if (data == null) return result;
            if (data is Dictionary<byte, object> dict)
            {
                foreach (var kv in dict) result[kv.Key] = kv.Value;
                return result;
            }
            var ht = data as Hashtable;
            if (ht != null)
            {
                foreach (DictionaryEntry e in ht)
                {
                    try { result[Convert.ToByte(e.Key)] = e.Value; }
                    catch { }
                }
                if (result.Count > 0) return result;
            }
            var map = data as IDictionary;
            if (map != null)
            {
                foreach (DictionaryEntry e in map)
                {
                    try { result[Convert.ToByte(e.Key)] = e.Value; }
                    catch { }
                }
                if (result.Count > 0) return result;
            }
            result[245] = data;
            return result;
        }

        private void HandleVoteEvent(IRaiseEventCallInfo info)
        {
            byte ev = info.Request.EvCode;
            int hint = ExtractVoteHint(info.Request.Data);
            int actorNr = info.ActorNr;
            byte team = GetActorTeamWithFallback(actorNr);
            LogToFile($"Vote EvCode={ev} actor={actorNr} team={team} hint={hint} activeType={_activeVoteType} raw={DescribeVoteData(info.Request.Data)}");

            try
            {
                if (ev == EVT_INIT_VOTING_REQUEST)
                {
                    if (hint == 0)
                        hint = GuessVoteTypeFromPayload(info.Request.Data);
                    if (hint == 0)
                    {
                        LogToFile($"Vote init ignored hint=0 raw={DescribeVoteData(info.Request.Data)}");
                    }
                    else if (_activeVoteType != 0 && _activeVoteType != hint)
                    {
                        LogToFile($"Vote init ignored: already running type={_activeVoteType}");
                    }
                    else if (_activeVoteType == 0)
                    {
                        byte voteTeam = team != 0 ? team : GetActorTeamWithFallback(actorNr);
                        if (hint == 3 && (_pauseActive || TeamAlreadyTookPause(voteTeam)))
                        {
                            LogToFile($"Vote init rejected: pause unavailable team={voteTeam} active={_pauseActive} took={TeamAlreadyTookPause(voteTeam)}");
                            _activeVoteType = hint;
                            _voteTeam = voteTeam;
                            _voteInitiatorActor = actorNr;
                            _voteYesActors.Clear();
                            _voteNoActors.Clear();
                            PublishVoteRequest(false);
                            RelayVoteToAllActors(info);
                            _activeVoteType = 0;
                            return;
                        }
                        StartTeamVote(hint, actorNr, team, ExtractVoteTargetUserId(info.Request.Data));
                    }
                    else
                        RegisterVote(actorNr, yes: true);
                }
                else if (ev == EVT_VOTE_REQUEST)
                {
                    bool yes = hint != 0;
                    RegisterVote(actorNr, yes);
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Vote handle error: {ex.Message}");
            }

            if (ev == EVT_INIT_VOTING_REQUEST && _activeVoteType != 0)
            {
                RelayVoteToAllActors(info);
                return;
            }
            if (ev == EVT_VOTE_REQUEST && _activeVoteType != 0)
            {
                RelayVoteToAllActors(info);
                return;
            }
            info.Continue();
        }

        private static string DescribeVoteData(object data)
        {
            try
            {
                var dict = ToPhotonEventData(data);
                var parts = new List<string>();
                foreach (var kv in dict)
                {
                    object v = kv.Value;
                    string text;
                    if (v == null) text = "null";
                    else if (v is Array arr)
                    {
                        var items = new List<string>();
                        foreach (var el in arr) items.Add(el == null ? "null" : el.ToString());
                        text = "[" + string.Join(",", items) + "]";
                    }
                    else text = v.GetType().Name + ":" + v;
                    parts.Add(kv.Key + "=" + text);
                }
                return "{" + string.Join(" ", parts) + "}";
            }
            catch (Exception ex) { return "<" + ex.Message + ">"; }
        }

        private void RelayVoteToAllActors(IRaiseEventCallInfo info)
        {
            var data = ToPhotonEventData(info.Request.Data);
            var receivers = new List<int>();
            foreach (var actor in this.PluginHost.GameActors)
            {
                if (actor != null && actor.IsActive)
                    receivers.Add(actor.ActorNr);
            }
            if (receivers.Count == 0)
                receivers.Add(info.ActorNr);

            this.PluginHost.BroadcastEvent(
                recieverActors: receivers,
                senderActor: info.ActorNr,
                evCode: info.Request.EvCode,
                data: data,
                cacheOp: 0,
                sendParameters: default(SendParameters)
            );
            info.Cancel();
            LogToFile($"Vote BroadcastEvent to {receivers.Count} actors EvCode={info.Request.EvCode}");
        }

        private void StartTeamVote(int voteType, int initiatorActor, byte team, string kickTargetUserId = "")
        {
            _voteInitUiSent = false;
            _activeVoteType = voteType;
            _voteKickTargetUserId = kickTargetUserId ?? "";
            _voteTeam = team != 0 ? team : GetActorTeamWithFallback(initiatorActor);
            _voteInitiatorActor = initiatorActor;
            _voteYesActors.Clear();
            _voteNoActors.Clear();
            _voteYesActors.Add(initiatorActor);
            _voteStartedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            double nowRoom = GetCurrentRoomTime();
            if (nowRoom < 1.0) nowRoom = 1.0;
            _voteEndRoomTime = nowRoom + (VoteTimeoutMs / 1000.0);
            _voteApplyScheduled = false;
            if (voteType == 2)
                _surrenderTeam = _voteTeam;
            PublishVoteRequest(false);
            BroadcastVoteInitEvent(voteType, initiatorActor);
            LogToFile($"Vote started type={voteType} team={_voteTeam} initiator={initiatorActor} need={GetVoteNeed()} kickTarget='{_voteKickTargetUserId}' endRoom={_voteEndRoomTime:0.0}");
            ScheduleVoteApplyCheck(VoteMinVisibleMs + 200);
            AutoYesTeammateBots();
        }

        /// <summary>Плашка «X начал голосование / Сдаться» — room props + raise event 100 всем.</summary>
        private void BroadcastVoteInitEvent(int voteType, int initiatorActor)
        {
            try
            {
                var receivers = new List<int>();
                foreach (var actor in this.PluginHost.GameActors)
                {
                    if (actor != null && actor.IsActive)
                        receivers.Add(actor.ActorNr);
                }
                if (receivers.Count == 0)
                    receivers.Add(initiatorActor);

                float durationSec = (float)Math.Max(1.0, VoteTimeoutMs / 1000.0);
                double endRoom = _voteEndRoomTime > 1.0 ? _voteEndRoomTime : (GetCurrentRoomTime() + durationSec);
                float endTimeF = (float)endRoom;

                var payload = new Dictionary<byte, object>();
                payload[245] = new object[] { voteType, durationSec, endTimeF, _voteInitiatorActor };
                payload[0] = (byte)voteType;
                payload[1] = endTimeF;
                payload[2] = durationSec;
                payload[3] = voteType;
                payload[4] = _voteInitiatorActor;
                this.PluginHost.BroadcastEvent(
                    recieverActors: receivers,
                    senderActor: initiatorActor,
                    evCode: EVT_INIT_VOTING_REQUEST,
                    data: payload,
                    cacheOp: 0,
                    sendParameters: default(SendParameters));
                LogToFile($"Vote UI broadcast init type={voteType} to {receivers.Count} actors");

                // Часть клиентов слушает 101 (обновление голосования), а не только room props.
                var progress = new Dictionary<byte, object>();
                progress[245] = new object[] { 1 };
                progress[0] = (byte)1;
                progress[3] = voteType;
                this.PluginHost.BroadcastEvent(
                    recieverActors: receivers,
                    senderActor: initiatorActor,
                    evCode: EVT_VOTE_REQUEST,
                    data: progress,
                    cacheOp: 0,
                    sendParameters: default(SendParameters));
            }
            catch (Exception ex)
            {
                LogToFile($"BroadcastVoteInitEvent error: {ex.Message}");
            }
        }

        private void RegisterVote(int actorNr, bool yes)
        {
            if (_activeVoteType == 0) return;
            // Голосуют только члены команды-инициатора — иначе рассинхрон и чужие «За».
            byte voterTeam = GetActorTeamWithFallback(actorNr);
            if (_voteTeam != 0 && voterTeam != 0 && voterTeam != _voteTeam)
            {
                LogToFile($"Vote ignored from enemy actor={actorNr} team={voterTeam} voteTeam={_voteTeam}");
                return;
            }
            if (yes)
            {
                _voteNoActors.Remove(actorNr);
                _voteYesActors.Add(actorNr);
            }
            else
            {
                _voteYesActors.Remove(actorNr);
                _voteNoActors.Add(actorNr);
            }
            PublishVoteRequest(false);
            TryApplyVoteResult(force: false);
        }

        private int GetVoteNeed()
        {
            int teamSize = 0;
            foreach (var actor in this.PluginHost.GameActors)
            {
                if (actor == null || !actor.IsActive) continue;
                if (GetActorTeamWithFallback(actor, actor.ActorNr) == _voteTeam)
                    teamSize++;
            }
            if (teamSize <= 0) teamSize = 1;
            // Союзники (2 из 2) и 1v1 (1 из 1) — нужны голоса ВСЕЙ команды.
            // В 5v5 остаётся большинство, иначе сдаться было бы невозможно.
            if (teamSize <= 2) return teamSize;
            return (teamSize / 2) + 1;
        }

        private void ScheduleVoteApplyCheck(int delayMs)
        {
            if (_voteApplyScheduled) return;
            _voteApplyScheduled = true;
            this.PluginHost.CreateOneTimeTimer(() => {
                try
                {
                    _voteApplyScheduled = false;
                    TryApplyVoteResult(force: true);
                }
                catch (Exception ex) { LogToFile($"Vote apply timer error: {ex.Message}"); }
            }, delayMs);
        }

        private void AutoYesTeammateBots()
        {
            this.PluginHost.CreateOneTimeTimer(() => {
                try
                {
                    if (_activeVoteType == 0) return;
                    foreach (var actor in this.PluginHost.GameActors)
                    {
                        if (actor == null || !actor.IsActive) continue;
                        if (!IsPhotonBotActor(actor)) continue;
                        if (GetActorTeamWithFallback(actor, actor.ActorNr) != _voteTeam) continue;
                        if (_voteYesActors.Contains(actor.ActorNr)) continue;
                        _voteYesActors.Add(actor.ActorNr);
                        LogToFile($"Vote bot auto-yes actor={actor.ActorNr}");
                    }
                    PublishVoteRequest(false);
                    TryApplyVoteResult(force: false);
                }
                catch (Exception ex) { LogToFile($"Vote bot auto-yes error: {ex.Message}"); }
            }, 2500);
        }

        private bool IsPhotonBotActor(IActor actor)
        {
            try
            {
                if (actor?.Properties != null)
                {
                    object botObj;
                    if (actor.Properties.TryGetValue("bot", out botObj) && botObj != null && Convert.ToBoolean(botObj))
                        return true;
                    object uidObj;
                    if (actor.Properties.TryGetValue("uid", out uidObj) && uidObj != null)
                    {
                        string uid = uidObj.ToString();
                        if (uid.StartsWith("BOT_", StringComparison.OrdinalIgnoreCase)
                            || uid.StartsWith("AFK_", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void TryApplyVoteResult(bool force)
        {
            if (_activeVoteType == 0) return;
            int need = GetVoteNeed();
            int yes = _voteYesActors.Count;
            int no = _voteNoActors.Count;
            long elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _voteStartedAtMs;
            LogToFile($"Vote tally type={_activeVoteType} team={_voteTeam} yes={yes}/{need} no={no} elapsed={elapsed}ms force={force}");

            if (yes < need)
            {
                if (elapsed >= VoteTimeoutMs)
                {
                    LogToFile("Vote timed out without majority — действие не применяется.");
                    ClearActiveVote(failed: true);
                    return;
                }
                // Раньше проверка планировалась ровно один раз: если голосов не хватило,
                // голосование зависало активным навсегда и блокировало все следующие.
                // Теперь перепроверяем до самого дедлайна и корректно закрываем плашку.
                ScheduleVoteApplyCheck(VoteRecheckMs);
                return;
            }

            // Плашку голосования нужно реально успеть увидеть: раньше результат
            // применялся уже через 3с и «Сдаться» срабатывало почти мгновенно.
            if (!force && elapsed < VoteMinVisibleMs)
            {
                ScheduleVoteApplyCheck(VoteMinVisibleMs - (int)elapsed);
                return;
            }

            int voteType = _activeVoteType;
            if (voteType == 3)
                ApplyPauseVote();
            else if (voteType == 2)
                ApplyGiveUpVote();
            else
                ApplyKickVote();
        }

        /// <summary>
        /// Любое другое голосование команды (в клиенте это «исключить игрока»).
        /// Плашка показывается так же, как у сдачи и паузы; выкидываем игрока только
        /// если цель однозначно нашлась в комнате — иначе просто гасим голосование,
        /// чтобы случайно не удалить не того.
        /// </summary>
        private void ApplyKickVote()
        {
            string target = _voteKickTargetUserId;
            int voteType = _activeVoteType;
            PublishVoteRequest(true);
            ClearActiveVote(failed: false);

            if (string.IsNullOrWhiteSpace(target))
            {
                LogToFile($"Vote: type={voteType} принято, но цель не указана — ничего не применяем.");
                return;
            }

            IActor victim = null;
            foreach (var actor in this.PluginHost.GameActors)
            {
                if (actor == null || !actor.IsActive) continue;
                string uid = !string.IsNullOrWhiteSpace(actor.UserId) ? actor.UserId : GetActorUserId(actor.ActorNr);
                if (string.Equals(uid, target, StringComparison.OrdinalIgnoreCase)) { victim = actor; break; }
            }
            if (victim == null)
            {
                LogToFile($"Vote: kick '{target}' — игрок уже не в комнате, ничего не делаем.");
                return;
            }
            try
            {
                LogToFile($"Vote: kick принят — удаляем actor={victim.ActorNr} uid='{target}'.");
                this.PluginHost.RemoveActor(victim.ActorNr, "VoteKick");
            }
            catch (Exception ex) { LogToFile($"Vote kick RemoveActor failed: {ex.Message}"); }
        }

        /// <summary>
        /// В событии голосования поле 1 — строка с UserId игрока, которого предлагают
        /// исключить (для сдачи и паузы она пустая).
        /// </summary>
        private static string ExtractVoteTargetUserId(object data)
        {
            try
            {
                var dict = ToPhotonEventData(data);
                foreach (var kv in dict)
                {
                    if (kv.Value is string str && !string.IsNullOrWhiteSpace(str))
                        return str.Trim();
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Игрок вышел во время голосования: его голос больше не учитывается, кворум
        /// пересчитывается по живым игрокам команды, плашка обновляется. Если вышел
        /// инициатор или вся команда — голосование гасим, чтобы оно не висело вечно.
        /// </summary>
        private void HandleLeaveDuringVote(int actorNr)
        {
            try
            {
                if (_activeVoteType == 0) return;
                bool wasInitiator = actorNr == _voteInitiatorActor;
                _voteYesActors.Remove(actorNr);
                _voteNoActors.Remove(actorNr);

                int teamLeft = 0;
                foreach (var actor in this.PluginHost.GameActors)
                {
                    if (actor == null || !actor.IsActive || actor.ActorNr == actorNr) continue;
                    if (GetActorTeamWithFallback(actor, actor.ActorNr) == _voteTeam) teamLeft++;
                }

                if (wasInitiator || teamLeft <= 0)
                {
                    LogToFile($"Vote cancelled: actor={actorNr} вышел (initiator={wasInitiator}, осталось в команде {teamLeft}).");
                    ClearActiveVote(failed: true);
                    return;
                }

                LogToFile($"Vote: actor={actorNr} вышел, пересчитываем кворум (осталось {teamLeft}).");
                PublishVoteRequest(false);
                TryApplyVoteResult(force: false);
            }
            catch (Exception ex) { LogToFile($"Vote leave handling error: {ex.Message}"); }
        }

        private bool TeamAlreadyTookPause(byte team)
        {
            return team != 0 && _teamsTookPause.Contains(team);
        }

        private void MarkTeamTookPause(byte team)
        {
            if (team == 0) return;
            _teamsTookPause.Add(team);
            try
            {
                Hashtable props = new Hashtable();
                string key = team == TEAM_CT ? "ct_took_pause" : "tr_took_pause";
                props[key] = true;
                this.PluginHost.SetProperties(0, props, null, true);
            }
            catch { }
        }

        private void ApplyPauseVote()
        {
            LogToFile($"Vote: Pause majority — applying tactical pause ({TacticalPauseDurationMs / 1000}s).");
            MarkTeamTookPause(_voteTeam);
            _pausePending = true;
            _pendingPauseTeam = _voteTeam;
            _pendingPauseEndUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + TacticalPauseDurationMs;

            byte current = GetCurrentRoomC2();
            if (current != 40)
                _c2BeforePause = current;
            if (_c2BeforePause < 22)
                _c2BeforePause = 31;

            PublishPauseBanner(_pendingPauseTeam, TacticalPauseDurationMs, active: false);
            PublishVoteRequest(true);
            ClearActiveVote(failed: false);

            // Синхронно у всех: если раунд уже идёт (или freeze) — ставим паузу сразу.
            // Иначе ждём C2=22 (старт раунда) и активируем там.
            if (current >= 22 && current != 40)
                TryActivatePendingPauseOnRoundStart();
            else if (current == 22)
                TryActivatePendingPauseOnRoundStart();
        }

        private void TryActivatePendingPauseOnRoundStart()
        {
            if (!_pausePending || _pauseActive) return;
            _pausePending = false;
            ActivateTacticalPause(_pendingPauseTeam);
        }

        private void ActivateTacticalPause(byte pauseTeam)
        {
            LogToFile($"Tactical pause activated team={pauseTeam} for {TacticalPauseDurationMs / 1000}s.");
            _pauseActive = true;
            _pendingPauseTeam = pauseTeam;

            Hashtable props = new Hashtable();
            props["C2"] = (byte)40;
            props["RouteToGameStateId"] = (byte)40;
            props["_Pause"] = true;
            this.PluginHost.SetProperties(0, props, null, true);
            PublishPauseBanner(pauseTeam, TacticalPauseDurationMs, active: true);

            this.PluginHost.CreateOneTimeTimer(() => {
                try { ResumeFromPause(); }
                catch (Exception ex) { LogToFile($"Pause resume error: {ex.Message}"); }
            }, TacticalPauseDurationMs);
        }

        private void PublishPauseBanner(byte pauseTeam, int durationMs, bool active)
        {
            try
            {
                long endMs = active
                    ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + durationMs
                    : _pendingPauseEndUnixMs;
                string teamLabel = pauseTeam == TEAM_CT ? "Спецназ" : "Террористы";
                Hashtable props = new Hashtable();
                props["pause_team"] = (int)pauseTeam;
                props["pause_team_label"] = teamLabel;
                props["pause_end_unix_ms"] = endMs;
                props["pause_duration_sec"] = durationMs / 1000;
                props["pause_pending"] = !active;
                props["pause_active"] = active;
                this.PluginHost.SetProperties(0, props, null, true);
                LogToFile($"Pause banner team={teamLabel} active={active} duration={durationMs / 1000}s");
            }
            catch (Exception ex) { LogToFile($"PublishPauseBanner error: {ex.Message}"); }
        }

        private void ResumeFromPause()
        {
            if (!_pauseActive) return;
            _pauseActive = false;
            byte restore = _c2BeforePause >= 22 && _c2BeforePause != 40 ? _c2BeforePause : (byte)31;
            Hashtable props = new Hashtable();
            props["C2"] = restore;
            props["RouteToGameStateId"] = restore;
            props["_Pause"] = false;
            props["pause_active"] = false;
            props["pause_pending"] = false;
            props["vote_request"] = null;
            this.PluginHost.SetProperties(0, props, null, true);
            LogToFile($"Pause ended, restored C2={restore}");
        }

        private void ApplyGiveUpVote()
        {
            _rejoinBlocked = true;
            _matchFinalizing = true;
            _surrenderTeam = _voteTeam != 0 ? _voteTeam : GetActorTeamWithFallback(_voteInitiatorActor);
            LogToFile($"Vote: GiveUp majority — surrender team={_surrenderTeam}, winner is enemy, rejoin blocked.");
            PublishVoteRequest(true);
            ClearActiveVote(failed: false);
            if (!_hadLiveRound)
                _hadLiveRound = true;
            _giveUpFinalization = true;
            PulseGiveUpFinalStateForAll();
            ReportMatchResultOnce();
            ScheduleServerOwnedFinalization();
            ScheduleRoomCloseAfterGiveUp();
            ScheduleForceLeaveAfterGiveUp();
            this.PluginHost.CreateOneTimeTimer(() => {
                try { SyncFinalRankAssignmentResults(); }
                catch (Exception ex) { LogToFile("Give-up rank sync error: " + ex.Message); }
            }, 2500);
        }

        /// <summary>
        /// Сдача: даём клиентам увидеть статистику (202) и начисление звания (203→204),
        /// после чего закрываем комнату и выкидываем всех. Вернуться в матч нельзя.
        /// </summary>
        /// <summary>Сдача: проталкиваем C2=201 всем (в т.ч. оппоненту), без реконнекта.</summary>
        private void PulseGiveUpFinalStateForAll()
        {
            for (int pulse = 0; pulse < 4; pulse++)
            {
                int delayMs = pulse * 350;
                this.PluginHost.CreateOneTimeTimer(() =>
                {
                    try
                    {
                        if (!_rejoinBlocked) return;
                        Hashtable props = new Hashtable();
                        props["IsGiveUp"] = true;
                        props["isGiveUp"] = true;
                        props["MatchEnded"] = true;
                        props["match_ended"] = true;
                        props["AllowReconnect"] = false;
                        props["allow_reconnect"] = false;
                        props["_ShowReconnect"] = false;
                        props["ShowReconnect"] = false;
                        props["IsMatchFinished"] = true;
                        props["vote_request"] = null;
                        props["vote"] = null;
                        EnsureFinalPlayers(props);
                        EnsureFinalResultProperties(props);
                        props["C2"] = (byte)201;
                        props["RouteToGameStateId"] = (byte)0;
                        props["Time"] = GetCurrentRoomTime();
                        this.PluginHost.SetProperties(0, props, null, true);
                        foreach (var actor in this.PluginHost.GameActors)
                        {
                            if (actor == null || !actor.IsActive) continue;
                            Hashtable ap = new Hashtable();
                            ap["AllowReconnect"] = false;
                            ap["allow_reconnect"] = false;
                            ap["vote_request"] = null;
                            this.PluginHost.SetProperties(actor.ActorNr, ap, null, true);
                        }
                    }
                    catch (Exception ex) { LogToFile("PulseGiveUpFinalState error: " + ex.Message); }
                }, delayMs);
            }
        }

        /// <summary>После экрана статистики — выкидываем из комнаты без «Переподключиться».</summary>
        private void ScheduleForceLeaveAfterGiveUp()
        {
            this.PluginHost.CreateOneTimeTimer(() =>
            {
                try { ForceLeaveRoomAfterGiveUp(); }
                catch (Exception ex) { LogToFile("ForceLeaveAfterGiveUp error: " + ex.Message); }
            }, GiveUpForceLeaveMs);
        }

        private void ForceLeaveRoomAfterGiveUp()
        {
            _rejoinBlocked = true;
            _matchFinalizing = true;
            try
            {
                Hashtable props = new Hashtable();
                props["C2"] = (byte)204;
                props["RouteToGameStateId"] = (byte)0;
                props["MatchEnded"] = true;
                props["match_ended"] = true;
                props["IsMatchFinished"] = true;
                props["AllowReconnect"] = false;
                props["allow_reconnect"] = false;
                props["_ShowReconnect"] = false;
                props["ShowReconnect"] = false;
                props["IsGiveUp"] = true;
                props["isGiveUp"] = true;
                this.PluginHost.SetProperties(0, props, null, true);
            }
            catch { }

            var actors = this.PluginHost.GameActors
                .Where(a => a != null && a.IsActive)
                .Select(a => a.ActorNr)
                .ToList();
            LogToFile($"GiveUp force-leave: kicking {actors.Count} actors (no reconnect UI).");
            foreach (int actorNr in actors)
            {
                try { this.PluginHost.RemoveActor(actorNr, "GiveUpLeave"); }
                catch (Exception ex) { LogToFile($"GiveUp RemoveActor {actorNr}: {ex.Message}"); }
            }
        }

        private void ScheduleRoomCloseAfterGiveUp()
        {
            if (_roomCloseScheduled) return;
            _roomCloseScheduled = true;

            // 204 (FinalState) на сдаче выставляется через GiveUpFinalStateDelayMs.
            // Раньше кик шёл через 24с — клиент не успевал показать звание/уровень и
            // выдачу наград, и вместо этого уходил в «переподключение к серверу».
            // Теперь ждём, пока клиент сам доиграет финальный экран и выйдет;
            // RemoveActor остаётся лишь страховкой для тех, кто завис в комнате.
            // Дополнительно подтягиваем rank props ещё раз перед 203, чтобы экран
            // «Звание и уровень» не открывался пустым.
            this.PluginHost.CreateOneTimeTimer(() => {
                try { SyncFinalRankAssignmentResults(); }
                catch (Exception ex) { LogToFile("Give-up pre-203 rank sync error: " + ex.Message); }
            }, 6000);
            this.PluginHost.CreateOneTimeTimer(() => {
                try { CloseRoomAndDisconnectAll("give_up"); }
                catch (Exception ex) { LogToFile("Give-up room close error: " + ex.Message); }
            }, GiveUpRoomCloseDelayMs);
        }

        private void CloseRoomAndDisconnectAll(string reason)
        {
            _rejoinBlocked = true;

            byte c2 = GetCurrentRoomC2();
            int stillHere = this.PluginHost.GameActors.Count(a => a != null && a.IsActive);
            // Финал / сдача: НИКОГДА не RemoveActor — APK рисует «Переподключиться»/
            // «Отключиться» именно на Photon disconnect, а не на кастомных props
            // (AllowReconnect/ShowReconnect в libil2cpp нет).
            bool finalScreen = c2 >= 201 || _giveUpFinalization || _matchFinalizing
                || string.Equals(reason, "give_up", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "match_finished", StringComparison.OrdinalIgnoreCase);
            if (finalScreen)
            {
                LogToFile($"Closing room ({reason}): C2={c2}, stillHere={stillHere} — NEVER RemoveActor (wait Continue→LeaveRoom).");
                try
                {
                    Hashtable closeProps = new Hashtable();
                    closeProps["MatchEnded"] = true;
                    closeProps["match_ended"] = true;
                    closeProps["AllowReconnect"] = false;
                    closeProps["allow_reconnect"] = false;
                    closeProps["_ShowReconnect"] = false;
                    closeProps["ShowReconnect"] = false;
                    closeProps["IsMatchFinished"] = true;
                    if (_giveUpFinalization)
                    {
                        closeProps["IsGiveUp"] = true;
                        closeProps["isGiveUp"] = true;
                    }
                    this.PluginHost.SetProperties(0, closeProps, null, true);
                }
                catch (Exception ex) { LogToFile($"Closing room props error: {ex.Message}"); }
                return;
            }

            var actorNumbers = this.PluginHost.GameActors
                .Where(a => a != null && a.IsActive)
                .Select(a => a.ActorNr)
                .ToList();

            if (actorNumbers.Count == 0)
            {
                LogToFile($"Closing room ({reason}): все уже вышли сами, кикать некого.");
                return;
            }

            LogToFile($"Closing room ({reason}): removing {actorNumbers.Count} actors (non-final C2={c2}).");
            foreach (int actorNr in actorNumbers)
            {
                try { this.PluginHost.RemoveActor(actorNr, "MatchFinished/" + reason); }
                catch (Exception ex) { LogToFile($"RemoveActor {actorNr} failed: {ex.Message}"); }
            }
        }

        private void ClearActiveVote(bool failed)
        {
            int clearedType = _activeVoteType;
            int clearedInitiator = _voteInitiatorActor;
            _activeVoteType = 0;
            _voteTeam = 0;
            _voteInitiatorActor = 0;
            _voteKickTargetUserId = "";
            _voteEndRoomTime = 0;
            _voteYesActors.Clear();
            _voteNoActors.Clear();
            _voteApplyScheduled = false;
            _voteInitUiSent = false;
            int clearDelayMs = failed ? 0 : 3500;
            this.PluginHost.CreateOneTimeTimer(() =>
            {
                try
                {
                    Hashtable props = new Hashtable();
                    props["vote_request"] = null;
                    props["vote"] = null;
                    props["vote_type"] = 0;
                    props["vote_yes"] = new string[0];
                    props["vote_no"] = new string[0];
                    props["vote_pending"] = new string[0];
                    this.PluginHost.SetProperties(0, props, null, true);
                    foreach (var actor in this.PluginHost.GameActors)
                    {
                        if (actor == null) continue;
                        Hashtable actorProps = new Hashtable();
                        actorProps["vote_request"] = null;
                        this.PluginHost.SetProperties(actor.ActorNr, actorProps, null, true);
                    }
                    LogToFile($"Vote UI cleared failed={failed} type={clearedType} initiator={clearedInitiator}");
                }
                catch { }
            }, clearDelayMs);
        }

        private string GetActorUserId(int actorNr)
        {
            var actor = this.PluginHost.GameActors.FirstOrDefault(a => a.ActorNr == actorNr);
            if (actor == null) return "";
            return GetActorDisplayId(actor);
        }

        /// <summary>
        /// Id для UI голосования: клиент рисует ники/иконки по prop uid
        /// (TEST_01), а не по Photon UserId (Mongo ObjectId).
        /// </summary>
        private static string GetActorDisplayId(IActor actor)
        {
            if (actor == null) return "";
            try
            {
                object uidObj;
                if (actor.Properties != null && actor.Properties.TryGetValue("uid", out uidObj) && uidObj != null)
                {
                    string uid = uidObj.ToString();
                    if (!string.IsNullOrWhiteSpace(uid)) return uid.Trim();
                }
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(actor.UserId)) return actor.UserId;
            return actor.ActorNr.ToString();
        }

        private void PublishVoteRequest(bool succeeded)
        {
            try
            {
                var yesIds = new List<string>();
                var noIds = new List<string>();
                var pendingIds = new List<string>();
                foreach (var actor in this.PluginHost.GameActors)
                {
                    if (actor == null || !actor.IsActive) continue;
                    if (_voteTeam != 0 && GetActorTeamWithFallback(actor, actor.ActorNr) != _voteTeam)
                        continue;
                    string id = GetActorDisplayId(actor);
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (_voteYesActors.Contains(actor.ActorNr))
                    {
                        if (!yesIds.Contains(id)) yesIds.Add(id);
                    }
                    else if (_voteNoActors.Contains(actor.ActorNr))
                    {
                        if (!noIds.Contains(id)) noIds.Add(id);
                    }
                    else if (!pendingIds.Contains(id))
                        pendingIds.Add(id);
                }

                double now = GetCurrentRoomTime();
                if (now < 1.0) now = 1.0;
                double endTime = _voteEndRoomTime > 1.0 ? _voteEndRoomTime : (now + (VoteTimeoutMs / 1000.0));
                // Клиент 0.17 читает end/duration как float — double ломал таймер/плашку.
                float endTimeF = (float)endTime;
                float durationSec = (float)Math.Max(1.0, endTime - now);
                string initiator = GetActorUserId(_voteInitiatorActor);
                string kickTarget = _voteKickTargetUserId ?? "";
                byte result = succeeded ? (byte)1 : (byte)0;
                int voteType = _activeVoteType;

                // Только int-ключи: дубли byte+int в Hashtable иногда ломали Photon serialize.
                var vote = new Hashtable();
                vote[0] = _voteInitiatorActor;
                vote[1] = endTimeF;
                vote[2] = durationSec;
                vote[3] = voteType;
                vote[4] = yesIds.ToArray();
                vote[5] = noIds.ToArray();
                vote[6] = initiator ?? "";
                vote[7] = kickTarget;
                vote[8] = (int)result;

                var byTeam = new Hashtable();
                byte voteTeamKey = _voteTeam != 0 ? _voteTeam : TEAM_TERRORISTS;
                byTeam[(int)TEAM_TERRORISTS] = voteTeamKey == TEAM_TERRORISTS ? vote : null;
                byTeam[(int)TEAM_CT] = voteTeamKey == TEAM_CT ? vote : null;

                Hashtable roomProps = new Hashtable();
                roomProps["vote_request"] = byTeam;
                // Дублируем плоский vote — часть UI читает room.vote_request напрямую.
                roomProps["vote"] = vote;
                roomProps["vote_type"] = voteType;
                roomProps["vote_yes"] = yesIds.ToArray();
                roomProps["vote_no"] = noIds.ToArray();
                roomProps["vote_pending"] = pendingIds.ToArray();
                roomProps["vote_end_time"] = endTimeF;
                this.PluginHost.SetProperties(0, roomProps, null, true);

                foreach (var actor in this.PluginHost.GameActors)
                {
                    if (actor == null || !actor.IsActive) continue;
                    Hashtable actorProps = new Hashtable();
                    actorProps["vote_request"] = vote;
                    this.PluginHost.SetProperties(actor.ActorNr, actorProps, null, true);
                }

                if (_activeVoteType != 0 && result == 0 && !_voteInitUiSent)
                {
                    _voteInitUiSent = true;
                    BroadcastVoteInitEvent(_activeVoteType, _voteInitiatorActor);
                }

                LogToFile($"Vote published vote_request type={voteType} team={_voteTeam} yes=[{string.Join(",", yesIds)}] no=[{string.Join(",", noIds)}] pending=[{string.Join(",", pendingIds)}] result={result} end={endTimeF:0.0}");
            }
            catch (Exception ex)
            {
                LogToFile($"PublishVoteRequest error: {ex.Message}");
            }
        }

        private int GuessVoteTypeFromPayload(object data)
        {
            try
            {
                var dict = ToPhotonEventData(data);
                foreach (var kv in dict)
                {
                    if (kv.Key == 245 || kv.Key == 0) continue;
                    try
                    {
                        int v = Convert.ToInt32(kv.Value);
                        if (v >= 1 && v <= 5) return v;
                    }
                    catch { }
                }
            }
            catch { }
            return 0;
        }

        private int ExtractVoteHint(object data)
        {
            try
            {
                var dict = ToPhotonEventData(data);
                object raw = null;
                if (dict.ContainsKey(245)) raw = dict[245];
                else if (dict.ContainsKey(0)) raw = dict[0];
                else if (dict.ContainsKey(1)) raw = dict[1];
                else if (dict.Count > 0)
                {
                    foreach (var kv in dict) { raw = kv.Value; break; }
                }
                if (raw == null) return 0;
                var arr = raw as object[];
                if (arr != null && arr.Length > 0) raw = arr[0];
                return Convert.ToInt32(raw);
            }
            catch { return 0; }
        }

        private void HandleMissionEvent(IRaiseEventCallInfo info)
        {
            try
            {
                var data = info.Request.Data as IDictionary;
                if (data == null) return;

                if (data.Contains((byte)0) && Convert.ToInt32(data[(byte)0]) == 1) 
                {
                    var attacker = this.PluginHost.GameActors.FirstOrDefault(a => a.ActorNr == info.ActorNr);
                    if (attacker != null && !string.IsNullOrEmpty(attacker.UserId))
                    {
                        this.PluginHost.LogInfo($"MatchmakingPlugin: Player {attacker.UserId} got a kill! Updating challenge progress.");
                        SendChallengeProgress(attacker.UserId, "daily_kills_10", 1);
                    }
                }
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: Error in HandleMissionEvent: {ex.Message}");
            }
        }

        private void SendChallengeProgress(string playerId, string challengeId, int points)
        {
            try
            {
                string url = $"{serverUrl}/api/gameevent/progress"; 
                
                string json = "{\"PlayerId\":\"" + playerId + "\", \"ChallengeId\":\"" + challengeId + "\", \"Points\":" + points + "}";
                
                var request = new HttpRequest
                {
                    Url = url,
                    Method = "POST",
                    Accept = "application/json",
                    ContentType = "application/json",
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(json)),
                    Callback = (response, userState) => {
                        if (response.Status != HttpRequestQueueResult.Success || response.HttpCode != 200)
                        {
                            this.PluginHost.LogWarning($"MatchmakingPlugin: Failed to sync challenge progress for {playerId}. Status: {response.Status}, HttpCode: {response.HttpCode}");
                        }
                    }
                };

                this.PluginHost.HttpRequest(request);
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: Error in SendChallengeProgress: {ex.Message}");
            }
        }

        private void HandleChangeTeamRequest(IRaiseEventCallInfo info)
        {
            try
            {
                this.PluginHost.LogDebug($"MatchmakingPlugin: Team change request from Actor {info.ActorNr}");

                var eventData = info.Request.Data as IDictionary;
                if (eventData == null || !eventData.Contains((byte)0))
                {
                    this.PluginHost.LogError("MatchmakingPlugin: Invalid team change request - no data or missing key 0");
                    info.Cancel();
                    return;
                }

                byte requestedTeam = Convert.ToByte(eventData[(byte)0]);
                this.PluginHost.LogInfo($"MatchmakingPlugin: Actor {info.ActorNr} requests team {requestedTeam}");

                if (requestedTeam != TEAM_TERVORISTS && requestedTeam != TEAM_CT && requestedTeam != TEAM_SPECTATOR)
                {
                    this.PluginHost.LogError($"MatchmakingPlugin: Invalid team value: {requestedTeam}");
                    info.Cancel();
                    return;
                }

                var actor = this.PluginHost.GameActors.FirstOrDefault(a => a.ActorNr == info.ActorNr);
                if (actor == null)
                {
                    this.PluginHost.LogError($"MatchmakingPlugin: Actor {info.ActorNr} not found");
                    info.Cancel();
                    return;
                }

                byte currentTeam = TEAM_NONE;
                object currentTeamObj;
                if (actor.Properties.TryGetValue("team", out currentTeamObj) && currentTeamObj != null)
                {
                    currentTeam = Convert.ToByte(currentTeamObj);
                }

                if (currentTeam == requestedTeam)
                {
                    this.PluginHost.LogInfo($"MatchmakingPlugin: Actor {info.ActorNr} already in team {requestedTeam}, approving to trigger spawn.");
                    SendChangeTeamResponse(info.ActorNr, true, currentTeam, requestedTeam, ERROR_NONE);
                    info.Continue();
                    return;
                }

                if (requestedTeam != TEAM_SPECTATOR)
                {
                    int maxTeamSize = GetMaxTeamSize();
                    int requestedTeamCount = CountPlayersInTeam(requestedTeam);

                    if (requestedTeamCount >= maxTeamSize)
                    {
                        this.PluginHost.LogInfo($"MatchmakingPlugin: Team {requestedTeam} is full ({requestedTeamCount}/{maxTeamSize})");
                        SendChangeTeamResponse(info.ActorNr, false, currentTeam, requestedTeam, ERROR_TEAM_FULL);
                        info.Cancel();
                        return;
                    }

                    string mode = this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("C0") ? this.PluginHost.GameProperties["C0"] as string : "";
                    bool isRanked = mode == "RankedDefuse" || mode == "Ranked2v2" || mode == "ClanRankedDefuse";

                    // Team balance bypassed for custom matches
                }

                Hashtable properties = new Hashtable();
                properties["team"] = requestedTeam;
                this.PluginHost.SetProperties(info.ActorNr, properties, null, true);

                this.PluginHost.LogInfo($"MatchmakingPlugin: Actor {info.ActorNr} changed team from {currentTeam} to {requestedTeam}");

                SendChangeTeamResponse(info.ActorNr, true, currentTeam, requestedTeam, ERROR_NONE);

                info.Continue();
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: Error handling team change: {ex}");
                info.Cancel();
            }
        }

        private void SendChangeTeamResponse(int actorNr, bool approved, byte oldTeam, byte newTeam, int errorCode)
        {
            object[] responseData = new object[] { approved, oldTeam, newTeam, errorCode };
            var data = new Dictionary<byte, object> { { DATA_KEY, responseData } };

            this.PluginHost.BroadcastEvent(
                recieverActors: new List<int> { actorNr },
                senderActor: 0,
                evCode: EVT_CHANGE_TEAM_RESPONSE,
                data: data,
                cacheOp: 0,
                sendParameters: default(SendParameters)
            );
        }

        private void BroadcastChangeTeamResponse(bool approved, byte oldTeam, byte newTeam, int errorCode)
        {
            object[] responseData = new object[] { approved, oldTeam, newTeam, errorCode };
            var data = new Dictionary<byte, object> { { DATA_KEY, responseData } };

            this.PluginHost.BroadcastEvent(
                recieverActors: null, 
                senderActor: 0,
                evCode: EVT_CHANGE_TEAM_RESPONSE,
                data: data,
                cacheOp: 0,
                sendParameters: default(SendParameters)
            );
        }

        private int GetMaxTeamSize()
        {
            if (this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("team_size"))
            {
                try
                {
                    int ts = Convert.ToInt32(this.PluginHost.GameProperties["team_size"]);
                    if (ts >= 1 && ts <= 5) return ts;
                }
                catch { }
            }
            if (this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("C0"))
            {
                string gameMode = this.PluginHost.GameProperties["C0"] as string;
                if (gameMode == "Ranked2v2")
                {
                    int req = GetRankedRequiredPlayers();
                    return Math.Max(1, req / 2);
                }
                if (gameMode == "Yokai")
                {
                    return this.PluginHost.GameActors.Count > 10 ? this.PluginHost.GameActors.Count : 10;
                }
            }

            if (this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("max_team_size"))
            {
                try
                {
                    return Convert.ToInt32(this.PluginHost.GameProperties["max_team_size"]);
                }
                catch { }
            }
            return 5; 
        }

        private int CountPlayersInTeam(byte team)
        {
            int count = 0;
            foreach (var actor in this.PluginHost.GameActors)
            {
                object teamObj;
                if (actor.Properties.TryGetValue("team", out teamObj) && teamObj != null)
                {
                    try
                    {
                        if (Convert.ToByte(teamObj) == team)
                            count++;
                    }
                    catch { }
                }
            }
            return count;
        }

        public override void OnSetProperties(ISetPropertiesCallInfo info)
        {
            try
            {
                 int senderActorNr = info.ActorNr;
                 int targetActorNumber = info.Request.ActorNumber;
                 
                 // Server-initiated updates should pass through unchanged, but keep
                 // enough logging to verify that the authoritative final-state chain
                 // is actually being broadcast.
                 if (senderActorNr == 0)
                 {
                     if (info.Request.Properties != null && info.Request.Properties.ContainsKey("C2"))
                     {
                         object routeObj = info.Request.Properties.ContainsKey("RouteToGameStateId")
                             ? info.Request.Properties["RouteToGameStateId"]
                             : null;
                         int finalPlayersCount = 0;
                         if (info.Request.Properties.ContainsKey("FinalPlayers") && info.Request.Properties["FinalPlayers"] is Hashtable finalPlayers)
                         {
                             finalPlayersCount = finalPlayers.Count;
                         }

                         LogToFile($"ServerProp broadcast: C2={info.Request.Properties["C2"]}, RouteToGameStateId={routeObj}, FinalPlayers={finalPlayersCount}");
                     }

                     info.Continue();
                     return;
                 }
                 
                 LogToFile($"OnSetProperties: Sender={senderActorNr}, Target={targetActorNumber}");

                if (info.Request.Properties != null)
                {
                    if (targetActorNumber == 0)
                    {
                        foreach (DictionaryEntry entry in info.Request.Properties)
                        {
                            LogToFile($"RoomProp set: {entry.Key} = {entry.Value}");
                            NoteRoomScoreProp(entry.Key, entry.Value);
                        }

                        if (info.Request.Properties.ContainsKey("C2"))
                        {
                            var gameStateObj = info.Request.Properties["C2"];
                            if (gameStateObj != null)
                            {
                                byte gameState = Convert.ToByte(gameStateObj);
                                LogToFile($"GameState (C2) changed to {gameState}");

                                byte currentC2 = 0;
                                if (this.PluginHost.GameProperties.ContainsKey("C2"))
                                {
                                    var curC2Obj = this.PluginHost.GameProperties["C2"];
                                    if (curC2Obj != null)
                                    {
                                        currentC2 = Convert.ToByte(curC2Obj);
                                    }
                                }

                                bool blockState = false;
                                if (_matchFinalizing && gameState < 200)
                                {
                                    LogToFile($"Blocking game state {gameState} because match is already finalizing. Forcing FinalStatistics (202).");
                                    gameState = 202;
                                    info.Request.Properties["C2"] = (byte)202;
                                    info.Request.Properties["RouteToGameStateId"] = (byte)0;
                                    EnsureFinalPlayers(info.Request.Properties);
                                    EnsureFinalResultProperties(info.Request.Properties);
                                    ScheduleFinalStateSync();
                                }

                                if (currentC2 >= 22 && gameState < 22)
                                {
                                    // ÐÐµ Ð±Ð»Ð¾ÐºÐ¸Ñ€ÑƒÐµÐ¼ Ð²Ñ‹Ñ…Ð¾Ð´ Ð¸Ð· Ð¿Ð°ÑƒÐ·Ñ‹ (40) Ð¸ Ð½Ð¾Ñ€Ð¼Ð°Ð»ÑŒÐ½Ñ‹Ð¹ Ñ€Ð°ÑƒÐ½Ð´-Ñ†Ð¸ÐºÐ».
                                    if (currentC2 == 40 || gameState == 40)
                                        blockState = false;
                                    else
                                        blockState = true;
                                }
                                else if (currentC2 >= 11 && (gameState == 10 || gameState == 13))
                                {
                                    blockState = true;
                                }

                                if (blockState)
                                {
                                    LogToFile($"Blocking regressive GameState change to {gameState} (current C2 is {currentC2}). Removing C2 from request properties.");
                                    info.Request.Properties.Remove("C2");
                                    info.Request.Properties.Remove("RouteToGameStateId");

                                     // Force-sync late joiners including WarmUp — иначе вечная загрузка.
                                     byte actualC2 = currentC2;
                                     if (actualC2 >= 11)
                                     {
                                         this.PluginHost.CreateOneTimeTimer(() => {
                                             try {
                                                 LogToFile($"Force syncing late actor {senderActorNr} to actual C2 = {actualC2}");
                                                 Hashtable syncProps = new Hashtable();
                                                 syncProps["C2"] = actualC2;
                                                 syncProps["RouteToGameStateId"] = actualC2;
                                                 this.PluginHost.SetProperties(0, syncProps, null, true);
                                             }
                                             catch (Exception syncEx) {
                                                 LogToFile($"Error in late actor sync timer: {syncEx}");
                                             }
                                         }, 100);
                                     }
                                }
                                else
                                {
                                    if (gameState == 10)
                                    {
                                        int loadedPlayers, needHumans, requiredPlayers, botSlots;
                                        bool ready = HasEnoughHumansForStart(out loadedPlayers, out needHumans, out requiredPlayers, out botSlots);

                                        if (ready)
                                        {
                                            LogToFile($"OnSetProperties: WaitingPlayers (10). Loaded players={loadedPlayers}/{requiredPlayers} needHumans={needHumans} bots={botSlots}. Routing to WarmUp (11).");
                                            info.Request.Properties["C2"] = (byte)11;
                                            info.Request.Properties["RouteToGameStateId"] = (byte)0;

                                            StartWarmUpTimer();
                                        }
                                        else
                                        {
                                            LogToFile($"OnSetProperties: WaitingPlayers (10). Loaded players={loadedPlayers}/{requiredPlayers} needHumans={needHumans} bots={botSlots}. Keeping state 10.");
                                            info.Request.Properties.Remove("RouteToGameStateId");
                                            StartJoinWaitUi(RankedHumanJoinWaitMs);
                                            ScheduleCancelIfNotFull(needHumans, RankedHumanJoinWaitMs);
                                        }
                                    }
                                    else if (gameState == 13)
                                    {
                                        int loadedPlayers, needHumans, requiredPlayers, botSlots;
                                        bool ready = HasEnoughHumansForStart(out loadedPlayers, out needHumans, out requiredPlayers, out botSlots);

                                        if (ready)
                                        {
                                            LogToFile($"OnSetProperties: GameStartRequest (13). Loaded players={loadedPlayers}/{requiredPlayers} needHumans={needHumans} bots={botSlots}. Routing to WarmUp (11).");
                                            info.Request.Properties["C2"] = (byte)11;
                                            info.Request.Properties["RouteToGameStateId"] = (byte)0;

                                            StartWarmUpTimer();
                                        }
                                        else
                                        {
                                            LogToFile($"OnSetProperties: GameStartRequest (13). Loaded players={loadedPlayers}/{requiredPlayers} needHumans={needHumans} bots={botSlots}. Routing to WaitingPlayers (10).");
                                            info.Request.Properties["C2"] = (byte)10;
                                            info.Request.Properties["RouteToGameStateId"] = (byte)10;
                                            ScheduleCancelIfNotFull(needHumans, RankedHumanJoinWaitMs);
                                        }
                                    }
                                     else if (gameState == 11)
                                     {
                                         int loadedPlayers, needHumans, requiredPlayers, botSlots;
                                         bool ready = HasEnoughHumansForStart(out loadedPlayers, out needHumans, out requiredPlayers, out botSlots);
                                         if (!ready)
                                         {
                                             LogToFile($"OnSetProperties: BLOCK WarmUp (11) — only {loadedPlayers}/{needHumans} humans (full={requiredPlayers} bots={botSlots}). Keep WaitingPlayers.");
                                             info.Request.Properties["C2"] = (byte)10;
                                             info.Request.Properties["RouteToGameStateId"] = (byte)10;
                                             StartJoinWaitUi(RankedHumanJoinWaitMs);
                                             ScheduleCancelIfNotFull(needHumans, RankedHumanJoinWaitMs);
                                         }
                                         else
                                         {
                                             _joinWaitActive = false;
                                             LogToFile($"OnSetProperties: Room transitioned to WarmUp (11). humans={loadedPlayers}/{needHumans} bots={botSlots}. Starting server countdown timer.");
                                             StartWarmUpTimer();
                                         }
                                     }
                                      else if (gameState == 21)
                                      {
                                          if (TryBlockEarlyRoundState(21))
                                          {
                                              LogToFile("OnSetProperties: BLOCK WarmUpFinished (21) — warmup timer not elapsed. Keep C2=11.");
                                              info.Request.Properties["C2"] = (byte)11;
                                              info.Request.Properties["RouteToGameStateId"] = (byte)0;
                                              if (_warmupStartedAtMs == 0) StartWarmUpTimer();
                                          }
                                          else
                                          {
                                          int loadedPlayers, needHumans, requiredPlayers, botSlots;
                                          bool ready = HasEnoughHumansForStart(out loadedPlayers, out needHumans, out requiredPlayers, out botSlots);
                                          if (!ready)
                                          {
                                              LogToFile($"OnSetProperties: BLOCK WarmUpFinished (21) — incomplete lobby {loadedPlayers}/{needHumans}. Stay waiting, no MMR.");
                                              info.Request.Properties["C2"] = (byte)10;
                                              info.Request.Properties["RouteToGameStateId"] = (byte)10;
                                              info.Request.Properties["MatchStarted"] = false;
                                              StartJoinWaitUi(30000);
                                          }
                                          else
                                          {
                                          LogToFile("OnSetProperties: WarmUpFinished (21) requested. Server starts round after 4s.");
                                          info.Request.Properties["C2"] = (byte)21;
                                          info.Request.Properties["RouteToGameStateId"] = (byte)21;
                                          ArmRoundStart(4000);
                                          }
                                          }
                                      }
                                       else if (gameState == 40)
                                       {
                                           // Tactical Pause â€” Ð½Ðµ Ð±Ð»Ð¾ÐºÐ¸Ñ€Ð¾Ð²Ð°Ñ‚ÑŒ Ð¸ Ð½Ðµ Ñ„Ð¾Ñ€ÑÐ¸Ñ‚ÑŒ sync Ð½Ð° Ð´Ñ€ÑƒÐ³Ð¾Ð¹ C2.
                                           LogToFile("OnSetProperties: Tactical Pause (40) allowed.");
                                           info.Request.Properties["C2"] = (byte)40;
                                           if (!info.Request.Properties.ContainsKey("RouteToGameStateId"))
                                               info.Request.Properties["RouteToGameStateId"] = (byte)40;
                                       }
                                       else if (gameState == 22)
                                       {
                                           if (TryBlockEarlyRoundState(22))
                                           {
                                               LogToFile("OnSetProperties: BLOCK RoundStarting (22) — warmup skipped by client. Keep C2=11.");
                                               info.Request.Properties["C2"] = (byte)11;
                                               info.Request.Properties["RouteToGameStateId"] = (byte)0;
                                               if (_warmupStartedAtMs == 0) StartWarmUpTimer();
                                           }
                                           else
                                           {
                                           _hadLiveRound = true;
                                           _joinWaitActive = false;
                                           LogToFile("OnSetProperties: RoundStarting (22) requested. Ensuring all players transition immediately.");
                                           info.Request.Properties["C2"] = (byte)22;
                                           info.Request.Properties["RouteToGameStateId"] = (byte)22;
                                           this.PluginHost.CreateOneTimeTimer(() => {
                                               try { TryActivatePendingPauseOnRoundStart(); }
                                               catch (Exception ex) { LogToFile($"Pending pause activate error: {ex.Message}"); }
                                           }, 150);

                                           byte previousC2 = 0;
                                           if (this.PluginHost.GameProperties.ContainsKey("C2"))
                                           {
                                               var prevC2Obj = this.PluginHost.GameProperties["C2"];
                                               if (prevC2Obj != null) previousC2 = Convert.ToByte(prevC2Obj);
                                           }

                                           if (previousC2 == 112)
                                           {
                                               LogToFile("Transition from halftime (112) to RoundStarting (22) detected. Force respawning all players on their new teams.");
                                               RespawnAllPlayers();
                                           }
                                           }
                                       }
                                      else if (gameState == 23)
                                      {
                                          LogToFile("OnSetProperties: RankedGameStartRequest (23) requested. Allowing transition to propagate naturally.");
                                          info.Request.Properties["C2"] = (byte)23;
                                          info.Request.Properties["RouteToGameStateId"] = (byte)23;

                                          // Schedule transition to state 22 with round init 100ms later
                                          this.PluginHost.CreateOneTimeTimer(() => {
                                              try {
                                                  byte checkC2 = 0;
                                                  if (this.PluginHost.GameProperties.ContainsKey("C2"))
                                                  {
                                                      var checkC2Obj = this.PluginHost.GameProperties["C2"];
                                                      if (checkC2Obj != null) checkC2 = Convert.ToByte(checkC2Obj);
                                                  }

                                                  if (checkC2 == 23)
                                                  {
                                                      LogToFile("OnSetProperties timer: Transitioning to state 22 (RoundStarting) from state 23.");
                                                      Hashtable props22 = new Hashtable();
                                                      props22["C2"] = (byte)22;
                                                      props22["RouteToGameStateId"] = (byte)22;

                                                      int currentRound = 0;
                                                      if (this.PluginHost.GameProperties.ContainsKey("Round") && this.PluginHost.GameProperties["Round"] != null)
                                                      {
                                                          currentRound = Convert.ToInt32(this.PluginHost.GameProperties["Round"]);
                                                      }
                                                      props22["Round"] = currentRound + 1;

                                                      double timeVal = 0.0;
                                                      if (this.PluginHost.GameProperties.ContainsKey("Time") && this.PluginHost.GameProperties["Time"] != null)
                                                      {
                                                          timeVal = Convert.ToDouble(this.PluginHost.GameProperties["Time"]);
                                                      }
                                                      props22["RoundStartTime"] = timeVal;
                                                      props22["Time"] = timeVal;

                                                      this.PluginHost.SetProperties(0, props22, null, true);
                                                  }
                                              }
                                              catch (Exception timerEx) {
                                                  LogToFile($"Error in OnSetProperties state 23 timer: {timerEx}");
                                              }
                                          }, 100);
                                      }
                                       else if (gameState == 110)
                                       {
                                           LogToFile("OnSetProperties: TeamSwapStartRequest (110) requested. Allowing transition to propagate naturally.");
                                           info.Request.Properties["C2"] = (byte)110;
                                           info.Request.Properties["RouteToGameStateId"] = (byte)110;

                                           // Schedule transition to 111 and then 112
                                           this.PluginHost.CreateOneTimeTimer(() => {
                                               try {
                                                   byte checkC2 = 0;
                                                   if (this.PluginHost.GameProperties.ContainsKey("C2"))
                                                   {
                                                       var checkC2Obj = this.PluginHost.GameProperties["C2"];
                                                       if (checkC2Obj != null) checkC2 = Convert.ToByte(checkC2Obj);
                                                   }

                                                   if (checkC2 == 110)
                                                   {
                                                       LogToFile("Halftime transition Step 2: Transitioning to state 111.");
                                                       Hashtable props111 = new Hashtable();
                                                       props111["C2"] = (byte)111;
                                                       props111["RouteToGameStateId"] = (byte)111;
                                                       this.PluginHost.SetProperties(0, props111, null, true);

                                                       this.PluginHost.CreateOneTimeTimer(() => {
                                                           try {
                                                               byte checkC2_3 = 0;
                                                               if (this.PluginHost.GameProperties.ContainsKey("C2"))
                                                               {
                                                                   var checkC2Obj3 = this.PluginHost.GameProperties["C2"];
                                                                   if (checkC2Obj3 != null) checkC2_3 = Convert.ToByte(checkC2Obj3);
                                                               }

                                                               if (checkC2_3 == 111)
                                                               {
                                                                   LogToFile("Halftime transition Step 3: Transitioning to state 112.");
                                                                   Hashtable props112 = new Hashtable();
                                                                   props112["C2"] = (byte)112;
                                                                   props112["RouteToGameStateId"] = (byte)112;
                                                                   props112["swapped_team"] = true;
                                                                   this.PluginHost.SetProperties(0, props112, null, true);
                                                               }
                                                           }
                                                           catch (Exception ex3) { LogToFile($"Halftime timer Step 3 error: {ex3}"); }
                                                       }, 100);
                                                   }
                                               }
                                               catch (Exception ex2) { LogToFile($"Halftime timer Step 2 error: {ex2}"); }
                                           }, 100);
                                       }
                                       else if (gameState == 111 || gameState == 113)
                                       {
                                           LogToFile($"OnSetProperties: Halftime state ({gameState}) requested. Ensuring all players transition immediately.");
                                           info.Request.Properties["C2"] = gameState;
                                           info.Request.Properties["RouteToGameStateId"] = gameState;
                                       }
                                        else if (gameState == 112)
                                        {
                                            LogToFile("OnSetProperties: TeamSwap (112) requested. Ensuring all players transition immediately.");
                                            info.Request.Properties["C2"] = (byte)112;
                                            info.Request.Properties["RouteToGameStateId"] = (byte)112;
                                            info.Request.Properties["swapped_team"] = true;

                                            PerformHalftimeTeamSwap();
                                        }
                                        else if (gameState == 102)
                                        {
                                            LogToFile("OnSetProperties: MatchEnded (102) requested. Ensuring all players transition immediately.");
                                            info.Request.Properties["C2"] = (byte)102;
                                            info.Request.Properties["RouteToGameStateId"] = (byte)102;
                                        }
                                          else if (gameState >= 200)
                                         {
                                            if (_lobbyAborted)
                                            {
                                                LogToFile($"OnSetProperties: lobby abort C2={gameState} — skip rank/final chain, keep JoinTimeout UI.");
                                                info.Request.Properties["C2"] = (byte)202;
                                                info.Request.Properties["RouteToGameStateId"] = (byte)0;
                                                if (!info.Request.Properties.ContainsKey("GameFailedErrorMsg"))
                                                {
                                                    Hashtable fail = new Hashtable();
                                                    fail["Code"] = 0;
                                                    fail["Message"] = "RankedMatchmakingError/WaitingForPlayers";
                                                    fail["RpcExceptionCode"] = 0;
                                                    info.Request.Properties["GameFailedErrorMsg"] = fail;
                                                }
                                                try { EnsureFinalPlayers(info.Request.Properties); } catch { }
                                            }
                                            else if (!_hadLiveRound && currentC2 < 22)
                                            {
                                                LogToFile($"OnSetProperties: premature end C2={gameState} during wait/warmup (current={currentC2}). Abort WITHOUT MMR.");
                                                AbortIncompleteRoom();
                                                info.Request.Properties.Remove("C2");
                                                info.Request.Properties.Remove("RouteToGameStateId");
                                            }
                                            else
                                            {
                                            _matchFinalizing = true;
                                            LogToFile($"OnSetProperties: End game state ({gameState}) requested. Ensuring all players transition immediately.");
                                            info.Request.Properties["C2"] = gameState;
                                            info.Request.Properties["RouteToGameStateId"] = (byte)0;

                                              if (gameState == 201)
                                              {
                                                  LogToFile($"RankedGameEndRequest (201) accepted from Actor {senderActorNr}. Preparing final payload and scheduling server final chain.");
                                                  ReportMatchResultOnce();
                                                  ScheduleServerOwnedFinalization();

                                                  EnsureFinalPlayers(info.Request.Properties);
                                                  EnsureFinalResultProperties(info.Request.Properties);
                                                  LogToFile("Client 201 request accepted with final payload; server will move clients to 202 after payload preload.");
                                              }
                                              else if (gameState == 202)
                                              {
                                                  EnsureFinalPlayers(info.Request.Properties);
                                                  EnsureFinalResultProperties(info.Request.Properties);
                                                  ScheduleFinalStateSync();
                                              }
                                              else if (gameState == 203)
                                              {
                                                  EnsureFinalPlayers(info.Request.Properties);
                                                  EnsureFinalResultProperties(info.Request.Properties);
                                                  ScheduleFinalStateSync();
                                              }
                                              else if (gameState == 204)
                                              {
                                                  EnsureFinalPlayers(info.Request.Properties);
                                                  EnsureFinalResultProperties(info.Request.Properties);
                                              }
                                              else if (gameState == 205)
                                              {
                                                  EnsureFinalPlayers(info.Request.Properties);
                                                  EnsureFinalResultProperties(info.Request.Properties);
                                              }
                                            }
                                          } 
                                    else if (gameState == 101)
                                    {
                                        try
                                        {
                                            string mode = "";
                                            if (this.PluginHost.GameProperties.ContainsKey("C0"))
                                                mode = this.PluginHost.GameProperties["C0"] as string ?? "";

                                            bool isRanked = mode == "RankedDefuse" || mode == "Ranked2v2" || mode == "ClanRankedDefuse";

                                            if (isRanked)
                                            {
                                                if (DEBUG_FINISH_RANKED2V2_AFTER_ROUND && mode == "Ranked2v2")
                                                {
                                                    _matchFinalizing = true;
                                                    LogToFile("Debug fast finish: Ranked2v2 RoundFinished (101). Forcing FinalStatistics (202) server-side.");
                                                    ReportMatchResultOnce();
                                                    ScheduleForcedFinalStatistics(100);
                                                }
                                                else
                                                {
                                                    int currentRound = 0;
                                                    if (this.PluginHost.GameProperties.ContainsKey("Round"))
                                                        currentRound = Convert.ToInt32(this.PluginHost.GameProperties["Round"]);

                                                    bool alreadySwapped = false;
                                                    if (this.PluginHost.GameProperties.ContainsKey("swapped_team"))
                                                    {
                                                        var swVal = this.PluginHost.GameProperties["swapped_team"];
                                                        if (swVal is bool b) alreadySwapped = b;
                                                        else alreadySwapped = Convert.ToBoolean(swVal);
                                                    }

                                                    int halfTimeRound = (mode == "Ranked2v2") ? 4 : 5;

                                                    LogToFile($"OnSetProperties: RoundFinished (101). Mode={mode}, Round={currentRound}, halfTimeRound={halfTimeRound}, alreadySwapped={alreadySwapped}");

                                                    if (currentRound == halfTimeRound && !alreadySwapped)
                                                    {
                                                        LogToFile("Halftime reached! Routing to TeamSwapStartRequest (110).");
                                                        info.Request.Properties["C2"] = (byte)110;
                                                        info.Request.Properties["RouteToGameStateId"] = (byte)110;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                LogToFile($"OnSetProperties: RoundFinished (101). Mode={mode} (not ranked, no halftime check).");
                                            }
                                        }
                                        catch (Exception hte)
                                        {
                                            LogToFile($"OnSetProperties: Halftime check error: {hte}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (DictionaryEntry entry in info.Request.Properties)
                        {
                            LogToFile($"Actor {targetActorNumber} Prop set: {entry.Key} = {entry.Value}");
                        }

                        string modeForTeamForce = "";
                        if (this.PluginHost.GameProperties.ContainsKey("C0"))
                            modeForTeamForce = this.PluginHost.GameProperties["C0"] as string ?? "";
                        bool shouldForceRankedTeam = IsRankedMode(modeForTeamForce);

                        // Force team verification only for ranked modes to prevent TEAM_NONE camera locks there.
                        byte currentTeam = TEAM_NONE;
                        var actorObj = this.PluginHost.GameActors.FirstOrDefault(a => a.ActorNr == targetActorNumber);
                        if (actorObj != null)
                        {
                            object teamObj;
                            if (actorObj.Properties.TryGetValue("team", out teamObj) && teamObj != null)
                            {
                                currentTeam = Convert.ToByte(teamObj);
                            }
                        }

                        if (shouldForceRankedTeam && info.Request.Properties.ContainsKey("team"))
                        {
                            object tVal = info.Request.Properties["team"];
                            byte reqTeam = tVal != null ? Convert.ToByte(tVal) : TEAM_NONE;
                            if (reqTeam == TEAM_NONE || reqTeam == TEAM_SPECTATOR)
                            {
                                byte forcedTeam = GetDeterministicTeam(targetActorNumber);
                                LogToFile($"Actor {targetActorNumber} tried to set team to {reqTeam}. Overwriting with ForceDoubleTeamAssignment to {forcedTeam}");
                                
                                info.Request.Properties.Remove("team");
                                ForceDoubleTeamAssignment(targetActorNumber, forcedTeam);
                                currentTeam = forcedTeam;
                            }
                        }
                        else if (shouldForceRankedTeam && currentTeam == TEAM_NONE)
                        {
                            byte forcedTeam = GetDeterministicTeam(targetActorNumber);
                            LogToFile($"Actor {targetActorNumber} has TEAM_NONE in cache during property set. Force triggering ForceDoubleTeamAssignment to {forcedTeam}");
                            ForceDoubleTeamAssignment(targetActorNumber, forcedTeam);
                            currentTeam = forcedTeam;
                        }

                        if (info.Request.Properties.ContainsKey("uid"))
                        {
                            if (shouldForceRankedTeam)
                            {
                                byte team = GetActorTeamWithFallback(targetActorNumber);
                                LogToFile($"Actor {targetActorNumber} loaded (uid set). Triggering ForceDoubleTeamAssignment for team {team} to ensure spawn.");
                                ForceDoubleTeamAssignment(targetActorNumber, team);
                            }

                            // UID-driven room warmup transition
                            try
                            {
                                byte currentC2 = 0;
                                if (this.PluginHost.GameProperties.ContainsKey("C2"))
                                {
                                    var curC2Obj = this.PluginHost.GameProperties["C2"];
                                    if (curC2Obj != null)
                                    {
                                        currentC2 = Convert.ToByte(curC2Obj);
                                    }
                                }

                                if (currentC2 == 10 || currentC2 == 13 || currentC2 == 0)
                                {
                                    int loadedPlayers, needHumans, requiredPlayers, botSlots;
                                    bool ready = HasEnoughHumansForStart(out loadedPlayers, out needHumans, out requiredPlayers, out botSlots);
                                    object uValDummy;
                                    bool currentHasUidInCache = actorObj != null && actorObj.Properties.TryGetValue("uid", out uValDummy);
                                    if (!currentHasUidInCache)
                                        loadedPlayers++;

                                    LogToFile($"Actor {targetActorNumber} set uid. Loaded={loadedPlayers} needHumans={needHumans} (full={requiredPlayers} bots={botSlots}) C2={currentC2} max_players={ReadMaxPlayersProp()}");

                                    if (loadedPlayers >= needHumans)
                                    {
                                        LogToFile($"UID-driven transition: humans ready ({loadedPlayers}/{needHumans}). Force WarmUp (11).");
                                        Hashtable roomProps = new Hashtable();
                                        roomProps["C2"] = (byte)11;
                                        roomProps["RouteToGameStateId"] = (byte)0;
                                        this.PluginHost.SetProperties(0, roomProps, null, true);

                                        BroadcastWarmUpSpawnsWithDelayedTimers();
                                        StartWarmUpTimer();
                                    }
                                }
                                else if (currentC2 >= 11)
                                {
                                    // Late join: сразу синкаем C2 (в т.ч. WarmUp), иначе вечная загрузка.
                                    byte syncC2 = currentC2;
                                    LogToFile($"Actor {targetActorNumber} late uid; syncing to C2={syncC2}");
                                    Hashtable syncProps = new Hashtable();
                                    syncProps["C2"] = syncC2;
                                    syncProps["RouteToGameStateId"] = syncC2;
                                    this.PluginHost.SetProperties(0, syncProps, null, true);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogToFile($"Error in UID-driven room transition: {ex}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"OnSetProperties Error: {ex}");
                this.PluginHost.LogError($"MatchmakingPlugin: OnSetProperties error: {ex}");
            }

            info.Continue();
        }

        public override void OnCloseGame(ICloseGameCallInfo info)
        {
            this.PluginHost.LogInfo($"MatchmakingPlugin: OnCloseGame - Room: {this.PluginHost.GameId}");

            try
            {
                if (!_matchResultReported && !_lobbyAborted && IsRankedRoomMode())
                {
                    LogToFile("OnCloseGame: ranked result missing — forcing ReportMatchResultOnce");
                    ReportMatchResultOnce();
                }
            }
            catch (Exception forceEx)
            {
                LogToFile("OnCloseGame force-result: " + forceEx.Message);
            }

            try
            {
                var ids = new List<string>();
                foreach (var actor in this.PluginHost.GameActors)
                {
                    if (!string.IsNullOrEmpty(actor.UserId))
                    {
                        ids.Add(actor.UserId);
                        this.PluginHost.LogInfo($"MatchmakingPlugin: Final sync for {actor.UserId} on close.");
                        ReportStatus(actor.UserId, this.PluginHost.GameId, true, "");
                    }
                }
                string matchId = CanonicalMatchId(this.PluginHost.GameId);
                var sb = new StringBuilder();
                sb.Append("{\"MatchId\":\"").Append(JsonEscape(matchId)).Append("\",\"PlayerIds\":[");
                for (int i = 0; i < ids.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(JsonEscape(ids[i])).Append('"');
                }
                sb.Append("]}");
                var request = new HttpRequest
                {
                    Url = serverUrl + "/api/match/finished",
                    Method = "POST",
                    Accept = "application/json",
                    ContentType = "application/json",
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())),
                    Callback = (response, userState) => { }
                };
                this.PluginHost.HttpRequest(request);
            }
            catch (Exception ex)
            {
                LogToFile("OnCloseGame finished-notify error: " + ex.Message);
            }

            info.Continue();
        }

        private void ClampEmptyRoomTtl(ICreateGameCallInfo info)
        {
            try
            {
                // 1. Clamp in info.CreateOptions
                if (info.CreateOptions != null)
                {
                    var keys = new List<string>(info.CreateOptions.Keys);
                    foreach (var key in keys)
                    {
                        if (key.Equals("EmptyRoomTTL", StringComparison.OrdinalIgnoreCase) || 
                            key.Equals("EmptyRoomTtl", StringComparison.OrdinalIgnoreCase) || 
                            key == "245" || 
                            key == "236")
                        {
                            try
                            {
                                int ttl = Convert.ToInt32(info.CreateOptions[key]);
                                if (ttl > 60000)
                                {
                                    this.PluginHost.LogInfo($"MatchmakingPlugin: Clamping CreateOption '{key}' from {ttl} to 60000");
                                    info.CreateOptions[key] = 60000;
                                }
                            }
                            catch (Exception ex)
                            {
                                this.PluginHost.LogError($"MatchmakingPlugin: Error converting CreateOption '{key}': {ex.Message}");
                            }
                        }
                    }
                }

                // 2. Clamp in raw OperationRequest Parameters
                if (info.OperationRequest != null && info.OperationRequest.Parameters != null)
                {
                    // Parameter 236 is EmptyRoomTTL in create options
                    if (info.OperationRequest.Parameters.TryGetValue(236, out object val236) && val236 != null)
                    {
                        try
                        {
                            int ttl = Convert.ToInt32(val236);
                            if (ttl > 60000)
                            {
                                this.PluginHost.LogInfo($"MatchmakingPlugin: Clamping raw parameter 236 from {ttl} to 60000");
                                info.OperationRequest.Parameters[236] = 60000;
                            }
                        }
                        catch (Exception ex)
                        {
                            this.PluginHost.LogError($"MatchmakingPlugin: Error converting raw parameter 236: {ex.Message}");
                        }
                    }

                    // Parameter 248 is GameProperties (Hashtable/Dictionary)
                    if (info.OperationRequest.Parameters.TryGetValue(248, out object propsObj) && propsObj is IDictionary props)
                    {
                        // Byte key 245 is EmptyRoomTtl inside GameProperties
                        if (props.Contains((byte)245) && props[(byte)245] != null)
                        {
                            try
                            {
                                int ttl = Convert.ToInt32(props[(byte)245]);
                                if (ttl > 60000)
                                {
                                    this.PluginHost.LogInfo($"MatchmakingPlugin: Clamping GameProperty 245 from {ttl} to 60000");
                                    props[(byte)245] = 60000;
                                }
                            }
                            catch (Exception ex)
                            {
                                this.PluginHost.LogError($"MatchmakingPlugin: Error converting GameProperty 245: {ex.Message}");
                            }
                        }
                    }
                }

                // 3. Clamp using reflection on info.Request
                var requestProp = info.GetType().GetProperty("Request");
                if (requestProp != null)
                {
                    object requestObj = requestProp.GetValue(info);
                    if (requestObj != null)
                    {
                        // Check for EmptyRoomLiveTime property
                        var liveTimeProp = requestObj.GetType().GetProperty("EmptyRoomLiveTime");
                        if (liveTimeProp != null && liveTimeProp.CanWrite)
                        {
                            try
                            {
                                int ttl = Convert.ToInt32(liveTimeProp.GetValue(requestObj));
                                if (ttl > 60000)
                                {
                                    this.PluginHost.LogInfo($"MatchmakingPlugin: Clamping Request.EmptyRoomLiveTime from {ttl} to 60000");
                                    liveTimeProp.SetValue(requestObj, 60000);
                                }
                            }
                            catch (Exception ex)
                            {
                                this.PluginHost.LogError($"MatchmakingPlugin: Error converting Request.EmptyRoomLiveTime: {ex.Message}");
                            }
                        }

                        // Check for EmptyRoomTTL property
                        var ttlProp = requestObj.GetType().GetProperty("EmptyRoomTTL");
                        if (ttlProp != null && ttlProp.CanWrite)
                        {
                            try
                            {
                                int ttl = Convert.ToInt32(ttlProp.GetValue(requestObj));
                                if (ttl > 60000)
                                {
                                    this.PluginHost.LogInfo($"MatchmakingPlugin: Clamping Request.EmptyRoomTTL from {ttl} to 60000");
                                    ttlProp.SetValue(requestObj, 60000);
                                }
                            }
                            catch (Exception ex)
                            {
                                this.PluginHost.LogError($"MatchmakingPlugin: Error converting Request.EmptyRoomTTL: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: Error in ClampEmptyRoomTtl: {ex}");
            }
        }

        private byte GetActorTeamWithFallback(int actorNr)
        {
            var actorObj = this.PluginHost.GameActors.FirstOrDefault(a => a.ActorNr == actorNr);
            return GetActorTeamWithFallback(actorObj, actorNr);
        }

        private byte GetActorTeamWithFallback(IActor actor, int actorNr)
        {
            if (actor != null && actor.Properties != null)
            {
                object teamObj;
                if (actor.Properties.TryGetValue("team", out teamObj) && teamObj != null)
                {
                    byte t = Convert.ToByte(teamObj);
                    if (t != TEAM_NONE) return t;
                }
            }
            return GetDeterministicTeam(actorNr);
        }

        private byte GetDeterministicTeam(int actorNr)
        {
            bool isSwapped = false;
            if (this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("swapped_team"))
            {
                var swVal = this.PluginHost.GameProperties["swapped_team"];
                if (swVal != null)
                {
                    if (swVal is bool b) isSwapped = b;
                    else isSwapped = Convert.ToBoolean(swVal);
                }
            }

            byte normalTeam = (actorNr % 2 == 1) ? TEAM_TERVORISTS : TEAM_CT;
            if (isSwapped)
            {
                return (normalTeam == TEAM_TERVORISTS) ? TEAM_CT : TEAM_TERVORISTS;
            }
            return normalTeam;
        }

        private void PerformHalftimeTeamSwap()
        {
            if (_halftimeSwapDone)
            {
                LogToFile("PerformHalftimeTeamSwap: SKIPPED â€” already performed this match. Preventing double-swap.");
                return;
            }
            _halftimeSwapDone = true;

            LogToFile("Performing Halftime Team Swap...");
            foreach (var actor in this.PluginHost.GameActors)
            {
                if (actor.IsActive)
                {
                    byte currentTeam = GetActorTeamWithFallback(actor, actor.ActorNr);
                    byte newTeam = (currentTeam == TEAM_TERVORISTS) ? TEAM_CT : TEAM_TERVORISTS;
                    
                    LogToFile($"Halftime Swap: Actor {actor.ActorNr} swapping from {currentTeam} to {newTeam}");
                    
                    Hashtable propsTarget = new Hashtable();
                    propsTarget["team"] = newTeam;
                    this.PluginHost.SetProperties(actor.ActorNr, propsTarget, null, true);
                }
            }
        }

        private void ForceDoubleTeamAssignment(int actorNr, byte targetTeam)
        {
            LogToFile($"ForceDoubleTeamAssignment (Single-Phase): Setting Actor {actorNr} team to {targetTeam}");
            
            // Phase 1: Set their team property directly on the server
            Hashtable propsTarget = new Hashtable();
            propsTarget["team"] = targetTeam;
            this.PluginHost.SetProperties(actorNr, propsTarget, null, true);
            
            // Phase 2: Send change team response immediately with oldTeam = TEAM_NONE (0) to trigger spawn
            this.PluginHost.CreateOneTimeTimer(() => {
                SendChangeTeamResponse(actorNr, true, TEAM_NONE, targetTeam, ERROR_NONE);
            }, 200);
        }

        private void BroadcastWarmUpSpawnsWithDelayedTimers()
        {
            string mode = this.PluginHost.GameProperties.ContainsKey("C0")
                ? this.PluginHost.GameProperties["C0"] as string ?? ""
                : "";
            if (!IsRankedMode(mode))
            {
                LogToFile($"WarmUp safety spawn timers skipped for non-ranked mode {mode}.");
                return;
            }

            int[] delays = { 500 };
            foreach (int delay in delays)
            {
                this.PluginHost.CreateOneTimeTimer(() => {
                    try {
                        LogToFile($"Executing WarmUp safety double-team assignment timer for delay {delay}ms.");
                        if (_matchFinalizing || GetCurrentRoomC2() >= 200)
                        {
                            LogToFile($"WarmUp safety timer skipped because match is finalizing. Current C2={GetCurrentRoomC2()}.");
                            return;
                        }

                        foreach (var actor in this.PluginHost.GameActors)
                        {
                            object uVal;
                            if (actor.IsActive && actor.Properties.TryGetValue("uid", out uVal))
                            {
                                byte team = GetActorTeamWithFallback(actor, actor.ActorNr);
                                if (team != TEAM_NONE)
                                {
                                    ForceDoubleTeamAssignment(actor.ActorNr, team);
                                }
                            }
                        }
                    }
                    catch (Exception ex) {
                        LogToFile($"Error in WarmUp safety spawn timer ({delay}ms): {ex}");
                    }
                }, delay);
            }
        }

        private void RespawnAllPlayers()
        {
            string mode = this.PluginHost.GameProperties.ContainsKey("C0")
                ? this.PluginHost.GameProperties["C0"] as string ?? ""
                : "";
            if (!IsRankedMode(mode))
            {
                LogToFile($"RespawnAllPlayers skipped for non-ranked mode {mode}.");
                return;
            }

            this.PluginHost.CreateOneTimeTimer(() => {
                try {
                    if (_matchFinalizing || GetCurrentRoomC2() >= 200)
                    {
                        LogToFile($"RespawnAllPlayers skipped because match is finalizing. Current C2={GetCurrentRoomC2()}.");
                        return;
                    }

                    LogToFile("RespawnAllPlayers: Force respawning all active players to reset equipment.");
                    foreach (var actor in this.PluginHost.GameActors)
                    {
                        if (!actor.IsActive) continue;
                        byte team = GetActorTeamWithFallback(actor, actor.ActorNr);
                        if (team == TEAM_NONE) continue;

                        // Ð¡Ð±Ñ€Ð¾Ñ Ð´ÐµÐ½ÐµÐ³/Ñ„Ð»Ð°Ð³Ð¾Ð² Ñ€Ð°Ð·Ð¼Ð¸Ð½ÐºÐ¸ â€” Ð¸Ð½Ð°Ñ‡Ðµ ÐºÑƒÐ¿Ð»ÐµÐ½Ð½Ð¾Ðµ Ð¾Ñ€ÑƒÐ¶Ð¸Ðµ Ñ‡Ð°ÑÑ‚Ð¾ Ð¾ÑÑ‚Ð°Ñ‘Ñ‚ÑÑ.
                        try
                        {
                            Hashtable resetProps = new Hashtable();
                            resetProps["money"] = 800;
                            resetProps["isDeath"] = false;
                            resetProps["round_kills"] = 0;
                            resetProps["round_assists"] = 0;
                            this.PluginHost.SetProperties(actor.ActorNr, resetProps, null, true);
                        }
                        catch { }

                        // Spectator bounce Ñ„Ð¾Ñ€ÑÐ¸Ñ‚ Ð¿Ð¾Ð»Ð½Ñ‹Ð¹ client-side respawn Ð±ÐµÐ· ÑÐºÐ¸Ð¿Ð° Ñ Ñ€Ð°Ð·Ð¼Ð¸Ð½ÐºÐ¸.
                        try
                        {
                            SendChangeTeamResponse(actor.ActorNr, true, team, TEAM_SPECTATOR, ERROR_NONE);
                        }
                        catch { }

                        int actorNr = actor.ActorNr;
                        byte targetTeam = team;
                        this.PluginHost.CreateOneTimeTimer(() => {
                            try { ForceDoubleTeamAssignment(actorNr, targetTeam); }
                            catch (Exception ex) { LogToFile($"RespawnAllPlayers reassign error actor={actorNr}: {ex.Message}"); }
                        }, 250);
                    }
                }
                catch (Exception ex) {
                    LogToFile($"Error in RespawnAllPlayers: {ex}");
                }
            }, 200);
        }

        private void ScheduleFinalStateTransitions()
        {
            _matchFinalizing = true;
            ScheduleFinalPayloadPreload(150);
            LogToFile("Final chain: 201 cannot exit by itself, arming delayed server transition to 202.");
            ScheduleFinalStateTransition(202, 1200, "server");
            TryScheduleRankAssignmentTransition();
        }

        private void ScheduleServerOwnedFinalization()
        {
            if (_serverOwnedFinalizationStarted)
            {
                LogToFile("Server-owned finalization already started; ignoring duplicate 201 request.");
                return;
            }

            _serverOwnedFinalizationStarted = true;
            _matchFinalizing = true;

            this.PluginHost.CreateOneTimeTimer(() => {
                try
                {
                    byte currentC2 = GetCurrentRoomC2();
                    if (currentC2 == 201)
                    {
                        LogToFile("Finalization: room is already at C2=201, preloading payload and arming fallback transitions.");
                        ScheduleFinalStateTransitions();
                        return;
                    }

                    if (currentC2 >= 202)
                    {
                        LogToFile($"Server-owned 201 skipped because room is already finalizing at C2={currentC2}.");
                        return;
                    }

                    Hashtable props = new Hashtable();
                    props["C2"] = (byte)201;
                    props["RouteToGameStateId"] = (byte)0;
                    props["Time"] = GetCurrentRoomTime();

                    LogToFile("Finalization: setting C2=201 for all clients.");
                    this.PluginHost.SetProperties(0, props, null, true);

                    ScheduleFinalStateTransitions();
                }
                catch (Exception ex)
                {
                    LogToFile($"Server-owned finalization error: {ex}");
                }
            }, 50);
        }

        private void ScheduleFinalPayloadPreload(int delayMs)
        {
            this.PluginHost.CreateOneTimeTimer(() => {
                try
                {
                    byte currentC2 = GetCurrentRoomC2();
                    if (currentC2 < 201 || currentC2 >= 202)
                    {
                        LogToFile($"Final payload preload skipped. Current C2={currentC2}.");
                        return;
                    }

                    Hashtable props = new Hashtable();
                    props["Time"] = GetCurrentRoomTime();
                    EnsureFinalPlayers(props);
                    EnsureFinalResultProperties(props);

                    LogToFile("Final payload preload: FinalPlayers, FinalWinTeam, WinTeam and MvpPlayer prepared before C2=202.");
                    this.PluginHost.SetProperties(0, props, null, true);
                }
                catch (Exception ex)
                {
                    LogToFile($"Final payload preload error: {ex}");
                }
            }, delayMs);
        }

        private void ScheduleFinalStatisticsBurstSync()
        {
            // Intentionally disabled. Final data is preloaded once before C2 changes,
            // then the host/client state machine handles the actual transition.
        }

        private void TryScheduleRankAssignmentTransition()
        {
            if (_rankAssignmentTransitionScheduled)
            {
                return;
            }

            string mode = "";
            if (this.PluginHost.GameProperties.ContainsKey("C0"))
            {
                mode = this.PluginHost.GameProperties["C0"] as string ?? "";
            }

            if (!IsRankedMode(mode))
            {
                LogToFile($"RankAssignment transition skipped for non-ranked mode '{mode}'.");
                return;
            }

            if (_lobbyAborted || !_hadLiveRound)
            {
                LogToFile("RankAssignment skipped: lobby aborted or round never started.");
                ScheduleFinalStateTransition(204, 10000, "join-timeout-close");
                return;
            }

            _rankAssignmentTransitionScheduled = true;
            LogToFile("Scheduling ranked transition from FinalStatistics (202) to RankAssignment (203) after statistics display window.");
            ScheduleFinalStateTransition(203, 8000, "server");
            int finalStateDelay = _giveUpFinalization ? GiveUpFinalStateDelayMs : 13000;
            LogToFile($"Scheduling ranked transition from RankAssignment (203) to FinalState (204) after rank display window ({finalStateDelay}ms, giveUp={_giveUpFinalization}).");
            ScheduleFinalStateTransition(204, finalStateDelay, "server");
        }

        private static bool IsRankedMode(string mode)
        {
            return mode == "RankedDefuse" || mode == "Ranked2v2" || mode == "ClanRankedDefuse";
        }

        private void ScheduleForcedFinalStatistics(int delayMs)
        {
            this.PluginHost.CreateOneTimeTimer(() => {
                try
                {
                    Hashtable props = new Hashtable();
                    props["C2"] = (byte)202;
                    props["RouteToGameStateId"] = (byte)0;
                    props["Time"] = GetCurrentRoomTime();
                    EnsureFinalPlayers(props);
                    EnsureFinalResultProperties(props);

                    LogToFile("Debug fast finish: forcing room to FinalStatistics (202).");
                    this.PluginHost.SetProperties(0, props, null, true);
                }
                catch (Exception ex)
                {
                    LogToFile($"Debug fast finish force FinalStatistics error: {ex}");
                }
            }, delayMs);
        }

        private void ScheduleFinalStateTransition(byte targetState, int delayMs, string reason = "scheduled")
        {
            this.PluginHost.CreateOneTimeTimer(() => {
                try {
                    byte currentC2 = GetCurrentRoomC2();
                    if (currentC2 < 201 || currentC2 >= targetState)
                    {
                        LogToFile($"Final transition to {targetState} skipped. Current C2={currentC2}.");
                        return;
                    }

                    LogToFile($"Final transition ({reason}): moving room from C2={currentC2} to {targetState}.");
                    Hashtable props = new Hashtable();
                    if (targetState == 202)
                    {
                        EnsureFinalPlayers(props);
                        EnsureFinalResultProperties(props);
                    }
                    else if (targetState >= 203)
                    {
                        EnsureFinalPlayers(props);
                        EnsureFinalResultProperties(props);
                    }
                    props["C2"] = targetState;
                    props["RouteToGameStateId"] = (byte)0;
                    props["Time"] = GetCurrentRoomTime();
                    // Клиент 0.17: эти флаги убирают «Переподключиться» / «Отключиться»
                    // после завершения матча — остаётся только путь через экран результата.
                    props["MatchEnded"] = true;
                    props["match_ended"] = true;
                    props["AllowReconnect"] = false;
                    props["allow_reconnect"] = false;
                    props["_ShowReconnect"] = false;
                    props["ShowReconnect"] = false;
                    props["IsMatchFinished"] = true;
                    this.PluginHost.SetProperties(0, props, null, true);

                    if (targetState == 203)
                    {
                        try { SyncFinalRankAssignmentResults(); }
                        catch (Exception syncEx) { LogToFile($"Rank sync at C2=203 error: {syncEx.Message}"); }
                    }

                    // Матч дошёл до финала — вход в комнату закрыт, реконнект в него не нужен.
                    if (targetState >= 204)
                    {
                        _rejoinBlocked = true;
                        // Не кикаем сразу: клиент сам уходит по «Продолжить».
                        // Страховка через 5 минут — как на сдаче.
                        if (!_roomCloseScheduled)
                        {
                            _roomCloseScheduled = true;
                            this.PluginHost.CreateOneTimeTimer(() => {
                                try { CloseRoomAndDisconnectAll("match_finished"); }
                                catch (Exception ex) { LogToFile("Match-finished room close error: " + ex.Message); }
                            }, GiveUpRoomCloseDelayMs);
                        }
                    }
                }
                catch (Exception ex) {
                    LogToFile($"Final transition to {targetState} error: {ex}");
                }
            }, delayMs);
        }

        private void EnsureFinalResultProperties(Hashtable props)
        {
            if (!props.ContainsKey("FinalWinTeam"))
            {
                props["FinalWinTeam"] = CreateFinalWinTeam();
                LogToFile("Injected FinalWinTeam into final room properties.");
            }

            if (!props.ContainsKey("WinTeam"))
            {
                props["WinTeam"] = CreateWinTeam();
                LogToFile("Injected WinTeam into final room properties.");
            }

            if (!props.ContainsKey("MvpPlayer"))
            {
                props["MvpPlayer"] = GetMvpActorNr();
                LogToFile($"Injected MvpPlayer={props["MvpPlayer"]} into final room properties.");
            }
        }

        private void EnsureFinalPlayers(Hashtable props)
        {
            // Только whitelist полей: полный копипаст actor.Properties (hits_logs, rank_data…)
            // ломает сериализацию FinalPlayers → у клиента пустые ники и 0/0/0.
            Hashtable existing = null;
            if (props.ContainsKey("FinalPlayers") && props["FinalPlayers"] is Hashtable fp)
                existing = fp;

            string[] scoreKeys = new[]
            {
                "kills", "death", "deaths", "assists", "score",
                "round_kills", "round_assists", "mvp", "ping", "money",
                "team", "uid", "name", "nickname", "playerName",
                "clan_tag", "clan_name", "avatar", "badgeId", "avatarFrameId",
                "isDeath", "bot", "mmr", "current_rank", "Rank", "rank", "RankNew", "rank_id"
            };

            Hashtable finalPlayers = new Hashtable();
            int includedPlayers = 0;
            foreach (var actor in this.PluginHost.GameActors)
            {
                if (actor == null) continue;

                object uidObj = null;
                bool hasUidInProps = actor.Properties != null && actor.Properties.TryGetValue("uid", out uidObj) && uidObj != null;
                object photonUserIdObj = null;
                bool hasPhotonUserIdInProps = actor.Properties != null && actor.Properties.TryGetValue((byte)253, out photonUserIdObj) && photonUserIdObj != null;
                bool hasIdentity = !string.IsNullOrWhiteSpace(actor.UserId);
                if (!hasIdentity && !hasUidInProps && !hasPhotonUserIdInProps)
                    continue;

                Hashtable actorProps = new Hashtable();

                foreach (string key in scoreKeys)
                {
                    object live = null;
                    if (actor.Properties != null && actor.Properties.TryGetValue(key, out live) && live != null)
                        actorProps[key] = live;
                }

                if (existing != null && existing.ContainsKey(actor.ActorNr) && existing[actor.ActorNr] is Hashtable prev)
                {
                    foreach (string key in scoreKeys)
                    {
                        if ((!actorProps.ContainsKey(key) || actorProps[key] == null) && prev.ContainsKey(key) && prev[key] != null)
                            actorProps[key] = prev[key];
                    }
                }

                string mongoId = !string.IsNullOrWhiteSpace(actor.UserId) ? actor.UserId
                    : (hasPhotonUserIdInProps && photonUserIdObj != null ? photonUserIdObj.ToString() : null);

                // Профиль с HTTP: ник, аватар, клан, ранг (для scoreboard как на фото 2).
                PlayerProfileSnap profile = FetchPlayerProfileHttp(mongoId);
                string displayName = profile?.Nickname;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    try
                    {
                        var nickProp = actor.GetType().GetProperty("Nickname") ?? actor.GetType().GetProperty("NickName");
                        if (nickProp != null)
                            displayName = nickProp.GetValue(actor) as string;
                    }
                    catch { }
                }
                if (string.IsNullOrWhiteSpace(displayName) && hasUidInProps && uidObj != null)
                {
                    string u = uidObj.ToString();
                    if (!string.IsNullOrWhiteSpace(u) && u.Length < 48 && !LooksLikeObjectId(u))
                        displayName = u;
                }
                if (string.IsNullOrWhiteSpace(displayName) && actorProps.ContainsKey("name") && actorProps["name"] != null)
                {
                    string n = actorProps["name"].ToString();
                    if (!LooksLikeObjectId(n)) displayName = n;
                }
                if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(actor.UserId) && !LooksLikeObjectId(actor.UserId))
                    displayName = actor.UserId;
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = "Player" + actor.ActorNr;

                // Клиент рисует ник из PhotonPlayer.NickName (= byte 255).
                // Кладем 255 И строковые name/uid — иначе пустые ники в FinalPlayers.
                actorProps[(byte)255] = displayName;
                actorProps["name"] = displayName;
                actorProps["nickname"] = displayName;
                actorProps["playerName"] = displayName;
                string liveUid = null;
                if (hasUidInProps && uidObj != null)
                {
                    string u = uidObj.ToString();
                    if (!string.IsNullOrWhiteSpace(u) && !LooksLikeObjectId(u)
                        && !u.StartsWith("Player", StringComparison.OrdinalIgnoreCase))
                        liveUid = u;
                }
                string accountUid = !string.IsNullOrWhiteSpace(liveUid) ? liveUid
                    : (profile != null ? profile.Uid : null);
                if (string.IsNullOrWhiteSpace(accountUid) || LooksLikeObjectId(accountUid))
                    accountUid = !string.IsNullOrWhiteSpace(liveUid) ? liveUid : ("Player" + actor.ActorNr);
                actorProps["uid"] = accountUid;

                if (profile != null)
                {
                    if (!string.IsNullOrWhiteSpace(profile.ClanTag)
                        && (!actorProps.ContainsKey("clan_tag") || actorProps["clan_tag"] == null
                            || string.IsNullOrWhiteSpace(actorProps["clan_tag"].ToString())))
                        actorProps["clan_tag"] = profile.ClanTag;
                    if (!string.IsNullOrWhiteSpace(profile.ClanName)
                        && (!actorProps.ContainsKey("clan_name") || actorProps["clan_name"] == null
                            || string.IsNullOrWhiteSpace(actorProps["clan_name"].ToString())))
                        actorProps["clan_name"] = profile.ClanName;
                    if (profile.CurrentRank != 0)
                        actorProps["current_rank"] = profile.CurrentRank;
                }

                if (!actorProps.ContainsKey("team") || actorProps["team"] == null)
                    actorProps["team"] = GetActorTeamWithFallback(actor, actor.ActorNr);

                actorProps["kills"] = ReadActorInt(actor, "kills", actorProps);
                int deathVal = ReadActorInt(actor, "death", actorProps);
                if (deathVal == 0) deathVal = ReadActorInt(actor, "deaths", actorProps);
                actorProps["death"] = deathVal;
                actorProps["deaths"] = deathVal;
                actorProps["assists"] = ReadActorInt(actor, "assists", actorProps);
                actorProps["score"] = ReadActorInt(actor, "score", actorProps);
                actorProps["round_kills"] = ReadActorInt(actor, "round_kills", actorProps);
                actorProps["round_assists"] = ReadActorInt(actor, "round_assists", actorProps);
                actorProps["mvp"] = ReadActorInt(actor, "mvp", actorProps);

                int pingVal = ReadActorInt(actor, "ping", actorProps);
                if (pingVal <= 0) pingVal = ReadActorPingFallback(actor);
                actorProps["ping"] = pingVal < 0 ? 0 : pingVal;

                if (!actorProps.ContainsKey("money") || actorProps["money"] == null)
                    actorProps["money"] = 0;
                if (!actorProps.ContainsKey("isDeath") || actorProps["isDeath"] == null)
                    actorProps["isDeath"] = false;
                if (!actorProps.ContainsKey("clan_tag") || actorProps["clan_tag"] == null)
                    actorProps["clan_tag"] = "";
                // Аватар: Byte[] скина с живого актора. UUID-строка → "0".
                object liveAvatar = actorProps.ContainsKey("avatar") ? actorProps["avatar"] : null;
                if (liveAvatar is byte[] avBytes && avBytes.Length > 0)
                    actorProps["avatar"] = avBytes;
                else if (liveAvatar is string avStr && avStr.Length > 0 && avStr.Length <= 8 && avStr.All(char.IsDigit))
                    actorProps["avatar"] = avStr;
                else
                    actorProps["avatar"] = "0";

                // Медальки игрока: badgeId как int (item definition). Не подменять рангом.
                object liveBadge = actorProps.ContainsKey("badgeId") ? actorProps["badgeId"] : null;
                if (liveBadge is byte[])
                    actorProps.Remove("badgeId");
                else if (liveBadge != null)
                {
                    try { actorProps["badgeId"] = Convert.ToInt32(liveBadge); }
                    catch { }
                }

                object liveFrame = actorProps.ContainsKey("avatarFrameId") ? actorProps["avatarFrameId"] : null;
                if (liveFrame is byte[])
                    actorProps.Remove("avatarFrameId");
                else if (liveFrame != null)
                {
                    try { actorProps["avatarFrameId"] = Convert.ToInt32(liveFrame); }
                    catch { }
                }

                // Живой current_rank = int 1..17 (как FetchRankedInfo). Byte/icon туда
                // нельзя: второй игрок не входит в союзники/соревы.
                int rankForUi = ResolveClientRankForScoreboard(actor, profile);
                if (rankForUi < 1) rankForUi = 1;
                if (rankForUi > 17) rankForUi = 17;
                int rankIcon = rankForUi >= 17 ? 16 : rankForUi - 1;
                if (rankIcon < 0) rankIcon = 0;
                actorProps["current_rank"] = rankIcon;
                actorProps["Rank"] = rankIcon;
                actorProps["rank"] = rankIcon;
                actorProps["rank_id"] = rankIcon;
                actorProps["RankNew"] = rankForUi;

                string rankJson = null;
                try
                {
                    object liveJson;
                    if (actor.Properties != null
                        && actor.Properties.TryGetValue("rank_assigment_result_prop", out liveJson)
                        && liveJson is string sJson && !string.IsNullOrWhiteSpace(sJson)
                        && sJson.IndexOf("RankNew", StringComparison.Ordinal) >= 0)
                        rankJson = sJson;
                    if (string.IsNullOrWhiteSpace(rankJson))
                    {
                        float mmrNow = GetActorFloatProperty(actor.ActorNr, "mmr", 1000f);
                        float mmrStart = GetActorFloatProperty(actor.ActorNr, "mmr_start", mmrNow);
                        int rankStart = GetActorIntProperty(actor.ActorNr, "rank_start", rankForUi);
                        rankJson = BuildRankAssignmentResultJson(
                            actor.ActorNr, mmrStart, mmrNow, rankStart, rankForUi, 10, 10);
                    }
                }
                catch (Exception jsonEx)
                {
                    LogToFile("EnsureFinalPlayers rank json fail actor=" + actor.ActorNr + ": " + jsonEx.Message);
                }

                try
                {
                    Hashtable liveSync = new Hashtable();
                    liveSync[(byte)255] = displayName;
                    liveSync["name"] = displayName;
                    liveSync["nickname"] = displayName;
                    liveSync["clan_tag"] = actorProps["clan_tag"];
                    liveSync["clan_name"] = actorProps.ContainsKey("clan_name") ? actorProps["clan_name"] : "";
                    // uid и current_rank на живом акторе не трогаем: byte/PlayerN
                    // ломает вход второго в союзники/соревы. RankNew — для ячейки ранга.
                    liveSync["RankNew"] = rankForUi;
                    liveSync["ping"] = actorProps["ping"];
                    if (actorProps.ContainsKey("badgeId") && actorProps["badgeId"] != null)
                        liveSync["badgeId"] = actorProps["badgeId"];
                    if (actorProps.ContainsKey("avatar") && actorProps["avatar"] != null)
                        liveSync["avatar"] = actorProps["avatar"];
                    if (!string.IsNullOrWhiteSpace(rankJson))
                    {
                        liveSync["rank_assigment_result_prop"] = rankJson;
                        liveSync["rank_data"] = rankJson;
                    }
                    this.PluginHost.SetProperties(actor.ActorNr, liveSync, null, true);
                }
                catch (Exception ex)
                {
                    LogToFile("EnsureFinalPlayers liveSync fail actor=" + actor.ActorNr + ": " + ex.Message);
                }

                finalPlayers[actor.ActorNr] = actorProps;
                includedPlayers++;
                    LogToFile($"FinalPlayers[{actor.ActorNr}] name={displayName} uid={actorProps["uid"]} ping={actorProps["ping"]} rankClient={rankForUi} rankIcon={rankIcon} clan={actorProps["clan_tag"]} K/D/A={actorProps["kills"]}/{actorProps["death"]}/{actorProps["assists"]} score={actorProps["score"]} team={actorProps["team"]}");
            }

            props["FinalPlayers"] = finalPlayers;
            LogToFile($"Injected FinalPlayers with {finalPlayers.Count} players into final room properties (included={includedPlayers}, totalActors={this.PluginHost.GameActors.Count}).");
        }

        private sealed class PlayerProfileSnap
        {
            public string Uid;
            public string Nickname;
            public string Avatar;
            public string ClanTag;
            public string ClanName;
            public int CurrentRank;
        }

        private readonly ConcurrentDictionary<string, PlayerProfileSnap> _profileCache =
            new ConcurrentDictionary<string, PlayerProfileSnap>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Client rank 1..17 для scoreboard. Живой current_rank — int из FetchRankedInfo.
        /// Byte/0 пропускаем: это icon после старого liveSync, не client-id.
        /// </summary>
        private int ResolveClientRankForScoreboard(IActor actor, PlayerProfileSnap profile)
        {
            try
            {
                object rn;
                if (actor != null && actor.Properties != null
                    && actor.Properties.TryGetValue("RankNew", out rn) && rn != null)
                {
                    int v = Convert.ToInt32(rn);
                    if (v >= 1 && v <= 17) return v;
                }
            }
            catch { }
            try
            {
                object cr;
                if (actor != null && actor.Properties != null
                    && actor.Properties.TryGetValue("current_rank", out cr) && cr != null
                    && !(cr is byte))
                {
                    int v = Convert.ToInt32(cr);
                    if (v >= 1 && v <= 17) return v;
                }
            }
            catch { }
            if (profile != null && profile.CurrentRank >= 1 && profile.CurrentRank <= 17)
                return profile.CurrentRank;
            try
            {
                int mmr = 0;
                if (actor != null)
                    mmr = (int)Math.Round(GetActorFloatProperty(actor.ActorNr, "mmr", 0f));
                int fromMmr = ClientRankFromMmr(mmr, 0);
                if (fromMmr >= 1 && fromMmr <= 17) return fromMmr;
            }
            catch { }
            return 1;
        }

        private PlayerProfileSnap FetchPlayerProfileHttp(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return null;
            string mode = "RankedDefuse";
            try
            {
                if (this.PluginHost != null && this.PluginHost.GameProperties != null
                    && this.PluginHost.GameProperties.ContainsKey("C0"))
                {
                    string c0 = this.PluginHost.GameProperties["C0"] as string;
                    if (!string.IsNullOrWhiteSpace(c0)) mode = c0;
                }
            }
            catch { }
            string cacheKey = playerId + "|" + mode;
            if (_profileCache.TryGetValue(cacheKey, out PlayerProfileSnap cached))
                return cached;
            try
            {
                string baseUrl = string.IsNullOrWhiteSpace(serverUrl) ? "http://127.0.0.1:2224" : serverUrl.TrimEnd('/');
                string url = baseUrl + "/api/player/ranked-info?PlayerId=" + Uri.EscapeDataString(playerId)
                    + "&Mode=" + Uri.EscapeDataString(mode);
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 900;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var stream = resp.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    string body = reader.ReadToEnd();
                    var snap = new PlayerProfileSnap
                    {
                        Uid = ParseJsonString(body, "uid")
                            ?? ParseJsonString(body, "Uid"),
                        Nickname = ParseJsonString(body, "name")
                            ?? ParseJsonString(body, "Name")
                            ?? ParseJsonString(body, "nickname")
                            ?? ParseJsonString(body, "Nickname"),
                        Avatar = ParseJsonString(body, "avatar") ?? ParseJsonString(body, "avatarId") ?? "",
                        ClanTag = ParseJsonString(body, "clan_tag") ?? "",
                        ClanName = ParseJsonString(body, "clan_name") ?? "",
                        CurrentRank = ParseIntFromJson(body, "current_rank", 0)
                    };
                    if (!string.IsNullOrWhiteSpace(snap.Uid) && LooksLikeObjectId(snap.Uid))
                        snap.Uid = null;
                    if (!string.IsNullOrWhiteSpace(snap.Nickname) && LooksLikeObjectId(snap.Nickname))
                        snap.Nickname = null;
                    _profileCache[cacheKey] = snap;
                    return snap;
                }
            }
            catch (Exception ex)
            {
                LogToFile("FetchPlayerProfileHttp failed for " + playerId + ": " + ex.Message);
            }
            return null;
        }

        private static string ParseJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int colon = json.IndexOf(':', i + needle.Length);
            if (colon < 0) return null;
            int p = colon + 1;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            if (p >= json.Length || json[p] != '"') return null;
            int q1 = p;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 <= q1) return null;
            string val = json.Substring(q1 + 1, q2 - q1 - 1);
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }

        private static int ReadActorPingFallback(IActor actor)
        {
            if (actor?.Properties == null) return 0;
            try
            {
                object v;
                if (actor.Properties.TryGetValue("ping", out v) && v != null)
                    return Convert.ToInt32(v);
                if (actor.Properties.TryGetValue("Ping", out v) && v != null)
                    return Convert.ToInt32(v);
            }
            catch { }
            return 0;
        }

        private string FetchPlayerDisplayNameHttp(string playerId)
        {
            var snap = FetchPlayerProfileHttp(playerId);
            return snap?.Nickname;
        }

        private static int ReadActorInt(IActor actor, string key, Hashtable fallback)
        {
            try
            {
                object v;
                if (actor?.Properties != null && actor.Properties.TryGetValue(key, out v) && v != null)
                    return Convert.ToInt32(v);
            }
            catch { }
            try
            {
                if (fallback != null && fallback.ContainsKey(key) && fallback[key] != null)
                    return Convert.ToInt32(fallback[key]);
            }
            catch { }
            return 0;
        }

        private static string CanonicalMatchId(string roomOrMatchId)
        {
            if (string.IsNullOrWhiteSpace(roomOrMatchId))
                return Guid.NewGuid().ToString("N");
            string s = roomOrMatchId.Trim();
            int us = s.LastIndexOf('_');
            if (us >= 0 && us < s.Length - 1)
            {
                string tail = s.Substring(us + 1).Trim();
                if (IsHex32(tail)) return tail.ToLowerInvariant();
            }
            if (IsHex32(s)) return s.ToLowerInvariant();
            return s;
        }

        private static bool IsHex32(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32) return false;
            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static bool LooksLikeObjectId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 24) return false;
            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private void CopyActorProperty(IActor actor, Hashtable target, string key)
        {
            object value;
            if (actor.Properties.TryGetValue(key, out value) && value != null)
            {
                target[key] = value;
            }
        }

        private void ScheduleFinalStateSync()
        {
            this.PluginHost.CreateOneTimeTimer(() => {
                try
                {
                    byte currentC2 = GetCurrentRoomC2();
                    if (currentC2 < 200)
                    {
                        LogToFile($"Final state sync skipped. Current C2={currentC2}.");
                        return;
                    }

                    Hashtable props = new Hashtable();
                    // Keep the current final-state phase (202/203/204...) and only ensure missing final payload.
                    props["C2"] = currentC2;
                    props["RouteToGameStateId"] = (byte)0;
                    props["Time"] = GetCurrentRoomTime();
                    EnsureFinalPlayers(props);
                    EnsureFinalResultProperties(props);

                    LogToFile($"Final state sync: preserving C2={currentC2}, clearing RouteToGameStateId and final result props.");
                    this.PluginHost.SetProperties(0, props, null, true);
                }
                catch (Exception ex)
                {
                    LogToFile($"Final state sync error: {ex}");
                }
            }, 100);
        }

        private bool ReadIsGiveUp()
        {
            try
            {
                if (this.PluginHost.GameProperties == null) return false;
                if (this.PluginHost.GameProperties.ContainsKey("IsGiveUp"))
                    return Convert.ToBoolean(this.PluginHost.GameProperties["IsGiveUp"]);
                if (this.PluginHost.GameProperties.ContainsKey("isGiveUp"))
                    return Convert.ToBoolean(this.PluginHost.GameProperties["isGiveUp"]);
            }
            catch { }
            return false;
        }

        private void ReportMatchResultOnce()
        {
            if (_lobbyAborted)
            {
                LogToFile("ReportMatchResult skipped: lobby aborted (no MMR).");
                return;
            }
            if (!_hadLiveRound && !ReadIsGiveUp())
            {
                byte c2 = 0;
                try { c2 = GetCurrentRoomC2(); } catch { }
                if (c2 < 22 && !_matchFinalizing)
                {
                    LogToFile("ReportMatchResult skipped: no live round (warmup/wait). No MMR.");
                    return;
                }
                LogToFile("ReportMatchResultOnce: no live-round flag but C2=" + c2 + " — saving ranked history anyway");
            }
            if (_matchResultReported)
            {
                LogToFile("ReportMatchResult skipped: result was already reported for this room.");
                return;
            }

            _matchResultReported = true;
            ReportMatchResult();
            // НЕ шлём IsLeaving здесь: клиент ещё на экране статистики (C2=201..205).
            // Статус чистится из OnLeave / OnCloseGame.
        }

        private Hashtable CreateFinalWinTeam()
        {
            bool isGiveUp = false;
            try
            {
                if (this.PluginHost.GameProperties != null)
                {
                    if (this.PluginHost.GameProperties.ContainsKey("IsGiveUp"))
                        isGiveUp = Convert.ToBoolean(this.PluginHost.GameProperties["IsGiveUp"]);
                    else if (this.PluginHost.GameProperties.ContainsKey("isGiveUp"))
                        isGiveUp = Convert.ToBoolean(this.PluginHost.GameProperties["isGiveUp"]);
                }
            }
            catch { }

            int winnerTeam;
            if (isGiveUp && _surrenderTeam != 0)
            {
                winnerTeam = _surrenderTeam == TEAM_CT ? TEAM_TERRORISTS : TEAM_CT;
                LogToFile($"CreateFinalWinTeam: GiveUp team={_surrenderTeam} → winner={winnerTeam} (not draw).");
            }
            else
            {
                winnerTeam = GetFinalWinnerTeam();
            }

            Hashtable finalWinTeam = new Hashtable();
            finalWinTeam["isDraw"] = !isGiveUp && winnerTeam == 0;
            finalWinTeam["isGiveUp"] = isGiveUp;
            finalWinTeam["team"] = (byte)winnerTeam;
            return finalWinTeam;
        }

        private Hashtable CreateWinTeam()
        {
            int mvpActorNr = GetMvpActorNr();
            var mvpActor = this.PluginHost.GameActors.FirstOrDefault(a => a.ActorNr == mvpActorNr);
            string mvpPlayerId = mvpActor?.UserId ?? "";
            if (string.IsNullOrWhiteSpace(mvpPlayerId) && mvpActor?.Properties != null && mvpActor.Properties.TryGetValue("uid", out object uidObj) && uidObj != null)
            {
                mvpPlayerId = uidObj.ToString();
            }

            Hashtable winTeam = new Hashtable();
            winTeam["team"] = (byte)GetFinalWinnerTeam();
            winTeam["mvpPlayer"] = mvpActorNr;
            winTeam["mvpPlayerId"] = mvpPlayerId ?? "";
            winTeam["mvpCode"] = 0;
            return winTeam;
        }

        private int GetFinalWinnerTeam()
        {
            int trScore = 0;
            int ctScore = 0;
            ResolveMatchScores(out trScore, out ctScore);

            // TrScore = команда T (1), CtScore = команда CT (2). Без инверсии.
            if (trScore > ctScore)
                return TEAM_TERRORISTS;
            if (ctScore > trScore)
                return TEAM_CT;
            return 0;
        }

        private int GetMvpActorNr()
        {
            int bestActorNr = 0;
            int bestScore = int.MinValue;

            foreach (var actor in this.PluginHost.GameActors)
            {
                if (!actor.IsActive)
                {
                    continue;
                }

                int score = 0;
                object scoreObj;
                if (actor.Properties.TryGetValue("score", out scoreObj) && scoreObj != null)
                {
                    try
                    {
                        score = Convert.ToInt32(scoreObj);
                    }
                    catch
                    {
                        score = 0;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestActorNr = actor.ActorNr;
                }
            }

            return bestActorNr;
        }

        private int GetRoomInt(string key)
        {
            try
            {
                if (this.PluginHost.GameProperties.ContainsKey(key) && this.PluginHost.GameProperties[key] != null)
                {
                    return Convert.ToInt32(this.PluginHost.GameProperties[key]);
                }
            }
            catch {}

            return 0;
        }

        private void NoteRoomScoreProp(object key, object value)
        {
            if (key == null || value == null) return;
            string name = key.ToString();
            int n;
            try { n = Convert.ToInt32(value); }
            catch { return; }
            if (n < 0) n = 0;
            if (string.Equals(name, "TrScore", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "score1", StringComparison.OrdinalIgnoreCase))
                _seenTrScore = n;
            else if (string.Equals(name, "CtScore", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "score2", StringComparison.OrdinalIgnoreCase))
                _seenCtScore = n;
            else
                return;
            if (_seenTrScore + _seenCtScore > 0)
            {
                _lastLiveTrScore = _seenTrScore;
                _lastLiveCtScore = _seenCtScore;
            }
        }

        private void ResolveMatchScores(out int tr, out int ct)
        {
            tr = GetRoomInt("TrScore");
            ct = GetRoomInt("CtScore");
            if (tr + ct == 0 && _lastLiveTrScore + _lastLiveCtScore > 0)
            {
                tr = _lastLiveTrScore;
                ct = _lastLiveCtScore;
                LogToFile($"ResolveMatchScores: room 0-0 → last live {tr}-{ct}");
            }
        }

        private byte GetCurrentRoomC2()
        {
            try {
                if (this.PluginHost.GameProperties.ContainsKey("C2") && this.PluginHost.GameProperties["C2"] != null)
                {
                    return Convert.ToByte(this.PluginHost.GameProperties["C2"]);
                }
            }
            catch {}
            return 0;
        }

        private double GetCurrentRoomTime()
        {
            try {
                if (this.PluginHost.GameProperties.ContainsKey("Time") && this.PluginHost.GameProperties["Time"] != null)
                {
                    return Convert.ToDouble(this.PluginHost.GameProperties["Time"]);
                }
            }
            catch {}
            return 0.0;
        }

        private bool HasWarmupElapsed(int minMs = 30000)
        {
            if (_warmupStartedAtMs <= 0) return false;
            long elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _warmupStartedAtMs;
            return elapsed >= minMs;
        }

        private bool TryBlockEarlyRoundState(int requestedC2)
        {
            if (requestedC2 != 21 && requestedC2 != 22) return false;
            if (!IsRankedRoomMode()) return false;
            if (_roundStartArmed || _hadLiveRound) return false;
            if (HasWarmupElapsed()) return false;
            return true;
        }

        private void StartWarmUpTimer()
        {
            if (_warmupStartedAtMs == 0)
                _warmupStartedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _joinWaitActive = false;
            if (_warmupTimerArmed)
                return;
            try { BroadcastWarmUpSpawnsWithDelayedTimers(); } catch { }
            try
            {
                Hashtable clock = new Hashtable();
                clock["Time"] = 30.0;
                clock["WarmUpDuration"] = 30.0f;
                clock["C2"] = (byte)11;
                clock["RouteToGameStateId"] = (byte)0;
                clock["MatchStarted"] = true;
                clock["WaitPlayersUi"] = "";
                clock["WaitPlayersTimeout"] = 0f;
                clock["AllowReconnect"] = false;
                clock["_ShowReconnect"] = false;
                clock["ShowReconnect"] = false;
                this.PluginHost.SetProperties(0, clock, null, true);
            }
            catch { }
            _warmupTimerArmed = true;
            const int warmupMs = 30000;
            LogToFile($"WarmUp started ONLY after all joined. Client 30s; server forces round in {warmupMs}ms if still on 11.");
            this.PluginHost.CreateOneTimeTimer(() =>
            {
                try
                {
                    if (_matchFinalizing || _lobbyAborted || _hadLiveRound || _roundStartArmed) return;
                    byte c2 = GetCurrentRoomC2();
                    if (c2 != 11 && c2 != 21) return;
                    LogToFile($"WarmUp watchdog: still C2={c2} after {warmupMs}ms. Forcing round start.");
                    if (c2 == 11)
                    {
                        Hashtable props = new Hashtable();
                        props["C2"] = (byte)21;
                        props["RouteToGameStateId"] = (byte)21;
                        props["MatchStarted"] = true;
                        this.PluginHost.SetProperties(0, props, null, true);
                    }
                    ArmRoundStart(4000);
                }
                catch (Exception ex)
                {
                    LogToFile($"WarmUp watchdog error: {ex.Message}");
                }
            }, warmupMs);
        }

        private void ArmRoundStart(int delayMs)
        {
            if (_roundStartArmed || _hadLiveRound || _lobbyAborted) return;
            _roundStartArmed = true;
            if (delayMs < 0) delayMs = 0;
            this.PluginHost.CreateOneTimeTimer(() =>
            {
                try
                {
                    if (_hadLiveRound || _lobbyAborted || _matchFinalizing) return;
                    byte checkC2 = GetCurrentRoomC2();
                    if (checkC2 != 11 && checkC2 != 21) return;
                    _hadLiveRound = true;
                    LogToFile("WarmUp→Round: C2=22 RoundStarting, reset equipment.");
                    Hashtable props22 = new Hashtable();
                    props22["C2"] = (byte)22;
                    props22["RouteToGameStateId"] = (byte)22;
                    int currentRound = 0;
                    if (this.PluginHost.GameProperties.ContainsKey("Round") && this.PluginHost.GameProperties["Round"] != null)
                        currentRound = Convert.ToInt32(this.PluginHost.GameProperties["Round"]);
                    props22["Round"] = currentRound + 1;
                    double timeVal = GetCurrentRoomTime();
                    props22["RoundStartTime"] = timeVal;
                    props22["Time"] = timeVal;
                    this.PluginHost.SetProperties(0, props22, null, true);
                    RespawnAllPlayers();
                }
                catch (Exception timerEx)
                {
                    LogToFile($"ArmRoundStart error: {timerEx}");
                }
            }, delayMs);
        }

        private sealed class TrackedPlayerScore
        {
            public string PlayerId;
            public int ActorNr;
            public byte Team;
            public int Kills;
            public int Deaths;
            public int Assists;
            public int Score;
        }

        private void RememberTrackedPlayerId(string playerId, int actorNr)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            _trackedPlayers.AddOrUpdate(playerId,
                _ => new TrackedPlayerScore { PlayerId = playerId, ActorNr = actorNr, Team = (byte)((actorNr % 2 == 1) ? 1 : 2) },
                (_, existing) =>
                {
                    if (existing == null) existing = new TrackedPlayerScore { PlayerId = playerId };
                    existing.ActorNr = actorNr;
                    return existing;
                });
        }

        private void RememberTrackedPlayer(IActor actor)
        {
            if (actor == null) return;
            string playerId = actor.UserId;
            try
            {
                object uidObj;
                if (string.IsNullOrWhiteSpace(playerId) && actor.Properties != null
                    && actor.Properties.TryGetValue("uid", out uidObj) && uidObj != null)
                    playerId = uidObj.ToString();
            }
            catch { }
            if (string.IsNullOrWhiteSpace(playerId)) return;

            var snap = new TrackedPlayerScore
            {
                PlayerId = playerId,
                ActorNr = actor.ActorNr,
                Team = GetActorTeamWithFallback(actor, actor.ActorNr)
            };
            if (snap.Team == TEAM_NONE || snap.Team == TEAM_SPECTATOR)
                snap.Team = (byte)((actor.ActorNr % 2 == 1) ? TEAM_TERRORISTS : TEAM_CT);
            try
            {
                object v;
                if (actor.Properties != null && actor.Properties.TryGetValue("kills", out v) && v != null)
                    snap.Kills = Convert.ToInt32(v);
                if (actor.Properties != null && actor.Properties.TryGetValue("assists", out v) && v != null)
                    snap.Assists = Convert.ToInt32(v);
                if (actor.Properties != null && actor.Properties.TryGetValue("death", out v) && v != null)
                    snap.Deaths = Convert.ToInt32(v);
                else if (actor.Properties != null && actor.Properties.TryGetValue("deaths", out v) && v != null)
                    snap.Deaths = Convert.ToInt32(v);
                if (actor.Properties != null && actor.Properties.TryGetValue("score", out v) && v != null)
                    snap.Score = Convert.ToInt32(v);
            }
            catch { }
            _trackedPlayers[playerId] = snap;
        }

        /// <summary>Название карты матча из room property C1 (например «Province 2x2»).</summary>
        private string GetRoomMapName()
        {
            try
            {
                if (this.PluginHost.GameProperties != null && this.PluginHost.GameProperties.ContainsKey("C1"))
                {
                    string c1 = this.PluginHost.GameProperties["C1"] as string;
                    if (!string.IsNullOrWhiteSpace(c1)) return c1.Trim();
                }
            }
            catch { }
            return "";
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private void ReportMatchResult()
        {
            try
            {
                LogToFile("ReportMatchResult: Preparing to send match results to RpcServer.");
                
                string mode = "Training";
                if (this.PluginHost.GameProperties.ContainsKey("C0"))
                {
                    mode = this.PluginHost.GameProperties["C0"] as string ?? "Training";
                }
                
                string serverMode = "ranked";
                if (mode == "Ranked2v2")
                {
                    serverMode = "allies";
                }
                
                int winnerTeam = 0;
                int score1 = 0;
                int score2 = 0;
                ResolveMatchScores(out score1, out score2);

                bool isGiveUp = ReadIsGiveUp();
                if (isGiveUp && _surrenderTeam != 0)
                {
                    winnerTeam = _surrenderTeam == TEAM_CT ? TEAM_TERRORISTS : TEAM_CT;
                    LogToFile("ReportMatchResult: GiveUp — surrender team=" + _surrenderTeam + " winner=" + winnerTeam);
                }
                else if (score1 > score2)
                {
                    winnerTeam = TEAM_TERRORISTS;
                }
                else if (score2 > score1)
                {
                    winnerTeam = TEAM_CT;
                }

                if (!_hadLiveRound && !isGiveUp)
                {
                    byte c2 = GetCurrentRoomC2();
                    if (c2 < 22 && !_matchFinalizing)
                    {
                        LogToFile("ReportMatchResult cancelled: no live round.");
                        return;
                    }
                }
                
                LogToFile("ReportMatchResult: Winner team is " + winnerTeam + " (TrScore=" + score1 + ", CtScore=" + score2 + "; T=1 CT=2) giveUp=" + isGiveUp);

                StringBuilder jsonBuilder = new StringBuilder();
                jsonBuilder.Append("{");
                jsonBuilder.Append("\"MatchId\":\"" + CanonicalMatchId(this.PluginHost.GameId) + "\",");
                jsonBuilder.Append("\"RoomId\":\"" + (this.PluginHost.GameId ?? "") + "\",");
                // Карта матча (room prop C1). Без неё история матчей на клиенте не может
                // построить строку списка: id комнаты названия карты не содержит.
                jsonBuilder.Append("\"Map\":\"" + JsonEscape(GetRoomMapName()) + "\",");
                jsonBuilder.Append("\"WinnerTeam\":" + winnerTeam + ",");
                jsonBuilder.Append("\"Mode\":\"" + serverMode + "\",");
                jsonBuilder.Append("\"TrScore\":" + score1 + ",");
                jsonBuilder.Append("\"CtScore\":" + score2 + ",");
                jsonBuilder.Append("\"IsGiveUp\":" + (isGiveUp ? "true" : "false") + ",");
                jsonBuilder.Append("\"HadLiveRound\":" + (_hadLiveRound ? "true" : "false") + ",");
                jsonBuilder.Append("\"Scores\":[");
                
                bool first = true;
                var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var actor in this.PluginHost.GameActors)
                {
                    RememberTrackedPlayer(actor);
                    string playerId = actor.UserId;
                    object uidObj;
                    if (string.IsNullOrWhiteSpace(playerId) && actor.Properties.TryGetValue("uid", out uidObj) && uidObj != null)
                        playerId = uidObj.ToString();
                    if (string.IsNullOrWhiteSpace(playerId))
                        continue;
                    if (!emitted.Add(playerId)) continue;

                    byte team = GetActorTeamWithFallback(actor, actor.ActorNr);
                    if (team == TEAM_NONE || team == TEAM_SPECTATOR)
                    {
                        team = (actor.ActorNr % 2 == 1) ? TEAM_TERRORISTS : TEAM_CT;
                        LogToFile($"ReportMatchResult: actor={actor.ActorNr} had no team → forced {team}");
                    }

                    int kills = 0, assists = 0, deaths = 0, score = 0;
                    object killsVal;
                    if (actor.Properties.TryGetValue("kills", out killsVal) && killsVal != null)
                        kills = Convert.ToInt32(killsVal);
                    object assistsVal;
                    if (actor.Properties.TryGetValue("assists", out assistsVal) && assistsVal != null)
                        assists = Convert.ToInt32(assistsVal);
                    object deathsVal;
                    if (actor.Properties.TryGetValue("death", out deathsVal) && deathsVal != null)
                        deaths = Convert.ToInt32(deathsVal);
                    else if (actor.Properties.TryGetValue("deaths", out deathsVal) && deathsVal != null)
                        deaths = Convert.ToInt32(deathsVal);
                    object actorScoreVal;
                    if (actor.Properties.TryGetValue("score", out actorScoreVal) && actorScoreVal != null)
                        score = Convert.ToInt32(actorScoreVal);

                    if (!first) jsonBuilder.Append(",");
                    first = false;
                    jsonBuilder.Append("{");
                    jsonBuilder.Append("\"PlayerId\":\"" + JsonEscape(playerId) + "\",");
                    jsonBuilder.Append("\"Kills\":" + kills + ",");
                    jsonBuilder.Append("\"Assists\":" + assists + ",");
                    jsonBuilder.Append("\"Deaths\":" + deaths + ",");
                    jsonBuilder.Append("\"Score\":" + score + ",");
                    jsonBuilder.Append("\"Team\":" + (int)team);
                    jsonBuilder.Append("}");
                }
                foreach (var kv in _trackedPlayers)
                {
                    if (kv.Value == null || string.IsNullOrWhiteSpace(kv.Value.PlayerId)) continue;
                    if (!emitted.Add(kv.Value.PlayerId)) continue;
                    if (!first) jsonBuilder.Append(",");
                    first = false;
                    jsonBuilder.Append("{");
                    jsonBuilder.Append("\"PlayerId\":\"" + JsonEscape(kv.Value.PlayerId) + "\",");
                    jsonBuilder.Append("\"Kills\":" + kv.Value.Kills + ",");
                    jsonBuilder.Append("\"Assists\":" + kv.Value.Assists + ",");
                    jsonBuilder.Append("\"Deaths\":" + kv.Value.Deaths + ",");
                    jsonBuilder.Append("\"Score\":" + kv.Value.Score + ",");
                    jsonBuilder.Append("\"Team\":" + (int)kv.Value.Team);
                    jsonBuilder.Append("}");
                    LogToFile("ReportMatchResult: included left player " + kv.Value.PlayerId);
                }
                jsonBuilder.Append("]");
                jsonBuilder.Append("}");
                string json = jsonBuilder.ToString();
                
                LogToFile("ReportMatchResult: Payload JSON: " + json);
                
                var request = new HttpRequest
                {
                    Url = serverUrl + "/api/match/result",
                    Method = "POST",
                    Accept = "application/json",
                    ContentType = "application/json",
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(json)),
            // После HTTP /api/match/result (MMR+rewards) — сразу синк rank props,
            // затем ещё раз перед показом 203, чтобы экран звания не был пустым.
            Callback = (response, userState) => {
                            if (response.Status == HttpRequestQueueResult.Success && response.HttpCode == 200)
                            {
                                LogToFile("ReportMatchResult: Match results successfully reported to RpcServer!");
                                this.PluginHost.CreateOneTimeTimer(() => {
                                    try { SyncFinalRankAssignmentResults(); }
                                    catch (Exception ex) { LogToFile("Delayed rank sync error: " + ex.Message); }
                                }, 800);
                                this.PluginHost.CreateOneTimeTimer(() => {
                                    try { SyncFinalRankAssignmentResults(); }
                                    catch (Exception ex) { LogToFile("Second rank sync error: " + ex.Message); }
                                }, 2500);
                                this.PluginHost.CreateOneTimeTimer(() => {
                                    try { SyncFinalRankAssignmentResults(); }
                                    catch (Exception ex) { LogToFile("Third rank sync error: " + ex.Message); }
                                }, 7000);
                            }
                            else
                            {
                                LogToFile("ReportMatchResult: Failed to report match results. Status: " + response.Status + ", HttpCode: " + response.HttpCode);
                            }
                    }
                };
                
                this.PluginHost.HttpRequest(request);
            }
            catch (Exception ex)
            {
                LogToFile("ReportMatchResult error: " + ex);
            }
        }

        private bool IsSoulHunt()
        {
            return string.Equals(pluginName, "UnevenTeamsGamePlugin", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(this.PluginHost?.GameId)
                    && this.PluginHost.GameId.StartsWith("SoulHunt", StringComparison.OrdinalIgnoreCase));
        }

        private bool IsSantaClaus()
        {
            return string.Equals(pluginName, "SantaClausGamePlugin", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(this.PluginHost?.GameId)
                    && this.PluginHost.GameId.StartsWith("SantaClaus", StringComparison.OrdinalIgnoreCase));
        }

        private void AssignSoulHuntTeam(int actorNr)
        {
            try
            {
                if (!this.PluginHost.GameActors.Any(a => a.ActorNr == actorNr)) return;
                Hashtable props = new Hashtable();
                props["team"] = TEAM_CT;
                this.PluginHost.SetProperties(actorNr, props, null, true);
                this.PluginHost.LogInfo($"MatchmakingPlugin: SoulHunt - Actor {actorNr} auto-assigned to CT");
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: Error in AssignSoulHuntTeam: {ex}");
            }
        }

        private void StartSoulHuntMatchIfReady()
        {
            try
            {
                if (this.PluginHost.GameActors.Count != 2) return;
                var terrorist = this.PluginHost.GameActors.OrderByDescending(a => a.ActorNr).First();
                Hashtable terroristProps = new Hashtable();
                terroristProps["team"] = TEAM_TERRORISTS;
                this.PluginHost.SetProperties(terrorist.ActorNr, terroristProps, null, true);
                Hashtable matchProps = new Hashtable();
                matchProps["MatchStarted"] = true;
                matchProps["S1"] = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                this.PluginHost.SetProperties(0, matchProps, null, true);
                this.PluginHost.LogInfo("MatchmakingPlugin: SoulHunt - 2 players joined, match started.");
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: Error in StartSoulHuntMatchIfReady: {ex}");
            }
        }

        private void AssignSantaClausTeam(int actorNr)
        {
            try
            {
                if (!this.PluginHost.GameActors.Any(a => a.ActorNr == actorNr)) return;
                Hashtable props = new Hashtable();
                props["team"] = TEAM_CT;
                this.PluginHost.SetProperties(actorNr, props, null, true);
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: Error in AssignSantaClausTeam: {ex}");
            }
        }

        private void StartSantaClausMatchIfReady()
        {
            try
            {
                if (this.PluginHost.GameActors.Count != 2) return;
                var actors = this.PluginHost.GameActors.ToList();
                var random = new Random();
                var terrorist = actors[random.Next(actors.Count)];
                var ct = actors.First(a => a.ActorNr != terrorist.ActorNr);
                Hashtable terroristProps = new Hashtable { ["team"] = TEAM_TERRORISTS };
                Hashtable ctProps = new Hashtable { ["team"] = TEAM_CT };
                this.PluginHost.SetProperties(terrorist.ActorNr, terroristProps, null, true);
                this.PluginHost.SetProperties(ct.ActorNr, ctProps, null, true);
                Hashtable matchProps = new Hashtable();
                matchProps["MatchStarted"] = true;
                matchProps["S1"] = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                this.PluginHost.SetProperties(0, matchProps, null, true);
                this.PluginHost.LogInfo("MatchmakingPlugin: SantaClaus - 2 players joined, match started.");
            }
            catch (Exception ex)
            {
                this.PluginHost.LogError($"MatchmakingPlugin: Error in StartSantaClausMatchIfReady: {ex}");
            }
        }
    }
}

