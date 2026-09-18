using System;
using System.Collections.Concurrent;
using System.Linq;

namespace StandRiseServer.RpcServer.Security
{
	public static class MarketplaceRequestGuard
	{
		private static readonly ConcurrentDictionary<string, DateTime> RecentRequests =
			new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

		private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(30);

		public static bool IsDuplicate(string requestHash)
		{
			DateTime now = DateTime.UtcNow;
			foreach (string key in RecentRequests.Where(kvp => (now - kvp.Value) > DuplicateWindow).Select(kvp => kvp.Key).ToList())
			{
				RecentRequests.TryRemove(key, out _);
			}

			return !RecentRequests.TryAdd(requestHash, now);
		}
	}
}
