using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Data;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.MongoDB.Main;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace StandRiseServer.RpcServer
{
    // Раньше AcceptTcpClientAsync принимал ЛЮБОЕ количество соединений без ограничений, и на
    // каждое новое соединение UserService.Init() сразу аллоцировал ~30 объектов RemoteService.
    // Флуд коннектами (много "Client connected" подряд в логе, потом процесс "Killed" - это
    // OOM-killer линукса) валит сервер именно через это: ни лимита на IP, ни глобального лимита
    // соединений не было. Здесь - простой guard: троттлинг по IP (не более N коннектов за
    // окно, иначе временный бан IP) + глобальный потолок одновременных соединений.
    internal static class ConnectionGuard
    {
        private const int MaxConnectsPerWindow = 15;
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan BanDuration = TimeSpan.FromSeconds(5);
        private const int MaxConcurrentConnections = 3000;

        private class IpState
        {
            public int Count;
            public DateTime WindowStart;
            public DateTime BannedUntil;
        }

        private static readonly ConcurrentDictionary<string, IpState> _perIp = new ConcurrentDictionary<string, IpState>();
        private static int _activeConnections = 0;

        // true = разрешить соединение (и занять слот, ОБЯЗАТЕЛЬНО потом вызвать ReleaseConnection)
        public static bool TryAcceptConnection(string ip)
        {
            var now = DateTime.UtcNow;

            var state = _perIp.GetOrAdd(ip, _ => new IpState { WindowStart = now });
            lock (state)
            {
                if (state.BannedUntil > now)
                {
                    return false;
                }

                if (now - state.WindowStart > Window)
                {
                    state.WindowStart = now;
                    state.Count = 0;
                }

                state.Count++;
                if (state.Count > MaxConnectsPerWindow)
                {
                    state.BannedUntil = now.Add(BanDuration);
                    Logger.Error($"[ConnectionGuard] IP {ip} banned for {BanDuration.TotalSeconds}s: {state.Count} connects in {Window.TotalSeconds}s");
                    return false;
                }
            }

            if (Interlocked.Increment(ref _activeConnections) > MaxConcurrentConnections)
            {
                Interlocked.Decrement(ref _activeConnections);
                Logger.Error($"[ConnectionGuard] Rejected connection from {ip}: global limit {MaxConcurrentConnections} reached");
                return false;
            }

            return true;
        }

        public static void ReleaseConnection()
        {
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    public class ServerService
    {
        private TcpListener _server;

        public static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }
        public ServerService(int port)
		{
			BoltDatabaseManager.Init();
			//BoltMainDatabaseProvider.Instance = (BoltMainDatabaseProvider)BoltDatabaseManager.Connect(new BoltDatabaseInfo { DatabaseName = "Main", DatabaseType = BoltDatabaseType.Main, Address = "80.66.89.231", Port = "27017", Uri = "mongodb://SKITLSE:GtkzmzapwSTjdAFYqPyP7DNUmRXjLSfG@80.66.89.231:27017/?authMechanism=SCRAM-SHA-256" });
			//BoltGameDatabaseProvider.Instance = (BoltGameDatabaseProvider)BoltDatabaseManager.Connect(new BoltDatabaseInfo { DatabaseName = "Inventory", DatabaseType = BoltDatabaseType.Game, Address = "87.251.78.67", Port = "27017", Uri = "mongodb://SKITLSE:GtkzmzapwSTjdAFYqPyP7DNUmRXjLSfG@87.251.78.67:27017/?authMechanism=SCRAM-SHA-256" });
            BoltMainDatabaseProvider.Instance = (BoltMainDatabaseProvider)BoltDatabaseManager.Connect(LocalServerConfig.ToMainDatabaseInfo());
            BoltGameDatabaseProvider.Instance = (BoltGameDatabaseProvider)BoltDatabaseManager.Connect(LocalServerConfig.ToGameDatabaseInfo());
            
            // Создаём дефолтные ресурсы если их нет
            BoltGameDatabaseProvider.Instance.EnsureDefaultClanAvatar();
            
            _server = new TcpListener(IPAddress.Any, port);
			_server.Start();
			Console.WriteLine("Address: " + GetLocalIPAddress() + ":" + port);
			
		}
		public async Task LoopClients()
		{
			while (true)
			{
				TcpClient newClient = await _server.AcceptTcpClientAsync();

				string ip = "unknown";
				try { ip = ((IPEndPoint)newClient.Client.RemoteEndPoint).Address.ToString(); } catch { }

				if (!ConnectionGuard.TryAcceptConnection(ip))
				{
					// Отклоняем СРАЗУ, до UserService.Init() (там ~30 аллокаций сервисов на
					// коннект) - именно ранний реджект и защищает от флуда/OOM.
					try { newClient.Close(); } catch { }
					continue;
				}

				newClient.NoDelay = true;
				Task a = Task.Run(() => HandleNewClient(newClient));
			}
		}

		private async Task HandleNewClient(TcpClient client)
		{
			try
			{
				UserService user = new UserService(client);
				await user.HandleClient();
			}
			finally
			{
				ConnectionGuard.ReleaseConnection();
			}
		}
	}
}
