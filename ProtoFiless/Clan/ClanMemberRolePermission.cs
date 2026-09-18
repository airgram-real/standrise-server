using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public enum ClanMemberRolePermission
{
	[OriginalName("CHANGE_CLAN_SETTINGS")]
	ChangeClanSettings,
	[OriginalName("ACCEPT_MEMBER")]
	AcceptMember,
	[OriginalName("INVITE_MEMBER")]
	InviteMember,
	[OriginalName("KICK_MEMBER_LESS")]
	KickMemberLess,
	[OriginalName("KICK_MEMBER_EQUAL")]
	KickMemberEqual,
	[OriginalName("ASSIGN_ROLE_LESS")]
	AssignRoleLess,
	[OriginalName("ASSIGN_ROLE_EQUAL")]
	AssignRoleEqual,
	[OriginalName("CREATE_CLAN_BATTLE")]
	CreateClanBattle,
	[OriginalName("JOIN_CLAN_BATTLE")]
	JoinClanBattle,
	[OriginalName("UPGRADE_CLAN")]
	UpgradeClan
}
