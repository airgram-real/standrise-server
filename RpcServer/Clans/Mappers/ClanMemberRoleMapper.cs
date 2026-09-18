using System;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.Bolt;

namespace StandRiseServer.RpcServer.Clans.Mappers
{
    public class ClanMemberRoleMapper : MessageMapper<ClanMemberRole, ClanRoleDocument>
    {
        public static readonly ClanMemberRoleMapper Instance = new ClanMemberRoleMapper();

        public override ClanRoleDocument ToOriginal(ClanMemberRole proto)
        {
            if (proto == null) throw new ArgumentNullException(nameof(proto));
            return new ClanRoleDocument
            {
                _id = proto.Id,
                name = proto.Name,
                level = proto.Level,
                description = proto.Descripption
            };
        }

        public override ClanMemberRole ToProto(ClanRoleDocument original)
        {
            if (original == null) return null;
            return original.GetRole();
        }
    }
}
