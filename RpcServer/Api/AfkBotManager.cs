using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Axlebolt.Bolt.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// AFK-боты: очередь + авто-accept инвайтов + авто-confirm.
    /// В Photon передаём afk_bots в room props — плагин не отменяет матч из‑за нехватки пиров.
    /// Полностью «видимые» боты в раунде требуют Photon-клиент; слоты заполняются на стороне плагина.
    /// </summary>
    public static class AfkBotManager
    {
        public const string UidPrefix = "BOT_";
        public const string NamePrefix = "AFK_Bot_";
        private static readonly string EnabledFlagPath = Path.Combine(AppContext.BaseDirectory, "afk_bots_enabled.flag");

        private static volatile bool _enabled = false;

        public static bool Enabled
        {
            get => false;
            set
            {
                _enabled = false;
                DisablePermanently();
            }
        }

        /// <summary>Боты отключены навсегда — остановить всех и записать флаг.</summary>
        public static void DisablePermanently()
        {
            _enabled = false;
            try
            {
                string disabled = Path.Combine(AppContext.BaseDirectory, "afk_bots_disabled.flag");
                File.WriteAllText(disabled, "permanent");
                if (File.Exists(EnabledFlagPath)) File.Delete(EnabledFlagPath);
            }
            catch { }
            try { StopAll(); } catch { }
        }

        public static void LoadEnabledFromDisk()
        {
            _enabled = false;
            DisablePermanently();
        }

        /// <summary>Лимит одновременно активных ботов на режим.</summary>
        public static readonly IReadOnlyDictionary<int, int> MaxBotsByMode = new Dictionary<int, int>
        {
            [5] = 16,  // Allies / Ranked2v2 — несколько параллельных матчей с добивкой
            [8] = 16,
            [6] = 9,   // Ranked Defuse 5v5
            [11] = 4,  // Clan ranked
            [0] = 5,   // DeathMatch
            [1] = 4,   // Defuse casual
            [2] = 5,   // ArmsRace
            [3] = 1,   // Training
            [4] = 1,   // Sniper
            [7] = 5,   // Escalation
            [9] = 1,
            [30] = 5,  // Arcade
        };

        public static readonly IReadOnlyDictionary<int, string> ModeNames = new Dictionary<int, string>
        {
            [0] = "DeathMatch",
            [1] = "Defuse",
            [2] = "ArmsRace",
            [3] = "Training",
            [4] = "SniperDuel",
            [5] = "Allies 2v2",
            [6] = "Ranked Defuse",
            [7] = "Escalation",
            [8] = "Ranked 2v2",
            [9] = "SniperDuel",
            [11] = "Clan Ranked",
            [30] = "Arcade",
        };

        private sealed class BotRuntime
        {
            public string PlayerId;
            public string Uid;
            public int Mode;
            public string Region;
            public bool InQueue;
        }

        private static readonly ConcurrentDictionary<string, BotRuntime> _bots = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _spawnLock = new();

        public static bool IsBot(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return false;
            if (_bots.ContainsKey(playerId)) return true;
            try
            {
                if (!ObjectId.TryParse(playerId, out var oid)) return false;
                var doc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);
                return doc != null && !string.IsNullOrEmpty(doc.uid) &&
                       doc.uid.StartsWith(UidPrefix, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static int GetLimit(int mode) =>
            MaxBotsByMode.TryGetValue(mode, out int lim) ? lim : 3;

        public static string GetModeName(int mode) =>
            ModeNames.TryGetValue(mode, out var n) ? n : $"Mode {mode}";

        public static int CountActive(int mode) =>
            _bots.Values.Count(b => b.Mode == mode);

        public static int CountAll() => _bots.Count;

        public static IEnumerable<(int mode, int active, int limit, string name)> SnapshotByMode()
        {
            foreach (var kv in MaxBotsByMode.OrderBy(x => x.Key))
            {
                yield return (kv.Key, CountActive(kv.Key), kv.Value, GetModeName(kv.Key));
            }
        }

        public static List<string> ListBotUids(int mode = -1)
        {
            return _bots.Values
                .Where(b => mode < 0 || b.Mode == mode)
                .Select(b => $"{b.Uid} ({GetModeName(b.Mode)})")
                .OrderBy(x => x)
                .ToList();
        }

        /// <summary>Запустить count ботов в очередь режима. Возвращает (ok, message).</summary>
        public static (bool ok, string message) SpawnForMode(int mode, int count, string region = null)
        {
            return (false, "AFK-боты удалены с сервера.");
        }

        public static List<string> GetActiveBotPlayerIds() =>
            _bots.Keys.ToList();

        public static int CountBotsAmong(IEnumerable<string> playerIds)
        {
            if (playerIds == null) return 0;
            return playerIds.Count(IsBot);
        }

        /// <summary>Освобождает ботов после матча, чтобы лимит не блокировал новые добивки.</summary>
        public static void ReleaseBotsFromMatch(IEnumerable<string> playerIds)
        {
            if (playerIds == null) return;
            foreach (var id in playerIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!IsBot(id)) continue;
                try { Despawn(id); }
                catch (Exception ex) { Logger.LogWarn($"[AfkBot] release after match failed {id}: {ex.Message}"); }
            }
        }

        public static (bool ok, string message) StopMode(int mode)
        {
            var ids = _bots.Where(kv => kv.Value.Mode == mode).Select(kv => kv.Key).ToList();
            foreach (var id in ids)
                Despawn(id);
            return (true, $"{GetModeName(mode)}: остановлено {ids.Count} бот(ов)");
        }

        public static (bool ok, string message) StopAll()
        {
            var ids = _bots.Keys.ToList();
            foreach (var id in ids)
                Despawn(id);
            return (true, $"Остановлены все AFK-боты ({ids.Count})");
        }

        private static void Despawn(string playerId)
        {
            if (!_bots.TryRemove(playerId, out var bot)) return;
            try { MatchmakingManager.AbandonPendingForPlayer(playerId); } catch { }
            try { MatchmakingManager.RemoveFromQueue(playerId); } catch { }
            try
            {
                // Выйти из лобби если сидел
                LeaveAnyLobby(playerId);
            }
            catch { }
            try
            {
                var st = StaticClasses.PlayersStatus.GetOrAdd(playerId, _ => new PlayerStatus());
                st.onlineStatus = PlayerStatus.OnlineStatus.StateOffline;
                st.playInGame = null;
                if (ObjectId.TryParse(playerId, out var oid))
                    BoltMainDatabaseProvider.Instance.SetPlayerStatus(oid, st);
            }
            catch { }
            Logger.Log($"[AfkBot] despawned {bot?.Uid ?? playerId}");
        }

        /// <summary>Авто-accept инвайта в лобби (Allies party и т.п.).</summary>
        public static void TryAutoAcceptLobbyInvite(string lobbyId, string invitedPlayerId, LobbyPlayerType playerType)
        {
            if (!Enabled) return;
            if (!IsBot(invitedPlayerId)) return;
            if (string.IsNullOrWhiteSpace(lobbyId)) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    if (!ForceJoinLobby(lobbyId, invitedPlayerId, playerType == LobbyPlayerType.Spectator
                            ? LobbyPlayerType.Spectator
                            : LobbyPlayerType.Member))
                    {
                        Logger.LogWarn($"[AfkBot] auto-accept failed lobby={lobbyId} bot={invitedPlayerId}");
                    }
                    else
                    {
                        Logger.Log($"[AfkBot] auto-accepted invite lobby={lobbyId} bot={invitedPlayerId}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[AfkBot] auto-accept error: {ex.Message}");
                }
            });
        }

        /// <summary>Авто-confirm найденного матча для всех ботов в pending.</summary>
        public static void AutoConfirmBotsInPending(IEnumerable<string> playerIds)
        {
            if (!Enabled) return;
            if (playerIds == null) return;
            foreach (var id in playerIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!IsBot(id)) continue;
                try
                {
                    // Галочки союзников на экране «ИГРА НАЙДЕНА». Старт матча ждёт людей, не ботов.
                    string capture = id;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1200).ConfigureAwait(false);
                        MatchmakingManager.ConfirmPlayer(capture);
                        Logger.Log($"[AfkBot] auto-confirmed match for {capture}");
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error($"[AfkBot] confirm fail {id}: {ex.Message}");
                }
            }
        }

        public static bool ForceJoinLobby(string lobbyId, string playerId, LobbyPlayerType playerType)
        {
            if (!StaticClasses.Lobbies.TryGetValue(lobbyId, out BoltLobby lobby))
                return false;
            if (!ObjectId.TryParse(playerId, out var oid))
                return false;
            var player = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);
            if (player == null) return false;

            if (playerType == LobbyPlayerType.Any) playerType = LobbyPlayerType.Member;
            if (lobby.IsMemberInvited(playerId)) playerType = LobbyPlayerType.Member;
            else if (lobby.IsSpectatorInvited(playerId)) playerType = LobbyPlayerType.Spectator;

            if (!lobby.IsAnyInvited(playerId) && !lobby.IsLobbyAny(playerId) && !lobby.Joinable)
                return false;

            if (playerType == LobbyPlayerType.Member && !lobby.IsLobbyMember(playerId)
                && lobby.LobbyMembers.Length >= lobby.MaxMembers)
                return false;

            LeaveAnyLobby(playerId);

            if (playerType == LobbyPlayerType.Spectator)
                lobby.AddLobbySpectator(player.GetBoltFriend());
            else
                lobby.AddLobbyMember(player.GetBoltFriend());

            StaticClasses.Lobbies[lobby.Id] = lobby;

            var status = StaticClasses.PlayersStatus.GetOrAdd(playerId, _ => new PlayerStatus());
            status.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
            status.playInGame ??= new PlayInGame();
            status.playInGame.lobbyId = lobby.Id;
            status.playInGame.lobbyName = lobby.Name ?? "";
            status.playInGame.gameCode = string.Empty;
            status.playInGame.gameVersion = StaticClasses.GetPlayerGameVersion(playerId);
            try { BoltMainDatabaseProvider.Instance.SetPlayerStatus(oid, status); } catch { }

            // Уведомить живых членов лобби
            try
            {
                var joinedFriend = player.GetPlayerFriend(playerId);
                string ev = playerType == LobbyPlayerType.Spectator
                    ? "onNewSpectatorJoinedLobby"
                    : "onNewPlayerJoinedLobby";
                foreach (string mid in lobby.LobbyMembers.Concat(lobby.LobbySpectators)
                             .Select(m => m.Id).Distinct())
                {
                    if (string.IsNullOrWhiteSpace(mid)) continue;
                    if (!StaticClasses.EventSenders.TryGetValue(mid, out var senders)) continue;
                    foreach (var s in senders)
                    {
                        if (s == null || s.GetType().Name != "MatchmakingEventSender") continue;
                        s.SendEvent(ev, new object[] { joinedFriend });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarn($"[AfkBot] join notify: {ex.Message}");
            }

            // Бот в лобби — не в solo-очереди
            if (_bots.TryGetValue(playerId, out var bot))
            {
                try { MatchmakingManager.RemoveFromQueue(playerId); } catch { }
                bot.InQueue = false;
            }

            return true;
        }

        private static void LeaveAnyLobby(string playerId)
        {
            try
            {
                var st = StaticClasses.PlayersStatus.GetOrAdd(playerId, _ => new PlayerStatus());
                string lid = st.playInGame?.lobbyId;
                if (string.IsNullOrWhiteSpace(lid)) return;
                if (!StaticClasses.Lobbies.TryGetValue(lid, out var lobby)) return;
                lobby.RemoveLobbyAnyById(playerId);
                lobby.RemoveLobbyAnyInviteById(playerId);
                StaticClasses.Lobbies[lid] = lobby;
                st.playInGame.lobbyId = "";
                st.playInGame.lobbyName = "";
            }
            catch { }
        }

        private static BotRuntime EnsureBotSlot()
        {
            // Переиспользовать оффлайн-слоты не из _bots, либо создать нового BOT_XX
            for (int n = 1; n <= 64; n++)
            {
                string uid = UidPrefix + n.ToString("00");
                string name = NamePrefix + n.ToString("00");
                var existing = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(uid);
                ObjectId oid;
                if (existing != null && existing.Length > 0)
                {
                    oid = existing[0]._id;
                    string pid = oid.ToString();
                    if (_bots.ContainsKey(pid)) continue; // занят
                    return new BotRuntime { PlayerId = pid, Uid = uid, Mode = -1, Region = BoltMainDatabaseProvider.MainServerRegion };
                }

                // Создать нового
                try
                {
                    oid = BoltMainDatabaseProvider.Instance.CreatePlayer(name, uid);
                    string pid = oid.ToString();
                    try { BoltGameDatabaseProvider.Instance.CreatePlayerStats(pid); } catch { }
                    try { BoltGameDatabaseProvider.Instance.CreatePlayerFiles(pid); } catch { }
                    try { BoltGameDatabaseProvider.Instance.CreatePlayerInventory(oid); } catch { }
                    Logger.Log($"[AfkBot] created player {uid} id={pid}");
                    return new BotRuntime { PlayerId = pid, Uid = uid, Mode = -1, Region = BoltMainDatabaseProvider.MainServerRegion };
                }
                catch (Exception ex)
                {
                    Logger.Error($"[AfkBot] create {uid} failed: {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        private static void EnsureOnline(string playerId)
        {
            try
            {
                var st = StaticClasses.PlayersStatus.GetOrAdd(playerId, _ => new PlayerStatus());
                st.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
                st.playInGame ??= new PlayInGame
                {
                    gameCode = string.Empty,
                    gameVersion = "0.17.0",
                    lobbyId = "",
                    lobbyName = ""
                };
                if (ObjectId.TryParse(playerId, out var oid))
                    BoltMainDatabaseProvider.Instance.SetPlayerStatus(oid, st);
            }
            catch { }
        }

        private static void EnsureRankedReady(string playerId, int mode)
        {
            try
            {
                var db = BoltGameDatabaseProvider.Instance;
                // Калибровка пройдена + MMR, иначе клиент/сервер могут не пустить в ranked
                if (mode == 5 || mode == 8)
                {
                    db.SetPlayerStat(playerId, "ranked_2v2_played_matches", 10);
                    db.SetPlayerStat(playerId, "allies_played_matches", 10);
                    if (db.GetPlayerStat(playerId, "ranked_2v2_current_mmr") <= 0)
                        db.SetPlayerStat(playerId, "ranked_2v2_current_mmr", 1000);
                    if ((int)db.GetPlayerStat(playerId, "ranked_2v2_rank") < 0)
                        db.SetPlayerStat(playerId, "ranked_2v2_rank", 0);
                }
                else if (mode == 6 || mode == 11)
                {
                    db.SetPlayerStat(playerId, "ranked_played_matches", 10);
                    if (db.GetPlayerStat(playerId, "ranked_current_mmr") <= 0)
                        db.SetPlayerStat(playerId, "ranked_current_mmr", 1000);
                    if ((int)db.GetPlayerStat(playerId, "ranked_rank") < 0)
                        db.SetPlayerStat(playerId, "ranked_rank", 0);
                }
                if ((int)db.GetPlayerStat(playerId, "level_id") < 5)
                    db.SetPlayerStat(playerId, "level_id", 20);
            }
            catch { }
        }
    }
}
