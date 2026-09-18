using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using StandRiseServer.MongoDB;

namespace StandRiseServer.RpcServer.Api
{
    public static class RegionIpHelper
    {
        /// <summary>Один физический сервер — все регионы на PublicIp.</summary>
        public static string GetRegionIp(string region = null) => StaticClasses.PublicIp;

        public static string GetRegionIp() => GetRegionIp(null);
    }
}
