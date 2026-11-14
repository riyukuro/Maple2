using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Maple2.Model.Validators;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Validators;

// The test tables turn every feature on except the ones a test names, so a feature name the client does not have
// passes every handler test while the ingested table never holds it. These tests check each name the server reads
// against the client's feature names.
public class FeatureNameTests {
    // The names CFeatureManager maps to a feature id (commonfiles/featuremanager.cpp, the SetAt calls, in order;
    // id 1's name is empty and left out).
    private static readonly HashSet<string> ClientNames = [
        "Develop", "KarkarIsland", "MassiveOxQuiz", "TimeAttackDungeon", "AricariDailyQ", "KatvanDungeon",
        "RuneBlader", "FieldEventSpawn", "MusicInstrument", "Fishing", "HabiToken", "Darkblossom", "DetectSkillHack",
        "Barkhant", "CompactControl", "PvPRankingDuelMode", "Striker", "MassiveDanceDanceStop", "ItemDropRenewal",
        "MesoMarket", "ArcadeCrazyMonkeyOcean", "LuaCorrectionPen", "ArcadeSpringFarm", "SuperWorldChat",
        "RewardPreview", "ArcadeSystem", "NutaNuta", "EventBossRush", "EventMapEnterUI", "EventDuckyCardGame",
        "ArcadePapulatus", "TorrentOfDark", "ShadowExpedition", "ShadowCataclysm", "EventSlimeCardGame", "SoulBinder",
        "ExtendLevel55", "ExtendLevel60", "ExtendLevel65", "ExtendLevel70", "ExtendLevel80", "ExtendLevel90",
        "ExtendLevel99", "CharacterAbility", "ItemSocketSystem", "GameDictionary", "JazzInstrument", "Kandura",
        "ChaosPapulatus", "EventMassiveDungeon", "ShadowExpedition2nd", "ShrineOfGuidance", "DoubleImpactStory",
        "itemOptionRenewal", "ItemRePacking", "UGCMapFun", "SkinBreak", "Maview", "PCBang", "MeratUI", "VisitorBook",
        "InGameRank", "RenewalDungeonQuest01", "ContentsGuide01", "EnableGoToNpc", "RedMerat", "RedMeratShop", "Gift",
        "GiftUgc", "GiftPremium", "UGCMarket", "PremiumShop", "VIP", "VIPDungeon", "ItemDungeon", "Mastery",
        "PvpTestSeason", "CashShop", "ResetServer", "MustBeModified", "PirateOcean", "SnowKingdom", "DarkCastle",
        "ItemPartyDungeon", "Club", "VIPAirTaxi", "NPCPortal01", "MasterySecond", "MagicScroll01", "GuildHouse01",
        "AdventureBar", "DungeonLimitMesoRevival", "SkinSocket", "WebGuide", "MusicConcert", "RedMeratConstruct",
        "RedMeratUGCDesign", "RedMeratUGCBanner", "QuestChapter00", "MagicScroll03", "GemDamageFont", "GemTombStone",
        "GemSwim", "GemCharEffect", "GemEffect", "GemPetSkin", "GemMeta", "QuestProgress", "ItemMerge", "UGCMapOX",
        "DungeonRank01", "DungeonRank02", "GuildSystem01", "BagRenewal01", "InventoryBank", "ReverseRaid01",
        "QuestNavigation", "GuildMinigame01", "GuildRaid01", "GemFishingRod", "HiddenStatAdd01", "HiddenStatAdd02",
        "EnchantScrollTab01", "RestoreScrollTab01", "FractoScrollTab01", "ProtectedScrollTab01", "ChaosRaid",
        "LiveBroadCast", "VIPGM", "MeratAirTaxi", "GlobalTransferType", "GuildBank01", "OptionBlock01",
        "EnchantBind01", "WebBrowserPopup", "OptionBlockWeapon", "OptionBlockArmor", "OptionBlockAccessory",
        "PetBase", "PetBattle", "Skin2tab", "ItemExtraction", "BlockExpelDungeonRoom", "Mentoring", "DailyMission",
        "FindDungeonHelp", "PetSkillStudy", "FameContents", "PcIllustOption", "PetButton01", "PetTrade01",
        "HappyBirthday", "PetRemake01", "PetRemakeBlock01", "PetRemakeBlock02", "AutoMastery02", "SurvivalContents01",
        "SurvivalContents02", "SurvivalContents03", "SurvivalContentsTeam", "SurvivalObserver", "BlackmarketMaxPrice",
        "ItemRePacking02", "OptionBlock02", "EnchantTransform01", "ItemSocketTransfer01", "QuestPostbox",
        "UGCMarketMusicScore", "WorldBossDungeon", "FameChallengeDungeon", "PremiumMarketPreListening",
        "RideShareAuthority", "HoldAttack", "MouseContoller", "Peachy01", "StatScale_01", "StatScale_02",
        "StatScale_03", "StatScale_04", "GamePad", "EnableCustomColor", "NaColorSystem", "DungeonRewardCountJoin01",
        "DungeonRewardCountJoin02", "MeratMessage", "AchieveReward01", "AchieveRewardMakeUp", "AdventureLevel",
        "PartyDamageMeter", "TriggerEditorHelpURL", "ConstructExp", "UserGeneratedDungeon_01", "CashCharge",
        "GamePadVibration", "MainMenuText", "UseNumberString", "TriggerVoiceDuration", "WorldChampionMushroom",
        "DungeonSmartMatch", "WaterMark", "JobChange_01", "LapentaDungeon", "EnchantReformation01",
        "OHItemAdditionalLevel01", "FreeSkin2tab", "ItemStoreMoveDisable01", "NextEpicQuestGuide",
        "EnchantExpansion01", "GuildSkillSystem01", "Lapenshard01", "FittingDollEquipAll", "RazorChroma",
        "GamePadLStickMoveSensitivity", "SplashIncludeCaster", "MushKingRoyaleModeSelect", "FoundersPackMode",
        "SwitchWeaponLR", "HousingLevelhurdle", "DirectQuestFindParty", "HighlightGameMenu", "ChatFormat",
        "StatScore_01", "PartyVoteKick", "JobChange_02", "MouseInteract", "Closet", "HousingDownsize",
        "EnchantDisableBreak01", "Kritias_2018_12", "Newbie", "AutoNewEquipSet", "Blueprint",
        "EnchantedGearItemBreak01", "PetEvolution01", "BlueprintMarket", "InventoryPieceTab", "ChatEmoticon",
        "FameCompletionTicket", "ColosseumDungeon", "MonsterBuffFilter", "ComboKeyCoolTime", "MesoMarketToken",
        "MesoMarketNA", "Discord", "LUA_itemRemakeOption", "ScreenshotWebSend", "MSRebuild", "DungeonFindRenewal01",
        "Peachy02", "AdventureLevelMission", "PeachyRecommended", "ItemSocketExpansion100", "GemStoneUpgrade100",
        "LapenshardUpgrade100", "UGCMapDelegator", "PetRePackingInfoOpen", "RedMeratMaid", "TurkaDungeon", "Wedding",
        "WeddingBlockSex", "WeddingBlockAge", "MassiveHideAndSeek", "MeratMarketNewBM", "Payback",
        "GachaCapsuleMachine", "RedMeratBeauty", "RedMeratSmartPush", "UnlimitedEnchant", "WaterGunBattle",
        "GemStoneSwap", "GuildPvP", "AdventureProtect", "DeleteAccount", "ProbabilityLogReport",
        "ProbabilityLogWriteDb", "ProbabilityLogUseStatistics", "ProbabilityLogReportForTest",
        "PersonalInfoAgreement",
    ];

