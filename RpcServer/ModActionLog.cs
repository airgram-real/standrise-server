using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StandRiseServer.RpcServer
{
    // Простой аудит-лог действий модераторов/админов в Telegram-боте. Раньше действия модеров
    // (баны, промокоды, чистка инвентаря и т.д.) нигде не сохранялись - админ не мог посмотреть,
    // что конкретный модер вообще делал. Пишем каждое действие в JSON-lines файл (по одной
    // JSON-записи на строку - легко дописывать и читать частями без парсинга всего файла) и
    // отдаём последние N записей по запросу (доступ к просмотру - только у админов, см.
    // TelegramBotService: кнопка "Логи" видна только в админ-панели).
    public static class ModActionLog
    {
        private const string LogFile = "mod_actions_log.jsonl";
        private static readonly object FileLock = new object();

        private class LogEntry
        {
            public DateTime TimestampUtc { get; set; }
            public long UserId { get; set; }
            public string Username { get; set; }
            public string Action { get; set; }
            public string Details { get; set; }
        }

        public static void Log(long userId, string username, string action, string details)
        {
            try
            {
                var entry = new LogEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    UserId = userId,
                    Username = username,
                    Action = action,
                    Details = details
                };
                string line = JsonSerializer.Serialize(entry);
                lock (FileLock)
                {
                    File.AppendAllText(LogFile, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[ModActionLog] Failed to write log entry: {ex.Message}");
            }
        }

        // Возвращает последние `count` записей, самые свежие первыми. `filterUserId` (если не
        // null) сужает выдачу до действий конкретного модера - используется для "Логи -> по
        // конкретному модеру" в админ-панели.
        public static List<string> GetRecentFormatted(int count, long? filterUserId = null)
        {
            var result = new List<string>();
            try
            {
                if (!File.Exists(LogFile)) return result;

                lock (FileLock)
                {
                    var lines = File.ReadAllLines(LogFile);
                    var entries = new List<LogEntry>();
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var e = JsonSerializer.Deserialize<LogEntry>(line);
                            if (e != null && (filterUserId == null || e.UserId == filterUserId.Value))
                            {
                                entries.Add(e);
                            }
                        }
                        catch { /* skip malformed line */ }
                    }

                    result = entries
                        .OrderByDescending(e => e.TimestampUtc)
                        .Take(count)
                        .Select(e => $"{e.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC | {(string.IsNullOrEmpty(e.Username) ? e.UserId.ToString() : "@" + e.Username)} ({e.UserId}) | {e.Action} | {e.Details}")
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[ModActionLog] Failed to read log: {ex.Message}");
            }
            return result;
        }
    }
}
