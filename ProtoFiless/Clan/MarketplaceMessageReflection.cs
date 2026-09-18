using System;
using Axlebolt.Bolt.Protobuf;
using Axlebolt.Bolt.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public static class MarketplaceMessageReflection
{
	private static FileDescriptor descriptor;

	public static FileDescriptor Descriptor => descriptor;

	static MarketplaceMessageReflection()
	{
		byte[] descriptorData = Convert.FromBase64String("ChltYXJrZXRwbGFjZV9tZXNzYWdlLnByb3RvEhFjb20uYXhsZWJvbHQuYm9sdBoUcGxheWVyX21lc3NhZ2UucHJvdG8aF2ludmVudG9yeV9tZXNzYWdlLnByb3RvIukCCgtPcGVuUmVxdWVzdBIKCgJpZBgBIAEoCRIqCgdjcmVhdG9yGAIgASgLMhkuY29tLmF4bGVib2x0LmJvbHQuUGxheWVyEhgKEGl0ZW1EZWZpbml0aW9uSWQYAyABKAUSDQoFcHJpY2UYBCABKAISEgoKY3JlYXRlRGF0ZRgFIAEoAxIyCgR0eXBlGAYgASgOMiQuY29tLmF4bGVib2x0LmJvbHQuTWFya2V0UmVxdWVzdFR5cGUSEAoIcXVhbnRpdHkYByABKAUSQgoKcHJvcGVydGllcxgIIAMoCzIuLmNvbS5heGxlYm9sdC5ib2x0Lk9wZW5SZXF1ZXN0LlByb3BlcnRpZXNFbnRyeRpbCg9Qcm9wZXJ0aWVzRW50cnkSCwoDa2V5GAEgASgJEjcKBXZhbHVlGAIgASgLMiguY29tLmF4bGVib2x0LmJvbHQuSW52ZW50b3J5SXRlbVByb3BlcnR5OgI4ASKKBAoNQ2xvc2VkUmVxdWVzdBIKCgJpZBgBIAEoCRIQCghvcmlnaW5JZBgCIAEoCRIqCgdjcmVhdG9yGAMgASgLMhkuY29tLmF4bGVib2x0LmJvbHQuUGxheWVyEhgKEGl0ZW1EZWZpbml0aW9uSWQYBCABKAUSDQoFcHJpY2UYBSABKAISEgoKY3JlYXRlRGF0ZRgGIAEoAxIRCgljbG9zZURhdGUYByABKAMSMgoEdHlwZRgIIAEoDjIkLmNvbS5heGxlYm9sdC5ib2x0Lk1hcmtldFJlcXVlc3RUeXBlEioKB3BhcnRuZXIYCSABKAsyGS5jb20uYXhsZWJvbHQuYm9sdC5QbGF5ZXISGAoQcGFydG5lclJlcXVlc3RJZBgKIAEoCRIwCgZyZWFzb24YCyABKA4yIC5jb20uYXhsZWJvbHQuYm9sdC5DbG9zaW5nUmVhc29uEhAKCHF1YW50aXR5GAwgASgFEkQKCnByb3BlcnRpZXMYDSADKAsyMC5jb20uYXhsZWJvbHQuYm9sdC5DbG9zZWRSZXF1ZXN0LlByb3BlcnRpZXNFbnRyeRpbCg9Qcm9wZXJ0aWVzRW50cnkSCwoDa2V5GAEgASgJEjcKBXZhbHVlGAIgASgLMiguY29tLmF4bGVib2x0LmJvbHQuSW52ZW50b3J5SXRlbVByb3BlcnR5OgI4ASKTAwoRUHJvY2Vzc2luZ1JlcXVlc3QSCgoCaWQYASABKAkSGAoQaXRlbURlZmluaXRpb25JZBgCIAEoBRINCgVwcmljZRgDIAEoAhISCgpjcmVhdGVEYXRlGAQgASgDEjIKBHR5cGUYBSABKA4yJC5jb20uYXhsZWJvbHQuYm9sdC5NYXJrZXRSZXF1ZXN0VHlwZRIVCg1zYWxlUmVxdWVzdElkGAYgASgJEjEKBXN0YXRlGAcgASgOMiIuY29tLmF4bGVib2x0LmJvbHQuUHJvY2Vzc2luZ1N0YXRlEhAKCHF1YW50aXR5GAggASgFEkgKCnByb3BlcnRpZXMYCSADKAsyNC5jb20uYXhsZWJvbHQuYm9sdC5Qcm9jZXNzaW5nUmVxdWVzdC5Qcm9wZXJ0aWVzRW50cnkaWwoPUHJvcGVydGllc0VudHJ5EgsKA2tleRgBIAEoCRI3CgV2YWx1ZRgCIAEoCzIoLmNvbS5heGxlYm9sdC5ib2x0LkludmVudG9yeUl0ZW1Qcm9wZXJ0eToCOAEiawoFVHJhZGUSCgoCaWQYASABKAUSEgoKc2FsZXNDb3VudBgCIAEoBRIWCg5wdXJjaGFzZXNDb3VudBgDIAEoBRISCgpzYWxlc1ByaWNlGAQgASgCEhYKDnB1cmNoYXNlc1ByaWNlGAUgASgCIpkBChVHZXRDbG9zZWRSZXF1ZXN0c0FyZ3MSMgoEdHlwZRgBIAEoDjIkLmNvbS5heGxlYm9sdC5ib2x0Lk1hcmtldFJlcXVlc3RUeXBlEjAKBnJlYXNvbhgCIAEoDjIgLmNvbS5heGxlYm9sdC5ib2x0LkNsb3NpbmdSZWFzb24SDAoEcGFnZRgDIAEoBRIMCgRzaXplGAQgASgFIoIBChpHZXRDbG9zZWRSZXF1ZXN0c0NvdW50QXJncxIyCgR0eXBlGAEgASgOMiQuY29tLmF4bGVib2x0LmJvbHQuTWFya2V0UmVxdWVzdFR5cGUSMAoGcmVhc29uGAIgASgOMiAuY29tLmF4bGVib2x0LmJvbHQuQ2xvc2luZ1JlYXNvbiI2ChVDcmVhdGVTYWxlUmVxdWVzdEFyZ3MSDgoGaXRlbUlkGAEgASgFEg0KBXByaWNlGAIgASgCIlYKGUNyZWF0ZVB1cmNoYXNlUmVxdWVzdEFyZ3MSGAoQaXRlbURlZmluaXRpb25JZBgBIAEoBRINCgVwcmljZRgCIAEoAhIQCghxdWFudGl0eRgDIAEoBSIqChhDcmVhdGVQdXJjaGFzZUJ5U2FsZUFyZ3MSDgoGc2FsZUlkGAEgASgJIkYKHEdldFRyYWRlT3BlblNhbGVSZXF1ZXN0c0FyZ3MSCgoCaWQYASABKAUSDAoEcGFnZRgCIAEoBRIMCgRzaXplGAMgASgFIkoKIEdldFRyYWRlT3BlblB1cmNoYXNlUmVxdWVzdHNBcmdzEgoKAmlkGAEgASgFEgwKBHBhZ2UYAiABKAUSDAoEc2l6ZRgDIAEoBSIfChFDYW5jZWxSZXF1ZXN0QXJncxIKCgJpZBgBIAEoCSIaCgxHZXRUcmFkZUFyZ3MSCgoCaWQYASABKAUiKgoNR2V0VHJhZGVzQXJncxIZChFpdGVtRGVmaW5pdGlvbklkcxgBIAMoBSJsChNNYXJrZXRwbGFjZVNldHRpbmdzEhkKEWNvbW1pc3Npb25QZXJjZW50GAEgASgCEhUKDW1pbkNvbW1pc3Npb24YAiABKAISEgoKY3VycmVuY3lJZBgDIAEoBRIPCgdlbmFibGVkGAQgASgIIk0KGk9uUGxheWVyUmVxdWVzdE9wZW5lZEV2ZW50Ei8KB3JlcXVlc3QYASABKAsyHi5jb20uYXhsZWJvbHQuYm9sdC5PcGVuUmVxdWVzdCJ/ChpPblBsYXllclJlcXVlc3RDbG9zZWRFdmVudBIxCgdyZXF1ZXN0GAEgASgLMiAuY29tLmF4bGVib2x0LmJvbHQuQ2xvc2VkUmVxdWVzdBIuCgRpdGVtGAIgASgLMiAuY29tLmF4bGVib2x0LmJvbHQuSW52ZW50b3J5SXRlbSJMChlPblRyYWRlUmVxdWVzdE9wZW5lZEV2ZW50Ei8KB3JlcXVlc3QYASABKAsyHi5jb20uYXhsZWJvbHQuYm9sdC5PcGVuUmVxdWVzdCJOChlPblRyYWRlUmVxdWVzdENsb3NlZEV2ZW50EjEKB3JlcXVlc3QYASABKAsyIC5jb20uYXhsZWJvbHQuYm9sdC5DbG9zZWRSZXF1ZXN0Ij4KE09uVHJhZGVVcGRhdGVkRXZlbnQSJwoFdHJhZGUYASABKAsyGC5jb20uYXhsZWJvbHQuYm9sdC5UcmFkZSpKChFNYXJrZXRSZXF1ZXN0VHlwZRINCglOT05FX1RZUEUQABIQCgxTQUxFX1JFUVVFU1QQARIUChBQVVJDSEFTRV9SRVFVRVNUEAIqLwoPUHJvY2Vzc2luZ1N0YXRlEgwKCENSRUFUSU5HEAASDgoKQ0FOQ0VMTElORxABKnoKDUNsb3NpbmdSZWFzb24SDwoLTk9ORV9SRUFTT04QABIXChNTVUNDRVNTX1RSQU5TQUNUSU9OEAESFAoQTk9UX0VOT1VHSF9GVU5EUxACEg0KCUNBTkNFTExFRBADEhoKFlNBTEVfUkVRVUVTVF9OT1RfRk9VTkQQBEI1Chpjb20uYXhsZWJvbHQuYm9sdC5wcm90b2J1ZqoCFkF4bGVib2x0LkJvbHQuUHJvdG9idWZiBnByb3RvMw==");
		descriptor = FileDescriptor.FromGeneratedCode(descriptorData, new FileDescriptor[2]
		{
			PlayerMessageReflection.Descriptor,
			InventoryMessageReflection.Descriptor
		}, new GeneratedClrTypeInfo(new Type[3]
		{
			typeof(MarketRequestType),
			typeof(ProcessingState),
			typeof(ClosingReason)
		}, new GeneratedClrTypeInfo[13]
		{
			new GeneratedClrTypeInfo(typeof(OpenRequest), OpenRequest.Parser, new string[8] { "Id", "Creator", "ItemDefinitionId", "Price", "CreateDate", "Type", "Quantity", "Properties" }, null, null, new GeneratedClrTypeInfo[1]),
			new GeneratedClrTypeInfo(typeof(ClosedRequest), ClosedRequest.Parser, new string[13]
			{
				"Id", "OriginId", "Creator", "ItemDefinitionId", "Price", "CreateDate", "CloseDate", "Type", "Partner", "PartnerRequestId",
				"Reason", "Quantity", "Properties"
			}, null, null, new GeneratedClrTypeInfo[1]),
			new GeneratedClrTypeInfo(typeof(ProcessingRequest), ProcessingRequest.Parser, new string[9] { "Id", "ItemDefinitionId", "Price", "CreateDate", "Type", "SaleRequestId", "State", "Quantity", "Properties" }, null, null, new GeneratedClrTypeInfo[1]),
			new GeneratedClrTypeInfo(typeof(Trade), Trade.Parser, new string[5] { "Id", "SalesCount", "PurchasesCount", "SalesPrice", "PurchasesPrice" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(GetClosedRequestsArgs), GetClosedRequestsArgs.Parser, new string[4] { "Type", "Reason", "Page", "Size" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(GetClosedRequestsCountArgs), GetClosedRequestsCountArgs.Parser, new string[2] { "Type", "Reason" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(CreateSaleRequestArgs), CreateSaleRequestArgs.Parser, new string[2] { "ItemId", "Price" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(CreatePurchaseRequestArgs), CreatePurchaseRequestArgs.Parser, new string[3] { "ItemDefinitionId", "Price", "Quantity" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(CreatePurchaseBySaleArgs), CreatePurchaseBySaleArgs.Parser, new string[1] { "SaleId" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(GetTradeOpenSaleRequestsArgs), GetTradeOpenSaleRequestsArgs.Parser, new string[3] { "Id", "Page", "Size" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(GetTradeOpenPurchaseRequestsArgs), GetTradeOpenPurchaseRequestsArgs.Parser, new string[3] { "Id", "Page", "Size" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(CancelRequestArgs), CancelRequestArgs.Parser, new string[1] { "Id" }, null, null, null),
			new GeneratedClrTypeInfo(typeof(GetTradeArgs), GetTradeArgs.Parser, new string[1] { "Id" }, null, null, null)
		}));
	}
}
