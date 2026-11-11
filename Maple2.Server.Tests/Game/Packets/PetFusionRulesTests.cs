using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Fodder = Maple2.Server.Game.Util.PetFusionRules.FodderError;
using Kind = Maple2.Server.Game.Util.PetFusionRules.FodderKind;

namespace Maple2.Server.Tests.Game.Packets;

// Pet fusion, RequestPet mode 12 (tabcontentspetcompose.cpp). Made-up ids and table rows.
public class PetFusionRulesTests {
    private const long Now = 1_000_000;

    internal static Item ItemOf(long uid, int rarity = 2, int levelLimit = 50, bool pet = true, ItemTag tag = ItemTag.None,
            ItemMetadataFunction? function = null, int amount = 1) {
        var property = new ItemMetadataProperty(false, 0, 99, 0, 0, string.Empty, string.Empty, tag, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0) {
            PetId = pet ? 61000001 : 0,
        };
        var limit = new ItemMetadataLimit(Gender.All, levelLimit, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(61900001, "61900001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, function, [], null, null, null, null);
        return new Item(metadata, rarity, amount) { Uid = uid, Pet = pet ? new ItemPet() : null };
    }

    private static PetExpTable Table() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <exp level="1" normal="100" rare="200" elite="300" excellent="400" legendary="500" artifact="0" />
              <exp level="2" normal="150" rare="250" elite="350" excellent="450" legendary="550" artifact="0" />
              <exp level="3" normal="1" rare="1" elite="1" excellent="1" legendary="1" artifact="1" feature="Off" />
              <exp level="3" normal="300" rare="300" />
              <exp level="0" normal="5" />
              <exp level="100" normal="5" />
            </ms2>
            """);
        Dictionary<int, long[]> rows = TableMapper.ReadPetExps(document, node => node.GetAttribute("feature") != "Off");
        return new PetExpTable(rows);
    }

    [Test]
    public void Table_KeepsLevels1To99AndTheLaterRow() {
        Dictionary<int, long[]> rows = new(Table().Entries);
        Assert.Multiple(() => {
            Assert.That(rows.Keys, Is.EquivalentTo(new[] { 1, 2, 3 }));
            Assert.That(rows[3], Is.EqualTo(new long[] { 300, 300, 0, 0, 0, 0 })); // missing columns read 0
            Assert.That(rows[2][4], Is.EqualTo(550));
        });
    }

    [Test]
    public void Need_IsTheRarityColumnScaledByTheLevelLimit() {
        PetExpTable table = Table();
        Assert.Multiple(() => {
            Assert.That(table.Need(1, 1, 50), Is.EqualTo(100));
            Assert.That(table.Need(1, 2, 60), Is.EqualTo(300)); // x1.5
            Assert.That(table.Need(2, 5, 70), Is.EqualTo(1237)); // 550 x2.25, truncated
            Assert.That(table.Need(1, 6, 50), Is.EqualTo(0));
            Assert.That(table.Need(1, 7, 50), Is.EqualTo(long.MaxValue));
            Assert.That(table.Need(51, 1, 50), Is.EqualTo(long.MaxValue));
            Assert.That(table.Need(40, 1, 50), Is.EqualTo(0)); // no row
        });
    }

    [TestCase(1, 1, 50, true)]
    [TestCase(1, 49, 50, true)]
    [TestCase(1, 50, 50, false)] // top level: s_pet_not_enchant
    [TestCase(0, 1, 50, false)] // pet row type 0 is not a pet card
    [TestCase(1, 1, 0, false)] // constant not loaded
    public void Target_IsATypedPetBelowTheTopLevel(int type, int level, int maxLevel, bool expected) {
        Assert.That(PetFusionRules.IsTarget(type, level, maxLevel), Is.EqualTo(expected));
    }

    [Test]
    public void Kind_PetsNeedATypeBooksTheTag() {
        Assert.Multiple(() => {
            Assert.That(PetFusionRules.KindOf(ItemOf(1), 2), Is.EqualTo(Kind.Pet));
            Assert.That(PetFusionRules.KindOf(ItemOf(1), 0), Is.EqualTo(Kind.None));
            Assert.That(PetFusionRules.KindOf(ItemOf(1), null), Is.EqualTo(Kind.None));
            Assert.That(PetFusionRules.KindOf(ItemOf(1, pet: false, tag: ItemTag.PetEXP), null), Is.EqualTo(Kind.Book));
            Assert.That(PetFusionRules.KindOf(ItemOf(1, pet: false), null), Is.EqualTo(Kind.None));
        });
    }

    [Test]
    public void Fodder_ChecksInTheWindowsOrder() {
        Item target = ItemOf(1);
        Item hasItems = ItemOf(3);
        hasItems.Pet!.HasItems = true;
        hasItems.IsLocked = true; // the item check comes first
        Item locked = ItemOf(4);
        locked.IsLocked = true;
        Item unlocking = ItemOf(5);
        unlocking.UnlockTime = Now + 1;
        Item unlocked = ItemOf(6);
        unlocked.UnlockTime = Now;
        Item books = ItemOf(7, pet: false, tag: ItemTag.PetEXP, amount: 3);
        Assert.Multiple(() => {
            Assert.That(PetFusionRules.CheckFodder(target, null, Kind.None, 1, 0, Now), Is.EqualTo(Fodder.Missing));
            Assert.That(PetFusionRules.CheckFodder(target, target, Kind.Pet, 1, 0, Now), Is.EqualTo(Fodder.Target));
            Assert.That(PetFusionRules.CheckFodder(target, ItemOf(2), Kind.Pet, 1, 2, Now), Is.EqualTo(Fodder.Summoned));
            Assert.That(PetFusionRules.CheckFodder(target, ItemOf(2), Kind.None, 1, 0, Now), Is.EqualTo(Fodder.NotFodder));
            Assert.That(PetFusionRules.CheckFodder(target, ItemOf(2), Kind.Pet, 2, 0, Now), Is.EqualTo(Fodder.Count));
            Assert.That(PetFusionRules.CheckFodder(target, ItemOf(2), Kind.Pet, 0, 0, Now), Is.EqualTo(Fodder.Count));
            Assert.That(PetFusionRules.CheckFodder(target, hasItems, Kind.Pet, 1, 0, Now), Is.EqualTo(Fodder.HasItems));
            Assert.That(PetFusionRules.CheckFodder(target, locked, Kind.Pet, 1, 0, Now), Is.EqualTo(Fodder.Locked));
            Assert.That(PetFusionRules.CheckFodder(target, unlocking, Kind.Pet, 1, 0, Now), Is.EqualTo(Fodder.Locked));
            Assert.That(PetFusionRules.CheckFodder(target, unlocked, Kind.Pet, 1, 0, Now), Is.EqualTo(Fodder.None));
            Assert.That(PetFusionRules.CheckFodder(target, books, Kind.Book, 3, 0, Now), Is.EqualTo(Fodder.None));
            Assert.That(PetFusionRules.CheckFodder(target, books, Kind.Book, 4, 0, Now), Is.EqualTo(Fodder.Count));
        });
    }

    [TestCase(0, 1, 50, Fodder.None)]
    [TestCase(11, 49, 50, Fodder.None)] // the last unit may go over the top level
    [TestCase(12, 1, 50, Fodder.TooMany)]
    [TestCase(3, 50, 50, Fodder.Full)]
    public void Unit_StopsAtTwelveOrTheTopLevel(int taken, int predicted, int maxLevel, Fodder expected) {
        Assert.That(PetFusionRules.CheckUnit(taken, predicted, maxLevel), Is.EqualTo(expected));
    }

    [TestCase("3000", 3000L)]
    [TestCase("12000,5", 12000L)]
    [TestCase(" 500 ", 500L)]
    [TestCase("", 0L)]
    [TestCase("abc", 0L)]
    [TestCase("-5", 0L)]
    public void Book_GivesTheFirstParameter(string parameters, long expected) {
        var function = new ItemMetadataFunction(ItemFunction.AddPetEXP, "AddPetEXP", parameters, false);
        Assert.That(PetFusionRules.BookExp(function), Is.EqualTo(expected));
    }

    [Test]
    public void Book_WithoutAFunctionGivesNothing() {
        Assert.That(PetFusionRules.BookExp(null), Is.EqualTo(0));
    }

    [Test]
    public void PetExp_UsesTheFodderAndTheCount() {
        Item target = ItemOf(1, rarity: 4);
        Item fodder = ItemOf(2, rarity: 3, levelLimit: 60);
        fodder.Pet!.Level = 5;
        fodder.Pet.Exp = 77;
        Assert.Multiple(() => {
            Assert.That(PetFusionRules.PetExp(target, fodder, 0, 10),
                Is.EqualTo(Server.Game.LuaFunctions.Lua.CalcPetEnchantExp(4, 3, 5, 77, 0, 10, 60)));
            Assert.That(PetFusionRules.PetExp(target, fodder, 20, 10),
                Is.EqualTo(Server.Game.LuaFunctions.Lua.CalcPetEnchantExp(4, 3, 5, 77, 20, 10, 60)));
            Assert.That(PetFusionRules.PetExp(target, fodder, 0, 10).Bonus, Is.GreaterThan(0)); // within the daily limit
            Assert.That(PetFusionRules.PetExp(target, fodder, 20, 10).Bonus, Is.EqualTo(0));
        });
    }

    [TestCase(1, 0L, 50L, 1, 50L)] // short of the need
    [TestCase(1, 0L, 100L, 2, 0L)] // exactly one level (rarity 1: 100)
    [TestCase(1, 50L, 300L, 3, 100L)] // 350 - 100 - 150
    [TestCase(1, 0L, 100000L, 5, 0L)] // capped at the top level with 0 exp
    [TestCase(5, 10L, 999L, 5, 10L)] // at the top level nothing changes
    [TestCase(1, 30L, -50L, 1, 0L)] // a loss larger than the exp
    [TestCase(1, 80L, -30L, 1, 50L)]
    public void Apply_LevelsUpWhileTheExpCoversTheNeed(int level, long exp, long gain, int expectedLevel, long expectedExp) {
        (int Level, long Exp) result = PetFusionRules.Apply(Table(), level, exp, 1, 50, gain, 5);
        Assert.That(result, Is.EqualTo((expectedLevel, expectedExp)));
    }

    [Test]
    public void Apply_ZeroNeedRunsToTheTopLevel() {
        // Rarity 6 reads 0 in every row: the client's loop runs past level 50, then caps at the top level.
        Assert.That(PetFusionRules.Apply(Table(), 1, 0, 6, 50, 1, 50), Is.EqualTo((50, 0L)));
    }

    [Test]
    public void Cost_PetByFodderLevelBookByTargetRarity() {
        Item target = ItemOf(1, rarity: 3);
        Item fodder = ItemOf(2, rarity: 1);
        fodder.Pet!.Level = 4;
        Item book = ItemOf(3, pet: false, tag: ItemTag.PetEXP);
        Assert.Multiple(() => {
            Assert.That(PetFusionRules.UnitCost(target, fodder, Kind.Pet), Is.EqualTo(10000 + 3 * 50000));
            Assert.That(PetFusionRules.UnitCost(target, book, Kind.Book), Is.EqualTo(10000));
        });
    }

    [TestCase(3000L, 0, 3000L)]
    [TestCase(3000L, 1000, 2700L)]
    [TestCase(3333L, 3000, 2333L)]
    [TestCase(5000L, 10000, 0L)]
    public void Sale_TakesTheRateInTenThousandths(long cost, int rate, long expected) {
        Assert.That(PetFusionRules.ApplySale(cost, rate), Is.EqualTo(expected));
    }

    [TestCase(7, 500L, 400L, 7)]
    [TestCase(7, 400L, 400L, 7)]
    [TestCase(7, 399L, 400L, 0)] // stored before the last reset
    [TestCase(7, 0L, 0L, 0)] // no time stored
    public void DailyCount_ResetsAfterTheDailyReset(int stored, long time, long lastReset, int expected) {
        Assert.That(PetFusionRules.DailyCount(stored, time, lastReset), Is.EqualTo(expected));
    }

    [Test]
    public void LoginCount_GivesTodaysPairOrZeroFromNow() {
        Assert.Multiple(() => {
            Assert.That(PetFusionRules.LoginCount(7, 500, 400, 900), Is.EqualTo((7, 500L)));
            Assert.That(PetFusionRules.LoginCount(7, 300, 400, 900), Is.EqualTo((0, 900L)));
            Assert.That(PetFusionRules.LoginCount(0, 0, 400, 900), Is.EqualTo((0, 900L)));
        });
    }
}
