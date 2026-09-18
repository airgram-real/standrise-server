using System.Collections.Generic;
using StandRiseServer.MongoDB.Game;
using MongoDB.Bson;

namespace StandRiseServer.RpcServer.Api
{
    public class SkinComparer : IComparer<BoltInventoryItemDefinitionDocument>
    {
        public int Compare(BoltInventoryItemDefinitionDocument x, BoltInventoryItemDefinitionDocument y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            return x.GetSkinValue().CompareTo(y.GetSkinValue());
        }
    }

    public enum SkinValue
    {
        None = 0,
        Common = 1,
        Uncommon = 2,
        Rare = 3,
        Epic = 4,
        Legendary = 5,
        Arcane = 6
    }

    public enum CollectionId
    {
        None = 0,
        Assistance = 1,
        Origin = 2,
        Veteran2018 = 3,
        Furious = 4,
        Nameless = 5,
        Rival = 6,
        Veteran2019 = 7,
        Event2Years = 8,
        Fable = 9,
        Competitive = 10,
        Halloween_2019 = 11,
        NewYear2020 = 12,
        Veteran2020 = 13,
        Scorpion = 14,
        Project_Z9 = 15,
        Rainbow = 16,
        Revival = 17,
        Halloween_2020 = 18,
        New_Year_2021 = 19,
        Veteran2021 = 20,
        Travelers_Bag = 21,
        Event4Years = 22,
        Empire = 23,
        Dragon_Rise = 24,
        hot_winter_party_2023 = 25,
        Winter_Fun_2022 = 26,
        Veteran2022 = 27,
        Event5Years = 28,
        Sharp = 29,
        Legends = 30,
        Project_Pandora = 31,
        Splash = 32,
        Hot_Winter_Party_2023 = 33,
        Standoff_2 = 34,
        Veteran_2023 = 35,
        Fireborn = 36,
        Revenge = 37,
        Space_Vision = 38,
        Sunstrike = 39,
        SubjectX = 40,
        Veteran_2024 = 41,
        Frosty_Chaos = 42,
        Outcast = 43,
        Fun_And_Sun = 44,
        Reforged = 45,
        Chameleon = 46,
        Breakout = 47,
        Collaboration = 48,
        Dia_De_Muertos = 49,
        Dynasty = 50,
        Esports = 51,
        Event8Years = 52,
        Flow = 53,
        Gambit = 54,
        Joy = 55,
        Kitsune_Dreams = 56,
        Nightmare = 57,
        Prey = 58,
        Rewind = 59,
        Shine = 60,
        Syndicate = 61,
        Valor = 62,
        Veteran_2025 = 63,
        Veteran_2026 = 64,
        Vibe = 65,
        Winter_Tale = 66,
        Year_Of_The_Horse = 67
    }

