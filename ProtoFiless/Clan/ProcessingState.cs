using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public enum ProcessingState
{
	[OriginalName("CREATING")]
	Creating,
	[OriginalName("CANCELLING")]
	Cancelling
}
