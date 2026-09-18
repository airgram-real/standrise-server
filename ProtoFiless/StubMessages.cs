// Stub protobuf message types needed by various RemoteServices.

// These are minimal IMessage implementations to satisfy compilation.

#pragma warning disable 1591, 0612, 3021



using pb = global::Google.Protobuf;

using pbc = global::Google.Protobuf.Collections;

using pbr = global::Google.Protobuf.Reflection;

using scg = global::System.Collections.Generic;



namespace Axlebolt.Bolt.Protobuf

{

    // ============ PlayerStatsRemoteService ============



    public sealed partial class GetCurrentStatsRequest : pb::IMessage<GetCurrentStatsRequest>

    {

        private static readonly pb::MessageParser<GetCurrentStatsRequest> _parser =

            new pb::MessageParser<GetCurrentStatsRequest>(() => new GetCurrentStatsRequest());

        public static pb::MessageParser<GetCurrentStatsRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private bool addLeaderboardStats_;

        public bool AddLeaderboardStats

        {

            get => addLeaderboardStats_;

            set => addLeaderboardStats_ = value;

        }



        public GetCurrentStatsRequest() { }

        public GetCurrentStatsRequest(GetCurrentStatsRequest other) : this() { addLeaderboardStats_ = other.addLeaderboardStats_; }

        public GetCurrentStatsRequest Clone() => new GetCurrentStatsRequest(this);

        public override bool Equals(object other) => Equals(other as GetCurrentStatsRequest);

        public bool Equals(GetCurrentStatsRequest other) => other != null && AddLeaderboardStats == other.AddLeaderboardStats;

        public override int GetHashCode() => AddLeaderboardStats ? 1 : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (AddLeaderboardStats)

            {

                output.WriteRawTag(8);

                output.WriteBool(AddLeaderboardStats);

            }

        }

        public int CalculateSize() => AddLeaderboardStats ? 1 + 1 : 0;

        public void MergeFrom(GetCurrentStatsRequest other)

