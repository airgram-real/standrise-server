using System;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.MongoDB.Main;
using StandRiseServer.Bolt;

namespace StandRiseServer.RpcServer.Clans.Mappers
{
    public class ClanInviteRequestMapper : MessageMapper<ClanInviteRequest, ClanInviteDocument>
    {
        public static readonly ClanInviteRequestMapper Instance = new ClanInviteRequestMapper();

        public override ClanInviteDocument ToOriginal(ClanInviteRequest proto)
        {
            if (proto == null) throw new ArgumentNullException(nameof(proto));
            return new ClanInviteDocument
            {
                senderId = proto.RequestSender.Id,
                invitedId = proto.InvitedPlayer.Id,
                clanId = proto.Clan.Id,
                createDate = global::MongoDB.Bson.BsonDateTime.Create(Utils.FromUnixTime(proto.CreateDate)),
                requestType = (int)proto.RequestType
            };
        }

        public override ClanInviteRequest ToProto(ClanInviteDocument original)
        {
            if (original == null) return null;
            return original.GetInviteRequest();
        }
    }
}
