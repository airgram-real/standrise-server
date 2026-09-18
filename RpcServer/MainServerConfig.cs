namespace StandRiseServer.RpcServer
{
    /// <summary>
    /// Единая точка конфигурации основного сервера (Bolt + ranked + Photon на одной VDS).
    /// Клиент всегда ходит сюда; отдельных «рейтинговых» портов нет.
    /// </summary>
    public static class MainServerConfig
    {
        /// <summary>Bolt RPC — основной порт (лобби, матчмейкинг, инвентарь, ranked).</summary>
        public const int RpcPort = 2222;

        /// <summary>HTTP API только для Photon-плагина на этой же машине (не для клиента).</summary>
        public const int HttpApiPort = 2224;

        /// <summary>Photon Master UDP (клиент подключается сюда для комнат).</summary>
        public const int PhotonMasterPort = 5055;

        /// <summary>Photon Game UDP (игровые комнаты).</summary>
        public const int PhotonGamePort = 5056;

        public static string PublicIp => StaticClasses.PublicIp;

        public static string HttpApiBaseUrl => "http://127.0.0.1:" + HttpApiPort;
    }
}