    public enum InventoryId
    {
        None = 0,
        MedalAssistanceBronze = 100,
        MedalAssistanceSilver = 101,
        MedalAssistanceGold = 102,
        MedalAssistancePlatinum = 103,
        MedalAssistanceBrilliant = 104,
        MedalVeteran2018Bronze = 105,
        MedalVeteran2018Silver = 106,
        MedalVeteran2018Gold = 107,
        MedalVeteran2018Platinum = 108,
        MedalVeteran2019Bronze = 109,
        MedalVeteran2019Silver = 110,
        MedalVeteran2019Gold = 111,
        MedalVeteran2019Platinum = 112,
        Medal2YearsSilver = 113,
        Medal2YearsGold = 114,
        MedalCompetitiveBronze = 115,
        MedalCompetitiveSilver = 116,
        MedalCompetitiveGold = 117,
        MedalCompetitivePlatinum = 118,
        MedalCompetitiveBrilliant = 119,
        MedalNewYearMadness2020Bronze = 120,
        MedalNewYearMadness2020Silver = 121,
        MedalNewYearMadness2020Gold = 122,
        MedalNewYearMadness2020Platinum = 123,
        MedalNewYearMadness2020Brilliant = 124,
        OriginCase = 301,
        FuriousCase = 302,
        RivalCase = 303,
        FableCase = 304,
        ScorpionCase = 305,
        OriginBox = 401,
        FuriousBox = 402,
        RivalBox = 403,
        FableBox = 404,
        ScorpionBox = 405,
        EmpireCase = 306,
        SharpCase = 307,
        EmpireBox = 406,
        SharpBox = 407,
        GiftNewYear2019 = 501,
        TwoYearsEventGoldPass = 601,
        NewYearMadness2020GoldPass = 602,
        Halloween2019StickersPack = 701,
        RainbowStickersPack = 702,
        Halloween2020CharmPack = 901,
        GoldSkull = 1101,
        Punisher = 1102,
        MadBat = 1103,
        InfernalSkull = 1104,
        Ghoul = 1105,
        Batrider = 1106,
        GangstaPumpkin = 1107,
        Snot = 1108,
        Devilish = 1109,
        HurryGhost = 1110,
        Feed = 1111,
        Anticamper = 1112,
        S1001 = 1113,
        BloodyClown = 1114,
        Ghosty = 1115,
        Mummy = 1116,
        Rush = 1117,
        EvilPumpkin = 1118,
        Zombie = 1119,
        Dracula = 1120,
        G22PixelCamouflage = 11001,
        G22Nest = 11002,
        G22Pattern = 11005,
        G22Inferno = 11006,
        G22FrostWyrm = 11008,
        G22NestStatTrack = 1011002,
        G22FrostWyrmStatTrack = 1011008,
        USP_2Years = 12002,
        USP_2YearsRed = 12003,
        P350Cyber = 13001,
        P350Savannah = 13002,
        P350ForestSpirit = 13003,
        P350Rally = 13004,
        P350Skull = 13005,
        P350CyberStatTrack = 1013001,
        P350ForestSpiritStatTrack = 1013003,
        P350RallyStatTrack = 1013004,
        UMP45Cyberpunk = 32001,
        UMP45Pixel = 32002,
        UMP45Shark = 32003,
        UMP45Winged = 32004,
        UMP45Beast = 32005,
        UMP45Iron = 32006,
        UMP45CyberpunkStatTrack = 1032001,
        UMP45SharkStatTrack = 1032003,
        UMP45WingedStatTrack = 1032004,
        UMP45BeastStatTrack = 1032005,
        MP7Offroad = 34001,
        MP7Arcade = 34002,
        MP7_2Years = 34003,
        MP7_2YearsRed = 34004,
        MP7OffroadStatTrack = 1034001,
        MP7ArcadeStatTrack = 1034002,
        P90Radiation = 35001,
        P90Ghoul = 35002,
        P90Fury = 35003,
        P90Pilot = 35004,
        P90GhoulStatTrack = 1035002,
        DeagleCaptainMorgan = 15001,
        DeagleBlood = 15002,
        DeaglePredator = 15003,
        DeagleRedDragon = 15004,
        DeagleWinner = 15005,
        DeagleDragonGlass = 15006,
        DeagleThunder = 15007,
        DeaglePredatorStatTrack = 1015003,
        DeagleRedDragonStatTrack = 1015004,
        DeagleDragonGlassStatTrack = 1015006,
        AKRTreasureHunter = 44002,
        AKRTiger = 44003,
        AKRSport = 44004,
        AKRNecromancer = 44005,
        AKRCarbon = 44006,
        AKR_2Years = 44007,
        AKRTreasureHunterStatTrack = 1044002,
        AKRSportStatTrack = 1044004,
        AKRCarbonStatTrack = 1044006,
        AKRNecromancerStatTrack = 1044005,
        AKR12Railgun = 45001,
        AKR12PixelCamouflage = 45002,
        AKR12Mechanic = 45003,
        AKR12Aurora = 45004,
        AKR12RailgunStatTrack = 1045001,
        AKR12PixelCamouflageStatTrack = 1045002,
        M4Predator = 46001,
        M4Necromancer = 46002,
        M4Tiger = 46003,
        M4Pro = 46006,
        M4GrandPrix = 46007,
        M4NecromancerStatTrack = 1046002,
        M4ProStatTrack = 1046006,
        M4GrandPrixStatTrack = 1046007,
        M16Camouflage = 47001,
        M16Winged = 47002,
        M16Facet = 47003,
        M16WingedStatTrack = 1047002,
        FamasBeagle = 48001,
        FamasFury = 48002,
        FamasHull = 48003,
        FamasBeagleStatTrack = 1048001,
        FamasFuryStatTrack = 1048002,
        FamasHullStatTrack = 1048003,
        AWMSport = 51001,
        AWMPhoenix = 51002,
        AWMGear = 51003,
        AWMScratch = 51004,
        AWMGenesis = 51007,
        AWM_2YearsRed = 51008,
        AWMPhoenixStatTrack = 1051002,
        AWMGearStatTrack = 1051003,
        AWMScratchStatTrack = 1051004,
        AWMGenesisStatTrack = 1051007,
        M40Quake = 52001,
        M40Pro = 52002,
        M40Beagle = 52003,
        M40QuakeStatTrack = 1052001,
        M40BeagleStatTrack = 1052003,
        SM1014Facet = 62001,
        SM1014Pathfinder = 62002,
        SM1014Necromancer = 62003,
        SM1014NorthernCamouflage = 62004,
        SM1014Quake = 62005,
        SM1014Branches = 62006,
        SM1014PathfinderStatTrack = 1062002,
        SM1014NecromancerStatTrack = 1062003,
        M9BayonetBlueBlood = 71001,
        M9BayonetAncient = 71002,
        M9BayonetScratch = 71003,
        M9BayonetUniverse = 71004,
        M9ByonetDragonGlass = 71005,
        KarambitClaw = 72002,
        KarambitIceDragon = 72004,
        KarambitScratch = 72006,
        KarambitUniverse = 72007,
        jKommandoAncient = 73002,
        jKommandoReaper = 73003,
        jKommandoFloral = 73004,
        jKommandoLuxury = 73006,
        FnFAL_Leather = 44901,
        G22_Relic = 41101,
        KarambitGold = 72003,
        AWMSportV2 = 51006,
        USPGenesis = 12001,
        AKR_Worm = 44401,
        Butterfly_Legacy = 47502,
        Butterfly_DragonGlass = 47503,
        Butterfly_BlackWidow = 47504,
        Butterfly_Starfall = 47505,
        Deagle_Ace = 41502,
        Deagle_Ace_StatTrack = 1041502,
        FiveSeven_Venom = 41701,
        FiveSeven_Venom_StatTrack = 1041701,
        FiveSeven_Tactical = 41703,
        FiveSeven_Tactical_StatTrack = 1041703,
        FnFAL_AcidCarbon = 44902,
        FnFAL_Tactical = 44903,
        FnFAL_Tactical_StatTrack = 1044903,
        G22_Starfall = 41102,
        G22_Starfall_StatTrack = 1041102,
        M4_Lizard = 44601,
        M4_Lizard_StatTrack = 1044601,
        M4_Samurai = 44603,
        M4_Samurai_StatTrack = 1044603,
        M110_Cyber = 45301,
        M110_Cyber_StatTrack = 1045301,
        SM1014_Blaster = 45302,
        MP7_Thorn = 43401,
        MP7_Lich = 43402,
        MP7_Lich_StatTrack = 1043402,
        P90_Jungle = 43502,
        Tec9_Aurora = 41601,
        Tec9_Fable = 41605,
        Tec9_Fable_StatTrack = 1041605,
        UMP45_PixelV2 = 43201,
        UMP45_Cerberus = 43202,
        UMP45_Cerberus_StatTrack = 1043202,
        USP_Fiend = 41201,
        USP_Pisces = 41212,
        USP_Pisces_StatTrack = 1041212,
        FiveSeven_New2 = 51701,
        Tec9_New3 = 51601,
        AKR_New2 = 54401,
        M4_New2 = 54601,
        Butterfly_Commpetive = 57501,
        FlipKnife_New1 = 67701,
        FlipKnife_New2 = 67702,
        FlipKnife_New3 = 67703,
        FlipKnife_New4 = 67704,
        FlipKnife_New5 = 67705,
        AMW_New1 = 65101,
        AMW_New1_StatTrack = 1065101,
        G22_New1 = 61101,
        G22_New1_StatTrack = 1061101,
        M40_New1 = 65201,
        M40_New1_StatTrack = 1065201,
        M40_New2 = 65202,
        M40_New2_StatTrack = 1065202,
        MP7_New1 = 63401,
        MP7_New1_StatTrack = 1063401,
        SM1014_New1 = 66201,
        SM1014_New1_StatTrack = 1066201,
        Tec9_New1 = 61601,
        Tec9_New1_StatTrack = 1061601,
        USP_New1 = 61201,
        USP_New1_StatTrack = 1061201,
        Sticker_New1 = 1121,
        Sticker_New2 = 1122,
        Sticker_New3 = 1123,
        Sticker_New4 = 1124,
        Sticker_New5 = 1125,
        Sticker_New6 = 1126,
        Sticker_New7 = 1127,
        Sticker_New8 = 1128,
        Sticker_New9 = 1129,
        Sticker_New10 = 1130,
        Sticker_New11 = 1131,
        Sticker_New12 = 1132
    }

