using System;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class ClanUserMessage : IMessage<ClanUserMessage>, IMessage, IEquatable<ClanUserMessage>, IDeepCloneable<ClanUserMessage>
{
	private static readonly MessageParser<ClanUserMessage> _parser = new MessageParser<ClanUserMessage>(() => new ClanUserMessage());

	private string id_ = "";

	private long timestamp_;

	private MessageType messageType_;

	private ClanChatMessage chatMessage_;

	private ClanLogMessage logMessage_;

	public static MessageParser<ClanUserMessage> Parser => _parser;

	public static MessageDescriptor Descriptor => ClanMessageReflection.Descriptor.MessageTypes[6];

	MessageDescriptor IMessage.Descriptor => Descriptor;

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

	public MessageType MessageType
	{
		get
		{
			return messageType_;
		}
		set
		{
			messageType_ = value;
		}
	}

	public ClanChatMessage ChatMessage
	{
		get
		{
			return chatMessage_;
		}
		set
		{
			chatMessage_ = value;
		}
	}

	public ClanLogMessage LogMessage
	{
		get
		{
			return logMessage_;
		}
		set
		{
			logMessage_ = value;
		}
	}

	public ClanUserMessage()
	{
	}

	public ClanUserMessage(ClanUserMessage other)
	{
		id_ = other.id_;
		timestamp_ = other.timestamp_;
		messageType_ = other.messageType_;
		chatMessage_ = other.chatMessage_;
		logMessage_ = other.logMessage_;
	}

	public override bool Equals(object other)
	{
		return Equals(other as ClanUserMessage);
	}

	public bool Equals(ClanUserMessage other)
	{
		if (other == null)
		{
			return false;
		}
		if (this == other)
		{
			return true;
		}
		return Id == other.Id && Timestamp == other.Timestamp && MessageType == other.MessageType && object.Equals(ChatMessage, other.ChatMessage) && object.Equals(LogMessage, other.LogMessage);
	}

	public override int GetHashCode()
	{
		int hash = 1;
		hash ^= Id.GetHashCode();
		hash ^= Timestamp.GetHashCode();
		hash ^= MessageType.GetHashCode();
		if (ChatMessage != null)
		{
			hash ^= ChatMessage.GetHashCode();
		}
		if (LogMessage != null)
		{
			hash ^= LogMessage.GetHashCode();
		}
		return hash;
	}

	public override string ToString()
	{
		return JsonFormatter.ToDiagnosticString(this);
	}

	public void WriteTo(CodedOutputStream output)
	{
		if (Id.Length != 0)
		{
			output.WriteRawTag(10);
			output.WriteString(Id);
		}
		if (Timestamp != 0)
		{
			output.WriteRawTag(16);
			output.WriteInt64(Timestamp);
		}
		if (MessageType != MessageType.NoneType)
		{
			output.WriteRawTag(24);
			output.WriteEnum((int)MessageType);
		}
		if (ChatMessage != null)
		{
			output.WriteRawTag(34);
			output.WriteMessage(ChatMessage);
		}
		if (LogMessage != null)
		{
			output.WriteRawTag(50);
			output.WriteMessage(LogMessage);
		}
	}

	public int CalculateSize()
	{
		int size = 0;
		if (Id.Length != 0)
		{
			size += CodedOutputStream.ComputeStringSize(Id) + 1;
		}
		if (Timestamp != 0)
		{
			size += CodedOutputStream.ComputeInt64Size(Timestamp) + 1;
		}
		if (MessageType != MessageType.NoneType)
		{
			size += CodedOutputStream.ComputeEnumSize((int)MessageType) + 1;
		}
		if (ChatMessage != null)
		{
			size += CodedOutputStream.ComputeMessageSize(ChatMessage) + 1;
		}
		if (LogMessage != null)
		{
			size += CodedOutputStream.ComputeMessageSize(LogMessage) + 1;
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
				Id = input.ReadString();
				break;
			case 16u:
				Timestamp = input.ReadInt64();
				break;
			case 24u:
				MessageType = (MessageType)input.ReadEnum();
				break;
			case 34u:
				if (chatMessage_ == null)
				{
					ChatMessage = new ClanChatMessage();
				}
				input.ReadMessage(ChatMessage);
				break;
			case 50u:
				if (logMessage_ == null)
				{
					LogMessage = new ClanLogMessage();
				}
				input.ReadMessage(LogMessage);
				break;
			default:
				input.SkipLastField();
				break;
			}
		}
	}

	public void MergeFrom(ClanUserMessage other)
	{
		if (other != null)
		{
			if (other.id_.Length != 0)
			{
				id_ = other.id_;
			}
			ChatMessage.MergeFrom(other.ChatMessage);
			LogMessage.MergeFrom(other.LogMessage);
		}
	}

	public ClanUserMessage Clone()
	{
		return new ClanUserMessage(this);
	}
}
