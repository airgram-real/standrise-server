using System;
using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class MountInventoryItemResponse : IMessage<MountInventoryItemResponse>, IMessage, IEquatable<MountInventoryItemResponse>, IDeepCloneable<MountInventoryItemResponse>
{
	private static readonly MessageParser<MountInventoryItemResponse> _parser = new MessageParser<MountInventoryItemResponse>(() => new MountInventoryItemResponse());

	private PlayerInventoryItem modifiedItem_;
	private PlayerInventoryItem unmountedItem_;

	[DebuggerNonUserCode]
	public static MessageParser<MountInventoryItemResponse> Parser => _parser;

	[DebuggerNonUserCode]
	public static MessageDescriptor Descriptor => null;

	[DebuggerNonUserCode]
	MessageDescriptor IMessage.Descriptor => Descriptor;

	[DebuggerNonUserCode]
	public PlayerInventoryItem ModifiedItem
	{
		get => modifiedItem_;
		set => modifiedItem_ = value;
	}

	[DebuggerNonUserCode]
	public PlayerInventoryItem UnmountedItem
	{
		get => unmountedItem_;
		set => unmountedItem_ = value;
	}

	[DebuggerNonUserCode]
	public MountInventoryItemResponse() { }

	[DebuggerNonUserCode]
	public MountInventoryItemResponse(MountInventoryItemResponse other) : this()
	{
		modifiedItem_ = other.modifiedItem_ != null ? other.modifiedItem_.Clone() : null;
		unmountedItem_ = other.unmountedItem_ != null ? other.unmountedItem_.Clone() : null;
	}

	[DebuggerNonUserCode]
	public MountInventoryItemResponse Clone() => new MountInventoryItemResponse(this);

	[DebuggerNonUserCode]
	public override bool Equals(object other) => Equals(other as MountInventoryItemResponse);

	[DebuggerNonUserCode]
	public bool Equals(MountInventoryItemResponse other)
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
		if (modifiedItem_ != null) { output.WriteRawTag(10); output.WriteMessage(modifiedItem_); }
		if (unmountedItem_ != null) { output.WriteRawTag(18); output.WriteMessage(unmountedItem_); }
	}

	[DebuggerNonUserCode]
	public int CalculateSize()
	{
		int num = 0;
		if (modifiedItem_ != null) num += 1 + CodedOutputStream.ComputeMessageSize(modifiedItem_);
		if (unmountedItem_ != null) num += 1 + CodedOutputStream.ComputeMessageSize(unmountedItem_);
		return num;
	}

	[DebuggerNonUserCode]
	public void MergeFrom(MountInventoryItemResponse other)
	{
		if (other == null) return;
		if (other.modifiedItem_ != null)
		{
			if (modifiedItem_ == null) modifiedItem_ = new PlayerInventoryItem();
			ModifiedItem.MergeFrom(other.ModifiedItem);
		}
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
					if (modifiedItem_ == null) modifiedItem_ = new PlayerInventoryItem();
					input.ReadMessage(modifiedItem_);
					break;
				case 18:
					if (unmountedItem_ == null) unmountedItem_ = new PlayerInventoryItem();
					input.ReadMessage(unmountedItem_);
					break;
				default: input.SkipLastField(); break;
			}
		}
	}
}
