using Axlebolt.RpcSupport.Protobuf;
using Axlebolt.Bolt.Protobuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.MongoDB.Main.GameSettings;
using Google.Protobuf;

namespace StandRiseServer.RpcServer.Api
{
    public class GameSettingsRemoteService : RpcClass
    {
        private static readonly object CacheLock = new object();
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);
        private static DateTime CacheExpireAtUtc = DateTime.MinValue;
        private static BinaryValue CachedPlainSettingsValue;
        private static ByteString CachedSettingsResponseBytes;

        public GameSettingsRemoteService(UserService user) : base(user)
        {
        }

        public static void InvalidateCache()
        {
            lock (CacheLock)
            {
                CachedPlainSettingsValue = null;
                CachedSettingsResponseBytes = null;
                CacheExpireAtUtc = DateTime.MinValue;
            }
        }

        private static void EnsureGameSettingsCache()
        {
            DateTime utcNow = DateTime.UtcNow;
            if (CachedPlainSettingsValue != null && CachedSettingsResponseBytes != null && CacheExpireAtUtc > utcNow)
            {
                return;
            }

            lock (CacheLock)
            {
                utcNow = DateTime.UtcNow;
                if (CachedPlainSettingsValue != null && CachedSettingsResponseBytes != null && CacheExpireAtUtc > utcNow)
                {
                    return;
                }

                BoltMainDatabaseProvider boltMain = BoltMainDatabaseProvider.Instance;
                GameDocument game = boltMain.GetGameInfo();
                GameSetting[] gameSettings = game?.androidSettings?.Select(a => a.GetGameSetting()).ToArray() ?? Array.Empty<GameSetting>();

                // Разовый дамп имён настроек при каждом обновлении кэша: надо понять, есть ли
                // среди них ключ со ссылкой (магазин/поддержка/соцсети) — тогда кнопку покупки
                // голды можно увести на донат-бота, не трогая APK.
                try
                {
                    Logger.Log("[GameSettings] keys(" + gameSettings.Length + "): "
                        + string.Join(", ", gameSettings.Select(s => s?.Key ?? "?")));
                    // Строковые настройки — единственное место, где может лежать URL.
                    foreach (var s in gameSettings)
                    {
                        if (s != null && s.Type == SettingType.String && !string.IsNullOrWhiteSpace(s.Value))
                            Logger.Log("[GameSettings] string " + s.Key + " = " + s.Value);
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarn("[GameSettings] key dump failed: " + ex.Message);
                }

                CachedPlainSettingsValue = new ToByteMethod(typeof(GameSetting[])).ToBytes(gameSettings);

                GetGameSettingsResponse response = new GetGameSettingsResponse();
                response.GameSettings.Add(gameSettings);
                byte[] responseBytes = response.ToByteArray();
                CachedSettingsResponseBytes = ByteString.CopyFrom(responseBytes);
                CacheExpireAtUtc = utcNow.Add(CacheTtl);
            }
        }

        protected void GetGameSettingsEnc(BinaryValue[] values, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id))
            {
                EnsureGameSettingsCache();
                byte[] key = _user.SessionAesKey ?? Encoding.ASCII.GetBytes("key_abcdefghijkl");
                byte[] IV = _user.SessionAesIV ?? Encoding.ASCII.GetBytes("iv_abcdefghijklm");
                byte[] encryptedSettings = Utils.EncryptByte(CachedSettingsResponseBytes.ToByteArray(), key, IV);

                BinaryValue value = new BinaryValue { IsNull = false, One = ByteString.CopyFrom(encryptedSettings) };
                _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = value } });
                return;
            }
            _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
        }
        protected void GetGameSettings(BinaryValue[] values, string guid)
        {
            if (StaticClasses.Users.TryGetValue(_user.TcpClient, out string Id) || StaticClasses.GameServerUsers.Contains(_user.TcpClient))
            {
                EnsureGameSettingsCache();
                BinaryValue value = CachedPlainSettingsValue?.Clone() ?? new ToByteMethod(typeof(GameSetting[])).ToBytes(Array.Empty<GameSetting>());
                _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Return = value } });
                return;
            }
            _user.SendResponce(new ResponseMessage { RpcResponse = new RpcResponse { Id = guid, Exception = new Axlebolt.RpcSupport.Protobuf.Exception { Id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 8), Code = 401 } } });
        }
        public override void Invoke(RpcRequest request)
        {
            string methodName = (request.MethodName ?? string.Empty).ToLowerInvariant();
            if (methodName == "getgamesettingsencrypted" || methodName == "getgamesettingsencrypted2") GetGameSettingsEnc(request.Params.ToArray(), request.Id);
            else if (methodName == "getgamesettings") GetGameSettings(request.Params.ToArray(), request.Id);
            else MethodNotFound(request);
        }
    }
}
