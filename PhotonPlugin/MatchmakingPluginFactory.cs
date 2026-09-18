using System;
using System.Collections.Generic;
using Photon.Hive.Plugin;

namespace Axlebolt.Standoff.PhotonPlugin
{
    public class MatchmakingPluginFactory : IPluginFactory
    {
        public IGamePlugin Create(IPluginHost gameHost, string pluginName, Dictionary<string, string> config, out string errorMsg)
        {
            errorMsg = "";
            try
            {
                // Use the client-requested plugin name (UnevenTeamsGamePlugin / SantaClausGamePlugin / …)
                // so Photon does not report plugin mismatch and SoulHunt/Santa detection works.
                string name = string.IsNullOrWhiteSpace(pluginName) ? "MatchmakingPlugin" : pluginName;
                gameHost.LogInfo($"MatchmakingPluginFactory: Creating plugin '{name}'");
                var plugin = new MatchmakingPlugin(name);

                if (plugin.SetupInstance(gameHost, config, out errorMsg))
                {
                    gameHost.LogInfo("MatchmakingPluginFactory: Plugin created and initialized successfully.");
                    return plugin;
                }

                gameHost.LogError($"MatchmakingPluginFactory: SetupInstance failed: {errorMsg}");
                return null;
            }
            catch (Exception ex)
            {
                errorMsg = ex.ToString();
                gameHost.LogError($"MatchmakingPluginFactory: Failed to create plugin: {errorMsg}");
                return null;
            }
        }
    }
}
