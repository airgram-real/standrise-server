using System;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using pbc = Google.Protobuf.Collections;

namespace Axlebolt.Bolt.Protobuf
{
    public sealed partial class ClanMemberStat : IMessage<ClanMemberStat>, IMessage, IEquatable<ClanMemberStat>, IDeepCloneable<ClanMemberStat>
    {
        private static readonly MessageParser<ClanMemberStat> _parser = new MessageParser<ClanMemberStat>(() => new ClanMemberStat());
        

        public static MessageParser<ClanMemberStat> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public ClanMemberStat() 
        {
            statId_ = "";
        }

        public ClanMemberStat(ClanMemberStat other) : this() 
        {
            statId_ = other.statId_;
            type_ = other.type_;
            intValue_ = other.intValue_;
            floatValue_ = other.floatValue_;
            longValue_ = other.longValue_;
            
        }

        public ClanMemberStat Clone() { return new ClanMemberStat(this); }

        public const int StatIdFieldNumber = 1;
        private string statId_ = "";
        public string StatId {
            get { return statId_; }
            set { statId_ = ProtoPreconditions.CheckNotNull(value, "value"); }
        }

        public const int TypeFieldNumber = 2;
        private StatDefType type_ = 0;
        public StatDefType Type {
            get { return type_; }
            set { type_ = value; }
        }

        public const int IntValueFieldNumber = 3;
        private int intValue_;
        public int IntValue {
            get { return intValue_; }
            set { intValue_ = value; }
        }

        public const int FloatValueFieldNumber = 4;
        private float floatValue_;
        public float FloatValue {
            get { return floatValue_; }
            set { floatValue_ = value; }
        }

        public const int LongValueFieldNumber = 5;
        private long longValue_;
        public long LongValue {
            get { return longValue_; }
            set { longValue_ = value; }
        }

        public override bool Equals(object other) { return Equals(other as ClanMemberStat); }

        public bool Equals(ClanMemberStat other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (StatId != other.StatId) return false;
            if (Type != other.Type) return false;
            if (IntValue != other.IntValue) return false;
            if (FloatValue != other.FloatValue) return false;
            if (LongValue != other.LongValue) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (StatId.Length != 0) hash ^= StatId.GetHashCode();
            if (Type != 0) hash ^= Type.GetHashCode();
            if (IntValue != 0) hash ^= IntValue.GetHashCode();
            if (FloatValue != 0F) hash ^= FloatValue.GetHashCode();
            if (LongValue != 0L) hash ^= LongValue.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (StatId.Length != 0) {
                output.WriteRawTag(10);
                output.WriteString(StatId);
            }
            if (Type != 0) {
                output.WriteRawTag(16);
                output.WriteEnum((int) Type);
            }
            if (IntValue != 0) {
                output.WriteRawTag(24);
                output.WriteInt32(IntValue);
            }
            if (FloatValue != 0F) {
                output.WriteRawTag(37);
                output.WriteFloat(FloatValue);
            }
            if (LongValue != 0L) {
                output.WriteRawTag(40);
                output.WriteInt64(LongValue);
            }
        }

        public int CalculateSize() 
        {
            int size = 0;
            if (StatId.Length != 0) {
                size += 1 + CodedOutputStream.ComputeStringSize(StatId);
            }
            if (Type != 0) {
                size += 1 + CodedOutputStream.ComputeEnumSize((int) Type);
            }
            if (IntValue != 0) {
                size += 1 + CodedOutputStream.ComputeInt32Size(IntValue);
            }
            if (FloatValue != 0F) {
                size += 1 + 4;
            }
            if (LongValue != 0L) {
                size += 1 + CodedOutputStream.ComputeInt64Size(LongValue);
            }
            return size;
        }

        public void MergeFrom(ClanMemberStat other) 
        {
            if (other == null) return;
            if (other.StatId.Length != 0) {
                StatId = other.StatId;
            }
            if (other.Type != 0) {
                Type = other.Type;
            }
            if (other.IntValue != 0) {
                IntValue = other.IntValue;
            }
            if (other.FloatValue != 0F) {
                FloatValue = other.FloatValue;
            }
            if (other.LongValue != 0L) {
                LongValue = other.LongValue;
            }
            
        }

