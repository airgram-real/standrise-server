using System;
using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class MountInventoryItemRequest : IMessage<MountInventoryItemRequest>, IMessage, IEquatable<MountInventoryItemRequest>, IDeepCloneable<MountInventoryItemRequest>
{
	private static readonly MessageParser<MountInventoryItemRequest> _parser = new MessageParser<MountInventoryItemRequest>(() => new MountInventoryItemRequest());

	private int consumedItemId_;
	private int modifiedItemId_;
	private string modificationName_ = "";

	[DebuggerNonUserCode]
	public static MessageParser<MountInventoryItemRequest> Parser => _parser;

	[DebuggerNonUserCode]
	public static MessageDescriptor Descriptor => null;

	[DebuggerNonUserCode]
	MessageDescriptor IMessage.Descriptor => Descriptor;

	[DebuggerNonUserCode]
	public int ConsumedItemId
	{
		get => consumedItemId_;
		set => consumedItemId_ = value;
	}

	[DebuggerNonUserCode]
	public int ModifiedItemId
	{
		get => modifiedItemId_;
		set => modifiedItemId_ = value;
	}

	[DebuggerNonUserCode]
	public string ModificationName
	{
		get => modificationName_;
		set => modificationName_ = ProtoPreconditions.CheckNotNull(value, "value");
	}

	[DebuggerNonUserCode]
	public MountInventoryItemRequest() { }

	[DebuggerNonUserCode]
	public MountInventoryItemRequest(MountInventoryItemRequest other) : this()
	{
		consumedItemId_ = other.consumedItemId_;
		modifiedItemId_ = other.modifiedItemId_;
		modificationName_ = other.modificationName_;
	}

	[DebuggerNonUserCode]
	public MountInventoryItemRequest Clone() => new MountInventoryItemRequest(this);

	[DebuggerNonUserCode]
	public override bool Equals(object other) => Equals(other as MountInventoryItemRequest);

	[DebuggerNonUserCode]
	public bool Equals(MountInventoryItemRequest other)
	{
		if (other == null) return false;
		if (other == this) return true;
		if (ConsumedItemId != other.ConsumedItemId) return false;
		if (ModifiedItemId != other.ModifiedItemId) return false;
		if (ModificationName != other.ModificationName) return false;
		return true;
	}

	[DebuggerNonUserCode]
	public override int GetHashCode()
	{
		int num = 1;
		if (ConsumedItemId != 0) num ^= ConsumedItemId.GetHashCode();
		if (ModifiedItemId != 0) num ^= ModifiedItemId.GetHashCode();
		if (ModificationName.Length != 0) num ^= ModificationName.GetHashCode();
		return num;
	}

	[DebuggerNonUserCode]
	public override string ToString() => JsonFormatter.ToDiagnosticString(this);

	[DebuggerNonUserCode]
	public void WriteTo(CodedOutputStream output)
	{
		if (ConsumedItemId != 0) { output.WriteRawTag(8); output.WriteInt32(ConsumedItemId); }
		if (ModifiedItemId != 0) { output.WriteRawTag(16); output.WriteInt32(ModifiedItemId); }
		if (ModificationName.Length != 0) { output.WriteRawTag(26); output.WriteString(ModificationName); }
	}

	[DebuggerNonUserCode]
	public int CalculateSize()
	{
		int num = 0;
		if (ConsumedItemId != 0) num += 1 + CodedOutputStream.ComputeInt32Size(ConsumedItemId);
		if (ModifiedItemId != 0) num += 1 + CodedOutputStream.ComputeInt32Size(ModifiedItemId);
		if (ModificationName.Length != 0) num += 1 + CodedOutputStream.ComputeStringSize(ModificationName);
		return num;
	}

	[DebuggerNonUserCode]
	public void MergeFrom(MountInventoryItemRequest other)
	{
		if (other == null) return;
		if (other.ConsumedItemId != 0) ConsumedItemId = other.ConsumedItemId;
		if (other.ModifiedItemId != 0) ModifiedItemId = other.ModifiedItemId;
		if (other.ModificationName.Length != 0) ModificationName = other.ModificationName;
	}

	[DebuggerNonUserCode]
	public void MergeFrom(CodedInputStream input)
	{
		uint num;
		while ((num = input.ReadTag()) != 0)
		{
			switch (num)
			{
				case 8: ConsumedItemId = input.ReadInt32(); break;
				case 16: ModifiedItemId = input.ReadInt32(); break;
				case 26: ModificationName = input.ReadString(); break;
				default: input.SkipLastField(); break;
			}
		}
	}
}
