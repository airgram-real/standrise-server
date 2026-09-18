using System;
using System.Diagnostics;
using Axlebolt.Bolt.Protobuf;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class ClanMember : IMessage<ClanMember>, IMessage, IEquatable<ClanMember>, IDeepCloneable<ClanMember>
{
	private static readonly MessageParser<ClanMember> _parser = new MessageParser<ClanMember>(() => new ClanMember());

	public const int PlayerFriendFieldNumber = 1;

	private PlayerFriend playerFriend_ = new PlayerFriend();

	public const int ClanIdFieldNumber = 2;

	private string clanId_ = "";

	public const int RoleIdFieldNumber = 3;

	private int roleId_;

	public const int CreateDateFieldNumber = 4;

	private long createDate_;

	[DebuggerNonUserCode]
	public static MessageParser<ClanMember> Parser => _parser;

	[DebuggerNonUserCode]
	public static MessageDescriptor Descriptor => ClanMessageReflection.Descriptor.MessageTypes[1];

	[DebuggerNonUserCode]
	MessageDescriptor IMessage.Descriptor => Descriptor;

	[DebuggerNonUserCode]
	public PlayerFriend PlayerFriend => playerFriend_;

	[DebuggerNonUserCode]
	public string ClanId
	{
		get
		{
			return clanId_;
		}
		set
		{
			clanId_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	[DebuggerNonUserCode]
	public int RoleId
	{
		get
		{
			return roleId_;
		}
		set
		{
			roleId_ = value;
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
	public ClanMember()
	{
	}

	[DebuggerNonUserCode]
	public ClanMember(ClanMember other)
		: this()
	{
		playerFriend_ = ((other.playerFriend_ != null) ? other.PlayerFriend.Clone() : null);
		clanId_ = other.clanId_;
		RoleId = other.roleId_;
		createDate_ = other.createDate_;
	}

	[DebuggerNonUserCode]
	public ClanMember Clone()
	{
		return new ClanMember(this);
	}

	[DebuggerNonUserCode]
	public override bool Equals(object other)
	{
		return Equals(other as ClanMember);
	}

	[DebuggerNonUserCode]
	public bool Equals(ClanMember other)
	{
		if (other == null)
		{
			return false;
		}
		if (other == this)
		{
			return true;
		}
		if (!object.Equals(PlayerFriend, other.PlayerFriend))
		{
			return false;
		}
		if (ClanId != other.ClanId)
		{
			return false;
		}
		if (!object.Equals(RoleId, other.RoleId))
		{
			return false;
		}
		if (CreateDate != other.CreateDate)
		{
			return false;
		}
		return true;
	}

	[DebuggerNonUserCode]
	public override int GetHashCode()
	{
		int num = 1;
		if (playerFriend_ != null)
		{
			num ^= PlayerFriend.GetHashCode();
		}
		if (ClanId.Length != 0)
		{
			num ^= ClanId.GetHashCode();
		}
		if (roleId_ != 0)
		{
			num ^= RoleId.GetHashCode();
		}
		if (CreateDate != 0)
		{
			num ^= CreateDate.GetHashCode();
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
		if (playerFriend_ != null)
		{
			output.WriteRawTag(10);
			output.WriteMessage(PlayerFriend);
		}
		if (ClanId.Length != 0)
		{
			output.WriteRawTag(18);
			output.WriteString(ClanId);
		}
		if (roleId_ != 0)
		{
			output.WriteRawTag(24);
			output.WriteInt32(RoleId);
		}
		if (CreateDate != 0)
		{
			output.WriteRawTag(32);
			output.WriteInt64(CreateDate);
		}
	}

	[DebuggerNonUserCode]
	public int CalculateSize()
	{
		int num = 0;
		if (playerFriend_ != null)
		{
			num += 1 + CodedOutputStream.ComputeMessageSize(PlayerFriend);
		}
		if (ClanId.Length != 0)
		{
			num += 1 + CodedOutputStream.ComputeStringSize(ClanId);
		}
		if (roleId_ != 0)
		{
			num += 1 + CodedOutputStream.ComputeInt32Size(RoleId);
		}
		if (CreateDate != 0)
		{
			num += 1 + CodedOutputStream.ComputeInt64Size(CreateDate);
		}
		return num;
	}

	[DebuggerNonUserCode]
	public void MergeFrom(ClanMember other)
	{
		if (other == null)
		{
			return;
		}
		if (other.playerFriend_ != null)
		{
			if (playerFriend_ == null)
			{
				playerFriend_ = new PlayerFriend();
			}
			PlayerFriend.MergeFrom(other.PlayerFriend);
		}
		if (other.ClanId.Length != 0)
		{
			ClanId = other.ClanId;
		}
		if (other.roleId_ != 0)
		{
			RoleId = other.RoleId;
		}
		if (other.CreateDate != 0)
		{
			CreateDate = other.CreateDate;
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
				if (playerFriend_ == null)
				{
					playerFriend_ = new PlayerFriend();
				}
				input.ReadMessage(playerFriend_);
				break;
			case 18u:
				ClanId = input.ReadString();
				break;
			case 24u:
				roleId_ = input.ReadInt32();
				break;
			case 32u:
				CreateDate = input.ReadInt64();
				break;
			}
		}
	}
}