    public static class InventoryExtensions
    {
        public static SkinValue GetSkinValue(this BoltInventoryItemDefinitionDocument definition)
        {
            if (definition.properties == null) return SkinValue.None;

            // Пробуем поле "value" — число или строка-число
            if (definition.properties.TryGetValue("value", out var result))
            {
                try
                {
                    if (result.IsInt32 || result.IsInt64)
                    {
                        int intVal = result.ToInt32();
                        if (System.Enum.IsDefined(typeof(SkinValue), intVal))
                            return (SkinValue)intVal;
                    }
                    else if (result.IsString)
                    {
                        // Строка-число: "2"
                        if (int.TryParse(result.AsString, out var parsed) && System.Enum.IsDefined(typeof(SkinValue), parsed))
                            return (SkinValue)parsed;
                        // Строка-имя: "uncommon", "Rare" и т.д.
                        if (System.Enum.TryParse<SkinValue>(result.AsString, true, out var named))
                            return named;
                    }
                }
                catch { }
            }

            // Пробуем поле "rarity" как строку
            if (definition.properties.TryGetValue("rarity", out var rarity))
            {
                try
                {
                    if (rarity.IsString && System.Enum.TryParse<SkinValue>(rarity.AsString, true, out var named))
                        return named;
                    if ((rarity.IsInt32 || rarity.IsInt64) && System.Enum.IsDefined(typeof(SkinValue), rarity.ToInt32()))
                        return (SkinValue)rarity.ToInt32();
                }
                catch { }
            }

            return SkinValue.None;
        }

