using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Axlebolt.Bolt.Matchmaking.Protobuf;
using Axlebolt.Bolt.Protobuf;
using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Game;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.RpcServer;
using Player = Axlebolt.Bolt.Matchmaking.Protobuf.Player;
using Group = Axlebolt.Bolt.Matchmaking.Protobuf.Group;

namespace StandRiseServer.RpcServer.Api;

public class MatchmakingManager
{
	public sealed class RoomRankedProps
	{
		public int MaxPlayers { get; set; }

		public int TeamSize { get; set; }

		public int AfkBots { get; set; }

		public int RoundCount { get; set; }
	}

	public class QueuedPlayer
	{
		public string PlayerId { get; set; }

		public int GameMode { get; set; }

		public string Region { get; set; }

		public DateTime QueuedAt { get; set; }

		public int Mmr { get; set; }

		public string MapCondition { get; set; } = "";


		public List<string> PartyMembers { get; set; } = new List<string>();

	}

	public class PendingMatch
	{
		public string MatchId { get; set; }

		public int GameMode { get; set; }

		public string Region { get; set; }

		public string RoomId { get; set; }

		public string ChosenMap { get; set; }

		public List<QueuedPlayer> Groups { get; set; }

		public HashSet<string> ConfirmedPlayers { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);


		public CancellationTokenSource TimeoutToken { get; set; }

		public MatchGroup MatchGroup { get; set; }

		public DateTime CreatedAt { get; set; }

		public bool DoneSent { get; set; }

		public HashSet<string> ConfirmationProgressSent { get; set; } = new HashSet<string>();

		public HashSet<string> SearchDoneSent { get; set; } = new HashSet<string>();

		public HashSet<string> PrimaryDoneMirrored { get; set; } = new HashSet<string>();

		public bool FinalizingStarted { get; set; }

	}

	private static readonly ConcurrentDictionary<string, QueuedPlayer> _queue = new ConcurrentDictionary<string, QueuedPlayer>();

	private static readonly ConcurrentDictionary<string, PendingMatch> _pendingMatches = new ConcurrentDictionary<string, PendingMatch>();

	/// <summary>
	/// Матчи, которые только что стартовали, по игрокам. Нужен, чтобы повторный confirm
	/// (клиент 0.17 долбит кнопку, пока окно не закрылось) не гасил игроку матч ошибкой
	/// «Match is no longer available», а переотправил ему Done + onGameStarted на тот сокет,
	/// с которого пришло нажатие. Живёт минуту — дольше не нужно.
	/// </summary>
	private static readonly ConcurrentDictionary<string, RecentStart> _recentStarts =
		new ConcurrentDictionary<string, RecentStart>(StringComparer.OrdinalIgnoreCase);

	private sealed class RecentStart
	{
		public string MatchId;
		public OnMatchmakingDoneEvent Done;
		public Axlebolt.Bolt.Protobuf.PhotonGame PhotonGame;
		public DateTime At;
	}

	private static readonly ConcurrentDictionary<string, int> _roomAfkBots = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private static readonly ConcurrentDictionary<string, RoomRankedProps> _roomRankedProps = new ConcurrentDictionary<string, RoomRankedProps>(StringComparer.OrdinalIgnoreCase);

	private static int _isMatching = 0;

	private static readonly string[] RankedDefuseMaps = new string[5] { "sandstone", "province", "rust", "sakura", "zone9" };

	private static readonly string[] Ranked2v2Maps = new string[5] { "sandstone", "rust", "province", "sakura", "zone9" };

	private static int _compMapCursor = -1;
	private static int _alliesMapCursor = -1;

	private static string NextRotatedMap(string[] pool, ref int cursor)
	{
		if (pool == null || pool.Length == 0) return "sandstone";
		cursor = (cursor + 1) % pool.Length;
		return pool[cursor];
	}

	public static void RegisterRoomRankedProps(string roomId, int maxPlayers, int teamSize, int botCount, int roundCount = 0)
	{
		if (!string.IsNullOrWhiteSpace(roomId))
		{
			if (maxPlayers <= 0)
			{
				_roomRankedProps.TryRemove(roomId, out RoomRankedProps _);
				RegisterRoomAfkBots(roomId, botCount);
				return;
			}
			_roomRankedProps[roomId] = new RoomRankedProps
			{
				MaxPlayers = maxPlayers,
				TeamSize = Math.Max(1, teamSize),
				AfkBots = Math.Max(0, botCount),
				RoundCount = roundCount > 0 ? roundCount : 0
			};
			RegisterRoomAfkBots(roomId, botCount);
		}
	}

	public static RoomRankedProps GetRoomRankedProps(string roomId)
	{
		if (string.IsNullOrWhiteSpace(roomId))
		{
			return new RoomRankedProps();
		}
		if (_roomRankedProps.TryGetValue(roomId, out RoomRankedProps value))
		{
			return value;
		}
		return new RoomRankedProps
		{
			AfkBots = GetRoomAfkBots(roomId)
		};
	}

	public static void RegisterRoomAfkBots(string roomId, int botCount)
	{
		if (!string.IsNullOrWhiteSpace(roomId))
		{
			if (botCount <= 0)
			{
				_roomAfkBots.TryRemove(roomId, out var _);
			}
			else
			{
				_roomAfkBots[roomId] = botCount;
			}
			if (_roomRankedProps.TryGetValue(roomId, out RoomRankedProps value2))
			{
				value2.AfkBots = Math.Max(0, botCount);
			}
		}
	}

	public static int GetRoomAfkBots(string roomId)
	{
		if (string.IsNullOrWhiteSpace(roomId))
		{
			return 0;
		}
		if (!_roomAfkBots.TryGetValue(roomId, out var value))
		{
			return 0;
		}
		return value;
	}

	private static string ChooseMapForMode(int gameMode, string region, string matchId)
	{
		string[] array = gameMode switch
		{
			6 => RankedDefuseMaps, 
			5 => Ranked2v2Maps, 
			8 => Ranked2v2Maps, 
			_ => RankedDefuseMaps, 
		};
		if (array.Length == 0)
		{
			return "sandstone";
		}
		// Не хеш playerId: у одних и тех же двух аккаунтов всегда выпадала Zone9.
		if (gameMode == 5 || gameMode == 8)
			return NextRotatedMap(array, ref _alliesMapCursor);
		return NextRotatedMap(array, ref _compMapCursor);
	}

	public static List<string> GetQueuedHumanRegions(int gameMode)
	{
		return (from q in _queue.Values
			where q.GameMode == gameMode && !AfkBotManager.IsBot(q.PlayerId)
			select (!string.IsNullOrWhiteSpace(q.Region)) ? q.Region.Trim().ToLowerInvariant() : "msk").Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
	}

	public static (bool success, string error) AddToQueue(string playerId, int gameMode, string region, string mapCondition = "", List<string> partyMembers = null)
	{
		List<string> list = partyMembers ?? new List<string> { playerId };
		if (list.Count == 0)
		{
			list = new List<string> { playerId };
		}
		if ((gameMode == 5 || gameMode == 8) && list.Count > AlliesMatchmakingConfig.GetMaxPartySize())
		{
			int maxPartySize = AlliesMatchmakingConfig.GetMaxPartySize();
			return (success: false, error: (maxPartySize == 1) ? "Allies 1v1: only solo queue is allowed." : "Party size cannot exceed 4 for 2v2 modes.");
		}
		if (gameMode == 6 && list.Count > CompetitiveMatchmakingConfig.GetMaxPartySize())
		{
			int maxCompParty = CompetitiveMatchmakingConfig.GetMaxPartySize();
			return (success: false, error: (maxCompParty == 1)
				? "Competitive 1v1: only solo queue is allowed."
				: "Party size cannot exceed 5 for competitive 5v5.");
		}
		HashSet<string> hashSet = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
		foreach (QueuedPlayer value in _queue.Values)
		{
			if (value.PartyMembers == null || value.PartyMembers.Count != hashSet.Count || !new HashSet<string>(value.PartyMembers, StringComparer.OrdinalIgnoreCase).SetEquals(hashSet))
			{
				continue;
			}
			Logger.Log($"[Matchmaking] Player {playerId} start for existing party of {hashSet.Count} members (already queued under {value.PlayerId}); sending progress.");
			SendProgressToQueuedPlayer(value);
			foreach (string partyMember in value.PartyMembers)
			{
				if (!string.IsNullOrWhiteSpace(partyMember) && !(partyMember == value.PlayerId))
				{
					SetSearchingState(partyMember);
					SendProgressForQueuedMember(partyMember, value);
				}
			}
			return (success: true, error: null);
		}
		QueuedPlayer queuedPlayer = new QueuedPlayer
		{
			PlayerId = playerId,
			GameMode = gameMode,
			Region = region,
			QueuedAt = DateTime.UtcNow,
			Mmr = 1000,
			MapCondition = mapCondition,
			PartyMembers = list
		};
		try
		{
			PlayerStatsDocument orCreatePlayerStatsDocument = BoltGameDatabaseProvider.Instance.GetOrCreatePlayerStatsDocument(playerId);
			if (orCreatePlayerStatsDocument != null && orCreatePlayerStatsDocument.stats != null)
			{
				object obj;
				switch (gameMode)
				{
				default:
					obj = "ranked_current_mmr";
					break;
				case 11:
					obj = "clan_ranked_current_mmr";
					break;
				case 5:
				case 8:
					obj = "ranked_2v2_current_mmr";
					break;
				}
				string name = (string)obj;
				queuedPlayer.Mmr = (orCreatePlayerStatsDocument.stats.Contains(name) ? orCreatePlayerStatsDocument.stats[name].ToInt32() : 1000);
				if (queuedPlayer.Mmr <= 0)
				{
					queuedPlayer.Mmr = 1000;
				}
			}
		}
		catch
		{
		}
		_queue[playerId] = queuedPlayer;
		SetSearchingState(playerId);
		Logger.Log($"[Matchmaking] Player {playerId} added to queue for mode {gameMode}, region {region}. MMR: {queuedPlayer.Mmr}, Party size: {queuedPlayer.PartyMembers.Count}");
		PromoteFlowForPartyMembers(queuedPlayer.PartyMembers);
		SendProgressToQueuedPlayer(queuedPlayer);
		if (queuedPlayer.PartyMembers != null)
		{
			foreach (string partyMember2 in queuedPlayer.PartyMembers)
			{
				if (!string.IsNullOrWhiteSpace(partyMember2) && !(partyMember2 == playerId))
				{
					SetSearchingState(partyMember2);
					SendProgressForQueuedMember(partyMember2, queuedPlayer);
				}
			}
		}
		if (Interlocked.CompareExchange(ref _isMatching, 1, 0) == 0)
		{
			TryMatchLoop();
		}
		return (success: true, error: null);
	}

	private static void SetSearchingState(string playerId)
	{
		try
		{
			PlayerStatus orAdd = StaticClasses.PlayersStatus.GetOrAdd(playerId, (string _) => new PlayerStatus());
			string lobbyId = orAdd.playInGame?.lobbyId ?? string.Empty;
			string lobbyName = orAdd.playInGame?.lobbyName ?? string.Empty;
			orAdd.playInGame = new PlayInGame
			{
				gameCode = "standoff2",
				gameVersion = StaticClasses.GetPlayerGameVersion(playerId),
				lobbyId = lobbyId,
				lobbyName = lobbyName,
				photonGame = null
			};
			orAdd.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
			BoltMainDatabaseProvider.Instance.SetPlayerStatus(ObjectId.Parse(playerId), orAdd);
			SendSelfStatus(playerId, orAdd);
		}
		catch (Exception value)
		{
			Logger.Error($"[Matchmaking] Failed to set StateLookingToPlay for party member {playerId}: {value}");
		}
	}

	public static void SendProgressToPlayer(string playerId)
	{
		if (!string.IsNullOrWhiteSpace(playerId) && _queue.TryGetValue(playerId, out QueuedPlayer value))
		{
			SendProgressToQueuedPlayer(value);
		}
	}

	public static bool IsSearching(string playerId)
	{
		string playerId2 = playerId;
		if (_queue.ContainsKey(playerId2))
		{
			return true;
		}
		if (HasPendingMatch(playerId2))
		{
			return true;
		}
		return _queue.Values.Any((QueuedPlayer q) => q.PartyMembers != null && q.PartyMembers.Contains(playerId2));
	}

	public static bool HasPendingMatch(string playerId)
	{
		string playerId2 = playerId;
		if (string.IsNullOrWhiteSpace(playerId2))
		{
			return false;
		}
		return _pendingMatches.Values.Any((PendingMatch m) => m.Groups.Any((QueuedPlayer g) => g.PartyMembers.Contains(playerId2)));
	}

	private static void SendSelfStatus(string playerId, PlayerStatus status)
	{
		if (!string.IsNullOrWhiteSpace(playerId) && status != null && StaticClasses.EventSenders.TryGetValue(playerId, out List<IEventSender> value))
		{
			value.FirstOrDefault((IEventSender item) => item is FriendsRemoteEventSender)?.SendEvent("onPlayerStatusChanged", new object[2]
			{
				playerId,
				status.GetPlayerStatusProto()
			});
		}
	}

