using System;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.Bolt;

namespace StandRiseServer.RpcServer.Clans.Mappers
{
    public class ClanMemberMapper : MessageMapper<Axlebolt.Bolt.Protobuf.ClanMember, StandRiseServer.MongoDB.Main.ClanMember>
    {
        public static readonly ClanMemberMapper Instance = new ClanMemberMapper();

        public override StandRiseServer.MongoDB.Main.ClanMember ToOriginal(Axlebolt.Bolt.Protobuf.ClanMember proto)
        {
            if (proto == null) throw new ArgumentNullException(nameof(proto));
            return new StandRiseServer.MongoDB.Main.ClanMember
            {
                playerId = proto.PlayerFriend.Player.Id,
                role = proto.RoleId.ToString(),
                clanId = proto.ClanId,
                joinDate = global::MongoDB.Bson.BsonDateTime.Create(Utils.FromUnixTime(proto.CreateDate))
            };
        }

        public override Axlebolt.Bolt.Protobuf.ClanMember ToProto(StandRiseServer.MongoDB.Main.ClanMember original)
        {
            if (original == null) return null;
            return original.GetClanMember();
        }
    }
}
