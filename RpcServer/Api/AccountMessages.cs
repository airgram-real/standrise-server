using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using System;
using System.Collections.Generic;

namespace Axlebolt.Bolt.Protobuf
{
    public sealed class LinkedAuth : IMessage<LinkedAuth>
    {
        private static readonly MessageParser<LinkedAuth> _parser = new MessageParser<LinkedAuth>(() => new LinkedAuth());
        public static MessageParser<LinkedAuth> Parser => _parser;

        private AuthType authType_;
        private bool primary_;

        public AuthType AuthType
        {
            get => authType_;
            set => authType_ = value;
        }

        public bool Primary
        {
            get => primary_;
            set => primary_ = value;
        }

        public void WriteTo(CodedOutputStream output)
        {
            if (AuthType != AuthType.Test)
            {
                output.WriteRawTag(8);
                output.WriteEnum((int)AuthType);
            }
            if (Primary)
            {
                output.WriteRawTag(16);
                output.WriteBool(Primary);
            }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (AuthType != AuthType.Test) size += 1 + CodedOutputStream.ComputeEnumSize((int)AuthType);
            if (Primary) size += 1 + 1;
            return size;
        }

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 8:
                        AuthType = (AuthType)input.ReadEnum();
                        break;
                    case 16:
                        Primary = input.ReadBool();
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }
        }

        public void MergeFrom(LinkedAuth other)
        {
            if (other == null) return;
            if (other.AuthType != AuthType.Test) AuthType = other.AuthType;
            if (other.Primary) Primary = other.Primary;
        }

        public bool Equals(LinkedAuth other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return AuthType == other.AuthType && Primary == other.Primary;
        }

        public override bool Equals(object other) => Equals(other as LinkedAuth);
        public override int GetHashCode() => AuthType.GetHashCode() ^ Primary.GetHashCode();
        public LinkedAuth Clone() => new LinkedAuth { AuthType = AuthType, Primary = Primary };
        MessageDescriptor IMessage.Descriptor => null; 
    }

    public sealed class GetLinkedAuthRequest : IMessage<GetLinkedAuthRequest>
    {
        private static readonly MessageParser<GetLinkedAuthRequest> _parser = new MessageParser<GetLinkedAuthRequest>(() => new GetLinkedAuthRequest());
        public static MessageParser<GetLinkedAuthRequest> Parser => _parser;
        public void WriteTo(CodedOutputStream output) { }
        public int CalculateSize() => 0;
        public void MergeFrom(CodedInputStream input) { uint tag; while ((tag = input.ReadTag()) != 0) input.SkipLastField(); }
        public void MergeFrom(GetLinkedAuthRequest other) { }
        public bool Equals(GetLinkedAuthRequest other) => !ReferenceEquals(other, null);
        public override bool Equals(object other) => Equals(other as GetLinkedAuthRequest);
        public override int GetHashCode() => 0;
        public GetLinkedAuthRequest Clone() => new GetLinkedAuthRequest();
        MessageDescriptor IMessage.Descriptor => null;
    }

    public sealed class GetLinkedAuthResponse : IMessage<GetLinkedAuthResponse>
    {
        private static readonly MessageParser<GetLinkedAuthResponse> _parser = new MessageParser<GetLinkedAuthResponse>(() => new GetLinkedAuthResponse());
        public static MessageParser<GetLinkedAuthResponse> Parser => _parser;

        private readonly RepeatedField<LinkedAuth> authTypes_ = new RepeatedField<LinkedAuth>();
        public RepeatedField<LinkedAuth> AuthTypes => authTypes_;

        public void WriteTo(CodedOutputStream output)
        {
            authTypes_.WriteTo(output, _repeated_authTypes_codec);
        }

        private static readonly FieldCodec<LinkedAuth> _repeated_authTypes_codec = FieldCodec.ForMessage(10, LinkedAuth.Parser);

        public int CalculateSize()
        {
            return authTypes_.CalculateSize(_repeated_authTypes_codec);
        }

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 10:
                        authTypes_.AddEntriesFrom(input, _repeated_authTypes_codec);
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }
        }

        public void MergeFrom(GetLinkedAuthResponse other)
        {
            if (other == null) return;
            authTypes_.Add(other.authTypes_);
        }

        public bool Equals(GetLinkedAuthResponse other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return authTypes_.Equals(other.authTypes_);
        }

        public override bool Equals(object other) => Equals(other as GetLinkedAuthResponse);
        public override int GetHashCode() => authTypes_.GetHashCode();
        public GetLinkedAuthResponse Clone()
        {
            var res = new GetLinkedAuthResponse();
            res.authTypes_.Add(authTypes_);
            return res;
        }
        MessageDescriptor IMessage.Descriptor => null;
    }
}
