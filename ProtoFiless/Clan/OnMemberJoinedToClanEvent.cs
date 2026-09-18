using System;
using System.Diagnostics;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class OnMemberJoinedToClanEvent : IMessage<OnMemberJoinedToClanEvent>, IMessage, IEquatable<OnMemberJoinedToClanEvent>, IDeepCloneable<OnMemberJoinedToClanEvent>
{
	private static readonly MessageParser<OnMemberJoinedToClanEvent> _parser = new MessageParser<OnMemberJoinedToClanEvent>(() => new OnMemberJoinedToClanEvent());

	public const int ClanMemberFieldNumber = 1;

	private ClanMember clanMember_;

	[DebuggerNonUserCode]
	public static MessageParser<OnMemberJoinedToClanEvent> Parser => _parser;

	[DebuggerNonUserCode]
	public static MessageDescriptor Descriptor => ClanMessageReflection.Descriptor.MessageTypes[10];

	[DebuggerNonUserCode]
	MessageDescriptor IMessage.Descriptor => Descriptor;

	[DebuggerNonUserCode]
	public ClanMember ClanMember
	{
		get
		{
			return clanMember_;
		}
		set
		{
			clanMember_ = value;
		}
	}

	[DebuggerNonUserCode]
	public OnMemberJoinedToClanEvent()
	{
	}

	[DebuggerNonUserCode]
	public OnMemberJoinedToClanEvent(OnMemberJoinedToClanEvent other)
		: this()
	{
		ClanMember = ((other.clanMember_ != null) ? other.ClanMember.Clone() : null);
	}

	[DebuggerNonUserCode]
	public OnMemberJoinedToClanEvent Clone()
	{
		return new OnMemberJoinedToClanEvent(this);
	}

	[DebuggerNonUserCode]
	public override bool Equals(object other)
	{
		return Equals(other as OnMemberJoinedToClanEvent);
	}

	[DebuggerNonUserCode]
	public bool Equals(OnMemberJoinedToClanEvent other)
	{
		if (other == null)
		{
			return false;
		}
		if (other == this)
		{
			return true;
		}
		if (!object.Equals(ClanMember, other.ClanMember))
		{
			return false;
		}
		return true;
	}

	[DebuggerNonUserCode]
	public override int GetHashCode()
	{
		int num = 1;
		if (clanMember_ != null)
		{
			num ^= ClanMember.GetHashCode();
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
		if (clanMember_ != null)
		{
			output.WriteRawTag(10);
			output.WriteMessage(ClanMember);
		}
	}

	[DebuggerNonUserCode]
	public int CalculateSize()
	{
		int num = 0;
		if (clanMember_ != null)
		{
			num += 1 + CodedOutputStream.ComputeMessageSize(ClanMember);
		}
		return num;
	}

	[DebuggerNonUserCode]
	public void MergeFrom(OnMemberJoinedToClanEvent other)
	{
		if (other != null && other.clanMember_ != null)
		{
			if (clanMember_ == null)
			{
				clanMember_ = new ClanMember();
			}
			ClanMember.MergeFrom(other.ClanMember);
		}
	}

	[DebuggerNonUserCode]
	public void MergeFrom(CodedInputStream input)
	{
		uint num;
		while ((num = input.ReadTag()) != 0)
		{
			uint num2 = num;
			if (num2 != 10)
			{
				input.SkipLastField();
				continue;
			}
			if (clanMember_ == null)
			{
				clanMember_ = new ClanMember();
			}
			input.ReadMessage(clanMember_);
		}
	}
}
