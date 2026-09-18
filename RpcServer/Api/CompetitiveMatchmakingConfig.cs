using StandRiseServer.MongoDB;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>
    /// Размер соревновательного матча (режим 6, RankedDefuse) из админских настроек.
    /// Сделан по образцу AlliesMatchmakingConfig: 2 = 1v1, 10 = 5v5.
    ///
    /// Зачем отдельный класс, а не общий с союзниками: у режимов разные значения
    /// (2/4 против 2/10) и переключаются они независимо — один общий счётчик
    /// означал бы, что нельзя включить 1v1 в соревновательном, оставив 2v2 в союзниках.
    /// </summary>
    public static class CompetitiveMatchmakingConfig
    {
        public const int Solo = 2;
        public const int Full = 10;

        private static int _requiredPlayers = Solo;
        private static readonly object _lock = new();

        public static void Refresh()
        {
            lock (_lock)
            {
                try
                {
                    int v = BoltGameDatabaseProvider.Instance.GetMatchmakingAdminSettings().competitiveRequiredPlayers;
                    _requiredPlayers = Normalize(v);
                }
                catch
                {
                    _requiredPlayers = Solo;
                }
                Logger.Log($"[CompetitiveMM] required players = {_requiredPlayers} ({ModeLabel()})");
            }
        }

        public static int GetRequiredPlayers()
        {
            lock (_lock) return _requiredPlayers;
        }

        public static int GetTeamSize() => GetRequiredPlayers() / 2;

        public static bool Is1v1() => GetRequiredPlayers() == Solo;

        /// <summary>В режиме 1v1 группы запрещены: заходят только одиночки.</summary>
        public static int GetMaxPartySize() => Is1v1() ? 1 : 5;

        public static string ModeLabel() => Is1v1() ? "1v1 (2 игрока)" : "5v5 (10 игроков)";

        public static void SetRequiredPlayers(int count)
        {
            count = Normalize(count);
            BoltGameDatabaseProvider.Instance.SetCompetitiveRequiredPlayers(count);
            lock (_lock) _requiredPlayers = count;
            Logger.Log($"[CompetitiveMM] Admin set required players = {count} ({ModeLabel()})");
        }

        // Незаполненное или битое значение считаем 1v1: этот сервер так настроен,
        // а 5v5 включается тумблером в админке и после этого хранится в Mongo.
        private static int Normalize(int count) => count == Full ? Full : Solo;
    }
}
