using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public enum ClanClosingReason
{
	[OriginalName("ACCEPT_REQUEST")]
	AcceptRequest,
	[OriginalName("DECLINE_REQUEST")]
	DeclineRequest,
	[OriginalName("CANCEL_REQUEST")]
	CancelRequest
}
