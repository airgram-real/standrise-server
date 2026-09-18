using System;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace StandRiseServer.RpcServer.Security
{
	public static class PlayerSessionRegistry
	{
		public const int DuplicateSessionErrorCode = 1488;

		private static readonly ConcurrentDictionary<string, UserService> ActiveSessions =
			new ConcurrentDictionary<string, UserService>(StringComparer.Ordinal);

		public static bool HasActiveSession(string playerId, UserService except = null)
		{
			return false;
		}

		public static bool TryAcquire(string playerId, UserService service)
		{
			if (string.IsNullOrWhiteSpace(playerId) || service == null)
			{
				return false;
			}

			ActiveSessions[playerId] = service;
			return true;
		}

		public static void Release(string playerId, UserService service)
		{
			if (string.IsNullOrWhiteSpace(playerId) || service == null)
			{
				return;
			}

			if (ActiveSessions.TryGetValue(playerId, out UserService existing)
			    && ReferenceEquals(existing, service))
			{
				ActiveSessions.TryRemove(playerId, out _);
			}
		}

		private static bool IsSessionAlive(UserService service)
		{
			try
			{
				return service.IsSessionAlive();
			}
			catch
			{
				return false;
			}
		}
	}
}