        public static CollectionId GetCollectionId(this BoltInventoryItemDefinitionDocument definition)
        {
            if (definition.properties != null && definition.properties.TryGetValue("collection", out var value))
            {
                try
                {
                    if (value.IsInt32 || value.IsInt64)
                    {
                        int rawId = value.ToInt32();
                        return System.Enum.IsDefined(typeof(CollectionId), rawId) ? (CollectionId)rawId : CollectionId.None;
                    }

                    string rawName = value.IsString ? value.AsString.Trim() : value.ToString().Trim();
                    if (int.TryParse(rawName, out int parsedId) && System.Enum.IsDefined(typeof(CollectionId), parsedId))
                    {
                        return (CollectionId)parsedId;
                    }

                    if (TryGetCollectionIdAlias(rawName, out CollectionId collectionId))
                    {
                        return collectionId;
                    }

                    if (System.Enum.TryParse(rawName, true, out collectionId))
                    {
                        return collectionId;
                    }

                    string normalizedName = rawName.Replace(" ", "_").Replace("-", "_");
                    if (TryGetCollectionIdAlias(normalizedName, out collectionId))
                    {
                        return collectionId;
                    }

                    if (System.Enum.TryParse(normalizedName, true, out collectionId))
                    {
                        return collectionId;
                    }
                }
                catch
                {
                    return CollectionId.None;
                }
            }
            return CollectionId.None;
        }

        private static bool TryGetCollectionIdAlias(string rawName, out CollectionId collectionId)
        {
            collectionId = CollectionId.None;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return false;
            }

            string normalized = rawName.Trim().Replace(" ", "_").Replace("-", "_").ToLowerInvariant();
            switch (normalized)
            {
                case "event5years":
                case "event_5_years":
                    collectionId = CollectionId.Event5Years;
                    return true;
                case "sharp":
                    collectionId = CollectionId.Sharp;
                    return true;
                case "legends":
                    collectionId = CollectionId.Legends;
                    return true;
                case "project_pandora":
                case "projectpandora":
                    collectionId = CollectionId.Project_Pandora;
                    return true;
                case "splash":
                    collectionId = CollectionId.Splash;
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsStattrack(this BoltInventoryItemDefinitionDocument definition)
        {
            if (definition.properties != null && definition.properties.TryGetValue("stattrack", out var value))
            {
                try
                {
                    return value.AsBoolean;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }
    }
}
