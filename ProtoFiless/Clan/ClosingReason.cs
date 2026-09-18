using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public enum ClosingReason
{
	[OriginalName("NONE_REASON")]
	NoneReason,
	[OriginalName("SUCCESS_TRANSACTION")]
	SuccessTransaction,
	[OriginalName("NOT_ENOUGH_FUNDS")]
	NotEnoughFunds,
	[OriginalName("CANCELLED")]
	Cancelled,
	[OriginalName("SALE_REQUEST_NOT_FOUND")]
	SaleRequestNotFound
}