        public void MergeFrom(CodedInputStream input) 
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0) 
            {
                switch(tag) 
                {
                    default: input.SkipLastField();
                        break;
                    case 10: {
                        StatId = input.ReadString();
                        break;
                    }
                    case 16: {
                        Type = (StatDefType) input.ReadEnum();
                        break;
                    }
                    case 24: {
                        IntValue = input.ReadInt32();
                        break;
                    }
                    case 37: {
                        FloatValue = input.ReadFloat();
                        break;
                    }
                    case 40: {
                        LongValue = input.ReadInt64();
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class ClanMemberStats : IMessage<ClanMemberStats>, IMessage, IEquatable<ClanMemberStats>, IDeepCloneable<ClanMemberStats>
    {
        private static readonly MessageParser<ClanMemberStats> _parser = new MessageParser<ClanMemberStats>(() => new ClanMemberStats());
        

        public static MessageParser<ClanMemberStats> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public ClanMemberStats() 
        {
            playerId_ = "";
        }

        public ClanMemberStats(ClanMemberStats other) : this() 
        {
            playerId_ = other.playerId_;
            stats_ = other.stats_.Clone();
            
        }

        public ClanMemberStats Clone() { return new ClanMemberStats(this); }

        public const int PlayerIdFieldNumber = 1;
        private string playerId_ = "";
        public string PlayerId {
            get { return playerId_; }
            set { playerId_ = ProtoPreconditions.CheckNotNull(value, "value"); }
        }

        public const int StatsFieldNumber = 2;
        private static readonly FieldCodec<ClanMemberStat> _repeated_stats_codec = FieldCodec.ForMessage(18, ClanMemberStat.Parser);
        private readonly RepeatedField<ClanMemberStat> stats_ = new RepeatedField<ClanMemberStat>();
        public RepeatedField<ClanMemberStat> Stats {
            get { return stats_; }
        }

        public override bool Equals(object other) { return Equals(other as ClanMemberStats); }

        public bool Equals(ClanMemberStats other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (PlayerId != other.PlayerId) return false;
            if(!stats_.Equals(other.stats_)) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (PlayerId.Length != 0) hash ^= PlayerId.GetHashCode();
            hash ^= stats_.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (PlayerId.Length != 0) {
                output.WriteRawTag(10);
                output.WriteString(PlayerId);
            }
            stats_.WriteTo(output, _repeated_stats_codec);
        }

        public int CalculateSize() 
        {
            int size = 0;
            if (PlayerId.Length != 0) {
                size += 1 + CodedOutputStream.ComputeStringSize(PlayerId);
            }
            size += stats_.CalculateSize(_repeated_stats_codec);
            return size;
        }

        public void MergeFrom(ClanMemberStats other) 
        {
            if (other == null) return;
            if (other.PlayerId.Length != 0) {
                PlayerId = other.PlayerId;
            }
            stats_.Add(other.stats_);
            
        }

        public void MergeFrom(CodedInputStream input) 
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0) 
            {
                switch(tag) 
                {
                    default: input.SkipLastField();
                        break;
                    case 10: {
                        PlayerId = input.ReadString();
                        break;
                    }
                    case 18: {
                        stats_.AddEntriesFrom(input, _repeated_stats_codec);
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class GetCurrentClanMemberStatsRequest : IMessage<GetCurrentClanMemberStatsRequest>, IMessage, IEquatable<GetCurrentClanMemberStatsRequest>, IDeepCloneable<GetCurrentClanMemberStatsRequest>
    {
        private static readonly MessageParser<GetCurrentClanMemberStatsRequest> _parser = new MessageParser<GetCurrentClanMemberStatsRequest>(() => new GetCurrentClanMemberStatsRequest());
        

        public static MessageParser<GetCurrentClanMemberStatsRequest> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public GetCurrentClanMemberStatsRequest() {}

        public GetCurrentClanMemberStatsRequest(GetCurrentClanMemberStatsRequest other) : this() 
        {
            
        }

        public GetCurrentClanMemberStatsRequest Clone() { return new GetCurrentClanMemberStatsRequest(this); }

        public override bool Equals(object other) { return Equals(other as GetCurrentClanMemberStatsRequest); }

        public bool Equals(GetCurrentClanMemberStatsRequest other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
        }

        public int CalculateSize() 
        {
            int size = 0;
            return size;
        }

        public void MergeFrom(GetCurrentClanMemberStatsRequest other) 
        {
            if (other == null) return;
            
        }

        public void MergeFrom(CodedInputStream input) 
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0) 
            {
                switch(tag) 
                {
                    default: input.SkipLastField();
                        break;
                }
            }
        }
    }

    public sealed partial class GetCurrentClanMemberStatsResponse : IMessage<GetCurrentClanMemberStatsResponse>, IMessage, IEquatable<GetCurrentClanMemberStatsResponse>, IDeepCloneable<GetCurrentClanMemberStatsResponse>
    {
        private static readonly MessageParser<GetCurrentClanMemberStatsResponse> _parser = new MessageParser<GetCurrentClanMemberStatsResponse>(() => new GetCurrentClanMemberStatsResponse());
        

        public static MessageParser<GetCurrentClanMemberStatsResponse> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public GetCurrentClanMemberStatsResponse() 
        {
            clanId_ = "";
        }

        public GetCurrentClanMemberStatsResponse(GetCurrentClanMemberStatsResponse other) : this() 
        {
            clanId_ = other.clanId_;
            clanMemberStats_ = other.clanMemberStats_ != null ? other.clanMemberStats_.Clone() : null;
            
        }

        public GetCurrentClanMemberStatsResponse Clone() { return new GetCurrentClanMemberStatsResponse(this); }

        public const int ClanIdFieldNumber = 1;
        private string clanId_ = "";
        public string ClanId {
            get { return clanId_; }
            set { clanId_ = ProtoPreconditions.CheckNotNull(value, "value"); }
        }

        public const int ClanMemberStatsFieldNumber = 2;
        private ClanMemberStats clanMemberStats_;
        public ClanMemberStats ClanMemberStats {
            get { return clanMemberStats_; }
            set { clanMemberStats_ = value; }
        }

        public override bool Equals(object other) { return Equals(other as GetCurrentClanMemberStatsResponse); }

        public bool Equals(GetCurrentClanMemberStatsResponse other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (ClanId != other.ClanId) return false;
            if (!object.Equals(ClanMemberStats, other.ClanMemberStats)) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (ClanId.Length != 0) hash ^= ClanId.GetHashCode();
            if (clanMemberStats_ != null) hash ^= ClanMemberStats.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (ClanId.Length != 0) {
                output.WriteRawTag(10);
                output.WriteString(ClanId);
            }
            if (clanMemberStats_ != null) {
                output.WriteRawTag(18);
                output.WriteMessage(ClanMemberStats);
            }
        }

        public int CalculateSize() 
        {
            int size = 0;
            if (ClanId.Length != 0) {
                size += 1 + CodedOutputStream.ComputeStringSize(ClanId);
            }
            if (clanMemberStats_ != null) {
                size += 1 + CodedOutputStream.ComputeMessageSize(ClanMemberStats);
            }
            return size;
        }

        public void MergeFrom(GetCurrentClanMemberStatsResponse other) 
        {
            if (other == null) return;
            if (other.ClanId.Length != 0) {
                ClanId = other.ClanId;
            }
            if (other.clanMemberStats_ != null) {
                if (clanMemberStats_ == null) {
                    ClanMemberStats = new ClanMemberStats();
                }
                ClanMemberStats.MergeFrom(other.ClanMemberStats);
            }
            
        }

        public void MergeFrom(CodedInputStream input) 
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0) 
            {
                switch(tag) 
                {
                    default: input.SkipLastField();
                        break;
                    case 10: {
                        ClanId = input.ReadString();
                        break;
                    }
                    case 18: {
                        if (clanMemberStats_ == null) {
                            ClanMemberStats = new ClanMemberStats();
                        }
                        input.ReadMessage(ClanMemberStats);
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class GetClanMembersStatsRequest : IMessage<GetClanMembersStatsRequest>, IMessage, IEquatable<GetClanMembersStatsRequest>, IDeepCloneable<GetClanMembersStatsRequest>
    {
        private static readonly MessageParser<GetClanMembersStatsRequest> _parser = new MessageParser<GetClanMembersStatsRequest>(() => new GetClanMembersStatsRequest());
        

        public static MessageParser<GetClanMembersStatsRequest> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public GetClanMembersStatsRequest() 
        {
            clanId_ = "";
        }

        public GetClanMembersStatsRequest(GetClanMembersStatsRequest other) : this() 
        {
            clanId_ = other.clanId_;
            
        }

        public GetClanMembersStatsRequest Clone() { return new GetClanMembersStatsRequest(this); }

        public const int ClanIdFieldNumber = 1;
        private string clanId_ = "";
        public string ClanId {
            get { return clanId_; }
            set { clanId_ = ProtoPreconditions.CheckNotNull(value, "value"); }
        }

        public override bool Equals(object other) { return Equals(other as GetClanMembersStatsRequest); }

        public bool Equals(GetClanMembersStatsRequest other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (ClanId != other.ClanId) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (ClanId.Length != 0) hash ^= ClanId.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (ClanId.Length != 0) {
                output.WriteRawTag(10);
                output.WriteString(ClanId);
            }
        }

        public int CalculateSize() 
        {
            int size = 0;
            if (ClanId.Length != 0) {
                size += 1 + CodedOutputStream.ComputeStringSize(ClanId);
            }
            return size;
        }

        public void MergeFrom(GetClanMembersStatsRequest other) 
        {
            if (other == null) return;
            if (other.ClanId.Length != 0) {
                ClanId = other.ClanId;
            }
            
        }

        public void MergeFrom(CodedInputStream input) 
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0) 
            {
                switch(tag) 
                {
                    default: input.SkipLastField();
                        break;
                    case 10: {
                        ClanId = input.ReadString();
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class GetClanMembersStatsResponse : IMessage<GetClanMembersStatsResponse>, IMessage, IEquatable<GetClanMembersStatsResponse>, IDeepCloneable<GetClanMembersStatsResponse>
    {
        private static readonly MessageParser<GetClanMembersStatsResponse> _parser = new MessageParser<GetClanMembersStatsResponse>(() => new GetClanMembersStatsResponse());
        

        public static MessageParser<GetClanMembersStatsResponse> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public GetClanMembersStatsResponse() 
        {
            clanId_ = "";
        }

        public GetClanMembersStatsResponse(GetClanMembersStatsResponse other) : this() 
        {
            clanId_ = other.clanId_;
            clanMembersStats_ = other.clanMembersStats_.Clone();
            
        }

        public GetClanMembersStatsResponse Clone() { return new GetClanMembersStatsResponse(this); }

        public const int ClanIdFieldNumber = 1;
        private string clanId_ = "";
        public string ClanId {
            get { return clanId_; }
            set { clanId_ = ProtoPreconditions.CheckNotNull(value, "value"); }
        }

        public const int ClanMembersStatsFieldNumber = 2;
        private static readonly FieldCodec<ClanMemberStats> _repeated_clanMembersStats_codec = FieldCodec.ForMessage(18, Axlebolt.Bolt.Protobuf.ClanMemberStats.Parser);
        private readonly RepeatedField<ClanMemberStats> clanMembersStats_ = new RepeatedField<ClanMemberStats>();
        public RepeatedField<ClanMemberStats> ClanMembersStats {
            get { return clanMembersStats_; }
        }

        public override bool Equals(object other) { return Equals(other as GetClanMembersStatsResponse); }

        public bool Equals(GetClanMembersStatsResponse other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (ClanId != other.ClanId) return false;
            if(!clanMembersStats_.Equals(other.clanMembersStats_)) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (ClanId.Length != 0) hash ^= ClanId.GetHashCode();
            hash ^= clanMembersStats_.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (ClanId.Length != 0) {
                output.WriteRawTag(10);
                output.WriteString(ClanId);
            }
            clanMembersStats_.WriteTo(output, _repeated_clanMembersStats_codec);
        }

        public int CalculateSize() 
        {
            int size = 0;
            if (ClanId.Length != 0) {
                size += 1 + CodedOutputStream.ComputeStringSize(ClanId);
            }
            size += clanMembersStats_.CalculateSize(_repeated_clanMembersStats_codec);
            return size;
        }

        public void MergeFrom(GetClanMembersStatsResponse other) 
        {
            if (other == null) return;
            if (other.ClanId.Length != 0) {
                ClanId = other.ClanId;
            }
            clanMembersStats_.Add(other.clanMembersStats_);
            
        }

        public void MergeFrom(CodedInputStream input) 
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0) 
            {
                switch(tag) 
                {
                    default: input.SkipLastField();
                        break;
                    case 10: {
                        ClanId = input.ReadString();
                        break;
                    }
                    case 18: {
                        clanMembersStats_.AddEntriesFrom(input, _repeated_clanMembersStats_codec);
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class SaveCurrentClanMemberStatsRequest : IMessage<SaveCurrentClanMemberStatsRequest>, IMessage, IEquatable<SaveCurrentClanMemberStatsRequest>, IDeepCloneable<SaveCurrentClanMemberStatsRequest>
    {
        private static readonly MessageParser<SaveCurrentClanMemberStatsRequest> _parser = new MessageParser<SaveCurrentClanMemberStatsRequest>(() => new SaveCurrentClanMemberStatsRequest());
        

        public static MessageParser<SaveCurrentClanMemberStatsRequest> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public SaveCurrentClanMemberStatsRequest() {}

        public SaveCurrentClanMemberStatsRequest(SaveCurrentClanMemberStatsRequest other) : this() 
        {
            clanMemberStats_ = other.clanMemberStats_ != null ? other.clanMemberStats_.Clone() : null;
            
        }

        public SaveCurrentClanMemberStatsRequest Clone() { return new SaveCurrentClanMemberStatsRequest(this); }

        public const int ClanMemberStatsFieldNumber = 1;
        private ClanMemberStats clanMemberStats_;
        public ClanMemberStats ClanMemberStats {
            get { return clanMemberStats_; }
            set { clanMemberStats_ = value; }
        }

        public override bool Equals(object other) { return Equals(other as SaveCurrentClanMemberStatsRequest); }

        public bool Equals(SaveCurrentClanMemberStatsRequest other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (!object.Equals(ClanMemberStats, other.ClanMemberStats)) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (clanMemberStats_ != null) hash ^= ClanMemberStats.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (clanMemberStats_ != null) {
                output.WriteRawTag(10);
                output.WriteMessage(ClanMemberStats);
            }
        }

        public int CalculateSize() 
        {
            int size = 0;
            if (clanMemberStats_ != null) {
                size += 1 + CodedOutputStream.ComputeMessageSize(ClanMemberStats);
            }
            return size;
        }

        public void MergeFrom(SaveCurrentClanMemberStatsRequest other) 
        {
            if (other == null) return;
            if (other.clanMemberStats_ != null) {
                if (clanMemberStats_ == null) {
                    ClanMemberStats = new ClanMemberStats();
                }
                ClanMemberStats.MergeFrom(other.ClanMemberStats);
            }
            
        }

        public void MergeFrom(CodedInputStream input) 
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0) 
            {
                switch(tag) 
                {
                    default: input.SkipLastField();
                        break;
                    case 10: {
                        if (clanMemberStats_ == null) {
                            ClanMemberStats = new ClanMemberStats();
                        }
                        input.ReadMessage(ClanMemberStats);
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class SaveCurrentClanMemberStatsResponse : IMessage<SaveCurrentClanMemberStatsResponse>, IMessage, IEquatable<SaveCurrentClanMemberStatsResponse>, IDeepCloneable<SaveCurrentClanMemberStatsResponse>
    {
        private static readonly MessageParser<SaveCurrentClanMemberStatsResponse> _parser = new MessageParser<SaveCurrentClanMemberStatsResponse>(() => new SaveCurrentClanMemberStatsResponse());
        

        public static MessageParser<SaveCurrentClanMemberStatsResponse> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public SaveCurrentClanMemberStatsResponse() {}

        public SaveCurrentClanMemberStatsResponse(SaveCurrentClanMemberStatsResponse other) : this() 
        {
            
        }

        public SaveCurrentClanMemberStatsResponse Clone() { return new SaveCurrentClanMemberStatsResponse(this); }

        public override bool Equals(object other) { return Equals(other as SaveCurrentClanMemberStatsResponse); }

        public bool Equals(SaveCurrentClanMemberStatsResponse other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
        }

        public int CalculateSize() 
        {
            int size = 0;
            return size;
        }

        public void MergeFrom(SaveCurrentClanMemberStatsResponse other) 
        {
            if (other == null) return;
            
        }

        public void MergeFrom(CodedInputStream input) 
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0) 
            {
                switch(tag) 
                {
                    default: input.SkipLastField();
                        break;
                }
            }
        }
    }
}
