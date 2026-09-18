using System;
using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class UnmountInventoryItemRequest : IMessage<UnmountInventoryItemRequest>, IMessage, IEquatable<UnmountInventoryItemRequest>, IDeepCloneable<UnmountInventoryItemRequest>
{
	private static readonly MessageParser<UnmountInventoryItemRequest> _parser = new MessageParser<UnmountInventoryItemRequest>(() => new UnmountInventoryItemRequest());

	private int modifiedItemId_;
	private string modificationName_ = "";

	[DebuggerNonUserCode]
	public static MessageParser<UnmountInventoryItemRequest> Parser => _parser;

	[DebuggerNonUserCode]
	public static MessageDescriptor Descriptor => null;

	[DebuggerNonUserCode]
	MessageDescriptor IMessage.Descriptor => Descriptor;

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
	public UnmountInventoryItemRequest() { }

	[DebuggerNonUserCode]
	public UnmountInventoryItemRequest(UnmountInventoryItemRequest other) : this()
	{
		modifiedItemId_ = other.modifiedItemId_;
		modificationName_ = other.modificationName_;
	}

	[DebuggerNonUserCode]
	public UnmountInventoryItemRequest Clone() => new UnmountInventoryItemRequest(this);

	[DebuggerNonUserCode]
	public override bool Equals(object other) => Equals(other as UnmountInventoryItemRequest);

	[DebuggerNonUserCode]
	public bool Equals(UnmountInventoryItemRequest other)
	{
		if (other == null) return false;
		if (other == this) return true;
		if (ModifiedItemId != other.ModifiedItemId) return false;
		if (ModificationName != other.ModificationName) return false;
		return true;
	}

	[DebuggerNonUserCode]
	public override int GetHashCode()
	{
		int num = 1;
		if (ModifiedItemId != 0) num ^= ModifiedItemId.GetHashCode();
		if (ModificationName.Length != 0) num ^= ModificationName.GetHashCode();
		return num;
	}

	[DebuggerNonUserCode]
	public override string ToString() => JsonFormatter.ToDiagnosticString(this);

	[DebuggerNonUserCode]
	public void WriteTo(CodedOutputStream output)
	{
		if (ModifiedItemId != 0) { output.WriteRawTag(8); output.WriteInt32(ModifiedItemId); }
		if (ModificationName.Length != 0) { output.WriteRawTag(18); output.WriteString(ModificationName); }
	}

	[DebuggerNonUserCode]
	public int CalculateSize()
	{
		int num = 0;
		if (ModifiedItemId != 0) num += 1 + CodedOutputStream.ComputeInt32Size(ModifiedItemId);
		if (ModificationName.Length != 0) num += 1 + CodedOutputStream.ComputeStringSize(ModificationName);
		return num;
	}

	[DebuggerNonUserCode]
	public void MergeFrom(UnmountInventoryItemRequest other)
	{
		if (other == null) return;
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
				case 8: ModifiedItemId = input.ReadInt32(); break;
				case 18: ModificationName = input.ReadString(); break;
				default: input.SkipLastField(); break;
			}
		}
	}
}
