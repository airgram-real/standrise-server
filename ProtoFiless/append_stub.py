import os
import re

file_path = r'C:\Users\Vadym\Desktop\tcp nulol prosto\ProtoFiless\StubMessages.cs'

with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# remove trailing }
idx = content.rfind('}')
if idx != -1:
    content = content[:idx]

classes = '''
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
}
'''

content += classes

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
