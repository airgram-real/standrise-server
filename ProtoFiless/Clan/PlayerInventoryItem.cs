using System;
using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class PlayerInventoryItem : IMessage<PlayerInventoryItem>, IMessage, IEquatable<PlayerInventoryItem>, IDeepCloneable<PlayerInventoryItem>
{
	private static readonly MessageParser<PlayerInventoryItem> _parser = new MessageParser<PlayerInventoryItem>(() => new PlayerInventoryItem());

	// Map codec for field 6: map<string, InventoryItemProperty>
	// Key tag: field 1, wire type 2 (LEN) = (1 << 3) | 2 = 10
	// Value tag: field 2, wire type 2 (LEN) = (2 << 3) | 2 = 18
	// Outer tag: field 6, wire type 2 (LEN) = (6 << 3) | 2 = 50
	private static readonly MapField<string, InventoryItemProperty>.Codec _map_properties_codec
		= new MapField<string, InventoryItemProperty>.Codec(FieldCodec.ForString(10), FieldCodec.ForMessage(18, InventoryItemProperty.Parser), 50);

	private int id_;
	private int itemDefinitionId_;
	private int quantity_;
	private int flags_;
	private long date_;
	private readonly MapField<string, InventoryItemProperty> properties_ = new MapField<string, InventoryItemProperty>();

	[DebuggerNonUserCode]
	public static MessageParser<PlayerInventoryItem> Parser => _parser;

	[DebuggerNonUserCode]
	public static MessageDescriptor Descriptor => null;

	[DebuggerNonUserCode]
	MessageDescriptor IMessage.Descriptor => Descriptor;

	[DebuggerNonUserCode]
	public int Id
	{
		get => id_;
		set => id_ = value;
	}

	[DebuggerNonUserCode]
	public int ItemDefinitionId
	{
		get => itemDefinitionId_;
		set => itemDefinitionId_ = value;
	}

	[DebuggerNonUserCode]
	public int Quantity
	{
		get => quantity_;
		set => quantity_ = value;
	}

	[DebuggerNonUserCode]
	public int Flags
	{
		get => flags_;
		set => flags_ = value;
	}

	[DebuggerNonUserCode]
	public long Date
	{
		get => date_;
		set => date_ = value;
	}

	[DebuggerNonUserCode]
	public MapField<string, InventoryItemProperty> Properties => properties_;

	[DebuggerNonUserCode]
	public PlayerInventoryItem() { }

	[DebuggerNonUserCode]
	public PlayerInventoryItem(PlayerInventoryItem other) : this()
	{
		id_ = other.id_;
		itemDefinitionId_ = other.itemDefinitionId_;
		quantity_ = other.quantity_;
		flags_ = other.flags_;
		date_ = other.date_;
		properties_.Add(other.properties_);
	}

	[DebuggerNonUserCode]
	public PlayerInventoryItem Clone() => new PlayerInventoryItem(this);

	[DebuggerNonUserCode]
	public override bool Equals(object other) => Equals(other as PlayerInventoryItem);

	[DebuggerNonUserCode]
	public bool Equals(PlayerInventoryItem other)
	{
		if (other == null) return false;
		if (other == this) return true;
		if (Id != other.Id) return false;
		if (ItemDefinitionId != other.ItemDefinitionId) return false;
		if (Quantity != other.Quantity) return false;
		if (Flags != other.Flags) return false;
		if (Date != other.Date) return false;
		return true;
	}

	[DebuggerNonUserCode]
	public override int GetHashCode()
	{
		int num = 1;
		if (Id != 0) num ^= Id.GetHashCode();
		if (ItemDefinitionId != 0) num ^= ItemDefinitionId.GetHashCode();
		if (Quantity != 0) num ^= Quantity.GetHashCode();
		if (Flags != 0) num ^= Flags.GetHashCode();
		if (Date != 0L) num ^= Date.GetHashCode();
		return num;
	}

	[DebuggerNonUserCode]
	public override string ToString() => JsonFormatter.ToDiagnosticString(this);

	[DebuggerNonUserCode]
	public void WriteTo(CodedOutputStream output)
	{
		if (Id != 0) { output.WriteRawTag(8); output.WriteInt32(Id); }
		if (ItemDefinitionId != 0) { output.WriteRawTag(16); output.WriteInt32(ItemDefinitionId); }
		if (Quantity != 0) { output.WriteRawTag(24); output.WriteInt32(Quantity); }
		if (Flags != 0) { output.WriteRawTag(32); output.WriteInt32(Flags); }
		if (Date != 0L) { output.WriteRawTag(40); output.WriteInt64(Date); }
		properties_.WriteTo(output, _map_properties_codec);
	}

	[DebuggerNonUserCode]
	public int CalculateSize()
	{
		int num = 0;
		if (Id != 0) num += 1 + CodedOutputStream.ComputeInt32Size(Id);
		if (ItemDefinitionId != 0) num += 1 + CodedOutputStream.ComputeInt32Size(ItemDefinitionId);
		if (Quantity != 0) num += 1 + CodedOutputStream.ComputeInt32Size(Quantity);
		if (Flags != 0) num += 1 + CodedOutputStream.ComputeInt32Size(Flags);
		if (Date != 0L) num += 1 + CodedOutputStream.ComputeInt64Size(Date);
		num += properties_.CalculateSize(_map_properties_codec);
		return num;
	}

	[DebuggerNonUserCode]
	public void MergeFrom(PlayerInventoryItem other)
	{
		if (other == null) return;
		if (other.Id != 0) Id = other.Id;
		if (other.ItemDefinitionId != 0) ItemDefinitionId = other.ItemDefinitionId;
		if (other.Quantity != 0) Quantity = other.Quantity;
		if (other.Flags != 0) Flags = other.Flags;
		if (other.Date != 0L) Date = other.Date;
		properties_.Add(other.properties_);
	}

	[DebuggerNonUserCode]
	public void MergeFrom(CodedInputStream input)
	{
		uint num;
		while ((num = input.ReadTag()) != 0)
		{
			switch (num)
			{
				case 8: Id = input.ReadInt32(); break;
				case 16: ItemDefinitionId = input.ReadInt32(); break;
				case 24: Quantity = input.ReadInt32(); break;
				case 32: Flags = input.ReadInt32(); break;
				case 40: Date = input.ReadInt64(); break;
				case 50:
					properties_.AddEntriesFrom(input, _map_properties_codec);
					break;
				default: input.SkipLastField(); break;
			}
		}
	}
}
