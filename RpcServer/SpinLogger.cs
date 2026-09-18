using System;
using System.IO;

namespace StandRiseServer.RpcServer
{
    /// <summary>
    /// Отдельный канал логов спина → logs/spin.log (и дубль в tcp_*.log через Logger).
    /// </summary>
    public static class SpinLogger
    {
        private static readonly object Lock = new object();
        private static readonly string LogsDirectory =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string SpinLogPath =
            Path.Combine(LogsDirectory, "spin.log");

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message) => Write("WARN", message, null);
        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception ex)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                + " [" + level + "] " + (message ?? "");
            if (ex != null)
                line += Environment.NewLine + ex;
            lock (Lock)
            {
                try
                {
                    Directory.CreateDirectory(LogsDirectory);
                    File.AppendAllText(SpinLogPath, line + Environment.NewLine);
                }
                catch { }
            }
            try
            {
                if (level == "ERROR")
                    StandRiseServer.Logger.Error("[SPIN] " + message + (ex != null ? " | " + ex : ""));
                else
                    StandRiseServer.Logger.Log("[SPIN] " + message);
            }
            catch { }
        }
    }
}
