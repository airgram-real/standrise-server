using System;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.Bolt;

namespace StandRiseServer.RpcServer.Clans.Mappers
{
    public class ClanJoinRequestMapper : MessageMapper<ClanJoinRequest, ClanRequestDocument>
    {
        public static readonly ClanJoinRequestMapper Instance = new ClanJoinRequestMapper();

        public override ClanRequestDocument ToOriginal(ClanJoinRequest proto)
        {
            if (proto == null) throw new ArgumentNullException(nameof(proto));
            return new ClanRequestDocument
            {
                senderId = proto.RequestSender.Id,
                clanId = proto.Clan.Id,
                createDate = global::MongoDB.Bson.BsonDateTime.Create(Utils.FromUnixTime(proto.CreateDate)),
                requestType = (int)proto.RequestType
            };
        }

        public override ClanJoinRequest ToProto(ClanRequestDocument original)
        {
            if (original == null) return null;
            return original.GetInviteRequest();
        }
    }
}
