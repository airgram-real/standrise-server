using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public enum ClanType
{
	[OriginalName("CLOSED")]
	Closed,
	[OriginalName("INVITE_ONLY")]
	InviteOnly,
	[OriginalName("OPEN")]
	Open
}