        {

            if (other == null) return;

            if (other.AddLeaderboardStats) AddLeaderboardStats = other.AddLeaderboardStats;

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                if (tag == 8) AddLeaderboardStats = input.ReadBool();

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class GetCurrentStatsResponse : pb::IMessage<GetCurrentStatsResponse>

    {

        private static readonly pb::MessageParser<GetCurrentStatsResponse> _parser =

            new pb::MessageParser<GetCurrentStatsResponse>(() => new GetCurrentStatsResponse());

        public static pb::MessageParser<GetCurrentStatsResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private global::Axlebolt.Bolt.Protobuf.Stats stats_;

        public global::Axlebolt.Bolt.Protobuf.Stats Stats

        {

            get => stats_;

            set => stats_ = value;

        }



        public GetCurrentStatsResponse() { }

        public GetCurrentStatsResponse(GetCurrentStatsResponse other) : this()

        {

            stats_ = other.stats_ != null ? other.stats_.Clone() : null;

        }

        public GetCurrentStatsResponse Clone() => new GetCurrentStatsResponse(this);

        public override bool Equals(object other) => Equals(other as GetCurrentStatsResponse);

        public bool Equals(GetCurrentStatsResponse other) => other != null && object.Equals(Stats, other.Stats);

        public override int GetHashCode() => stats_ != null ? Stats.GetHashCode() : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (stats_ != null)

            {

                output.WriteRawTag(10);

                output.WriteMessage(Stats);

            }

        }

        public int CalculateSize()

        {

            int size = 0;

            if (stats_ != null) size += 1 + pb::CodedOutputStream.ComputeMessageSize(Stats);

            return size;

        }

        public void MergeFrom(GetCurrentStatsResponse other)

        {

            if (other == null) return;

            if (other.stats_ != null)

            {

                if (stats_ == null) Stats = new global::Axlebolt.Bolt.Protobuf.Stats();

                Stats.MergeFrom(other.Stats);

            }

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                if (tag == 10)

                {

                    if (stats_ == null) Stats = new global::Axlebolt.Bolt.Protobuf.Stats();

                    input.ReadMessage(Stats);

                }

                else input.SkipLastField();

            }

        }

    }



    // ============ SeasonalStatsRemoteService ============



    public sealed partial class GetStatsForSeasonRequest : pb::IMessage<GetStatsForSeasonRequest>

    {

        private static readonly pb::MessageParser<GetStatsForSeasonRequest> _parser =

            new pb::MessageParser<GetStatsForSeasonRequest>(() => new GetStatsForSeasonRequest());

        public static pb::MessageParser<GetStatsForSeasonRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private string seasonId_ = "";

        public string SeasonId

        {

            get => seasonId_;

            set => seasonId_ = pb::ProtoPreconditions.CheckNotNull(value, "value");

        }



        public GetStatsForSeasonRequest() { }

        public GetStatsForSeasonRequest(GetStatsForSeasonRequest other) : this() { seasonId_ = other.seasonId_; }

        public GetStatsForSeasonRequest Clone() => new GetStatsForSeasonRequest(this);

        public override bool Equals(object other) => Equals(other as GetStatsForSeasonRequest);

        public bool Equals(GetStatsForSeasonRequest other) => other != null && SeasonId == other.SeasonId;

        public override int GetHashCode() => SeasonId.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (SeasonId.Length != 0)

            {

                output.WriteRawTag(10);

                output.WriteString(SeasonId);

            }

        }

        public int CalculateSize() => SeasonId.Length == 0 ? 0 : 1 + pb::CodedOutputStream.ComputeStringSize(SeasonId);

        public void MergeFrom(GetStatsForSeasonRequest other)

        {

            if (other == null) return;

            if (other.SeasonId.Length != 0) SeasonId = other.SeasonId;

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                if (tag == 10) SeasonId = input.ReadString();

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class GetPlayerStatsForSeasonRequest : pb::IMessage<GetPlayerStatsForSeasonRequest>

    {

        private static readonly pb::MessageParser<GetPlayerStatsForSeasonRequest> _parser =

            new pb::MessageParser<GetPlayerStatsForSeasonRequest>(() => new GetPlayerStatsForSeasonRequest());

        public static pb::MessageParser<GetPlayerStatsForSeasonRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private string seasonId_ = "";

        private string playerId_ = "";



        public string SeasonId

        {

            get => seasonId_;

            set => seasonId_ = pb::ProtoPreconditions.CheckNotNull(value, "value");

        }



        public string PlayerId

        {

            get => playerId_;

            set => playerId_ = pb::ProtoPreconditions.CheckNotNull(value, "value");

        }



        public GetPlayerStatsForSeasonRequest() { }

        public GetPlayerStatsForSeasonRequest(GetPlayerStatsForSeasonRequest other) : this()

        {

            seasonId_ = other.seasonId_;

            playerId_ = other.playerId_;

        }

        public GetPlayerStatsForSeasonRequest Clone() => new GetPlayerStatsForSeasonRequest(this);

        public override bool Equals(object other) => Equals(other as GetPlayerStatsForSeasonRequest);

        public bool Equals(GetPlayerStatsForSeasonRequest other)

            => other != null && SeasonId == other.SeasonId && PlayerId == other.PlayerId;

        public override int GetHashCode()

        {

            int hash = 1;

            if (SeasonId.Length != 0) hash ^= SeasonId.GetHashCode();

            if (PlayerId.Length != 0) hash ^= PlayerId.GetHashCode();

            return hash;

        }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (SeasonId.Length != 0)

            {

                output.WriteRawTag(10);

                output.WriteString(SeasonId);

            }

            if (PlayerId.Length != 0)

            {

                output.WriteRawTag(18);

                output.WriteString(PlayerId);

            }

        }

        public int CalculateSize()

        {

            int size = 0;

            if (SeasonId.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(SeasonId);

            if (PlayerId.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(PlayerId);

            return size;

        }

        public void MergeFrom(GetPlayerStatsForSeasonRequest other)

        {

            if (other == null) return;

            if (other.SeasonId.Length != 0) SeasonId = other.SeasonId;

            if (other.PlayerId.Length != 0) PlayerId = other.PlayerId;

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10: SeasonId = input.ReadString(); break;

                    case 18: PlayerId = input.ReadString(); break;

                    default: input.SkipLastField(); break;

                }

            }

        }

    }



    public sealed partial class GetCurrentClanStatsForSeasonRequest : pb::IMessage<GetCurrentClanStatsForSeasonRequest>

    {

        private static readonly pb::MessageParser<GetCurrentClanStatsForSeasonRequest> _parser =

            new pb::MessageParser<GetCurrentClanStatsForSeasonRequest>(() => new GetCurrentClanStatsForSeasonRequest());

        public static pb::MessageParser<GetCurrentClanStatsForSeasonRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private string seasonId_ = "";

        public string SeasonId

        {

            get => seasonId_;

            set => seasonId_ = pb::ProtoPreconditions.CheckNotNull(value, "value");

        }



        public GetCurrentClanStatsForSeasonRequest() { }

        public GetCurrentClanStatsForSeasonRequest(GetCurrentClanStatsForSeasonRequest other) : this() { seasonId_ = other.seasonId_; }

        public GetCurrentClanStatsForSeasonRequest Clone() => new GetCurrentClanStatsForSeasonRequest(this);

        public override bool Equals(object other) => Equals(other as GetCurrentClanStatsForSeasonRequest);

        public bool Equals(GetCurrentClanStatsForSeasonRequest other) => other != null && SeasonId == other.SeasonId;

        public override int GetHashCode() => SeasonId.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (SeasonId.Length != 0)

            {

                output.WriteRawTag(10);

                output.WriteString(SeasonId);

            }

        }

        public int CalculateSize() => SeasonId.Length == 0 ? 0 : 1 + pb::CodedOutputStream.ComputeStringSize(SeasonId);

        public void MergeFrom(GetCurrentClanStatsForSeasonRequest other)

        {

            if (other == null) return;

            if (other.SeasonId.Length != 0) SeasonId = other.SeasonId;

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                if (tag == 10) SeasonId = input.ReadString();

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class GetClanStatsForSeasonRequest : pb::IMessage<GetClanStatsForSeasonRequest>

    {

        private static readonly pb::MessageParser<GetClanStatsForSeasonRequest> _parser =

            new pb::MessageParser<GetClanStatsForSeasonRequest>(() => new GetClanStatsForSeasonRequest());

        public static pb::MessageParser<GetClanStatsForSeasonRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private string seasonId_ = "";

        private string clanId_ = "";



        public string SeasonId

        {

            get => seasonId_;

            set => seasonId_ = pb::ProtoPreconditions.CheckNotNull(value, "value");

        }



        public string ClanId

        {

            get => clanId_;

            set => clanId_ = pb::ProtoPreconditions.CheckNotNull(value, "value");

        }



        public GetClanStatsForSeasonRequest() { }

        public GetClanStatsForSeasonRequest(GetClanStatsForSeasonRequest other) : this()

        {

            seasonId_ = other.seasonId_;

            clanId_ = other.clanId_;

        }

        public GetClanStatsForSeasonRequest Clone() => new GetClanStatsForSeasonRequest(this);

        public override bool Equals(object other) => Equals(other as GetClanStatsForSeasonRequest);

        public bool Equals(GetClanStatsForSeasonRequest other)

            => other != null && SeasonId == other.SeasonId && ClanId == other.ClanId;

        public override int GetHashCode()

        {

            int hash = 1;

            if (SeasonId.Length != 0) hash ^= SeasonId.GetHashCode();

            if (ClanId.Length != 0) hash ^= ClanId.GetHashCode();

            return hash;

        }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (SeasonId.Length != 0)

            {

                output.WriteRawTag(10);

                output.WriteString(SeasonId);

            }

            if (ClanId.Length != 0)

            {

                output.WriteRawTag(18);

                output.WriteString(ClanId);

            }

        }

        public int CalculateSize()

        {

            int size = 0;

            if (SeasonId.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(SeasonId);

            if (ClanId.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(ClanId);

            return size;

        }

        public void MergeFrom(GetClanStatsForSeasonRequest other)

        {

            if (other == null) return;

            if (other.SeasonId.Length != 0) SeasonId = other.SeasonId;

            if (other.ClanId.Length != 0) ClanId = other.ClanId;

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10: SeasonId = input.ReadString(); break;

                    case 18: ClanId = input.ReadString(); break;

                    default: input.SkipLastField(); break;

                }

            }

        }

    }



    public sealed partial class GetStatsForSeasonResponse : pb::IMessage<GetStatsForSeasonResponse> {

        private static readonly pb::MessageParser<GetStatsForSeasonResponse> _parser = new pb::MessageParser<GetStatsForSeasonResponse>(() => new GetStatsForSeasonResponse());

        public static pb::MessageParser<GetStatsForSeasonResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.PlayerStat> _repeated_stat_codec =

            pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.PlayerStat.Parser);

        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.PlayerStat> stat_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.PlayerStat>();

        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.PlayerStat> Stat => stat_;



        public GetStatsForSeasonResponse() {}

        public GetStatsForSeasonResponse(GetStatsForSeasonResponse other) : this() { stat_.Add(other.stat_); }

        public GetStatsForSeasonResponse Clone() => new GetStatsForSeasonResponse(this);

        public override bool Equals(object other) => Equals(other as GetStatsForSeasonResponse);

        public bool Equals(GetStatsForSeasonResponse other) => other != null && stat_.Equals(other.stat_);

        public override int GetHashCode() => stat_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            stat_.WriteTo(output, _repeated_stat_codec);

        }

        public int CalculateSize() => stat_.CalculateSize(_repeated_stat_codec);

        public void MergeFrom(GetStatsForSeasonResponse other)

        {

            if (other == null) return;

            stat_.Add(other.stat_);

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                if (tag == 10) stat_.AddEntriesFrom(input, _repeated_stat_codec);

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class GetPlayerStatsForSeasonResponse : pb::IMessage<GetPlayerStatsForSeasonResponse> {

        private static readonly pb::MessageParser<GetPlayerStatsForSeasonResponse> _parser = new pb::MessageParser<GetPlayerStatsForSeasonResponse>(() => new GetPlayerStatsForSeasonResponse());

        public static pb::MessageParser<GetPlayerStatsForSeasonResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.PlayerStat> _repeated_stat_codec =

            pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.PlayerStat.Parser);

        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.PlayerStat> stat_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.PlayerStat>();

        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.PlayerStat> Stat => stat_;



        public GetPlayerStatsForSeasonResponse() {}

        public GetPlayerStatsForSeasonResponse(GetPlayerStatsForSeasonResponse other) : this() { stat_.Add(other.stat_); }

        public GetPlayerStatsForSeasonResponse Clone() => new GetPlayerStatsForSeasonResponse(this);

        public override bool Equals(object other) => Equals(other as GetPlayerStatsForSeasonResponse);

        public bool Equals(GetPlayerStatsForSeasonResponse other) => other != null && stat_.Equals(other.stat_);

        public override int GetHashCode() => stat_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            stat_.WriteTo(output, _repeated_stat_codec);

        }

        public int CalculateSize() => stat_.CalculateSize(_repeated_stat_codec);

        public void MergeFrom(GetPlayerStatsForSeasonResponse other)

        {

            if (other == null) return;

            stat_.Add(other.stat_);

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                if (tag == 10) stat_.AddEntriesFrom(input, _repeated_stat_codec);

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class GetCurrentClanStatsForSeasonResponse : pb::IMessage<GetCurrentClanStatsForSeasonResponse> {

        private static readonly pb::MessageParser<GetCurrentClanStatsForSeasonResponse> _parser = new pb::MessageParser<GetCurrentClanStatsForSeasonResponse>(() => new GetCurrentClanStatsForSeasonResponse());

        public static pb::MessageParser<GetCurrentClanStatsForSeasonResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private global::Axlebolt.Bolt.Protobuf.ClanStats clanStats_;

        public global::Axlebolt.Bolt.Protobuf.ClanStats ClanStats {

            get { return clanStats_; }

            set { clanStats_ = value; }

        }



        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.ClanMemberStats> _repeated_clanMemberStats_codec = pb::FieldCodec.ForMessage(18, global::Axlebolt.Bolt.Protobuf.ClanMemberStats.Parser);

        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanMemberStats> clanMemberStats_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanMemberStats>();

        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanMemberStats> ClanMemberStats {

            get { return clanMemberStats_; }

        }



        public GetCurrentClanStatsForSeasonResponse() {}

        public GetCurrentClanStatsForSeasonResponse(GetCurrentClanStatsForSeasonResponse other) : this() {

            clanStats_ = other.clanStats_ != null ? other.clanStats_.Clone() : null;

            clanMemberStats_.Add(other.clanMemberStats_);

        }

        public GetCurrentClanStatsForSeasonResponse Clone() => new GetCurrentClanStatsForSeasonResponse(this);

        public override bool Equals(object other) => Equals(other as GetCurrentClanStatsForSeasonResponse);

        public bool Equals(GetCurrentClanStatsForSeasonResponse other) {

            if (other == null) return false;

            if (!object.Equals(ClanStats, other.ClanStats)) return false;

            if (!clanMemberStats_.Equals(other.clanMemberStats_)) return false;

            return true;

        }

        public override int GetHashCode() {

            int hash = 1;

            if (clanStats_ != null) hash ^= ClanStats.GetHashCode();

            hash ^= clanMemberStats_.GetHashCode();

            return hash;

        }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {

            if (clanStats_ != null) {

                output.WriteRawTag(10);

                output.WriteMessage(ClanStats);

            }

            clanMemberStats_.WriteTo(output, _repeated_clanMemberStats_codec);

        }

        public int CalculateSize() {

            int size = 0;

            if (clanStats_ != null) {

                size += 1 + pb::CodedOutputStream.ComputeMessageSize(ClanStats);

            }

            size += clanMemberStats_.CalculateSize(_repeated_clanMemberStats_codec);

            return size;

        }

        public void MergeFrom(GetCurrentClanStatsForSeasonResponse other) {

            if (other == null) return;

            if (other.clanStats_ != null) {

                if (clanStats_ == null) {

                    ClanStats = new global::Axlebolt.Bolt.Protobuf.ClanStats();

                }

                ClanStats.MergeFrom(other.ClanStats);

            }

            clanMemberStats_.Add(other.clanMemberStats_);

        }

        public void MergeFrom(pb::CodedInputStream input) {

            uint tag;

            while ((tag = input.ReadTag()) != 0) {

                switch(tag) {

                    default: input.SkipLastField(); break;

                    case 10: {

                        if (clanStats_ == null) {

                            ClanStats = new global::Axlebolt.Bolt.Protobuf.ClanStats();

                        }

                        input.ReadMessage(ClanStats);

                        break;

                    }

                    case 18: {

                        clanMemberStats_.AddEntriesFrom(input, _repeated_clanMemberStats_codec);

                        break;

                    }

                }

            }

        }

    }



    public sealed partial class GetClanStatsForSeasonResponse : pb::IMessage<GetClanStatsForSeasonResponse> {

        private static readonly pb::MessageParser<GetClanStatsForSeasonResponse> _parser = new pb::MessageParser<GetClanStatsForSeasonResponse>(() => new GetClanStatsForSeasonResponse());

        public static pb::MessageParser<GetClanStatsForSeasonResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private global::Axlebolt.Bolt.Protobuf.ClanStats clanStats_;

        public global::Axlebolt.Bolt.Protobuf.ClanStats ClanStats

        {

            get { return clanStats_; }

            set { clanStats_ = value; }

        }



        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.ClanMemberStats> _repeated_clanMemberStats_codec =

            pb::FieldCodec.ForMessage(18, global::Axlebolt.Bolt.Protobuf.ClanMemberStats.Parser);

        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanMemberStats> clanMemberStats_ =

            new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanMemberStats>();

        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanMemberStats> ClanMemberStats => clanMemberStats_;



        public GetClanStatsForSeasonResponse() {}

        public GetClanStatsForSeasonResponse(GetClanStatsForSeasonResponse other) : this()

        {

            clanStats_ = other.clanStats_ != null ? other.clanStats_.Clone() : null;

            clanMemberStats_.Add(other.clanMemberStats_);

        }

        public GetClanStatsForSeasonResponse Clone() => new GetClanStatsForSeasonResponse(this);

        public override bool Equals(object other) => Equals(other as GetClanStatsForSeasonResponse);

        public bool Equals(GetClanStatsForSeasonResponse other)

        {

            if (other == null) return false;

            if (!object.Equals(ClanStats, other.ClanStats)) return false;

            if (!clanMemberStats_.Equals(other.clanMemberStats_)) return false;

            return true;

        }

        public override int GetHashCode()

        {

            int hash = 1;

            if (clanStats_ != null) hash ^= clanStats_.GetHashCode();

            hash ^= clanMemberStats_.GetHashCode();

            return hash;

        }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (clanStats_ != null)

            {

                output.WriteRawTag(10);

                output.WriteMessage(ClanStats);

            }

            clanMemberStats_.WriteTo(output, _repeated_clanMemberStats_codec);

        }

        public int CalculateSize()

        {

            int size = 0;

            if (clanStats_ != null)

            {

                size += 1 + pb::CodedOutputStream.ComputeMessageSize(ClanStats);

            }

            size += clanMemberStats_.CalculateSize(_repeated_clanMemberStats_codec);

            return size;

        }

        public void MergeFrom(GetClanStatsForSeasonResponse other)

        {

            if (other == null) return;

            if (other.clanStats_ != null)

            {

                if (clanStats_ == null)

                {

                    clanStats_ = new global::Axlebolt.Bolt.Protobuf.ClanStats();

                }

                clanStats_.MergeFrom(other.clanStats_);

            }

            clanMemberStats_.Add(other.clanMemberStats_);

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10:

                    {

                        if (clanStats_ == null)

                        {

                            clanStats_ = new global::Axlebolt.Bolt.Protobuf.ClanStats();

                        }

                        input.ReadMessage(clanStats_);

                        break;

                    }

                    case 18:

                    {

                        clanMemberStats_.AddEntriesFrom(input, _repeated_clanMemberStats_codec);

                        break;

                    }

                    default:

                        input.SkipLastField();

                        break;

                }

            }

        }

    }



    // ============ RateGameRemoteService ============



    public sealed partial class GetLastRateGameResponse : pb::IMessage<GetLastRateGameResponse> {

        private static readonly pb::MessageParser<GetLastRateGameResponse> _parser = new pb::MessageParser<GetLastRateGameResponse>(() => new GetLastRateGameResponse());

        public static pb::MessageParser<GetLastRateGameResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public GetLastRateGameResponse() {}

        public GetLastRateGameResponse(GetLastRateGameResponse other) : this() {}

        public GetLastRateGameResponse Clone() => new GetLastRateGameResponse(this);

        public override bool Equals(object other) => Equals(other as GetLastRateGameResponse);

        public bool Equals(GetLastRateGameResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(GetLastRateGameResponse other) {}

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class AskLaterResponse : pb::IMessage<AskLaterResponse> {

        private static readonly pb::MessageParser<AskLaterResponse> _parser = new pb::MessageParser<AskLaterResponse>(() => new AskLaterResponse());

        public static pb::MessageParser<AskLaterResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public AskLaterResponse() {}

        public AskLaterResponse(AskLaterResponse other) : this() {}

        public AskLaterResponse Clone() => new AskLaterResponse(this);

        public override bool Equals(object other) => Equals(other as AskLaterResponse);

        public bool Equals(AskLaterResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(AskLaterResponse other) {}

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class DontAskLaterResponse : pb::IMessage<DontAskLaterResponse> {

        private static readonly pb::MessageParser<DontAskLaterResponse> _parser = new pb::MessageParser<DontAskLaterResponse>(() => new DontAskLaterResponse());

        public static pb::MessageParser<DontAskLaterResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public DontAskLaterResponse() {}

        public DontAskLaterResponse(DontAskLaterResponse other) : this() {}

        public DontAskLaterResponse Clone() => new DontAskLaterResponse(this);

        public override bool Equals(object other) => Equals(other as DontAskLaterResponse);

        public bool Equals(DontAskLaterResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(DontAskLaterResponse other) {}

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class RateGameResponse : pb::IMessage<RateGameResponse> {

        private static readonly pb::MessageParser<RateGameResponse> _parser = new pb::MessageParser<RateGameResponse>(() => new RateGameResponse());

        public static pb::MessageParser<RateGameResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public RateGameResponse() {}

        public RateGameResponse(RateGameResponse other) : this() {}

        public RateGameResponse Clone() => new RateGameResponse(this);

        public override bool Equals(object other) => Equals(other as RateGameResponse);

        public bool Equals(RateGameResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(RateGameResponse other) {}

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    // ============ MatchesRemoteService ============



    public sealed partial class GetClanMatchesResponse : pb::IMessage<GetClanMatchesResponse> {

        private static readonly pb::MessageParser<GetClanMatchesResponse> _parser = new pb::MessageParser<GetClanMatchesResponse>(() => new GetClanMatchesResponse());

        public static pb::MessageParser<GetClanMatchesResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public GetClanMatchesResponse() {}

        public GetClanMatchesResponse(GetClanMatchesResponse other) : this() {}

        public GetClanMatchesResponse Clone() => new GetClanMatchesResponse(this);

        public override bool Equals(object other) => Equals(other as GetClanMatchesResponse);

        public bool Equals(GetClanMatchesResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(GetClanMatchesResponse other) {}

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class GetMatchResponse : pb::IMessage<GetMatchResponse> {

        private static readonly pb::MessageParser<GetMatchResponse> _parser = new pb::MessageParser<GetMatchResponse>(() => new GetMatchResponse());

        public static pb::MessageParser<GetMatchResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private FinishedMatch match_;
        public FinishedMatch Match { get => match_; set => match_ = value; }

        public GetMatchResponse() {}

        public GetMatchResponse(GetMatchResponse other) : this() { match_ = other.match_?.Clone(); }

        public GetMatchResponse Clone() => new GetMatchResponse(this);

        public override bool Equals(object other) => Equals(other as GetMatchResponse);

        public bool Equals(GetMatchResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)
        {
            if (match_ != null) { output.WriteRawTag(10); match_.WriteLengthDelimited(output); }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (match_ != null) size += 1 + match_.LengthDelimitedSize();
            return size;
        }

        public void MergeFrom(GetMatchResponse other)
        {
            if (other?.match_ == null) return;
            if (match_ == null) match_ = new FinishedMatch();
            match_.MergeFrom(other.match_);
        }

        public void MergeFrom(pb::CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10)
                {
                    if (match_ == null) match_ = new FinishedMatch();
                    input.ReadMessage(match_);
                }
                else input.SkipLastField();
            }
        }

    }



    public sealed partial class GetCurrentPlayerLastMatchResponse : pb::IMessage<GetCurrentPlayerLastMatchResponse> {

        private static readonly pb::MessageParser<GetCurrentPlayerLastMatchResponse> _parser = new pb::MessageParser<GetCurrentPlayerLastMatchResponse>(() => new GetCurrentPlayerLastMatchResponse());

        public static pb::MessageParser<GetCurrentPlayerLastMatchResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private FinishedMatch match_;
        public FinishedMatch Match { get => match_; set => match_ = value; }

        public GetCurrentPlayerLastMatchResponse() {}

        public GetCurrentPlayerLastMatchResponse(GetCurrentPlayerLastMatchResponse other) : this() { match_ = other.match_?.Clone(); }

        public GetCurrentPlayerLastMatchResponse Clone() => new GetCurrentPlayerLastMatchResponse(this);

        public override bool Equals(object other) => Equals(other as GetCurrentPlayerLastMatchResponse);

        public bool Equals(GetCurrentPlayerLastMatchResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)
        {
            if (match_ != null) { output.WriteRawTag(10); match_.WriteLengthDelimited(output); }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (match_ != null) size += 1 + match_.LengthDelimitedSize();
            return size;
        }

        public void MergeFrom(GetCurrentPlayerLastMatchResponse other)
        {
            if (other?.match_ == null) return;
            if (match_ == null) match_ = new FinishedMatch();
            match_.MergeFrom(other.match_);
        }

        public void MergeFrom(pb::CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10)
                {
                    if (match_ == null) match_ = new FinishedMatch();
                    input.ReadMessage(match_);
                }
                else input.SkipLastField();
            }
        }

    }



    public sealed partial class GetClanLastMatchResponse : pb::IMessage<GetClanLastMatchResponse> {

        private static readonly pb::MessageParser<GetClanLastMatchResponse> _parser = new pb::MessageParser<GetClanLastMatchResponse>(() => new GetClanLastMatchResponse());

        public static pb::MessageParser<GetClanLastMatchResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public GetClanLastMatchResponse() {}

        public GetClanLastMatchResponse(GetClanLastMatchResponse other) : this() {}

        public GetClanLastMatchResponse Clone() => new GetClanLastMatchResponse(this);

        public override bool Equals(object other) => Equals(other as GetClanLastMatchResponse);

        public bool Equals(GetClanLastMatchResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(GetClanLastMatchResponse other) {}

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class GetPlayerMatchesResponse : pb::IMessage<GetPlayerMatchesResponse> {

        private static readonly pb::MessageParser<GetPlayerMatchesResponse> _parser = new pb::MessageParser<GetPlayerMatchesResponse>(() => new GetPlayerMatchesResponse());

        public static pb::MessageParser<GetPlayerMatchesResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private readonly pbc::RepeatedField<FinishedMatch> matches_ = new pbc::RepeatedField<FinishedMatch>();
        public pbc::RepeatedField<FinishedMatch> Matches => matches_;

        public int PageOffset { get; set; }
        public int PageSize { get; set; }

        public GetPlayerMatchesResponse() {}

        public GetPlayerMatchesResponse(GetPlayerMatchesResponse other) : this() { matches_.Add(other.matches_); }

        public GetPlayerMatchesResponse Clone() => new GetPlayerMatchesResponse(this);

        public override bool Equals(object other) => Equals(other as GetPlayerMatchesResponse);

        public bool Equals(GetPlayerMatchesResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)
        {
            foreach (var m in matches_)
            {
                if (m == null) continue;
                output.WriteRawTag(10);
                m.WriteLengthDelimited(output);
            }
        }

        public int CalculateSize()
        {
            int size = 0;
            foreach (var m in matches_)
            {
                if (m == null) continue;
                size += 1 + m.LengthDelimitedSize();
            }
            return size;
        }

        public void MergeFrom(GetPlayerMatchesResponse other)
        {
            if (other == null) return;
            matches_.Add(other.matches_);
        }

        public void MergeFrom(pb::CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10)
                {
                    var m = new FinishedMatch();
                    input.ReadMessage(m);
                    matches_.Add(m);
                }
                else input.SkipLastField();
            }
        }

    }



    public sealed partial class GetPlayerLastMatchResponse : pb::IMessage<GetPlayerLastMatchResponse> {

        private static readonly pb::MessageParser<GetPlayerLastMatchResponse> _parser = new pb::MessageParser<GetPlayerLastMatchResponse>(() => new GetPlayerLastMatchResponse());

        public static pb::MessageParser<GetPlayerLastMatchResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private FinishedMatch match_;
        public FinishedMatch Match { get => match_; set => match_ = value; }

        public GetPlayerLastMatchResponse() {}

        public GetPlayerLastMatchResponse(GetPlayerLastMatchResponse other) : this() { match_ = other.match_?.Clone(); }

        public GetPlayerLastMatchResponse Clone() => new GetPlayerLastMatchResponse(this);

        public override bool Equals(object other) => Equals(other as GetPlayerLastMatchResponse);

        public bool Equals(GetPlayerLastMatchResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)
        {
            if (match_ != null) { output.WriteRawTag(10); match_.WriteLengthDelimited(output); }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (match_ != null) size += 1 + match_.LengthDelimitedSize();
            return size;
        }

        public void MergeFrom(GetPlayerLastMatchResponse other)
        {
            if (other?.match_ == null) return;
            if (match_ == null) match_ = new FinishedMatch();
            match_.MergeFrom(other.match_);
        }

        public void MergeFrom(pb::CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10) { if (match_ == null) match_ = new FinishedMatch(); input.ReadMessage(match_); }
                else input.SkipLastField();
            }
        }

    }



    // ============ GSClanStatsRemoteService ============



    public sealed partial class GSSaveClanStatsRequest : pb::IMessage<GSSaveClanStatsRequest> {

        private static readonly pb::MessageParser<GSSaveClanStatsRequest> _parser = new pb::MessageParser<GSSaveClanStatsRequest>(() => new GSSaveClanStatsRequest());

        public static pb::MessageParser<GSSaveClanStatsRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public GSSaveClanStatsRequest() { clanStats_ = new pbc::RepeatedField<ClanStats>(); }

        public GSSaveClanStatsRequest(GSSaveClanStatsRequest other) : this() { clanStats_ = other.clanStats_.Clone(); }

        public GSSaveClanStatsRequest Clone() => new GSSaveClanStatsRequest(this);

        private readonly pbc::RepeatedField<ClanStats> clanStats_;

        public pbc::RepeatedField<ClanStats> ClanStats => clanStats_;

        public override bool Equals(object other) => Equals(other as GSSaveClanStatsRequest);

        public bool Equals(GSSaveClanStatsRequest other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(GSSaveClanStatsRequest other) { if (other != null) clanStats_.Add(other.clanStats_); }

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class GSSaveClanStatsResponse : pb::IMessage<GSSaveClanStatsResponse> {

        private static readonly pb::MessageParser<GSSaveClanStatsResponse> _parser = new pb::MessageParser<GSSaveClanStatsResponse>(() => new GSSaveClanStatsResponse());

        public static pb::MessageParser<GSSaveClanStatsResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public GSSaveClanStatsResponse() {}

        public GSSaveClanStatsResponse(GSSaveClanStatsResponse other) : this() {}

        public GSSaveClanStatsResponse Clone() => new GSSaveClanStatsResponse(this);

        public override bool Equals(object other) => Equals(other as GSSaveClanStatsResponse);

        public bool Equals(GSSaveClanStatsResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(GSSaveClanStatsResponse other) {}

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class GSGetClanStatsRequest : pb::IMessage<GSGetClanStatsRequest> {

        private static readonly pb::MessageParser<GSGetClanStatsRequest> _parser = new pb::MessageParser<GSGetClanStatsRequest>(() => new GSGetClanStatsRequest());

        public static pb::MessageParser<GSGetClanStatsRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public GSGetClanStatsRequest() { clanIds_ = new pbc::RepeatedField<string>(); }

        public GSGetClanStatsRequest(GSGetClanStatsRequest other) : this() { clanIds_ = other.clanIds_.Clone(); }

        public GSGetClanStatsRequest Clone() => new GSGetClanStatsRequest(this);

        private readonly pbc::RepeatedField<string> clanIds_;

        public pbc::RepeatedField<string> ClanIds => clanIds_;

        public override bool Equals(object other) => Equals(other as GSGetClanStatsRequest);

        public bool Equals(GSGetClanStatsRequest other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(GSGetClanStatsRequest other) { if (other != null) clanIds_.Add(other.clanIds_); }

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class GSGetClanStatsResponse : pb::IMessage<GSGetClanStatsResponse> {

        private static readonly pb::MessageParser<GSGetClanStatsResponse> _parser = new pb::MessageParser<GSGetClanStatsResponse>(() => new GSGetClanStatsResponse());

        public static pb::MessageParser<GSGetClanStatsResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public GSGetClanStatsResponse() { clanStats_ = new pbc::RepeatedField<ClanStats>(); }

        public GSGetClanStatsResponse(GSGetClanStatsResponse other) : this() { clanStats_ = other.clanStats_.Clone(); }

        public GSGetClanStatsResponse Clone() => new GSGetClanStatsResponse(this);

        private readonly pbc::RepeatedField<ClanStats> clanStats_;

        public pbc::RepeatedField<ClanStats> ClanStats => clanStats_;

        public override bool Equals(object other) => Equals(other as GSGetClanStatsResponse);

        public bool Equals(GSGetClanStatsResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(GSGetClanStatsResponse other) { if (other != null) clanStats_.Add(other.clanStats_); }

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    // ============ GSMatchesRemoteService ============



    public sealed partial class FinishMatchRequest : pb::IMessage<FinishMatchRequest> {

        private static readonly pb::MessageParser<FinishMatchRequest> _parser = new pb::MessageParser<FinishMatchRequest>(() => new FinishMatchRequest());

        public static pb::MessageParser<FinishMatchRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public FinishMatchRequest() { matchResults_ = new pbc::RepeatedField<MatchResult>(); }

        public FinishMatchRequest(FinishMatchRequest other) : this() { matchResults_ = other.matchResults_.Clone(); }

        public FinishMatchRequest Clone() => new FinishMatchRequest(this);

        private readonly pbc::RepeatedField<MatchResult> matchResults_;

        public pbc::RepeatedField<MatchResult> MatchResults => matchResults_;

        public override bool Equals(object other) => Equals(other as FinishMatchRequest);

        public bool Equals(FinishMatchRequest other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(FinishMatchRequest other) { if (other != null) matchResults_.Add(other.matchResults_); }

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class MatchResult : pb::IMessage<MatchResult> {

        private static readonly pb::MessageParser<MatchResult> _parser = new pb::MessageParser<MatchResult>(() => new MatchResult());

        public static pb::MessageParser<MatchResult> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public MatchResult() {}

        public MatchResult(MatchResult other) : this() { playerId_ = other.playerId_; gameMode_ = other.gameMode_; isWin_ = other.isWin_; }

        public MatchResult Clone() => new MatchResult(this);

        private string playerId_ = "";

        public string PlayerId { get => playerId_; set => playerId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        private string gameMode_ = "";

        public string GameMode { get => gameMode_; set => gameMode_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        private bool isWin_;

        public bool IsWin { get => isWin_; set => isWin_ = value; }

        public override bool Equals(object other) => Equals(other as MatchResult);

        public bool Equals(MatchResult other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(MatchResult other) { if (other != null) { playerId_ = other.playerId_; gameMode_ = other.gameMode_; isWin_ = other.isWin_; } }

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class FinishMatchResponse : pb::IMessage<FinishMatchResponse> {

        private static readonly pb::MessageParser<FinishMatchResponse> _parser = new pb::MessageParser<FinishMatchResponse>(() => new FinishMatchResponse());

        public static pb::MessageParser<FinishMatchResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public FinishMatchResponse() {}

        public FinishMatchResponse(FinishMatchResponse other) : this() {}

        public FinishMatchResponse Clone() => new FinishMatchResponse(this);

        public override bool Equals(object other) => Equals(other as FinishMatchResponse);

        public bool Equals(FinishMatchResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(FinishMatchResponse other) {}

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    // ============ GdprRemoteService ============



    public sealed partial class GetRequestsEncryptedResponse : pb::IMessage<GetRequestsEncryptedResponse> {

        private static readonly pb::MessageParser<GetRequestsEncryptedResponse> _parser = new pb::MessageParser<GetRequestsEncryptedResponse>(() => new GetRequestsEncryptedResponse());

        public static pb::MessageParser<GetRequestsEncryptedResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public GetRequestsEncryptedResponse() {}

        public GetRequestsEncryptedResponse(GetRequestsEncryptedResponse other) : this() {}

        public GetRequestsEncryptedResponse Clone() => new GetRequestsEncryptedResponse(this);

        public override bool Equals(object other) => Equals(other as GetRequestsEncryptedResponse);

        public bool Equals(GetRequestsEncryptedResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(GetRequestsEncryptedResponse other) {}

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class CreateRequestEncryptedResponse : pb::IMessage<CreateRequestEncryptedResponse> {

        private static readonly pb::MessageParser<CreateRequestEncryptedResponse> _parser = new pb::MessageParser<CreateRequestEncryptedResponse>(() => new CreateRequestEncryptedResponse());

        public static pb::MessageParser<CreateRequestEncryptedResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public CreateRequestEncryptedResponse() {}

        public CreateRequestEncryptedResponse(CreateRequestEncryptedResponse other) : this() {}

        public CreateRequestEncryptedResponse Clone() => new CreateRequestEncryptedResponse(this);

        public override bool Equals(object other) => Equals(other as CreateRequestEncryptedResponse);

        public bool Equals(CreateRequestEncryptedResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(CreateRequestEncryptedResponse other) {}

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    // ============ GameSeasonRemoteService ============



    public sealed partial class GameSeason : pb::IMessage<GameSeason>, System.IEquatable<GameSeason>, pb::IDeepCloneable<GameSeason>

    {

        private static readonly pb::MessageParser<GameSeason> _parser = new pb::MessageParser<GameSeason>(() => new GameSeason());

        public static pb::MessageParser<GameSeason> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private string id_ = "";

        public string Id { get => id_; set => id_ = value; }

        

        private string name_ = "";

        public string Name { get => name_; set => name_ = value; }



        public GameSeason() {}

        public GameSeason(GameSeason other) : this() { id_ = other.id_; name_ = other.name_; }

        public GameSeason Clone() => new GameSeason(this);

        public bool Equals(GameSeason other) => other != null && id_ == other.id_;

        public override bool Equals(object other) => Equals(other as GameSeason);

        public override int GetHashCode() => id_.GetHashCode();

        public override string ToString() => $"{{\"id\":\"{id_}\",\"name\":\"{name_}\"}}";

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (id_.Length != 0) { output.WriteRawTag(10); output.WriteString(id_); }

            if (name_.Length != 0) { output.WriteRawTag(18); output.WriteString(name_); }

        }

        public int CalculateSize()

        {

            int size = 0;

            if (id_.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(id_);

            if (name_.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(name_);

            return size;

        }

        public void MergeFrom(GameSeason other) { id_ = other.id_; name_ = other.name_; }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint num;

            while ((num = input.ReadTag()) != 0)

            {

                switch (num)

                {

                    case 10: id_ = input.ReadString(); break;

                    case 18: name_ = input.ReadString(); break;

                    default: input.SkipLastField(); break;

                }

            }

        }

    }



    public sealed partial class GetGameSeasonsResponse : pb::IMessage<GetGameSeasonsResponse>, System.IEquatable<GetGameSeasonsResponse>, pb::IDeepCloneable<GetGameSeasonsResponse> {

        private static readonly pb::MessageParser<GetGameSeasonsResponse> _parser = new pb::MessageParser<GetGameSeasonsResponse>(() => new GetGameSeasonsResponse());

        public static pb::MessageParser<GetGameSeasonsResponse> Parser => _parser;

        private static readonly pb::FieldCodec<GameSeason> _repeated_seasons_codec = pb::FieldCodec.ForMessage(10, GameSeason.Parser);

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private readonly pbc.RepeatedField<GameSeason> seasons_ = new pbc.RepeatedField<GameSeason>();

        public pbc.RepeatedField<GameSeason> Seasons => seasons_;



        public GetGameSeasonsResponse() {}

        public GetGameSeasonsResponse(GetGameSeasonsResponse other) : this() { seasons_.Add(other.seasons_); }

        public GetGameSeasonsResponse Clone() => new GetGameSeasonsResponse(this);

        public bool Equals(GetGameSeasonsResponse other) => other != null && seasons_.Equals(other.seasons_);

        public override bool Equals(object other) => Equals(other as GetGameSeasonsResponse);

        public override int GetHashCode() => seasons_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { seasons_.WriteTo(output, _repeated_seasons_codec); }

        public int CalculateSize() => seasons_.CalculateSize(_repeated_seasons_codec);

        public void MergeFrom(GetGameSeasonsResponse other) { seasons_.Add(other.seasons_); }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0) {

                if (tag == 10) seasons_.AddEntriesFrom(input, _repeated_seasons_codec);

                else input.SkipLastField();

            }

        }

    }



    // ============ GameEventRemoteService ============



    public sealed partial class GetAllChallengesResponse : pb::IMessage<GetAllChallengesResponse> {

        private static readonly pb::MessageParser<GetAllChallengesResponse> _parser = new pb::MessageParser<GetAllChallengesResponse>(() => new GetAllChallengesResponse());

        public static pb::MessageParser<GetAllChallengesResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public GetAllChallengesResponse() {}

        public GetAllChallengesResponse(GetAllChallengesResponse other) : this() {}

        public GetAllChallengesResponse Clone() => new GetAllChallengesResponse(this);

        public override bool Equals(object other) => Equals(other as GetAllChallengesResponse);

        public bool Equals(GetAllChallengesResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(GetAllChallengesResponse other) {}

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    // ============ GameServerStatsRemoteService ============



    public sealed partial class StorePlayersStatsRequest : pb::IMessage<StorePlayersStatsRequest> {

        private static readonly pb::MessageParser<StorePlayersStatsRequest> _parser = new pb::MessageParser<StorePlayersStatsRequest>(() => new StorePlayersStatsRequest());

        private static readonly pb::FieldCodec<StorePlayerStats> _repeated_storePlayersStats_codec =

            pb::FieldCodec.ForMessage(10, StorePlayerStats.Parser);

        public static pb::MessageParser<StorePlayersStatsRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private readonly pbc::RepeatedField<StorePlayerStats> storePlayersStats_ = new pbc::RepeatedField<StorePlayerStats>();

        public pbc::RepeatedField<StorePlayerStats> StorePlayersStats => storePlayersStats_;

        // Legacy alias used by existing server code.

        public pbc::RepeatedField<StorePlayerStats> PlayerStats => storePlayersStats_;



        public StorePlayersStatsRequest() { }

        public StorePlayersStatsRequest(StorePlayersStatsRequest other) : this() { storePlayersStats_.Add(other.storePlayersStats_); }

        public StorePlayersStatsRequest Clone() => new StorePlayersStatsRequest(this);

        public override bool Equals(object other) => Equals(other as StorePlayersStatsRequest);

        public bool Equals(StorePlayersStatsRequest other) => other != null && storePlayersStats_.Equals(other.storePlayersStats_);

        public override int GetHashCode() => storePlayersStats_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) => storePlayersStats_.WriteTo(output, _repeated_storePlayersStats_codec);

        public int CalculateSize() => storePlayersStats_.CalculateSize(_repeated_storePlayersStats_codec);

        public void MergeFrom(StorePlayersStatsRequest other)

        {

            if (other == null) return;

            storePlayersStats_.Add(other.storePlayersStats_);

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                if (tag == 10) storePlayersStats_.AddEntriesFrom(input, _repeated_storePlayersStats_codec);

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class StorePlayerStats : pb::IMessage<StorePlayerStats> {

        private static readonly pb::MessageParser<StorePlayerStats> _parser = new pb::MessageParser<StorePlayerStats>(() => new StorePlayerStats());

        private static readonly pb::FieldCodec<StorePlayerStat> _repeated_stats_codec =

            pb::FieldCodec.ForMessage(18, StorePlayerStat.Parser);

        private static readonly pb::FieldCodec<StoreAchievement> _repeated_achievements_codec =

            pb::FieldCodec.ForMessage(26, StoreAchievement.Parser);

        public static pb::MessageParser<StorePlayerStats> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private string playerId_ = "";

        public string PlayerId { get => playerId_; set => playerId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        private readonly pbc::RepeatedField<StorePlayerStat> stats_ = new pbc::RepeatedField<StorePlayerStat>();

        public pbc::RepeatedField<StorePlayerStat> Stats => stats_;

        private readonly pbc::RepeatedField<StoreAchievement> achievements_ = new pbc::RepeatedField<StoreAchievement>();

        public pbc::RepeatedField<StoreAchievement> Achievements => achievements_;

        private string seasonId_ = "";

        public string SeasonId { get => seasonId_; set => seasonId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }



        public StorePlayerStats() { }

        public StorePlayerStats(StorePlayerStats other) : this()

        {

            playerId_ = other.playerId_;

            stats_.Add(other.stats_);

            achievements_.Add(other.achievements_);

            seasonId_ = other.seasonId_;

        }

        public StorePlayerStats Clone() => new StorePlayerStats(this);

        public override bool Equals(object other) => Equals(other as StorePlayerStats);

        public bool Equals(StorePlayerStats other)

            => other != null &&

               PlayerId == other.PlayerId &&

               stats_.Equals(other.stats_) &&

               achievements_.Equals(other.achievements_) &&

               SeasonId == other.SeasonId;

        public override int GetHashCode()

        {

            int hash = 1;

            if (PlayerId.Length != 0) hash ^= PlayerId.GetHashCode();

            hash ^= stats_.GetHashCode();

            hash ^= achievements_.GetHashCode();

            if (SeasonId.Length != 0) hash ^= SeasonId.GetHashCode();

            return hash;

        }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (PlayerId.Length != 0)

            {

                output.WriteRawTag(10);

                output.WriteString(PlayerId);

            }

            stats_.WriteTo(output, _repeated_stats_codec);

            achievements_.WriteTo(output, _repeated_achievements_codec);

            if (SeasonId.Length != 0)

            {

                output.WriteRawTag(34);

                output.WriteString(SeasonId);

            }

        }

        public int CalculateSize()

        {

            int size = 0;

            if (PlayerId.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(PlayerId);

            size += stats_.CalculateSize(_repeated_stats_codec);

            size += achievements_.CalculateSize(_repeated_achievements_codec);

            if (SeasonId.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(SeasonId);

            return size;

        }

        public void MergeFrom(StorePlayerStats other)

        {

            if (other == null) return;

            if (other.PlayerId.Length != 0) PlayerId = other.PlayerId;

            stats_.Add(other.stats_);

            achievements_.Add(other.achievements_);

            if (other.SeasonId.Length != 0) SeasonId = other.SeasonId;

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10: PlayerId = input.ReadString(); break;

                    case 18: stats_.AddEntriesFrom(input, _repeated_stats_codec); break;

                    case 26: achievements_.AddEntriesFrom(input, _repeated_achievements_codec); break;

                    case 34: SeasonId = input.ReadString(); break;

                    default: input.SkipLastField(); break;

                }

            }

        }

    }



    public sealed partial class GetPlayerStatsRequest : pb::IMessage<GetPlayerStatsRequest> {

        private static readonly pb::MessageParser<GetPlayerStatsRequest> _parser = new pb::MessageParser<GetPlayerStatsRequest>(() => new GetPlayerStatsRequest());

        private static readonly pb::FieldCodec<string> _repeated_apiNames_codec = pb::FieldCodec.ForString(18);

        public static pb::MessageParser<GetPlayerStatsRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private string playerId_ = "";

        public string PlayerId { get => playerId_; set => playerId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        private readonly pbc::RepeatedField<string> apiNames_ = new pbc::RepeatedField<string>();

        public pbc::RepeatedField<string> ApiNames => apiNames_;

        private bool addLeaderboardStats_;

        public bool AddLeaderboardStats { get => addLeaderboardStats_; set => addLeaderboardStats_ = value; }



        public GetPlayerStatsRequest() { }

        public GetPlayerStatsRequest(GetPlayerStatsRequest other) : this()

        {

            playerId_ = other.playerId_;

            apiNames_.Add(other.apiNames_);

            addLeaderboardStats_ = other.addLeaderboardStats_;

        }

        public GetPlayerStatsRequest Clone() => new GetPlayerStatsRequest(this);

        public override bool Equals(object other) => Equals(other as GetPlayerStatsRequest);

        public bool Equals(GetPlayerStatsRequest other)

            => other != null &&

               PlayerId == other.PlayerId &&

               apiNames_.Equals(other.apiNames_) &&

               AddLeaderboardStats == other.AddLeaderboardStats;

        public override int GetHashCode()

        {

            int hash = 1;

            if (PlayerId.Length != 0) hash ^= PlayerId.GetHashCode();

            hash ^= apiNames_.GetHashCode();

            if (AddLeaderboardStats) hash ^= AddLeaderboardStats.GetHashCode();

            return hash;

        }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (PlayerId.Length != 0)

            {

                output.WriteRawTag(10);

                output.WriteString(PlayerId);

            }

            apiNames_.WriteTo(output, _repeated_apiNames_codec);

            if (AddLeaderboardStats)

            {

                output.WriteRawTag(24);

                output.WriteBool(AddLeaderboardStats);

            }

        }

        public int CalculateSize()

        {

            int size = 0;

            if (PlayerId.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(PlayerId);

            size += apiNames_.CalculateSize(_repeated_apiNames_codec);

            if (AddLeaderboardStats) size += 1 + 1;

            return size;

        }

        public void MergeFrom(GetPlayerStatsRequest other)

        {

            if (other == null) return;

            if (other.PlayerId.Length != 0) PlayerId = other.PlayerId;

            apiNames_.Add(other.apiNames_);

            if (other.AddLeaderboardStats) AddLeaderboardStats = other.AddLeaderboardStats;

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10: PlayerId = input.ReadString(); break;

                    case 18: apiNames_.AddEntriesFrom(input, _repeated_apiNames_codec); break;

                    case 24: AddLeaderboardStats = input.ReadBool(); break;

                    default: input.SkipLastField(); break;

                }

            }

        }

    }



    public sealed partial class GetPlayerStatsResponse : pb::IMessage<GetPlayerStatsResponse> {

        private static readonly pb::MessageParser<GetPlayerStatsResponse> _parser = new pb::MessageParser<GetPlayerStatsResponse>(() => new GetPlayerStatsResponse());

        public static pb::MessageParser<GetPlayerStatsResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private PlayerStats playerStats_;

        public PlayerStats PlayerStats

        {

            get => playerStats_;

            set => playerStats_ = value;

        }



        public GetPlayerStatsResponse() { }

        public GetPlayerStatsResponse(GetPlayerStatsResponse other) : this()

        {

            playerStats_ = other.playerStats_ != null ? other.playerStats_.Clone() : null;

        }

        public GetPlayerStatsResponse Clone() => new GetPlayerStatsResponse(this);

        public override bool Equals(object other) => Equals(other as GetPlayerStatsResponse);

        public bool Equals(GetPlayerStatsResponse other) => other != null && object.Equals(PlayerStats, other.PlayerStats);

        public override int GetHashCode() => playerStats_ != null ? PlayerStats.GetHashCode() : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (playerStats_ != null)

            {

                output.WriteRawTag(10);

                output.WriteMessage(PlayerStats);

            }

        }

        public int CalculateSize()

        {

            int size = 0;

            if (playerStats_ != null) size += 1 + pb::CodedOutputStream.ComputeMessageSize(PlayerStats);

            return size;

        }

        public void MergeFrom(GetPlayerStatsResponse other)

        {

            if (other == null) return;

            if (other.playerStats_ != null)

            {

                if (playerStats_ == null) PlayerStats = new PlayerStats();

                PlayerStats.MergeFrom(other.PlayerStats);

            }

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            while (input.ReadTag() != 0) input.SkipLastField();

        }

    }



    // ============ GameEventRemoteService ============



    public sealed partial class GetPlayersStatsRequest : pb::IMessage<GetPlayersStatsRequest> {

        private static readonly pb::MessageParser<GetPlayersStatsRequest> _parser = new pb::MessageParser<GetPlayersStatsRequest>(() => new GetPlayersStatsRequest());

        private static readonly pb::FieldCodec<string> _repeated_playerIds_codec = pb::FieldCodec.ForString(10);

        private static readonly pb::FieldCodec<string> _repeated_apiNames_codec = pb::FieldCodec.ForString(18);

        public static pb::MessageParser<GetPlayersStatsRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private readonly pbc::RepeatedField<string> playerIds_ = new pbc::RepeatedField<string>();

        public pbc::RepeatedField<string> PlayerIds => playerIds_;

        private readonly pbc::RepeatedField<string> apiNames_ = new pbc::RepeatedField<string>();

        public pbc::RepeatedField<string> ApiNames => apiNames_;

        private bool addLeaderboardStats_;

        public bool AddLeaderboardStats { get => addLeaderboardStats_; set => addLeaderboardStats_ = value; }



        public GetPlayersStatsRequest() { }

        public GetPlayersStatsRequest(GetPlayersStatsRequest other) : this()

        {

            playerIds_.Add(other.playerIds_);

            apiNames_.Add(other.apiNames_);

            addLeaderboardStats_ = other.addLeaderboardStats_;

        }

        public GetPlayersStatsRequest Clone() => new GetPlayersStatsRequest(this);

        public override bool Equals(object other) => Equals(other as GetPlayersStatsRequest);

        public bool Equals(GetPlayersStatsRequest other)

            => other != null &&

               playerIds_.Equals(other.playerIds_) &&

               apiNames_.Equals(other.apiNames_) &&

               AddLeaderboardStats == other.AddLeaderboardStats;

        public override int GetHashCode()

        {

            int hash = 1;

            hash ^= playerIds_.GetHashCode();

            hash ^= apiNames_.GetHashCode();

            if (AddLeaderboardStats) hash ^= AddLeaderboardStats.GetHashCode();

            return hash;

        }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            playerIds_.WriteTo(output, _repeated_playerIds_codec);

            apiNames_.WriteTo(output, _repeated_apiNames_codec);

            if (AddLeaderboardStats)

            {

                output.WriteRawTag(24);

                output.WriteBool(AddLeaderboardStats);

            }

        }

        public int CalculateSize()

        {

            int size = 0;

            size += playerIds_.CalculateSize(_repeated_playerIds_codec);

            size += apiNames_.CalculateSize(_repeated_apiNames_codec);

            if (AddLeaderboardStats) size += 1 + 1;

            return size;

        }

        public void MergeFrom(GetPlayersStatsRequest other)

        {

            if (other == null) return;

            playerIds_.Add(other.playerIds_);

            apiNames_.Add(other.apiNames_);

            if (other.AddLeaderboardStats) AddLeaderboardStats = other.AddLeaderboardStats;

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 10: playerIds_.AddEntriesFrom(input, _repeated_playerIds_codec); break;

                    case 18: apiNames_.AddEntriesFrom(input, _repeated_apiNames_codec); break;

                    case 24: AddLeaderboardStats = input.ReadBool(); break;

                    default: input.SkipLastField(); break;

                }

            }

        }

    }



    public sealed partial class GetPlayersStatsResponse : pb::IMessage<GetPlayersStatsResponse> {

        private static readonly pb::MessageParser<GetPlayersStatsResponse> _parser = new pb::MessageParser<GetPlayersStatsResponse>(() => new GetPlayersStatsResponse());

        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.PlayerStats> _repeated_playersStats_codec =

            pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.PlayerStats.Parser);

        public static pb::MessageParser<GetPlayersStatsResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private readonly pbc::RepeatedField<PlayerStats> playersStats_ = new pbc::RepeatedField<PlayerStats>();

        public pbc::RepeatedField<PlayerStats> PlayersStats => playersStats_;

        // Legacy alias used by existing server code.

        public pbc::RepeatedField<PlayerStats> PlayerStats => playersStats_;



        public GetPlayersStatsResponse() { }

        public GetPlayersStatsResponse(GetPlayersStatsResponse other) : this() { playersStats_.Add(other.playersStats_); }

        public GetPlayersStatsResponse Clone() => new GetPlayersStatsResponse(this);

        public override bool Equals(object other) => Equals(other as GetPlayersStatsResponse);

        public bool Equals(GetPlayersStatsResponse other) => other != null && playersStats_.Equals(other.playersStats_);

        public override int GetHashCode() => playersStats_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) => playersStats_.WriteTo(output, _repeated_playersStats_codec);

        public int CalculateSize() => playersStats_.CalculateSize(_repeated_playersStats_codec);

        public void MergeFrom(GetPlayersStatsResponse other)

        {

            if (other == null) return;

            playersStats_.Add(other.playersStats_);

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                if (tag == 10) playersStats_.AddEntriesFrom(input, _repeated_playersStats_codec);

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class StorePlayerStatsRequest : pb::IMessage<StorePlayerStatsRequest> {

        private static readonly pb::MessageParser<StorePlayerStatsRequest> _parser = new pb::MessageParser<StorePlayerStatsRequest>(() => new StorePlayerStatsRequest());

        public static pb::MessageParser<StorePlayerStatsRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private StorePlayerStats storePlayerStats_;

        public StorePlayerStats StorePlayerStats

        {

            get => storePlayerStats_;

            set => storePlayerStats_ = value;

        }



        // Legacy accessors used by older server code paths.

        public string PlayerId

        {

            get => storePlayerStats_?.PlayerId ?? string.Empty;

            set => EnsureStorePlayerStats().PlayerId = value ?? string.Empty;

        }

        public pbc::RepeatedField<StorePlayerStat> Stats => EnsureStorePlayerStats().Stats;



        public StorePlayerStatsRequest() { }

        public StorePlayerStatsRequest(StorePlayerStatsRequest other) : this()

        {

            storePlayerStats_ = other.storePlayerStats_ != null ? other.storePlayerStats_.Clone() : null;

        }

        public StorePlayerStatsRequest Clone() => new StorePlayerStatsRequest(this);

        public override bool Equals(object other) => Equals(other as StorePlayerStatsRequest);

        public bool Equals(StorePlayerStatsRequest other) => other != null && object.Equals(StorePlayerStats, other.StorePlayerStats);

        public override int GetHashCode() => storePlayerStats_ != null ? StorePlayerStats.GetHashCode() : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (storePlayerStats_ != null)

            {

                output.WriteRawTag(10);

                output.WriteMessage(StorePlayerStats);

            }

        }

        public int CalculateSize()

        {

            int size = 0;

            if (storePlayerStats_ != null) size += 1 + pb::CodedOutputStream.ComputeMessageSize(StorePlayerStats);

            return size;

        }

        public void MergeFrom(StorePlayerStatsRequest other)

        {

            if (other == null) return;

            if (other.storePlayerStats_ != null)

            {

                if (storePlayerStats_ == null) StorePlayerStats = new StorePlayerStats();

                StorePlayerStats.MergeFrom(other.StorePlayerStats);

            }

        }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                if (tag == 10)

                {

                    if (storePlayerStats_ == null) StorePlayerStats = new StorePlayerStats();

                    input.ReadMessage(StorePlayerStats);

                }

                else input.SkipLastField();

            }

        }



        private StorePlayerStats EnsureStorePlayerStats()

        {

            if (storePlayerStats_ == null)

            {

                storePlayerStats_ = new StorePlayerStats();

            }

            return storePlayerStats_;

        }

    }



    public sealed partial class CreateSaleRequest : pb::IMessage<CreateSaleRequest> {

        private static readonly pb::MessageParser<CreateSaleRequest> _parser = new pb::MessageParser<CreateSaleRequest>(() => new CreateSaleRequest());

        public static pb::MessageParser<CreateSaleRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public CreateSaleRequest() {}

        public CreateSaleRequest(CreateSaleRequest other) : this() { itemId_ = other.itemId_; price_ = other.price_; }

        public CreateSaleRequest Clone() => new CreateSaleRequest(this);

        private int itemId_;

        public int ItemId { get => itemId_; set => itemId_ = value; }

        private float price_;

        public float Price { get => price_; set => price_ = value; }

        public override bool Equals(object other) => Equals(other as CreateSaleRequest);

        public bool Equals(CreateSaleRequest other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(CreateSaleRequest other) { if (other != null) { itemId_ = other.itemId_; price_ = other.price_; } }

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    public sealed partial class CreateSaleResponse : pb::IMessage<CreateSaleResponse> {

        private static readonly pb::MessageParser<CreateSaleResponse> _parser = new pb::MessageParser<CreateSaleResponse>(() => new CreateSaleResponse());

        public static pb::MessageParser<CreateSaleResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        public CreateSaleResponse() {}

        public CreateSaleResponse(CreateSaleResponse other) : this() { requestId_ = other.requestId_; }

        public CreateSaleResponse Clone() => new CreateSaleResponse(this);

        private string requestId_ = "";

        public string RequestId { get => requestId_; set => requestId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public override bool Equals(object other) => Equals(other as CreateSaleResponse);

        public bool Equals(CreateSaleResponse other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {}

        public int CalculateSize() => 0;

        public void MergeFrom(CreateSaleResponse other) { if (other != null) { requestId_ = other.requestId_; } }

        public void MergeFrom(pb::CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }

    }



    // ============ MatchmakingRemoteService — API request wrappers (0.19.0) ============



    public sealed partial class CreateLobbyRequest : pb::IMessage<CreateLobbyRequest>

    {

        private static readonly pb::MessageParser<CreateLobbyRequest> _parser = new pb::MessageParser<CreateLobbyRequest>(() => new CreateLobbyRequest());

        public static pb::MessageParser<CreateLobbyRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string name_ = "";

        private LobbyType lobbyType_ = 0;

        private int maxMembers_;

        public string Name { get => name_; set => name_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public LobbyType LobbyType { get => lobbyType_; set => lobbyType_ = value; }

        public int MaxMembers { get => maxMembers_; set => maxMembers_ = value; }

        public CreateLobbyRequest() { }

        public CreateLobbyRequest(CreateLobbyRequest other) : this() { name_ = other.name_; lobbyType_ = other.lobbyType_; maxMembers_ = other.maxMembers_; }

        public CreateLobbyRequest Clone() => new CreateLobbyRequest(this);

        public override bool Equals(object other) => Equals(other as CreateLobbyRequest);

        public bool Equals(CreateLobbyRequest other) => other != null && Name == other.Name && LobbyType == other.LobbyType && MaxMembers == other.MaxMembers;

        public override int GetHashCode() { int h = 1; h ^= Name.GetHashCode(); h ^= LobbyType.GetHashCode(); h ^= MaxMembers.GetHashCode(); return h; }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (name_.Length != 0) { output.WriteRawTag(10); output.WriteString(Name); } if (lobbyType_ != 0) { output.WriteRawTag(16); output.WriteEnum((int)LobbyType); } if (maxMembers_ != 0) { output.WriteRawTag(24); output.WriteInt32(MaxMembers); } }

        public int CalculateSize() { int s = 0; if (name_.Length != 0) s += 1 + pb::CodedOutputStream.ComputeStringSize(Name); if (lobbyType_ != 0) s += 1 + pb::CodedOutputStream.ComputeEnumSize((int)LobbyType); if (maxMembers_ != 0) s += 1 + pb::CodedOutputStream.ComputeInt32Size(MaxMembers); return s; }

        public void MergeFrom(CreateLobbyRequest other) { if (other == null) return; if (other.name_.Length != 0) Name = other.name_; if (other.lobbyType_ != 0) LobbyType = other.lobbyType_; if (other.maxMembers_ != 0) MaxMembers = other.maxMembers_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { switch (tag) { case 10: Name = input.ReadString(); break; case 16: lobbyType_ = (LobbyType)input.ReadEnum(); break; case 24: MaxMembers = input.ReadInt32(); break; default: input.SkipLastField(); break; } } }

    }



    public sealed partial class CreateLobbyWithSpectatorsRequest : pb::IMessage<CreateLobbyWithSpectatorsRequest>

    {

        private static readonly pb::MessageParser<CreateLobbyWithSpectatorsRequest> _parser = new pb::MessageParser<CreateLobbyWithSpectatorsRequest>(() => new CreateLobbyWithSpectatorsRequest());

        public static pb::MessageParser<CreateLobbyWithSpectatorsRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string name_ = "";

        private LobbyType lobbyType_ = 0;

        private int maxMembers_;

        private int maxSpectators_;

        public string Name { get => name_; set => name_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public LobbyType LobbyType { get => lobbyType_; set => lobbyType_ = value; }

        public int MaxMembers { get => maxMembers_; set => maxMembers_ = value; }

        public int MaxSpectators { get => maxSpectators_; set => maxSpectators_ = value; }

        public CreateLobbyWithSpectatorsRequest() { }

        public CreateLobbyWithSpectatorsRequest(CreateLobbyWithSpectatorsRequest other) : this() { name_ = other.name_; lobbyType_ = other.lobbyType_; maxMembers_ = other.maxMembers_; maxSpectators_ = other.maxSpectators_; }

        public CreateLobbyWithSpectatorsRequest Clone() => new CreateLobbyWithSpectatorsRequest(this);

        public override bool Equals(object other) => Equals(other as CreateLobbyWithSpectatorsRequest);

        public bool Equals(CreateLobbyWithSpectatorsRequest other) => other != null && Name == other.Name && LobbyType == other.LobbyType && MaxMembers == other.MaxMembers && MaxSpectators == other.MaxSpectators;

        public override int GetHashCode() { int h = 1; h ^= Name.GetHashCode(); h ^= LobbyType.GetHashCode(); h ^= MaxMembers.GetHashCode(); h ^= MaxSpectators.GetHashCode(); return h; }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (name_.Length != 0) { output.WriteRawTag(10); output.WriteString(Name); } if (lobbyType_ != 0) { output.WriteRawTag(16); output.WriteEnum((int)LobbyType); } if (maxMembers_ != 0) { output.WriteRawTag(24); output.WriteInt32(MaxMembers); } if (maxSpectators_ != 0) { output.WriteRawTag(32); output.WriteInt32(MaxSpectators); } }

        public int CalculateSize() { int s = 0; if (name_.Length != 0) s += 1 + pb::CodedOutputStream.ComputeStringSize(Name); if (lobbyType_ != 0) s += 1 + pb::CodedOutputStream.ComputeEnumSize((int)LobbyType); if (maxMembers_ != 0) s += 1 + pb::CodedOutputStream.ComputeInt32Size(MaxMembers); if (maxSpectators_ != 0) s += 1 + pb::CodedOutputStream.ComputeInt32Size(MaxSpectators); return s; }

        public void MergeFrom(CreateLobbyWithSpectatorsRequest other) { if (other == null) return; if (other.name_.Length != 0) Name = other.name_; if (other.lobbyType_ != 0) LobbyType = other.lobbyType_; if (other.maxMembers_ != 0) MaxMembers = other.maxMembers_; if (other.maxSpectators_ != 0) MaxSpectators = other.maxSpectators_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { switch (tag) { case 10: Name = input.ReadString(); break; case 16: lobbyType_ = (LobbyType)input.ReadEnum(); break; case 24: MaxMembers = input.ReadInt32(); break; case 32: MaxSpectators = input.ReadInt32(); break; default: input.SkipLastField(); break; } } }

    }



    public sealed partial class SetLobbyDataRequest : pb::IMessage<SetLobbyDataRequest>

    {

        private static readonly pb::MessageParser<SetLobbyDataRequest> _parser = new pb::MessageParser<SetLobbyDataRequest>(() => new SetLobbyDataRequest());

        public static pb::MessageParser<SetLobbyDataRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private global::Axlebolt.Bolt.Protobuf.Dictionary data_;

        public global::Axlebolt.Bolt.Protobuf.Dictionary Data { get => data_; set => data_ = value; }

        public SetLobbyDataRequest() { }

        public SetLobbyDataRequest(SetLobbyDataRequest other) : this() { data_ = other.data_ != null ? other.data_.Clone() : null; }

        public SetLobbyDataRequest Clone() => new SetLobbyDataRequest(this);

        public override bool Equals(object other) => Equals(other as SetLobbyDataRequest);

        public bool Equals(SetLobbyDataRequest other) => other != null && object.Equals(Data, other.Data);

        public override int GetHashCode() => data_ != null ? Data.GetHashCode() : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (data_ != null) { output.WriteRawTag(10); output.WriteMessage(Data); } }

        public int CalculateSize() { int s = 0; if (data_ != null) s += 1 + pb::CodedOutputStream.ComputeMessageSize(Data); return s; }

        public void MergeFrom(SetLobbyDataRequest other) { if (other == null) return; if (other.data_ != null) { if (data_ == null) data_ = new global::Axlebolt.Bolt.Protobuf.Dictionary(); Data.MergeFrom(other.Data); } }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) { if (data_ == null) data_ = new global::Axlebolt.Bolt.Protobuf.Dictionary(); input.ReadMessage(data_); } else input.SkipLastField(); } }

    }



    public sealed partial class SetLobbyJoinableRequest : pb::IMessage<SetLobbyJoinableRequest>

    {

        private static readonly pb::MessageParser<SetLobbyJoinableRequest> _parser = new pb::MessageParser<SetLobbyJoinableRequest>(() => new SetLobbyJoinableRequest());

        public static pb::MessageParser<SetLobbyJoinableRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private bool joinable_;

        public bool Joinable { get => joinable_; set => joinable_ = value; }

        public SetLobbyJoinableRequest() { }

        public SetLobbyJoinableRequest(SetLobbyJoinableRequest other) : this() { joinable_ = other.joinable_; }

        public SetLobbyJoinableRequest Clone() => new SetLobbyJoinableRequest(this);

        public override bool Equals(object other) => Equals(other as SetLobbyJoinableRequest);

        public bool Equals(SetLobbyJoinableRequest other) => other != null && Joinable == other.Joinable;

        public override int GetHashCode() => Joinable ? 1 : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (joinable_) { output.WriteRawTag(8); output.WriteBool(Joinable); } }

        public int CalculateSize() => Joinable ? 2 : 0;

        public void MergeFrom(SetLobbyJoinableRequest other) { if (other != null) joinable_ = other.joinable_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 8) Joinable = input.ReadBool(); else input.SkipLastField(); } }

    }



    public sealed partial class SetLobbyTypeRequest : pb::IMessage<SetLobbyTypeRequest>

    {

        private static readonly pb::MessageParser<SetLobbyTypeRequest> _parser = new pb::MessageParser<SetLobbyTypeRequest>(() => new SetLobbyTypeRequest());

        public static pb::MessageParser<SetLobbyTypeRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private LobbyType lobbyType_;

        public LobbyType LobbyType { get => lobbyType_; set => lobbyType_ = value; }

        public SetLobbyTypeRequest() { }

        public SetLobbyTypeRequest(SetLobbyTypeRequest other) : this() { lobbyType_ = other.lobbyType_; }

        public SetLobbyTypeRequest Clone() => new SetLobbyTypeRequest(this);

        public override bool Equals(object other) => Equals(other as SetLobbyTypeRequest);

        public bool Equals(SetLobbyTypeRequest other) => other != null && LobbyType == other.LobbyType;

        public override int GetHashCode() => (int)LobbyType;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (lobbyType_ != 0) { output.WriteRawTag(8); output.WriteEnum((int)LobbyType); } }

        public int CalculateSize() => lobbyType_ != 0 ? 2 : 0;

        public void MergeFrom(SetLobbyTypeRequest other) { if (other != null) lobbyType_ = other.lobbyType_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 8) lobbyType_ = (LobbyType)input.ReadEnum(); else input.SkipLastField(); } }

    }



    public sealed partial class SetLobbyNameRequest : pb::IMessage<SetLobbyNameRequest>

    {

        private static readonly pb::MessageParser<SetLobbyNameRequest> _parser = new pb::MessageParser<SetLobbyNameRequest>(() => new SetLobbyNameRequest());

        public static pb::MessageParser<SetLobbyNameRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string name_ = "";

        public string Name { get => name_; set => name_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public SetLobbyNameRequest() { }

        public SetLobbyNameRequest(SetLobbyNameRequest other) : this() { name_ = other.name_; }

        public SetLobbyNameRequest Clone() => new SetLobbyNameRequest(this);

        public override bool Equals(object other) => Equals(other as SetLobbyNameRequest);

        public bool Equals(SetLobbyNameRequest other) => other != null && Name == other.Name;

        public override int GetHashCode() => Name.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (name_.Length != 0) { output.WriteRawTag(10); output.WriteString(Name); } }

        public int CalculateSize() => name_.Length != 0 ? 1 + pb::CodedOutputStream.ComputeStringSize(Name) : 0;

        public void MergeFrom(SetLobbyNameRequest other) { if (other != null && other.name_.Length != 0) Name = other.name_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) Name = input.ReadString(); else input.SkipLastField(); } }

    }



    public sealed partial class SetLobbyMaxMembersRequest : pb::IMessage<SetLobbyMaxMembersRequest>

    {

        private static readonly pb::MessageParser<SetLobbyMaxMembersRequest> _parser = new pb::MessageParser<SetLobbyMaxMembersRequest>(() => new SetLobbyMaxMembersRequest());

        public static pb::MessageParser<SetLobbyMaxMembersRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private int maxMembers_;

        public int MaxMembers { get => maxMembers_; set => maxMembers_ = value; }

        public SetLobbyMaxMembersRequest() { }

        public SetLobbyMaxMembersRequest(SetLobbyMaxMembersRequest other) : this() { maxMembers_ = other.maxMembers_; }

        public SetLobbyMaxMembersRequest Clone() => new SetLobbyMaxMembersRequest(this);

        public override bool Equals(object other) => Equals(other as SetLobbyMaxMembersRequest);

        public bool Equals(SetLobbyMaxMembersRequest other) => other != null && MaxMembers == other.MaxMembers;

        public override int GetHashCode() => MaxMembers;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (maxMembers_ != 0) { output.WriteRawTag(8); output.WriteInt32(MaxMembers); } }

        public int CalculateSize() => maxMembers_ != 0 ? 1 + pb::CodedOutputStream.ComputeInt32Size(MaxMembers) : 0;

        public void MergeFrom(SetLobbyMaxMembersRequest other) { if (other != null) maxMembers_ = other.maxMembers_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 8) MaxMembers = input.ReadInt32(); else input.SkipLastField(); } }

    }



    public sealed partial class SetLobbyMaxSpectatorsRequest : pb::IMessage<SetLobbyMaxSpectatorsRequest>

    {

        private static readonly pb::MessageParser<SetLobbyMaxSpectatorsRequest> _parser = new pb::MessageParser<SetLobbyMaxSpectatorsRequest>(() => new SetLobbyMaxSpectatorsRequest());

        public static pb::MessageParser<SetLobbyMaxSpectatorsRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private int maxSpectators_;

        public int MaxSpectators { get => maxSpectators_; set => maxSpectators_ = value; }

        public SetLobbyMaxSpectatorsRequest() { }

        public SetLobbyMaxSpectatorsRequest(SetLobbyMaxSpectatorsRequest other) : this() { maxSpectators_ = other.maxSpectators_; }

        public SetLobbyMaxSpectatorsRequest Clone() => new SetLobbyMaxSpectatorsRequest(this);

        public override bool Equals(object other) => Equals(other as SetLobbyMaxSpectatorsRequest);

        public bool Equals(SetLobbyMaxSpectatorsRequest other) => other != null && MaxSpectators == other.MaxSpectators;

        public override int GetHashCode() => MaxSpectators;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (maxSpectators_ != 0) { output.WriteRawTag(8); output.WriteInt32(MaxSpectators); } }

        public int CalculateSize() => maxSpectators_ != 0 ? 1 + pb::CodedOutputStream.ComputeInt32Size(MaxSpectators) : 0;

        public void MergeFrom(SetLobbyMaxSpectatorsRequest other) { if (other != null) maxSpectators_ = other.maxSpectators_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 8) MaxSpectators = input.ReadInt32(); else input.SkipLastField(); } }

    }



    public sealed partial class SetLobbyOwnerRequest : pb::IMessage<SetLobbyOwnerRequest>

    {

        private static readonly pb::MessageParser<SetLobbyOwnerRequest> _parser = new pb::MessageParser<SetLobbyOwnerRequest>(() => new SetLobbyOwnerRequest());

        public static pb::MessageParser<SetLobbyOwnerRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string playerId_ = "";

        public string PlayerId { get => playerId_; set => playerId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public SetLobbyOwnerRequest() { }

        public SetLobbyOwnerRequest(SetLobbyOwnerRequest other) : this() { playerId_ = other.playerId_; }

        public SetLobbyOwnerRequest Clone() => new SetLobbyOwnerRequest(this);

        public override bool Equals(object other) => Equals(other as SetLobbyOwnerRequest);

        public bool Equals(SetLobbyOwnerRequest other) => other != null && PlayerId == other.PlayerId;

        public override int GetHashCode() => PlayerId.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (playerId_.Length != 0) { output.WriteRawTag(10); output.WriteString(PlayerId); } }

        public int CalculateSize() => playerId_.Length != 0 ? 1 + pb::CodedOutputStream.ComputeStringSize(PlayerId) : 0;

        public void MergeFrom(SetLobbyOwnerRequest other) { if (other != null && other.playerId_.Length != 0) PlayerId = other.playerId_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) PlayerId = input.ReadString(); else input.SkipLastField(); } }

    }



    public sealed partial class GetLobbyOwnerRequest : pb::IMessage<GetLobbyOwnerRequest>

    {

        private static readonly pb::MessageParser<GetLobbyOwnerRequest> _parser = new pb::MessageParser<GetLobbyOwnerRequest>(() => new GetLobbyOwnerRequest());

        public static pb::MessageParser<GetLobbyOwnerRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string lobbyId_ = "";

        public string LobbyId { get => lobbyId_; set => lobbyId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public GetLobbyOwnerRequest() { }

        public GetLobbyOwnerRequest(GetLobbyOwnerRequest other) : this() { lobbyId_ = other.lobbyId_; }

        public GetLobbyOwnerRequest Clone() => new GetLobbyOwnerRequest(this);

        public override bool Equals(object other) => Equals(other as GetLobbyOwnerRequest);

        public bool Equals(GetLobbyOwnerRequest other) => other != null && LobbyId == other.LobbyId;

        public override int GetHashCode() => LobbyId.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (lobbyId_.Length != 0) { output.WriteRawTag(10); output.WriteString(LobbyId); } }

        public int CalculateSize() => lobbyId_.Length != 0 ? 1 + pb::CodedOutputStream.ComputeStringSize(LobbyId) : 0;

        public void MergeFrom(GetLobbyOwnerRequest other) { if (other != null && other.lobbyId_.Length != 0) LobbyId = other.lobbyId_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) LobbyId = input.ReadString(); else input.SkipLastField(); } }

    }



    public sealed partial class SetLobbyPhotonGameRequest : pb::IMessage<SetLobbyPhotonGameRequest>

    {

        private static readonly pb::MessageParser<SetLobbyPhotonGameRequest> _parser = new pb::MessageParser<SetLobbyPhotonGameRequest>(() => new SetLobbyPhotonGameRequest());

        public static pb::MessageParser<SetLobbyPhotonGameRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private PhotonGame photonGame_;

        public PhotonGame PhotonGame { get => photonGame_; set => photonGame_ = value; }

        public SetLobbyPhotonGameRequest() { }

        public SetLobbyPhotonGameRequest(SetLobbyPhotonGameRequest other) : this() { photonGame_ = other.photonGame_ != null ? other.photonGame_.Clone() : null; }

        public SetLobbyPhotonGameRequest Clone() => new SetLobbyPhotonGameRequest(this);

        public override bool Equals(object other) => Equals(other as SetLobbyPhotonGameRequest);

        public bool Equals(SetLobbyPhotonGameRequest other) => other != null && object.Equals(PhotonGame, other.PhotonGame);

        public override int GetHashCode() => photonGame_ != null ? PhotonGame.GetHashCode() : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (photonGame_ != null) { output.WriteRawTag(10); output.WriteMessage(PhotonGame); } }

        public int CalculateSize() => photonGame_ != null ? 1 + pb::CodedOutputStream.ComputeMessageSize(PhotonGame) : 0;

        public void MergeFrom(SetLobbyPhotonGameRequest other) { if (other != null && other.photonGame_ != null) { if (photonGame_ == null) photonGame_ = new PhotonGame(); PhotonGame.MergeFrom(other.PhotonGame); } }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) { if (photonGame_ == null) photonGame_ = new PhotonGame(); input.ReadMessage(photonGame_); } else input.SkipLastField(); } }

    }



    public sealed partial class SetLobbyGameServerRequest : pb::IMessage<SetLobbyGameServerRequest>

    {

        private static readonly pb::MessageParser<SetLobbyGameServerRequest> _parser = new pb::MessageParser<SetLobbyGameServerRequest>(() => new SetLobbyGameServerRequest());

        public static pb::MessageParser<SetLobbyGameServerRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private GameServer gameServer_;

        public GameServer GameServer { get => gameServer_; set => gameServer_ = value; }

        public SetLobbyGameServerRequest() { }

        public SetLobbyGameServerRequest(SetLobbyGameServerRequest other) : this() { gameServer_ = other.gameServer_ != null ? other.gameServer_.Clone() : null; }

        public SetLobbyGameServerRequest Clone() => new SetLobbyGameServerRequest(this);

        public override bool Equals(object other) => Equals(other as SetLobbyGameServerRequest);

        public bool Equals(SetLobbyGameServerRequest other) => other != null && object.Equals(GameServer, other.GameServer);

        public override int GetHashCode() => gameServer_ != null ? GameServer.GetHashCode() : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (gameServer_ != null) { output.WriteRawTag(10); output.WriteMessage(GameServer); } }

        public int CalculateSize() => gameServer_ != null ? 1 + pb::CodedOutputStream.ComputeMessageSize(GameServer) : 0;

        public void MergeFrom(SetLobbyGameServerRequest other) { if (other != null && other.gameServer_ != null) { if (gameServer_ == null) gameServer_ = new GameServer(); GameServer.MergeFrom(other.GameServer); } }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) { if (gameServer_ == null) gameServer_ = new GameServer(); input.ReadMessage(gameServer_); } else input.SkipLastField(); } }

    }



    public sealed partial class GetLobbyRequest : pb::IMessage<GetLobbyRequest>

    {

        private static readonly pb::MessageParser<GetLobbyRequest> _parser = new pb::MessageParser<GetLobbyRequest>(() => new GetLobbyRequest());

        public static pb::MessageParser<GetLobbyRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string lobbyId_ = "";

        public string LobbyId { get => lobbyId_; set => lobbyId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public GetLobbyRequest() { }

        public GetLobbyRequest(GetLobbyRequest other) : this() { lobbyId_ = other.lobbyId_; }

        public GetLobbyRequest Clone() => new GetLobbyRequest(this);

        public override bool Equals(object other) => Equals(other as GetLobbyRequest);

        public bool Equals(GetLobbyRequest other) => other != null && LobbyId == other.LobbyId;

        public override int GetHashCode() => LobbyId.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (lobbyId_.Length != 0) { output.WriteRawTag(10); output.WriteString(LobbyId); } }

        public int CalculateSize() => lobbyId_.Length != 0 ? 1 + pb::CodedOutputStream.ComputeStringSize(LobbyId) : 0;

        public void MergeFrom(GetLobbyRequest other) { if (other != null && other.lobbyId_.Length != 0) LobbyId = other.lobbyId_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) LobbyId = input.ReadString(); else input.SkipLastField(); } }

    }



    public sealed partial class GetLobbyMembersRequest : pb::IMessage<GetLobbyMembersRequest>

    {

        private static readonly pb::MessageParser<GetLobbyMembersRequest> _parser = new pb::MessageParser<GetLobbyMembersRequest>(() => new GetLobbyMembersRequest());

        public static pb::MessageParser<GetLobbyMembersRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string lobbyId_ = "";

        public string LobbyId { get => lobbyId_; set => lobbyId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public GetLobbyMembersRequest() { }

        public GetLobbyMembersRequest(GetLobbyMembersRequest other) : this() { lobbyId_ = other.lobbyId_; }

        public GetLobbyMembersRequest Clone() => new GetLobbyMembersRequest(this);

        public override bool Equals(object other) => Equals(other as GetLobbyMembersRequest);

        public bool Equals(GetLobbyMembersRequest other) => other != null && LobbyId == other.LobbyId;

        public override int GetHashCode() => LobbyId.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (lobbyId_.Length != 0) { output.WriteRawTag(10); output.WriteString(LobbyId); } }

        public int CalculateSize() => lobbyId_.Length != 0 ? 1 + pb::CodedOutputStream.ComputeStringSize(LobbyId) : 0;

        public void MergeFrom(GetLobbyMembersRequest other) { if (other != null && other.lobbyId_.Length != 0) LobbyId = other.lobbyId_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) LobbyId = input.ReadString(); else input.SkipLastField(); } }

    }



    public sealed partial class GetLobbyPhotonGameRequest : pb::IMessage<GetLobbyPhotonGameRequest>

    {

        private static readonly pb::MessageParser<GetLobbyPhotonGameRequest> _parser = new pb::MessageParser<GetLobbyPhotonGameRequest>(() => new GetLobbyPhotonGameRequest());

        public static pb::MessageParser<GetLobbyPhotonGameRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string lobbyId_ = "";

        public string LobbyId { get => lobbyId_; set => lobbyId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public GetLobbyPhotonGameRequest() { }

        public GetLobbyPhotonGameRequest(GetLobbyPhotonGameRequest other) : this() { lobbyId_ = other.lobbyId_; }

        public GetLobbyPhotonGameRequest Clone() => new GetLobbyPhotonGameRequest(this);

        public override bool Equals(object other) => Equals(other as GetLobbyPhotonGameRequest);

        public bool Equals(GetLobbyPhotonGameRequest other) => other != null && LobbyId == other.LobbyId;

        public override int GetHashCode() => LobbyId.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (lobbyId_.Length != 0) { output.WriteRawTag(10); output.WriteString(LobbyId); } }

        public int CalculateSize() => lobbyId_.Length != 0 ? 1 + pb::CodedOutputStream.ComputeStringSize(LobbyId) : 0;

        public void MergeFrom(GetLobbyPhotonGameRequest other) { if (other != null && other.lobbyId_.Length != 0) LobbyId = other.lobbyId_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) LobbyId = input.ReadString(); else input.SkipLastField(); } }

    }



    public sealed partial class GetLobbyGameServerRequest : pb::IMessage<GetLobbyGameServerRequest>

    {

        private static readonly pb::MessageParser<GetLobbyGameServerRequest> _parser = new pb::MessageParser<GetLobbyGameServerRequest>(() => new GetLobbyGameServerRequest());

        public static pb::MessageParser<GetLobbyGameServerRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string lobbyId_ = "";

        public string LobbyId { get => lobbyId_; set => lobbyId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public GetLobbyGameServerRequest() { }

        public GetLobbyGameServerRequest(GetLobbyGameServerRequest other) : this() { lobbyId_ = other.lobbyId_; }

        public GetLobbyGameServerRequest Clone() => new GetLobbyGameServerRequest(this);

        public override bool Equals(object other) => Equals(other as GetLobbyGameServerRequest);

        public bool Equals(GetLobbyGameServerRequest other) => other != null && LobbyId == other.LobbyId;

        public override int GetHashCode() => LobbyId.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (lobbyId_.Length != 0) { output.WriteRawTag(10); output.WriteString(LobbyId); } }

        public int CalculateSize() => lobbyId_.Length != 0 ? 1 + pb::CodedOutputStream.ComputeStringSize(LobbyId) : 0;

        public void MergeFrom(GetLobbyGameServerRequest other) { if (other != null && other.lobbyId_.Length != 0) LobbyId = other.lobbyId_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) LobbyId = input.ReadString(); else input.SkipLastField(); } }

    }



    public sealed partial class SendLobbyChatMsgRequest : pb::IMessage<SendLobbyChatMsgRequest>

    {

        private static readonly pb::MessageParser<SendLobbyChatMsgRequest> _parser = new pb::MessageParser<SendLobbyChatMsgRequest>(() => new SendLobbyChatMsgRequest());

        public static pb::MessageParser<SendLobbyChatMsgRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string message_ = "";

        public string Message { get => message_; set => message_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public SendLobbyChatMsgRequest() { }

        public SendLobbyChatMsgRequest(SendLobbyChatMsgRequest other) : this() { message_ = other.message_; }

        public SendLobbyChatMsgRequest Clone() => new SendLobbyChatMsgRequest(this);

        public override bool Equals(object other) => Equals(other as SendLobbyChatMsgRequest);

        public bool Equals(SendLobbyChatMsgRequest other) => other != null && Message == other.Message;

        public override int GetHashCode() => Message.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (message_.Length != 0) { output.WriteRawTag(10); output.WriteString(Message); } }

        public int CalculateSize() => message_.Length != 0 ? 1 + pb::CodedOutputStream.ComputeStringSize(Message) : 0;

        public void MergeFrom(SendLobbyChatMsgRequest other) { if (other != null && other.message_.Length != 0) Message = other.message_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) Message = input.ReadString(); else input.SkipLastField(); } }

    }



    public sealed partial class DeleteLobbyDataRequest : pb::IMessage<DeleteLobbyDataRequest>

    {

        private static readonly pb::MessageParser<DeleteLobbyDataRequest> _parser = new pb::MessageParser<DeleteLobbyDataRequest>(() => new DeleteLobbyDataRequest());

        public static pb::MessageParser<DeleteLobbyDataRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private static readonly pb::FieldCodec<string> _repeated_keys_codec = pb::FieldCodec.ForString(10);

        private readonly pbc::RepeatedField<string> keys_ = new pbc::RepeatedField<string>();

        public pbc::RepeatedField<string> Keys => keys_;

        public DeleteLobbyDataRequest() { }

        public DeleteLobbyDataRequest(DeleteLobbyDataRequest other) : this() { keys_.Add(other.keys_); }

        public DeleteLobbyDataRequest Clone() => new DeleteLobbyDataRequest(this);

        public override bool Equals(object other) => Equals(other as DeleteLobbyDataRequest);

        public bool Equals(DeleteLobbyDataRequest other) => other != null && keys_.Equals(other.keys_);

        public override int GetHashCode() => keys_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { keys_.WriteTo(output, _repeated_keys_codec); }

        public int CalculateSize() => keys_.CalculateSize(_repeated_keys_codec);

        public void MergeFrom(DeleteLobbyDataRequest other) { if (other == null) return; keys_.Add(other.keys_); }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) keys_.AddEntriesFrom(input, _repeated_keys_codec); else input.SkipLastField(); } }

    }



    public sealed partial class ChangeLobbyPlayerTypeRequest : pb::IMessage<ChangeLobbyPlayerTypeRequest>

    {

        private static readonly pb::MessageParser<ChangeLobbyPlayerTypeRequest> _parser = new pb::MessageParser<ChangeLobbyPlayerTypeRequest>(() => new ChangeLobbyPlayerTypeRequest());

        public static pb::MessageParser<ChangeLobbyPlayerTypeRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private LobbyPlayerType playerType_;

        public LobbyPlayerType PlayerType { get => playerType_; set => playerType_ = value; }

        public ChangeLobbyPlayerTypeRequest() { }

        public ChangeLobbyPlayerTypeRequest(ChangeLobbyPlayerTypeRequest other) : this() { playerType_ = other.playerType_; }

        public ChangeLobbyPlayerTypeRequest Clone() => new ChangeLobbyPlayerTypeRequest(this);

        public override bool Equals(object other) => Equals(other as ChangeLobbyPlayerTypeRequest);

        public bool Equals(ChangeLobbyPlayerTypeRequest other) => other != null && PlayerType == other.PlayerType;

        public override int GetHashCode() => (int)PlayerType;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (playerType_ != 0) { output.WriteRawTag(8); output.WriteEnum((int)PlayerType); } }

        public int CalculateSize() => playerType_ != 0 ? 2 : 0;

        public void MergeFrom(ChangeLobbyPlayerTypeRequest other) { if (other != null) playerType_ = other.playerType_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 8) playerType_ = (LobbyPlayerType)input.ReadEnum(); else input.SkipLastField(); } }

    }



    public sealed partial class ChangeLobbyOtherPlayerTypeRequest : pb::IMessage<ChangeLobbyOtherPlayerTypeRequest>

    {

        private static readonly pb::MessageParser<ChangeLobbyOtherPlayerTypeRequest> _parser = new pb::MessageParser<ChangeLobbyOtherPlayerTypeRequest>(() => new ChangeLobbyOtherPlayerTypeRequest());

        public static pb::MessageParser<ChangeLobbyOtherPlayerTypeRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string playerId_ = "";

        private LobbyPlayerType playerType_;

        public string PlayerId { get => playerId_; set => playerId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public LobbyPlayerType PlayerType { get => playerType_; set => playerType_ = value; }

        public ChangeLobbyOtherPlayerTypeRequest() { }

        public ChangeLobbyOtherPlayerTypeRequest(ChangeLobbyOtherPlayerTypeRequest other) : this() { playerId_ = other.playerId_; playerType_ = other.playerType_; }

        public ChangeLobbyOtherPlayerTypeRequest Clone() => new ChangeLobbyOtherPlayerTypeRequest(this);

        public override bool Equals(object other) => Equals(other as ChangeLobbyOtherPlayerTypeRequest);

        public bool Equals(ChangeLobbyOtherPlayerTypeRequest other) => other != null && PlayerId == other.PlayerId && PlayerType == other.PlayerType;

        public override int GetHashCode() { int h = 1; h ^= PlayerId.GetHashCode(); h ^= PlayerType.GetHashCode(); return h; }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (playerId_.Length != 0) { output.WriteRawTag(10); output.WriteString(PlayerId); } if (playerType_ != 0) { output.WriteRawTag(16); output.WriteEnum((int)PlayerType); } }

        public int CalculateSize() { int s = 0; if (playerId_.Length != 0) s += 1 + pb::CodedOutputStream.ComputeStringSize(PlayerId); if (playerType_ != 0) s += 1 + pb::CodedOutputStream.ComputeEnumSize((int)PlayerType); return s; }

        public void MergeFrom(ChangeLobbyOtherPlayerTypeRequest other) { if (other == null) return; if (other.playerId_.Length != 0) PlayerId = other.playerId_; if (other.playerType_ != 0) PlayerType = other.playerType_; }

        public void MergeFrom(pb::CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 10: PlayerId = input.ReadString(); break;
                    case 16: playerType_ = (LobbyPlayerType)input.ReadEnum(); break;
                    // Клиент 0.17: PlayerType в field 4 как length-delimited Rpc Enum (1a020802).
                    case 34:
                        playerType_ = ParseLobbyPlayerTypeFromWrappedEnum(input.ReadBytes());
                        break;
                    default: input.SkipLastField(); break;
                }
            }
        }

        internal static LobbyPlayerType ParseLobbyPlayerTypeFromWrappedEnum(Google.Protobuf.ByteString bytes)
        {
            if (bytes == null || bytes.Length == 0) return LobbyPlayerType.Any;
            try
            {
                var input = new pb::CodedInputStream(bytes.ToByteArray());
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    if ((tag & 7) == 0)
                        return (LobbyPlayerType)input.ReadEnum();
                    input.SkipLastField();
                }
            }
            catch { }
            if (bytes.Length == 1) return (LobbyPlayerType)bytes[0];
            return LobbyPlayerType.Any;
        }

    }



    public sealed partial class SearchLobbyRequest : pb::IMessage<SearchLobbyRequest>

    {

        private static readonly pb::MessageParser<SearchLobbyRequest> _parser = new pb::MessageParser<SearchLobbyRequest>(() => new SearchLobbyRequest());

        public static pb::MessageParser<SearchLobbyRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.Filter> _repeated_filters_codec = pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.Filter.Parser);

        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Filter> filters_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Filter>();

        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Filter> Filters => filters_;

        public SearchLobbyRequest() { }

        public SearchLobbyRequest(SearchLobbyRequest other) : this() { filters_.Add(other.filters_); }

        public SearchLobbyRequest Clone() => new SearchLobbyRequest(this);

        public override bool Equals(object other) => Equals(other as SearchLobbyRequest);

        public bool Equals(SearchLobbyRequest other) => other != null && filters_.Equals(other.filters_);

        public override int GetHashCode() => filters_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { filters_.WriteTo(output, _repeated_filters_codec); }

        public int CalculateSize() => filters_.CalculateSize(_repeated_filters_codec);

        public void MergeFrom(SearchLobbyRequest other) { if (other == null) return; filters_.Add(other.filters_); }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) filters_.AddEntriesFrom(input, _repeated_filters_codec); else input.SkipLastField(); } }

    }



    public sealed partial class GetGameServerPlayersRequest : pb::IMessage<GetGameServerPlayersRequest>

    {

        private static readonly pb::MessageParser<GetGameServerPlayersRequest> _parser = new pb::MessageParser<GetGameServerPlayersRequest>(() => new GetGameServerPlayersRequest());

        public static pb::MessageParser<GetGameServerPlayersRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string gameServerId_ = "";

        public string GameServerId { get => gameServerId_; set => gameServerId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public GetGameServerPlayersRequest() { }

        public GetGameServerPlayersRequest(GetGameServerPlayersRequest other) : this() { gameServerId_ = other.gameServerId_; }

        public GetGameServerPlayersRequest Clone() => new GetGameServerPlayersRequest(this);

        public override bool Equals(object other) => Equals(other as GetGameServerPlayersRequest);

        public bool Equals(GetGameServerPlayersRequest other) => other != null && GameServerId == other.GameServerId;

        public override int GetHashCode() => GameServerId.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (gameServerId_.Length != 0) { output.WriteRawTag(10); output.WriteString(GameServerId); } }

        public int CalculateSize() => gameServerId_.Length != 0 ? 1 + pb::CodedOutputStream.ComputeStringSize(GameServerId) : 0;

        public void MergeFrom(GetGameServerPlayersRequest other) { if (other != null && other.gameServerId_.Length != 0) GameServerId = other.gameServerId_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) GameServerId = input.ReadString(); else input.SkipLastField(); } }

    }



    public sealed partial class GetGameServerPlayersResponse : pb::IMessage<GetGameServerPlayersResponse>

    {

        private static readonly pb::MessageParser<GetGameServerPlayersResponse> _parser = new pb::MessageParser<GetGameServerPlayersResponse>(() => new GetGameServerPlayersResponse());

        public static pb::MessageParser<GetGameServerPlayersResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.Player> _repeated_players_codec = pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.Player.Parser);

        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Player> players_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Player>();

        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Player> Players => players_;

        public GetGameServerPlayersResponse() { }

        public GetGameServerPlayersResponse(GetGameServerPlayersResponse other) : this() { players_.Add(other.players_); }

        public GetGameServerPlayersResponse Clone() => new GetGameServerPlayersResponse(this);

        public override bool Equals(object other) => Equals(other as GetGameServerPlayersResponse);

        public bool Equals(GetGameServerPlayersResponse other) => other != null && players_.Equals(other.players_);

        public override int GetHashCode() => players_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { players_.WriteTo(output, _repeated_players_codec); }

        public int CalculateSize() => players_.CalculateSize(_repeated_players_codec);

        public void MergeFrom(GetGameServerPlayersResponse other) { if (other == null) return; players_.Add(other.players_); }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) players_.AddEntriesFrom(input, _repeated_players_codec); else input.SkipLastField(); } }

    }



    public sealed partial class GetGameServerDetailsRequest : pb::IMessage<GetGameServerDetailsRequest>

    {

        private static readonly pb::MessageParser<GetGameServerDetailsRequest> _parser = new pb::MessageParser<GetGameServerDetailsRequest>(() => new GetGameServerDetailsRequest());

        public static pb::MessageParser<GetGameServerDetailsRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string gameServerId_ = "";

        public string GameServerId { get => gameServerId_; set => gameServerId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public GetGameServerDetailsRequest() { }

        public GetGameServerDetailsRequest(GetGameServerDetailsRequest other) : this() { gameServerId_ = other.gameServerId_; }

        public GetGameServerDetailsRequest Clone() => new GetGameServerDetailsRequest(this);

        public override bool Equals(object other) => Equals(other as GetGameServerDetailsRequest);

        public bool Equals(GetGameServerDetailsRequest other) => other != null && GameServerId == other.GameServerId;

        public override int GetHashCode() => GameServerId.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (gameServerId_.Length != 0) { output.WriteRawTag(10); output.WriteString(GameServerId); } }

        public int CalculateSize() => gameServerId_.Length != 0 ? 1 + pb::CodedOutputStream.ComputeStringSize(GameServerId) : 0;

        public void MergeFrom(GetGameServerDetailsRequest other) { if (other != null && other.gameServerId_.Length != 0) GameServerId = other.gameServerId_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) GameServerId = input.ReadString(); else input.SkipLastField(); } }

    }



    public sealed partial class RequestInternetServerListRequest : pb::IMessage<RequestInternetServerListRequest>

    {

        private static readonly pb::MessageParser<RequestInternetServerListRequest> _parser = new pb::MessageParser<RequestInternetServerListRequest>(() => new RequestInternetServerListRequest());

        public static pb::MessageParser<RequestInternetServerListRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string map_ = "";

        private bool withPassword_;

        public string Map { get => map_; set => map_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        public bool WithPassword { get => withPassword_; set => withPassword_ = value; }

        public RequestInternetServerListRequest() { }

        public RequestInternetServerListRequest(RequestInternetServerListRequest other) : this() { map_ = other.map_; withPassword_ = other.withPassword_; }

        public RequestInternetServerListRequest Clone() => new RequestInternetServerListRequest(this);

        public override bool Equals(object other) => Equals(other as RequestInternetServerListRequest);

        public bool Equals(RequestInternetServerListRequest other) => other != null && Map == other.Map && WithPassword == other.WithPassword;

        public override int GetHashCode() { int h = 1; h ^= Map.GetHashCode(); h ^= WithPassword.GetHashCode(); return h; }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (map_.Length != 0) { output.WriteRawTag(10); output.WriteString(Map); } if (withPassword_) { output.WriteRawTag(16); output.WriteBool(WithPassword); } }

        public int CalculateSize() { int s = 0; if (map_.Length != 0) s += 1 + pb::CodedOutputStream.ComputeStringSize(Map); if (withPassword_) s += 2; return s; }

        public void MergeFrom(RequestInternetServerListRequest other) { if (other == null) return; if (other.map_.Length != 0) Map = other.map_; if (other.withPassword_) WithPassword = other.withPassword_; }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { switch (tag) { case 10: Map = input.ReadString(); break; case 16: WithPassword = input.ReadBool(); break; default: input.SkipLastField(); break; } } }

    }



    public sealed partial class RequestInternetServerListResponse : pb::IMessage<RequestInternetServerListResponse>

    {

        private static readonly pb::MessageParser<RequestInternetServerListResponse> _parser = new pb::MessageParser<RequestInternetServerListResponse>(() => new RequestInternetServerListResponse());

        public static pb::MessageParser<RequestInternetServerListResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.GameServerDetails> _repeated_servers_codec = pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.GameServerDetails.Parser);

        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.GameServerDetails> servers_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.GameServerDetails>();

        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.GameServerDetails> Servers => servers_;

        public RequestInternetServerListResponse() { }

        public RequestInternetServerListResponse(RequestInternetServerListResponse other) : this() { servers_.Add(other.servers_); }

        public RequestInternetServerListResponse Clone() => new RequestInternetServerListResponse(this);

        public override bool Equals(object other) => Equals(other as RequestInternetServerListResponse);

        public bool Equals(RequestInternetServerListResponse other) => other != null && servers_.Equals(other.servers_);

        public override int GetHashCode() => servers_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { servers_.WriteTo(output, _repeated_servers_codec); }

        public int CalculateSize() => servers_.CalculateSize(_repeated_servers_codec);

        public void MergeFrom(RequestInternetServerListResponse other) { if (other == null) return; servers_.Add(other.servers_); }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) servers_.AddEntriesFrom(input, _repeated_servers_codec); else input.SkipLastField(); } }

    }



    // ============ MatchmakingRemoteService — lobby list responses ============

    // Клиент ожидает SearchLobbyResponse { lobbies } и RequestLobbyListResponse { lobbies }

    // Оба содержат RepeatedField<Lobby> с тегом 10 (field 1)



    // RequestLobbyListRequest { distanceFilter, filters[] }

    public sealed partial class RequestLobbyListRequest : pb::IMessage<RequestLobbyListRequest>

    {

        private static readonly pb::MessageParser<RequestLobbyListRequest> _parser =

            new pb::MessageParser<RequestLobbyListRequest>(() => new RequestLobbyListRequest());

        public static pb::MessageParser<RequestLobbyListRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private LobbyDistanceFilter distanceFilter_;

        public LobbyDistanceFilter DistanceFilter { get => distanceFilter_; set => distanceFilter_ = value; }



        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.Filter> _repeated_filters_codec =

            pb::FieldCodec.ForMessage(18, global::Axlebolt.Bolt.Protobuf.Filter.Parser);

        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Filter> filters_ =

            new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Filter>();

        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Filter> Filters => filters_;



        public RequestLobbyListRequest() { }

        public RequestLobbyListRequest(RequestLobbyListRequest other) : this()

        {

            distanceFilter_ = other.distanceFilter_;

            filters_.Add(other.filters_);

        }

        public RequestLobbyListRequest Clone() => new RequestLobbyListRequest(this);

        public override bool Equals(object other) => Equals(other as RequestLobbyListRequest);

        public bool Equals(RequestLobbyListRequest other) => other != null && DistanceFilter == other.DistanceFilter && filters_.Equals(other.filters_);

        public override int GetHashCode() { int h = 1; h ^= DistanceFilter.GetHashCode(); h ^= filters_.GetHashCode(); return h; }

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output)

        {

            if (distanceFilter_ != LobbyDistanceFilter.Close) { output.WriteRawTag(8); output.WriteEnum((int)distanceFilter_); }

            filters_.WriteTo(output, _repeated_filters_codec);

        }

        public int CalculateSize()

        {

            int size = 0;

            if (distanceFilter_ != LobbyDistanceFilter.Close) size += 1 + pb::CodedOutputStream.ComputeEnumSize((int)distanceFilter_);

            size += filters_.CalculateSize(_repeated_filters_codec);

            return size;

        }

        public void MergeFrom(RequestLobbyListRequest other) { if (other == null) return; if (other.distanceFilter_ != LobbyDistanceFilter.Close) distanceFilter_ = other.distanceFilter_; filters_.Add(other.filters_); }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                switch (tag)

                {

                    case 8: distanceFilter_ = (LobbyDistanceFilter)input.ReadEnum(); break;

                    case 18: filters_.AddEntriesFrom(input, _repeated_filters_codec); break;

                    default: input.SkipLastField(); break;

                }

            }

        }

    }



    public sealed partial class SearchLobbyResponse : pb::IMessage<SearchLobbyResponse>

    {

        private static readonly pb::MessageParser<SearchLobbyResponse> _parser =

            new pb::MessageParser<SearchLobbyResponse>(() => new SearchLobbyResponse());

        public static pb::MessageParser<SearchLobbyResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.Lobby> _repeated_lobbies_codec =

            pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.Lobby.Parser);

        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Lobby> lobbies_ =

            new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Lobby>();



        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Lobby> Lobbies => lobbies_;



        public SearchLobbyResponse() { }

        public SearchLobbyResponse(SearchLobbyResponse other) : this() { lobbies_.Add(other.lobbies_); }

        public SearchLobbyResponse Clone() => new SearchLobbyResponse(this);

        public override bool Equals(object other) => Equals(other as SearchLobbyResponse);

        public bool Equals(SearchLobbyResponse other) => other != null && lobbies_.Equals(other.lobbies_);

        public override int GetHashCode() => lobbies_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { lobbies_.WriteTo(output, _repeated_lobbies_codec); }

        public int CalculateSize() => lobbies_.CalculateSize(_repeated_lobbies_codec);

        public void MergeFrom(SearchLobbyResponse other) { if (other == null) return; lobbies_.Add(other.lobbies_); }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                if (tag == 10) lobbies_.AddEntriesFrom(input, _repeated_lobbies_codec);

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class RequestLobbyListResponse : pb::IMessage<RequestLobbyListResponse>

    {

        private static readonly pb::MessageParser<RequestLobbyListResponse> _parser =

            new pb::MessageParser<RequestLobbyListResponse>(() => new RequestLobbyListResponse());

        public static pb::MessageParser<RequestLobbyListResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.Lobby> _repeated_lobbies_codec =

            pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.Lobby.Parser);

        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Lobby> lobbies_ =

            new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Lobby>();



        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Lobby> Lobbies => lobbies_;



        public RequestLobbyListResponse() { }

        public RequestLobbyListResponse(RequestLobbyListResponse other) : this() { lobbies_.Add(other.lobbies_); }

        public RequestLobbyListResponse Clone() => new RequestLobbyListResponse(this);

        public override bool Equals(object other) => Equals(other as RequestLobbyListResponse);

        public bool Equals(RequestLobbyListResponse other) => other != null && lobbies_.Equals(other.lobbies_);

        public override int GetHashCode() => lobbies_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { lobbies_.WriteTo(output, _repeated_lobbies_codec); }

        public int CalculateSize() => lobbies_.CalculateSize(_repeated_lobbies_codec);

        public void MergeFrom(RequestLobbyListResponse other) { if (other == null) return; lobbies_.Add(other.lobbies_); }

        public void MergeFrom(pb::CodedInputStream input)

        {

            uint tag;

            while ((tag = input.ReadTag()) != 0)

            {

                if (tag == 10) lobbies_.AddEntriesFrom(input, _repeated_lobbies_codec);

                else input.SkipLastField();

            }

        }

    }



    // ============ MatchmakingRemoteEventListener — wrapper event types ============



    public sealed partial class OnReceivedInviteToLobbyEvent : pb::IMessage<OnReceivedInviteToLobbyEvent>

    {

        private static readonly pb::MessageParser<OnReceivedInviteToLobbyEvent> _parser = new pb::MessageParser<OnReceivedInviteToLobbyEvent>(() => new OnReceivedInviteToLobbyEvent());

        public static pb::MessageParser<OnReceivedInviteToLobbyEvent> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private global::Axlebolt.Bolt.Protobuf.LobbyInvite invite_;

        public global::Axlebolt.Bolt.Protobuf.LobbyInvite Invite { get => invite_; set => invite_ = value; }



        public OnReceivedInviteToLobbyEvent() { }

        public OnReceivedInviteToLobbyEvent(OnReceivedInviteToLobbyEvent other) : this() { invite_ = other.invite_ != null ? other.invite_.Clone() : null; }

        public OnReceivedInviteToLobbyEvent Clone() => new OnReceivedInviteToLobbyEvent(this);

        public override bool Equals(object other) => Equals(other as OnReceivedInviteToLobbyEvent);

        public bool Equals(OnReceivedInviteToLobbyEvent other) => other != null && object.Equals(Invite, other.Invite);

        public override int GetHashCode() => invite_ != null ? Invite.GetHashCode() : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (invite_ != null) { output.WriteRawTag(10); output.WriteMessage(Invite); } }

        public int CalculateSize() => invite_ != null ? 1 + pb::CodedOutputStream.ComputeMessageSize(Invite) : 0;

        public void MergeFrom(OnReceivedInviteToLobbyEvent other) { if (other == null) return; if (other.invite_ != null) { if (invite_ == null) invite_ = new global::Axlebolt.Bolt.Protobuf.LobbyInvite(); Invite.MergeFrom(other.Invite); } }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) { if (invite_ == null) invite_ = new global::Axlebolt.Bolt.Protobuf.LobbyInvite(); input.ReadMessage(invite_); } else input.SkipLastField(); } }

    }



    public sealed partial class OnReceivedSpectatorInviteToLobbyEvent : pb::IMessage<OnReceivedSpectatorInviteToLobbyEvent>

    {

        private static readonly pb::MessageParser<OnReceivedSpectatorInviteToLobbyEvent> _parser = new pb::MessageParser<OnReceivedSpectatorInviteToLobbyEvent>(() => new OnReceivedSpectatorInviteToLobbyEvent());

        public static pb::MessageParser<OnReceivedSpectatorInviteToLobbyEvent> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private global::Axlebolt.Bolt.Protobuf.LobbyInvite invite_;

        public global::Axlebolt.Bolt.Protobuf.LobbyInvite Invite { get => invite_; set => invite_ = value; }



        public OnReceivedSpectatorInviteToLobbyEvent() { }

        public OnReceivedSpectatorInviteToLobbyEvent(OnReceivedSpectatorInviteToLobbyEvent other) : this() { invite_ = other.invite_ != null ? other.invite_.Clone() : null; }

        public OnReceivedSpectatorInviteToLobbyEvent Clone() => new OnReceivedSpectatorInviteToLobbyEvent(this);

        public override bool Equals(object other) => Equals(other as OnReceivedSpectatorInviteToLobbyEvent);

        public bool Equals(OnReceivedSpectatorInviteToLobbyEvent other) => other != null && object.Equals(Invite, other.Invite);

        public override int GetHashCode() => invite_ != null ? Invite.GetHashCode() : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (invite_ != null) { output.WriteRawTag(10); output.WriteMessage(Invite); } }

        public int CalculateSize() => invite_ != null ? 1 + pb::CodedOutputStream.ComputeMessageSize(Invite) : 0;

        public void MergeFrom(OnReceivedSpectatorInviteToLobbyEvent other) { if (other == null) return; if (other.invite_ != null) { if (invite_ == null) invite_ = new global::Axlebolt.Bolt.Protobuf.LobbyInvite(); Invite.MergeFrom(other.Invite); } }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) { if (invite_ == null) invite_ = new global::Axlebolt.Bolt.Protobuf.LobbyInvite(); input.ReadMessage(invite_); } else input.SkipLastField(); } }

    }



    // ============ MatchesRemoteEventListener — OnMatchFinishedEvent + FinishedMatch ============

    // Bug 9: minimal protobuf stubs so we can broadcast `onMatchFinished` to clients

    // after a match ends. The full FinishedMatch protobuf has player/clan stats and

    // rewards, but the client primarily uses the event to navigate to the post-match

    // results screen. Sending only matchId / matchType / start/finish dates is enough

    // to trigger that navigation; the missing repeated fields parse as empty defaults.



    public enum MatchType

    {

        Casual = 0,

        Ranked = 1,

        Custom = 2,

        Tournament = 3,

        Ranked2v2 = 4,

        ClanRanked = 5,

    }



    public enum MatchState

    {

        InProgress = 0,

        Finished = 1,

        Aborted = 2,

    }



    public sealed partial class FinishedMatch : pb::IMessage<FinishedMatch>

    {

        private static readonly pb::MessageParser<FinishedMatch> _parser = new pb::MessageParser<FinishedMatch>(() => new FinishedMatch());

        public static pb::MessageParser<FinishedMatch> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        /// <summary>Parser.ParseFrom падает NRE на stub Descriptor=null — грузим через MergeFrom.
        /// CodedInputStream из byte[] нельзя Dispose (Dispose NRE в этой версии Google.Protobuf).</summary>
        public static byte[] ToClientWire(byte[] payload)
        {
            if (payload == null || payload.Length < 4) return payload;
            if (TryUnwrapSingleField1(payload, out byte[] inner) && inner != null && inner.Length > 0)
                return inner;
            return payload;
        }

        public static FinishedMatch SafeParseFrom(byte[] payload)
        {
            var m = new FinishedMatch();
            if (payload == null || payload.Length == 0) return m;
            try
            {
                // Legacy Save: весь payload = одно field1(wrapper) с inner MCMGBNHJEAG.
                // Плоский формат: field1=matchId + field7=finishDate + …
                // Нельзя эвристикой «парсить matchId как protobuf» — Ranked2v2_… ложно
                // выглядит как nested (байт 'R'=0x52) и ломает историю.
                if (TryUnwrapSingleField1(payload, out byte[] inner))
                {
                    m.MergeFromInner(new pb::CodedInputStream(inner));
                }
                else
                {
                    m.MergeFromFlat(new pb::CodedInputStream(payload));
                }
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine("[FinishedMatch] SafeParseFrom failed: " + ex.Message);
            }
            return m;
        }

        private static bool TryUnwrapSingleField1(byte[] payload, out byte[] inner)
        {
            inner = null;
            try
            {
                var input = new pb::CodedInputStream(payload);
                uint tag = input.ReadTag();
                if (tag != 10) return false;
                var bytes = input.ReadBytes();
                if (bytes == null || bytes.Length < 2) return false;
                if (input.ReadTag() != 0) return false; // есть ещё top-level поля → плоский
                // Inner должен начинаться с field1 string (matchId) и иметь ещё поля.
                byte[] raw = bytes.ToByteArray();
                var peek = new pb::CodedInputStream(raw);
                if (peek.ReadTag() != 10) return false;
                string id = peek.ReadString();
                if (string.IsNullOrEmpty(id) || peek.ReadTag() == 0) return false;
                inner = raw;
                return true;
            }
            catch { return false; }
        }



        private string matchId_ = "";

        public string MatchId { get => matchId_; set => matchId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        private MatchType matchType_;

        public MatchType MatchType { get => matchType_; set => matchType_ = value; }

        private string creatorGpid_ = "";

        public string CreatorGpid { get => creatorGpid_; set => creatorGpid_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        private string region_ = "";

        public string Region { get => region_; set => region_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        private string version_ = "";

        public string Version { get => version_; set => version_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        private long startDate_;

        public long StartDate { get => startDate_; set => startDate_ = value; }

        private long finishDate_;

        public long FinishDate { get => finishDate_; set => finishDate_ = value; }

        private string seasonId_ = "";

        public string SeasonId { get => seasonId_; set => seasonId_ = pb::ProtoPreconditions.CheckNotNull(value, "value"); }

        private MatchState state_;

        public MatchState State { get => state_; set => state_ = value; }

        // Клиентский тип матча в getPlayerMatches/getMatch — MCMGBNHJEAG (плоский):
        // 1=matchId 2=type 3=creator 4=region 5=version 6=start 7=finish 8=season 9=state
        // 10=repeated ILJHHHFDPEM overall 11=repeated NBILNLINAIC players 12=…
        // EELPFKFMHKN/onMatchFinished = field1 → MCMGBNHJEAG; наш OnMatchFinishedEvent
        // пишет field1=FinishedMatch, поэтому WriteTo должен быть ПЛОСКИМ (не double-wrap).
        private readonly System.Collections.Generic.List<string> _playerRowNames = new System.Collections.Generic.List<string>();
        private readonly System.Collections.Generic.List<string> _playerRowIds = new System.Collections.Generic.List<string>();
        private readonly System.Collections.Generic.List<string> _playerRowUids = new System.Collections.Generic.List<string>();
        private readonly System.Collections.Generic.List<System.Collections.Generic.List<StatValueRow>> _playerRowStats = new System.Collections.Generic.List<System.Collections.Generic.List<StatValueRow>>();
        private readonly System.Collections.Generic.List<System.Collections.Generic.List<DroppedItem>> _playerRowDrops = new System.Collections.Generic.List<System.Collections.Generic.List<DroppedItem>>();
        private readonly System.Collections.Generic.List<StatValueRow> _overallStats = new System.Collections.Generic.List<StatValueRow>();
        // Сырые player-row bytes из legacy payload — чтобы не терять K/D при round-trip.
        private readonly System.Collections.Generic.List<byte[]> _rawPlayerRowBytes = new System.Collections.Generic.List<byte[]>();
        private readonly System.Collections.Generic.List<byte[]> _rawOverallStatBytes = new System.Collections.Generic.List<byte[]>();

        /// <summary>
        /// Сырой WirePayload с type=4 ломает список 0.17.
        /// Raw row bytes оставляем только если структурированные строки не распарсились.
        /// </summary>
        public void UseStructuredWire()
        {
            WirePayload = null;
            if (_playerRowIds.Count > 0)
                _rawPlayerRowBytes.Clear();
            if (_overallStats.Count > 0)
                _rawOverallStatBytes.Clear();
        }

        public int PlayerRowCount => _playerRowIds.Count;

        public void NormalizeClientDates()
        {
            finishDate_ = ToUnixMs(finishDate_);
            startDate_ = ToUnixMs(startDate_);
            if (startDate_ == 0 && finishDate_ != 0) startDate_ = finishDate_ - 600000;
            if (finishDate_ != 0 && startDate_ > finishDate_) startDate_ = finishDate_ - 600000;
        }

        private static long ToUnixMs(long v)
        {
            if (v == 0) return 0;
            if (v > 10_000_000_000_000L)
            {
                try { return new DateTimeOffset(new DateTime(v, DateTimeKind.Utc)).ToUnixTimeMilliseconds(); }
                catch { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
            }
            if (v > 0 && v < 10_000_000_000L) return v * 1000;
            return v;
        }

        public static long ClientDateToUnixMs(long v) => ToUnixMs(v);

        public void EnsureViewerPlayerRow(string viewerId, string viewerUid = null, string viewerName = null)
        {
            if (string.IsNullOrWhiteSpace(viewerId)) return;
            int found = -1;
            for (int i = 0; i < _playerRowIds.Count; i++)
            {
                if (string.Equals(_playerRowIds[i], viewerId, System.StringComparison.OrdinalIgnoreCase))
                {
                    found = i;
                    break;
                }
                string uid = i < _playerRowUids.Count ? _playerRowUids[i] : null;
                string name = i < _playerRowNames.Count ? _playerRowNames[i] : null;
                if (!string.IsNullOrEmpty(viewerUid) && (
                    string.Equals(_playerRowIds[i], viewerUid, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uid, viewerUid, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, viewerUid, System.StringComparison.OrdinalIgnoreCase)))
                {
                    _playerRowIds[i] = viewerId;
                    found = i;
                    break;
                }
            }
            if (found >= 0)
            {
                FillViewerListStats(found);
                return;
            }
            // Не добавляем пустую dummy-строку поверх реальных игроков: клиент
            // берёт «мои» score/MMR из строки зрителя и рисует пустой список.
            if (_playerRowIds.Count > 0)
            {
                FillViewerListStats(0);
                return;
            }
            var stats = new System.Collections.Generic.List<StatValueRow>
            {
                new StatValueRow { Name = "kill", Type = 0, IntValue = 0 },
                new StatValueRow { Name = "death", Type = 0, IntValue = 0 },
                new StatValueRow { Name = "assist", Type = 0, IntValue = 0 },
                new StatValueRow { Name = "score1", Type = 0, IntValue = GetOverallInt("score1") },
                new StatValueRow { Name = "score2", Type = 0, IntValue = GetOverallInt("score2") },
                new StatValueRow { Name = "TrScore", Type = 0, IntValue = GetOverallInt("TrScore") },
                new StatValueRow { Name = "CtScore", Type = 0, IntValue = GetOverallInt("CtScore") },
                new StatValueRow { Name = "result", Type = 0, IntValue = GetOverallInt("result") },
                new StatValueRow { Name = "mmr_delta", Type = 0, IntValue = GetOverallInt("mmr_delta") },
            };
            AddPlayerRow(viewerId, viewerName ?? viewerUid ?? "Player", stats, null, viewerUid ?? viewerName ?? viewerId);
        }

        private void FillViewerListStats(int index)
        {
            if (index < 0 || index >= _playerRowStats.Count) return;
            var stats = _playerRowStats[index];
            EnsureStat(stats, "score1", GetOverallInt("score1"));
            EnsureStat(stats, "score2", GetOverallInt("score2"));
            EnsureStat(stats, "TrScore", GetOverallInt("TrScore"));
            EnsureStat(stats, "CtScore", GetOverallInt("CtScore"));
            EnsureStat(stats, "result", GetOverallInt("result"));
            EnsureStat(stats, "mmr_delta", GetOverallInt("mmr_delta"));
            EnsureStat(stats, "mmr_change", GetOverallInt("mmr_change") != 0 ? GetOverallInt("mmr_change") : GetOverallInt("mmr_delta"));
        }

        private static void EnsureStat(System.Collections.Generic.List<StatValueRow> stats, string name, int value)
        {
            if (stats == null || string.IsNullOrEmpty(name)) return;
            foreach (var s in stats)
            {
                if (s != null && string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return;
            }
            stats.Add(new StatValueRow { Name = name, Type = 0, IntValue = value });
        }

        public int GetOverallInt(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            foreach (var s in _overallStats)
            {
                if (s == null || string.IsNullOrEmpty(s.Name)) continue;
                if (!string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase)) continue;
                return s.IntValue;
            }
            return 0;
        }

        public void AddPlayerRow(string id, string name, System.Collections.Generic.List<StatValueRow> stats, System.Collections.Generic.List<DroppedItem> drops, string uid = null)
        {
            _playerRowIds.Add(id);
            _playerRowNames.Add(name);
            _playerRowUids.Add(uid ?? "");
            _playerRowStats.Add(stats ?? new System.Collections.Generic.List<StatValueRow>());
            _playerRowDrops.Add(drops ?? new System.Collections.Generic.List<DroppedItem>());
        }

        public void AddOverallStat(StatValueRow row) => _overallStats.Add(row);

        public string GetOverallString(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var s in _overallStats)
            {
                if (s == null || string.IsNullOrEmpty(s.Name)) continue;
                if (!string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(s.StringValue)) return s.StringValue.Trim();
            }
            return null;
        }

        /// <summary>
        /// Клиент 0.17 рисует строку истории только при наличии map/level.
        /// Старые записи без карты иначе молча отбрасываются → пустой список.
        /// </summary>
        public void EnsureMapLevelForClient()
        {
            string map = GetOverallString("map");
            if (string.IsNullOrEmpty(map))
                map = GetOverallString("level");
            string mode = GetOverallString("mode") ?? "";
            bool allies = mode.IndexOf("2v2", System.StringComparison.OrdinalIgnoreCase) >= 0
                || mode.IndexOf("Ranked2v2", System.StringComparison.OrdinalIgnoreCase) >= 0
                || mode.IndexOf("allies", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (string.IsNullOrEmpty(map))
                map = allies ? "Province 2x2" : "Province";
            map = PrettyHistoryMapName(map, allies);
            if (string.IsNullOrEmpty(GetOverallString("map")))
                AddOverallStat(new StatValueRow { Name = "map", Type = 2, StringValue = map });
            else
                SetOverallString("map", map);
            if (string.IsNullOrEmpty(GetOverallString("level")))
                AddOverallStat(new StatValueRow { Name = "level", Type = 2, StringValue = map });
            else
                SetOverallString("level", map);
        }

        private void SetOverallString(string name, string value)
        {
            foreach (var s in _overallStats)
            {
                if (s != null && string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    s.Type = 2;
                    s.StringValue = value;
                    return;
                }
            }
            AddOverallStat(new StatValueRow { Name = name, Type = 2, StringValue = value });
        }

        private static string PrettyHistoryMapName(string raw, bool allies)
        {
            string s = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(s))
                return allies ? "Province 2x2" : "Province";
            string lower = s.ToLowerInvariant();
            string pretty =
                lower.Contains("sakura") ? "Sakura" :
                lower.Contains("rust") ? "Rust" :
                lower.Contains("province") ? "Province" :
                lower.Contains("sandstone") || lower.Contains("breeze") ? "Sandstone" :
                lower.Contains("zone") ? "Zone9" :
                (char.ToUpperInvariant(s[0]) + (s.Length > 1 ? s.Substring(1) : ""));
            pretty = pretty.Replace(" 2x2", "", System.StringComparison.OrdinalIgnoreCase)
                .Replace(" 2v2", "", System.StringComparison.OrdinalIgnoreCase)
                .Trim();
            if (allies && pretty.IndexOf("2x2", System.StringComparison.OrdinalIgnoreCase) < 0)
                pretty += " 2x2";
            return pretty;
        }

        public sealed class StatValueRow : Google.Protobuf.IMessage
        {
            public string Name;
            public int Type;      // ALFJDPDNEAI: Int=0,Float=1,String=2,Boolean=3,Long=4,ItemId=5,ImageUrl=6,Json=7
            public long LongValue;
            public int IntValue;
            public string StringValue;

            public void WriteTo(pb::CodedOutputStream output) => WriteStatRow(output, this);
            public int CalculateSize() => StatRowSize(this);
            public void MergeFrom(pb::CodedInputStream input) { }
            pbr::MessageDescriptor pb::IMessage.Descriptor => null;
        }

        public sealed class DroppedItem : Google.Protobuf.IMessage
        {
            public string ItemId;
            public int Count;     // int value
            public float Float1;  // float value (field 3)
            public float Float2;  // float value (field 4)
            public int ItemType;  // enum field 5
            public long LongValue;// field 6

            public void WriteTo(pb::CodedOutputStream output) => WriteDropItem(output, this);
            public int CalculateSize() => DropItemSize(this);
            public void MergeFrom(pb::CodedInputStream input) { }
            pbr::MessageDescriptor pb::IMessage.Descriptor => null;
        }



        public FinishedMatch() { }

        public FinishedMatch(FinishedMatch other) : this()

        {

            matchId_ = other.matchId_;

            matchType_ = other.matchType_;

            creatorGpid_ = other.creatorGpid_;

            region_ = other.region_;

            version_ = other.version_;

            startDate_ = other.startDate_;

            finishDate_ = other.finishDate_;

            seasonId_ = other.seasonId_;

            state_ = other.state_;

            if (other._rawPlayerRowBytes.Count > 0)
                _rawPlayerRowBytes.AddRange(other._rawPlayerRowBytes);
            if (other._rawOverallStatBytes.Count > 0)
                _rawOverallStatBytes.AddRange(other._rawOverallStatBytes);
            for (int i = 0; i < other._playerRowIds.Count; i++)
            {
                AddPlayerRow(
                    other._playerRowIds[i],
                    i < other._playerRowNames.Count ? other._playerRowNames[i] : "",
                    i < other._playerRowStats.Count ? other._playerRowStats[i] : null,
                    i < other._playerRowDrops.Count ? other._playerRowDrops[i] : null,
                    i < other._playerRowUids.Count ? other._playerRowUids[i] : "");
            }
            foreach (var s in other._overallStats) AddOverallStat(s);

        }

        public FinishedMatch Clone() => new FinishedMatch(this);

        public override bool Equals(object other) => Equals(other as FinishedMatch);

        public bool Equals(FinishedMatch other) => other != null;

        public override int GetHashCode() => 1;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        private static void WriteStatRow(pb::CodedOutputStream o, StatValueRow s)
        {
            if (s == null) return;
            // ILJHHHFDPEM : 1=name(string) 2=type(enum) then oneof 3=int 4=float 5=long 6=string 7=bool
            if (!string.IsNullOrEmpty(s.Name)) { o.WriteRawTag(10); o.WriteString(s.Name); }
            o.WriteRawTag(16); o.WriteInt32(s.Type);
            switch (s.Type)
            {
                case 3: o.WriteRawTag(56); o.WriteBool(s.IntValue != 0); break; // Boolean = field 7
                case 4: o.WriteRawTag(40); o.WriteInt64(s.LongValue); break;    // Long = field 5
                case 1: o.WriteRawTag(37); o.WriteFloat(s.IntValue); break;     // Float = field 4
                case 0: o.WriteRawTag(24); o.WriteInt32(s.IntValue); break;     // Int = field 3
                default: if (s.StringValue != null) { o.WriteRawTag(50); o.WriteString(s.StringValue); } break; // String(2), ItemId(5), etc.
            }
        }

        private static int StatRowSize(StatValueRow s)
        {
            if (s == null) return 0;
            int size = 0;
            if (!string.IsNullOrEmpty(s.Name)) size += 1 + pb::CodedOutputStream.ComputeStringSize(s.Name);
            size += 1 + pb::CodedOutputStream.ComputeInt32Size(s.Type);
            switch (s.Type)
            {
                case 0: size += 1 + pb::CodedOutputStream.ComputeInt32Size(s.IntValue); break;
                case 1: size += 5; break; // float
                case 3: size += 2; break; // bool field 7
                case 4: size += 1 + pb::CodedOutputStream.ComputeInt64Size(s.LongValue); break;
                default: if (s.StringValue != null) size += 1 + pb::CodedOutputStream.ComputeStringSize(s.StringValue); break;
            }
            return size;
        }

        private static void WriteDropItem(pb::CodedOutputStream o, DroppedItem d)
        {
            if (d == null) return;
            // CGJAKHCOALP : 1=itemId(string) 2=int 3=float 4=float 5=enum 6=long
            if (!string.IsNullOrEmpty(d.ItemId)) { o.WriteRawTag(10); o.WriteString(d.ItemId); }
            if (d.Count != 0) { o.WriteRawTag(16); o.WriteInt32(d.Count); }
            if (d.Float1 != 0f) { o.WriteRawTag(29); o.WriteFloat(d.Float1); }
            if (d.Float2 != 0f) { o.WriteRawTag(37); o.WriteFloat(d.Float2); }
            if (d.ItemType != 0) { o.WriteRawTag(40); o.WriteInt32(d.ItemType); }
            if (d.LongValue != 0) { o.WriteRawTag(48); o.WriteInt64(d.LongValue); }
        }

        private static int DropItemSize(DroppedItem d)
        {
            if (d == null) return 0;
            int size = 0;
            if (!string.IsNullOrEmpty(d.ItemId)) size += 1 + pb::CodedOutputStream.ComputeStringSize(d.ItemId);
            if (d.Count != 0) size += 1 + pb::CodedOutputStream.ComputeInt32Size(d.Count);
            if (d.Float1 != 0f) size += 5;
            if (d.Float2 != 0f) size += 5;
            if (d.ItemType != 0) size += 1 + pb::CodedOutputStream.ComputeInt32Size(d.ItemType);
            if (d.LongValue != 0) size += 1 + pb::CodedOutputStream.ComputeInt64Size(d.LongValue);
            return size;
        }

        public byte[] WirePayload;

        public void WriteLengthDelimited(pb::CodedOutputStream output)
        {
            if (WirePayload != null && WirePayload.Length > 0)
                output.WriteBytes(Google.Protobuf.ByteString.CopyFrom(WirePayload));
            else
                output.WriteMessage(this);
        }

        public int LengthDelimitedSize()
        {
            if (WirePayload != null && WirePayload.Length > 0)
                return pb::CodedOutputStream.ComputeBytesSize(Google.Protobuf.ByteString.CopyFrom(WirePayload));
            return pb::CodedOutputStream.ComputeMessageSize(this);
        }

        public void WriteTo(pb::CodedOutputStream output)

        {
            // Плоский MCMGBNHJEAG — так клиент читает getPlayerMatches / getMatch / onMatchFinished.
            WriteInnerTo(output);
        }

        /// <summary>Пишет поля MCMGBNHJEAG без outer-wrapper.</summary>
        public void WriteInnerTo(pb::CodedOutputStream inner)
        {
                if (matchId_.Length != 0) { inner.WriteRawTag(10); inner.WriteString(matchId_); }
                // Клиент 0.17 FKKOHFKJPKB: Regular=0, ClanBattle=1. Ranked2v2=4 ломает историю.
                int clientMatchType = (matchType_ == MatchType.ClanRanked) ? 1 : 0;
                if (clientMatchType != 0) { inner.WriteRawTag(16); inner.WriteInt32(clientMatchType); }
                if (creatorGpid_.Length != 0) { inner.WriteRawTag(26); inner.WriteString(creatorGpid_); }
                if (region_.Length != 0) { inner.WriteRawTag(34); inner.WriteString(region_); }
                if (version_.Length != 0) { inner.WriteRawTag(42); inner.WriteString(version_); }
                if (startDate_ != 0) { inner.WriteRawTag(48); inner.WriteInt64(startDate_); }
                if (finishDate_ != 0) { inner.WriteRawTag(56); inner.WriteInt64(finishDate_); }
                if (seasonId_.Length != 0) { inner.WriteRawTag(66); inner.WriteString(seasonId_); }
                // Клиент HPEHIEDGNLE: Finished=0, Canceled=1, Annulled=2.
                // Наш enum Finished=1 → клиент читал как Canceled. Finished(0) = default, поле не пишем.
                if (state_ == MatchState.Aborted) { inner.WriteRawTag(72); inner.WriteInt32(2); }

                // field 10 = repeated ILJHHHFDPEM overall stats
                if (_rawOverallStatBytes.Count > 0 && _overallStats.Count == 0)
                {
                    foreach (var raw in _rawOverallStatBytes)
                    {
                        if (raw == null || raw.Length == 0) continue;
                        inner.WriteRawTag(82);
                        inner.WriteBytes(Google.Protobuf.ByteString.CopyFrom(raw));
                    }
                }
                else
                {
                    foreach (var st in _overallStats)
                    {
                        inner.WriteRawTag(82);
                        inner.WriteBytes(Google.Protobuf.ByteString.CopyFrom(WriteMessageBytes(st)));
                    }
                }

                // field 11 = repeated NBILNLINAIC player rows
                if (_rawPlayerRowBytes.Count > 0 && _playerRowIds.Count == 0)
                {
                    foreach (var raw in _rawPlayerRowBytes)
                    {
                        if (raw == null || raw.Length == 0) continue;
                        inner.WriteRawTag(90);
                        inner.WriteBytes(Google.Protobuf.ByteString.CopyFrom(raw));
                    }
                }
                else
                {
                for (int p = 0; p < _playerRowIds.Count; p++)
                {
                    using (var ps = new System.IO.MemoryStream())
                    using (var po = new pb::CodedOutputStream(ps, true))
                    {
                        if (p < _playerRowIds.Count && !string.IsNullOrEmpty(_playerRowIds[p])) { po.WriteRawTag(10); po.WriteString(_playerRowIds[p]); }
                        string uid = (p < _playerRowUids.Count && !string.IsNullOrEmpty(_playerRowUids[p]))
                            ? _playerRowUids[p]
                            : ((p < _playerRowNames.Count && !string.IsNullOrEmpty(_playerRowNames[p])) ? _playerRowNames[p] : _playerRowIds[p]);
                        if (!string.IsNullOrEmpty(uid)) { po.WriteRawTag(18); po.WriteString(uid); }
                        if (p < _playerRowNames.Count && !string.IsNullOrEmpty(_playerRowNames[p])) { po.WriteRawTag(26); po.WriteString(_playerRowNames[p]); }
                        if (p < _playerRowStats.Count)
                            foreach (var st in _playerRowStats[p])
                            {
                                if (st != null && string.Equals(st.Name, "deaths", System.StringComparison.OrdinalIgnoreCase))
                                    st.Name = "death";
                                byte[] statBytes = WriteMessageBytes(st);
                                // field 4 и field 5: клиент 0.17 читает статы с одного из них.
                                po.WriteRawTag(34); po.WriteBytes(Google.Protobuf.ByteString.CopyFrom(statBytes));
                                po.WriteRawTag(42); po.WriteBytes(Google.Protobuf.ByteString.CopyFrom(statBytes));
                            }
                        if (p < _playerRowDrops.Count && _playerRowDrops[p].Count > 0)
                        {
                            using (var ds = new System.IO.MemoryStream())
                            using (var dso = new pb::CodedOutputStream(ds, true))
                            {
                                foreach (var dp in _playerRowDrops[p]) { dso.WriteRawTag(10); dso.WriteBytes(Google.Protobuf.ByteString.CopyFrom(WriteMessageBytes(dp))); }
                                dso.Flush();
                                po.WriteRawTag(50); po.WriteBytes(Google.Protobuf.ByteString.CopyFrom(ds.ToArray()));
                            }
                        }
                        po.Flush();
                        inner.WriteRawTag(90); inner.WriteBytes(Google.Protobuf.ByteString.CopyFrom(ps.ToArray()));
                    }
                }
                }
        }

        private static byte[] WriteMessageBytes(pb::IMessage msg)
        {
            using (var s = new System.IO.MemoryStream())
            using (var o = new pb::CodedOutputStream(s, true))
            {
                msg.WriteTo(o);
                o.Flush();
                return s.ToArray();
            }
        }

        public int CalculateSize()

        {

            // Serialize once to a scratch buffer and return the exact length.
            // This guarantees the size matches WriteTo exactly (no over/under-count
            // of nested length prefixes), avoiding "ran out of space" overflow.
            using (var s = new System.IO.MemoryStream())
            {
                using (var o = new pb::CodedOutputStream(s, true)) { WriteTo(o); o.Flush(); }
                return (int)s.Length;
            }

        }

        public void MergeFrom(FinishedMatch other)

        {

            if (other == null) return;

            if (other.matchId_.Length != 0) matchId_ = other.matchId_;

            if (other.matchType_ != 0) matchType_ = other.matchType_;

            if (other.creatorGpid_.Length != 0) creatorGpid_ = other.creatorGpid_;

            if (other.region_.Length != 0) region_ = other.region_;

            if (other.version_.Length != 0) version_ = other.version_;

            if (other.startDate_ != 0) startDate_ = other.startDate_;

            if (other.finishDate_ != 0) finishDate_ = other.finishDate_;

            if (other.seasonId_.Length != 0) seasonId_ = other.seasonId_;

            if (other.state_ != 0) state_ = other.state_;

            if (other._rawPlayerRowBytes.Count > 0)
                _rawPlayerRowBytes.AddRange(other._rawPlayerRowBytes);
            if (other._rawOverallStatBytes.Count > 0)
                _rawOverallStatBytes.AddRange(other._rawOverallStatBytes);
            for (int i = 0; i < other._playerRowIds.Count; i++)
            {
                AddPlayerRow(
                    other._playerRowIds[i],
                    i < other._playerRowNames.Count ? other._playerRowNames[i] : "",
                    i < other._playerRowStats.Count ? other._playerRowStats[i] : null,
                    i < other._playerRowDrops.Count ? other._playerRowDrops[i] : null,
                    i < other._playerRowUids.Count ? other._playerRowUids[i] : "");
            }
            foreach (var s in other._overallStats) AddOverallStat(s);

        }

        public void MergeFrom(pb::CodedInputStream input) => MergeFromFlat(input);

        private void MergeFromFlat(pb::CodedInputStream input)
        {
            // Плоский MCMGBNHJEAG: field1 всегда matchId (string), никогда wrapper.
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 10: matchId_ = input.ReadString(); break;
                    case 16: matchType_ = (MatchType)input.ReadInt32(); break;
                    case 26: creatorGpid_ = input.ReadString(); break;
                    case 34: region_ = input.ReadString(); break;
                    case 42: version_ = input.ReadString(); break;
                    case 48: startDate_ = input.ReadInt64(); break;
                    case 56: finishDate_ = input.ReadInt64(); break;
                    case 66: seasonId_ = input.ReadString(); break;
                    case 72: state_ = (MatchState)input.ReadInt32(); break;
                    case 82:
                    {
                        var stBytes = input.ReadBytes();
                        if (stBytes != null && stBytes.Length > 0)
                        {
                            _rawOverallStatBytes.Add(stBytes.ToByteArray());
                            try { ParseOverallStatBytes(stBytes.ToByteArray()); } catch { }
                        }
                        break;
                    }
                    case 90:
                    {
                        var rowBytes = input.ReadBytes();
                        if (rowBytes != null && rowBytes.Length > 0)
                        {
                            byte[] rawRow = rowBytes.ToByteArray();
                            _rawPlayerRowBytes.Add(rawRow);
                            try { ParsePlayerRowBytes(rawRow); } catch { }
                        }
                        break;
                    }
                    case 98:
                    {
                        // field 12 = PLBHPFCIGNL (rounds), не overall stats.
                        try { SkipWireField(input, tag); } catch { return; }
                        break;
                    }
                    default:
                        try { SkipWireField(input, tag); } catch { return; }
                        break;
                }
            }
        }

        private static void SkipWireField(pb::CodedInputStream input, uint tag)
        {
            switch (tag & 7)
            {
                case 0: input.ReadInt64(); break;
                case 1: input.ReadFixed64(); break;
                case 2: input.ReadBytes(); break;
                case 5: input.ReadFixed32(); break;
                default: break;
            }
        }

        private void ParsePlayerRowBytes(byte[] rowBytes)
        {
            string id = "", uid = "", name = "";
            var stats = new System.Collections.Generic.List<StatValueRow>();
            var rs = new pb::CodedInputStream(rowBytes);
            uint rt;
            while ((rt = rs.ReadTag()) != 0)
            {
                if (rt == 10) id = rs.ReadString();
                else if (rt == 18) uid = rs.ReadString();
                else if (rt == 26) name = rs.ReadString();
                else if (rt == 34 || rt == 42)
                {
                    var stBytes = rs.ReadBytes();
                    if (stBytes != null && stBytes.Length > 0)
                    {
                        try
                        {
                            var row = new StatValueRow();
                            var ss = new pb::CodedInputStream(stBytes.ToByteArray());
                            uint st;
                            while ((st = ss.ReadTag()) != 0)
                            {
                                if (st == 10) row.Name = ss.ReadString();
                                else if (st == 16) row.Type = ss.ReadInt32();
                                else if (st == 24) row.IntValue = ss.ReadInt32();
                                else if (st == 40) row.LongValue = ss.ReadInt64();
                                else if (st == 50) row.StringValue = ss.ReadString();
                                else SkipWireField(ss, st);
                            }
                            if (string.Equals(row.Name, "deaths", System.StringComparison.OrdinalIgnoreCase))
                                row.Name = "death";
                            stats.Add(row);
                        }
                        catch { }
                    }
                }
                else SkipWireField(rs, rt);
            }
            if (string.IsNullOrEmpty(name)) name = uid;
            AddPlayerRow(id, name, stats, null, uid);
        }

        private void ParseOverallStatBytes(byte[] stBytes)
        {
            var row = new StatValueRow();
            var ss = new pb::CodedInputStream(stBytes);
            uint st;
            while ((st = ss.ReadTag()) != 0)
            {
                if (st == 10) row.Name = ss.ReadString();
                else if (st == 16) row.Type = ss.ReadInt32();
                else if (st == 24)
                {
                    if (row.Type == 3) row.IntValue = ss.ReadBool() ? 1 : 0;
                    else row.IntValue = ss.ReadInt32();
                }
                else if (st == 40) row.LongValue = ss.ReadInt64();
                else if (st == 50) row.StringValue = ss.ReadString();
                else SkipWireField(ss, st);
            }
            AddOverallStat(row);
        }

        private void MergeFromInner(pb::CodedInputStream input) => MergeFromFlat(input);

    }



    public sealed partial class OnMatchFinishedEvent : pb::IMessage<OnMatchFinishedEvent>

    {

        private static readonly pb::MessageParser<OnMatchFinishedEvent> _parser = new pb::MessageParser<OnMatchFinishedEvent>(() => new OnMatchFinishedEvent());

        public static pb::MessageParser<OnMatchFinishedEvent> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private FinishedMatch match_;

        public FinishedMatch Match { get => match_; set => match_ = value; }



        public OnMatchFinishedEvent() { }

        public OnMatchFinishedEvent(OnMatchFinishedEvent other) : this() { match_ = other.match_ != null ? other.match_.Clone() : null; }

        public OnMatchFinishedEvent Clone() => new OnMatchFinishedEvent(this);

        public override bool Equals(object other) => Equals(other as OnMatchFinishedEvent);

        public bool Equals(OnMatchFinishedEvent other) => other != null && object.Equals(Match, other.Match);

        public override int GetHashCode() => match_ != null ? Match.GetHashCode() : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) { if (match_ != null) { output.WriteRawTag(10); output.WriteMessage(Match); } }

        public int CalculateSize() => match_ != null ? 1 + pb::CodedOutputStream.ComputeMessageSize(Match) : 0;

        public void MergeFrom(OnMatchFinishedEvent other) { if (other == null) return; if (other.match_ != null) { if (match_ == null) match_ = new FinishedMatch(); Match.MergeFrom(other.Match); } }

        public void MergeFrom(pb::CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) { if (match_ == null) match_ = new FinishedMatch(); input.ReadMessage(match_); } else input.SkipLastField(); } }

    }



    public sealed partial class SearchPlayersRequest : pb::IMessage<SearchPlayersRequest> {

        private static readonly pb::MessageParser<SearchPlayersRequest> _parser = new pb::MessageParser<SearchPlayersRequest>(() => new SearchPlayersRequest());

        public static pb::MessageParser<SearchPlayersRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        public string Value { get; set; } = "";

        public int Page { get; set; }

        public int Size { get; set; }



        public SearchPlayersRequest() { }

        public SearchPlayersRequest(SearchPlayersRequest other) : this() { Value = other.Value; Page = other.Page; Size = other.Size; }

        public SearchPlayersRequest Clone() => new SearchPlayersRequest(this);

        public override bool Equals(object other) => Equals(other as SearchPlayersRequest);

        public bool Equals(SearchPlayersRequest other) => other != null && Value == other.Value && Page == other.Page && Size == other.Size;

        public override int GetHashCode() => Value.GetHashCode() ^ Page.GetHashCode() ^ Size.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {

            if (Value.Length != 0) { output.WriteRawTag(10); output.WriteString(Value); }

            if (Page != 0) { output.WriteRawTag(16); output.WriteInt32(Page); }

            if (Size != 0) { output.WriteRawTag(24); output.WriteInt32(Size); }

        }

        public int CalculateSize() {

            int size = 0;

            if (Value.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(Value);

            if (Page != 0) size += 1 + pb::CodedOutputStream.ComputeInt32Size(Page);

            if (Size != 0) size += 1 + pb::CodedOutputStream.ComputeInt32Size(Size);

            return size;

        }

        public void MergeFrom(SearchPlayersRequest other) {

            if (other == null) return;

            if (other.Value.Length != 0) Value = other.Value;

            if (other.Page != 0) Page = other.Page;

            if (other.Size != 0) Size = other.Size;

        }

        public void MergeFrom(pb::CodedInputStream input) {

            uint tag;

            while ((tag = input.ReadTag()) != 0) {

                if (tag == 10) Value = input.ReadString();

                else if (tag == 16) Page = input.ReadInt32();

                else if (tag == 24) Size = input.ReadInt32();

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class SearchPlayersResponse : pb::IMessage<SearchPlayersResponse> {

        private static readonly pb::MessageParser<SearchPlayersResponse> _parser = new pb::MessageParser<SearchPlayersResponse>(() => new SearchPlayersResponse());

        public static pb::MessageParser<SearchPlayersResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.PlayerFriend> _repeated_players_codec = pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.PlayerFriend.Parser);

        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.PlayerFriend> players_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.PlayerFriend>();

        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.PlayerFriend> Players => players_;



        public SearchPlayersResponse() { }

        public SearchPlayersResponse(SearchPlayersResponse other) : this() { players_.Add(other.players_); }

        public SearchPlayersResponse Clone() => new SearchPlayersResponse(this);

        public override bool Equals(object other) => Equals(other as SearchPlayersResponse);

        public bool Equals(SearchPlayersResponse other) => other != null && players_.Equals(other.players_);

        public override int GetHashCode() => players_.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) => players_.WriteTo(output, _repeated_players_codec);

        public int CalculateSize() => players_.CalculateSize(_repeated_players_codec);

        public void MergeFrom(SearchPlayersResponse other) { if (other == null) return; players_.Add(other.players_); }

        public void MergeFrom(pb::CodedInputStream input) {

            uint tag;

            while ((tag = input.ReadTag()) != 0) {

                if (tag == 10) players_.AddEntriesFrom(input, _repeated_players_codec);

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class GetPlayerStats2ResponseData : pb::IMessage<GetPlayerStats2ResponseData> {

        private static readonly pb::MessageParser<GetPlayerStats2ResponseData> _parser = new pb::MessageParser<GetPlayerStats2ResponseData>(() => new GetPlayerStats2ResponseData());

        public static pb::MessageParser<GetPlayerStats2ResponseData> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        public string PlayerId { get; set; } = "";

        public global::Axlebolt.Bolt.Protobuf.Stats Stats { get; set; }



        public GetPlayerStats2ResponseData() { }

        public GetPlayerStats2ResponseData(GetPlayerStats2ResponseData other) : this() { PlayerId = other.PlayerId; Stats = other.Stats != null ? other.Stats.Clone() : null; }

        public GetPlayerStats2ResponseData Clone() => new GetPlayerStats2ResponseData(this);

        public override bool Equals(object other) => Equals(other as GetPlayerStats2ResponseData);

        public bool Equals(GetPlayerStats2ResponseData other) => other != null && PlayerId == other.PlayerId && object.Equals(Stats, other.Stats);

        public override int GetHashCode() => PlayerId.GetHashCode() ^ (Stats != null ? Stats.GetHashCode() : 0);

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {

            if (PlayerId.Length != 0) { output.WriteRawTag(10); output.WriteString(PlayerId); }

            if (Stats != null) { output.WriteRawTag(18); output.WriteMessage(Stats); }

        }

        public int CalculateSize() {

            int size = 0;

            if (PlayerId.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(PlayerId);

            if (Stats != null) size += 1 + pb::CodedOutputStream.ComputeMessageSize(Stats);

            return size;

        }

        public void MergeFrom(GetPlayerStats2ResponseData other) {

            if (other == null) return;

            if (other.PlayerId.Length != 0) PlayerId = other.PlayerId;

            if (other.Stats != null) { if (Stats == null) Stats = new global::Axlebolt.Bolt.Protobuf.Stats(); Stats.MergeFrom(other.Stats); }

        }

        public void MergeFrom(pb::CodedInputStream input) {

            uint tag;

            while ((tag = input.ReadTag()) != 0) {

                if (tag == 10) PlayerId = input.ReadString();

                else if (tag == 18) { if (Stats == null) Stats = new global::Axlebolt.Bolt.Protobuf.Stats(); input.ReadMessage(Stats); }

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class GetPlayerStats2Response : pb::IMessage<GetPlayerStats2Response> {

        private static readonly pb::MessageParser<GetPlayerStats2Response> _parser = new pb::MessageParser<GetPlayerStats2Response>(() => new GetPlayerStats2Response());

        public static pb::MessageParser<GetPlayerStats2Response> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        public GetPlayerStats2ResponseData Data { get; set; }



        public GetPlayerStats2Response() { }

        public GetPlayerStats2Response(GetPlayerStats2Response other) : this() { Data = other.Data != null ? other.Data.Clone() : null; }

        public GetPlayerStats2Response Clone() => new GetPlayerStats2Response(this);

        public override bool Equals(object other) => Equals(other as GetPlayerStats2Response);

        public bool Equals(GetPlayerStats2Response other) => other != null && object.Equals(Data, other.Data);

        public override int GetHashCode() => Data != null ? Data.GetHashCode() : 0;

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {

            if (Data != null) { output.WriteRawTag(10); output.WriteMessage(Data); }

        }

        public int CalculateSize() {

            int size = 0;

            if (Data != null) size += 1 + pb::CodedOutputStream.ComputeMessageSize(Data);

            return size;

        }

        public void MergeFrom(GetPlayerStats2Response other) {

            if (other == null) return;

            if (other.Data != null) { if (Data == null) Data = new GetPlayerStats2ResponseData(); Data.MergeFrom(other.Data); }

        }

        public void MergeFrom(pb::CodedInputStream input) {

            uint tag;

            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) { if (Data == null) Data = new GetPlayerStats2ResponseData(); input.ReadMessage(Data); }
                else input.SkipLastField();
            }
        }
    }
        
    public sealed partial class GetOtherPlayerItemsRequest : pb::IMessage<GetOtherPlayerItemsRequest> {

        private static readonly pb::MessageParser<GetOtherPlayerItemsRequest> _parser = new pb::MessageParser<GetOtherPlayerItemsRequest>(() => new GetOtherPlayerItemsRequest());

        public static pb::MessageParser<GetOtherPlayerItemsRequest> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        public string PlayerId { get; set; } = "";

        private static readonly pb::FieldCodec<int> _repeated_categories_codec = pb::FieldCodec.ForInt32(18);

        public pb::Collections.RepeatedField<int> Categories { get; } = new pb::Collections.RepeatedField<int>();



        public GetOtherPlayerItemsRequest() { }

        public GetOtherPlayerItemsRequest(GetOtherPlayerItemsRequest other) : this() { PlayerId = other.PlayerId; Categories.Add(other.Categories); }

        public GetOtherPlayerItemsRequest Clone() => new GetOtherPlayerItemsRequest(this);

        public override bool Equals(object other) => Equals(other as GetOtherPlayerItemsRequest);

        public bool Equals(GetOtherPlayerItemsRequest other) => other != null && PlayerId == other.PlayerId && Categories.Equals(other.Categories);

        public override int GetHashCode() => PlayerId.GetHashCode() ^ Categories.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {

            if (PlayerId.Length != 0) { output.WriteRawTag(10); output.WriteString(PlayerId); }

            Categories.WriteTo(output, _repeated_categories_codec);

        }

        public int CalculateSize() {

            int size = 0;

            if (PlayerId.Length != 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(PlayerId);

            size += Categories.CalculateSize(_repeated_categories_codec);

            return size;

        }

        public void MergeFrom(GetOtherPlayerItemsRequest other) {

            if (other == null) return;

            if (other.PlayerId.Length != 0) PlayerId = other.PlayerId;

            Categories.Add(other.Categories);

        }

        public void MergeFrom(pb::CodedInputStream input) {

            uint tag;

            while ((tag = input.ReadTag()) != 0) {

                if (tag == 10) PlayerId = input.ReadString();

                else if (tag == 16 || tag == 18) Categories.AddEntriesFrom(input, _repeated_categories_codec);

                else input.SkipLastField();

            }

        }

    }



    public sealed partial class GetOtherPlayerItemsResponse : pb::IMessage<GetOtherPlayerItemsResponse> {

        private static readonly pb::MessageParser<GetOtherPlayerItemsResponse> _parser = new pb::MessageParser<GetOtherPlayerItemsResponse>(() => new GetOtherPlayerItemsResponse());

        public static pb::MessageParser<GetOtherPlayerItemsResponse> Parser => _parser;

        public static pbr::MessageDescriptor Descriptor => null;

        pbr::MessageDescriptor pb::IMessage.Descriptor => null;



        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.InventoryItem> _repeated_items_codec = pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.InventoryItem.Parser);

        public pb::Collections.RepeatedField<global::Axlebolt.Bolt.Protobuf.InventoryItem> Items { get; } = new pb::Collections.RepeatedField<global::Axlebolt.Bolt.Protobuf.InventoryItem>();



        public GetOtherPlayerItemsResponse() { }

        public GetOtherPlayerItemsResponse(GetOtherPlayerItemsResponse other) : this() { Items.Add(other.Items); }

        public GetOtherPlayerItemsResponse Clone() => new GetOtherPlayerItemsResponse(this);

        public override bool Equals(object other) => Equals(other as GetOtherPlayerItemsResponse);

        public bool Equals(GetOtherPlayerItemsResponse other) => other != null && Items.Equals(other.Items);

        public override int GetHashCode() => Items.GetHashCode();

        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

        public void WriteTo(pb::CodedOutputStream output) {

            Items.WriteTo(output, _repeated_items_codec);

        }

        public int CalculateSize() {

            int size = 0;

            size += Items.CalculateSize(_repeated_items_codec);

            return size;

        }

        public void MergeFrom(GetOtherPlayerItemsResponse other) {

            if (other == null) return;

            Items.Add(other.Items);

        }

        public void MergeFrom(pb::CodedInputStream input) {

            uint tag;

            while ((tag = input.ReadTag()) != 0) {

                if (tag == 10) Items.AddEntriesFrom(input, _repeated_items_codec);

                else input.SkipLastField();
}

}
}
    public sealed partial class GetClanMessages2Request : pb::IMessage<GetClanMessages2Request> {
        private static readonly pb::MessageParser<GetClanMessages2Request> _parser = new pb::MessageParser<GetClanMessages2Request>(() => new GetClanMessages2Request());
        public static pb::MessageParser<GetClanMessages2Request> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private int skip_ ;
        public int SkipCount { get => skip_; set => skip_ = value; }
        private int limit_ ;
        public int Limit { get => limit_; set => limit_ = value; }

        public GetClanMessages2Request() { }
        public GetClanMessages2Request(GetClanMessages2Request other) : this() {
            skip_ = other.skip_;
            limit_ = other.limit_;
        }
        public GetClanMessages2Request Clone() => new GetClanMessages2Request(this);
        public override bool Equals(object other) => Equals(other as GetClanMessages2Request);
        public bool Equals(GetClanMessages2Request other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            // tag 8
            if (SkipCount != 0) { output.WriteRawTag(8); output.WriteInt32(SkipCount); }
            // tag 16
            if (Limit != 0) { output.WriteRawTag(16); output.WriteInt32(Limit); }
        }
        public int CalculateSize() { return 0; }
        public void MergeFrom(GetClanMessages2Request other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 8: SkipCount = input.ReadInt32(); break;
                    case 16: Limit = input.ReadInt32(); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class GetClanMessages2Response : pb::IMessage<GetClanMessages2Response> {
        private static readonly pb::MessageParser<GetClanMessages2Response> _parser = new pb::MessageParser<GetClanMessages2Response>(() => new GetClanMessages2Response());
        public static pb::MessageParser<GetClanMessages2Response> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.ClanUserMessage> _repeated_codec = pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.ClanUserMessage.Parser);
        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanUserMessage> items_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanUserMessage>();
        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanUserMessage> Items => items_;

        public GetClanMessages2Response() { }
        public GetClanMessages2Response(GetClanMessages2Response other) : this() {
            items_.Add(other.items_);
        }
        public GetClanMessages2Response Clone() => new GetClanMessages2Response(this);
        public override bool Equals(object other) => Equals(other as GetClanMessages2Response);
        public bool Equals(GetClanMessages2Response other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            items_.WriteTo(output, _repeated_codec);
        }
        public int CalculateSize() {
            int size = 0;
            size += items_.CalculateSize(_repeated_codec);
            return size;
        }
        public void MergeFrom(GetClanMessages2Response other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 10: items_.AddEntriesFrom(input, _repeated_codec); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class GetClanChatMessages2Request : pb::IMessage<GetClanChatMessages2Request> {
        private static readonly pb::MessageParser<GetClanChatMessages2Request> _parser = new pb::MessageParser<GetClanChatMessages2Request>(() => new GetClanChatMessages2Request());
        public static pb::MessageParser<GetClanChatMessages2Request> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private int skip_ ;
        public int SkipCount { get => skip_; set => skip_ = value; }
        private int limit_ ;
        public int Limit { get => limit_; set => limit_ = value; }

        public GetClanChatMessages2Request() { }
        public GetClanChatMessages2Request(GetClanChatMessages2Request other) : this() {
            skip_ = other.skip_;
            limit_ = other.limit_;
        }
        public GetClanChatMessages2Request Clone() => new GetClanChatMessages2Request(this);
        public override bool Equals(object other) => Equals(other as GetClanChatMessages2Request);
        public bool Equals(GetClanChatMessages2Request other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            // tag 8
            if (SkipCount != 0) { output.WriteRawTag(8); output.WriteInt32(SkipCount); }
            // tag 16
            if (Limit != 0) { output.WriteRawTag(16); output.WriteInt32(Limit); }
        }
        public int CalculateSize() { return 0; }
        public void MergeFrom(GetClanChatMessages2Request other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 8: SkipCount = input.ReadInt32(); break;
                    case 16: Limit = input.ReadInt32(); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class GetClanChatMessages2Response : pb::IMessage<GetClanChatMessages2Response> {
        private static readonly pb::MessageParser<GetClanChatMessages2Response> _parser = new pb::MessageParser<GetClanChatMessages2Response>(() => new GetClanChatMessages2Response());
        public static pb::MessageParser<GetClanChatMessages2Response> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.ClanUserMessage> _repeated_codec = pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.ClanUserMessage.Parser);
        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanUserMessage> items_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanUserMessage>();
        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanUserMessage> Items => items_;

        public GetClanChatMessages2Response() { }
        public GetClanChatMessages2Response(GetClanChatMessages2Response other) : this() {
            items_.Add(other.items_);
        }
        public GetClanChatMessages2Response Clone() => new GetClanChatMessages2Response(this);
        public override bool Equals(object other) => Equals(other as GetClanChatMessages2Response);
        public bool Equals(GetClanChatMessages2Response other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            items_.WriteTo(output, _repeated_codec);
        }
        public int CalculateSize() {
            int size = 0;
            size += items_.CalculateSize(_repeated_codec);
            return size;
        }
        public void MergeFrom(GetClanChatMessages2Response other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 10: items_.AddEntriesFrom(input, _repeated_codec); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class GetClanLogMessages2Request : pb::IMessage<GetClanLogMessages2Request> {
        private static readonly pb::MessageParser<GetClanLogMessages2Request> _parser = new pb::MessageParser<GetClanLogMessages2Request>(() => new GetClanLogMessages2Request());
        public static pb::MessageParser<GetClanLogMessages2Request> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private int skip_ ;
        public int SkipCount { get => skip_; set => skip_ = value; }
        private int limit_ ;
        public int Limit { get => limit_; set => limit_ = value; }

        public GetClanLogMessages2Request() { }
        public GetClanLogMessages2Request(GetClanLogMessages2Request other) : this() {
            skip_ = other.skip_;
            limit_ = other.limit_;
        }
        public GetClanLogMessages2Request Clone() => new GetClanLogMessages2Request(this);
        public override bool Equals(object other) => Equals(other as GetClanLogMessages2Request);
        public bool Equals(GetClanLogMessages2Request other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            // tag 8
            if (SkipCount != 0) { output.WriteRawTag(8); output.WriteInt32(SkipCount); }
            // tag 16
            if (Limit != 0) { output.WriteRawTag(16); output.WriteInt32(Limit); }
        }
        public int CalculateSize() { return 0; }
        public void MergeFrom(GetClanLogMessages2Request other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 8: SkipCount = input.ReadInt32(); break;
                    case 16: Limit = input.ReadInt32(); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class GetClanLogMessages2Response : pb::IMessage<GetClanLogMessages2Response> {
        private static readonly pb::MessageParser<GetClanLogMessages2Response> _parser = new pb::MessageParser<GetClanLogMessages2Response>(() => new GetClanLogMessages2Response());
        public static pb::MessageParser<GetClanLogMessages2Response> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.ClanUserMessage> _repeated_codec = pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.ClanUserMessage.Parser);
        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanUserMessage> items_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanUserMessage>();
        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanUserMessage> Items => items_;

        public GetClanLogMessages2Response() { }
        public GetClanLogMessages2Response(GetClanLogMessages2Response other) : this() {
            items_.Add(other.items_);
        }
        public GetClanLogMessages2Response Clone() => new GetClanLogMessages2Response(this);
        public override bool Equals(object other) => Equals(other as GetClanLogMessages2Response);
        public bool Equals(GetClanLogMessages2Response other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            items_.WriteTo(output, _repeated_codec);
        }
        public int CalculateSize() {
            int size = 0;
            size += items_.CalculateSize(_repeated_codec);
            return size;
        }
        public void MergeFrom(GetClanLogMessages2Response other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 10: items_.AddEntriesFrom(input, _repeated_codec); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class SendClanChatMessage2Request : pb::IMessage<SendClanChatMessage2Request> {
        private static readonly pb::MessageParser<SendClanChatMessage2Request> _parser = new pb::MessageParser<SendClanChatMessage2Request>(() => new SendClanChatMessage2Request());
        public static pb::MessageParser<SendClanChatMessage2Request> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string message_ ="";
        public string Message { get => message_; set => message_ = value; }

        public SendClanChatMessage2Request() { }
        public SendClanChatMessage2Request(SendClanChatMessage2Request other) : this() {
            message_ = other.message_;
        }
        public SendClanChatMessage2Request Clone() => new SendClanChatMessage2Request(this);
        public override bool Equals(object other) => Equals(other as SendClanChatMessage2Request);
        public bool Equals(SendClanChatMessage2Request other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            // tag 10
            if (Message.Length != 0) { output.WriteRawTag(10); output.WriteString(Message); }
        }
        public int CalculateSize() { return 0; }
        public void MergeFrom(SendClanChatMessage2Request other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 10: Message = input.ReadString(); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class SendClanChatMessage2Response : pb::IMessage<SendClanChatMessage2Response> {
        private static readonly pb::MessageParser<SendClanChatMessage2Response> _parser = new pb::MessageParser<SendClanChatMessage2Response>(() => new SendClanChatMessage2Response());
        public static pb::MessageParser<SendClanChatMessage2Response> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;


        public SendClanChatMessage2Response() { }
        public SendClanChatMessage2Response(SendClanChatMessage2Response other) : this() {
        }
        public SendClanChatMessage2Response Clone() => new SendClanChatMessage2Response(this);
        public override bool Equals(object other) => Equals(other as SendClanChatMessage2Response);
        public bool Equals(SendClanChatMessage2Response other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
        }
        public int CalculateSize() {
            int size = 0;
            return size;
        }
        public void MergeFrom(SendClanChatMessage2Response other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class RequestToJoinClan2Request : pb::IMessage<RequestToJoinClan2Request> {
        private static readonly pb::MessageParser<RequestToJoinClan2Request> _parser = new pb::MessageParser<RequestToJoinClan2Request>(() => new RequestToJoinClan2Request());
        public static pb::MessageParser<RequestToJoinClan2Request> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string message_ ="";
        public string Message { get => message_; set => message_ = value; }

        public RequestToJoinClan2Request() { }
        public RequestToJoinClan2Request(RequestToJoinClan2Request other) : this() {
            message_ = other.message_;
        }
        public RequestToJoinClan2Request Clone() => new RequestToJoinClan2Request(this);
        public override bool Equals(object other) => Equals(other as RequestToJoinClan2Request);
        public bool Equals(RequestToJoinClan2Request other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            // tag 10
            if (Message.Length != 0) { output.WriteRawTag(10); output.WriteString(Message); }
        }
        public int CalculateSize() { return 0; }
        public void MergeFrom(RequestToJoinClan2Request other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 10: Message = input.ReadString(); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class RequestToJoinClan2Response : pb::IMessage<RequestToJoinClan2Response> {
        private static readonly pb::MessageParser<RequestToJoinClan2Response> _parser = new pb::MessageParser<RequestToJoinClan2Response>(() => new RequestToJoinClan2Response());
        public static pb::MessageParser<RequestToJoinClan2Response> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;


        public RequestToJoinClan2Response() { }
        public RequestToJoinClan2Response(RequestToJoinClan2Response other) : this() {
        }
        public RequestToJoinClan2Response Clone() => new RequestToJoinClan2Response(this);
        public override bool Equals(object other) => Equals(other as RequestToJoinClan2Response);
        public bool Equals(RequestToJoinClan2Response other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
        }
        public int CalculateSize() {
            int size = 0;
            return size;
        }
        public void MergeFrom(RequestToJoinClan2Response other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class LeaveClan2Request : pb::IMessage<LeaveClan2Request> {
        private static readonly pb::MessageParser<LeaveClan2Request> _parser = new pb::MessageParser<LeaveClan2Request>(() => new LeaveClan2Request());
        public static pb::MessageParser<LeaveClan2Request> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;


        public LeaveClan2Request() { }
        public LeaveClan2Request(LeaveClan2Request other) : this() {
        }
        public LeaveClan2Request Clone() => new LeaveClan2Request(this);
        public override bool Equals(object other) => Equals(other as LeaveClan2Request);
        public bool Equals(LeaveClan2Request other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
        }
        public int CalculateSize() { return 0; }
        public void MergeFrom(LeaveClan2Request other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class LeaveClan2Response : pb::IMessage<LeaveClan2Response> {
        private static readonly pb::MessageParser<LeaveClan2Response> _parser = new pb::MessageParser<LeaveClan2Response>(() => new LeaveClan2Response());
        public static pb::MessageParser<LeaveClan2Response> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;


        public LeaveClan2Response() { }
        public LeaveClan2Response(LeaveClan2Response other) : this() {
        }
        public LeaveClan2Response Clone() => new LeaveClan2Response(this);
        public override bool Equals(object other) => Equals(other as LeaveClan2Response);
        public bool Equals(LeaveClan2Response other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
        }
        public int CalculateSize() {
            int size = 0;
            return size;
        }
        public void MergeFrom(LeaveClan2Response other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class GetClanMembersById2Request : pb::IMessage<GetClanMembersById2Request> {
        private static readonly pb::MessageParser<GetClanMembersById2Request> _parser = new pb::MessageParser<GetClanMembersById2Request>(() => new GetClanMembersById2Request());
        public static pb::MessageParser<GetClanMembersById2Request> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string clanId_ ="";
        public string ClanId { get => clanId_; set => clanId_ = value; }

        public GetClanMembersById2Request() { }
        public GetClanMembersById2Request(GetClanMembersById2Request other) : this() {
            clanId_ = other.clanId_;
        }
        public GetClanMembersById2Request Clone() => new GetClanMembersById2Request(this);
        public override bool Equals(object other) => Equals(other as GetClanMembersById2Request);
        public bool Equals(GetClanMembersById2Request other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            // tag 10
            if (ClanId.Length != 0) { output.WriteRawTag(10); output.WriteString(ClanId); }
        }
        public int CalculateSize() { return 0; }
        public void MergeFrom(GetClanMembersById2Request other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 10: ClanId = input.ReadString(); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class GetClanMembersById2Response : pb::IMessage<GetClanMembersById2Response> {
        private static readonly pb::MessageParser<GetClanMembersById2Response> _parser = new pb::MessageParser<GetClanMembersById2Response>(() => new GetClanMembersById2Response());
        public static pb::MessageParser<GetClanMembersById2Response> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.ClanMember> _repeated_codec = pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.ClanMember.Parser);
        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanMember> items_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanMember>();
        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.ClanMember> Items => items_;

        public GetClanMembersById2Response() { }
        public GetClanMembersById2Response(GetClanMembersById2Response other) : this() {
            items_.Add(other.items_);
        }
        public GetClanMembersById2Response Clone() => new GetClanMembersById2Response(this);
        public override bool Equals(object other) => Equals(other as GetClanMembersById2Response);
        public bool Equals(GetClanMembersById2Response other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            items_.WriteTo(output, _repeated_codec);
        }
        public int CalculateSize() {
            int size = 0;
            size += items_.CalculateSize(_repeated_codec);
            return size;
        }
        public void MergeFrom(GetClanMembersById2Response other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 10: items_.AddEntriesFrom(input, _repeated_codec); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class FindClan2Request : pb::IMessage<FindClan2Request> {
        private static readonly pb::MessageParser<FindClan2Request> _parser = new pb::MessageParser<FindClan2Request>(() => new FindClan2Request());
        public static pb::MessageParser<FindClan2Request> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private string search_ ="";
        public string Search { get => search_; set => search_ = value; }
        private int skip_ ;
        public int SkipCount { get => skip_; set => skip_ = value; }
        private int limit_ ;
        public int Limit { get => limit_; set => limit_ = value; }

        public FindClan2Request() { }
        public FindClan2Request(FindClan2Request other) : this() {
            search_ = other.search_;
            skip_ = other.skip_;
            limit_ = other.limit_;
        }
        public FindClan2Request Clone() => new FindClan2Request(this);
        public override bool Equals(object other) => Equals(other as FindClan2Request);
        public bool Equals(FindClan2Request other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            // tag 10
            if (Search.Length != 0) { output.WriteRawTag(10); output.WriteString(Search); }
            // tag 16
            if (SkipCount != 0) { output.WriteRawTag(16); output.WriteInt32(SkipCount); }
            // tag 24
            if (Limit != 0) { output.WriteRawTag(24); output.WriteInt32(Limit); }
        }
        public int CalculateSize() { return 0; }
        public void MergeFrom(FindClan2Request other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 10: Search = input.ReadString(); break;
                    case 16: SkipCount = input.ReadInt32(); break;
                    case 24: Limit = input.ReadInt32(); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }
    public sealed partial class FindClan2Response : pb::IMessage<FindClan2Response> {
        private static readonly pb::MessageParser<FindClan2Response> _parser = new pb::MessageParser<FindClan2Response>(() => new FindClan2Response());
        public static pb::MessageParser<FindClan2Response> Parser => _parser;
        public static pbr::MessageDescriptor Descriptor => null;
        pbr::MessageDescriptor pb::IMessage.Descriptor => null;

        private static readonly pb::FieldCodec<global::Axlebolt.Bolt.Protobuf.Clan> _repeated_codec = pb::FieldCodec.ForMessage(10, global::Axlebolt.Bolt.Protobuf.Clan.Parser);
        private readonly pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Clan> items_ = new pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Clan>();
        public pbc::RepeatedField<global::Axlebolt.Bolt.Protobuf.Clan> Items => items_;

        public FindClan2Response() { }
        public FindClan2Response(FindClan2Response other) : this() {
            items_.Add(other.items_);
        }
        public FindClan2Response Clone() => new FindClan2Response(this);
        public override bool Equals(object other) => Equals(other as FindClan2Response);
        public bool Equals(FindClan2Response other) => other != null;
        public override int GetHashCode() => 1;
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        public void WriteTo(pb::CodedOutputStream output) {
            items_.WriteTo(output, _repeated_codec);
        }
        public int CalculateSize() {
            int size = 0;
            size += items_.CalculateSize(_repeated_codec);
            return size;
        }
        public void MergeFrom(FindClan2Response other) { }
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                switch(tag) {
                    case 10: items_.AddEntriesFrom(input, _repeated_codec); break;
                    default: input.SkipLastField(); break;
                }
            }
        }
    }

    // Chat 2
    public sealed partial class IAHNFFPHEPH : pb::IMessage<IAHNFFPHEPH> {
        public IAHNFFPHEPH() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(IAHNFFPHEPH other) {}
        public bool Equals(IAHNFFPHEPH other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {}
        public int CalculateSize() => 0;
        public IAHNFFPHEPH Clone() => new IAHNFFPHEPH();
    }

    public sealed partial class IEINDJAEFAM : pb::IMessage<IEINDJAEFAM> {
        public readonly pbc::RepeatedField<ChatUser> Users = new pbc::RepeatedField<ChatUser>();
        public IEINDJAEFAM() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(IEINDJAEFAM other) {}
        public bool Equals(IEINDJAEFAM other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) {
                    Users.AddEntriesFrom(input, pb::FieldCodec.ForMessage(10, ChatUser.Parser));
                } else {
                    input.SkipLastField();
                }
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            Users.WriteTo(output, pb::FieldCodec.ForMessage(10, ChatUser.Parser));
        }
        public int CalculateSize() {
            return Users.CalculateSize(pb::FieldCodec.ForMessage(10, ChatUser.Parser));
        }
        public IEINDJAEFAM Clone() { var c = new IEINDJAEFAM(); c.Users.Add(Users); return c; }
    }

    public sealed partial class FDAJKEOABND : pb::IMessage<FDAJKEOABND> {
        public string FriendId = "";
        public FDAJKEOABND() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(FDAJKEOABND other) {}
        public bool Equals(FDAJKEOABND other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) FriendId = input.ReadString();
                else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            if (FriendId.Length > 0) {
                output.WriteRawTag(10);
                output.WriteString(FriendId);
            }
        }
        public int CalculateSize() => FriendId.Length > 0 ? pb::CodedOutputStream.ComputeStringSize(FriendId) + 1 : 0;
        public FDAJKEOABND Clone() => new FDAJKEOABND { FriendId = FriendId };
    }

    public sealed partial class NGIACCDBMJC : pb::IMessage<NGIACCDBMJC> {
        public NGIACCDBMJC() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(NGIACCDBMJC other) {}
        public bool Equals(NGIACCDBMJC other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {}
        public int CalculateSize() => 0;
        public NGIACCDBMJC Clone() => new NGIACCDBMJC();
    }

    public sealed partial class HLCEEOEKNDP : pb::IMessage<HLCEEOEKNDP> {
        public int Offset = 0;
        public HLCEEOEKNDP() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(HLCEEOEKNDP other) {}
        public bool Equals(HLCEEOEKNDP other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 8) Offset = input.ReadInt32();
                else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            if (Offset != 0) {
                output.WriteRawTag(8);
                output.WriteInt32(Offset);
            }
        }
        public int CalculateSize() => Offset != 0 ? pb::CodedOutputStream.ComputeInt32Size(Offset) + 1 : 0;
        public HLCEEOEKNDP Clone() => new HLCEEOEKNDP { Offset = Offset };
    }

    public sealed partial class NHKKPEBIALH : pb::IMessage<NHKKPEBIALH> {
        public string FriendId = "";
        public HLCEEOEKNDP Offset = new HLCEEOEKNDP();
        public NHKKPEBIALH() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(NHKKPEBIALH other) {}
        public bool Equals(NHKKPEBIALH other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) FriendId = input.ReadString();
                else if (tag == 18) {
                    var builder = new HLCEEOEKNDP();
                    input.ReadMessage(builder);
                    Offset = builder;
                }
                else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            if (FriendId.Length > 0) {
                output.WriteRawTag(10);
                output.WriteString(FriendId);
            }
            if (Offset != null && Offset.CalculateSize() > 0) {
                output.WriteRawTag(18);
                output.WriteMessage(Offset);
            }
        }
        public int CalculateSize() {
            int size = 0;
            if (FriendId.Length > 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(FriendId);
            if (Offset != null && Offset.CalculateSize() > 0) size += 1 + pb::CodedOutputStream.ComputeMessageSize(Offset);
            return size;
        }
        public NHKKPEBIALH Clone() => new NHKKPEBIALH { FriendId = FriendId, Offset = Offset.Clone() };
    }

    public sealed partial class OLEEDCHIKAA : pb::IMessage<OLEEDCHIKAA> {
        public readonly pbc::RepeatedField<UserMessage> Messages = new pbc::RepeatedField<UserMessage>();
        public OLEEDCHIKAA() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(OLEEDCHIKAA other) {}
        public bool Equals(OLEEDCHIKAA other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) Messages.AddEntriesFrom(input, pb::FieldCodec.ForMessage(10, UserMessage.Parser));
                else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            Messages.WriteTo(output, pb::FieldCodec.ForMessage(10, UserMessage.Parser));
        }
        public int CalculateSize() => Messages.CalculateSize(pb::FieldCodec.ForMessage(10, UserMessage.Parser));
        public OLEEDCHIKAA Clone() { var c = new OLEEDCHIKAA(); c.Messages.Add(Messages); return c; }
    }

    public sealed partial class KGPDINEDBNF : pb::IMessage<KGPDINEDBNF> {
        public HLCEEOEKNDP Offset = new HLCEEOEKNDP();
        public KGPDINEDBNF() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(KGPDINEDBNF other) {}
        public bool Equals(KGPDINEDBNF other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) {
                    var builder = new HLCEEOEKNDP();
                    input.ReadMessage(builder);
                    Offset = builder;
                } else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            if (Offset != null && Offset.CalculateSize() > 0) {
                output.WriteRawTag(10);
                output.WriteMessage(Offset);
            }
        }
        public int CalculateSize() => (Offset != null && Offset.CalculateSize() > 0) ? 1 + pb::CodedOutputStream.ComputeMessageSize(Offset) : 0;
        public KGPDINEDBNF Clone() => new KGPDINEDBNF { Offset = Offset.Clone() };
    }

    public sealed partial class FAKEJMLPEHN : pb::IMessage<FAKEJMLPEHN> {
        public readonly pbc::RepeatedField<ChatUser> Users = new pbc::RepeatedField<ChatUser>();
        public FAKEJMLPEHN() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(FAKEJMLPEHN other) {}
        public bool Equals(FAKEJMLPEHN other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) Users.AddEntriesFrom(input, pb::FieldCodec.ForMessage(10, ChatUser.Parser));
                else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            Users.WriteTo(output, pb::FieldCodec.ForMessage(10, ChatUser.Parser));
        }
        public int CalculateSize() => Users.CalculateSize(pb::FieldCodec.ForMessage(10, ChatUser.Parser));
        public FAKEJMLPEHN Clone() { var c = new FAKEJMLPEHN(); c.Users.Add(Users); return c; }
    }

    public sealed partial class DJOGNFDHPHL : pb::IMessage<DJOGNFDHPHL> {
        public DJOGNFDHPHL() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(DJOGNFDHPHL other) {}
        public bool Equals(DJOGNFDHPHL other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) input.SkipLastField();
        }
        public void WriteTo(pb::CodedOutputStream output) {}
        public int CalculateSize() => 0;
        public DJOGNFDHPHL Clone() => new DJOGNFDHPHL();
    }

    public sealed partial class LFFHLDJOHMI : pb::IMessage<LFFHLDJOHMI> {
        public int Count = 0;
        public LFFHLDJOHMI() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(LFFHLDJOHMI other) {}
        public bool Equals(LFFHLDJOHMI other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 8) Count = input.ReadInt32();
                else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            if (Count != 0) {
                output.WriteRawTag(8);
                output.WriteInt32(Count);
            }
        }
        public int CalculateSize() => Count != 0 ? 1 + pb::CodedOutputStream.ComputeInt32Size(Count) : 0;
        public LFFHLDJOHMI Clone() => new LFFHLDJOHMI { Count = Count };
    }

    public sealed partial class JADIJMAKOAC : pb::IMessage<JADIJMAKOAC> {
        public string FriendId = "";
        public string Message = "";
        public JADIJMAKOAC() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(JADIJMAKOAC other) {}
        public bool Equals(JADIJMAKOAC other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) FriendId = input.ReadString();
                else if (tag == 18) Message = input.ReadString();
                else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            if (FriendId.Length > 0) { output.WriteRawTag(10); output.WriteString(FriendId); }
            if (Message.Length > 0) { output.WriteRawTag(18); output.WriteString(Message); }
        }
        public int CalculateSize() {
            int size = 0;
            if (FriendId.Length > 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(FriendId);
            if (Message.Length > 0) size += 1 + pb::CodedOutputStream.ComputeStringSize(Message);
            return size;
        }
        public JADIJMAKOAC Clone() => new JADIJMAKOAC { FriendId = FriendId, Message = Message };
    }

    public sealed partial class FPDMIGJDNOK : pb::IMessage<FPDMIGJDNOK> {
        public FPDMIGJDNOK() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(FPDMIGJDNOK other) {}
        public bool Equals(FPDMIGJDNOK other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) input.SkipLastField();
        }
        public void WriteTo(pb::CodedOutputStream output) {}
        public int CalculateSize() => 0;
        public FPDMIGJDNOK Clone() => new FPDMIGJDNOK();
    }

    public sealed partial class ILEHBMLCGNB : pb::IMessage<ILEHBMLCGNB> {
        public string FriendId = "";
        public ILEHBMLCGNB() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(ILEHBMLCGNB other) {}
        public bool Equals(ILEHBMLCGNB other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) FriendId = input.ReadString();
                else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            if (FriendId.Length > 0) { output.WriteRawTag(10); output.WriteString(FriendId); }
        }
        public int CalculateSize() => FriendId.Length > 0 ? 1 + pb::CodedOutputStream.ComputeStringSize(FriendId) : 0;
        public ILEHBMLCGNB Clone() => new ILEHBMLCGNB { FriendId = FriendId };
    }

    public sealed partial class BJHICOKNOBI : pb::IMessage<BJHICOKNOBI> {
        public BJHICOKNOBI() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(BJHICOKNOBI other) {}
        public bool Equals(BJHICOKNOBI other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) input.SkipLastField();
        }
        public void WriteTo(pb::CodedOutputStream output) {}
        public int CalculateSize() => 0;
        public BJHICOKNOBI Clone() => new BJHICOKNOBI();
    }

    public sealed partial class FPDEPHOGNDB : pb::IMessage<FPDEPHOGNDB> {
        public FPDEPHOGNDB() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(FPDEPHOGNDB other) {}
        public bool Equals(FPDEPHOGNDB other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) input.SkipLastField();
        }
        public void WriteTo(pb::CodedOutputStream output) {}
        public int CalculateSize() => 0;
        public FPDEPHOGNDB Clone() => new FPDEPHOGNDB();
    }

    public sealed partial class ABJAMBOFOAA : pb::IMessage<ABJAMBOFOAA> {
        public readonly pbc::RepeatedField<ChatUser> Users = new pbc::RepeatedField<ChatUser>();
        public ABJAMBOFOAA() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(ABJAMBOFOAA other) {}
        public bool Equals(ABJAMBOFOAA other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) Users.AddEntriesFrom(input, pb::FieldCodec.ForMessage(10, ChatUser.Parser));
                else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            Users.WriteTo(output, pb::FieldCodec.ForMessage(10, ChatUser.Parser));
        }
        public int CalculateSize() => Users.CalculateSize(pb::FieldCodec.ForMessage(10, ChatUser.Parser));
        public ABJAMBOFOAA Clone() { var c = new ABJAMBOFOAA(); c.Users.Add(Users); return c; }
    }

    public sealed partial class EGIAHMODMAC : pb::IMessage<EGIAHMODMAC> {
        public int Page = 0;
        public EGIAHMODMAC() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(EGIAHMODMAC other) {}
        public bool Equals(EGIAHMODMAC other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 8) Page = input.ReadInt32();
                else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            if (Page != 0) { output.WriteRawTag(8); output.WriteInt32(Page); }
        }
        public int CalculateSize() => Page != 0 ? 1 + pb::CodedOutputStream.ComputeInt32Size(Page) : 0;
        public EGIAHMODMAC Clone() => new EGIAHMODMAC { Page = Page };
    }

    public sealed partial class BCFIBAOJDOH : pb::IMessage<BCFIBAOJDOH> {
        public EGIAHMODMAC Page = new EGIAHMODMAC();
        public BCFIBAOJDOH() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(BCFIBAOJDOH other) {}
        public bool Equals(BCFIBAOJDOH other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) {
                    var builder = new EGIAHMODMAC();
                    input.ReadMessage(builder);
                    Page = builder;
                } else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            if (Page != null && Page.CalculateSize() > 0) {
                output.WriteRawTag(10);
                output.WriteMessage(Page);
            }
        }
        public int CalculateSize() => (Page != null && Page.CalculateSize() > 0) ? 1 + pb::CodedOutputStream.ComputeMessageSize(Page) : 0;
        public BCFIBAOJDOH Clone() => new BCFIBAOJDOH { Page = Page.Clone() };
    }

    public sealed partial class COHNBOGIGKA : pb::IMessage<COHNBOGIGKA> {
        public readonly pbc::RepeatedField<ChatUser> Users = new pbc::RepeatedField<ChatUser>();
        public COHNBOGIGKA() {}
        public pb::Reflection.MessageDescriptor Descriptor => null;
        public void MergeFrom(COHNBOGIGKA other) {}
        public bool Equals(COHNBOGIGKA other) => true;
        public void MergeFrom(pb::CodedInputStream input) {
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                if (tag == 10) Users.AddEntriesFrom(input, pb::FieldCodec.ForMessage(10, ChatUser.Parser));
                else input.SkipLastField();
            }
        }
        public void WriteTo(pb::CodedOutputStream output) {
            Users.WriteTo(output, pb::FieldCodec.ForMessage(10, ChatUser.Parser));
        }
        public int CalculateSize() => Users.CalculateSize(pb::FieldCodec.ForMessage(10, ChatUser.Parser));
        public COHNBOGIGKA Clone() { var c = new COHNBOGIGKA(); c.Users.Add(Users); return c; }
    }

}