	private static bool SendDirectFlowEvent(string playerId, string eventName, bool preferSearchChannel, params object[] args)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(playerId))
			{
				return true;
			}
			if (AfkBotManager.IsBot(playerId))
			{
				return true;
			}
			UserService userService = StaticClasses.ResolveMatchmakingEventChannel(playerId, preferSearchChannel);
			if (userService == null || !userService.IsSessionAlive())
			{
				userService = ResolveLiveUserService(playerId);
				if (userService == null || !userService.IsSessionAlive())
				{
					Logger.LogWarn("[Matchmaking] Direct flow skipped: no live TCP for " + playerId + " event=" + eventName);
					return false;
				}
				if (preferSearchChannel)
				{
					StaticClasses.SetMatchmakingFlowChannel(playerId, userService);
				}
			}
			string value = "unknown";
			try
			{
				value = userService.TcpClient?.Client?.RemoteEndPoint?.ToString() ?? "null";
			}
			catch
			{
			}
			string channelLabel = preferSearchChannel ? "SEARCH" : "PRIMARY";
			MatchmakingRemoteService.MatchmakingFlowEventSender matchmakingFlowEventSender = new MatchmakingRemoteService.MatchmakingFlowEventSender(userService);
			matchmakingFlowEventSender.BoundPlayerId = playerId;
			matchmakingFlowEventSender.SendEventDirect(eventName, args);
			Logger.Log($"[Matchmaking] Direct {eventName} ({channelLabel}) -> {playerId} via {value}");
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error($"[Matchmaking] SendDirectFlowEvent failed event={eventName} player={playerId}: {ex.Message}");
			return false;
		}
	}

	private static bool SendDirectSearchFlowEvent(string playerId, string eventName, params object[] args)
	{
		return SendDirectFlowEvent(playerId, eventName, preferSearchChannel: true, args);
	}

	private static bool SendDirectPrimaryFlowEvent(string playerId, string eventName, params object[] args)
	{
		return SendDirectFlowEvent(playerId, eventName, preferSearchChannel: false, args);
	}

	public static bool SendDirectSearchFlowEventPublic(string playerId, string eventName, params object[] args)
		=> SendDirectSearchFlowEvent(playerId, eventName, args);

	public static bool SendDirectPrimaryFlowEventPublic(string playerId, string eventName, params object[] args)
		=> SendDirectPrimaryFlowEvent(playerId, eventName, args);

	private static void SendFlowEvent(string playerId, string eventName, params object[] args)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(playerId) || AfkBotManager.IsBot(playerId))
			{
				return;
			}
			bool flag = string.Equals(eventName, "onMatchmakingDone", StringComparison.Ordinal) || string.Equals(eventName, "onMatchmakingFail", StringComparison.Ordinal);
			if (!flag && string.Equals(eventName, "onMatchmakingProgress", StringComparison.Ordinal))
			{
				OnMatchmakingProgressEvent onMatchmakingProgressEvent = args?.OfType<OnMatchmakingProgressEvent>().FirstOrDefault();
				if (onMatchmakingProgressEvent != null && (onMatchmakingProgressEvent.State == MatchmakingProgressState.Confirmation || onMatchmakingProgressEvent.State == MatchmakingProgressState.NotConfirmed))
				{
					flag = true;
				}
			}
			if (!flag)
			{
				StaticClasses.PromoteLiveFlowSession(playerId);
			}
			if (StaticClasses.EventSenders.TryGetValue(playerId, out List<IEventSender> value))
			{
				IEventSender eventSender = value.FirstOrDefault((IEventSender s) => s is MatchmakingRemoteService.MatchmakingFlowEventSender);
				if (eventSender != null)
				{
					eventSender.SendEvent(eventName, args);
					return;
				}
			}
			UserService userService = ResolveLiveUserService(playerId);
			if (userService != null)
			{
				MatchmakingRemoteService.MatchmakingFlowEventSender matchmakingFlowEventSender = new MatchmakingRemoteService.MatchmakingFlowEventSender(userService);
				matchmakingFlowEventSender.BoundPlayerId = playerId;
				matchmakingFlowEventSender.SendEvent(eventName, args);
			}
			else
			{
				Logger.Error("[Matchmaking] SendFlowEvent skipped: no live TCP for player=" + playerId + " event=" + eventName);
			}
		}
		catch (Exception ex)
		{
			Logger.Error($"[Matchmaking] SendFlowEvent failed event={eventName} player={playerId}: {ex.Message}");
		}
	}

	private static UserService ResolveLiveUserService(string playerId)
	{
		if (string.IsNullOrWhiteSpace(playerId))
		{
			return null;
		}
		if (StaticClasses.TryGetMatchmakingFlowChannel(playerId, out UserService channel) && channel.IsSessionAlive())
		{
			return channel;
		}
		if (StaticClasses.UserServices.TryGetValue(playerId, out UserService value) && value.IsSessionAlive())
		{
			return value;
		}
		if (StaticClasses.AllUserServices.TryGetValue(playerId, out List<UserService> value2))
		{
			lock (value2)
			{
				return value2.FirstOrDefault((UserService s) => s?.IsSessionAlive() ?? false);
			}
		}
		return null;
	}

	private static void PromoteFlowForPartyMembers(IEnumerable<string> memberIds)
	{
		if (memberIds == null)
		{
			return;
		}
		foreach (string item in memberIds.Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			if (!string.IsNullOrWhiteSpace(item) && !AfkBotManager.IsBot(item))
			{
				StaticClasses.PromoteLiveFlowSession(item);
			}
		}
	}

	private static void SendLobbyEvent(string playerId, string eventName, params object[] args)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(playerId))
			{
				return;
			}
			if (!StaticClasses.TryGetEventSenders(playerId, out List<IEventSender> value))
			{
				Logger.Error("[Matchmaking] SendLobbyEvent skipped: no EventSenders for player=" + playerId + " event=" + eventName);
				return;
			}
			IEventSender eventSender = value.FirstOrDefault((IEventSender s) => s is MatchmakingRemoteService.MatchmakingEventSender);
			if (eventSender == null)
			{
				Logger.Error($"[Matchmaking] SendLobbyEvent skipped: no MatchmakingEventSender for player={playerId} event={eventName} (senders={value.Count})");
			}
			else
			{
				eventSender.SendEvent(eventName, args);
				Logger.Log("[Matchmaking] SendLobbyEvent OK: player=" + playerId + " event=" + eventName);
			}
		}
		catch (Exception ex)
		{
			Logger.Error($"[Matchmaking] SendLobbyEvent failed event={eventName} player={playerId}: {ex.Message}");
		}
	}

	private static string GetMatchGameVersion(IEnumerable<QueuedPlayer> groups)
	{
		// Photon room appVersion must match client 0.17.0 — never take 0.20.x from sessions.
		return StaticClasses.DefaultGameVersion;
	}

	/// <summary>
	/// Перед Done+onGameStarted: search/flow TCP → primary.
	/// Иначе Done уходит в search, клиент рвёт его, onGameStarted — на другой сокет → гость не Join.
	/// </summary>
	private static void BindMatchmakingFlowAsPrimary(string playerId)
	{
		if (string.IsNullOrWhiteSpace(playerId) || AfkBotManager.IsBot(playerId))
			return;
		try
		{
			if (StaticClasses.TryGetMatchmakingFlowChannel(playerId, out var flow)
				&& flow != null && flow.IsSessionAlive())
			{
				StaticClasses.SetMatchmakingFlowChannel(playerId, flow);
				StaticClasses.RegisterUserService(playerId, flow);
				Logger.Log($"[Session] Bound flow channel for start {playerId}");
				return;
			}
			if (StaticClasses.UserServices.TryGetValue(playerId, out var primary)
				&& primary != null && primary.IsSessionAlive())
			{
				StaticClasses.SetMatchmakingFlowChannel(playerId, primary);
				Logger.Log($"[Session] Bound existing primary flow for start {playerId}");
				return;
			}
			StaticClasses.PromoteLiveFlowSession(playerId);
		}
		catch (Exception ex)
		{
			Logger.LogWarn($"[Session] BindMatchmakingFlowAsPrimary failed for {playerId}: {ex.Message}");
		}
	}

	public static void RemoveFromQueue(string playerId)
	{
		RemoveFromQueue(playerId, null, null);
	}

	public static void RemoveFromQueue(string playerId, string preserveLobbyId, string preserveLobbyName)
	{
		string playerId2 = playerId;
		if (_queue.TryRemove(playerId2, out QueuedPlayer value))
		{
			Logger.Log("[Matchmaking] Player " + playerId2 + " removed from queue");
			StaticClasses.ClearMatchmakingFlowChannel(playerId2);
			try
			{
				PlayerStatus orAdd = StaticClasses.PlayersStatus.GetOrAdd(playerId2, (string _) => new PlayerStatus());
				PlayerStatus playerStatus = orAdd;
				if (playerStatus.playInGame == null)
				{
					PlayerStatus playerStatus2 = playerStatus;
					PlayInGame obj = new PlayInGame
					{
						gameCode = "standoff2",
						gameVersion = StaticClasses.GetPlayerGameVersion(playerId2),
						lobbyId = string.Empty,
						lobbyName = string.Empty
					};
					PlayInGame playInGame = obj;
					playerStatus2.playInGame = obj;
				}
				if (!string.IsNullOrEmpty(preserveLobbyId))
				{
					orAdd.playInGame.lobbyId = preserveLobbyId;
					orAdd.playInGame.lobbyName = preserveLobbyName ?? string.Empty;
				}
				else
				{
					orAdd.playInGame.lobbyId = string.Empty;
					orAdd.playInGame.lobbyName = string.Empty;
				}
				orAdd.playInGame.photonGame = null;
				orAdd.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
				BoltMainDatabaseProvider.Instance.SetPlayerStatus(ObjectId.Parse(playerId2), orAdd);
				SendSelfStatus(playerId2, orAdd);
			}
			catch (Exception value2)
			{
				Logger.Error($"[Matchmaking] Failed to restore status to StateOnline for player {playerId2}: {value2}");
			}
			OnMatchmakingFailEvent onMatchmakingFailEvent = new OnMatchmakingFailEvent
			{
				Cause = MatchmakingFailCause.Unknown,
				Message = "Matchmaking cancelled."
			};
			SendFlowEvent(playerId2, "onMatchmakingFail", onMatchmakingFailEvent);
			if (value.PartyMembers != null)
			{
				foreach (string partyMember in value.PartyMembers)
				{
					if (string.IsNullOrWhiteSpace(partyMember) || partyMember == playerId2 || !StaticClasses.EventSenders.ContainsKey(partyMember))
					{
						continue;
					}
					try
					{
						PlayerStatus orAdd2 = StaticClasses.PlayersStatus.GetOrAdd(partyMember, (string _) => new PlayerStatus());
						PlayerStatus playerStatus = orAdd2;
						if (playerStatus.playInGame == null)
						{
							PlayerStatus playerStatus3 = playerStatus;
							PlayInGame obj2 = new PlayInGame
							{
								gameCode = "standoff2",
								gameVersion = StaticClasses.GetPlayerGameVersion(partyMember),
								lobbyId = string.Empty,
								lobbyName = string.Empty
							};
							PlayInGame playInGame = obj2;
							playerStatus3.playInGame = obj2;
						}
						if (!string.IsNullOrEmpty(preserveLobbyId) && StaticClasses.Lobbies.TryGetValue(preserveLobbyId, out BoltLobby value3) && value3.IsLobbyMember(partyMember))
						{
							orAdd2.playInGame.lobbyId = preserveLobbyId;
							orAdd2.playInGame.lobbyName = preserveLobbyName ?? string.Empty;
						}
						else
						{
							orAdd2.playInGame.lobbyId = string.Empty;
							orAdd2.playInGame.lobbyName = string.Empty;
						}
						orAdd2.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
						BoltMainDatabaseProvider.Instance.SetPlayerStatus(ObjectId.Parse(partyMember), orAdd2);
						SendSelfStatus(partyMember, orAdd2);
					}
					catch (Exception value4)
					{
						Logger.Error($"[Matchmaking] Failed to restore status to StateOnline for party member {partyMember}: {value4}");
					}
					SendFlowEvent(partyMember, "onMatchmakingFail", onMatchmakingFailEvent);
				}
			}
		}
		PendingMatch pendingMatch = _pendingMatches.Values.FirstOrDefault((PendingMatch m) => m.Groups.Any((QueuedPlayer g) => g.PartyMembers.Contains(playerId2)));
		if (pendingMatch != null)
		{
			PendingMatch value5;
			if (StaticClasses.EventSenders.ContainsKey(playerId2) || StaticClasses.UserServices.ContainsKey(playerId2) || StaticClasses.AllUserServices.ContainsKey(playerId2))
			{
				Logger.Log($"[Matchmaking] Keep pending {pendingMatch.MatchId}: player {playerId2} still live (reconnect)");
			}
			else if (_pendingMatches.TryRemove(pendingMatch.MatchId, out value5))
			{
				pendingMatch.TimeoutToken.Cancel();
				Logger.Log($"[Matchmaking] Pending match {pendingMatch.MatchId} cancelled because player {playerId2} disconnected/declined.");
				OnMatchmakingFailEvent onMatchmakingFailEvent2 = new OnMatchmakingFailEvent
				{
					Cause = MatchmakingFailCause.GroupMemberDisconnected,
					Message = "A player left or disconnected during confirmation."
				};
				foreach (QueuedPlayer group in pendingMatch.Groups)
				{
					foreach (string partyMember2 in group.PartyMembers)
					{
						if (!(partyMember2 == playerId2) && !AfkBotManager.IsBot(partyMember2))
						{
							SendFlowEvent(partyMember2, "onMatchmakingFail", onMatchmakingFailEvent2);
						}
					}
					if (group.PartyMembers.All((string p) => p != playerId2) && !AfkBotManager.IsBot(group.PlayerId))
					{
						AddToQueue(group.PlayerId, pendingMatch.GameMode, pendingMatch.Region, group.MapCondition, group.PartyMembers);
					}
				}
			}
		}
		QueuedPlayer queuedPlayer = _queue.Values.FirstOrDefault((QueuedPlayer q) => q.PartyMembers != null && q.PlayerId != playerId2 && q.PartyMembers.Contains(playerId2));
		if (queuedPlayer != null)
		{
			RemoveFromQueue(queuedPlayer.PlayerId);
		}
	}

	public static void AbandonPendingForPlayer(string playerId)
	{
		string playerId2 = playerId;
		PendingMatch pendingMatch = _pendingMatches.Values.FirstOrDefault((PendingMatch m) => m.Groups.Any((QueuedPlayer g) => g.PartyMembers.Contains(playerId2)));
		if (pendingMatch == null)
		{
			return;
		}
		lock (pendingMatch)
		{
			if (!_pendingMatches.TryGetValue(pendingMatch.MatchId, out PendingMatch value))
			{
				return;
			}
			bool hadConfirmed = pendingMatch.ConfirmedPlayers.Contains(playerId2);
			Logger.Log($"[Confirm] player_disconnect_during_confirm: match_id={pendingMatch.MatchId}, player={playerId2}, had_confirmed={hadConfirmed}");
			Logger.Log($"[Confirm] confirm_cancel: match_id={pendingMatch.MatchId}, reason=disconnect|decline, by={playerId2}, humans=[{HumanList(pendingMatch)}]");
			pendingMatch.TimeoutToken.Cancel();
			_pendingMatches.TryRemove(pendingMatch.MatchId, out value);
			pendingMatch.ConfirmedPlayers.Remove(playerId2);
			foreach (TeamGroup team in pendingMatch.MatchGroup.Teams)
			{
				foreach (Axlebolt.Bolt.Matchmaking.Protobuf.Group group in team.Groups)
				{
					foreach (Axlebolt.Bolt.Matchmaking.Protobuf.Player player in group.Players)
					{
						if (player.Id == playerId2)
						{
							player.Confirmed = false;
						}
					}
				}
			}
			OnMatchmakingProgressEvent onMatchmakingProgressEvent = new OnMatchmakingProgressEvent
			{
				State = MatchmakingProgressState.NotConfirmed,
				MatchGroup = pendingMatch.MatchGroup
			};
			OnMatchmakingFailEvent onMatchmakingFailEvent = new OnMatchmakingFailEvent
			{
				Cause = MatchmakingFailCause.GroupMemberNotConfirmed,
				Message = "A player declined the match."
			};
			foreach (QueuedPlayer group2 in pendingMatch.Groups)
			{
				foreach (string partyMember in group2.PartyMembers)
				{
					if (!AfkBotManager.IsBot(partyMember))
					{
						bool pSearch = SendDirectSearchFlowEvent(partyMember, "onMatchmakingProgress", onMatchmakingProgressEvent);
						bool pPrimary = SendDirectPrimaryFlowEvent(partyMember, "onMatchmakingProgress", onMatchmakingProgressEvent);
						if (!pSearch && !pPrimary)
							SendFlowEvent(partyMember, "onMatchmakingProgress", onMatchmakingProgressEvent);
						bool fSearch = SendDirectSearchFlowEvent(partyMember, "onMatchmakingFail", onMatchmakingFailEvent);
						bool fPrimary = SendDirectPrimaryFlowEvent(partyMember, "onMatchmakingFail", onMatchmakingFailEvent);
						if (!fSearch && !fPrimary)
							SendFlowEvent(partyMember, "onMatchmakingFail", onMatchmakingFailEvent);
						Logger.Log($"[Matchmaking] abandon fail -> {partyMember} search={fSearch} primary={fPrimary}");
						if (partyMember == playerId2)
						{
							RestoreOnlineAfterPendingCancel(partyMember, pendingMatch);
						}
					}
				}
				_queue.TryRemove(group2.PlayerId, out QueuedPlayer _);
			}
			Logger.Log($"[Matchmaking] Pending match {pendingMatch.MatchId} cancelled: {playerId2} declined.");
		}
	}

	/// <summary>Сколько живых людей (без ботов) участвует в подборе.</summary>
	private static int HumanCount(PendingMatch pending)
	{
		if (pending?.Groups == null) return 0;
		return pending.Groups
			.SelectMany((QueuedPlayer g) => g.PartyMembers)
			.Where((string id) => !string.IsNullOrWhiteSpace(id) && !AfkBotManager.IsBot(id))
			.Distinct()
			.Count();
	}

	/// <summary>Список людей матча — для логов confirm_*.</summary>
	private static string HumanList(PendingMatch pending)
	{
		if (pending?.Groups == null) return "";
		return string.Join(",", pending.Groups
			.SelectMany((QueuedPlayer g) => g.PartyMembers)
			.Where((string id) => !string.IsNullOrWhiteSpace(id) && !AfkBotManager.IsBot(id))
			.Distinct());
	}

	/// <summary>Запоминает старт матча для игрока — чтобы переотправить его по повторному confirm.</summary>
	private static void RememberStartedMatch(string playerId, string matchId,
		OnMatchmakingDoneEvent done, Axlebolt.Bolt.Protobuf.PhotonGame photonGame)
	{
		if (string.IsNullOrWhiteSpace(playerId) || AfkBotManager.IsBot(playerId)) return;
		_recentStarts[playerId] = new RecentStart
		{
			MatchId = matchId,
			Done = done,
			PhotonGame = photonGame,
			At = DateTime.UtcNow
		};

		// Чистим протухшее, чтобы словарь не рос бесконечно.
		foreach (var kv in _recentStarts)
		{
			if ((DateTime.UtcNow - kv.Value.At).TotalSeconds > 60)
				_recentStarts.TryRemove(kv.Key, out RecentStart _);
		}
	}

	/// <summary>
	/// Повторный confirm по уже стартовавшему матчу: вместо Fail переотправляем старт.
	/// Возвращает true, если игроку было что переотправить.
	/// </summary>
	/// <summary>
	/// Один TCP на событие. Дубль onPlayersConfirmed/Done на search+primary ломает UI 0.17
	/// (кнопка «ПОДТВЕРДИТЬ» не прожимается, окно залипает).
	/// </summary>
	private static bool SendMatchFlowSingle(string playerId, string eventName, params object[] args)
	{
		if (string.IsNullOrWhiteSpace(playerId)) return false;
		if (SendDirectSearchFlowEvent(playerId, eventName, args)) return true;
		return SendDirectPrimaryFlowEvent(playerId, eventName, args);
	}

	private static void DeliverPlayersConfirmed(string playerId, OnPlayersConfirmedEvent evt)
	{
		if (string.IsNullOrWhiteSpace(playerId) || evt == null) return;
		SendMatchFlowSingle(playerId, "onPlayersConfirmed", evt);
	}

	private static bool TryResendRecentStart(string playerId)
	{
		if (string.IsNullOrWhiteSpace(playerId)) return false;
		if (!_recentStarts.TryGetValue(playerId, out RecentStart recent) || recent == null) return false;
		if ((DateTime.UtcNow - recent.At).TotalSeconds > 60)
		{
			_recentStarts.TryRemove(playerId, out RecentStart _);
			return false;
		}

		try
		{
			if (recent.Done != null)
				SendMatchFlowSingle(playerId, "onMatchmakingDone", recent.Done);
			if (recent.PhotonGame != null)
				SendMatchFlowSingle(playerId, "onGameStarted", recent.PhotonGame);
			Logger.Log($"[Confirm] confirm_recv: player={playerId}, state=ALREADY_STARTED match_id={recent.MatchId} — переотправил Done+onGameStarted (dual)");
			return true;
		}
		catch (Exception ex)
		{
			Logger.LogWarn($"[Confirm] resend start failed for {playerId}: {ex.Message}");
			return false;
		}
	}

	public static void ResendConfirmProgressIfPending(string playerId)
	{
		if (string.IsNullOrWhiteSpace(playerId))
			return;
		PendingMatch pending = _pendingMatches.Values.FirstOrDefault((PendingMatch m) =>
			m.Groups.Any((QueuedPlayer g) => g.PartyMembers.Any((string mId) => PlayerIdsEqual(mId, playerId))));
		if (pending == null)
			return;
		lock (pending)
		{
			if (pending.FinalizingStarted)
				return;
		}
		SendConfirmationProgressOnce(pending, playerId, forceResend: true);
		if (pending.ConfirmedPlayers.Contains(playerId))
			SendConfirmRosterRefresh(pending, playerId);
	}

	public static void RefreshConfirmUiForPlayer(string playerId)
	{
		if (string.IsNullOrWhiteSpace(playerId))
			return;
		PendingMatch pending = _pendingMatches.Values.FirstOrDefault((PendingMatch m) =>
			m.Groups.Any((QueuedPlayer g) => g.PartyMembers.Any((string mId) => PlayerIdsEqual(mId, playerId))));
		if (pending == null)
			return;
		lock (pending)
		{
			if (!pending.ConfirmedPlayers.Contains(playerId))
				return;
			if (pending.FinalizingStarted)
				return;
		}
		Player rosterPlayer = FindRosterPlayer(pending, playerId)
			?? new Player { Id = playerId, Confirmed = true };
		List<string> humans = (from pid in pending.Groups.SelectMany((QueuedPlayer g) => g.PartyMembers).ToList()
			where !AfkBotManager.IsBot(pid)
			select pid).Distinct().ToList();
		if (humans.Count == 0)
			return;
		SendConfirmRosterRefresh(pending, playerId);
	}

	public static void ConfirmPlayer(string playerId)
	{
		string playerId2 = playerId;
		PendingMatch pending = _pendingMatches.Values.FirstOrDefault((PendingMatch m) =>
			m.Groups.Any((QueuedPlayer g) => g.PartyMembers.Any((string mId) => PlayerIdsEqual(mId, playerId2))));
		if (pending == null)
		{
			// Матча уже нет: отменён по таймауту/дисконнекту или сервер перезапустили,
			// пока окно висело. Раньше мы просто выходили — и меню у игрока оставалось
			// на экране навсегда, выйти можно было только перезапуском клиента.
			// Теперь принудительно гасим окно тем же событием, что и при отмене.
			// Матч мог только что стартовать — тогда это не «отменён», а «окно у клиента
			// не успело закрыться». Гасить такой матч ошибкой нельзя: игрок останется вне
			// комнаты и будет жать ПОДТВЕРДИТЬ по кругу. Сначала пробуем дослать старт.
			if (TryResendRecentStart(playerId2))
			{
				return;
			}

			Logger.Log($"[Confirm] confirm_recv: player={playerId2}, state=NO_PENDING — матч уже стартовал или отменён, закрываю окно");
			var staleFail = new OnMatchmakingFailEvent
			{
				Cause = MatchmakingFailCause.GroupMemberNotConfirmed,
				Message = "Match is no longer available."
			};
			var staleProgress = new OnMatchmakingProgressEvent
			{
				State = MatchmakingProgressState.NotConfirmed
			};
			bool pS = SendDirectSearchFlowEvent(playerId2, "onMatchmakingProgress", staleProgress);
			bool pP = SendDirectPrimaryFlowEvent(playerId2, "onMatchmakingProgress", staleProgress);
			if (!pS && !pP) SendFlowEvent(playerId2, "onMatchmakingProgress", staleProgress);
			bool fS = SendDirectSearchFlowEvent(playerId2, "onMatchmakingFail", staleFail);
			bool fP = SendDirectPrimaryFlowEvent(playerId2, "onMatchmakingFail", staleFail);
			if (!fS && !fP) SendFlowEvent(playerId2, "onMatchmakingFail", staleFail);
			Logger.Log($"[Confirm] confirm_cancel: match_id=none, reason=stale_confirm, player={playerId2}, search={fS}, primary={fP}");
			return;
		}
		lock (pending)
		{
			if (pending.ConfirmedPlayers.Contains(playerId2))
			{
				Logger.Log($"[Confirm] confirm_recv: match_id={pending.MatchId}, player={playerId2}, state=DUPLICATE, confirmed={pending.ConfirmedPlayers.Count}, finalizing={pending.FinalizingStarted}");
				if (pending.FinalizingStarted)
				{
					TryResendRecentStart(playerId2);
					return;
				}
				SyncMatchGroupConfirmedFlags(pending);
				List<string> dupHumans = (from pid in pending.Groups.SelectMany((QueuedPlayer g) => g.PartyMembers).ToList()
					where !AfkBotManager.IsBot(pid)
					select pid).Distinct().ToList();
				Player dupPlayer = FindRosterPlayer(pending, playerId2) ?? new Player { Id = playerId2, Confirmed = true };
				BroadcastConfirmRosterUpdate(pending, playerId2, dupPlayer, dupHumans, dupHumans.All(h => pending.ConfirmedPlayers.Contains(h)));
				return;
			}
			Player rosterHit = FindRosterPlayer(pending, playerId2);
			string canonicalConfirmId = rosterHit?.Id ?? playerId2;
			pending.ConfirmedPlayers.Add(canonicalConfirmId);
			playerId2 = canonicalConfirmId;
			Logger.Log($"[Confirm] confirm_recv: match_id={pending.MatchId}, player={playerId2}, state=WAITING, confirmed={pending.ConfirmedPlayers.Count}/{HumanCount(pending)}");
			// НЕ шлём onMatchmakingDone здесь. Done на search-TCP после первого Confirm
			// закрывал у клиента окно/канал до onGameStarted → при 2/2 матч «не впускал»,
			// а таймер confirm залипал. Done уходит вместе со стартом в FinalizeMatch.
			Axlebolt.Bolt.Matchmaking.Protobuf.Player player = null;
			if (pending.MatchGroup != null)
			{
				foreach (TeamGroup team in pending.MatchGroup.Teams)
				{
					foreach (Axlebolt.Bolt.Matchmaking.Protobuf.Group group in team.Groups)
					{
						foreach (Axlebolt.Bolt.Matchmaking.Protobuf.Player player2 in group.Players)
						{
							if (string.Equals(player2.Id, playerId2, StringComparison.OrdinalIgnoreCase))
							{
								player2.Confirmed = true;
								player = player2;
								break;
							}
						}
					}
				}
			}
			if (player == null)
			{
				player = new Player { Id = playerId2, Confirmed = true };
				Logger.Log("[Confirm] confirm_recv: player not in MatchGroup, stub id=" + playerId2
					+ " matchId=" + pending.MatchId);
			}
			SyncMatchGroupConfirmedFlags(pending);
			List<string> list = (from pid in pending.Groups.SelectMany((QueuedPlayer g) => g.PartyMembers).ToList()
				where !AfkBotManager.IsBot(pid)
				select pid).Distinct().ToList();
			if (list.Count == 0)
			{
				Logger.Log("[Matchmaking] " + pending.MatchId + ": no humans — not starting");
				return;
			}
			List<string> list2 = list.Where((string h) => !pending.ConfirmedPlayers.Contains(h)).ToList();
			bool allConfirmed = list2.Count == 0;

			BroadcastConfirmRosterUpdate(pending, playerId2, player, list, allConfirmed);

			if (!allConfirmed)
			{
				Logger.Log($"[Matchmaking] {pending.MatchId}: waiting ConfirmMatch from {string.Join(",", list2)} (bots may already be confirmed)");
			}
			else
			{
				Logger.Log("[Matchmaking] onPlayersConfirmed all-confirmed -> [" + string.Join(",", list)
					+ "] matchId=" + pending.MatchId);
				if (pending.FinalizingStarted)
				{
					Logger.Log("[Matchmaking] " + pending.MatchId + ": finalize already started");
					return;
				}
				pending.FinalizingStarted = true;
				// НЕ гасим TimeoutToken: если Done/onGameStarted сорвутся, watchdog
				// снимет залипший confirm через Fail. Успешный Finalize сам Cancel.
				Logger.Log($"[Confirm] confirm_all: match_id={pending.MatchId}, players=[{string.Join(",", list)}] — запускаю match_starting");
				int delayMs = LocalServerConfig.Current.MatchStartHoldMs;
				if (delayMs < 0) delayMs = 0;
				if (delayMs > 5000) delayMs = 5000;
				DelayedFinalizeMatch(pending, delayMs);
			}
		}
	}

	private static async Task DelayedFinalizeMatch(PendingMatch pending, int delayMs)
	{
		_ = 1;
		try
		{
			try
			{
				string text = pending.GameMode switch
				{
					6 => "RankedDefuse", 
					8 => "Ranked2v2", 
					5 => "Ranked2v2", 
					_ => "Ranked2v2", 
				} + "_" + pending.MatchId;
				int num = AfkBotManager.CountBotsAmong((from id in pending.Groups.SelectMany((QueuedPlayer g) => g.PartyMembers)
					where !string.IsNullOrWhiteSpace(id)
					select id).ToList());
				int requiredPlayers = AlliesMatchmakingConfig.GetRequiredPlayers();
				int num2 = pending.GameMode switch
				{
					6 => CompetitiveMatchmakingConfig.GetRequiredPlayers(), 
					11 => 5, 
					8 => requiredPlayers, 
					5 => requiredPlayers, 
					_ => requiredPlayers, 
				};
				int rounds = (pending.GameMode == 6 || pending.GameMode == 11) ? 10 : 8;
				RegisterRoomRankedProps(text, num2, Math.Max(1, num2 / 2), num, rounds);
				Logger.Log($"[Matchmaking] Pre-register room props max_players={num2} afk_bots={num} room={text}");
			}
			catch
			{
			}
			Logger.Log($"[Matchmaking] Holding accept UI {delayMs}ms before onGameStarted (match={pending.MatchId})");
			await Task.Delay(delayMs).ConfigureAwait(continueOnCapturedContext: false);
			await FinalizeMatch(pending).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Logger.LogWarn("[Matchmaking] DelayedFinalize failed for " + pending.MatchId + ": " + ex.Message);
			try
			{
				pending.FinalizingStarted = false;
				NotifyFinalizeFailed(pending.Groups, new OnMatchmakingFailEvent
				{
					Cause = MatchmakingFailCause.GroupMemberNotConfirmed,
					Message = "Failed to start match."
				});
				try { pending.TimeoutToken?.Cancel(); } catch { }
				_pendingMatches.TryRemove(pending.MatchId, out PendingMatch _);
			}
			catch { }
		}
	}

	private static HashSet<string> GetAllowedMapsForPlayer(QueuedPlayer player)
	{
		string[] array = player.GameMode switch
		{
			6 => RankedDefuseMaps, 
			5 => Ranked2v2Maps, 
			8 => Ranked2v2Maps, 
			_ => RankedDefuseMaps, 
		};
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(player.MapCondition))
		{
			hashSet.UnionWith(array);
		}
		else
		{
			string[] array2 = array;
			foreach (string text in array2)
			{
				string text2 = ((text == "zone9") ? "zone\\s*9" : Regex.Escape(text));
				if (Regex.IsMatch(player.MapCondition, "(?<!\\w)" + text2 + "(?:[\\s_-]*2x2)?(?!\\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
				{
					hashSet.Add(text);
				}
			}
			if (hashSet.Count == 0)
			{
				hashSet.UnionWith(array);
			}
		}
		return hashSet;
	}

	private static async Task TryMatchLoop()
	{
		try
		{
			while (_queue.Count > 0)
			{
				foreach (int mode in _queue.Values.Select((QueuedPlayer q) => q.GameMode).Distinct().ToList())
				{
					List<QueuedPlayer> list = (from q in _queue.Values
						where q.GameMode == mode
						orderby q.QueuedAt
						select q).ToList();
					if (mode == 5 || mode == 8)
					{
						TryMatchAllies2v2(mode, list);
						continue;
					}
					if (mode == 6 && CompetitiveMatchmakingConfig.Is1v1())
					{
						TryMatchCompetitiveSolo(mode, list);
						continue;
					}
					List<List<QueuedPlayer>> list2 = (from g in list.GroupBy<QueuedPlayer, string>((QueuedPlayer q) => q.Region ?? "msk", StringComparer.OrdinalIgnoreCase)
						select g.ToList()).ToList();
					if (list.Any((QueuedPlayer q) => AfkBotManager.IsBot(q.PlayerId)) && list2.Count > 1)
					{
						list2.Add(list);
					}
					foreach (List<QueuedPlayer> item in list2)
					{
						string text = item[0].Region ?? "msk";
						List<QueuedPlayer> source = item.OrderBy((QueuedPlayer q) => q.QueuedAt).ToList();
						int num = mode switch
						{
							6 => CompetitiveMatchmakingConfig.GetRequiredPlayers(), 
							8 => 4, 
							5 => 4, 
							_ => 1, 
						};
						if (source.Sum((QueuedPlayer p) => p.PartyMembers.Count) < num)
						{
							continue;
						}
						List<QueuedPlayer> list3 = (from q in source
							where _queue.ContainsKey(q.PlayerId)
							select q into p
							orderby p.Mmr
							select p).ToList();
						if (list3.Sum((QueuedPlayer p) => p.PartyMembers.Count) < num)
						{
							continue;
						}
						for (int i = 0; i <= list3.Count - 1; i++)
						{
							if (!_queue.ContainsKey(list3[i].PlayerId))
							{
								continue;
							}
							List<QueuedPlayer> list4 = new List<QueuedPlayer>();
							list4.Add(list3[i]);
							int num2 = list3[i].PartyMembers.Count;
							int num3 = list3[i].Mmr;
							int num4 = list3[i].Mmr;
							HashSet<string> hashSet = new HashSet<string>(GetAllowedMapsForPlayer(list3[i]), StringComparer.OrdinalIgnoreCase);
							double totalSeconds = (DateTime.UtcNow - list3[i].QueuedAt).TotalSeconds;
							int num5 = 200 + (int)(totalSeconds / 10.0) * 50;
							if (num5 > 1200)
							{
								num5 = 1200;
							}
							for (int j = 0; j < list3.Count; j++)
							{
								if (i == j)
								{
									continue;
								}
								QueuedPlayer queuedPlayer = list3[j];
								bool flag = AfkBotManager.IsBot(queuedPlayer.PlayerId);
								bool flag2 = AfkBotManager.IsBot(list3[i].PlayerId);
								if (!flag && !flag2 && Math.Abs(queuedPlayer.Mmr - (num3 + num4) / 2) > num5)
								{
									continue;
								}
								if (num2 + queuedPlayer.PartyMembers.Count <= num)
								{
									if (!flag && !flag2)
									{
										HashSet<string> allowedMapsForPlayer = GetAllowedMapsForPlayer(queuedPlayer);
										HashSet<string> hashSet2 = new HashSet<string>(hashSet, StringComparer.OrdinalIgnoreCase);
										hashSet2.IntersectWith(allowedMapsForPlayer);
										if (hashSet2.Count == 0)
										{
											continue;
										}
										hashSet = hashSet2;
									}
									list4.Add(queuedPlayer);
									num2 += queuedPlayer.PartyMembers.Count;
									num3 = Math.Min(num3, queuedPlayer.Mmr);
									num4 = Math.Max(num4, queuedPlayer.Mmr);
								}
								if (num2 == num)
								{
									break;
								}
							}
							if (num2 == num && hashSet.Count > 0)
							{
								string region = (from g in list4
									where !AfkBotManager.IsBot(g.PlayerId)
									select g.Region).FirstOrDefault((string r) => !string.IsNullOrWhiteSpace(r)) ?? list4[0].Region ?? text;
								CreateMatch(mode, region, list4);
								break;
							}
						}
					}
				}
				foreach (QueuedPlayer value in _queue.Values)
				{
					SendProgressToQueuedPlayer(value);
					if (value.PartyMembers == null)
					{
						continue;
					}
					foreach (string partyMember in value.PartyMembers)
					{
						if (!string.IsNullOrWhiteSpace(partyMember) && !(partyMember == value.PlayerId))
						{
							SendProgressForQueuedMember(partyMember, value);
						}
					}
				}
				await Task.Delay(2000);
			}
		}
		catch (Exception ex)
		{
			Logger.Error("[Matchmaking] Error in MatchLoop: " + ex.Message);
		}
		finally
		{
			Interlocked.Exchange(ref _isMatching, 0);
		}
	}

	private static void SendProgressToQueuedPlayer(QueuedPlayer queued)
	{
		try
		{
			if (!AfkBotManager.IsBot(queued.PlayerId))
			{
				OnMatchmakingProgressEvent onMatchmakingProgressEvent = new OnMatchmakingProgressEvent
				{
					State = MatchmakingProgressState.Matchmaking,
					MatchGroup = BuildSearchProgressMatchGroup(queued)
				};
				SendFlowEvent(queued.PlayerId, "onMatchmakingProgress", onMatchmakingProgressEvent);
			}
		}
		catch (Exception ex)
		{
			Logger.Error("[Matchmaking] Error sending progress event: " + ex.Message);
		}
	}

	private static void SendProgressForQueuedMember(string memberId, QueuedPlayer queued)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(memberId) && queued != null && !AfkBotManager.IsBot(memberId))
			{
				StaticClasses.PromoteLiveFlowSession(memberId);
				OnMatchmakingProgressEvent onMatchmakingProgressEvent = new OnMatchmakingProgressEvent
				{
					State = MatchmakingProgressState.Matchmaking,
					MatchGroup = BuildSearchProgressMatchGroup(queued)
				};
				SendFlowEvent(memberId, "onMatchmakingProgress", onMatchmakingProgressEvent);
				Logger.Log("[Matchmaking] Sent onMatchmakingProgress to party member " + memberId);
			}
		}
		catch (Exception ex)
		{
			Logger.Error("[Matchmaking] Error sending progress to party member " + memberId + ": " + ex.Message);
		}
	}

	private static MatchGroup BuildSearchProgressMatchGroup(QueuedPlayer queued)
	{
		// Поиск соревновательного: Zone9 клиент рисует как «СОЮЗНИКИ».
		// Province — классическая ranked-карта без 2x2.
		string previewMap = queued.GameMode == 6 ? "province" : ChooseMapForMode(queued.GameMode, queued.Region, queued.PlayerId);
		string filterCondition = ToConfirmFilterCondition(queued.GameMode, previewMap);
		MatchGroup obj = new MatchGroup
		{
			FilterCondition = filterCondition
		};
		TeamGroup item = new TeamGroup
		{
			Groups = { BuildProtoGroup(queued) }
		};
		obj.Teams.Add(item);
		obj.Teams.Add(new TeamGroup());
		return obj;
	}

	private static Axlebolt.Bolt.Matchmaking.Protobuf.Group BuildProtoGroup(QueuedPlayer group)
	{
		Axlebolt.Bolt.Matchmaking.Protobuf.Group group2 = new Axlebolt.Bolt.Matchmaking.Protobuf.Group();
		foreach (string item in group.PartyMembers ?? new List<string>())
		{
			string text = "Player";
			string clanId = "";
			try
			{
				PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(item));
				if (playerDocument != null)
				{
					if (!string.IsNullOrWhiteSpace(playerDocument.name))
						text = playerDocument.name;
					else if (!string.IsNullOrWhiteSpace(playerDocument.uid))
						text = playerDocument.uid;
					else
						text = item;
				}
			}
			catch
			{
			}
			if (AfkBotManager.IsBot(item) && (string.IsNullOrWhiteSpace(text) || text == "Player"))
			{
				text = "AFK_Bot";
			}
			group2.Players.Add(new Axlebolt.Bolt.Matchmaking.Protobuf.Player
			{
				Id = item,
				Name = text,
				Online = true,
				Confirmed = false,
				ClanId = clanId
			});
		}
		return group2;
	}

	/// <summary>
	/// Строка для окна «ИГРА НАЙДЕНА». Клиент 0.17 по «2x2» или lowercase
	/// рисует «СОЮЗНИКИ», по «Zone9»/«Sakura» без суффикса — «СОРЕВНОВАТЕЛЬНЫЙ».
	/// Photon C1 берётся из ToPhotonSceneMap, не отсюда.
	/// </summary>
	private static string ToConfirmFilterCondition(int gameMode, string chosenMap)
	{
		string map = ToConfirmMapName(chosenMap);
		bool allies = gameMode == 5 || gameMode == 8;
		if (allies)
		{
			if (map.IndexOf("2x2", StringComparison.OrdinalIgnoreCase) < 0
				&& map.IndexOf("2v2", StringComparison.OrdinalIgnoreCase) < 0)
				return map + " 2x2";
			return map;
		}
		map = map.Replace(" 2x2", "", StringComparison.OrdinalIgnoreCase)
			.Replace(" 2v2", "", StringComparison.OrdinalIgnoreCase)
			.Trim();
		// Zone9 / Zone 9 клиент 0.17 в confirm рисует как «СОЮЗНИКИ».
		if (string.Equals(map, "Zone9", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(map.Replace(" ", ""), "Zone9", StringComparison.OrdinalIgnoreCase))
			return "Province";
		return map;
	}

	/// <summary>
	/// Клиент 0.17 рисует «СОРЕВНОВАТЕЛЬНЫЙ» только если в MatchGroup ≥5 игроков
	/// (официальный 5v5). 1v1 соревновательный иначе всегда «СОЮЗНИКИ».
	/// Добиваем уже подтверждёнными слотами — живых в Groups это не меняет,
	/// ConfirmMatch ждёт только людей.
	/// </summary>
	private static void PadCompetitiveConfirmRoster(MatchGroup matchGroup)
	{
		if (matchGroup == null || matchGroup.Teams.Count < 2) return;
		const int perTeam = 5;
		PadConfirmTeam(matchGroup.Teams[0], perTeam, 1);
		PadConfirmTeam(matchGroup.Teams[1], perTeam, 2);
	}

	private static void PadConfirmTeam(TeamGroup team, int need, int teamNo)
	{
		if (team == null) return;
		int have = 0;
		foreach (Group g in team.Groups)
		{
			if (g?.Players == null) continue;
			have += g.Players.Count;
		}
		if (have >= need) return;
		if (team.Groups.Count == 0)
			team.Groups.Add(new Group());
		Group dst = team.Groups[0];
		int slot = have;
		while (have < need)
		{
			slot++;
			have++;
			string hex = "c0ffee00" + teamNo.ToString("x2") + slot.ToString("x2") + "000000000000";
			if (hex.Length > 24) hex = hex.Substring(0, 24);
			dst.Players.Add(new Player
			{
				Id = hex,
				Name = "",
				Online = true,
				Confirmed = true
			});
		}
	}

	private static string ToConfirmMapName(string map)
	{
		if (string.IsNullOrWhiteSpace(map))
			return "Sandstone";
		// Клиент открывает окно «ИГРА НАЙДЕНА» только на Zone9/Sakura без пробела.
		// «Zone 9» / «Zone 9 2x2» оставляют UI на статусе «ПОДТВЕРЖДЕНИЕ».
		return MapNames.Key(map) switch
		{
			"zone9" => "Zone9",
			"sandstone" => "Sandstone",
			"breeze" => "Sandstone",
			"province" => "Province",
			"rust" => "Rust",
			"sakura" => "Sakura",
			_ => "Sandstone",
		};
	}

	private static string ToPhotonSceneMap(int gameMode, string chosenMap)
	{
		if (string.IsNullOrWhiteSpace(chosenMap))
		{
			return chosenMap ?? "";
		}
		bool num = gameMode == 5 || gameMode == 8;
		string text = ToConfirmMapName(chosenMap);
		if (!num)
		{
			return text.ToLowerInvariant();
		}
		if (text.IndexOf("2x2", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return text;
		}
		return text + " 2x2";
	}

	private static string ToFilterCondition(int gameMode, string chosenMap, List<QueuedPlayer> groups)
	{
		bool num = gameMode == 5 || gameMode == 8;
		string text = groups?.FirstOrDefault()?.MapCondition;
		if (num && !string.IsNullOrWhiteSpace(text) && text.IndexOf("2x2", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			string text2 = text.Trim();
			if (text2.IndexOf(',') >= 0 || text2.IndexOf(';') >= 0)
			{
				string text3 = (from p in text2.Split(new char[2] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
					select p.Trim()).FirstOrDefault((string p) => p.IndexOf("2x2", StringComparison.OrdinalIgnoreCase) >= 0);
				if (!string.IsNullOrEmpty(text3))
				{
					return text3;
				}
			}
			return text2;
		}
		return ToPhotonSceneMap(gameMode, chosenMap);
	}

	private static void CreateMatch(int gameMode, string region, List<QueuedPlayer> groups)
	{
		string text = Guid.NewGuid().ToString("N");
		// Неранговые режимы раньше выходили отсюда молча в трёх местах, и на клиенте
		// это выглядело просто как «не пускает». Теперь каждый выход виден в логе.
		Logger.Log($"[Matchmaking] CreateMatch: mode={gameMode} region='{region}' "
			+ $"groups={groups.Count} players={groups.Sum(g => g.PartyMembers?.Count ?? 1)} matchId={text}");
		string text2 = gameMode switch
		{
			0 => "DeathMatch", 
			1 => "Defuse", 
			2 => "ArmsRace", 
			3 => "Training", 
			4 => "SniperDuel", 
			5 => "Ranked2v2", 
			6 => "RankedDefuse", 
			7 => "Escalation", 
			8 => "Ranked2v2", 
			9 => "SniperDuel", 
			30 => "Arcade", 
			_ => "DeathMatch", 
		};
		string roomId = gameMode switch
		{
			6 => "RankedDefuse", 
			8 => "Ranked2v2", 
			5 => "Ranked2v2", 
			_ => text2, 
		} + "_" + text;
		HashSet<string> hashSet = new HashSet<string>(gameMode switch
		{
			6 => RankedDefuseMaps, 
			5 => Ranked2v2Maps, 
			8 => Ranked2v2Maps, 
			_ => RankedDefuseMaps, 
		}, StringComparer.OrdinalIgnoreCase);
		// Клиент 0.17 в соревновательном шлёт condition='Zone9' как текущий превью,
		// а не как пул карт. Пересечение оставляло одну Zone9 на все матчи.
		if (gameMode != 5 && gameMode != 8 && gameMode != 6)
		{
			foreach (QueuedPlayer group5 in groups)
			{
				HashSet<string> allowedMapsForPlayer = GetAllowedMapsForPlayer(group5);
				hashSet.IntersectWith(allowedMapsForPlayer);
			}
		}
		if (hashSet.Count == 0)
		{
			Logger.LogWarn($"[Matchmaking] CreateMatch ОТМЕНА mode={gameMode}: после пересечения "
				+ $"выбранных игроками карт не осталось ни одной. Условия: "
				+ string.Join(" | ", groups.Select(g => $"{g.PlayerId}:'{g.MapCondition}'")));
			return;
		}
		string text3 = (gameMode == 6)
			? NextRotatedMap(RankedDefuseMaps, ref _compMapCursor)
			: ((gameMode == 5 || gameMode == 8)
				? NextRotatedMap(Ranked2v2Maps, ref _alliesMapCursor)
				: hashSet.ElementAt(Random.Shared.Next(hashSet.Count)));
		Logger.Log($"[Matchmaking] CreateMatch map='{text3}' mode={gameMode} (pool={hashSet.Count})");
		string text4 = ToConfirmFilterCondition(gameMode, text3);
		MatchGroup matchGroup = new MatchGroup
		{
			FilterCondition = text4
		};
		TeamGroup teamGroup = new TeamGroup();
		TeamGroup teamGroup2 = new TeamGroup();
		List<string> list = groups.SelectMany((QueuedPlayer g) => g.PartyMembers).ToList();
		int num = 0;
		int count = list.Count;
		int num2 = Math.Max(1, count / 2);
		if (gameMode == 6)
		{
			// Соревновательный собирается ровно по своему размеру: 2 для 1v1, 10 для 5v5.
			// Деление по командам ниже общее — оно кладёт половину в одну, половину в другую,
			// и для двух одиночек даёт корректный 1v1.
			int compRequired = CompetitiveMatchmakingConfig.GetRequiredPlayers();
			if (count != compRequired)
			{
				Logger.LogWarn($"[Matchmaking] CreateMatch ОТМЕНА mode=6: игроков {count}, "
					+ $"а соревновательный режим требует ровно {compRequired} ({CompetitiveMatchmakingConfig.ModeLabel()})");
				return;
			}
			if (compRequired == CompetitiveMatchmakingConfig.Solo)
			{
				foreach (QueuedPlayer solo in groups)
				{
					if (solo.PartyMembers.Count != 1)
					{
						Logger.LogWarn("[Matchmaking] CreateMatch ОТМЕНА mode=6: в 1v1 группы запрещены");
						return;
					}
				}
			}
		}
		if (gameMode == 5 || gameMode == 8)
		{
			int requiredPlayers = AlliesMatchmakingConfig.GetRequiredPlayers();
			int num3 = requiredPlayers / 2;
			if (count != requiredPlayers)
			{
				Logger.LogWarn($"[Matchmaking] CreateMatch ОТМЕНА mode={gameMode}: игроков {count}, "
					+ $"а режим союзников требует ровно {requiredPlayers}");
				return;
			}
			int num4 = 0;
			if (requiredPlayers == 2)
			{
				foreach (QueuedPlayer item2 in groups.OrderBy((QueuedPlayer g) => g.QueuedAt))
				{
					if (item2.PartyMembers.Count != 1)
					{
						return;
					}
					if (num == 0)
					{
						teamGroup.Groups.Add(BuildProtoGroup(item2));
						num = 1;
						continue;
					}
					if (num4 == 0)
					{
						teamGroup2.Groups.Add(BuildProtoGroup(item2));
						num4 = 1;
						continue;
					}
					return;
				}
			}
			else
			{
				foreach (QueuedPlayer item3 in groups.OrderByDescending((QueuedPlayer g) => g.PartyMembers.Count))
				{
					int count2 = item3.PartyMembers.Count;
					switch (count2)
					{
					default:
						return;
					case 4:
					{
						List<string> list2 = item3.PartyMembers.ToList();
						QueuedPlayer group = new QueuedPlayer
						{
							PlayerId = list2[0],
							PartyMembers = new List<string>
							{
								list2[0],
								list2[1]
							},
							GameMode = item3.GameMode,
							Region = item3.Region,
							MapCondition = item3.MapCondition,
							Mmr = item3.Mmr
						};
						QueuedPlayer group2 = new QueuedPlayer
						{
							PlayerId = list2[2],
							PartyMembers = new List<string>
							{
								list2[2],
								list2[3]
							},
							GameMode = item3.GameMode,
							Region = item3.Region,
							MapCondition = item3.MapCondition,
							Mmr = item3.Mmr
						};
						teamGroup.Groups.Add(BuildProtoGroup(group));
						teamGroup2.Groups.Add(BuildProtoGroup(group2));
						num += 2;
						num4 += 2;
						break;
					}
					case 3:
					{
						List<string> list3 = item3.PartyMembers.ToList();
						QueuedPlayer group3 = new QueuedPlayer
						{
							PlayerId = list3[0],
							PartyMembers = new List<string>
							{
								list3[0],
								list3[1]
							},
							GameMode = item3.GameMode,
							Region = item3.Region,
							MapCondition = item3.MapCondition,
							Mmr = item3.Mmr
						};
						QueuedPlayer group4 = new QueuedPlayer
						{
							PlayerId = list3[2],
							PartyMembers = new List<string> { list3[2] },
							GameMode = item3.GameMode,
							Region = item3.Region,
							MapCondition = item3.MapCondition,
							Mmr = item3.Mmr
						};
						teamGroup.Groups.Add(BuildProtoGroup(group3));
						teamGroup2.Groups.Add(BuildProtoGroup(group4));
						num += 2;
						num4++;
						break;
					}
					case 1:
					case 2:
						if (num + count2 <= num3)
						{
							teamGroup.Groups.Add(BuildProtoGroup(item3));
							num += count2;
							break;
						}
						if (num4 + count2 <= num3)
						{
							teamGroup2.Groups.Add(BuildProtoGroup(item3));
							num4 += count2;
							break;
						}
						return;
					}
				}
			}
		}
		else
		{
			foreach (QueuedPlayer group6 in groups)
			{
				Axlebolt.Bolt.Matchmaking.Protobuf.Group item = BuildProtoGroup(group6);
				if (num < num2)
				{
					teamGroup.Groups.Add(item);
					num += group6.PartyMembers.Count;
				}
				else
				{
					teamGroup2.Groups.Add(item);
				}
			}
		}
		matchGroup.Teams.Add(teamGroup);
		matchGroup.Teams.Add(teamGroup2);
		if (gameMode == 6)
			PadCompetitiveConfirmRoster(matchGroup);
		foreach (QueuedPlayer group7 in groups)
		{
			_queue.TryRemove(group7.PlayerId, out QueuedPlayer _);
		}
		PendingMatch pendingMatch = new PendingMatch
		{
			MatchId = text,
			GameMode = gameMode,
			Region = region,
			RoomId = roomId,
			ChosenMap = text3,
			Groups = groups,
			TimeoutToken = new CancellationTokenSource(),
			MatchGroup = matchGroup,
			CreatedAt = DateTime.UtcNow
		};
		_pendingMatches[text] = pendingMatch;
		if (gameMode == 5 || gameMode == 8 || gameMode == 6 || gameMode == 11)
		{
			int requiredPlayers2 = AlliesMatchmakingConfig.GetRequiredPlayers();
			int num5 = gameMode switch
			{
				6 => CompetitiveMatchmakingConfig.GetRequiredPlayers(), 
				11 => 5, 
				8 => requiredPlayers2, 
				5 => requiredPlayers2, 
				_ => requiredPlayers2, 
			};
			int preRounds = (gameMode == 6 || gameMode == 11) ? 10 : 8;
			RegisterRoomRankedProps(roomId, num5, Math.Max(1, num5 / 2), 0, preRounds);
		}
		if (gameMode == 5 || gameMode == 6 || gameMode == 8 || gameMode == 11)
		{
			string value2 = gameMode switch
			{
				5 => "Allies/Ranked2v2", 
				6 => "RankedDefuse", 
				8 => "Ranked2v2", 
				11 => "ClanRankedDefuse", 
				_ => text2, 
			};
			string value3 = text4;
			foreach (QueuedPlayer group8 in groups)
			{
				foreach (string partyMember in group8.PartyMembers)
				{
					if (SendMatchFoundConfirmUi(pendingMatch, partyMember))
					{
						Logger.Log($"[Matchmaking] Ranked match found ({value2}) for {partyMember}. Sent confirm UI matchId={text} (Map: {value3}).");
					}
					else
					{
						Logger.LogWarn($"[Matchmaking] Ranked match found ({value2}) for {partyMember} but no live TCP for confirm UI matchId={text}");
					}
				}
			}
			StartConfirmationTimeout(pendingMatch);
			StartDoneKeepAlive(pendingMatch);
			AfkBotManager.AutoConfirmBotsInPending(groups.SelectMany((QueuedPlayer g) => g.PartyMembers));
			return;
		}
		if (LocalServerConfig.Current.AlwaysRequireConfirm)
		{
			// Раньше неранговые режимы подтверждались сами и сразу финализировались —
			// игрока бросало в матч без окна ПОДТВЕРДИТЬ. Теперь через него идут все режимы.
			foreach (QueuedPlayer groupC in groups)
			{
				foreach (string memberC in groupC.PartyMembers)
				{
					if (SendMatchFoundConfirmUi(pendingMatch, memberC))
					{
						Logger.Log($"[Matchmaking] Match found ({text2}) for {memberC}. Sent confirm UI matchId={text} (Map: {text4}).");
					}
					else
					{
						Logger.LogWarn($"[Matchmaking] Match found ({text2}) for {memberC} but no live TCP for confirm UI matchId={text}");
					}
				}
			}
			StartConfirmationTimeout(pendingMatch);
			StartDoneKeepAlive(pendingMatch);
			AfkBotManager.AutoConfirmBotsInPending(groups.SelectMany((QueuedPlayer g) => g.PartyMembers));
			return;
		}
		foreach (QueuedPlayer group9 in groups)
		{
			foreach (string partyMember2 in group9.PartyMembers)
			{
				pendingMatch.ConfirmedPlayers.Add(partyMember2);
			}
		}
		_pendingMatches.TryRemove(text, out PendingMatch _);
		Logger.Log($"[Matchmaking] Non-ranked match found ({text2}) auto-confirmed. Finalizing immediately (Map: {text4}).");
		FinalizeMatch(pendingMatch);
	}

	private static void TryMatchAllies2v2(int mode, List<QueuedPlayer> playersInQueue)
	{
		int requiredPlayers = AlliesMatchmakingConfig.GetRequiredPlayers();
		int maxParty = AlliesMatchmakingConfig.GetMaxPartySize();
		List<QueuedPlayer> list = (from q in playersInQueue.Where((QueuedPlayer q) => _queue.ContainsKey(q.PlayerId)).ToList()
			where !AfkBotManager.IsBot(q.PlayerId)
			where (q.PartyMembers?.Count ?? 1) <= maxParty
			orderby q.QueuedAt
			select q).ToList();
		if (list.Sum((QueuedPlayer h) => h.PartyMembers?.Count ?? 1) < requiredPlayers)
		{
			Logger.Log($"[Matchmaking] Allies queue wait: humans={list.Sum((QueuedPlayer h) => h.PartyMembers?.Count ?? 1)}/{requiredPlayers} ({AlliesMatchmakingConfig.ModeLabel()}) ids={string.Join(",", list.Select(p => p.PlayerId))}");
			return;
		}
		List<QueuedPlayer> list2 = new List<QueuedPlayer>();
		int num = TryPickAlliesGroups(list, requiredPlayers, list2);
		if (num == requiredPlayers)
		{
			PromoteFlowForPartyMembers(list2.SelectMany((QueuedPlayer p) => p.PartyMembers));
			string region = list2.Select((QueuedPlayer p) => p.Region).FirstOrDefault((string r) => !string.IsNullOrWhiteSpace(r)) ?? "msk";
			Logger.Log($"[Matchmaking] Allies match ({AlliesMatchmakingConfig.ModeLabel()}): {num} humans (ids={string.Join(",", list2.SelectMany((QueuedPlayer p) => p.PartyMembers))})");
			CreateMatch(mode, region, list2);
		}
	}

	/// <summary>
	/// Соревновательный 1v1. Устроен так же, как подбор у союзников: берём двух
	/// дольше всех ждущих одиночек и запускаем матч.
	///
	/// MMR здесь намеренно не проверяется. Общий путь подбора сводит игроков только
	/// если их рейтинги близки: окно стартует с ±200 и растёт на 50 за каждые
	/// 10 секунд ожидания. На приватном сервере с парой человек в очереди это
	/// означало вечный поиск — 2111 против 1000 сошлись бы только через три минуты.
	/// У союзников этой проверки нет, поэтому 1v1 там и работает.
	/// </summary>
	private static void TryMatchCompetitiveSolo(int mode, List<QueuedPlayer> playersInQueue)
	{
		int requiredPlayers = CompetitiveMatchmakingConfig.GetRequiredPlayers();
		List<QueuedPlayer> list = (from q in playersInQueue.Where((QueuedPlayer q) => _queue.ContainsKey(q.PlayerId)).ToList()
			where !AfkBotManager.IsBot(q.PlayerId)
			where (q.PartyMembers?.Count ?? 1) == 1
			orderby q.QueuedAt
			select q).ToList();
		if (list.Count < requiredPlayers)
		{
			Logger.Log($"[Matchmaking] Competitive queue wait: humans={list.Count}/{requiredPlayers} "
				+ $"({CompetitiveMatchmakingConfig.ModeLabel()}) ids={string.Join(",", list.Select(p => p.PlayerId))}");
			return;
		}
		List<QueuedPlayer> pick = list.Take(requiredPlayers).ToList();
		PromoteFlowForPartyMembers(pick.SelectMany((QueuedPlayer p) => p.PartyMembers));
		string region = pick.Select((QueuedPlayer p) => p.Region).FirstOrDefault((string r) => !string.IsNullOrWhiteSpace(r)) ?? "msk";
		Logger.Log($"[Matchmaking] Competitive match ({CompetitiveMatchmakingConfig.ModeLabel()}): "
			+ $"{pick.Count} humans (ids={string.Join(",", pick.Select(p => p.PlayerId))})");
		CreateMatch(mode, region, pick);
	}

	private static int TryPickAlliesGroups(List<QueuedPlayer> candidates, int required, List<QueuedPlayer> pick)
	{
		pick.Clear();
		int num = 0;
		foreach (QueuedPlayer item in from q in candidates
			orderby q.PartyMembers?.Count ?? 1 descending, q.QueuedAt
			select q)
		{
			int num2 = item.PartyMembers?.Count ?? 1;
			if (num2 >= 1 && num2 <= required && num + num2 <= required)
			{
				pick.Add(item);
				num += num2;
				if (num == required)
				{
					break;
				}
			}
		}
		return num;
	}

	public static void ResendPendingMatchFound(string playerId)
	{
		string playerId2 = playerId;
		if (string.IsNullOrWhiteSpace(playerId2) || AfkBotManager.IsBot(playerId2))
		{
			return;
		}
		PendingMatch pendingMatch = _pendingMatches.Values.FirstOrDefault((PendingMatch m) => m.Groups.Any((QueuedPlayer g) => g.PartyMembers.Contains(playerId2)));
		if (pendingMatch?.MatchGroup == null)
		{
			return;
		}
		lock (pendingMatch)
		{
			if (pendingMatch.ConfirmedPlayers.Contains(playerId2))
			{
				return;
			}
		}
		// Search-TCP закрыт — только Done на primary, без повторного Confirmation (0.17 ломается).
		if (SendMatchFoundDoneToPrimaryOnce(pendingMatch, playerId2))
		{
			Logger.Log("[Matchmaking] Mirrored Done to primary for " + playerId2 + " pending " + pendingMatch.MatchId);
		}
	}

	private static string PendingLobbyName(int gameMode)
	{
		switch (gameMode)
		{
		case 5:
		case 8:
			return "Ranked2v2";
		case 6:
			return "RankedDefuse";
		case 11:
			return "ClanRankedDefuse";
		default:
			return "RankedDefuse";
		}
	}

	private static void SetPendingMatchPlayerStatus(PendingMatch pending, string playerId)
	{
		try
		{
			PlayerStatus orAdd = StaticClasses.PlayersStatus.GetOrAdd(playerId, (string _) => new PlayerStatus());
			orAdd.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
			PlayerStatus playerStatus = orAdd;
			if (playerStatus.playInGame == null)
			{
				PlayInGame obj = new PlayInGame
				{
					gameCode = "standoff2",
					gameVersion = StaticClasses.GetPlayerGameVersion(playerId),
					lobbyId = string.Empty,
					lobbyName = string.Empty
				};
				PlayInGame playInGame = obj;
				playerStatus.playInGame = obj;
			}
			orAdd.playInGame.gameCode = "standoff2";
			orAdd.playInGame.lobbyName = PendingLobbyName(pending.GameMode);
			orAdd.playInGame.photonGame = null;
			BoltMainDatabaseProvider.Instance.SetPlayerStatus(ObjectId.Parse(playerId), orAdd);
			SendSelfStatus(playerId, orAdd);
		}
		catch (Exception ex)
		{
			Logger.LogWarn("[Matchmaking] SetPendingMatchPlayerStatus failed for " + playerId + ": " + ex.Message);
		}
	}

	private static void RestoreOnlineAfterPendingCancel(string playerId, PendingMatch pending)
	{
		try
		{
			PlayerStatus orAdd = StaticClasses.PlayersStatus.GetOrAdd(playerId, (string _) => new PlayerStatus());
			PlayerStatus playerStatus = orAdd;
			if (playerStatus.playInGame == null)
			{
				PlayInGame obj = new PlayInGame
				{
					gameCode = "standoff2",
					gameVersion = StaticClasses.GetPlayerGameVersion(playerId),
					lobbyId = string.Empty,
					lobbyName = string.Empty
				};
				PlayInGame playInGame = obj;
				playerStatus.playInGame = obj;
			}
			orAdd.playInGame.photonGame = null;
			orAdd.playInGame.lobbyId = string.Empty;
			orAdd.playInGame.lobbyName = string.Empty;
			orAdd.playInGame.gameCode = "standoff2";
			orAdd.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
			BoltMainDatabaseProvider.Instance.SetPlayerStatus(ObjectId.Parse(playerId), orAdd);
			SendSelfStatus(playerId, orAdd);
		}
		catch (Exception ex)
		{
			Logger.LogWarn("[Matchmaking] RestoreOnlineAfterPendingCancel failed for " + playerId + ": " + ex.Message);
		}
	}

	private static void NotifyFinalizeFailed(List<QueuedPlayer> groups, OnMatchmakingFailEvent failEvent)
	{
		if (groups == null || failEvent == null)
		{
			return;
		}
		foreach (QueuedPlayer group in groups)
		{
			foreach (string partyMember in group.PartyMembers)
			{
				if (!AfkBotManager.IsBot(partyMember))
				{
					SendFlowEvent(partyMember, "onMatchmakingFail", failEvent);
					ResetPlayerAfterFailedFinalize(partyMember);
				}
			}
			_queue.TryRemove(group.PlayerId, out QueuedPlayer _);
		}
	}

	private static void ResetPlayerAfterFailedFinalize(string playerId)
	{
		try
		{
			PlayerStatus orAdd = StaticClasses.PlayersStatus.GetOrAdd(playerId, (string _) => new PlayerStatus());
			orAdd.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
			if (orAdd.playInGame != null)
			{
				orAdd.playInGame.photonGame = null;
			}
		}
		catch
		{
		}
	}

	private static bool SendMatchFoundConfirmUi(PendingMatch pending, string playerId)
	{
		if (pending == null || string.IsNullOrWhiteSpace(playerId))
		{
			return false;
		}
		if (AfkBotManager.IsBot(playerId))
		{
			return true;
		}
		if (!EnsureSearchFlowChannel(playerId))
		{
			return false;
		}
		SendConfirmBundle(pending, playerId);
		SetPendingMatchPlayerStatus(pending, playerId);
		Logger.Log($"[Confirm] confirm_open: match_id={pending.MatchId}, player={playerId}, players=[{HumanList(pending)}], filter='{pending.MatchGroup?.FilterCondition}', timer={LocalServerConfig.Current.ConfirmTimeoutSeconds}s");
		return true;
	}

	private static void SendConfirmBundle(PendingMatch pending, string playerId, bool forceResend = false)
	{
		if (pending == null || string.IsNullOrWhiteSpace(playerId) || AfkBotManager.IsBot(playerId))
		{
			return;
		}

		if (forceResend)
		{
			SendMatchFoundDoneToPrimaryOnce(pending, playerId);
			return;
		}

		// start() и confirm идут с search-TCP; без Confirmation(3) UI остаётся на «СОЮЗНИКИ».
		// Боты открывают UI через onPlayersConfirmed — в чистом 1v1 его нет до confirm.
		SendConfirmationProgressOnce(pending, playerId, forceResend: false);
		SendPlayersConfirmedPrime(pending, playerId);
		// 0.17: onMatchmakingDone, присланный сразу за Confirmation, схлопывает окно
		// «ИГРА НАЙДЕНА / ПОДТВЕРДИТЬ» — клиент считает подбор завершённым и кнопку не рисует,
		// матч умирает по таймауту с Confirmed 0/N. Done теперь уходит после ConfirmPlayer.
		// Старое поведение возвращается флагом SendDoneWithConfirmUi в local.settings.json.
		if (LocalServerConfig.Current.SendDoneWithConfirmUi)
		{
			SendMatchFoundDoneOnSearch(pending, playerId);
		}
	}

	private static void SendConfirmationProgressOnce(PendingMatch pending, string playerId, bool forceResend = false)
	{
		if (pending == null || string.IsNullOrWhiteSpace(playerId) || AfkBotManager.IsBot(playerId))
		{
			return;
		}
		lock (pending)
		{
			if (!forceResend && pending.ConfirmationProgressSent.Contains(playerId))
			{
				return;
			}
			pending.ConfirmationProgressSent.Add(playerId);
		}
		OnMatchmakingProgressEvent onMatchmakingProgressEvent = new OnMatchmakingProgressEvent
		{
			State = MatchmakingProgressState.Confirmation,
			MatchGroup = pending.MatchGroup
		};
		SendMatchFlowSingle(playerId, "onMatchmakingProgress", onMatchmakingProgressEvent);
	}

	internal static bool PlayerIdsEqual(string a, string b) =>
		!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
		&& string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	private static Player FindRosterPlayer(PendingMatch pending, string playerId)
	{
		if (pending?.MatchGroup?.Teams == null || string.IsNullOrWhiteSpace(playerId))
			return null;
		foreach (TeamGroup team in pending.MatchGroup.Teams)
		{
			if (team?.Groups == null) continue;
			foreach (Group group in team.Groups)
			{
				if (group?.Players == null) continue;
				foreach (Player rosterPlayer in group.Players)
				{
					if (rosterPlayer != null && string.Equals(rosterPlayer.Id, playerId, StringComparison.OrdinalIgnoreCase))
						return rosterPlayer;
				}
			}
		}
		return null;
	}

	private static void SyncMatchGroupConfirmedFlags(PendingMatch pending)
	{
		if (pending?.MatchGroup?.Teams == null) return;
		foreach (TeamGroup team in pending.MatchGroup.Teams)
		{
			if (team?.Groups == null) continue;
			foreach (Group group in team.Groups)
			{
				if (group?.Players == null) continue;
				foreach (Player rosterPlayer in group.Players)
				{
					if (rosterPlayer == null || string.IsNullOrWhiteSpace(rosterPlayer.Id)) continue;
					rosterPlayer.Confirmed = pending.ConfirmedPlayers.Contains(rosterPlayer.Id)
						|| AfkBotManager.IsBot(rosterPlayer.Id);
				}
			}
		}
	}

	/// <summary>
	/// Галочки «кто принял»: confirmer получает NewConfirmedPlayers с собой (кнопка прожата),
	/// остальным — только обновлённый MatchGroup на search+primary.
	/// </summary>
	private static void BroadcastConfirmRosterUpdate(PendingMatch pending, string confirmerId, Player confirmerPlayer, List<string> humans, bool allConfirmed)
	{
		if (pending?.MatchGroup == null || humans == null || humans.Count == 0) return;
		SyncMatchGroupConfirmedFlags(pending);
		foreach (string pid in humans)
		{
			OnPlayersConfirmedEvent evt = new OnPlayersConfirmedEvent { MatchGroup = pending.MatchGroup };
			// Только search-TCP: confirmer получает себя в NewConfirmedPlayers (кнопка «прожата»).
			// Остальным — пустой список, галочки через Confirmed в MatchGroup.
			if (!allConfirmed && confirmerPlayer != null && PlayerIdsEqual(pid, confirmerId))
				evt.NewConfirmedPlayers.Add(confirmerPlayer);

			DeliverPlayersConfirmed(pid, evt);

			Logger.Log("[Matchmaking] confirm roster -> " + pid + " confirmed="
				+ pending.ConfirmedPlayers.Count + "/" + humans.Count
				+ " matchId=" + pending.MatchId + " allConfirmed=" + allConfirmed);
		}
	}

	/// <summary>
	/// Железобетонное правило: пока игрок не нажал ПОДТВЕРДИТЬ, он не получает
	/// onMatchmakingDone НИ ПО ОДНОМУ каналу — ни search, ни primary. Иначе клиент 0.17
	/// считает подбор завершённым и уходит в матч мимо окна подтверждения,
	/// а матч потом не попадает в историю.
	/// </summary>
	private static bool MaySendDone(PendingMatch pending, string playerId)
	{
		if (!LocalServerConfig.Current.AlwaysRequireConfirm) return true;
		if (pending == null || string.IsNullOrWhiteSpace(playerId)) return true;
		if (AfkBotManager.IsBot(playerId)) return true;
		lock (pending)
		{
			if (pending.ConfirmedPlayers.Contains(playerId)) return true;
		}
		Logger.Log("[Matchmaking] Done withheld for " + playerId + " — ещё не подтвердил (matchId=" + pending.MatchId + ")");
		return false;
	}

	/// <summary>
	/// Открывает окно подтверждения в клиенте 0.17. Сам по себе
	/// onMatchmakingProgress(Confirmation) окно не рисует — клиент ждёт onPlayersConfirmed
	/// с составом матча. Раньше это событие прилетало только когда подтверждался бот,
	/// поэтому в матче двух живых игроков окна не было вовсе и поиск шёл бесконечно.
	/// Шлём его сразу и с пустым списком подтвердившихся — это чистый сигнал «открой окно».
	/// </summary>
	private static void SendPlayersConfirmedPrime(PendingMatch pending, string playerId)
	{
		if (!LocalServerConfig.Current.PrimeConfirmWithPlayersConfirmed) return;
		if (pending == null || string.IsNullOrWhiteSpace(playerId) || AfkBotManager.IsBot(playerId)) return;
		if (pending.MatchGroup == null) return;
		try
		{
			OnPlayersConfirmedEvent evt = new OnPlayersConfirmedEvent
			{
				MatchGroup = pending.MatchGroup
			};
			// NewConfirmedPlayers оставляем пустым: это чистый сигнал «открой окно».
			// Непустой список клиент трактует как «фаза подтверждения закончилась» и окно закрывает.
			SendMatchFlowSingle(playerId, "onPlayersConfirmed", evt);
			Logger.Log("[Matchmaking] onPlayersConfirmed prime -> " + playerId + " matchId=" + pending.MatchId);
		}
		catch (System.Exception ex)
		{
			Logger.LogWarn("[Matchmaking] prime onPlayersConfirmed failed: " + ex.Message);
		}
	}

	/// <summary>
	/// Обновляет состав в уже открытом окне подтверждения у игрока, который ещё не нажал
	/// ПОДТВЕРДИТЬ. Непустой NewConfirmedPlayers клиент понимает как «фаза подтверждения
	/// закончилась» и окно закрывает, поэтому список оставляем пустым: меняются только
	/// флаги Confirmed внутри MatchGroup, и в UI появляются галочки принявших.
	/// </summary>
	private static void SendConfirmRosterRefresh(PendingMatch pending, string playerId)
	{
		if (pending == null || string.IsNullOrWhiteSpace(playerId) || AfkBotManager.IsBot(playerId)) return;
		if (pending.MatchGroup == null) return;
		try
		{
			OnPlayersConfirmedEvent evt = new OnPlayersConfirmedEvent
			{
				MatchGroup = pending.MatchGroup
			};
			SendMatchFlowSingle(playerId, "onPlayersConfirmed", evt);
			Logger.Log("[Matchmaking] confirm roster refresh -> " + playerId
				+ " confirmed=" + pending.ConfirmedPlayers.Count
				+ " matchId=" + pending.MatchId);
		}
		catch (System.Exception ex)
		{
			Logger.LogWarn("[Matchmaking] confirm roster refresh failed: " + ex.Message);
		}
	}

	private static OnMatchmakingDoneEvent BuildMatchFoundDoneEvent(PendingMatch pending)
	{
		return new OnMatchmakingDoneEvent
		{
			Id = pending.MatchId,
			MatchGroup = pending.MatchGroup
		};
	}

	private static void SendMatchFoundDoneOnSearch(PendingMatch pending, string playerId)
	{
		if (pending == null || string.IsNullOrWhiteSpace(playerId) || AfkBotManager.IsBot(playerId))
		{
			return;
		}
		if (!MaySendDone(pending, playerId))
		{
			return;
		}
		lock (pending)
		{
			if (pending.SearchDoneSent.Contains(playerId))
			{
				return;
			}
			pending.SearchDoneSent.Add(playerId);
		}
		if (SendDirectSearchFlowEvent(playerId, "onMatchmakingDone", BuildMatchFoundDoneEvent(pending)))
		{
			pending.DoneSent = true;
		}
		else
		{
			lock (pending)
			{
				pending.SearchDoneSent.Remove(playerId);
			}
		}
	}

	/// <summary>
	/// Финальный Done при старте: РОВНО один канал, PRIMARY первым.
	/// Раньше Done уходил в search и убивал matchmaking-listener до onGameStarted —
	/// гости Allies оставались вне комнаты / с залипшим confirm.
	/// </summary>
	private static void SendMatchFoundDoneForStart(PendingMatch pending, string playerId)
	{
		if (pending == null || string.IsNullOrWhiteSpace(playerId) || AfkBotManager.IsBot(playerId))
			return;
		if (!MaySendDone(pending, playerId))
			return;
		var done = BuildMatchFoundDoneEvent(pending);
		bool ok = SendMatchFlowSingle(playerId, "onMatchmakingDone", done);
		if (ok)
		{
			lock (pending)
			{
				pending.SearchDoneSent.Add(playerId);
				pending.PrimaryDoneMirrored.Add(playerId);
				pending.DoneSent = true;
			}
		}
		Logger.Log($"[Matchmaking] Done-for-start -> {playerId} once={ok} match={pending.MatchId}");
	}

	/// <summary>
	/// Честная доставка старта: true только при живом канале.
	/// onGameStarted — ТОЛЬКО PRIMARY (после Done search-listener мёртв; fallback
	/// в search давал sent=True в логе при реальной потере → «впустил одного»).
	/// </summary>
	private static bool TrySendStartDirect(string playerId, string eventName, params object[] args)
	{
		bool gameStarted = string.Equals(eventName, "onGameStarted", StringComparison.Ordinal);
		if (gameStarted)
		{
			if (SendDirectPrimaryFlowEvent(playerId, eventName, args))
			{
				Logger.Log($"[Matchmaking] {eventName} DIRECT(primary) -> {playerId}");
				return true;
			}
			if (SendDirectSearchFlowEvent(playerId, eventName, args))
			{
				Logger.Log($"[Matchmaking] {eventName} DIRECT(search) -> {playerId}");
				return true;
			}
			Logger.LogWarn($"[Matchmaking] {eventName} miss -> {playerId} (primary+search dead)");
			return false;
		}
		if (SendDirectPrimaryFlowEvent(playerId, eventName, args))
		{
			Logger.Log($"[Matchmaking] {eventName} DIRECT(primary) -> {playerId}");
			return true;
		}
		if (SendDirectSearchFlowEvent(playerId, eventName, args))
		{
			Logger.Log($"[Matchmaking] {eventName} DIRECT(search) -> {playerId}");
			return true;
		}
		return false;
	}

	/// <summary>Один канал для старта: PRIMARY → search → queue. Без dual-send.</summary>
	private static bool SendStartFlowEventOncePreferPrimary(string playerId, string eventName, params object[] args)
	{
		if (SendDirectPrimaryFlowEvent(playerId, eventName, args))
		{
			Logger.Log($"[Matchmaking] {eventName} ONCE(primary) -> {playerId}");
			return true;
		}
		if (SendDirectSearchFlowEvent(playerId, eventName, args))
		{
			Logger.Log($"[Matchmaking] {eventName} ONCE(search) -> {playerId}");
			return true;
		}
		SendFlowEvent(playerId, eventName, args);
		Logger.Log($"[Matchmaking] {eventName} ONCE(fallback) -> {playerId}");
		return true;
	}

	/// <summary>Один живой канал: search предпочтительнее primary. Без dual-send.</summary>
	private static bool SendStartFlowEventOnce(string playerId, string eventName, params object[] args)
	{
		if (SendDirectSearchFlowEvent(playerId, eventName, args))
		{
			Logger.Log($"[Matchmaking] {eventName} ONCE(search) -> {playerId}");
			return true;
		}
		if (SendDirectPrimaryFlowEvent(playerId, eventName, args))
		{
			Logger.Log($"[Matchmaking] {eventName} ONCE(primary) -> {playerId}");
			return true;
		}
		SendFlowEvent(playerId, eventName, args);
		Logger.Log($"[Matchmaking] {eventName} ONCE(fallback) -> {playerId}");
		return true;
	}

	private static bool SendMatchFoundDoneToPrimaryOnce(PendingMatch pending, string playerId)
	{
		if (pending == null || string.IsNullOrWhiteSpace(playerId) || AfkBotManager.IsBot(playerId))
		{
			return false;
		}
		if (!MaySendDone(pending, playerId))
		{
			return false;
		}
		lock (pending)
		{
			if (pending.ConfirmedPlayers.Contains(playerId) || pending.PrimaryDoneMirrored.Contains(playerId))
			{
				return false;
			}
		}
		if (StaticClasses.TryGetMatchmakingFlowChannel(playerId, out UserService searchChannel)
			&& searchChannel != null && searchChannel.IsSessionAlive())
		{
			return false;
		}
		if (!SendDirectPrimaryFlowEvent(playerId, "onMatchmakingDone", BuildMatchFoundDoneEvent(pending)))
		{
			return false;
		}
		lock (pending)
		{
			pending.PrimaryDoneMirrored.Add(playerId);
		}
		return true;
	}

	private static bool EnsureSearchFlowChannel(string playerId)
	{
		if (StaticClasses.TryGetMatchmakingFlowChannel(playerId, out UserService channel) && channel.IsSessionAlive())
		{
			return true;
		}
		UserService live = ResolveLiveUserService(playerId);
		if (live != null && live.IsSessionAlive())
		{
			StaticClasses.SetMatchmakingFlowChannel(playerId, live);
			return true;
		}
		Logger.LogWarn("[Matchmaking] Flow event skipped — no search/live TCP for " + playerId);
		return false;
	}

	private static void StartDoneKeepAlive(PendingMatch pending)
	{
		PendingMatch pending2 = pending;
		Task.Run(async delegate
		{
			try
			{
				for (int i = 0; i < 20; i++)
				{
					await Task.Delay(2000, pending2.TimeoutToken.Token);
					if (!_pendingMatches.ContainsKey(pending2.MatchId))
					{
						break;
					}
					foreach (QueuedPlayer group in pending2.Groups)
					{
						foreach (string partyMember in group.PartyMembers)
						{
							if (!AfkBotManager.IsBot(partyMember))
							{
								lock (pending2)
								{
									if (pending2.ConfirmedPlayers.Contains(partyMember))
									{
										continue;
									}
								}
								if (LocalServerConfig.Current.AlwaysRequireConfirm)
								{
									// Неподтвердившему не шлём НИЧЕГО. Повтор Confirmation,
									// который тут стоял раньше, закрывал уже открытое окно
									// «ИГРА НАЙДЕНА» — второй игрок терял кнопку через 2 секунды
									// после того, как подтверждал первый. Done тем более нельзя:
									// он увёл бы игрока в матч мимо подтверждения.
								}
								else
								{
									SendMatchFoundDoneToPrimaryOnce(pending2, partyMember);
								}
							}
						}
					}
				}
			}
			catch (TaskCanceledException)
			{
			}
			catch (Exception ex2)
			{
				Logger.LogWarn("[Matchmaking] Done keepalive error: " + ex2.Message);
			}
		});
	}

	private static void StartConfirmationTimeout(PendingMatch pending)
	{
		PendingMatch pending2 = pending;
		Task.Run(async delegate
		{
			try
			{
				int waitSeconds = LocalServerConfig.Current.ConfirmTimeoutSeconds;
				// Клиентский таймер ~16с. Сервер чуть дольше, но не 45 — иначе
				// подтвердивший залипает в confirm после того, как у второго UI уже сбросился.
				if (waitSeconds < 12) waitSeconds = 12;
				if (waitSeconds > 25) waitSeconds = 25;
				await Task.Delay(waitSeconds * 1000, pending2.TimeoutToken.Token);
				if (pending2.TimeoutToken.Token.IsCancellationRequested)
					return;
				// Finalize уже идёт — даём ещё ~20с на Done/Join, иначе Fail снимет UI.
				if (pending2.FinalizingStarted)
				{
					try { await Task.Delay(20_000, pending2.TimeoutToken.Token); }
					catch (TaskCanceledException) { return; }
					if (pending2.TimeoutToken.Token.IsCancellationRequested)
						return;
				}
				if (!_pendingMatches.TryRemove(pending2.MatchId, out PendingMatch _))
					return;
				Logger.Log($"[Confirm] confirm_cancel: match_id={pending2.MatchId}, reason={(pending2.FinalizingStarted ? "finalize_watchdog" : "timeout")}, waited={waitSeconds}s, confirmed=[{string.Join(",", pending2.ConfirmedPlayers)}], humans=[{HumanList(pending2)}]");
				OnMatchmakingFailEvent onMatchmakingFailEvent = new OnMatchmakingFailEvent
				{
					Cause = MatchmakingFailCause.GroupMemberNotConfirmed,
					Message = "A player failed to confirm the match."
				};
				OnMatchmakingProgressEvent onMatchmakingProgressEvent = new OnMatchmakingProgressEvent
				{
					State = MatchmakingProgressState.NotConfirmed,
					MatchGroup = pending2.MatchGroup
				};
				foreach (QueuedPlayer group in pending2.Groups)
				{
					foreach (string partyMember in group.PartyMembers)
					{
						if (!AfkBotManager.IsBot(partyMember))
						{
							// В ОБА канала: игрок, который уже подтвердил, мог закрыть search-TCP,
							// и отправленный только туда fail до него не доходил — экран
							// подтверждения у него оставался висеть.
							bool pSearch = SendDirectSearchFlowEvent(partyMember, "onMatchmakingProgress", onMatchmakingProgressEvent);
							bool pPrimary = SendDirectPrimaryFlowEvent(partyMember, "onMatchmakingProgress", onMatchmakingProgressEvent);
							if (!pSearch && !pPrimary)
							{
								SendFlowEvent(partyMember, "onMatchmakingProgress", onMatchmakingProgressEvent);
							}
							bool fSearch = SendDirectSearchFlowEvent(partyMember, "onMatchmakingFail", onMatchmakingFailEvent);
							bool fPrimary = SendDirectPrimaryFlowEvent(partyMember, "onMatchmakingFail", onMatchmakingFailEvent);
							if (!fSearch && !fPrimary)
							{
								SendFlowEvent(partyMember, "onMatchmakingFail", onMatchmakingFailEvent);
							}
							Logger.Log($"[Matchmaking] timeout fail -> {partyMember} search={fSearch} primary={fPrimary}");
						}
					}
					_queue.TryRemove(group.PlayerId, out QueuedPlayer _);
				}
			}
			catch (TaskCanceledException)
			{
			}
			catch (Exception ex2)
			{
				Logger.Error("[Matchmaking] Error in confirmation timeout: " + ex2.Message);
			}
		});
	}

	private static async Task FinalizeMatch(PendingMatch pending)
	{
		PendingMatch pending2 = pending;
		try
		{
			List<string> list = (from id in (from id in pending2.Groups.SelectMany((QueuedPlayer g) => g.PartyMembers)
					where !string.IsNullOrWhiteSpace(id)
					select id).ToList()
				where !AfkBotManager.IsBot(id)
				select id).Distinct().ToList();
			List<string> list2 = list.Where((string h) => !pending2.ConfirmedPlayers.Contains(h)).ToList();
			if (list.Count > 0 && list2.Count > 0)
			{
				Logger.LogWarn("[Matchmaking] Finalize blocked " + pending2.MatchId + ": missing human confirms " + string.Join(",", list2));
				NotifyFinalizeFailed(pending2.Groups, new OnMatchmakingFailEvent
				{
					Cause = MatchmakingFailCause.GroupMemberNotConfirmed,
					Message = "Not all players confirmed."
				});
				return;
			}
			string matchId = pending2.MatchId;
			int gameMode = pending2.GameMode;
			string region = pending2.Region;
			string roomId = pending2.RoomId;
			List<QueuedPlayer> groups = pending2.Groups;
			string chosenMap = (string.IsNullOrWhiteSpace(pending2.ChosenMap) ? ChooseMapForMode(gameMode, region, matchId) : pending2.ChosenMap);
			string value = ToPhotonSceneMap(gameMode, chosenMap);
			// История матчей берёт отсюда реальную карту, если плагин её не донесёт.
			MatchHistoryBuilder.RememberMatchMap(matchId, roomId, chosenMap);
			string text = gameMode switch
			{
				0 => "DeathMatch", 
				1 => "Defuse", 
				2 => "ArmsRace", 
				3 => "Training", 
				4 => "SniperDuel", 
				5 => "Ranked2v2", 
				6 => "RankedDefuse", 
				7 => "Escalation", 
				8 => "Ranked2v2", 
				9 => "SniperDuel", 
				11 => "ClanRankedDefuse", 
				30 => "Arcade", 
				_ => "DeathMatch", 
			};
			int requiredPlayers = AlliesMatchmakingConfig.GetRequiredPlayers();
			int num = gameMode switch
			{
				6 => CompetitiveMatchmakingConfig.GetRequiredPlayers(), 
				11 => 5, 
				8 => requiredPlayers, 
				5 => requiredPlayers, 
				1 => 5, 
				_ => 10, 
			};
			string value2 = gameMode switch
			{
				6 => "RankedDefuse", 
				11 => "RankedDefuse", 
				8 => "Ranked2v2", 
				5 => "Ranked2v2", 
				_ => text, 
			};
			int num2 = gameMode switch
			{
				6 => 10,
				11 => 10,
				8 => 8,
				5 => 8,
				_ => 0,
			};
			GameServer gameServer = new GameServer
			{
				Ip = MainServerConfig.PublicIp,
				Port = 5056
			};
			string matchGameVersion = GetMatchGameVersion(groups);
			BoltLobby boltLobby = new BoltLobby(matchId, groups[0].PlayerId, text + " Match", BoltLobby.LobbyType.Private, joinable: false, num, 0, new ConcurrentDictionary<string, string>(), new List<BoltFriend>(), new List<BoltFriend>(), new List<BoltFriend>(), new List<BoltFriend>(), gameServer, new PhotonGame
			{
				region = region.ToLower(),
				roomId = roomId,
				appVersion = matchGameVersion,
				customProperties = new Dictionary<string, string>
				{
					{ "C0", value2 },
					{ "C1", value },
					{ "game_mode", text }
				}
			});
			if (num2 > 0)
			{
				boltLobby.PhotonGame.customProperties["round_count"] = num2.ToString();
				boltLobby.PhotonGame.customProperties["max_rounds"] = num2.ToString();
				boltLobby.PhotonGame.customProperties["win_rounds"] = num2.ToString();
			}
			boltLobby.PhotonGame.customProperties["max_players"] = num.ToString();
			if (num == 2 || num == 4)
			{
				boltLobby.PhotonGame.customProperties["team_size"] = (num / 2).ToString();
			}
			List<string> list3 = (from id in groups.SelectMany((QueuedPlayer g) => g.PartyMembers)
				where !string.IsNullOrWhiteSpace(id)
				select id).ToList();
			int num3 = AfkBotManager.CountBotsAmong(list3);
			if (num3 > 0)
			{
				List<string> values = list3.Where(AfkBotManager.IsBot).ToList();
				boltLobby.PhotonGame.customProperties["afk_bots"] = num3.ToString();
				boltLobby.PhotonGame.customProperties["afk_bot_ids"] = string.Join(",", values);
				RegisterRoomRankedProps(roomId, num, Math.Max(1, num / 2), num3, num2);
				Logger.Log($"[Matchmaking] Match {matchId} has {num3} AFK bots → room props set");
			}
			else
			{
				RegisterRoomRankedProps(roomId, num, Math.Max(1, num / 2), 0, num2);
			}
			foreach (QueuedPlayer item in groups)
			{
				foreach (string partyMember in item.PartyMembers)
				{
					try
					{
						PlayerDocument playerDocument = BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(partyMember));
						if (playerDocument != null)
						{
							boltLobby.AddLobbyMember(playerDocument.GetBoltFriend());
						}
					}
					catch
					{
					}
				}
			}
			StaticClasses.Lobbies[matchId] = boltLobby;
			PhotonGame photonGame = new PhotonGame
			{
				region = region.ToLower(),
				roomId = roomId,
				appVersion = matchGameVersion,
				customProperties = new Dictionary<string, string>
				{
					{ "C0", value2 },
					{ "C1", value },
					{ "game_mode", text },
					{
						"max_players",
						num.ToString()
					}
				}
			};
			if (num2 > 0)
			{
				photonGame.customProperties["round_count"] = num2.ToString();
				photonGame.customProperties["max_rounds"] = num2.ToString();
				photonGame.customProperties["win_rounds"] = num2.ToString();
			}
			if (num == 2 || num == 4)
			{
				photonGame.customProperties["team_size"] = (num / 2).ToString();
			}
			PromoteFlowForPartyMembers(groups.SelectMany((QueuedPlayer g) => g.PartyMembers));
			List<string> list4 = new List<string>();
			List<string> list5 = new List<string>();
			foreach (QueuedPlayer item2 in groups)
			{
				foreach (string partyMember2 in item2.PartyMembers)
				{
					if (!AfkBotManager.IsBot(partyMember2))
					{
						if (StaticClasses.HasLiveFlowChannel(partyMember2) || ResolveLiveUserService(partyMember2) != null)
						{
							list4.Add(partyMember2);
						}
						else
						{
							list5.Add(partyMember2);
						}
					}
				}
			}
			if (groups.SelectMany((QueuedPlayer g) => g.PartyMembers).Count((string id) => !AfkBotManager.IsBot(id)) > 0 && list4.Count == 0)
			{
				Logger.LogWarn($"[Matchmaking] Finalize aborted match={matchId}: no live humans (missing={string.Join(",", list5)})");
				NotifyFinalizeFailed(groups, new OnMatchmakingFailEvent
				{
					Cause = MatchmakingFailCause.GroupMemberNotConfirmed,
					Message = "Not all players are connected."
				});
				return;
			}
			if (list5.Count > 0)
			{
				Logger.LogWarn($"[Matchmaking] Finalize match={matchId}: {list5.Count} humans without TCP ({string.Join(",", list5)}), starting for {list4.Count} live");
			}

			// Хост комнаты = первый в Groups: ему Done+onGameStarted раньше остальных,
			// чтобы CreateRoom успел до Join у остальных. Иначе при «второй принял первым»
			// оба почти одновременно CreateRoom и один клиент не входит в матч.
			string roomHostId = null;
			foreach (QueuedPlayer g in groups)
			{
				foreach (string pid in g.PartyMembers)
				{
					if (!AfkBotManager.IsBot(pid))
					{
						roomHostId = pid;
						break;
					}
				}
				if (roomHostId != null) break;
			}

			List<string> startOrder = new List<string>();
			if (!string.IsNullOrEmpty(roomHostId))
				startOrder.Add(roomHostId);
			foreach (QueuedPlayer item3 in groups)
			{
				foreach (string partyMember3 in item3.PartyMembers)
				{
					if (AfkBotManager.IsBot(partyMember3)) continue;
					if (string.Equals(partyMember3, roomHostId, StringComparison.OrdinalIgnoreCase)) continue;
					startOrder.Add(partyMember3);
				}
			}

			// Payload комнаты собран ОДИН раз выше (photonGame) и одинаков для всех:
			// расхождения между игроками тут быть не может по построению.
			int staggerMs = LocalServerConfig.Current.MatchStartStaggerMs;
			if (staggerMs < 0) staggerMs = 0;
			// Гостю нужно, чтобы хост успел CreateRoom. Меньше ~800мс Join проскакивает
			// мимо комнаты — второй жмёт «Подключиться» вхолостую, пока не заспамит.
			if (startOrder.Count >= 2 && staggerMs < 900) staggerMs = 900;
			if (staggerMs > 1200) staggerMs = 1200;
			int startRetries = LocalServerConfig.Current.MatchStartRetryCount;
			if (startRetries < 0) startRetries = 0;
			if (startRetries > 5) startRetries = 5;
			var startSent = new List<string>();
			var startFailed = new List<string>();
			Logger.Log($"[Confirm] match_starting: match_id={matchId}, order=[{string.Join(",", startOrder)}], host={roomHostId}, stagger={staggerMs}ms, retries={startRetries}");

			foreach (string creditPid in startOrder)
			{
				try { StandRiseServer.RpcServer.Security.InventoryDupeProtection.GrantPostMatchDropCredits(creditPid); }
				catch { }
			}

			// Один TCP на игрока: flow → primary, затем Done и onGameStarted на нём же.
			foreach (string bindPid in startOrder)
				BindMatchmakingFlowAsPrimary(bindPid);

			// ФАЗА 1 — Done ВСЕМ сразу на PRIMARY (закрыть confirm до клиентского таймера).
			foreach (string doneTarget in startOrder)
			{
				try { SendMatchFoundDoneForStart(pending2, doneTarget); }
				catch (Exception exDone) { Logger.LogWarn($"[Confirm] done_send_failed: player={doneTarget}, err={exDone.Message}"); }
			}
			Logger.Log($"[Confirm] match_done_broadcast: match_id={matchId}, players=[{string.Join(",", startOrder)}] — окно подтверждения закрыто у всех");

			// ФАЗА 2 — onGameStarted: хост CreateRoom, гости Join; stagger между КАЖДЫМ.
			for (int startIdx = 0; startIdx < startOrder.Count; startIdx++)
			{
				string partyMember3 = startOrder[startIdx];
				PlayerStatus orAdd = StaticClasses.PlayersStatus.GetOrAdd(partyMember3, (string _) => new PlayerStatus());
				orAdd.onlineStatus = PlayerStatus.OnlineStatus.StateBusy;
				orAdd.playInGame = new PlayInGame
				{
					gameCode = "standoff2",
					gameVersion = StaticClasses.DefaultGameVersion,
					lobbyId = matchId,
					lobbyName = text,
					photonGame = photonGame
				};
				try
				{
					BoltMainDatabaseProvider.Instance.SetPlayerStatus(ObjectId.Parse(partyMember3), orAdd);
				}
				catch
				{
				}
				try
				{
					Axlebolt.Bolt.Protobuf.PhotonGame byDocument = PhotonGame.GetByDocument(photonGame);
					// Запоминаем старт до отправки: если onGameStarted не долетит, повторный
					// confirm от клиента дошлёт его сам, вместо отмены матча.
					RememberStartedMatch(partyMember3, matchId, BuildMatchFoundDoneEvent(pending2), byDocument);
					bool sent = TrySendStartDirect(partyMember3, "onGameStarted", byDocument);

					for (int attempt = 1; !sent && attempt <= startRetries; attempt++)
					{
						try { await Task.Delay(250).ConfigureAwait(false); } catch { }
						BindMatchmakingFlowAsPrimary(partyMember3);
						sent = TrySendStartDirect(partyMember3, "onGameStarted", byDocument);
						Logger.Log($"[Confirm] match_starting_retry: match_id={matchId}, player={partyMember3}, attempt={attempt}, sent={sent}");
					}

					if (!sent)
					{
						SendFlowEvent(partyMember3, "onGameStarted", byDocument);
						Logger.LogWarn($"[Confirm] match_starting_queued: match_id={matchId}, player={partyMember3} — primary miss, старт в очередь");
					}

					if (sent) startSent.Add(partyMember3); else startFailed.Add(partyMember3);
					Logger.Log($"[Confirm] match_starting_sent: match_id={matchId}, player={partyMember3}, sent={sent}, host={(string.Equals(partyMember3, roomHostId, StringComparison.OrdinalIgnoreCase) ? "yes" : "no")}, ord={startIdx}, room={roomId}, map={value}");

					// Гостю второй onGameStarted через 400мс — Join после CreateRoom хоста.
					if (sent && startIdx > 0)
					{
						try { await Task.Delay(400).ConfigureAwait(false); } catch { }
						bool resent = TrySendStartDirect(partyMember3, "onGameStarted", byDocument);
						Logger.Log($"[Confirm] match_starting_guest_resend: match_id={matchId}, player={partyMember3}, sent={resent}");
					}
				}
				catch (Exception ex)
				{
					startFailed.Add(partyMember3);
					Logger.Error($"[Confirm] match_starting_error: match_id={matchId}, player={partyMember3}, err={ex.Message}");
				}
				if (startIdx < startOrder.Count - 1 && staggerMs > 0)
				{
					try { await Task.Delay(staggerMs).ConfigureAwait(false); }
					catch { }
				}
			}
			foreach (string item4 in list4)
			{
				StaticClasses.ClearMatchmakingFlowChannel(item4);
			}
			try { pending2.TimeoutToken?.Cancel(); } catch { }
			_pendingMatches.TryRemove(matchId, out PendingMatch _);
			Logger.Log($"[Confirm] match_starting_done: match_id={matchId}, sent=[{string.Join(",", startSent)}], failed=[{string.Join(",", startFailed)}], bots={num3}, room={roomId}, host={roomHostId}");
			Logger.Log($"[Matchmaking] Match {matchId} finalized successfully! humans={list4.Count} bots={num3} room={roomId} host={roomHostId}");
		}
		catch (Exception ex2)
		{
			Logger.Error("[Matchmaking] Error finalizing match: " + ex2.Message);
			try
			{
				pending2.FinalizingStarted = false;
				NotifyFinalizeFailed(pending2.Groups, new OnMatchmakingFailEvent
				{
					Cause = MatchmakingFailCause.GroupMemberNotConfirmed,
					Message = "Failed to start match."
				});
				try { pending2.TimeoutToken?.Cancel(); } catch { }
				_pendingMatches.TryRemove(pending2.MatchId, out PendingMatch _);
			}
			catch { }
		}
	}

	/// <summary>
	/// Матч уже сыгран: убираем photonGame/lobby, иначе главное меню рисует
	/// «Переподключиться / Вернуться в игру».
	/// </summary>
	public static void ClearFinishedMatchStatus(IEnumerable<string> playerIds, string roomId = null, string matchId = null)
	{
		var ids = (playerIds ?? Array.Empty<string>())
			.Where(s => !string.IsNullOrWhiteSpace(s))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		try
		{
			foreach (var kv in StaticClasses.Lobbies.ToArray())
			{
				bool matchLobby = false;
				if (!string.IsNullOrWhiteSpace(matchId) && string.Equals(kv.Key, matchId, StringComparison.OrdinalIgnoreCase))
					matchLobby = true;
				if (!string.IsNullOrWhiteSpace(roomId)
					&& (string.Equals(kv.Key, roomId, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(kv.Value?.PhotonGame?.roomId, roomId, StringComparison.OrdinalIgnoreCase)))
					matchLobby = true;
				string lobbyName = kv.Value?.Name ?? "";
				if (!matchLobby && ids.Count > 0 && ids.Any(id => kv.Value != null && kv.Value.IsLobbyAny(id)))
				{
					if (lobbyName.IndexOf("Match", StringComparison.OrdinalIgnoreCase) >= 0
						|| lobbyName.IndexOf("RankedDefuse", StringComparison.OrdinalIgnoreCase) >= 0
						|| lobbyName.IndexOf("Ranked2v2", StringComparison.OrdinalIgnoreCase) >= 0)
						matchLobby = true;
				}
				if (matchLobby)
					StaticClasses.Lobbies.TryRemove(kv.Key, out _);
			}
		}
		catch (Exception ex)
		{
			Logger.LogWarn("[Matchmaking] ClearFinishedMatchStatus lobby sweep failed: " + ex.Message);
		}

		foreach (string playerId in ids)
		{
			if (AfkBotManager.IsBot(playerId)) continue;
			try
			{
				PlayerStatus orAdd = StaticClasses.PlayersStatus.GetOrAdd(playerId, (string _) => new PlayerStatus());
				orAdd.onlineStatus = PlayerStatus.OnlineStatus.StateOnline;
				if (orAdd.playInGame == null)
				{
					orAdd.playInGame = new PlayInGame
					{
						gameCode = "standoff2",
						gameVersion = StaticClasses.GetPlayerGameVersion(playerId),
						lobbyId = string.Empty,
						lobbyName = string.Empty,
						photonGame = null
					};
				}
				else
				{
					orAdd.playInGame.photonGame = null;
					orAdd.playInGame.lobbyId = string.Empty;
					orAdd.playInGame.lobbyName = string.Empty;
					orAdd.playInGame.gameCode = "standoff2";
				}
				try { BoltMainDatabaseProvider.Instance.SetPlayerStatus(ObjectId.Parse(playerId), orAdd); } catch { }
				SendSelfStatus(playerId, orAdd);
			}
			catch (Exception ex)
			{
				Logger.LogWarn("[Matchmaking] ClearFinishedMatchStatus failed for " + playerId + ": " + ex.Message);
			}
		}
	}
}
