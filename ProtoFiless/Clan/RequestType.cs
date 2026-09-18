using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public enum RequestType
{
	[OriginalName("NONE_TYPE")]
	NoneType,
	[OriginalName("JOIN_REQUEST")]
	JoinRequest,
	[OriginalName("INVITE_REQUEST")]
	InviteRequest
}
