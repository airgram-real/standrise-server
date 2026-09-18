using System;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.Bolt;

namespace StandRiseServer.RpcServer.Clans.Mappers
{
    public class ClanRequestTypeMapper : MessageMapper<RequestType, int>
    {
        public static readonly ClanRequestTypeMapper Instance = new ClanRequestTypeMapper();

        public override int ToOriginal(RequestType proto)
        {
            return (int)proto;
        }

        public override RequestType ToProto(int original)
        {
            return (RequestType)original;
        }
    }
}
