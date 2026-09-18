using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using pb = Google.Protobuf;
using pbc = Google.Protobuf.Collections;

namespace Axlebolt.Bolt.Protobuf
{
    public sealed class GetGameSettingsResponse : IMessage<GetGameSettingsResponse>
    {
        private static readonly MessageParser<GetGameSettingsResponse> _parser = 
            new MessageParser<GetGameSettingsResponse>(() => new GetGameSettingsResponse());
        
        public static MessageParser<GetGameSettingsResponse> Parser => _parser;
        
        private readonly RepeatedField<GameSetting> gameSettings_ = new RepeatedField<GameSetting>();
        
        public RepeatedField<GameSetting> GameSettings => gameSettings_;
        
        public GetGameSettingsResponse() { }
        
        public GetGameSettingsResponse(GetGameSettingsResponse other)
        {
            gameSettings_ = other.gameSettings_.Clone();
        }
        
        public GetGameSettingsResponse Clone() => new GetGameSettingsResponse(this);
        
        public override bool Equals(object other) => Equals(other as GetGameSettingsResponse);
        
        public bool Equals(GetGameSettingsResponse other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (!gameSettings_.Equals(other.gameSettings_)) return false;
            return true;
        }
        
        public override int GetHashCode()
        {
            int hash = 1;
            hash ^= gameSettings_.GetHashCode();
            return hash;
        }
        
        public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);
        
        public void WriteTo(CodedOutputStream output)
        {
            gameSettings_.WriteTo(output, _repeated_gameSettings_codec);
        }
        
        public int CalculateSize()
        {
            int size = 0;
            size += gameSettings_.CalculateSize(_repeated_gameSettings_codec);
            return size;
        }
        
        public void MergeFrom(GetGameSettingsResponse other)
        {
            if (other == null) return;
            gameSettings_.Add(other.gameSettings_);
        }
        
        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    default:
                        input.SkipLastField();
                        break;
                    case 10:
                        gameSettings_.AddEntriesFrom(input, _repeated_gameSettings_codec);
                        break;
                }
            }
        }
        
        private static readonly FieldCodec<GameSetting> _repeated_gameSettings_codec =
            FieldCodec.ForMessage(10, GameSetting.Parser);
        
        public MessageDescriptor Descriptor => null;
    }
}
