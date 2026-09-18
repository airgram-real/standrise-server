using System;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class ClanLogMessage : IMessage<ClanLogMessage>, IMessage, IEquatable<ClanLogMessage>, IDeepCloneable<ClanLogMessage>
{
	private static readonly MessageParser<ClanLogMessage> _parser = new MessageParser<ClanLogMessage>(() => new ClanLogMessage());

	private int clanLogType_;

	private int changedClanType_;

	private string changedClanName_ = "";

	private string changedClanTag_ = "";

	private string primaryMember_ = "";

	private string secondaryMember_ = "";

	private int changedMaxMemberCount_;

	private int assignedRole_;

	private long timestamp_;

	public static MessageParser<ClanLogMessage> Parser => _parser;

	public static MessageDescriptor Descriptor => ClanMessageReflection.Descriptor.MessageTypes[8];

	MessageDescriptor IMessage.Descriptor => Descriptor;

	public int ClanLogType
	{
		get
		{
			return clanLogType_;
		}
		set
		{
			clanLogType_ = value;
		}
	}

	public int ChangedClanType
	{
		get
		{
			return changedClanType_;
		}
		set
		{
			changedClanType_ = value;
		}
	}

	public string ChangedClanName
	{
		get
		{
			return changedClanName_;
		}
		set
		{
			changedClanName_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	public string ChangedClanTag
	{
		get
		{
			return changedClanTag_;
		}
		set
		{
			changedClanTag_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	public string PrimaryMember
	{
		get
		{
			return primaryMember_;
		}
		set
		{
			primaryMember_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	public string SecondaryMember
	{
		get
		{
			return secondaryMember_;
		}
		set
		{
			secondaryMember_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	public int ChangedMaxMemberCount
	{
		get
		{
			return changedMaxMemberCount_;
		}
		set
		{
			changedMaxMemberCount_ = value;
		}
	}

	public int AssignedRole
	{
		get
		{
			return assignedRole_;
		}
		set
		{
			assignedRole_ = value;
		}
	}

	public long Timestamp
	{
		get
		{
			return timestamp_;
		}
		set
		{
			timestamp_ = value;
		}
	}

	public ClanLogMessage()
	{
	}

	public ClanLogMessage(ClanLogMessage other)
	{
		clanLogType_ = other.clanLogType_;
		changedClanType_ = other.changedClanType_;
		changedClanName_ = other.changedClanName_;
		changedClanTag_ = other.changedClanTag_;
		primaryMember_ = other.primaryMember_;
		secondaryMember_ = other.secondaryMember_;
		changedMaxMemberCount_ = other.changedMaxMemberCount_;
		assignedRole_ = other.assignedRole_;
		timestamp_ = other.timestamp_;
	}

	public override bool Equals(object other)
	{
		return Equals(other as ClanLogMessage);
	}

	public bool Equals(ClanLogMessage other)
	{
		if (other == null)
		{
			return false;
		}
		if (this == other)
		{
			return true;
		}
		return clanLogType_ == other.clanLogType_ && changedClanType_ == other.changedClanType_ && string.Equals(ChangedClanName, other.ChangedClanName) && string.Equals(ChangedClanTag, other.ChangedClanTag) && string.Equals(PrimaryMember, other.PrimaryMember) && string.Equals(SecondaryMember, other.SecondaryMember) && changedMaxMemberCount_ == other.changedMaxMemberCount_ && assignedRole_ == other.assignedRole_ && timestamp_ == other.timestamp_;
	}

	public override int GetHashCode()
	{
		int num = 0;
		if (clanLogType_ != 0)
		{
			num ^= clanLogType_.GetHashCode();
		}
		if (changedClanType_ != 0)
		{
			num ^= changedClanType_.GetHashCode();
		}
		if (changedClanName_.Length != 0)
		{
			num ^= changedClanName_.GetHashCode();
		}
		if (changedClanTag_.Length != 0)
		{
			num ^= changedClanTag_.GetHashCode();
		}
		if (primaryMember_.Length != 0)
		{
			num ^= primaryMember_.GetHashCode();
		}
		if (secondaryMember_.Length != 0)
		{
			num ^= secondaryMember_.GetHashCode();
		}
		if (changedMaxMemberCount_ != 0)
		{
			num ^= changedMaxMemberCount_.GetHashCode();
		}
		if (assignedRole_ != 0)
		{
			num ^= assignedRole_.GetHashCode();
		}
		if (timestamp_ != 0)
		{
			num ^= timestamp_.GetHashCode();
		}
		return num;
	}

	public override string ToString()
	{
		return JsonFormatter.ToDiagnosticString(this);
	}

	public void WriteTo(CodedOutputStream output)
	{
		if (clanLogType_ != 0)
		{
			output.WriteRawTag(8);
			output.WriteInt32(clanLogType_);
		}
		if (changedClanType_ != 0)
		{
			output.WriteRawTag(16);
			output.WriteInt32(changedClanType_);
		}
		if (changedClanName_.Length != 0)
		{
			output.WriteRawTag(26);
			output.WriteString(changedClanName_);
		}
		if (changedClanTag_.Length != 0)
		{
			output.WriteRawTag(34);
			output.WriteString(changedClanTag_);
		}
		if (primaryMember_.Length != 0)
		{
			output.WriteRawTag(42);
			output.WriteString(primaryMember_);
		}
		if (secondaryMember_.Length != 0)
		{
			output.WriteRawTag(50);
			output.WriteString(secondaryMember_);
		}
		if (changedMaxMemberCount_ != 0)
		{
			output.WriteRawTag(56);
			output.WriteInt32(changedMaxMemberCount_);
		}
		if (assignedRole_ != 0)
		{
			output.WriteRawTag(64);
			output.WriteInt32(assignedRole_);
		}
		if (timestamp_ != 0)
		{
			output.WriteRawTag(72);
			output.WriteInt64(timestamp_);
		}
	}

	public int CalculateSize()
	{
		int size = 0;
		if (clanLogType_ != 0)
		{
			size += CodedOutputStream.ComputeInt32Size(clanLogType_) + 1;
		}
		if (changedClanType_ != 0)
		{
			size += CodedOutputStream.ComputeInt32Size(changedClanType_) + 1;
		}
		if (changedClanName_.Length != 0)
		{
			size += CodedOutputStream.ComputeStringSize(changedClanName_) + 1;
		}
		if (changedClanTag_.Length != 0)
		{
			size += CodedOutputStream.ComputeStringSize(changedClanTag_) + 1;
		}
		if (primaryMember_.Length != 0)
		{
			size += CodedOutputStream.ComputeStringSize(primaryMember_) + 1;
		}
		if (secondaryMember_.Length != 0)
		{
			size += CodedOutputStream.ComputeStringSize(secondaryMember_) + 1;
		}
		if (changedMaxMemberCount_ != 0)
		{
			size += CodedOutputStream.ComputeInt32Size(changedMaxMemberCount_) + 1;
		}
		if (assignedRole_ != 0)
		{
			size += CodedOutputStream.ComputeInt32Size(assignedRole_) + 1;
		}
		if (timestamp_ != 0)
		{
			size += CodedOutputStream.ComputeInt64Size(timestamp_) + 1;
		}
		return size;
	}

	public void MergeFrom(CodedInputStream input)
	{
		uint tag;
		while ((tag = input.ReadTag()) != 0)
		{
			switch (tag)
			{
			case 8u:
				clanLogType_ = input.ReadInt32();
				break;
			case 16u:
				changedClanType_ = input.ReadInt32();
				break;
			case 26u:
				ChangedClanName = input.ReadString();
				break;
			case 34u:
				ChangedClanTag = input.ReadString();
				break;
			case 42u:
				PrimaryMember = input.ReadString();
				break;
			case 50u:
				SecondaryMember = input.ReadString();
				break;
			case 56u:
				changedMaxMemberCount_ = input.ReadInt32();
				break;
			case 64u:
				assignedRole_ = input.ReadInt32();
				break;
			case 72u:
				timestamp_ = input.ReadInt64();
				break;
			default:
				input.SkipLastField();
				break;
			}
		}
	}

	public void MergeFrom(ClanLogMessage message)
	{
		if (message != null)
		{
			if (message.clanLogType_ != 0)
			{
				clanLogType_ = message.clanLogType_;
			}
			if (message.changedClanName_.Length != 0)
			{
				changedClanName_ = message.changedClanName_;
			}
			if (message.changedClanTag_.Length != 0)
			{
				changedClanTag_ = message.changedClanTag_;
			}
			if (message.changedClanType_ != 0)
			{
				changedClanType_ = message.changedClanType_;
			}
			if (message.primaryMember_.Length != 0)
			{
				primaryMember_ = message.primaryMember_;
			}
			if (message.secondaryMember_.Length != 0)
			{
				secondaryMember_ = message.secondaryMember_;
			}
			if (message.changedMaxMemberCount_ != 0)
			{
				changedMaxMemberCount_ = message.changedMaxMemberCount_;
			}
			if (message.assignedRole_ != 0)
			{
				assignedRole_ = message.assignedRole_;
			}
			if (message.timestamp_ != 0)
			{
				timestamp_ = message.timestamp_;
			}
		}
	}

	public ClanLogMessage Clone()
	{
		return new ClanLogMessage(this);
	}
}
