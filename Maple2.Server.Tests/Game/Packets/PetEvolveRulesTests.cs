using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Fodder = Maple2.Server.Game.Util.PetEvolveRules.FodderError;

namespace Maple2.Server.Tests.Game.Packets;

// Pet evolution, RequestPet modes 17 and 18 (tabcontentspetevolution.cpp). Made-up ids and table rows.
public class PetEvolveRulesTests {
    private const long Now = 1_000_000;

    private static Item PetOf(long uid, int petId = 61000001, int rarity = 2, int points = 0) {
        var property = new ItemMetadataProperty(false, 0, 1, 0, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0) {
            PetId = petId,
        };
        var limit = new ItemMetadataLimit(Gender.All, 50, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(61900001, "61900001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Item(metadata, rarity) { Uid = uid, Pet = new ItemPet { EvolvePoints = points } };
    }

    private static PetEvolutionTable Table() {
        var document = new XmlDocument();
        document.LoadXml("""
            <ms2>
              <petEvolution requireItemLevel="50" Grade1="7" Grade2="9" Grade3="11" Grade4="0" />
              <petEvolution requireItemLevel="60" Grade1="1" Grade2="1" Grade3="1" Grade4="1" feature="Off" />
              <petEvolution requireItemLevel="70" Grade1="3" Grade2="3" Grade3="3" Grade4="5" />
              <petEvolution requireItemLevel="70" Grade1="4" Grade2="4" Grade3="4" Grade4="6" />
            </ms2>
            """);
        Dictionary<int, PetEvolutionTable.Entry> rows = TableMapper.ReadPetEvolutions(document, node => node.GetAttribute("feature") != "Off");
        return new PetEvolutionTable(rows);
    }

    [Test]
    public void Need_IsTheGradeColumnOfTheLevelRow() {
        PetEvolutionTable table = Table();
        Assert.Multiple(() => {
            Assert.That(PetEvolveRules.Need(table, 50, 1), Is.EqualTo(7));
            Assert.That(PetEvolveRules.Need(table, 50, 3), Is.EqualTo(11));
            Assert.That(PetEvolveRules.Need(table, 50, 4), Is.EqualTo(0)); // Grade4 0: does not evolve
            Assert.That(PetEvolveRules.Need(table, 50, 5), Is.EqualTo(0)); // no column
            Assert.That(PetEvolveRules.Need(table, 50, 0), Is.EqualTo(0));
            Assert.That(PetEvolveRules.Need(table, 60, 1), Is.EqualTo(0)); // row the filter refuses
            Assert.That(PetEvolveRules.Need(table, 70, 4), Is.EqualTo(6)); // the later row of a level wins
            Assert.That(PetEvolveRules.Need(table, 40, 1), Is.EqualTo(0)); // no row
        });
    }

    [TestCase(1, 50, 50, true)]
    [TestCase(0, 50, 50, false)] // pet row type 0
    [TestCase(1, 49, 50, false)] // below the top pet level
    [TestCase(1, 0, 0, false)] // constant not loaded
    public void Target_IsATypedPetAtTheTopLevel(int type, int level, int maxLevel, bool expected) {
        Assert.That(PetEvolveRules.IsTarget(type, level, maxLevel), Is.EqualTo(expected));
    }

    [TestCase(0, 1)]
    [TestCase(1, 1)]
    [TestCase(5, 5)]
    public void FodderAddsItsPointsAtLeastOne(int points, int expected) {
        Assert.That(PetEvolveRules.FodderPoints(points), Is.EqualTo(expected));
    }

    [Test]
    public void FodderChecks() {
        Item target = PetOf(1);
        Item ok = PetOf(2);
        Item hasItems = PetOf(3);
        hasItems.Pet!.HasItems = true;
        Item locked = PetOf(4);
        locked.IsLocked = true;
        Item unlocking = PetOf(5);
        unlocking.UnlockTime = Now + 1;
        Item unlocked = PetOf(6);
        unlocked.UnlockTime = Now;

        Assert.Multiple(() => {
            Assert.That(PetEvolveRules.CheckFodder(target, ok, 0, 0, 7, Now), Is.EqualTo(Fodder.None));
            Assert.That(PetEvolveRules.CheckFodder(target, null, 0, 0, 7, Now), Is.EqualTo(Fodder.Missing));
            Assert.That(PetEvolveRules.CheckFodder(target, target, 0, 0, 7, Now), Is.EqualTo(Fodder.Target));
            Assert.That(PetEvolveRules.CheckFodder(target, ok, ok.Uid, 0, 7, Now), Is.EqualTo(Fodder.Summoned));
            Assert.That(PetEvolveRules.CheckFodder(target, PetOf(7, petId: 61000002), 0, 0, 7, Now), Is.EqualTo(Fodder.OtherKind));
            Assert.That(PetEvolveRules.CheckFodder(target, PetOf(8, rarity: 3), 0, 0, 7, Now), Is.EqualTo(Fodder.OtherRarity));
            Assert.That(PetEvolveRules.CheckFodder(target, hasItems, 0, 0, 7, Now), Is.EqualTo(Fodder.HasItems));
            Assert.That(PetEvolveRules.CheckFodder(target, locked, 0, 0, 7, Now), Is.EqualTo(Fodder.Locked));
            Assert.That(PetEvolveRules.CheckFodder(target, unlocking, 0, 0, 7, Now), Is.EqualTo(Fodder.Locked));
            Assert.That(PetEvolveRules.CheckFodder(target, unlocked, 0, 0, 7, Now), Is.EqualTo(Fodder.None));
            Assert.That(PetEvolveRules.CheckFodder(target, ok, 0, 6, 7, Now), Is.EqualTo(Fodder.None)); // the last one may go over
            Assert.That(PetEvolveRules.CheckFodder(target, ok, 0, 7, 7, Now), Is.EqualTo(Fodder.Full));
        });
    }

    [Test]
    public void Cost_IsTheScriptCostPerFodderRarity() {
        Assert.Multiple(() => {
            Assert.That(PetEvolveRules.Cost([2, 2, 2]), Is.EqualTo(3 * Server.Game.LuaFunctions.Lua.CalcPetEvolutionCost(2)));
            Assert.That(PetEvolveRules.Cost([]), Is.EqualTo(0));
        });
    }

    [TestCase(7, 7, 1, true)]
    [TestCase(9, 7, 1, true)] // points over the need
    [TestCase(6, 7, 1, false)]
    [TestCase(0, 0, 1, false)] // rarity that does not evolve
    [TestCase(7, 7, 0, false)] // no result level for the next rarity
    public void Evolve_NeedsThePoints(long points, int need, int resultLevel, bool expected) {
        Assert.That(PetEvolveRules.CanEvolve(points, need, resultLevel), Is.EqualTo(expected));
    }

    [Test]
    public void EvolvedStats_KeepTheRandomGroup() {
        var old = new ItemStats();
        old[ItemStats.Type.Random] = new ItemStats.Option(new Dictionary<BasicAttribute, BasicOption> { [BasicAttribute.Strength] = new BasicOption(3) });
        old[ItemStats.Type.Constant] = new ItemStats.Option(new Dictionary<BasicAttribute, BasicOption> { [BasicAttribute.Dexterity] = new BasicOption(1) });
        var rolled = new ItemStats();
        rolled[ItemStats.Type.Constant] = new ItemStats.Option(new Dictionary<BasicAttribute, BasicOption> { [BasicAttribute.Dexterity] = new BasicOption(2) });

        ItemStats? result = PetEvolveRules.EvolvedStats(rolled, old);
        Assert.Multiple(() => {
            Assert.That(result![ItemStats.Type.Random].Basic[BasicAttribute.Strength].Value, Is.EqualTo(3));
            Assert.That(result[ItemStats.Type.Constant].Basic[BasicAttribute.Dexterity].Value, Is.EqualTo(2));
            Assert.That(PetEvolveRules.EvolvedStats(null, old), Is.SameAs(old)); // item without an option row
        });
    }
}