    // feature.xml rows the ingest reads that the client maps to no id, so no client code checks them (all off in KR).
    private static readonly HashSet<string> NotClientNames = ["GlobalQuestRewardItem", "GlobalTransferTypeNA", "HiddenStatAdd03", "HiddenStatAdd04"];

    [Test]
    public void ClientList_HasEveryName() {
        Assert.That(ClientNames, Has.Count.EqualTo(278));
    }

    // Every public string constant named *Feature in the model and the game server.
    [Test]
    public void FeatureConstants_AreClientNames() {
        List<(string Field, string Name)> constants = new[] { typeof(ItemSocketRules).Assembly, typeof(GuildHandler).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name.EndsWith("Feature"))
            .Select(field => ($"{field.DeclaringType!.Name}.{field.Name}", (string) field.GetRawConstantValue()!))
            .ToList();
        Assert.That(constants, Has.Count.GreaterThanOrEqualTo(10));
        Assert.That(constants.Where(constant => !ClientNames.Contains(constant.Name)), Is.Empty);
    }

    // The names written in place: IsOn("...") in the servers and FeatureEnabled("...") in the ingest.
    [Test]
    public void FeatureLiterals_AreClientNames() {
        string root = RepositoryRoot();
        var literal = new Regex(@"(?:IsOn|FeatureEnabled)\(""([^""]+)""\)");
        List<string> unknown = [];
        int count = 0;
        foreach (string project in new[] { "Maple2.Server.Game", "Maple2.Server.World", "Maple2.Server.Login", "Maple2.Server.Core", "Maple2.Model", "Maple2.File.Ingest" }) {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories)) {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) {
                    continue;
                }
                foreach (Match match in literal.Matches(System.IO.File.ReadAllText(file))) {
                    count++;
                    string name = match.Groups[1].Value;
                    if (!ClientNames.Contains(name) && !NotClientNames.Contains(name)) {
                        unknown.Add($"{Path.GetFileName(file)}: {name}");
                    }
                }
            }
        }
        Assert.That(count, Is.GreaterThan(0));
        Assert.That(unknown, Is.Empty);
    }

    [Test]
    public void NotClientNames_AreNotClientNames() {
        Assert.That(NotClientNames.Where(ClientNames.Contains), Is.Empty);
    }

    private static string RepositoryRoot() {
        DirectoryInfo? directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Maple2.File.Ingest"))) {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
