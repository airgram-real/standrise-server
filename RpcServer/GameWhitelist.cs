using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StandRiseServer.RpcServer
{
    public static class GameWhitelist
    {
        private const string RolesFile = "bot_roles.json";
        private static readonly object FileLock = new object();

        private static JsonObject LoadRoot()
        {
            if (!File.Exists(RolesFile))
                return new JsonObject();
            try
            {
                string json = File.ReadAllText(RolesFile);
                return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? new JsonObject();
            }
            catch (Exception ex)
            {
                Logger.Error($"[Whitelist] Failed to load {RolesFile}: {ex.Message}");
                return new JsonObject();
            }
        }

        private static void SaveRoot(JsonObject root)
        {
            File.WriteAllText(RolesFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        }

        private static bool ReadEnabled(JsonObject root)
        {
            return root["WhitelistEnabled"]?.GetValue<bool>() ?? false;
        }

        private static bool ReadRankedQueueLock(JsonObject root)
        {
            return root["RankedQueueLockEnabled"]?.GetValue<bool>() ?? false;
        }

        private static List<string> ReadGameIds(JsonObject root)
        {
            var list = new List<string>();
            if (root["GameWhitelistIds"] is JsonArray arr)
            {
                foreach (var n in arr)
                {
                    string s = n?.ToString()?.Trim('"');
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
            return list;
        }

        public static bool IsEnabled()
        {
            lock (FileLock) return ReadEnabled(LoadRoot());
        }

        public static void SetEnabled(bool enabled)
        {
            lock (FileLock)
            {
                var root = LoadRoot();
                root["WhitelistEnabled"] = enabled;
                SaveRoot(root);
            }
            Logger.Log($"[Whitelist] Server lock {(enabled ? "ON" : "OFF")}");
        }

        public static List<string> GetGameWhitelistIds()
        {
            lock (FileLock) return ReadGameIds(LoadRoot());
        }

        public static void AddGameId(string id)
        {
            id = id?.Trim();
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (FileLock)
            {
                var root = LoadRoot();
                var ids = ReadGameIds(root);
                if (ids.Contains(id)) return;
                ids.Add(id);
                root["GameWhitelistIds"] = new JsonArray(ids.Select(x => (JsonNode)x).ToArray());
                SaveRoot(root);
            }
            Logger.Log($"[Whitelist] Added game id {id}");
        }

        public static void RemoveGameId(string id)
        {
            id = id?.Trim();
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (FileLock)
            {
                var root = LoadRoot();
                var ids = ReadGameIds(root);
                if (!ids.Remove(id)) return;
                root["GameWhitelistIds"] = new JsonArray(ids.Select(x => (JsonNode)x).ToArray());
                SaveRoot(root);
            }
            Logger.Log($"[Whitelist] Removed game id {id}");
        }

        public static bool IsRankedQueueLocked()
        {
            lock (FileLock) return ReadRankedQueueLock(LoadRoot());
        }

        public static void SetRankedQueueLocked(bool enabled)
        {
            lock (FileLock)
            {
                var root = LoadRoot();
                root["RankedQueueLockEnabled"] = enabled;
                SaveRoot(root);
            }
            Logger.Log($"[Whitelist] Ranked queue lock {(enabled ? "ON" : "OFF")}");
        }

        private static bool IsIdListed(ObjectId playerObjectId, List<string> gameIds)
        {
            string objectId = playerObjectId.ToString();
            if (gameIds.Contains(objectId))
                return true;
            try
            {
                PlayerDocument player = BoltMainDatabaseProvider.Instance.GetPlayerDocument(playerObjectId);
                if (!string.IsNullOrWhiteSpace(player?.uid) && gameIds.Contains(player.uid))
                    return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[Whitelist] Failed to read player {objectId}: {ex.Message}");
            }
            return false;
        }

        public static bool IsPlayerAllowed(ObjectId playerObjectId)
        {
            bool enabled;
            List<string> gameIds;
            lock (FileLock)
            {
                var root = LoadRoot();
                enabled = ReadEnabled(root);
                gameIds = ReadGameIds(root);
            }

            if (!enabled)
                return true;

            return IsIdListed(playerObjectId, gameIds);
        }

        /// <summary>Союзники / соревновательный: отдельный замок, те же UID в GameWhitelistIds.</summary>
        public static bool IsRankedQueueAllowed(ObjectId playerObjectId)
        {
            if (!IsRankedQueueLocked())
                return true;
            List<string> gameIds;
            lock (FileLock) { gameIds = ReadGameIds(LoadRoot()); }
            return IsIdListed(playerObjectId, gameIds);
        }
    }
}
