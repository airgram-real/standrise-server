using Google.Protobuf;
using Google.Protobuf.Reflection;
using System;

namespace Axlebolt.Bolt.Protobuf
{
    public sealed class GetPlayerRankRequest : IMessage<GetPlayerRankRequest>
    {
        public string LeaderboardCode { get; set; } = "";
        public string PlayerId { get; set; } = "";

        public MessageDescriptor Descriptor => null;
        public void MergeFrom(GetPlayerRankRequest other) { }
        public void MergeFrom(CodedInputStream input) { }
        public void WriteTo(CodedOutputStream output) { }
        public int CalculateSize() => 0;
        public GetPlayerRankRequest Clone() => new GetPlayerRankRequest { LeaderboardCode = this.LeaderboardCode, PlayerId = this.PlayerId };
        public bool Equals(GetPlayerRankRequest other) => other != null && other.LeaderboardCode == LeaderboardCode && other.PlayerId == PlayerId;
    }

    public sealed class GetPlayerRankResponse : IMessage<GetPlayerRankResponse>
    {
        public LeaderboardEntry Entry { get; set; }

        public MessageDescriptor Descriptor => null;
        public void MergeFrom(GetPlayerRankResponse other) { }
        public void MergeFrom(CodedInputStream input) { }
        public void WriteTo(CodedOutputStream output) { }
        public int CalculateSize() => 0;
        public GetPlayerRankResponse Clone() => new GetPlayerRankResponse { Entry = this.Entry };
        public bool Equals(GetPlayerRankResponse other) => other != null && Equals(other.Entry, Entry);
    }

    public sealed class LeaderboardEntry : IMessage<LeaderboardEntry>
    {
        public string PlayerId { get; set; } = "";
        public int Rank { get; set; }
        public int Points { get; set; }
        public int Position { get; set; }

        public MessageDescriptor Descriptor => null;
        public void MergeFrom(LeaderboardEntry other) { }
        public void MergeFrom(CodedInputStream input) { }
        public void WriteTo(CodedOutputStream output) { }
        public int CalculateSize() => 0;
        public LeaderboardEntry Clone() => new LeaderboardEntry { PlayerId = this.PlayerId, Rank = this.Rank, Points = this.Points, Position = this.Position };
        public bool Equals(LeaderboardEntry other) => other != null && other.PlayerId == PlayerId && other.Rank == Rank && other.Points == Points && other.Position == Position;
    }

    public sealed class MatchmakingRequest : IMessage<MatchmakingRequest>
    {
        public string Profile { get; set; } = "";
        public Axlebolt.Bolt.Matchmaking.Protobuf.Filter Filter { get; set; }
        public string GroupId { get; set; } = "";
        public int GroupSize { get; set; }

        public static MessageParser<MatchmakingRequest> Parser { get; } = new MessageParser<MatchmakingRequest>(() => new MatchmakingRequest());
        public MessageDescriptor Descriptor => null;
        public void MergeFrom(MatchmakingRequest other)
        {
            if (other == null) return;
            if (other.Profile != "") Profile = other.Profile;
            if (other.Filter != null)
            {
                if (Filter == null) Filter = new Axlebolt.Bolt.Matchmaking.Protobuf.Filter();
                Filter.MergeFrom(other.Filter);
            }
            if (other.GroupId != "") GroupId = other.GroupId;
            if (other.GroupSize != 0) GroupSize = other.GroupSize;
        }

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 10: Profile = input.ReadString(); break;
                    case 18: if (Filter == null) Filter = new Axlebolt.Bolt.Matchmaking.Protobuf.Filter(); input.ReadMessage(Filter); break;
                    case 26: GroupId = input.ReadString(); break;
                    case 32: GroupSize = input.ReadInt32(); break;
                    default: input.SkipLastField(); break;
                }
            }
        }

        public void WriteTo(CodedOutputStream output)
        {
            if (Profile != "") { output.WriteRawTag(10); output.WriteString(Profile); }
            if (Filter != null) { output.WriteRawTag(18); output.WriteMessage(Filter); }
            if (GroupId != "") { output.WriteRawTag(26); output.WriteString(GroupId); }
            if (GroupSize != 0) { output.WriteRawTag(32); output.WriteInt32(GroupSize); }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (Profile != "") size += 1 + CodedOutputStream.ComputeStringSize(Profile);
            if (Filter != null) size += 1 + CodedOutputStream.ComputeMessageSize(Filter);
            if (GroupId != "") size += 1 + CodedOutputStream.ComputeStringSize(GroupId);
            if (GroupSize != 0) size += 1 + CodedOutputStream.ComputeInt32Size(GroupSize);
            return size;
        }
        public MatchmakingRequest Clone() => new MatchmakingRequest { Profile = this.Profile, Filter = this.Filter, GroupId = this.GroupId, GroupSize = this.GroupSize };
        public bool Equals(MatchmakingRequest other) => other != null && other.Profile == Profile && Equals(other.Filter, Filter) && other.GroupId == GroupId && other.GroupSize == GroupSize;
    }
}

