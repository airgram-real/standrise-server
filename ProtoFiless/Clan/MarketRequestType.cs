using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public enum MarketRequestType
{
	[OriginalName("NONE_TYPE")]
	NoneType,
	[OriginalName("SALE_REQUEST")]
	SaleRequest,
	[OriginalName("PURCHASE_REQUEST")]
	PurchaseRequest
}
