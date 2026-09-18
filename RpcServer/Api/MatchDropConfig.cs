using System;
using System.Linq;
using MongoDB.Bson;
using StandRiseServer.MongoDB.Game;

namespace StandRiseServer.RpcServer.Api
{
    /// <summary>Дроп за матч / удачу: веса редкости и медали в пуле luck.</summary>
    public static class MatchDropConfig
    {
        public static void ApplyMatchDropWeights(RandomGenerator generator)
        {
            // uncommon 80%, rare 10%, legendary 4%, arcane 1%, common 5% (остаток)
            generator.AddDropChance(SkinValue.Common, 8);
            generator.AddDropChance(SkinValue.Uncommon, 82);
            generator.AddDropChance(SkinValue.Rare, 8);
            generator.AddDropChance(SkinValue.Epic, 1);
            generator.AddDropChance(SkinValue.Legendary, 1);
            generator.AddDropChance(SkinValue.Arcane, 0);
        }

        public static bool IsAssistanceMedal(BoltInventoryItemDefinitionDocument def)
        {
            if (def == null) return false;
            int key = def.key;
            if (key >= 100 && key < 200) return true;
            try { return def.GetCollectionId() == CollectionId.Assistance; } catch { return false; }
        }

        /// <summary>Медали не выдаются из post-match / luck drop.</summary>
        public static bool IsExcludedFromMatchDrop(BoltInventoryItemDefinitionDocument def)
        {
            if (def == null) return true;
            if (IsAssistanceMedal(def)) return true;
            int key = def.key;
            if (key > 0 && key < 300) return true;
            return false;
        }

        public static bool IsLuckDropCandidate(BoltInventoryItemDefinitionDocument def, bool luckWeaponRecipe)
        {
            if (def == null || def.properties == null) return false;
            if (IsAssistanceMedal(def)) return false;

            if (!luckWeaponRecipe) return false;
            if (!def.properties.Contains("match_drop")) return false;
            var md = def.properties["match_drop"];
            bool flag = false;
            try
            {
                if (md.IsBoolean) flag = md.AsBoolean;
                else if (md.IsInt32 || md.IsInt64) flag = md.ToInt32() != 0;
                else if (md.IsString) flag = md.AsString == "1" || md.AsString.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
            if (!flag) return false;
            int key = def.key;
            if (key < 1000) return false;
            if (key >= 1200 && key < 2000) return false;
            if (def.properties.Contains("stickerMount")) return false;
            if (def.properties.Contains("maxStackSize")) return false;
            if (def.properties.Contains("contains")) return false;
            var rarity = def.GetSkinValue();
            if (rarity == SkinValue.None) return false;
            return def.GetCollectionId() != CollectionId.None;
        }
    }
}
