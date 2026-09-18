using System;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.Bolt;

namespace StandRiseServer.RpcServer.Clans.Mappers
{
    public class ClanTypeMapper : MessageMapper<ClanType, int>
    {
        public static readonly ClanTypeMapper Instance = new ClanTypeMapper();

        public override int ToOriginal(ClanType proto)
        {
            return (int)proto;
        }

        public override ClanType ToProto(int original)
        {
            return (ClanType)original;
        }
    }
}
