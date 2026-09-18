using System;
using System.Diagnostics;
using Axlebolt.Bolt.Protobuf;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

[DebuggerNonUserCode]
public sealed class GetPlayerCouponsResponse : IMessage<GetPlayerCouponsResponse>, IMessage, IEquatable<GetPlayerCouponsResponse>, IDeepCloneable<GetPlayerCouponsResponse>
{
	private static readonly MessageParser<GetPlayerCouponsResponse> _parser = new MessageParser<GetPlayerCouponsResponse>(() => new GetPlayerCouponsResponse());

	public const int CouponFieldNumber = 1;

	private static readonly FieldCodec<Coupon> _repeated_coupon_codec = FieldCodec.ForMessage(10u, Coupon.Parser);

	private readonly RepeatedField<Coupon> coupon_ = new RepeatedField<Coupon>();

	[DebuggerNonUserCode]
	public static MessageParser<GetPlayerCouponsResponse> Parser => _parser;

	[DebuggerNonUserCode]
	public static MessageDescriptor Descriptor => InventoryMessageReflection.Descriptor.MessageTypes[11];

	[DebuggerNonUserCode]
	MessageDescriptor IMessage.Descriptor => Descriptor;

	[DebuggerNonUserCode]
	public RepeatedField<Coupon> Coupons => coupon_;

	[DebuggerNonUserCode]
	public GetPlayerCouponsResponse()
	{
	}

	[DebuggerNonUserCode]
	public GetPlayerCouponsResponse(GetPlayerCouponsResponse other)
		: this()
	{
		coupon_ = other.coupon_.Clone();
	}

	[DebuggerNonUserCode]
	public GetPlayerCouponsResponse Clone()
	{
		return new GetPlayerCouponsResponse(this);
	}

	[DebuggerNonUserCode]
	public override bool Equals(object other)
	{
		return Equals(other as GetPlayerCouponsResponse);
	}

	[DebuggerNonUserCode]
	public bool Equals(GetPlayerCouponsResponse other)
	{
		if (other == null)
		{
			return false;
		}
		if (other == this)
		{
			return true;
		}
		return coupon_.Equals(other.coupon_);
	}

	[DebuggerNonUserCode]
	public override int GetHashCode()
	{
		int hash = 1;
		return hash ^ coupon_.GetHashCode();
	}

	[DebuggerNonUserCode]
	public override string ToString()
	{
		return JsonFormatter.ToDiagnosticString(this);
	}

	[DebuggerNonUserCode]
	public void WriteTo(CodedOutputStream output)
	{
		coupon_.WriteTo(output, _repeated_coupon_codec);
	}

	[DebuggerNonUserCode]
	public int CalculateSize()
	{
		int size = 0;
		return size + coupon_.CalculateSize(_repeated_coupon_codec);
	}

	[DebuggerNonUserCode]
	public void MergeFrom(GetPlayerCouponsResponse other)
	{
		if (other != null)
		{
			coupon_.Add(other.coupon_);
		}
	}

	[DebuggerNonUserCode]
	public void MergeFrom(CodedInputStream input)
	{
		uint tag;
		while ((tag = input.ReadTag()) != 0)
		{
			if (tag == 10)
			{
				coupon_.AddEntriesFrom(input, _repeated_coupon_codec);
			}
			else
			{
				input.SkipLastField();
			}
		}
	}
}
