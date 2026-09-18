using System;
using System.Diagnostics;
using Axlebolt.Bolt.Protobuf;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Axlebolt.Bolt.Protobuf
{
    public sealed class ClanSettings : IMessage<ClanSettings>, IMessage, IEquatable<ClanSettings>, IDeepCloneable<ClanSettings>
    {
        private static readonly MessageParser<ClanSettings> _parser = new MessageParser<ClanSettings>(() => new ClanSettings());

        private int initialMembersCount_;

        private int membersCountLimit_;

        private CurrencyAmount membercCountUpgradeCost_ = new CurrencyAmount();

        private CurrencyAmount changeClanNameOrTagCost_ = new CurrencyAmount();

        private CurrencyAmount clanCreateCost_ = new CurrencyAmount();

        public static MessageParser<ClanSettings> Parser => _parser;

        [DebuggerNonUserCode]
        public int InitialMembersCount
        {
            get
            {
                return initialMembersCount_;
            }
            set
            {
                initialMembersCount_ = value;
            }
        }

        [DebuggerNonUserCode]
        public int MembersCountLimit
        {
            get
            {
                return membersCountLimit_;
            }
            set
            {
                membersCountLimit_ = value;
            }
        }

        [DebuggerNonUserCode]
        public CurrencyAmount MembercCountUpgradeCost => membercCountUpgradeCost_;

        [DebuggerNonUserCode]
        public CurrencyAmount ChangeClanNameOrTagCost => changeClanNameOrTagCost_;

        [DebuggerNonUserCode]
        public CurrencyAmount ClanCreateCost => clanCreateCost_;

        public MessageDescriptor Descriptor => ClanMessageReflection.Descriptor.MessageTypes[17];

        MessageDescriptor IMessage.Descriptor => Descriptor;

        [DebuggerNonUserCode]
        public ClanSettings()
        {
        }

        public ClanSettings(ClanSettings other)
        {
            initialMembersCount_ = other.initialMembersCount_;
            membersCountLimit_ = other.membersCountLimit_;
            membercCountUpgradeCost_ = other.membercCountUpgradeCost_;
            changeClanNameOrTagCost_ = other.changeClanNameOrTagCost_;
            clanCreateCost_ = other.clanCreateCost_;
        }

        [DebuggerNonUserCode]
        public override bool Equals(object other)
        {
            return Equals(other as ClanSettings);
        }

        [DebuggerNonUserCode]
        public bool Equals(ClanSettings other)
        {
            if (other == null)
            {
                return false;
            }
            if (other == this)
            {
                return true;
            }
            if (InitialMembersCount != other.InitialMembersCount)
            {
                return false;
            }
            if (MembersCountLimit != other.MembersCountLimit)
            {
                return false;
            }
            if (!object.Equals(MembercCountUpgradeCost, other.MembercCountUpgradeCost))
            {
                return false;
            }
            if (!object.Equals(ChangeClanNameOrTagCost, other.ChangeClanNameOrTagCost))
            {
                return false;
            }
            if (!object.Equals(ClanCreateCost, other.ClanCreateCost))
            {
                return false;
            }
            return true;
        }

        [DebuggerNonUserCode]
        public override int GetHashCode()
        {
            int num = 1;
            if (membersCountLimit_ != 0)
            {
                num ^= membersCountLimit_.GetHashCode();
            }
            if (initialMembersCount_ != 0)
            {
                num ^= initialMembersCount_.GetHashCode();
            }
            if (membercCountUpgradeCost_ != null)
            {
                num ^= membercCountUpgradeCost_.GetHashCode();
            }
            if (changeClanNameOrTagCost_ != null)
            {
                num ^= changeClanNameOrTagCost_.GetHashCode();
            }
            if (clanCreateCost_ != null)
            {
                num ^= clanCreateCost_.GetHashCode();
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
            if (initialMembersCount_ != 0)
            {
                output.WriteRawTag(8);
                output.WriteInt32(initialMembersCount_);
            }
            if (membersCountLimit_ != 0)
            {
                output.WriteRawTag(16);
                output.WriteInt32(membersCountLimit_);
            }
            if (membercCountUpgradeCost_ != null)
            {
                output.WriteRawTag(26);
                output.WriteMessage(membercCountUpgradeCost_);
            }
            if (changeClanNameOrTagCost_ != null)
            {
                output.WriteRawTag(34);
                output.WriteMessage(changeClanNameOrTagCost_);
            }
            if (clanCreateCost_ != null)
            {
                output.WriteRawTag(42);
                output.WriteMessage(clanCreateCost_);
            }
        }

        [DebuggerNonUserCode]
        public int CalculateSize()
        {
            int num = 0;
            if (initialMembersCount_ != 0)
            {
                num += 1 + CodedOutputStream.ComputeInt32Size(initialMembersCount_);
            }
            if (membersCountLimit_ != 0)
            {
                num += 1 + CodedOutputStream.ComputeInt32Size(membersCountLimit_);
            }
            if (membercCountUpgradeCost_ != null)
            {
                num += 1 + CodedOutputStream.ComputeMessageSize(membercCountUpgradeCost_);
            }
            if (changeClanNameOrTagCost_ != null)
            {
                num += 1 + CodedOutputStream.ComputeMessageSize(changeClanNameOrTagCost_);
            }
            if (clanCreateCost_ != null)
            {
                num += 1 + CodedOutputStream.ComputeMessageSize(clanCreateCost_);
            }
            return num;
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
                    case 16u:
                        InitialMembersCount = input.ReadInt32();
                        break;
                    case 8u:
                        MembersCountLimit = input.ReadInt32();
                        break;
                    case 26u:
                        if (membercCountUpgradeCost_ == null)
                        {
                            membercCountUpgradeCost_ = new CurrencyAmount();
                        }
                        input.ReadMessage(membercCountUpgradeCost_);
                        break;
                    case 34u:
                        if (changeClanNameOrTagCost_ == null)
                        {
                            changeClanNameOrTagCost_ = new CurrencyAmount();
                        }
                        input.ReadMessage(changeClanNameOrTagCost_);
                        break;
                    case 42u:
                        if (clanCreateCost_ == null)
                        {
                            clanCreateCost_ = new CurrencyAmount();
                        }
                        input.ReadMessage(clanCreateCost_);
                        break;
                }
            }
        }

        public void MergeFrom(ClanSettings other)
        {
            if (other == null)
            {
                return;
            }
            if (other.InitialMembersCount != 0)
            {
                InitialMembersCount = other.InitialMembersCount;
            }
            if (other.MembersCountLimit != 0)
            {
                MembersCountLimit = other.MembersCountLimit;
            }
            if (other.MembercCountUpgradeCost != null)
            {
                if (membercCountUpgradeCost_ == null)
                {
                    membercCountUpgradeCost_ = new CurrencyAmount();
                }
                MembercCountUpgradeCost.MergeFrom(other.MembercCountUpgradeCost);
            }
            if (other.ChangeClanNameOrTagCost != null)
            {
                if (changeClanNameOrTagCost_ == null)
                {
                    changeClanNameOrTagCost_ = new CurrencyAmount();
                }
                ChangeClanNameOrTagCost.MergeFrom(other.ChangeClanNameOrTagCost);
            }
            if (other.ClanCreateCost != null)
            {
                if (clanCreateCost_ == null)
                {
                    clanCreateCost_ = new CurrencyAmount();
                }
                ClanCreateCost.MergeFrom(other.ClanCreateCost);
            }
        }

        public ClanSettings Clone()
        {
            return new ClanSettings(this);
        }
    }
}
