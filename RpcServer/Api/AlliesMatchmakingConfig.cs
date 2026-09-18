using StandRiseServer.MongoDB;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>Cached Allies (mode 5/8) match size from admin settings.</summary>
    public static class AlliesMatchmakingConfig
    {
        private static int _requiredPlayers = 2;
        private static readonly object _lock = new();

        public static void Refresh()
        {
            lock (_lock)
            {
                try
                {
                    int v = BoltGameDatabaseProvider.Instance.GetMatchmakingAdminSettings().alliesRequiredPlayers;
                    _requiredPlayers = Normalize(v);
                }
                catch
                {
                    _requiredPlayers = 2;
                }
                Logger.Log($"[AlliesMM] required players = {_requiredPlayers} ({ModeLabel()})");
            }
        }

        public static int GetRequiredPlayers()
        {
            lock (_lock) return _requiredPlayers;
        }

        public static int GetTeamSize() => GetRequiredPlayers() / 2;

        public static bool Is1v1() => GetRequiredPlayers() == 2;

        public static int GetMaxPartySize() => Is1v1() ? 1 : 4;

        public static string ModeLabel()
        {
            return Is1v1() ? "1v1 (2 игрока)" : "2v2 (4 игрока)";
        }

        public static void SetRequiredPlayers(int count)
        {
            count = Normalize(count);
            BoltGameDatabaseProvider.Instance.SetAlliesRequiredPlayers(count);
            lock (_lock) _requiredPlayers = count;
            Logger.Log($"[AlliesMM] Admin set required players = {count} ({ModeLabel()})");
        }

        private static int Normalize(int count) => count == 2 ? 2 : 4;
    }
}
