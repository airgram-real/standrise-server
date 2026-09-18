using System;
using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class Clan : IMessage<Clan>, IMessage, IEquatable<Clan>, IDeepCloneable<Clan>
{
	private static readonly MessageParser<Clan> _parser = new MessageParser<Clan>(() => new Clan());

	public const int IdFieldNumber = 1;

	private string id_ = "";

	public const int NameFieldNumber = 2;

	private string name_ = "";

	public const int TagFieldNumber = 3;

	private string tag_ = "";

	public const int ClanTypeFieldNumber = 4;

	private ClanType clanType_ = ClanType.Closed;

	public const int AvatarIdFieldNumber = 5;

	private string avatarId_ = "";

	public const int CreateDateFieldNumber = 6;

	private long createDate_;

	public const int MebersCountFieldNumber = 7;

	private int mebersCount_;

	public const int MaxMemberFieldNumber = 8;

	private int maxMemberCount_;

	public const int DescriptionFieldNumber = 9;

	private string description_ = "";

	[DebuggerNonUserCode]
	public static MessageParser<Clan> Parser => _parser;

	[DebuggerNonUserCode]
	public static MessageDescriptor Descriptor => ClanMessageReflection.Descriptor.MessageTypes[0];

	[DebuggerNonUserCode]
	MessageDescriptor IMessage.Descriptor => Descriptor;

	[DebuggerNonUserCode]
	public string Id
	{
		get
		{
			return id_;
		}
		set
		{
			id_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	[DebuggerNonUserCode]
	public string Name
	{
		get
		{
			return name_;
		}
		set
		{
			name_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	[DebuggerNonUserCode]
	public string Tag
	{
		get
		{
			return tag_;
		}
		set
		{
			tag_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	[DebuggerNonUserCode]
	public ClanType ClanType
	{
		get
		{
			return clanType_;
		}
		set
		{
			clanType_ = value;
		}
	}

	[DebuggerNonUserCode]
	public string AvatarId
	{
		get
		{
			return avatarId_;
		}
		set
		{
			avatarId_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	[DebuggerNonUserCode]
	public long CreateDate
	{
		get
		{
			return createDate_;
		}
		set
		{
			createDate_ = value;
		}
	}

	[DebuggerNonUserCode]
	public int MebersCount
	{
		get
		{
			return mebersCount_;
		}
		set
		{
			mebersCount_ = value;
		}
	}

	[DebuggerNonUserCode]
	public int MaxMemberCount
	{
		get
		{
			return maxMemberCount_;
		}
		set
		{
			maxMemberCount_ = value;
		}
	}

	[DebuggerNonUserCode]
	public string Description
	{
		get
		{
			return description_;
		}
		set
		{
			description_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	[DebuggerNonUserCode]
	public Clan()
	{
	}

	[DebuggerNonUserCode]
	public Clan(Clan other)
		: this()
	{
		id_ = other.id_;
		name_ = other.name_;
		tag_ = other.tag_;
		clanType_ = other.clanType_;
		avatarId_ = other.avatarId_;
		createDate_ = other.createDate_;
		mebersCount_ = other.mebersCount_;
		maxMemberCount_ = other.maxMemberCount_;
		description_ = other.description_;
	}

	[DebuggerNonUserCode]
	public Clan Clone()
	{
		return new Clan(this);
	}

	[DebuggerNonUserCode]
	public override bool Equals(object other)
	{
		return Equals(other as Clan);
	}

	[DebuggerNonUserCode]
	public bool Equals(Clan other)
	{
		if (other == null)
		{
			return false;
		}
		if (other == this)
		{
			return true;
		}
		if (Id != other.Id)
		{
			return false;
		}
		if (Name != other.Name)
		{
			return false;
		}
		if (Tag != other.Tag)
		{
			return false;
		}
		if (ClanType != other.ClanType)
		{
			return false;
		}
		if (AvatarId != other.AvatarId)
		{
			return false;
		}
		if (CreateDate != other.CreateDate)
		{
			return false;
		}
		if (MebersCount != other.MebersCount)
		{
			return false;
		}
		if (MaxMemberCount != other.MaxMemberCount)
		{
			return false;
		}
		if (Description != other.Description)
		{
			return false;
		}
		return true;
	}

	[DebuggerNonUserCode]
	public override int GetHashCode()
	{
		int num = 1;
		if (Id.Length != 0)
		{
			num ^= Id.GetHashCode();
		}
		if (Name.Length != 0)
		{
			num ^= Name.GetHashCode();
		}
		if (Tag.Length != 0)
		{
			num ^= Tag.GetHashCode();
		}
		if (ClanType != ClanType.Closed)
		{
			num ^= ClanType.GetHashCode();
		}
		if (AvatarId.Length != 0)
		{
			num ^= AvatarId.GetHashCode();
		}
		if (CreateDate != 0)
		{
			num ^= CreateDate.GetHashCode();
		}
		if (MebersCount != 0)
		{
			num ^= MebersCount.GetHashCode();
		}
		if (MaxMemberCount != 0)
		{
			num ^= MaxMemberCount.GetHashCode();
		}
		if (Description.Length != 0)
		{
			num ^= Description.GetHashCode();
		}
		return num;
	}

	[DebuggerNonUserCode]
	public override string ToString()
	{
		return JsonFormatter.ToDiagnosticString(this);
	}

	[DebuggerNonUserCode]
	public void WriteTo(CodedOutputStream output)
	{
		if (Id.Length != 0)
		{
			output.WriteRawTag(10);
			output.WriteString(Id);
		}
		if (Name.Length != 0)
		{
			output.WriteRawTag(18);
			output.WriteString(Name);
		}
		if (Tag.Length != 0)
		{
			output.WriteRawTag(26);
			output.WriteString(Tag);
		}
		if (ClanType != ClanType.Closed)
		{
			output.WriteRawTag(32);
			output.WriteEnum((int)ClanType);
		}
		if (AvatarId.Length != 0)
		{
			output.WriteRawTag(42);
			output.WriteString(AvatarId);
		}
		if (CreateDate != 0)
		{
			output.WriteRawTag(48);
			output.WriteInt64(CreateDate);
		}
		if (MebersCount != 0)
		{
			output.WriteRawTag(56);
			output.WriteInt32(MebersCount);
		}
		if (MaxMemberCount != 0)
		{
			output.WriteRawTag(64);
			output.WriteInt32(MaxMemberCount);
		}
		if (Description.Length != 0)
		{
			output.WriteRawTag(74);
			output.WriteString(Description);
		}
	}

	[DebuggerNonUserCode]
	public int CalculateSize()
	{
		int num = 0;
		if (Id.Length != 0)
		{
			num += 1 + CodedOutputStream.ComputeStringSize(Id);
		}
		if (Name.Length != 0)
		{
			num += 1 + CodedOutputStream.ComputeStringSize(Name);
		}
		if (Tag.Length != 0)
		{
			num += 1 + CodedOutputStream.ComputeStringSize(Tag);
		}
		if (ClanType != ClanType.Closed)
		{
			num += 1 + CodedOutputStream.ComputeEnumSize((int)ClanType);
		}
		if (AvatarId.Length != 0)
		{
			num += 1 + CodedOutputStream.ComputeStringSize(AvatarId);
		}
		if (CreateDate != 0)
		{
			num += 1 + CodedOutputStream.ComputeInt64Size(CreateDate);
		}
		if (MebersCount != 0)
		{
			num += 1 + CodedOutputStream.ComputeInt32Size(MebersCount);
		}
		if (MaxMemberCount != 0)
		{
			num += 1 + CodedOutputStream.ComputeInt32Size(MaxMemberCount);
		}
		if (Description.Length != 0)
		{
			num += 1 + CodedOutputStream.ComputeStringSize(Description);
		}
		return num;
	}

	[DebuggerNonUserCode]
	public void MergeFrom(Clan other)
	{
		if (other != null)
		{
			if (other.Id.Length != 0)
			{
				Id = other.Id;
			}
			if (other.Name.Length != 0)
			{
				Name = other.Name;
			}
			if (other.Tag.Length != 0)
			{
				Tag = other.Tag;
			}
			if (other.ClanType != ClanType.Closed)
			{
				ClanType = other.ClanType;
			}
			if (other.AvatarId.Length != 0)
			{
				AvatarId = other.AvatarId;
			}
			if (other.CreateDate != 0)
			{
				CreateDate = other.CreateDate;
			}
			if (other.Description.Length != 0)
			{
				Description = other.Description;
			}
			if (other.MebersCount != 0)
			{
				MebersCount = other.MebersCount;
			}
			if (other.MaxMemberCount != 0)
			{
				MaxMemberCount = other.MaxMemberCount;
			}
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
			default:
				input.SkipLastField();
				break;
			case 10u:
				Id = input.ReadString();
				break;
			case 18u:
				Name = input.ReadString();
				break;
			case 26u:
				Tag = input.ReadString();
				break;
			case 32u:
				clanType_ = (ClanType)input.ReadEnum();
				break;
			case 42u:
				AvatarId = input.ReadString();
				break;
			case 48u:
				CreateDate = input.ReadInt64();
				break;
			case 56u:
				MebersCount = input.ReadInt32();
				break;
			case 64u:
				MaxMemberCount = input.ReadInt32();
				break;
			case 74u:
				Description = input.ReadString();
				break;
			}
		}
	}
}
