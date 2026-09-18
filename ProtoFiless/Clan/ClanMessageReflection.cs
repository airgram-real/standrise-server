using System;
using Axlebolt.Bolt.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public static class ClanMessageReflection
{
	private static FileDescriptor descriptor;

	public static FileDescriptor Descriptor => descriptor;

	static ClanMessageReflection()
	{
		byte[] descriptorData = Convert.FromBase64String("ChJjbGFuX21lc3NhZ2UucHJvdG8SEWNvbS5heGxlYm9sdC5ib2x0GhRwbGF5ZXJfbWVzc2FnZS5wcm90bxoWY3VycmVuY3lfbWVzc2FnZS5wcm90byKVAQoEQ2xhbhIKCgJpZBgBIAEoCRIMCgRuYW1lGAIgASgJEgsKA3RhZxgDIAEoCRItCghjbGFuVHlwZRgEIAEoDjIbLmNvbS5heGxlYm9sdC5ib2x0LkNsYW5UeXBlEhAKCGF2YXRhcklkGAUgASgJEhIKCmNyZWF0ZURhdGUYBiABKAMSEQoJY2xhbkxldmVsGAcgASgFIpgBCgpDbGFuTWVtYmVyEgoKAmlkGAEgASgJEikKBnBsYXllchgCIAEoCzIZLmNvbS5heGxlYm9sdC5ib2x0LlBsYXllchIOCgZjbGFuSWQYAyABKAkSLwoEcm9sZRgEIAEoCzIhLmNvbS5heGxlYm9sdC5ib2x0LkNsYW5NZW1iZXJSb2xlEhIKCmNyZWF0ZURhdGUYBSABKAMiRgoPQ2xhbk9wZW5SZXF1ZXN0EjMKC2NsYW5SZXF1ZXN0GAEgASgLMh4uY29tLmF4bGVib2x0LmJvbHQuQ2xhblJlcXVlc3QimAEKEUNsYW5DbG9zZWRSZXF1ZXN0EjMKC2NsYW5SZXF1ZXN0GAEgASgLMh4uY29tLmF4bGVib2x0LmJvbHQuQ2xhblJlcXVlc3QSEQoJY2xvc2VEYXRlGAIgASgDEjsKDWNsb3NpbmdSZWFzb24YAyABKA4yJC5jb20uYXhsZWJvbHQuYm9sdC5DbGFuQ2xvc2luZ1JlYXNvbiLtAQoLQ2xhblJlcXVlc3QSCgoCaWQYASABKAkSJQoEY2xhbhgCIAEoCzIXLmNvbS5heGxlYm9sdC5ib2x0LkNsYW4SMAoNcmVxdWVzdFNlbmRlchgDIAEoCzIZLmNvbS5heGxlYm9sdC5ib2x0LlBsYXllchISCgpjcmVhdGVEYXRlGAQgASgDEjMKC3JlcXVlc3RUeXBlGAUgASgOMh4uY29tLmF4bGVib2x0LmJvbHQuUmVxdWVzdFR5cGUSMAoNaW52aXRlZFBsYXllchgGIAEoCzIZLmNvbS5heGxlYm9sdC5ib2x0LlBsYXllciKRAQoOQ2xhbk1lbWJlclJvbGUSCgoCaWQYASABKAkSDAoEbmFtZRgCIAEoCRINCgVsZXZlbBgDIAEoBRIUCgxkZXNjcmlwcHRpb24YBCABKAkSQAoLcGVybWlzc2lvbnMYBSADKA4yKy5jb20uYXhsZWJvbHQuYm9sdC5DbGFuTWVtYmVyUm9sZVBlcm1pc3Npb24i5gEKCUNsYW5MZXZlbBITCgtsZXZlbE51bWJlchgBIAEoBRIXCg9tYXhNZW1iZXJzQ291bnQYAiABKAUSNgoLbGV2ZWxVcENvc3QYAyABKAsyIS5jb20uYXhsZWJvbHQuYm9sdC5DdXJyZW5jeUFtb3VudBJACgpwcm9wZXJ0aWVzGAQgAygLMiwuY29tLmF4bGVib2x0LmJvbHQuQ2xhbkxldmVsLlByb3BlcnRpZXNFbnRyeRoxCg9Qcm9wZXJ0aWVzRW50cnkSCwoDa2V5GAEgASgJEg0KBXZhbHVlGAIgASgJOgI4ASJHCg9DbGFuVXNlck1lc3NhZ2USEAoIc2VuZGVySWQYASABKAkSDwoHbWVzc2FnZRgCIAEoCRIRCgl0aW1lc3RhbXAYAyABKAMiVwoXT25Kb2luUmVxdWVzdFRha2VuRXZlbnQSEQoJcmVxdWVzdElkGAEgASgJEikKBnBsYXllchgCIAEoCzIZLmNvbS5heGxlYm9sdC5ib2x0LlBsYXllciI8ChNPbkpvaW5lZFRvQ2xhbkV2ZW50EiUKBGNsYW4YASABKAsyFy5jb20uYXhsZWJvbHQuYm9sdC5DbGFuIk4KGU9uTWVtYmVySm9pbmVkVG9DbGFuRXZlbnQSMQoKY2xhbk1lbWJlchgBIAEoCzIdLmNvbS5heGxlYm9sdC5ib2x0LkNsYW5NZW1iZXIiRQoTT25LaWNrZWRNZW1iZXJFdmVudBIWCg5raWNrZXJNZW1iZXJJZBgBIAEoCRIWCg5raWNrZWRNZW1iZXJJZBgCIAEoCSJ7ChRPbkludml0ZWRUb0NsYW5FdmVudBIRCglyZXF1ZXN0SWQYASABKAkSJQoEY2xhbhgCIAEoCzIXLmNvbS5heGxlYm9sdC5ib2x0LkNsYW4SKQoGcGxheWVyGAMgASgLMhkuY29tLmF4bGVib2x0LmJvbHQuUGxheWVyIoABChNPbkFzc2lnbmVkUm9sZUV2ZW50EjIKB25ld1JvbGUYASABKAsyIS5jb20uYXhsZWJvbHQuYm9sdC5DbGFuTWVtYmVyUm9sZRIaChJhc3NpZ25hdG9yTWVtYmVySWQYAiABKAkSGQoRYXNzaWduaW5nTWVtYmVySWQYAyABKAkiugEKEU9uUmVxdWVzdERlY2xpbmVkEhEKCXJlcXVlc3RJZBgBIAEoCRIzCgtyZXF1ZXN0VHlwZRgCIAEoDjIeLmNvbS5heGxlYm9sdC5ib2x0LlJlcXVlc3RUeXBlEisKCmNsYW5Ub0pvaW4YAyABKAsyFy5jb20uYXhsZWJvbHQuYm9sdC5DbGFuEjAKDWludml0ZWRQbGF5ZXIYBCABKAsyGS5jb20uYXhsZWJvbHQuYm9sdC5QbGF5ZXIiRQoRT25DbGFuVHlwZUNoYW5nZWQSMAoLbmV3Q2xhblR5cGUYASABKA4yGy5jb20uYXhsZWJvbHQuYm9sdC5DbGFuVHlwZSIkChFPbkNsYW5OYW1lQ2hhbmdlZBIPCgduZXdOYW1lGAEgASgJIiYKDU9uS2lja2VkRXZlbnQSFQoNa2lja2luZ1JlYXNvbhgBIAEoCSIiCg5PbkxlZnRGcm9tQ2xhbhIQCghtZW1iZXJJZBgBIAEoCSJMChVPbkluY29taW5nQ2xhbk1lc3NhZ2USMwoHbWVzc2FnZRgBIAEoCzIiLmNvbS5heGxlYm9sdC5ib2x0LkNsYW5Vc2VyTWVzc2FnZSJFChJPbkNsYW5VcGdyYWRlRXZlbnQSLwoJY2xhbkxldmVsGAEgASgLMhwuY29tLmF4bGVib2x0LmJvbHQuQ2xhbkxldmVsKjEKCENsYW5UeXBlEgoKBkNMT1NFRBAAEg8KC0lOVklURV9PTkxZEAESCAoET1BFThACKlAKEUNsYW5DbG9zaW5nUmVhc29uEhIKDkFDQ0VQVF9SRVFVRVNUEAASEwoPREVDTElORV9SRVFVRVNUEAESEgoOQ0FOQ0VMX1JFUVVFU1QQAipCCgtSZXF1ZXN0VHlwZRINCglOT05FX1RZUEUQABIQCgxKT0lOX1JFUVVFU1QQARISCg5JTlZJVEVfUkVRVUVTVBACKvQBChhDbGFuTWVtYmVyUm9sZVBlcm1pc3Npb24SGAoUQ0hBTkdFX0NMQU5fU0VUVElOR1MQABIRCg1BQ0NFUFRfTUVNQkVSEAESEQoNSU5WSVRFX01FTUJFUhACEhQKEEtJQ0tfTUVNQkVSX0xFU1MQAxIVChFLSUNLX01FTUJFUl9FUVVBTBAEEhQKEEFTU0lHTl9ST0xFX0xFU1MQBRIVChFBU1NJR05fUk9MRV9FUVVBTBAGEhYKEkNSRUFURV9DTEFOX0JBVFRMRRAHEhQKEEpPSU5fQ0xBTl9CQVRUTEUQCBIQCgxVUEdSQURFX0NMQU4QCUI1Chpjb20uYXhsZWJvbHQuYm9sdC5wcm90b2J1ZqoCFkF4bGVib2x0LkJvbHQuUHJvdG9idWZiBnByb3RvMw==");
		descriptor = FileDescriptor.FromGeneratedCode(descriptorData, new FileDescriptor[2]
		{
			PlayerMessageReflection.Descriptor,
			CurrencyMessageReflection.Descriptor
		}, new GeneratedClrTypeInfo(new Type[4]
		{
			typeof(ClanType),
			typeof(ClanClosingReason),
			typeof(RequestType),
			typeof(ClanMemberRolePermission)
		}, new GeneratedClrTypeInfo[21]
		{
			new GeneratedClrTypeInfo(typeof(Clan), Clan.Parser, new string[7] { "Id", "Name", "Tag", "ClanType", "AvatarId", "CreateDate", "ClanLevel" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(ClanMember), ClanMember.Parser, new string[5] { "Id", "Player", "ClanId", "Role", "CreateDate" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(ClanOpenRequest), ClanOpenRequest.Parser, new string[1] { "ClanRequest" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(ClanClosedRequest), ClanClosedRequest.Parser, new string[3] { "ClanRequest", "CloseDate", "ClosingReason" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(ClanRequest), ClanRequest.Parser, new string[6] { "Id", "Clan", "RequestSender", "CreateDate", "RequestType", "InvitedPlayer" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(ClanMemberRole), ClanMemberRole.Parser, new string[5] { "Id", "Name", "Level", "Descripption", "Permissions" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(ClanLevel), ClanLevel.Parser, new string[4] { "LevelNumber", "MaxMembersCount", "LevelUpCost", "Properties" }, null, null, new GeneratedClrTypeInfo[1]),
			new GeneratedClrTypeInfo(typeof(ClanUserMessage), ClanUserMessage.Parser, new string[3] { "SenderId", "Message", "Timestamp" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnJoinRequestTakenEvent), OnJoinRequestTakenEvent.Parser, new string[2] { "RequestId", "Player" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnJoinedToClanEvent), OnJoinedToClanEvent.Parser, new string[1] { "Clan" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnMemberJoinedToClanEvent), OnMemberJoinedToClanEvent.Parser, new string[1] { "ClanMember" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnKickedMemberEvent), OnKickedMemberEvent.Parser, new string[2] { "KickerMemberId", "KickedMemberId" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnInvitedToClanEvent), OnInvitedToClanEvent.Parser, new string[3] { "RequestId", "Clan", "Player" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnAssignedRoleEvent), OnAssignedRoleEvent.Parser, new string[3] { "NewRole", "AssignatorMemberId", "AssigningMemberId" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnRequestDeclined), OnRequestDeclined.Parser, new string[4] { "RequestId", "RequestType", "ClanToJoin", "InvitedPlayer" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnClanTypeChanged), OnClanTypeChanged.Parser, new string[1] { "NewClanType" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnClanNameChanged), OnClanNameChanged.Parser, new string[1] { "NewName" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnKickedEvent), OnKickedEvent.Parser, new string[1] { "KickingReason" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnLeftFromClan), OnLeftFromClan.Parser, new string[1] { "MemberId" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnIncomingClanMessage), OnIncomingClanMessage.Parser, new string[1] { "Message" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(OnClanUpgradeEvent), OnClanUpgradeEvent.Parser, new string[1] { "ClanLevel" }, null, null, null)
		}));
	}
}
