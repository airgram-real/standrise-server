using System;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using pbc = Google.Protobuf.Collections;

namespace Axlebolt.Bolt.Protobuf
{
    // Make sure we have StatDefType if not already defined elsewhere
    // Assuming StatDefType is an enum defined in another namespace or file we will resolve later.
    // Based on the reflection, here are the proto messages.

    public sealed partial class ClanStat : IMessage<ClanStat>, IMessage, IEquatable<ClanStat>, IDeepCloneable<ClanStat>
    {
        private static readonly MessageParser<ClanStat> _parser = new MessageParser<ClanStat>(() => new ClanStat());
        

        public static MessageParser<ClanStat> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } } // We don't have the full descriptor builder, but that's ok for basic serialization.
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public ClanStat() 
        {
            statId_ = "";
        }

        public ClanStat(ClanStat other) : this() 
        {
            type_ = other.type_;
            intValue_ = other.intValue_;
            floatValue_ = other.floatValue_;
            longValue_ = other.longValue_;
            statId_ = other.statId_;
            
        }

        public ClanStat Clone() { return new ClanStat(this); }

        public const int TypeFieldNumber = 1;
        private StatDefType type_ = 0;
        public StatDefType Type {
            get { return type_; }
            set { type_ = value; }
        }

        public const int IntValueFieldNumber = 2;
        private int intValue_;
        public int IntValue {
            get { return intValue_; }
            set { intValue_ = value; }
        }

        public const int FloatValueFieldNumber = 3;
        private float floatValue_;
        public float FloatValue {
            get { return floatValue_; }
            set { floatValue_ = value; }
        }

        public const int LongValueFieldNumber = 4;
        private long longValue_;
        public long LongValue {
            get { return longValue_; }
            set { longValue_ = value; }
        }

        public const int StatIdFieldNumber = 5;
        private string statId_ = "";
        public string StatId {
            get { return statId_; }
            set { statId_ = ProtoPreconditions.CheckNotNull(value, "value"); }
        }

        public override bool Equals(object other) { return Equals(other as ClanStat); }

        public bool Equals(ClanStat other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (Type != other.Type) return false;
            if (IntValue != other.IntValue) return false;
            if (FloatValue != other.FloatValue) return false;
            if (LongValue != other.LongValue) return false;
            if (StatId != other.StatId) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (Type != 0) hash ^= Type.GetHashCode();
            if (IntValue != 0) hash ^= IntValue.GetHashCode();
            if (FloatValue != 0F) hash ^= FloatValue.GetHashCode();
            if (LongValue != 0L) hash ^= LongValue.GetHashCode();
            if (StatId.Length != 0) hash ^= StatId.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (Type != 0) {
                output.WriteRawTag(8);
                output.WriteEnum((int) Type);
            }
            if (IntValue != 0) {
                output.WriteRawTag(16);
                output.WriteInt32(IntValue);
            }
            if (FloatValue != 0F) {
                output.WriteRawTag(29);
                output.WriteFloat(FloatValue);
            }
            if (LongValue != 0L) {
                output.WriteRawTag(32);
                output.WriteInt64(LongValue);
            }
            if (StatId.Length != 0) {
                output.WriteRawTag(42);
                output.WriteString(StatId);
            }
        }

        public int CalculateSize() 
        {
            int size = 0;
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
            if (StatId.Length != 0) {
                size += 1 + CodedOutputStream.ComputeStringSize(StatId);
            }
            return size;
        }

        public void MergeFrom(ClanStat other) 
        {
            if (other == null) return;
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
            if (other.StatId.Length != 0) {
                StatId = other.StatId;
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
                    case 8: {
                        Type = (StatDefType) input.ReadEnum();
                        break;
                    }
                    case 16: {
                        IntValue = input.ReadInt32();
                        break;
                    }
                    case 29: {
                        FloatValue = input.ReadFloat();
                        break;
                    }
                    case 32: {
                        LongValue = input.ReadInt64();
                        break;
                    }
                    case 42: {
                        StatId = input.ReadString();
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class ClanStats : IMessage<ClanStats>, IMessage, IEquatable<ClanStats>, IDeepCloneable<ClanStats>
    {
        private static readonly MessageParser<ClanStats> _parser = new MessageParser<ClanStats>(() => new ClanStats());
        

        public static MessageParser<ClanStats> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public ClanStats() 
        {
            clanId_ = "";
            seasonId_ = "";
        }

        public ClanStats(ClanStats other) : this() 
        {
            clanId_ = other.clanId_;
            stats_ = other.stats_.Clone();
            seasonId_ = other.seasonId_;
            
        }

        public ClanStats Clone() { return new ClanStats(this); }

        public const int ClanIdFieldNumber = 1;
        private string clanId_ = "";
        public string ClanId {
            get { return clanId_; }
            set { clanId_ = ProtoPreconditions.CheckNotNull(value, "value"); }
        }

        public const int StatsFieldNumber = 2;
        private static readonly FieldCodec<ClanStat> _repeated_stats_codec = FieldCodec.ForMessage(18, ClanStat.Parser);
        private readonly RepeatedField<ClanStat> stats_ = new RepeatedField<ClanStat>();
        public RepeatedField<ClanStat> Stats {
            get { return stats_; }
        }

        public const int SeasonIdFieldNumber = 3;
        private string seasonId_ = "";
        public string SeasonId {
            get { return seasonId_; }
            set { seasonId_ = ProtoPreconditions.CheckNotNull(value, "value"); }
        }

        public override bool Equals(object other) { return Equals(other as ClanStats); }

        public bool Equals(ClanStats other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (ClanId != other.ClanId) return false;
            if(!stats_.Equals(other.stats_)) return false;
            if (SeasonId != other.SeasonId) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (ClanId.Length != 0) hash ^= ClanId.GetHashCode();
            hash ^= stats_.GetHashCode();
            if (SeasonId.Length != 0) hash ^= SeasonId.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (ClanId.Length != 0) {
                output.WriteRawTag(10);
                output.WriteString(ClanId);
            }
            stats_.WriteTo(output, _repeated_stats_codec);
            if (SeasonId.Length != 0) {
                output.WriteRawTag(26);
                output.WriteString(SeasonId);
            }
        }

        public int CalculateSize() 
        {
            int size = 0;
            if (ClanId.Length != 0) {
                size += 1 + CodedOutputStream.ComputeStringSize(ClanId);
            }
            size += stats_.CalculateSize(_repeated_stats_codec);
            if (SeasonId.Length != 0) {
                size += 1 + CodedOutputStream.ComputeStringSize(SeasonId);
            }
            return size;
        }

        public void MergeFrom(ClanStats other) 
        {
            if (other == null) return;
            if (other.ClanId.Length != 0) {
                ClanId = other.ClanId;
            }
            stats_.Add(other.stats_);
            if (other.SeasonId.Length != 0) {
                SeasonId = other.SeasonId;
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
                        stats_.AddEntriesFrom(input, _repeated_stats_codec);
                        break;
                    }
                    case 26: {
                        SeasonId = input.ReadString();
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class ClanStatsMap : IMessage<ClanStatsMap>, IMessage, IEquatable<ClanStatsMap>, IDeepCloneable<ClanStatsMap>
    {
        private static readonly MessageParser<ClanStatsMap> _parser = new MessageParser<ClanStatsMap>(() => new ClanStatsMap());
        

        public static MessageParser<ClanStatsMap> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public ClanStatsMap() {}

        public ClanStatsMap(ClanStatsMap other) : this() 
        {
            stats_ = other.stats_.Clone();
            
        }

        public ClanStatsMap Clone() { return new ClanStatsMap(this); }

        public const int StatsFieldNumber = 1;
        private static readonly MapField<string, ClanStat>.Codec _dict_stats_codec = new MapField<string, ClanStat>.Codec(FieldCodec.ForString(10), FieldCodec.ForMessage(18, ClanStat.Parser), 10);
        private readonly MapField<string, ClanStat> stats_ = new MapField<string, ClanStat>();
        public MapField<string, ClanStat> Stats {
            get { return stats_; }
        }

        public override bool Equals(object other) { return Equals(other as ClanStatsMap); }

        public bool Equals(ClanStatsMap other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (!Stats.Equals(other.Stats)) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            hash ^= Stats.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            stats_.WriteTo(output, _dict_stats_codec);
        }

        public int CalculateSize() 
        {
            int size = 0;
            size += stats_.CalculateSize(_dict_stats_codec);
            return size;
        }

        public void MergeFrom(ClanStatsMap other) 
        {
            if (other == null) return;
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
                        stats_.AddEntriesFrom(input, _dict_stats_codec);
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class GetCurrentClanStatsRequest : IMessage<GetCurrentClanStatsRequest>, IMessage, IEquatable<GetCurrentClanStatsRequest>, IDeepCloneable<GetCurrentClanStatsRequest>
    {
        private static readonly MessageParser<GetCurrentClanStatsRequest> _parser = new MessageParser<GetCurrentClanStatsRequest>(() => new GetCurrentClanStatsRequest());
        

        public static MessageParser<GetCurrentClanStatsRequest> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public GetCurrentClanStatsRequest() {}

        public GetCurrentClanStatsRequest(GetCurrentClanStatsRequest other) : this() 
        {
            addLeaderboardStats_ = other.addLeaderboardStats_;
            
        }

        public GetCurrentClanStatsRequest Clone() { return new GetCurrentClanStatsRequest(this); }

        public const int AddLeaderboardStatsFieldNumber = 1;
        private bool addLeaderboardStats_;
        public bool AddLeaderboardStats {
            get { return addLeaderboardStats_; }
            set { addLeaderboardStats_ = value; }
        }

        public override bool Equals(object other) { return Equals(other as GetCurrentClanStatsRequest); }

        public bool Equals(GetCurrentClanStatsRequest other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (AddLeaderboardStats != other.AddLeaderboardStats) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (AddLeaderboardStats != false) hash ^= AddLeaderboardStats.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (AddLeaderboardStats != false) {
                output.WriteRawTag(8);
                output.WriteBool(AddLeaderboardStats);
            }
        }

        public int CalculateSize() 
        {
            int size = 0;
            if (AddLeaderboardStats != false) {
                size += 1 + 1;
            }
            return size;
        }

        public void MergeFrom(GetCurrentClanStatsRequest other) 
        {
            if (other == null) return;
            if (other.AddLeaderboardStats != false) {
                AddLeaderboardStats = other.AddLeaderboardStats;
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
                    case 8: {
                        AddLeaderboardStats = input.ReadBool();
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class GetCurrentClanStatsResponse : IMessage<GetCurrentClanStatsResponse>, IMessage, IEquatable<GetCurrentClanStatsResponse>, IDeepCloneable<GetCurrentClanStatsResponse>
    {
        private static readonly MessageParser<GetCurrentClanStatsResponse> _parser = new MessageParser<GetCurrentClanStatsResponse>(() => new GetCurrentClanStatsResponse());
        

        public static MessageParser<GetCurrentClanStatsResponse> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public GetCurrentClanStatsResponse() 
        {
            clanId_ = "";
        }

        public GetCurrentClanStatsResponse(GetCurrentClanStatsResponse other) : this() 
        {
            clanId_ = other.clanId_;
            stats_ = other.stats_ != null ? other.stats_.Clone() : null;
            clanStats_ = other.clanStats_ != null ? other.clanStats_.Clone() : null;
            clanMemberStats_ = other.clanMemberStats_.Clone();
            
        }

        public GetCurrentClanStatsResponse Clone() { return new GetCurrentClanStatsResponse(this); }

        public const int ClanIdFieldNumber = 1;
        private string clanId_ = "";
        public string ClanId {
            get { return clanId_; }
            set { clanId_ = ProtoPreconditions.CheckNotNull(value, "value"); }
        }

        public const int StatsFieldNumber = 2;
        private ClanStatsMap stats_;
        public ClanStatsMap Stats {
            get { return stats_; }
            set { stats_ = value; }
        }

        public const int ClanStatsFieldNumber = 3;
        private ClanStats clanStats_;
        public ClanStats ClanStats {
            get { return clanStats_; }
            set { clanStats_ = value; }
        }

        public const int ClanMemberStatsFieldNumber = 4;
        private static readonly FieldCodec<ClanMemberStats> _repeated_clanMemberStats_codec = FieldCodec.ForMessage(34, Axlebolt.Bolt.Protobuf.ClanMemberStats.Parser);
        private readonly RepeatedField<ClanMemberStats> clanMemberStats_ = new RepeatedField<ClanMemberStats>();
        public RepeatedField<ClanMemberStats> ClanMemberStats {
            get { return clanMemberStats_; }
        }

        public override bool Equals(object other) { return Equals(other as GetCurrentClanStatsResponse); }

        public bool Equals(GetCurrentClanStatsResponse other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (ClanId != other.ClanId) return false;
            if (!object.Equals(Stats, other.Stats)) return false;
            if (!object.Equals(ClanStats, other.ClanStats)) return false;
            if(!clanMemberStats_.Equals(other.clanMemberStats_)) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (ClanId.Length != 0) hash ^= ClanId.GetHashCode();
            if (stats_ != null) hash ^= Stats.GetHashCode();
            if (clanStats_ != null) hash ^= ClanStats.GetHashCode();
            hash ^= clanMemberStats_.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (ClanId.Length != 0) {
                output.WriteRawTag(10);
                output.WriteString(ClanId);
            }
            if (stats_ != null) {
                output.WriteRawTag(18);
                output.WriteMessage(Stats);
            }
            if (clanStats_ != null) {
                output.WriteRawTag(26);
                output.WriteMessage(ClanStats);
            }
            clanMemberStats_.WriteTo(output, _repeated_clanMemberStats_codec);
        }

        public int CalculateSize() 
        {
            int size = 0;
            if (ClanId.Length != 0) {
                size += 1 + CodedOutputStream.ComputeStringSize(ClanId);
            }
            if (stats_ != null) {
                size += 1 + CodedOutputStream.ComputeMessageSize(Stats);
            }
            if (clanStats_ != null) {
                size += 1 + CodedOutputStream.ComputeMessageSize(ClanStats);
            }
            size += clanMemberStats_.CalculateSize(_repeated_clanMemberStats_codec);
            return size;
        }

        public void MergeFrom(GetCurrentClanStatsResponse other) 
        {
            if (other == null) return;
            if (other.ClanId.Length != 0) {
                ClanId = other.ClanId;
            }
            if (other.stats_ != null) {
                if (stats_ == null) {
                    Stats = new ClanStatsMap();
                }
                Stats.MergeFrom(other.Stats);
            }
            if (other.clanStats_ != null) {
                if (clanStats_ == null) {
                    ClanStats = new ClanStats();
                }
                ClanStats.MergeFrom(other.ClanStats);
            }
            clanMemberStats_.Add(other.clanMemberStats_);
            
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
                        if (stats_ == null) {
                            Stats = new ClanStatsMap();
                        }
                        input.ReadMessage(Stats);
                        break;
                    }
                    case 26: {
                        if (clanStats_ == null) {
                            ClanStats = new ClanStats();
                        }
                        input.ReadMessage(ClanStats);
                        break;
                    }
                    case 34: {
                        clanMemberStats_.AddEntriesFrom(input, _repeated_clanMemberStats_codec);
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class GetClanStatsRequest : IMessage<GetClanStatsRequest>, IMessage, IEquatable<GetClanStatsRequest>, IDeepCloneable<GetClanStatsRequest>
    {
        private static readonly MessageParser<GetClanStatsRequest> _parser = new MessageParser<GetClanStatsRequest>(() => new GetClanStatsRequest());
        

        public static MessageParser<GetClanStatsRequest> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public GetClanStatsRequest() 
        {
            clanId_ = "";
        }

        public GetClanStatsRequest(GetClanStatsRequest other) : this() 
        {
            clanId_ = other.clanId_;
            addLeaderboardStats_ = other.addLeaderboardStats_;
            
        }

        public GetClanStatsRequest Clone() { return new GetClanStatsRequest(this); }

        public const int ClanIdFieldNumber = 1;
        private string clanId_ = "";
        public string ClanId {
            get { return clanId_; }
            set { clanId_ = ProtoPreconditions.CheckNotNull(value, "value"); }
        }

        public const int AddLeaderboardStatsFieldNumber = 2;
        private bool addLeaderboardStats_;
        public bool AddLeaderboardStats {
            get { return addLeaderboardStats_; }
            set { addLeaderboardStats_ = value; }
        }

        public override bool Equals(object other) { return Equals(other as GetClanStatsRequest); }

        public bool Equals(GetClanStatsRequest other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (ClanId != other.ClanId) return false;
            if (AddLeaderboardStats != other.AddLeaderboardStats) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (ClanId.Length != 0) hash ^= ClanId.GetHashCode();
            if (AddLeaderboardStats != false) hash ^= AddLeaderboardStats.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (ClanId.Length != 0) {
                output.WriteRawTag(10);
                output.WriteString(ClanId);
            }
            if (AddLeaderboardStats != false) {
                output.WriteRawTag(16);
                output.WriteBool(AddLeaderboardStats);
            }
        }

        public int CalculateSize() 
        {
            int size = 0;
            if (ClanId.Length != 0) {
                size += 1 + CodedOutputStream.ComputeStringSize(ClanId);
            }
            if (AddLeaderboardStats != false) {
                size += 1 + 1;
            }
            return size;
        }

        public void MergeFrom(GetClanStatsRequest other) 
        {
            if (other == null) return;
            if (other.ClanId.Length != 0) {
                ClanId = other.ClanId;
            }
            if (other.AddLeaderboardStats != false) {
                AddLeaderboardStats = other.AddLeaderboardStats;
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
                    case 16: {
                        AddLeaderboardStats = input.ReadBool();
                        break;
                    }
                }
            }
        }
    }

    public sealed partial class GetClanStatsResponse : IMessage<GetClanStatsResponse>, IMessage, IEquatable<GetClanStatsResponse>, IDeepCloneable<GetClanStatsResponse>
    {
        private static readonly MessageParser<GetClanStatsResponse> _parser = new MessageParser<GetClanStatsResponse>(() => new GetClanStatsResponse());
        

        public static MessageParser<GetClanStatsResponse> Parser { get { return _parser; } }
        public static MessageDescriptor Descriptor { get { return null; } }
        MessageDescriptor IMessage.Descriptor { get { return Descriptor; } }

        public GetClanStatsResponse() 
        {
            clanId_ = "";
        }

        public GetClanStatsResponse(GetClanStatsResponse other) : this() 
        {
            clanId_ = other.clanId_;
            stats_ = other.stats_ != null ? other.stats_.Clone() : null;
            clanStats_ = other.clanStats_ != null ? other.clanStats_.Clone() : null;
            clanMemberStats_ = other.clanMemberStats_.Clone();
            
        }

        public GetClanStatsResponse Clone() { return new GetClanStatsResponse(this); }

        public const int ClanIdFieldNumber = 1;
        private string clanId_ = "";
        public string ClanId {
            get { return clanId_; }
            set { clanId_ = ProtoPreconditions.CheckNotNull(value, "value"); }
        }

        public const int StatsFieldNumber = 2;
        private ClanStatsMap stats_;
        public ClanStatsMap Stats {
            get { return stats_; }
            set { stats_ = value; }
        }

        public const int ClanStatsFieldNumber = 3;
        private ClanStats clanStats_;
        public ClanStats ClanStats {
            get { return clanStats_; }
            set { clanStats_ = value; }
        }

        public const int ClanMemberStatsFieldNumber = 4;
        private static readonly FieldCodec<ClanMemberStats> _repeated_clanMemberStats_codec = FieldCodec.ForMessage(34, Axlebolt.Bolt.Protobuf.ClanMemberStats.Parser);
        private readonly RepeatedField<ClanMemberStats> clanMemberStats_ = new RepeatedField<ClanMemberStats>();
        public RepeatedField<ClanMemberStats> ClanMemberStats {
            get { return clanMemberStats_; }
        }

        public override bool Equals(object other) { return Equals(other as GetClanStatsResponse); }

        public bool Equals(GetClanStatsResponse other) 
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (ClanId != other.ClanId) return false;
            if (!object.Equals(Stats, other.Stats)) return false;
            if (!object.Equals(ClanStats, other.ClanStats)) return false;
            if(!clanMemberStats_.Equals(other.clanMemberStats_)) return false;
            return true;
        }

        public override int GetHashCode() 
        {
            int hash = 1;
            if (ClanId.Length != 0) hash ^= ClanId.GetHashCode();
            if (stats_ != null) hash ^= Stats.GetHashCode();
            if (clanStats_ != null) hash ^= ClanStats.GetHashCode();
            hash ^= clanMemberStats_.GetHashCode();
            
            return hash;
        }

        public override string ToString() { return JsonFormatter.ToDiagnosticString(this); }

        public void WriteTo(CodedOutputStream output) 
        {
            if (ClanId.Length != 0) {
                output.WriteRawTag(10);
                output.WriteString(ClanId);
            }
            if (stats_ != null) {
                output.WriteRawTag(18);
                output.WriteMessage(Stats);
            }
            if (clanStats_ != null) {
                output.WriteRawTag(26);
                output.WriteMessage(ClanStats);
            }
            clanMemberStats_.WriteTo(output, _repeated_clanMemberStats_codec);
        }

        public int CalculateSize() 
        {
            int size = 0;
            if (ClanId.Length != 0) {
                size += 1 + CodedOutputStream.ComputeStringSize(ClanId);
            }
            if (stats_ != null) {
                size += 1 + CodedOutputStream.ComputeMessageSize(Stats);
            }
            if (clanStats_ != null) {
                size += 1 + CodedOutputStream.ComputeMessageSize(ClanStats);
            }
            size += clanMemberStats_.CalculateSize(_repeated_clanMemberStats_codec);
            return size;
        }

        public void MergeFrom(GetClanStatsResponse other) 
        {
            if (other == null) return;
            if (other.ClanId.Length != 0) {
                ClanId = other.ClanId;
            }
            if (other.stats_ != null) {
                if (stats_ == null) {
                    Stats = new ClanStatsMap();
                }
                Stats.MergeFrom(other.Stats);
            }
            if (other.clanStats_ != null) {
                if (clanStats_ == null) {
                    ClanStats = new ClanStats();
                }
                ClanStats.MergeFrom(other.ClanStats);
            }
            clanMemberStats_.Add(other.clanMemberStats_);
            
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
                        if (stats_ == null) {
                            Stats = new ClanStatsMap();
                        }
                        input.ReadMessage(Stats);
                        break;
                    }
                    case 26: {
                        if (clanStats_ == null) {
                            ClanStats = new ClanStats();
                        }
                        input.ReadMessage(ClanStats);
                        break;
                    }
                    case 34: {
                        clanMemberStats_.AddEntriesFrom(input, _repeated_clanMemberStats_codec);
                        break;
                    }
                }
            }
        }
    }
}
