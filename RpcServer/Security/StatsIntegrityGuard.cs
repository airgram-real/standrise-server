using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Axlebolt.Bolt.Protobuf;
using MongoDB.Bson;

namespace StandRiseServer.RpcServer.Security
{
	public static class StatsIntegrityGuard
	{
		public const string IntegrityStatName = "_srv_integrity_hex";

		private const int DefaultMaxDeltaPerRequest = 500;

		private static readonly HashSet<string> CumulativeStatSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"_kills", "_deaths", "_assists", "_headshots", "_damage", "_matches", "_wins", "_losses",
			"_shots", "_hits", "_games", "_played", "_xp", "_score", "_time"
		};

		public static bool TryValidateStoreStats(
			string playerId,
			BsonDocument currentStats,
			IEnumerable<StorePlayerStat> incoming,
			out string reason)
		{
			reason = null;
			if (incoming == null)
			{
				return true;
			}

			currentStats ??= new BsonDocument();

			foreach (StorePlayerStat stat in incoming)
			{
				if (stat == null || string.IsNullOrWhiteSpace(stat.Name))
				{
					continue;
				}

				if (stat.Name.StartsWith("_srv_", StringComparison.Ordinal))
				{
					reason = "forbidden meta stat write";
					return false;
				}

				double newValue = ReadStoreValue(stat);
				if (double.IsNaN(newValue) || double.IsInfinity(newValue))
				{
					reason = $"invalid value for {stat.Name}";
					return false;
				}

				if (newValue < 0)
				{
					reason = $"negative value for {stat.Name}";
					return false;
				}

				if (newValue > 10_000_000)
				{
					reason = $"suspicious huge value for {stat.Name}";
					return false;
				}

				if (!IsCumulativeStat(stat.Name))
				{
					continue;
				}

				double oldValue = ReadBsonNumber(currentStats, stat.Name);
				double delta = newValue - oldValue;
				if (delta < 0)
				{
					reason = $"stat rollback blocked: {stat.Name}";
					return false;
				}

				if (delta > GetMaxDelta(stat.Name))
				{
					reason = $"stat spike blocked: {stat.Name} (+{delta})";
					return false;
				}
			}

			return true;
		}

		public static string ComputeIntegrityHex(BsonDocument stats)
		{
			if (stats == null || stats.ElementCount == 0)
			{
				return string.Empty;
			}

			var canonical = new StringBuilder();
			foreach (BsonElement element in stats.Elements.OrderBy(e => e.Name, StringComparer.Ordinal))
			{
				if (element.Name.StartsWith("_srv_", StringComparison.Ordinal))
				{
					continue;
				}

				canonical.Append(element.Name)
					.Append('=')
					.Append(NormalizeBsonValue(element.Value))
					.Append(';');
			}

		 using SHA256 sha = SHA256.Create();
		 byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
		 return Convert.ToHexString(hash).ToLowerInvariant();
		}

		public static void ApplyIntegrityHash(BsonDocument stats)
		{
			if (stats == null)
			{
				return;
			}

			stats[IntegrityStatName] = ComputeIntegrityHex(stats);
		}

		public static bool VerifyIntegrity(BsonDocument stats, out string reason)
		{
			reason = null;
			if (stats == null || !stats.Contains(IntegrityStatName))
			{
				return true;
			}

			string expected = stats[IntegrityStatName].IsString
				? stats[IntegrityStatName].AsString
				: stats[IntegrityStatName].ToString();

			string actual = ComputeIntegrityHex(stats);
			if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
			{
				reason = "stats integrity hex mismatch";
				return false;
			}

			return true;
		}

		private static bool IsCumulativeStat(string name)
		{
			foreach (string suffix in CumulativeStatSuffixes)
			{
				if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return name.Contains("kill", StringComparison.OrdinalIgnoreCase)
			       || name.Contains("death", StringComparison.OrdinalIgnoreCase)
			       || name.Contains("headshot", StringComparison.OrdinalIgnoreCase);
		}

		private static int GetMaxDelta(string statName)
		{
			if (statName.EndsWith("_time", StringComparison.OrdinalIgnoreCase))
			{
				return 60 * 60 * 6;
			}

			if (statName.EndsWith("_xp", StringComparison.OrdinalIgnoreCase)
			    || statName.EndsWith("_score", StringComparison.OrdinalIgnoreCase))
			{
				return 50_000;
			}

			return DefaultMaxDeltaPerRequest;
		}

		private static double ReadStoreValue(StorePlayerStat stat)
		{
			if (stat.StoreLong != 0)
			{
				return stat.StoreLong;
			}

			if (stat.StoreFloat != 0)
			{
				return stat.StoreFloat;
			}

			return stat.StoreInt;
		}

		private static double ReadBsonNumber(BsonDocument doc, string name)
		{
			if (!doc.TryGetValue(name, out BsonValue value) || value == null || value.IsBsonNull)
			{
				return 0;
			}

			if (value.IsInt32)
			{
				return value.AsInt32;
			}

			if (value.IsInt64)
			{
				return value.AsInt64;
			}

			if (value.IsDouble)
			{
				return value.AsDouble;
			}

			if (value.IsString && double.TryParse(value.AsString, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
			{
				return parsed;
			}

			return 0;
		}

		private static string NormalizeBsonValue(BsonValue value)
		{
			if (value == null || value.IsBsonNull)
			{
				return "0";
			}

			if (value.IsInt32)
			{
				return value.AsInt32.ToString(CultureInfo.InvariantCulture);
			}

			if (value.IsInt64)
			{
				return value.AsInt64.ToString(CultureInfo.InvariantCulture);
			}

			if (value.IsDouble)
			{
				return value.AsDouble.ToString("R", CultureInfo.InvariantCulture);
			}

			if (value.IsBoolean)
			{
				return value.AsBoolean ? "1" : "0";
			}

			return value.ToString();
		}
	}
}
