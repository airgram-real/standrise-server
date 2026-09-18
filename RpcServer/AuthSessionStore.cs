using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MongoDB.Bson;

namespace StandRiseServer.RpcServer
{
	/// <summary>
	/// Persists auth tickets across process restarts so reconnect without full re-login
	/// does not fail with RPC 2003 after the server was restarted.
	/// </summary>
	public static class AuthSessionStore
	{
		private static readonly object Sync = new object();
		private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "auth_sessions.json");
		private static bool _loaded;

		private sealed class StoredSession
		{
			public string Ticket { get; set; }
			public string PlayerId { get; set; }
			public string GameVersion { get; set; }
			public string GameCode { get; set; }
		}

		private sealed class StoreFile
		{
			public List<StoredSession> Sessions { get; set; } = new List<StoredSession>();
		}

		public static void Load()
		{
			lock (Sync)
			{
				_loaded = true;
				try
				{
					if (!File.Exists(FilePath))
						return;

					var json = File.ReadAllText(FilePath);
					var data = JsonSerializer.Deserialize<StoreFile>(json);
					if (data?.Sessions == null)
						return;

					int loaded = 0;
					foreach (var s in data.Sessions)
					{
						if (string.IsNullOrWhiteSpace(s.Ticket) || string.IsNullOrWhiteSpace(s.PlayerId))
							continue;
						if (!ObjectId.TryParse(s.PlayerId, out var oid))
							continue;

						var token = new Token
						{
							playerId = oid,
							gameVersion = s.GameVersion ?? StaticClasses.DefaultGameVersion,
							gameCode = s.GameCode ?? "standoff2"
						};
						if (StaticClasses.Tokens.TryAdd(s.Ticket, token))
						{
							StaticClasses.CiphertextToTokenMap[s.Ticket] = s.Ticket;
							StaticClasses.CiphertextToTokenMap[Utils.MD5(s.Ticket)] = s.Ticket;
							loaded++;
						}
					}
					Console.WriteLine($"[boot] Auth sessions restored: {loaded}");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[boot] Auth sessions load warn: {ex.Message}");
				}
			}
		}

		public static void Register(string ticket, Token token)
		{
			if (string.IsNullOrWhiteSpace(ticket) || token == null)
				return;

			StaticClasses.Tokens[ticket] = token;
			StaticClasses.CiphertextToTokenMap[ticket] = ticket;
			try { StaticClasses.CiphertextToTokenMap[Utils.MD5(ticket)] = ticket; } catch { }

			Persist();
		}

		public static bool TryGet(string ticket, out Token token)
		{
			token = null;
			if (string.IsNullOrWhiteSpace(ticket))
				return false;

			EnsureLoaded();
			return StaticClasses.Tokens.TryGetValue(ticket, out token);
		}

		private static void EnsureLoaded()
		{
			if (_loaded) return;
			Load();
		}

		private static void Persist()
		{
			lock (Sync)
			{
				try
				{
					var data = new StoreFile();
					foreach (var kv in StaticClasses.Tokens)
					{
						if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null)
							continue;
						data.Sessions.Add(new StoredSession
						{
							Ticket = kv.Key,
							PlayerId = kv.Value.playerId.ToString(),
							GameVersion = kv.Value.gameVersion,
							GameCode = kv.Value.gameCode
						});
					}
					// Keep file bounded — drop oldest if huge (dict order is undefined; cap size).
					if (data.Sessions.Count > 5000)
						data.Sessions = data.Sessions.GetRange(data.Sessions.Count - 5000, 5000);

					var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
					var tmp = FilePath + ".tmp";
					File.WriteAllText(tmp, json);
					File.Copy(tmp, FilePath, true);
					try { File.Delete(tmp); } catch { }
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[AuthSessionStore] persist warn: {ex.Message}");
				}
			}
		}
	}
}
