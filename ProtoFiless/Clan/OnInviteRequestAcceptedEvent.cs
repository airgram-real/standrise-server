using System;
using Axlebolt.Bolt.Protobuf;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf;

public sealed class OnInviteRequestAcceptedEvent : IMessage<OnInviteRequestAcceptedEvent>, IMessage, IEquatable<OnInviteRequestAcceptedEvent>, IDeepCloneable<OnInviteRequestAcceptedEvent>
{
	private static readonly MessageParser<OnInviteRequestAcceptedEvent> _parser = new MessageParser<OnInviteRequestAcceptedEvent>(() => new OnInviteRequestAcceptedEvent());

	private Player player_;

	public static MessageParser<OnInviteRequestAcceptedEvent> Parser => _parser;

	public static MessageDescriptor Descriptor => ClanMessageReflection.Descriptor.MessageTypes[19];

	MessageDescriptor IMessage.Descriptor => Descriptor;

	public Player Player
	{
		get
		{
			return player_;
		}
		set
		{
			player_ = value;
		}
	}

	public void UpdatePlayer(Player newPlayer)
	{
		player_ = newPlayer;
	}

	public OnInviteRequestAcceptedEvent()
	{
	}

	public OnInviteRequestAcceptedEvent(OnInviteRequestAcceptedEvent other)
	{
		player_ = other.player_;
	}

	public override bool Equals(object other)
	{
		return Equals(other as OnInviteRequestAcceptedEvent);
	}

	public bool Equals(OnInviteRequestAcceptedEvent other)
	{
		if (other == null)
		{
			return false;
		}
		if (other == this)
		{
			return true;
		}
		return object.Equals(Player, other.Player);
	}

	public override int GetHashCode()
	{
		int hash = 1;
		if (Player != null)
		{
			hash ^= Player.GetHashCode();
		}
		return hash;
	}

	public override string ToString()
	{
		return JsonFormatter.ToDiagnosticString(this);
	}

	public void WriteTo(CodedOutputStream output)
	{
		if (Player != null)
		{
			output.WriteRawTag(10);
			output.WriteMessage(Player);
		}
	}

	public int CalculateSize()
	{
		int size = 0;
		if (Player != null)
		{
			size += CodedOutputStream.ComputeMessageSize(Player);
			size++;
		}
		return size;
	}

	public void MergeFrom(CodedInputStream input)
	{
		uint tag;
		while ((tag = input.ReadTag()) != 0)
		{
			uint num = tag;
			uint num2 = num;
			if (num2 == 10)
			{
				if (Player == null)
				{
					player_ = new Player();
				}
				input.ReadMessage(Player);
			}
			else
			{
				input.SkipLastField();
			}
		}
	}

	public void MergeFrom(OnInviteRequestAcceptedEvent message)
	{
		if (message != null)
		{
			Player.MergeFrom(message.Player);
		}
	}

	public OnInviteRequestAcceptedEvent Clone()
	{
		return new OnInviteRequestAcceptedEvent(this);
	}
}
