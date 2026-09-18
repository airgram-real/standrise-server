using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.MongoDB.Main;

namespace StandRiseServer.RpcServer
{
    public partial class DonateBotService
    {
        private static DonateBotService _running;
        private static readonly object VerifyFileGate = new object();
        private static string _verifyFile = "donate_bot_verify_pending.json";

        private readonly ConcurrentDictionary<long, PendingVerify> _pendingVerify = new();

        private sealed class PendingVerify
        {
            public string PlayerId;
            public string Code;
            public long ExpiresUnix;
            public string UidHint;
        }

        private static readonly string[] VerifierUids =
        {
            "StandRework Code",
            "STANDREWORK_CODE",
            "DEV_01",
        };

        private const int VerifyTtlSeconds = 900;

        partial void OnBotStarted(string token)
        {
            _running = this;
            _verifyFile = $"donate_bot_verify_{token.Substring(0, Math.Min(8, token.Length))}.json";
            LoadPendingVerify();
        }

        private void LoadPendingVerify()
        {
            try
            {
                if (!System.IO.File.Exists(_verifyFile)) return;
                var dict = System.Text.Json.JsonSerializer
                    .Deserialize<System.Collections.Generic.Dictionary<long, PendingVerify>>(
                        System.IO.File.ReadAllText(_verifyFile));
                if (dict == null) return;
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (var kv in dict)
                {
                    if (kv.Value != null && kv.Value.ExpiresUnix > now)
                        _pendingVerify[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DonateBot] verify load: {ex.Message}");
            }
        }

        private void SavePendingVerify()
        {
            try
            {
                lock (VerifyFileGate)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var dict = _pendingVerify
                        .Where(kv => kv.Value != null && kv.Value.ExpiresUnix > now)
                        .ToDictionary(k => k.Key, v => v.Value);
                    string json = System.Text.Json.JsonSerializer.Serialize(dict);
                    System.IO.File.WriteAllText(_verifyFile, json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DonateBot] verify save: {ex.Message}");
            }
        }

        private static string GenerateVerifyCode()
        {
            int n = RandomNumberGenerator.GetInt32(100000, 999999);
            return n.ToString();
        }

        private static string ResolveVerifierPlayerId(out string displayName)
        {
            displayName = "StandRework Code";
            foreach (string uid in VerifierUids)
            {
                try
                {
                    var docs = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(uid);
                    if (docs != null && docs.Length > 0)
                    {
                        displayName = string.IsNullOrWhiteSpace(docs[0].name) ? uid : docs[0].name;
                        return docs[0]._id.ToString();
                    }
                }
                catch { }
            }
            return "";
        }

        private bool TryResolveTargetPlayer(string input, out ObjectId oid, out string uid, out string error)
        {
            oid = ObjectId.Empty;
            uid = "";
            error = "";
            input = (input ?? "").Trim();
            if (string.IsNullOrEmpty(input))
            {
                error = "пустой ID";
                return false;
            }

            if (ObjectId.TryParse(input, out ObjectId parsedOid))
            {
                oid = parsedOid;
            }
            else
            {
                var byUid = BoltMainDatabaseProvider.Instance.GetPlayersDocumentsByUid(input);
                if (byUid == null || byUid.Length == 0)
                {
                    error = "игрок не найден";
                    return false;
                }
                oid = byUid[0]._id;
                uid = byUid[0].uid ?? input;
            }

            try
            {
                var inv = BoltGameDatabaseProvider.Instance.GetPlayerInventoryDocument(oid);
                if (inv == null) throw new InvalidOperationException("not found");
            }
            catch
            {
                error = "игрок не найден";
                return false;
            }

            if (string.IsNullOrEmpty(uid))
            {
                try
                {
                    var doc = BoltMainDatabaseProvider.Instance.GetPlayerDocument(oid);
                    uid = doc?.uid ?? oid.ToString();
                }
                catch { uid = oid.ToString(); }
            }

            return true;
        }

        private bool BeginVerification(long telegramId, string input, out string userMessage, out string error)
        {
            userMessage = "";
            error = "";
            if (!TryResolveTargetPlayer(input, out ObjectId oid, out string uid, out error))
                return false;

            string verifierId = ResolveVerifierPlayerId(out string verifierName);
            if (string.IsNullOrEmpty(verifierId))
            {
                error = "системный аккаунт для кодов не найден (создай StandRework Code или DEV_01)";
                return false;
            }

            string code = GenerateVerifyCode();
            long exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + VerifyTtlSeconds;
            _pendingVerify[telegramId] = new PendingVerify
            {
                PlayerId = oid.ToString(),
                Code = code,
                ExpiresUnix = exp,
                UidHint = uid
            };
            SavePendingVerify();

            string text =
                "StandRework — код для Telegram-магазина (@StandReworkBot): " + code + "\n" +
                "Действует 15 минут. Если это не вы — проигнорируйте.";
            GameChatNotify.SendFriendMessage(verifierId, oid.ToString(), text);

            userMessage =
                "Код отправлен в личные сообщения в игре от \"" + verifierName + "\".\n" +
                "Привязка к ID " + uid + ". Код действует 15 минут.\n" +
                "Пришли код сюда одним сообщением.";
            return true;
        }

        private bool ConfirmVerification(long telegramId, string code, out string playerId, out string uid, out string error)
        {
            playerId = "";
            uid = "";
            error = "";
            code = (code ?? "").Trim();
            if (code.Length < 4)
            {
                error = "введи код из игры";
                return false;
            }

            if (!_pendingVerify.TryGetValue(telegramId, out PendingVerify ticket) || ticket == null)
            {
                error = "сначала укажи игровой ID";
                return false;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (ticket.ExpiresUnix <= now)
            {
                _pendingVerify.TryRemove(telegramId, out PendingVerify _);
                SavePendingVerify();
                error = "код устарел — начни снова с игрового ID";
                return false;
            }

            if (!string.Equals(ticket.Code, code, StringComparison.Ordinal))
            {
                error = "неверный код";
                return false;
            }

            playerId = ticket.PlayerId;
            uid = ticket.UidHint ?? "";
            _pendingVerify.TryRemove(telegramId, out PendingVerify _);
            SavePendingVerify();
            return true;
        }

        private void LinkTelegramPlayer(long chatId, long telegramId, string playerId, string uidHint)
        {
            long storeKey = telegramId != 0 ? telegramId : chatId;
            if (storeKey != 0)
                _linkedPlayerId[storeKey] = playerId;
            if (chatId != 0 && chatId != storeKey)
                _linkedPlayerId[chatId] = playerId;
            SaveLinkedPlayers();
            _state[chatId] = DonateState.None;
            Console.WriteLine($"[DonateBot] linked tg={telegramId} player={playerId} uid={uidHint}");
        }

        // ---- HTTP / сайт (тот же процесс, что RPC) ----

        internal static bool BridgeVerifyStart(long telegramId, string idOrUid, out string message, out string error)
        {
            message = "";
            error = "";
            if (_running == null)
            {
                error = "бот не запущен";
                return false;
            }
            return _running.BeginVerification(telegramId, idOrUid, out message, out error);
        }

        internal static bool BridgeVerifyConfirm(long telegramId, string code, out string playerId, out string uid, out string error)
        {
            playerId = "";
            uid = "";
            error = "";
            if (_running == null)
            {
                error = "бот не запущен";
                return false;
            }
            if (!_running.ConfirmVerification(telegramId, code, out playerId, out uid, out error))
                return false;
            _running.LinkTelegramPlayer(telegramId, telegramId, playerId, uid);
            return true;
        }

        internal static bool BridgeIsTelegramLinked(long telegramId)
        {
            if (_running == null) return false;
            return _running._linkedPlayerId.ContainsKey(telegramId);
        }

        internal static string BridgeLinkedPlayerId(long telegramId)
        {
            if (_running == null) return "";
            return _running._linkedPlayerId.TryGetValue(telegramId, out string pid) ? pid : "";
        }
    }
}
