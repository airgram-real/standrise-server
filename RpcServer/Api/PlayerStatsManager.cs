using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main.PlayerStats;
using StandRiseServer.RpcServer.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Axlebolt.Bolt.Protobuf;

namespace StandRiseServer.RpcServer.Api
{
    public static class PlayerStatsManager
    {
        public static void AddKill(string playerId, string gameMode = null)
        {
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "kills");
            if (!string.IsNullOrEmpty(gameMode))
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, $"{gameMode}_kills");
            }
            Logger.Log($"[PlayerStats] Added kill for player {playerId} in mode {gameMode}");
        }

        public static void AddDeath(string playerId, string gameMode = null)
        {
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "deaths");
            if (!string.IsNullOrEmpty(gameMode))
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, $"{gameMode}_deaths");
            }
            Logger.Log($"[PlayerStats] Added death for player {playerId} in mode {gameMode}");
        }

        public static void AddAssist(string playerId, string gameMode = null)
        {
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "assists");
            if (!string.IsNullOrEmpty(gameMode))
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, $"{gameMode}_assists");
            }
            Logger.Log($"[PlayerStats] Added assist for player {playerId} in mode {gameMode}");
        }

        public static void AddShot(string playerId, string gameMode = null)
        {
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "shots");
            if (!string.IsNullOrEmpty(gameMode))
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, $"{gameMode}_shots");
            }
        }

        public static void AddHit(string playerId, string gameMode = null)
        {
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "hits");
            if (!string.IsNullOrEmpty(gameMode))
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, $"{gameMode}_hits");
            }
        }

        public static void AddHeadshot(string playerId, string gameMode = null)
        {
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "headshots");
            if (!string.IsNullOrEmpty(gameMode))
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, $"{gameMode}_headshots");
            }
            Logger.Log($"[PlayerStats] Added headshot for player {playerId} in mode {gameMode}");
        }

        public static void AddDamage(string playerId, float damage, string gameMode = null)
        {
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "damage", damage);
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "damage_dealt", (int)damage);
            if (!string.IsNullOrEmpty(gameMode))
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, $"{gameMode}_damage", (int)damage);
            }
        }

        public static void AddDamageTaken(string playerId, float damage)
        {
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "damage_taken", (int)damage);
        }

        public static void AddGamePlayed(string playerId, string gameMode = null)
        {
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "games_played");
            if (!string.IsNullOrEmpty(gameMode))
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, $"{gameMode}_games_played");
            }
            Logger.Log($"[PlayerStats] Added game played for player {playerId} in mode {gameMode}");
        }

        public static void AddWin(string playerId, string gameMode = null)
        {
            gameMode = NormalizeRankedGameMode(gameMode);
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "wins");
            if (!string.IsNullOrEmpty(gameMode))
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, $"{gameMode}_wins");
            }
            
            if (gameMode == "ranked")
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_won_match_count");
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_played_matches");
                // Счётчик калибровочных игр обязан расти и на победе: в AddLoss он инкрементился,
                // а здесь — нет, поэтому «осталось N игр до конца калибровки» уменьшалось
                // только после поражений и калибровка никогда не закрывалась победами.
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_calibration_match_count");
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_last_match_status", 1);
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_last_activity_time1", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                int played = (int)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "ranked_played_matches");
                var currentMmr = (float)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "ranked_current_mmr");
                if (currentMmr <= 0f) currentMmr = 1000f;
                // На калибровке MMR не двигаем и rank = -1 (0 = Bronze I на клиенте).
                float newMmr = played >= 10 ? currentMmr + 25f : currentMmr;
                if (newMmr <= 0f) newMmr = 1000f;
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_current_mmr", newMmr);
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_target_mmr", newMmr);

                int rank = played >= 10 ? CalculateRankFromMmr(newMmr) : -1;
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_rank", rank);
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_current_rank", rank);

                if (played >= 10)
                {
                    var bestRank = BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "ranked_best_rank");
                    if (rank > (int)bestRank)
                        BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_best_rank", rank);
                }
                try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }
                PushProfileStatsUpdate(playerId);
            }

            if (gameMode == "allies")
            {
                // ClientStatNamesSwapped=false: won_match_count = победы, calibration = сыграно.
                if (!LocalServerConfig.Current.ClientStatNamesSwapped)
                {
                    BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_2v2_won_match_count");
                    BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_2v2_played_matches");
                    int playedWin = (int)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "ranked_2v2_played_matches");
                    BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_2v2_calibration_match_count",
                        Math.Clamp(playedWin, 0, AlliesMmrSystem.CalibrationMatches));
                }
                else
                {
                    BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_2v2_calibration_match_count");
                    BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_2v2_played_matches");
                    BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_2v2_won_match_count");
                }
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked2v2_wins");
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_2v2_last_match_status", 1);
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_2v2_last_match_start_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                ApplySimpleAlliesMmrDelta(playerId, won: true);
            }
            
            Logger.Log($"[PlayerStats] Added win for player {playerId} in mode {gameMode}");
        }

        public static void AddLoss(string playerId, string gameMode = null)
        {
            gameMode = NormalizeRankedGameMode(gameMode);
            BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "losses");
            if (!string.IsNullOrEmpty(gameMode))
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, $"{gameMode}_losses");
            }
            
            if (gameMode == "ranked")
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_played_matches");
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_calibration_match_count");
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_last_match_status", 0);
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_last_activity_time1", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                int played = (int)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "ranked_played_matches");
                var currentMmr = (float)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "ranked_current_mmr");
                if (currentMmr <= 0f) currentMmr = 1000f;
                float newMmr = played >= 10 ? Math.Max(0f, currentMmr - 20f) : currentMmr;
                if (newMmr <= 0f) newMmr = 1000f;
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_current_mmr", newMmr);
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_target_mmr", newMmr);

                int rank = played >= 10 ? CalculateRankFromMmr(newMmr) : -1;
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_rank", rank);
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_current_rank", rank);
                try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }
                PushProfileStatsUpdate(playerId);
            }

            if (gameMode == "allies")
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_2v2_played_matches");
                if (!LocalServerConfig.Current.ClientStatNamesSwapped)
                {
                    // Проигрыш НЕ трогает won_match_count (это победы на клиенте 0.17).
                    int playedLoss = (int)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "ranked_2v2_played_matches");
                    BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_2v2_calibration_match_count",
                        Math.Clamp(playedLoss, 0, AlliesMmrSystem.CalibrationMatches));
                }
                else
                {
                    BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_2v2_won_match_count");
                }
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked2v2_losses");
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_2v2_last_match_status", 0);
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_2v2_last_match_start_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                ApplySimpleAlliesMmrDelta(playerId, won: false);
            }
            
            Logger.Log($"[PlayerStats] Added loss for player {playerId} in mode {gameMode}");
        }

        /// <summary>
        /// Применяет итог ranked/allies матча. won=null → только калибровка/+1 played без MMR.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _recentRankedOutcomeTicks =
            new System.Collections.Concurrent.ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        public static void ApplyRankedMatchOutcome(string playerId, string gameMode, bool? won)
        {
            gameMode = NormalizeRankedGameMode(gameMode);
            if (gameMode != "ranked" && gameMode != "allies")
                return;

            // Дедуп: HTTP /api/match/result и StoreStats оба вызывают это — иначе win+unknown или win+loss.
            string dedupeKey = playerId + "|" + gameMode;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_recentRankedOutcomeTicks.TryGetValue(dedupeKey, out long prev) && now - prev < 90000)
            {
                Logger.Log($"[PlayerStats] Skip duplicate ranked outcome for {playerId} mode={gameMode} (within 90s)");
                return;
            }
            _recentRankedOutcomeTicks[dedupeKey] = now;

            if (won == true)
            {
                AddWin(playerId, gameMode);
                try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }
                return;
            }
            if (won == false)
            {
                AddLoss(playerId, gameMode);
                try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }
                return;
            }

            try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }

            // Исход неизвестен (клиент прислал только games_played) — двигаем калибровку.
            if (gameMode == "allies")
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_2v2_played_matches");
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_2v2_won_match_count"); // calibration counter on 2v2 client
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_2v2_last_match_start_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                int played = (int)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "ranked_2v2_played_matches");
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_2v2_rank", played >= 10
                    ? CalculateRankFromMmr((float)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "ranked_2v2_current_mmr"))
                    : -1);
                Logger.Log($"[PlayerStats] Allies calibration +1 (outcome unknown) for {playerId}");
            }
            else if (gameMode == "ranked")
            {
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_played_matches");
                BoltGameDatabaseProvider.Instance.IncrementPlayerStat(playerId, "ranked_calibration_match_count");
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_last_activity_time1", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                int played = (int)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "ranked_played_matches");
                BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "ranked_rank", played >= 10
                    ? CalculateRankFromMmr((float)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "ranked_current_mmr"))
                    : -1);
                Logger.Log($"[PlayerStats] Ranked calibration +1 (outcome unknown) for {playerId}");
            }
        }

        // XP formula from ProfileSettings: level N requires (StartLevelUpXp + LevelUpXpK*(N-1)) XP
        private const int StartLevelUpXp = 3000;
        private const int LevelUpXpK    = 100;
        private const int MaxLevel       = 600;

        public static float GetXpForLevel(int level)
        {
            // XP needed to go FROM level to level+1
            return StartLevelUpXp + LevelUpXpK * (level - 1);
        }

        public static void AddExperience(string playerId, float xp)
        {
            if (xp <= 0f) return;

            var currentXp    = (float)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "level_xp");
            var currentLevel = (int)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, "level_id");

            // Ensure level starts at 1 (0 means never initialised)
            if (currentLevel < 1) currentLevel = 1;

            var newXp    = currentXp + xp;
            var newLevel = currentLevel;

            while (newLevel < MaxLevel)
            {
                float needed = GetXpForLevel(newLevel); // XP to reach next level
                if (newXp < needed) break;
                newXp -= needed;
                newLevel++;
            }

            // At max level don't accumulate XP beyond threshold
            if (newLevel >= MaxLevel) newXp = 0f;

            BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "level_xp", newXp);
            BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, "level_id", newLevel);

            Logger.Log($"[PlayerStats] +{xp} XP → player {playerId}: level {newLevel}, xp {newXp}/{GetXpForLevel(newLevel)}");
        }

        public static void SetRankedMmr(string playerId, float mmr) => ApplyCompetitiveMmr(playerId, mmr, ensureCalibrated: true);

        /// <summary>Установить MMR 5v5, звание = RankFromMmr(mmr), мгновенный push в профиль.</summary>
        public static void ApplyCompetitiveMmr(string playerId, float mmr, bool ensureCalibrated = true)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            mmr = Math.Clamp(mmr, AlliesMmrSystem.MinMmr, AlliesMmrSystem.MaxMmr);
            var db = BoltGameDatabaseProvider.Instance;
            if (ensureCalibrated)
                EnsureCompetitiveCalibrated(playerId);
            db.SetPlayerStat(playerId, "ranked_current_mmr", mmr);
            db.SetPlayerStat(playerId, "ranked_target_mmr", mmr);
            SyncCompetitiveRankFromMmr(playerId, mmr);
            try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }
            PushProfileStatsUpdate(playerId);
            Logger.Log($"[PlayerStats] Apply Competitive MMR {mmr} for {playerId}");
        }

        /// <summary>Изменить MMR 5v5 на delta (+/-), синхронизировать звание и профиль.</summary>
        public static void AdjustCompetitiveMmr(string playerId, float delta, bool ensureCalibrated = true)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            var db = BoltGameDatabaseProvider.Instance;
            float current = (float)db.GetPlayerStat(playerId, "ranked_current_mmr");
            if (current <= 0f) current = AlliesMmrSystem.DefaultMmr;
            ApplyCompetitiveMmr(playerId, current + delta, ensureCalibrated);
        }

        /// <summary>Установить MMR Allies, звание = RankFromMmr(mmr), мгновенный push в профиль.</summary>
        public static void ApplyAlliesMmr(string playerId, float mmr, bool ensureCalibrated = true)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            mmr = Math.Clamp(mmr, AlliesMmrSystem.MinMmr, AlliesMmrSystem.MaxMmr);
            var db = BoltGameDatabaseProvider.Instance;
            int played = (int)db.GetPlayerStat(playerId, "ranked_2v2_played_matches");
            if (ensureCalibrated && played < AlliesMmrSystem.CalibrationMatches)
                played = AlliesMmrSystem.CalibrationMatches;
            WriteAlliesMmr(playerId, mmr, played);
            Logger.Log($"[PlayerStats] Apply Allies MMR {mmr} for {playerId}");
        }

        /// <summary>Изменить MMR Allies на delta (+/-), синхронизировать звание и профиль.</summary>
        public static void AdjustAlliesMmr(string playerId, float delta, bool ensureCalibrated = true)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            var db = BoltGameDatabaseProvider.Instance;
            float current = (float)db.GetPlayerStat(playerId, "ranked_2v2_current_mmr");
            if (current <= 0f) current = AlliesMmrSystem.DefaultMmr;
            ApplyAlliesMmr(playerId, current + delta, ensureCalibrated);
        }

        private static void EnsureCompetitiveCalibrated(string playerId)
        {
            var db = BoltGameDatabaseProvider.Instance;
            int played = (int)db.GetPlayerStat(playerId, "ranked_played_matches");
            if (played >= AlliesMmrSystem.CalibrationMatches) return;
            int wins = Math.Max(50, (int)db.GetPlayerStat(playerId, "ranked_won_match_count"));
            db.SetPlayerStat(playerId, "ranked_played_matches", Math.Max(AlliesMmrSystem.CalibrationMatches, wins));
            db.SetPlayerStat(playerId, "ranked_won_match_count", wins);
            db.SetPlayerStat(playerId, "ranked_calibration_match_count", AlliesMmrSystem.CalibrationMatches);
            db.SetPlayerStat(playerId, "ranked_last_match_status", 1);
            db.SetPlayerStat(playerId, "ranked_last_match_start_time", 0);
        }

        private static void SyncCompetitiveRankFromMmr(string playerId, float mmr)
        {
            var db = BoltGameDatabaseProvider.Instance;
            int played = (int)db.GetPlayerStat(playerId, "ranked_played_matches");
            int rank = played >= AlliesMmrSystem.CalibrationMatches ? AlliesMmrSystem.RankFromMmr(mmr) : -1;
            db.SetPlayerStat(playerId, "ranked_rank", rank);
            db.SetPlayerStat(playerId, "ranked_current_rank", rank);
            if (rank >= 0)
            {
                int best = (int)db.GetPlayerStat(playerId, "ranked_best_rank");
                if (rank > best)
                    db.SetPlayerStat(playerId, "ranked_best_rank", rank);
            }
        }

        /// <summary>Выдача звания Allies (0..16). MMR — источник правды: ranked_*_rank = RankFromMmr(mmr).</summary>
        public static void SetAlliesRank(string playerId, int rank, float? mmrOverride = null, int? winsOverride = null, int? lossesOverride = null, int? killsOverride = null, int? deathsOverride = null)
        {
            if (rank > 16) rank = 16;
            rank = Math.Clamp(rank, -1, 16);
            float mmr = ResolveMmrForRankGrant(rank, mmrOverride);

            var db = BoltGameDatabaseProvider.Instance;
            // current + target: профиль/полоска XP часто смотрит target_mmr
            db.SetPlayerStat(playerId, "ranked_2v2_current_mmr", mmr);
            db.SetPlayerStat(playerId, "ranked_2v2_target_mmr", mmr);
            db.SetPlayerStat(playerId, "allies_current_mmr", mmr);
            db.SetPlayerStat(playerId, "allies_target_mmr", mmr);
            db.SetPlayerStat(playerId, "ranked2v2_current_mmr", mmr);
            if (rank >= 0)
            {
                // Полностью снять калибровку (клиент смотрит played + swapped calibration counter).
                int wins = winsOverride ?? Math.Max(50, (int)db.GetPlayerStat(playerId, "ranked_2v2_calibration_match_count"));
                int losses = lossesOverride ?? Math.Max(0, (int)db.GetPlayerStat(playerId, "ranked2v2_losses"));
                int played = Math.Max(AlliesMmrSystem.CalibrationMatches, wins + losses);
                db.SetPlayerStat(playerId, "ranked_2v2_played_matches", played);
                db.SetPlayerStat(playerId, "allies_played_matches", played);
                db.SetPlayerStat(playerId, "ranked2v2_games_played", played);
                db.SetPlayerStat(playerId, "ranked_2v2_won_match_count", AlliesMmrSystem.CalibrationMatches); // calibration done
                db.SetPlayerStat(playerId, "ranked_2v2_calibration_match_count", wins); // wins (client swap)
                db.SetPlayerStat(playerId, "ranked2v2_wins", wins);
                if (lossesOverride.HasValue)
                    db.SetPlayerStat(playerId, "ranked2v2_losses", losses);
                db.SetPlayerStat(playerId, "ranked_2v2_last_match_status", 1);
                db.SetPlayerStat(playerId, "ranked_2v2_last_match_start_time", 0);
                if (killsOverride.HasValue || deathsOverride.HasValue)
                {
                    int kills = Math.Max(0, killsOverride ?? (int)db.GetPlayerStat(playerId, "allies_kills"));
                    int deaths = Math.Max(0, deathsOverride ?? (int)db.GetPlayerStat(playerId, "allies_deaths"));
                    db.SetPlayerStat(playerId, "allies_kills", kills);
                    db.SetPlayerStat(playerId, "allies_deaths", deaths);
                    db.SetPlayerStat(playerId, "ranked_2v2_kills", kills);
                    db.SetPlayerStat(playerId, "ranked2v2_kills", kills);
                    db.SetPlayerStat(playerId, "ranked_2v2_deaths", deaths);
                    db.SetPlayerStat(playerId, "ranked2v2_deaths", deaths);
                }
                int playedAfter = (int)db.GetPlayerStat(playerId, "ranked_2v2_played_matches");
                WriteAlliesMmr(playerId, mmr, playedAfter);
                rank = (int)db.GetPlayerStat(playerId, "ranked_2v2_rank");
            }
            else
            {
                db.SetPlayerStat(playerId, "ranked_2v2_played_matches", 0);
                db.SetPlayerStat(playerId, "allies_played_matches", 0);
                db.SetPlayerStat(playerId, "ranked_2v2_won_match_count", 0);
                db.SetPlayerStat(playerId, "ranked_2v2_rank", -1);
                db.SetPlayerStat(playerId, "allies_rank", -1);
                try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }
                PushProfileStatsUpdate(playerId);
            }
            Logger.Log($"[PlayerStats] Set Allies rank {rank} ({AlliesMmrSystem.RankName(rank)}) mmr={mmr} for {playerId}");
        }

        /// <summary>
        /// Полный сброс звания Allies и связанной статистики (W/L/KD/played/best),
        /// для игроков, которым ранг выдали через Telegram-бота.
        /// </summary>
        public static void WipeAlliesRankAndStats(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            var db = BoltGameDatabaseProvider.Instance;
            float mmr = AlliesMmrSystem.DefaultMmr;
            string[] zeroKeys =
            {
                "ranked_2v2_played_matches", "allies_played_matches", "ranked2v2_games_played",
                "ranked_2v2_won_match_count", "ranked_2v2_calibration_match_count",
                "ranked2v2_wins", "ranked2v2_losses", "allies_wins", "allies_losses",
                "ranked_2v2_kills", "ranked_2v2_deaths", "ranked_2v2_assists",
                "allies_kills", "allies_deaths", "allies_assists",
                "ranked2v2_kills", "ranked2v2_deaths",
                "ranked_2v2_last_match_status", "ranked_2v2_last_match_start_time"
            };
            foreach (var key in zeroKeys)
                db.SetPlayerStat(playerId, key, 0);

            db.SetPlayerStat(playerId, "ranked_2v2_current_mmr", mmr);
            db.SetPlayerStat(playerId, "ranked_2v2_target_mmr", mmr);
            db.SetPlayerStat(playerId, "allies_current_mmr", mmr);
            db.SetPlayerStat(playerId, "allies_target_mmr", mmr);
            db.SetPlayerStat(playerId, "ranked2v2_current_mmr", mmr);
            db.SetPlayerStat(playerId, "ranked_2v2_rank", -1);
            db.SetPlayerStat(playerId, "ranked_2v2_current_rank", -1);
            db.SetPlayerStat(playerId, "allies_rank", -1);
            db.SetPlayerStat(playerId, "allies_current_rank", -1);
            db.SetPlayerStat(playerId, "competitive_2v2_rank", -1);
            db.SetPlayerStat(playerId, "ranked2v2_rank", -1);
            db.SetPlayerStat(playerId, "ranked_2v2_best_rank", -1);
            db.SetPlayerStat(playerId, "allies_best_rank", -1);
            try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }
            PushProfileStatsUpdate(playerId);
            Logger.Log($"[PlayerStats] Wiped Allies rank+stats for {playerId}");
        }

        public static int WipeKnownBotGrantedAlliesRanks()
        {
            var ids = new[]
            {
                "6a9822d8ac7beb602c39adb4",
                "6a9c0a273f1c44f40f4cd472",
                "6a9c6cd59e249a2b0494a32d"
            };
            int n = 0;
            foreach (var id in ids)
            {
                try { WipeAlliesRankAndStats(id); n++; }
                catch (Exception ex) { Logger.LogWarn($"[PlayerStats] Wipe bot-rank failed for {id}: {ex.Message}"); }
            }
            Logger.Log($"[PlayerStats] Wiped bot-granted Allies rank+stats for {n} players");
            return n;
        }

        /// <summary>
        /// Безопасный сброс Allies: звания/статы/история. Аккаунты и инвентарь не трогаем.
        /// </summary>
        public static string SafeResetAlliesDatabase(bool rebuildHistory = false)
        {
            var report = new StringBuilder();
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string grantLog = Path.Combine(AppContext.BaseDirectory ?? ".", "bot_allies_rank_grants.log");
                if (File.Exists(grantLog))
                {
                    foreach (var line in File.ReadAllLines(grantLog))
                    {
                        var parts = line.Split('\t');
                        if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                            touched.Add(parts[1].Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarn($"[PlayerStats] SafeReset read grant log: {ex.Message}");
            }

            foreach (var id in new[] { "6a9822d8ac7beb602c39adb4", "6a9c0a273f1c44f40f4cd472", "6a9c6cd59e249a2b0494a32d" })
                touched.Add(id);

            int wipedStats = 0;
            foreach (var id in BoltGameDatabaseProvider.Instance.GetAllPlayerStatIds())
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                try
                {
                    WipeAlliesRankAndStats(id);
                    wipedStats++;
                }
                catch (Exception ex)
                {
                    Logger.LogWarn($"[PlayerStats] SafeReset wipe failed for {id}: {ex.Message}");
                }
            }

            long histRows = 0;
            try { histRows = BoltGameDatabaseProvider.Instance.ClearAllMatchHistory(); }
            catch (Exception ex) { Logger.LogWarn($"[PlayerStats] SafeReset clear history: {ex.Message}"); }

            report.AppendLine($"Сброшено Allies-статов: {wipedStats}");
            report.AppendLine($"Из лога бота (extra): {touched.Count}");
            report.AppendLine($"Удалено записей истории: {histRows}");
            report.AppendLine("Аккаунты и инвентарь не затронуты.");
            string text = report.ToString();
            Logger.Log("[PlayerStats] SafeResetAlliesDatabase:\n" + text);
            return text;
        }

        /// <summary>Осталось N калибровочных игр (1–9). rank=-1, played=10-N.</summary>
        public static void SetAlliesCalibrationRemaining(string playerId, int remaining)
        {
            remaining = Math.Clamp(remaining, 1, 9);
            int played = AlliesMmrSystem.CalibrationMatches - remaining;
            float mmr = AlliesMmrSystem.DefaultMmr;
            var db = BoltGameDatabaseProvider.Instance;
            db.SetPlayerStat(playerId, "ranked_2v2_current_mmr", mmr);
            db.SetPlayerStat(playerId, "ranked_2v2_target_mmr", mmr);
            db.SetPlayerStat(playerId, "allies_current_mmr", mmr);
            db.SetPlayerStat(playerId, "allies_target_mmr", mmr);
            db.SetPlayerStat(playerId, "ranked2v2_current_mmr", mmr);
            db.SetPlayerStat(playerId, "ranked_2v2_rank", -1);
            db.SetPlayerStat(playerId, "ranked_2v2_current_rank", -1);
            db.SetPlayerStat(playerId, "allies_rank", -1);
            db.SetPlayerStat(playerId, "allies_current_rank", -1);
            db.SetPlayerStat(playerId, "competitive_2v2_rank", -1);
            db.SetPlayerStat(playerId, "ranked2v2_rank", -1);
            db.SetPlayerStat(playerId, "ranked_2v2_played_matches", played);
            db.SetPlayerStat(playerId, "allies_played_matches", played);
            db.SetPlayerStat(playerId, "ranked2v2_games_played", played);
            db.SetPlayerStat(playerId, "ranked_2v2_won_match_count", played);
            try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }
            PushProfileStatsUpdate(playerId);
            Logger.Log($"[PlayerStats] Allies calibration remaining={remaining} played={played} for {playerId}");
        }

        public static int ResetAllAlliesToCalibration()
        {
            var ids = BoltGameDatabaseProvider.Instance.GetAllPlayerStatIds();
            int n = 0;
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                try
                {
                    SetAlliesRank(id, -1);
                    n++;
                }
                catch (Exception ex)
                {
                    Logger.LogWarn($"[PlayerStats] Reset calib failed for {id}: {ex.Message}");
                }
            }
            Logger.Log($"[PlayerStats] Reset {n} players to Allies calibration");
            return n;
        }

        public static string GetAlliesRankName(int rank) => AlliesMmrSystem.RankName(rank);

        /// <summary>Короткая подпись для Telegram-кнопок (ASCII, без проблем кодировки).</summary>
        public static string GetRankButtonLabel(int rank) => rank switch
        {
            -1 => "Calib",
            0 => "0 Br I", 1 => "1 Br II", 2 => "2 Br III", 3 => "3 Br IV",
            4 => "4 Sl I", 5 => "5 Sl II", 6 => "6 Sl III", 7 => "7 Sl IV",
            8 => "8 Gd I", 9 => "9 Gd II", 10 => "10 Gd III", 11 => "11 Gd IV",
            12 => "12 Phoenix", 13 => "13 Ranger", 14 => "14 Champ", 15 => "15 Elite",
            16 => "16 Legend",
            _ => rank.ToString()
        };

        /// <summary>Выдача звания Соревновательный 5v5 (0..16). MMR — источник правды для ranked_rank.</summary>
        public static void SetCompetitiveRank(string playerId, int rank, float? mmrOverride = null, int? winsOverride = null, int? lossesOverride = null, int? killsOverride = null, int? deathsOverride = null)
        {
            if (rank > 16) rank = 16;
            rank = Math.Clamp(rank, -1, 16);
            float mmr = ResolveMmrForRankGrant(rank, mmrOverride);

            var db = BoltGameDatabaseProvider.Instance;
            int winsSet = 0;
            if (rank >= 0)
            {
                winsSet = winsOverride ?? Math.Max(50, (int)db.GetPlayerStat(playerId, "ranked_won_match_count"));
                int losses = lossesOverride ?? Math.Max(0, (int)db.GetPlayerStat(playerId, "ranked_calibration_match_count"));
                int played = Math.Max(AlliesMmrSystem.CalibrationMatches, winsSet + losses);
                db.SetPlayerStat(playerId, "ranked_played_matches", played);
                db.SetPlayerStat(playerId, "ranked_won_match_count", winsSet);
                db.SetPlayerStat(playerId, "ranked_calibration_match_count", Math.Clamp(played, 0, 10));
                db.SetPlayerStat(playerId, "ranked_last_match_status", 1);
                db.SetPlayerStat(playerId, "ranked_last_match_start_time", 0);
                db.SetPlayerStat(playerId, "ranked_last_activity_time1", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                if (killsOverride.HasValue || deathsOverride.HasValue)
                {
                    int kills = Math.Max(0, killsOverride ?? (int)db.GetPlayerStat(playerId, "rankeddefuse_kills"));
                    int deaths = Math.Max(1, deathsOverride ?? (int)db.GetPlayerStat(playerId, "rankeddefuse_deaths"));
                    db.SetPlayerStat(playerId, "rankeddefuse_kills", kills);
                    db.SetPlayerStat(playerId, "rankeddefuse_deaths", deaths);
                }
                ApplyCompetitiveMmr(playerId, mmr, ensureCalibrated: false);
                rank = (int)db.GetPlayerStat(playerId, "ranked_rank");
            }
            else
            {
                db.SetPlayerStat(playerId, "ranked_played_matches", 0);
                db.SetPlayerStat(playerId, "ranked_won_match_count", 0);
                db.SetPlayerStat(playerId, "ranked_calibration_match_count", 0);
                db.SetPlayerStat(playerId, "ranked_current_mmr", AlliesMmrSystem.DefaultMmr);
                db.SetPlayerStat(playerId, "ranked_target_mmr", AlliesMmrSystem.DefaultMmr);
                db.SetPlayerStat(playerId, "ranked_rank", -1);
                db.SetPlayerStat(playerId, "ranked_current_rank", -1);
                try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }
                PushProfileStatsUpdate(playerId);
            }
            Logger.Log($"[PlayerStats] Set Competitive rank {rank} ({AlliesMmrSystem.RankName(rank)}) mmr={mmr} wins={winsSet} for {playerId}");
        }

        private static float ResolveMmrForRankGrant(int rank, float? mmrOverride)
        {
            if (mmrOverride.HasValue)
                return Math.Clamp(mmrOverride.Value, AlliesMmrSystem.MinMmr, AlliesMmrSystem.MaxMmr);
            if (rank < 0)
                return AlliesMmrSystem.DefaultMmr;
            return Math.Clamp(AlliesMmrSystem.MinMmrForRank(rank) + 5f, AlliesMmrSystem.MinMmr, AlliesMmrSystem.MaxMmr);
        }

        /// <summary>onStatsUpdatedEvent + onStatsUpdated — полный снимок как getCurrentStats.</summary>
        private static void PushProfileStatsUpdate(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            try
            {
                PlayerStatsRemoteEventListener.PushProfileUpdate(playerId);
            }
            catch (Exception ex)
            {
                Logger.LogWarn("[PlayerStats] PushProfileStatsUpdate failed: " + ex.Message);
            }
        }

        private static void ApplySimpleAlliesMmrDelta(string playerId, bool won)
        {
            var db = BoltGameDatabaseProvider.Instance;
            int played = (int)db.GetPlayerStat(playerId, "ranked_2v2_played_matches");
            float currentMmr = (float)db.GetPlayerStat(playerId, "ranked_2v2_current_mmr");
            if (currentMmr <= 0f) currentMmr = AlliesMmrSystem.DefaultMmr;
            bool calibrating = played < AlliesMmrSystem.CalibrationMatches;
            float delta = AlliesMmrSystem.ComputeDelta(
                currentMmr, won,
                ourRounds: won ? 8 : 6, enemyRounds: won ? 6 : 8,
                kills: 0, deaths: 0, assists: 0, score: won ? 20 : 10,
                lobbyAvgScore: 15f);
            float newMmr = AlliesMmrSystem.ApplyDelta(currentMmr, delta, calibrating);
            WriteAlliesMmr(playerId, newMmr, played);
            Logger.Log($"[PlayerStats] Allies simple MMR {currentMmr:0}→{newMmr:0} (Δ{delta:0.0}) win={won} played={played}");
        }

        /// <summary>Полный MMR по вики (3 критерия) после матча Allies.</summary>
        public static void ApplyAlliesMatchMmr(
            string playerId,
            bool won,
            int trScore,
            int ctScore,
            int team,
            int kills,
            int deaths,
            int assists,
            int score,
            float lobbyAvgScore,
            bool abandoned = false)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;

            string dedupeKey = playerId + "|allies|fullmmr";
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_recentRankedOutcomeTicks.TryGetValue(dedupeKey, out long prev) && now - prev < 90000)
            {
                Logger.Log($"[PlayerStats] Skip duplicate Allies MMR for {playerId}");
                return;
            }
            _recentRankedOutcomeTicks[dedupeKey] = now;
            _recentRankedOutcomeTicks[playerId + "|allies"] = now;

            var db = BoltGameDatabaseProvider.Instance;
            if (won)
            {
                db.IncrementPlayerStat(playerId, "ranked2v2_wins");
                db.SetPlayerStat(playerId, "ranked_2v2_last_match_status", 1);
            }
            else
            {
                db.IncrementPlayerStat(playerId, "ranked2v2_losses");
                db.SetPlayerStat(playerId, "ranked_2v2_last_match_status", 0);
            }
            db.IncrementPlayerStat(playerId, "ranked_2v2_played_matches");
            // won_match_count / calibration выставляются абсолютом в WriteAlliesMmr — не инкрементить
            // won_match_count на каждом матче (иначе проигрыш выглядел как победа).
            db.SetPlayerStat(playerId, "ranked_2v2_last_match_start_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            int played = (int)db.GetPlayerStat(playerId, "ranked_2v2_played_matches");
            float currentMmr = (float)db.GetPlayerStat(playerId, "ranked_2v2_current_mmr");
            if (currentMmr <= 0f) currentMmr = AlliesMmrSystem.DefaultMmr;
            bool calibrating = played < AlliesMmrSystem.CalibrationMatches;

            var (our, enemy) = AlliesMmrSystem.RoundsForTeam(team, trScore, ctScore, won ? team : (team == 1 ? 2 : 1));
            float delta = AlliesMmrSystem.ComputeDelta(
                currentMmr, won, our, enemy, kills, deaths, assists, score, lobbyAvgScore, abandoned);
            float newMmr = AlliesMmrSystem.ApplyDelta(currentMmr, delta, calibrating);
            WriteAlliesMmr(playerId, newMmr, played);
            try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }
            Logger.Log($"[PlayerStats] Allies MMR {currentMmr:0}→{newMmr:0} Δ{delta:0.0} win={won} rounds={our}:{enemy} K/D/A={kills}/{deaths}/{assists} score={score} played={played}");
        }

        private static void WriteAlliesMmr(string playerId, float newMmr, int played)
        {
            var db = BoltGameDatabaseProvider.Instance;
            db.SetPlayerStat(playerId, "ranked_2v2_current_mmr", newMmr);
            db.SetPlayerStat(playerId, "allies_current_mmr", newMmr);
            db.SetPlayerStat(playerId, "ranked2v2_current_mmr", newMmr);

            int rank = played >= AlliesMmrSystem.CalibrationMatches ? AlliesMmrSystem.RankFromMmr(newMmr) : -1;
            float targetMmr = rank >= 0 ? AlliesMmrSystem.TargetMmrForRankBar(rank) : newMmr;
            db.SetPlayerStat(playerId, "ranked_2v2_target_mmr", targetMmr);
            db.SetPlayerStat(playerId, "allies_target_mmr", targetMmr);
            db.SetPlayerStat(playerId, "ranked_2v2_rank", rank);
            db.SetPlayerStat(playerId, "ranked_2v2_current_rank", rank);
            db.SetPlayerStat(playerId, "allies_rank", rank);
            db.SetPlayerStat(playerId, "allies_current_rank", rank);
            db.SetPlayerStat(playerId, "competitive_2v2_rank", rank);
            db.SetPlayerStat(playerId, "ranked2v2_rank", rank);
            if (rank >= 0)
            {
                var best = (int)db.GetPlayerStat(playerId, "ranked_2v2_best_rank");
                if (rank > best)
                {
                    db.SetPlayerStat(playerId, "ranked_2v2_best_rank", rank);
                    db.SetPlayerStat(playerId, "allies_best_rank", rank);
                }
            }
            // Счётчики калибровки и побед — единственная точка записи для Allies.
            // Клиент 0.17 читает имена буквально (в его метаданных лежат строковые
            // константы ranked_2v2_calibration_match_count и ranked_2v2_won_match_count).
            // Раньше в них писали наоборот — «победы» в счётчик калибровки и «сыграно»
            // в счётчик побед, — из-за чего экран калибровки не показывался.
            // Пишем абсолютными значениями, поэтому уже накопленные данные выправляются сами.
            if (!LocalServerConfig.Current.ClientStatNamesSwapped)
            {
                try
                {
                    int playedNow = (int)db.GetPlayerStat(playerId, "ranked_2v2_played_matches");
                    int calibPlayed = Math.Clamp(playedNow, 0, AlliesMmrSystem.CalibrationMatches);
                    int wins2v2 = (int)db.GetPlayerStat(playerId, "ranked2v2_wins");
                    db.SetPlayerStat(playerId, "ranked_2v2_calibration_match_count", calibPlayed);
                    db.SetPlayerStat(playerId, "ranked_2v2_won_match_count", wins2v2);
                    Logger.Log($"[PlayerStats] Allies калибровка {calibPlayed}/{AlliesMmrSystem.CalibrationMatches}, побед {wins2v2} для {playerId}");
                }
                catch (Exception calEx)
                {
                    Logger.LogWarn($"[PlayerStats] calibration counters failed for {playerId}: {calEx.Message}");
                }
            }

            try { PlayerStatsRemoteService.InvalidateStatsCachePublic(playerId); } catch { }
            PushProfileStatsUpdate(playerId);
        }

        // Stats that only the server may write — block any client attempt to set them directly.
        private static readonly System.Collections.Generic.HashSet<string> ServerOnlyStats = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "level_id", "level_xp",
            "ranked_current_mmr", "ranked_target_mmr", "ranked_rank",
            "ranked_2v2_current_mmr", "ranked_2v2_rank",
        };

        public static bool IsClientStatWriteAllowed(string statName)
        {
            if (string.IsNullOrWhiteSpace(statName)) return false;
            if (ServerOnlyStats.Contains(statName)) return false;
            if (statName.StartsWith("_srv_", StringComparison.Ordinal)) return false;

            string n = statName.ToLowerInvariant();

            // Сезон / история сезонов — только сервер (иначе клиент пишет 1 и UI залипает).
            if (n == "current_season_id" || n.Contains("best_rank_history"))
                return false;

            // Оружейная + боевая агрегация клиента (StoreStats).
            if (n.StartsWith("gun_", StringComparison.Ordinal))
                return true;
            // ranked2v2_kills / allies_deaths и т.п. — не звание, а K/D профиля.
            if (n.EndsWith("_kills", StringComparison.Ordinal) || n.EndsWith("_deaths", StringComparison.Ordinal)
                || n.EndsWith("_assists", StringComparison.Ordinal) || n.EndsWith("_hits", StringComparison.Ordinal)
                || n.EndsWith("_shots", StringComparison.Ordinal) || n.EndsWith("_headshots", StringComparison.Ordinal)
                || n.EndsWith("_damage", StringComparison.Ordinal))
            {
                if (n.StartsWith("ranked2v2_", StringComparison.Ordinal) || n.StartsWith("allies_", StringComparison.Ordinal)
                    || n.StartsWith("ranked_", StringComparison.Ordinal) || n.StartsWith("competitive_", StringComparison.Ordinal))
                    return true;
            }

            if (n.Contains("mmr") && (n.Contains("ranked") || n.Contains("allies") || n.Contains("competitive")))
                return false;
            // Только ключи звания (*_rank / current_rank), не слово "ranked" внутри gun_/mode.
            bool looksLikeRankKey =
                n.EndsWith("_rank", StringComparison.Ordinal)
                || n.EndsWith("_current_rank", StringComparison.Ordinal)
                || n.Contains("_rank_", StringComparison.Ordinal)
                || n == "rank";
            if (looksLikeRankKey && (n.Contains("ranked") || n.Contains("allies") || n.Contains("competitive")))
                return false;
            if (n.StartsWith("ranked_", StringComparison.Ordinal) || n.StartsWith("allies_", StringComparison.Ordinal) || n.StartsWith("ranked2v2_", StringComparison.Ordinal))
                return false;
            return true;
        }

        private static bool IsStoreStatPayloadSafe(StorePlayerStat stat)
        {
            if (stat == null || string.IsNullOrWhiteSpace(stat.Name))
                return false;

            if (!IsClientStatWriteAllowed(stat.Name))
            {
                Logger.LogWarn($"[StoreStats] Client tried to set server-only stat '{stat.Name}' — blocked.");
                return false;
            }

            if (float.IsNaN(stat.StoreFloat) || float.IsInfinity(stat.StoreFloat))
                return false;

            return stat.StoreInt >= 0 && stat.StoreLong >= 0 && stat.StoreFloat >= 0f;
        }

        public static string NormalizeRankedGameMode(string gameMode)
        {
            if (string.IsNullOrWhiteSpace(gameMode))
            {
                return null;
            }

            string normalized = gameMode.Trim().ToLowerInvariant();
            if (normalized.Contains("2v2") || normalized.Contains("allies") || normalized.Contains("ally") || normalized.Contains("ranked2v2"))
            {
                return "allies";
            }

            if (normalized.Contains("ranked") || normalized.Contains("defuse") || normalized.Contains("competitive"))
            {
                return "ranked";
            }

            return normalized;
        }

        private static int CalculateRankFromMmr(float mmr)
        {
            return AlliesMmrSystem.RankFromMmr(mmr);
        }

        public static float GetPlayerStat(string playerId, string statName)
        {
            return (float)BoltGameDatabaseProvider.Instance.GetPlayerStat(playerId, statName);
        }

        public static void SetPlayerStat(string playerId, string statName, float value)
        {
            BoltGameDatabaseProvider.Instance.SetPlayerStat(playerId, statName, value);
        }
    }
}
