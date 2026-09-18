using System;
using System.Diagnostics;
using Axlebolt.Bolt.Protobuf;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class ClanJoinRequest : IMessage<ClanJoinRequest>, IMessage, IEquatable<ClanJoinRequest>, IDeepCloneable<ClanJoinRequest>
{
	private static readonly MessageParser<ClanJoinRequest> _parser = new MessageParser<ClanJoinRequest>(() => new ClanJoinRequest());

	public const int IdFieldNumber = 1;

	private string id_ = "";

	public const int ClanFieldNumber = 2;

	private Clan clan_;

	public const int RequestSenderFieldNumber = 3;

	private Player requestSender_;

	public const int CreateDateFieldNumber = 4;

	private long createDate_;

	public const int CloseDateFieldNumber = 5;

	private long closeDate_;

	public const int RequestTypeFieldNumber = 6;

	private RequestType requestType_ = RequestType.NoneType;

	[DebuggerNonUserCode]
	public static MessageParser<ClanJoinRequest> Parser => _parser;

	[DebuggerNonUserCode]
	public static MessageDescriptor Descriptor => ClanMessageReflection.Descriptor.MessageTypes[2];

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
	public Clan Clan
	{
		get
		{
			return clan_;
		}
		set
		{
			clan_ = value;
		}
	}

	[DebuggerNonUserCode]
	public Player RequestSender
	{
		get
		{
			return requestSender_;
		}
		set
		{
			requestSender_ = value;
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

	public long CloseDate
	{
		get
		{
			return closeDate_;
		}
		set
		{
			closeDate_ = value;
		}
	}

	[DebuggerNonUserCode]
	public RequestType RequestType
	{
		get
		{
			return requestType_;
		}
		set
		{
			requestType_ = value;
		}
	}

	[DebuggerNonUserCode]
	public ClanJoinRequest()
	{
	}

	[DebuggerNonUserCode]
	public ClanJoinRequest(ClanJoinRequest other)
		: this()
	{
		id_ = other.id_;
		Clan = ((other.clan_ != null) ? other.Clan.Clone() : null);
		RequestSender = ((other.requestSender_ != null) ? other.RequestSender.Clone() : null);
		createDate_ = other.createDate_;
		closeDate_ = other.closeDate_;
		requestType_ = other.requestType_;
	}

	[DebuggerNonUserCode]
	public ClanJoinRequest Clone()
	{
		return new ClanJoinRequest(this);
	}

	[DebuggerNonUserCode]
	public override bool Equals(object other)
	{
		return Equals(other as ClanJoinRequest);
	}

	[DebuggerNonUserCode]
	public bool Equals(ClanJoinRequest other)
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
		if (!object.Equals(Clan, other.Clan))
		{
			return false;
		}
		if (!object.Equals(RequestSender, other.RequestSender))
		{
			return false;
		}
		if (CreateDate != other.CreateDate)
		{
			return false;
		}
		if (CloseDate != other.CloseDate)
		{
			return false;
		}
		if (RequestType != other.RequestType)
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
		if (clan_ != null)
		{
			num ^= Clan.GetHashCode();
		}
		if (requestSender_ != null)
		{
			num ^= RequestSender.GetHashCode();
		}
		if (CreateDate != 0)
		{
			num ^= CreateDate.GetHashCode();
		}
		if (CloseDate != 0)
		{
			num ^= CloseDate.GetHashCode();
		}
		if (RequestType != RequestType.NoneType)
		{
			num ^= RequestType.GetHashCode();
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
		if (clan_ != null)
		{
			output.WriteRawTag(18);
			output.WriteMessage(Clan);
		}
		if (requestSender_ != null)
		{
			output.WriteRawTag(26);
			output.WriteMessage(RequestSender);
		}
		if (CreateDate != 0)
		{
			output.WriteRawTag(32);
			output.WriteInt64(CreateDate);
		}
		if (CloseDate != 0)
		{
			output.WriteRawTag(40);
			output.WriteInt64(CloseDate);
		}
		if (RequestType != RequestType.NoneType)
		{
			output.WriteRawTag(48);
			output.WriteEnum((int)RequestType);
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
		if (clan_ != null)
		{
			num += 1 + CodedOutputStream.ComputeMessageSize(Clan);
		}
		if (requestSender_ != null)
		{
			num += 1 + CodedOutputStream.ComputeMessageSize(RequestSender);
		}
		if (CreateDate != 0)
		{
			num += 1 + CodedOutputStream.ComputeInt64Size(CreateDate);
		}
		if (CloseDate != 0)
		{
			num += 1 + CodedOutputStream.ComputeInt64Size(CloseDate);
		}
		if (RequestType != RequestType.NoneType)
		{
			num += 1 + CodedOutputStream.ComputeEnumSize((int)RequestType);
		}
		return num;
	}

	[DebuggerNonUserCode]
	public void MergeFrom(ClanJoinRequest other)
	{
		if (other == null)
		{
			return;
		}
		if (other.Id.Length != 0)
		{
			Id = other.Id;
		}
		if (other.clan_ != null)
		{
			if (clan_ == null)
			{
				clan_ = new Clan();
			}
			Clan.MergeFrom(other.Clan);
		}
		if (other.requestSender_ != null)
		{
			if (requestSender_ == null)
			{
				requestSender_ = new Player();
			}
			RequestSender.MergeFrom(other.RequestSender);
		}
		if (other.CreateDate != 0)
		{
			CreateDate = other.CreateDate;
		}
		if (other.CloseDate != 0)
		{
			CloseDate = other.CloseDate;
		}
		if (other.RequestType != RequestType.NoneType)
		{
			RequestType = other.RequestType;
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
				if (clan_ == null)
				{
					clan_ = new Clan();
				}
				input.ReadMessage(clan_);
				break;
			case 26u:
				if (requestSender_ == null)
				{
					requestSender_ = new Player();
				}
				input.ReadMessage(requestSender_);
				break;
			case 32u:
				CreateDate = input.ReadInt64();
				break;
			case 40u:
				CloseDate = input.ReadInt64();
				break;
			case 48u:
				requestType_ = (RequestType)input.ReadEnum();
				break;
			}
		}
	}
}
