using pb = global::Google.Protobuf;
using pbr = global::Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf
{
	/// <summary>
	/// Wire-compatible with client MFHJEEBMDBJ (ExchangeResult field 3 / FFJHLDEICFB).
	/// InventoryItem в field 3 ломает парсинг → SpinnerReleaseFailed → «ошибка запроса».
	/// </summary>
	public sealed class ExchangeSpentEntry : pb::IMessage<ExchangeSpentEntry>
	{
		private static readonly pb::MessageParser<ExchangeSpentEntry> _parser =
			new pb::MessageParser<ExchangeSpentEntry>(() => new ExchangeSpentEntry());

		public static pb::MessageParser<ExchangeSpentEntry> Parser => _parser;

		public static pbr::MessageDescriptor Descriptor => null;

		pbr::MessageDescriptor pb::IMessage.Descriptor => null;

		private string name_ = "";
		private int propertyType_;
		private int intValue_;
		private float floatValue_;
		private long longValue_;

		/// <summary>Field 1 — slot id (string).</summary>
		public string Name
		{
			get => name_;
			set => name_ = value ?? "";
		}

		/// <summary>Field 2 — PropertyType; ItemId = 5 for spin tokens.</summary>
		public int PropertyType
		{
			get => propertyType_;
			set => propertyType_ = value;
		}

		/// <summary>Field 3 — itemDefinitionId (201/219).</summary>
		public int IntValue
		{
			get => intValue_;
			set => intValue_ = value;
		}

		public float FloatValue
		{
			get => floatValue_;
			set => floatValue_ = value;
		}

		public long LongValue
		{
			get => longValue_;
			set => longValue_ = value;
		}

		public ExchangeSpentEntry Clone() => new ExchangeSpentEntry
		{
			Name = name_,
			PropertyType = propertyType_,
			IntValue = intValue_,
			FloatValue = floatValue_,
			LongValue = longValue_
		};

		public override bool Equals(object other) => Equals(other as ExchangeSpentEntry);

		public bool Equals(ExchangeSpentEntry other) =>
			other != null
			&& name_ == other.name_
			&& propertyType_ == other.propertyType_
			&& intValue_ == other.intValue_
			&& floatValue_.Equals(other.floatValue_)
			&& longValue_ == other.longValue_;

		public override int GetHashCode()
		{
			int hash = 1;
			if (name_ != null) hash ^= name_.GetHashCode();
			hash ^= propertyType_.GetHashCode();
			hash ^= intValue_.GetHashCode();
			hash ^= floatValue_.GetHashCode();
			hash ^= longValue_.GetHashCode();
			return hash;
		}

		public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

		public void WriteTo(pb::CodedOutputStream output)
		{
			if (!string.IsNullOrEmpty(name_))
			{
				output.WriteRawTag(10);
				output.WriteString(name_);
			}
			if (propertyType_ != 0)
			{
				output.WriteRawTag(16);
				output.WriteInt32(propertyType_);
			}
			if (intValue_ != 0)
			{
				output.WriteRawTag(24);
				output.WriteInt32(intValue_);
			}
			if (floatValue_ != 0f)
			{
				output.WriteRawTag(37);
				output.WriteFloat(floatValue_);
			}
			if (longValue_ != 0L)
			{
				output.WriteRawTag(40);
				output.WriteInt64(longValue_);
			}
		}

		public int CalculateSize()
		{
			int size = 0;
			if (!string.IsNullOrEmpty(name_))
				size += 1 + pb::CodedOutputStream.ComputeStringSize(name_);
			if (propertyType_ != 0)
				size += 1 + pb::CodedOutputStream.ComputeInt32Size(propertyType_);
			if (intValue_ != 0)
				size += 1 + pb::CodedOutputStream.ComputeInt32Size(intValue_);
			if (floatValue_ != 0f)
				size += 1 + 4;
			if (longValue_ != 0L)
				size += 1 + pb::CodedOutputStream.ComputeInt64Size(longValue_);
			return size;
		}

		public void MergeFrom(ExchangeSpentEntry other)
		{
			if (other == null) return;
			if (!string.IsNullOrEmpty(other.name_)) Name = other.name_;
			if (other.propertyType_ != 0) PropertyType = other.propertyType_;
			if (other.intValue_ != 0) IntValue = other.intValue_;
			if (other.floatValue_ != 0f) FloatValue = other.floatValue_;
			if (other.longValue_ != 0L) LongValue = other.longValue_;
		}

		public void MergeFrom(pb::CodedInputStream input)
		{
			uint tag;
			while ((tag = input.ReadTag()) != 0)
			{
				switch (tag)
				{
					case 10: Name = input.ReadString(); break;
					case 16: PropertyType = input.ReadInt32(); break;
					case 24: IntValue = input.ReadInt32(); break;
					case 37: FloatValue = input.ReadFloat(); break;
					case 40: LongValue = input.ReadInt64(); break;
					default: input.SkipLastField(); break;
				}
			}
		}
	}
}
