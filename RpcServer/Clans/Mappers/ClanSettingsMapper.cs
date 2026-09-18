using System;
using Axlebolt.Bolt.Protobuf;
using StandRiseServer.Bolt;

namespace StandRiseServer.RpcServer.Clans.Mappers
{
    public class ClanSettingsMapper : MessageMapper<ClanSettings, object>
    {
        public static readonly ClanSettingsMapper Instance = new ClanSettingsMapper();

        public override object ToOriginal(ClanSettings proto)
        {
            return null;
        }

        public override ClanSettings ToProto(object original)
        {
            var settings = new ClanSettings
            {
                InitialMembersCount = 10,
                MembersCountLimit = 50
            };
            
            settings.ClanCreateCost.CurrencyId = 102;
            settings.ClanCreateCost.Value = 5000f;
            
            settings.ChangeClanNameOrTagCost.CurrencyId = 102;
            settings.ChangeClanNameOrTagCost.Value = 1000f;
            
            settings.MembercCountUpgradeCost.CurrencyId = 102;
            settings.MembercCountUpgradeCost.Value = 500f;
            
            return settings;
        }
    }
}
