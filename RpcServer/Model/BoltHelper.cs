using MongoDB.Bson;
using StandRiseServer.MongoDB;
using StandRiseServer.MongoDB.Main;

namespace StandRiseServer.RpcServer.Model
{
    public static class BoltHelper
    {
        public static BoltFriend GetBoltFriend(this PlayerDocument a)
        {
            return new BoltFriend
            {
                Id = a._id.ToString(),
                Uid = a.uid
            };
        }

        public static PlayerDocument GetPlayerDocument(this BoltFriend a)
        {
            return BoltMainDatabaseProvider.Instance.GetPlayerDocument(ObjectId.Parse(a.Id));
        }
    }
}
