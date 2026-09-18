using System;
using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class UnmountInventoryItemResponse : IMessage<UnmountInventoryItemResponse>, IMessage, IEquatable<UnmountInventoryItemResponse>, IDeepCloneable<UnmountInventoryItemResponse>
{
	private static readonly MessageParser<UnmountInventoryItemResponse> _parser = new MessageParser<UnmountInventoryItemResponse>(() => new UnmountInventoryItemResponse());

	private PlayerInventoryItem unmountedItem_;

	[DebuggerNonUserCode]
	public static MessageParser<UnmountInventoryItemResponse> Parser => _parser;

	[DebuggerNonUserCode]
	public static MessageDescriptor Descriptor => null;

	[DebuggerNonUserCode]
	MessageDescriptor IMessage.Descriptor => Descriptor;

	[DebuggerNonUserCode]
	public PlayerInventoryItem UnmountedItem
	{
		get => unmountedItem_;
		set => unmountedItem_ = value;
	}

	[DebuggerNonUserCode]
	public UnmountInventoryItemResponse() { }

	[DebuggerNonUserCode]
	public UnmountInventoryItemResponse(UnmountInventoryItemResponse other) : this()
	{
		unmountedItem_ = other.unmountedItem_ != null ? other.unmountedItem_.Clone() : null;
	}

	[DebuggerNonUserCode]
	public UnmountInventoryItemResponse Clone() => new UnmountInventoryItemResponse(this);

	[DebuggerNonUserCode]
	public override bool Equals(object other) => Equals(other as UnmountInventoryItemResponse);

	[DebuggerNonUserCode]
	public bool Equals(UnmountInventoryItemResponse other)
	{
		if (other == null) return false;
		if (other == this) return true;
		return true;
	}

	[DebuggerNonUserCode]
	public override int GetHashCode() => 1;

	[DebuggerNonUserCode]
	public override string ToString() => JsonFormatter.ToDiagnosticString(this);

	[DebuggerNonUserCode]
	public void WriteTo(CodedOutputStream output)
	{
		if (unmountedItem_ != null) { output.WriteRawTag(10); output.WriteMessage(unmountedItem_); }
	}

	[DebuggerNonUserCode]
	public int CalculateSize()
	{
		int num = 0;
		if (unmountedItem_ != null) num += 1 + CodedOutputStream.ComputeMessageSize(unmountedItem_);
		return num;
	}

	[DebuggerNonUserCode]
	public void MergeFrom(UnmountInventoryItemResponse other)
	{
		if (other == null) return;
		if (other.unmountedItem_ != null)
		{
			if (unmountedItem_ == null) unmountedItem_ = new PlayerInventoryItem();
			UnmountedItem.MergeFrom(other.UnmountedItem);
		}
	}

	[DebuggerNonUserCode]
	public void MergeFrom(CodedInputStream input)
	{
		uint num;
		while ((num = input.ReadTag()) != 0)
		{
			switch (num)
			{
				case 10:
					if (unmountedItem_ == null) unmountedItem_ = new PlayerInventoryItem();
					input.ReadMessage(unmountedItem_);
					break;
				default: input.SkipLastField(); break;
			}
		}
	}
}