namespace Axlebolt.Bolt.Matchmaking.Protobuf
{
    public class Filter : IMessage<Filter>
    {
        public MessageDescriptor Descriptor => null;
        public string Condition { get; set; } = "";
        public void MergeFrom(Filter other) { if (other != null && other.Condition != "") Condition = other.Condition; }
        public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) { if (tag == 10) Condition = input.ReadString(); else input.SkipLastField(); } }
        public void WriteTo(CodedOutputStream output) { if (Condition != "") { output.WriteRawTag(10); output.WriteString(Condition); } }
        public int CalculateSize() => Condition != "" ? 1 + CodedOutputStream.ComputeStringSize(Condition) : 0;
        public Filter Clone() => new Filter { Condition = Condition };
        public bool Equals(Filter other) => other != null && other.Condition == Condition;
    }

    public class Player : IMessage<Player>
    {
        public MessageDescriptor Descriptor => null;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Online { get; set; }
        public bool Confirmed { get; set; }
        public string ClanId { get; set; } = "";

        public static MessageParser<Player> Parser = new MessageParser<Player>(() => new Player());
        private static readonly FieldCodec<Player> _codec = FieldCodec.ForMessage(10, Parser);

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 10: Id = input.ReadString(); break;
                    case 18: Name = input.ReadString(); break;
                    case 24: Online = input.ReadBool(); break;
                    case 32: Confirmed = input.ReadBool(); break;
                    case 42: ClanId = input.ReadString(); break;
                    default: input.SkipLastField(); break;
                }
            }
        }

        public void MergeFrom(Player other)
        {
            if (other == null) return;
            if (other.Id != "") Id = other.Id;
            if (other.Name != "") Name = other.Name;
            if (other.Online) Online = other.Online;
            if (other.Confirmed) Confirmed = other.Confirmed;
            if (other.ClanId != "") ClanId = other.ClanId;
        }

        public void WriteTo(CodedOutputStream output)
        {
            if (Id != "") { output.WriteRawTag(10); output.WriteString(Id); }
            if (Name != "") { output.WriteRawTag(18); output.WriteString(Name); }
            if (Online) { output.WriteRawTag(24); output.WriteBool(Online); }
            output.WriteRawTag(32); output.WriteBool(Confirmed);
            if (ClanId != "") { output.WriteRawTag(42); output.WriteString(ClanId); }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (Id != "") size += 1 + CodedOutputStream.ComputeStringSize(Id);
            if (Name != "") size += 1 + CodedOutputStream.ComputeStringSize(Name);
            if (Online) size += 2;
            size += 2;
            if (ClanId != "") size += 1 + CodedOutputStream.ComputeStringSize(ClanId);
            return size;
        }
        public Player Clone() => new Player { Id = Id, Name = Name, Online = Online, Confirmed = Confirmed, ClanId = ClanId };
        public bool Equals(Player other) => other != null && other.Id == Id && other.Name == Name && other.Online == Online && other.Confirmed == Confirmed && other.ClanId == ClanId;
    }

    public class Group : IMessage<Group>
    {
        public MessageDescriptor Descriptor => null;
        public Google.Protobuf.Collections.RepeatedField<Player> Players { get; } = new Google.Protobuf.Collections.RepeatedField<Player>();

        public static MessageParser<Group> Parser = new MessageParser<Group>(() => new Group());
        private static readonly FieldCodec<Player> _playersCodec = FieldCodec.ForMessage(10, Player.Parser);

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10) Players.AddEntriesFrom(input, _playersCodec);
                else input.SkipLastField();
            }
        }

        public void MergeFrom(Group other)
        {
            if (other == null) return;
            Players.Add(other.Players);
        }

        public void WriteTo(CodedOutputStream output)
        {
            Players.WriteTo(output, _playersCodec);
        }

        public int CalculateSize() { return Players.CalculateSize(_playersCodec); }
        public Group Clone() { var g = new Group(); g.Players.Add(Players); return g; }
        public bool Equals(Group other) => other != null && Players.Equals(other.Players);
    }

    public class TeamGroup : IMessage<TeamGroup>
    {
        public MessageDescriptor Descriptor => null;
        public Google.Protobuf.Collections.RepeatedField<Group> Groups { get; } = new Google.Protobuf.Collections.RepeatedField<Group>();

        public static MessageParser<TeamGroup> Parser = new MessageParser<TeamGroup>(() => new TeamGroup());
        private static readonly FieldCodec<Group> _groupsCodec = FieldCodec.ForMessage(10, Group.Parser);

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10) Groups.AddEntriesFrom(input, _groupsCodec);
                else input.SkipLastField();
            }
        }

        public void MergeFrom(TeamGroup other)
        {
            if (other == null) return;
            Groups.Add(other.Groups);
        }

        public void WriteTo(CodedOutputStream output)
        {
            Groups.WriteTo(output, _groupsCodec);
        }

        public int CalculateSize() { return Groups.CalculateSize(_groupsCodec); }
        public TeamGroup Clone() { var tg = new TeamGroup(); tg.Groups.Add(Groups); return tg; }
        public bool Equals(TeamGroup other) => other != null && Groups.Equals(other.Groups);
    }

    public class MatchGroup : IMessage<MatchGroup>
    {
        public MessageDescriptor Descriptor => null;
        public Google.Protobuf.Collections.RepeatedField<TeamGroup> Teams { get; } = new Google.Protobuf.Collections.RepeatedField<TeamGroup>();
        public string FilterCondition { get; set; } = "";

        public static MessageParser<MatchGroup> Parser = new MessageParser<MatchGroup>(() => new MatchGroup());
        private static readonly FieldCodec<TeamGroup> _teamsCodec = FieldCodec.ForMessage(10, TeamGroup.Parser);

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10) Teams.AddEntriesFrom(input, _teamsCodec);
                else if (tag == 18) FilterCondition = input.ReadString();
                else input.SkipLastField();
            }
        }

        public void MergeFrom(MatchGroup other)
        {
            if (other == null) return;
            Teams.Add(other.Teams);
            if (other.FilterCondition != "") FilterCondition = other.FilterCondition;
        }

        public void WriteTo(CodedOutputStream output)
        {
            Teams.WriteTo(output, _teamsCodec);
            if (FilterCondition != "") { output.WriteRawTag(18); output.WriteString(FilterCondition); }
        }

        public int CalculateSize()
        {
            int size = Teams.CalculateSize(_teamsCodec);
            if (FilterCondition != "") size += 1 + CodedOutputStream.ComputeStringSize(FilterCondition);
            return size;
        }
        public MatchGroup Clone() { var mg = new MatchGroup { FilterCondition = FilterCondition }; mg.Teams.Add(Teams); return mg; }
        public bool Equals(MatchGroup other) => other != null && Teams.Equals(other.Teams) && FilterCondition == other.FilterCondition;
    }

    /// <summary>
    /// Поле 1 в onMatchmakingProgress у клиента 0.17 — это состояние поиска, а не taskId.
    /// Клиент рисует по нему UI: Matchmaking — «Поиск», Confirmation — попап
    /// «ИГРА НАЙДЕНА / ПОДТВЕРДИТЬ» с таймером, NotConfirmed — «кто-то не подтвердил».
    /// </summary>
    public enum MatchmakingProgressState
    {
        None = 0,
        GroupWait = 1,
        Matchmaking = 2,
        Confirmation = 3,
        NotConfirmed = 4
    }

    public class OnMatchmakingProgressEvent : IMessage<OnMatchmakingProgressEvent>
    {
        public MessageDescriptor Descriptor => null;
        public MatchmakingProgressState State { get; set; }
        public MatchGroup MatchGroup { get; set; }

        public static MessageParser<OnMatchmakingProgressEvent> Parser = new MessageParser<OnMatchmakingProgressEvent>(() => new OnMatchmakingProgressEvent());

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 8) State = (MatchmakingProgressState)input.ReadInt32();
                else if (tag == 18) { if (MatchGroup == null) MatchGroup = new MatchGroup(); input.ReadMessage(MatchGroup); }
                else input.SkipLastField();
            }
        }

        public void MergeFrom(OnMatchmakingProgressEvent other)
        {
            if (other == null) return;
            if (other.State != MatchmakingProgressState.None) State = other.State;
            if (other.MatchGroup != null) { if (MatchGroup == null) MatchGroup = new MatchGroup(); MatchGroup.MergeFrom(other.MatchGroup); }
        }

        public void WriteTo(CodedOutputStream output)
        {
            if (State != MatchmakingProgressState.None) { output.WriteRawTag(8); output.WriteInt32((int)State); }
            if (MatchGroup != null) { output.WriteRawTag(18); output.WriteMessage(MatchGroup); }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (State != MatchmakingProgressState.None) size += 1 + CodedOutputStream.ComputeInt32Size((int)State);
            if (MatchGroup != null) size += 1 + CodedOutputStream.ComputeMessageSize(MatchGroup);
            return size;
        }
        public OnMatchmakingProgressEvent Clone() => new OnMatchmakingProgressEvent { State = State, MatchGroup = MatchGroup?.Clone() };
        public bool Equals(OnMatchmakingProgressEvent other) => other != null && other.State == State && Equals(other.MatchGroup, MatchGroup);
    }

    // Формат ТОЧНО по клиентскому дампу: onMatchmakingDone -> FAJOCNGHINA =
    // { string Id = 1; HIFCJENMONP MatchGroup = 2 }, где HIFCJENMONP (наш MatchGroup) =
    // { repeated TeamGroup = 1; string FilterCondition = 2 }.
    // ВНИМАНИЕ: не путать с "плоским" форматом (repeated Teams в поле 1) - это payload
    // ДРУГОГО события (onPlayersConfirmed). Плоский формат в done ломает парсинг:
    // клиент парсит наш Id-строку как сообщение, получает мусор и висит на подтверждении.
    // Done завершает Task start() и открывает «ИГРА НАЙДЕНА». onGameStarted — только
    // после ConfirmMatch, иначе бой стартует без кнопки «ПОДТВЕРДИТЬ».
    public class OnMatchmakingDoneEvent : IMessage<OnMatchmakingDoneEvent>
    {
        public MessageDescriptor Descriptor => null;
        public string Id { get; set; } = "";
        public MatchGroup MatchGroup { get; set; }

        public static MessageParser<OnMatchmakingDoneEvent> Parser = new MessageParser<OnMatchmakingDoneEvent>(() => new OnMatchmakingDoneEvent());

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10) Id = input.ReadString();
                else if (tag == 18) { if (MatchGroup == null) MatchGroup = new MatchGroup(); input.ReadMessage(MatchGroup); }
                else input.SkipLastField();
            }
        }

        public void MergeFrom(OnMatchmakingDoneEvent other)
        {
            if (other == null) return;
            if (other.Id != "") Id = other.Id;
            if (other.MatchGroup != null) { if (MatchGroup == null) MatchGroup = new MatchGroup(); MatchGroup.MergeFrom(other.MatchGroup); }
        }

        public void WriteTo(CodedOutputStream output)
        {
            if (Id != "") { output.WriteRawTag(10); output.WriteString(Id); }
            if (MatchGroup != null) { output.WriteRawTag(18); output.WriteMessage(MatchGroup); }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (Id != "") size += 1 + CodedOutputStream.ComputeStringSize(Id);
            if (MatchGroup != null) size += 1 + CodedOutputStream.ComputeMessageSize(MatchGroup);
            return size;
        }
        public OnMatchmakingDoneEvent Clone() => new OnMatchmakingDoneEvent { Id = Id, MatchGroup = MatchGroup?.Clone() };
        public bool Equals(OnMatchmakingDoneEvent other) => other != null && other.Id == Id && Equals(other.MatchGroup, MatchGroup);
    }

    public class OnPlayersConfirmedEvent : IMessage<OnPlayersConfirmedEvent>
    {
        public MessageDescriptor Descriptor => null;
        public MatchGroup MatchGroup { get; set; }
        public Google.Protobuf.Collections.RepeatedField<Player> NewConfirmedPlayers { get; } = new Google.Protobuf.Collections.RepeatedField<Player>();

        public static MessageParser<OnPlayersConfirmedEvent> Parser = new MessageParser<OnPlayersConfirmedEvent>(() => new OnPlayersConfirmedEvent());
        private static readonly FieldCodec<Player> _playersCodec = FieldCodec.ForMessage(18, Player.Parser);

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag == 10) { if (MatchGroup == null) MatchGroup = new MatchGroup(); input.ReadMessage(MatchGroup); }
                else if (tag == 18) NewConfirmedPlayers.AddEntriesFrom(input, _playersCodec);
                else input.SkipLastField();
            }
        }
        
        public void MergeFrom(OnPlayersConfirmedEvent other)
        {
            if (other == null) return;
            if (other.MatchGroup != null) { if (MatchGroup == null) MatchGroup = new MatchGroup(); MatchGroup.MergeFrom(other.MatchGroup); }
            NewConfirmedPlayers.Add(other.NewConfirmedPlayers);
        }

        public void WriteTo(CodedOutputStream output)
        {
            if (MatchGroup != null) { output.WriteRawTag(10); output.WriteMessage(MatchGroup); }
            NewConfirmedPlayers.WriteTo(output, _playersCodec);
        }

        public int CalculateSize()
        {
            int size = 0;
            if (MatchGroup != null) size += 1 + CodedOutputStream.ComputeMessageSize(MatchGroup);
            size += NewConfirmedPlayers.CalculateSize(_playersCodec);
            return size;
        }
        public OnPlayersConfirmedEvent Clone() { var e = new OnPlayersConfirmedEvent { MatchGroup = MatchGroup?.Clone() }; e.NewConfirmedPlayers.Add(NewConfirmedPlayers); return e; }
        public bool Equals(OnPlayersConfirmedEvent other) => other != null && Equals(other.MatchGroup, MatchGroup) && NewConfirmedPlayers.Equals(other.NewConfirmedPlayers);
    }

    public enum MatchmakingFailCause
    {
        Unknown = 0,
        GroupWaitTimeout = 1,
        GroupMemberDisconnected = 2,
        GroupMemberNotConfirmed = 3,
        GroupMemberIsNotOneClan = 4
    }

    public class OnMatchmakingFailEvent : IMessage<OnMatchmakingFailEvent>
    {
        public MessageDescriptor Descriptor => null;
        public MatchmakingFailCause Cause { get; set; }
        public string Message { get; set; } = "";
        public Group Group { get; set; }

        public static MessageParser<OnMatchmakingFailEvent> Parser = new MessageParser<OnMatchmakingFailEvent>(() => new OnMatchmakingFailEvent());

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 8: Cause = (MatchmakingFailCause)input.ReadInt32(); break;
                    case 18: Message = input.ReadString(); break;
                    case 26: if (Group == null) Group = new Group(); input.ReadMessage(Group); break;
                    default: input.SkipLastField(); break;
                }
            }
        }

        public void MergeFrom(OnMatchmakingFailEvent other)
        {
            if (other == null) return;
            Cause = other.Cause;
            if (other.Message != "") Message = other.Message;
            if (other.Group != null) { if (Group == null) Group = new Group(); Group.MergeFrom(other.Group); }
        }

        public void WriteTo(CodedOutputStream output)
        {
            if (Cause != MatchmakingFailCause.Unknown) { output.WriteRawTag(8); output.WriteInt32((int)Cause); }
            if (Message != "") { output.WriteRawTag(18); output.WriteString(Message); }
            if (Group != null) { output.WriteRawTag(26); output.WriteMessage(Group); }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (Cause != MatchmakingFailCause.Unknown) size += 1 + CodedOutputStream.ComputeInt32Size((int)Cause);
            if (Message != "") size += 1 + CodedOutputStream.ComputeStringSize(Message);
            if (Group != null) size += 1 + CodedOutputStream.ComputeMessageSize(Group);
            return size;
        }
        public OnMatchmakingFailEvent Clone() => new OnMatchmakingFailEvent { Cause = Cause, Message = Message, Group = Group?.Clone() };
        public bool Equals(OnMatchmakingFailEvent other) => other != null && other.Cause == Cause && other.Message == Message && Equals(other.Group, Group);
    }
}
