using System;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class ClanChatMessage : IMessage<ClanChatMessage>, IMessage, IEquatable<ClanChatMessage>, IDeepCloneable<ClanChatMessage>
{
	private static readonly MessageParser<ClanChatMessage> _parser = new MessageParser<ClanChatMessage>(() => new ClanChatMessage());

	private string senderId_ = "";

	private string message_ = "";

	public static MessageParser<ClanChatMessage> Parser => _parser;

	public static MessageDescriptor Descriptor => ClanMessageReflection.Descriptor.MessageTypes[7];

	MessageDescriptor IMessage.Descriptor => Descriptor;

	public string SenderId
	{
		get
		{
			return senderId_;
		}
		set
		{
			senderId_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	public string Message
	{
		get
		{
			return message_;
		}
		set
		{
			message_ = ProtoPreconditions.CheckNotNull(value, "value");
		}
	}

	public ClanChatMessage()
	{
	}

	public ClanChatMessage(ClanChatMessage other)
	{
		senderId_ = other.senderId_;
		message_ = other.message_;
	}

	public override bool Equals(object other)
	{
		return Equals(other as ClanChatMessage);
	}

	public bool Equals(ClanChatMessage other)
	{
		if (other == null)
		{
			return false;
		}
		if (this == other)
		{
			return true;
		}
		return SenderId == other.SenderId && Message == other.Message;
	}

	public override int GetHashCode()
	{
		int hash = 0;
		hash ^= SenderId.GetHashCode();
		return hash ^ Message.GetHashCode();
	}

	public override string ToString()
	{
		return JsonFormatter.ToDiagnosticString(this);
	}

	public void WriteTo(CodedOutputStream output)
	{
		if (SenderId.Length != 0)
		{
			output.WriteRawTag(10);
			output.WriteString(SenderId);
		}
		if (Message.Length != 0)
		{
			output.WriteRawTag(18);
			output.WriteString(Message);
		}
	}

	public int CalculateSize()
	{
		int size = 0;
		if (SenderId.Length != 0)
		{
			size += CodedOutputStream.ComputeStringSize(SenderId) + 1;
		}
		if (Message.Length != 0)
		{
			size += CodedOutputStream.ComputeStringSize(Message) + 1;
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
			case 10u:
				SenderId = input.ReadString();
				break;
			case 18u:
				Message = input.ReadString();
				break;
			default:
				input.SkipLastField();
				break;
			}
		}
	}

	public void MergeFrom(ClanChatMessage message)
	{
		if (message != null)
		{
			if (message.senderId_.Length != 0)
			{
				senderId_ = message.senderId_;
			}
			if (message.message_.Length != 0)
			{
				message_ = message.message_;
			}
		}
	}

	public ClanChatMessage Clone()
	{
		return new ClanChatMessage(this);
	}
}
