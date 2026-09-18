using System;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.Bolt;

namespace StandRiseServer.RpcServer.Clans.Mappers
{
    public class ClanMapper : MessageMapper<Clan, ClanDocument>
    {
        public static readonly ClanMapper Instance = new ClanMapper();

        public override ClanDocument ToOriginal(Clan proto)
        {
            if (proto == null) throw new ArgumentNullException(nameof(proto));
            return new ClanDocument
            {
                name = proto.Name,
                tag = proto.Tag,
                description = proto.Description,
                avatarId = proto.AvatarId,
                membersCount = proto.MebersCount,
                maxMembersCount = proto.MaxMemberCount,
                type = proto.ClanType
            };
        }

        public override Clan ToProto(ClanDocument original)
        {
            if (original == null) return null;
            return original.GetClan();
        }
    }
}
