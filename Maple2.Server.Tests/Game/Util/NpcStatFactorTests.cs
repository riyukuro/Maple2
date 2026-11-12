using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class NpcStatFactorTests {
    // Made-up factor ids and rates in the table's row layout.
    private const string Xml = """
        <ms2>
          <PlayerCountFactor factorID="9101" class="1" playerCount="1" hpRate="1" hpValue="0" papRate="0.5" papValue="0" mapRate="0.5" mapValue="0" nddRate="1" nddValue="0" capRate="1" capValue="0" />
          <PlayerCountFactor factorID="9101" class="1" playerCount="2" hpRate="2" hpValue="0" papRate="0.6" papValue="0" mapRate="0.6" mapValue="0" nddRate="1" nddValue="0" capRate="1" capValue="0" />
          <PlayerCountFactor factorID="9101" class="1" playerCount="3" hpRate="3" hpValue="10" papRate="0.7" papValue="5" mapRate="0.8" mapValue="0" nddRate="1.5" nddValue="0" capRate="0.25" capValue="0" />
          <PlayerCountFactor factorID="9101" class="6" playerCount="1" hpRate="0.1" hpValue="0" papRate="0.2" papValue="0" mapRate="0.2" mapValue="0" nddRate="1" nddValue="0" capRate="1" capValue="0" />
          <PlayerCountFactor factorID="9102" class="1" playerCount="4" hpRate="4" hpValue="0" papRate="1" papValue="0" mapRate="1" mapValue="0" nddRate="1" nddValue="0" capRate="1" capValue="0" />
          <PlayerCountFactor factorID="9102" class="1" playerCount="6" hpRate="6" hpValue="0" papRate="1" papValue="0" mapRate="1" mapValue="0" nddRate="1" nddValue="0" capRate="1" capValue="0" />
          <PlayerCountFactor factorID="9103" class="1" playerCount="1" feature="SomeOffFeature" hpRate="9" hpValue="0" papRate="9" papValue="0" mapRate="9" mapValue="0" nddRate="9" nddValue="0" capRate="9" capValue="0" />
          <PlayerCountFactor factorID="" class="" playerCount="" hpRate="" hpValue="" papRate="" papValue="" mapRate="" mapValue="" nddRate="" nddValue="" capRate="" capValue="" />
        </ms2>
        """;

    private static NpcStatFactorTable Table() {
        var document = new XmlDocument();
        document.LoadXml(Xml);
        return ServerTableMapper.MapNpcStatFactor(document, row => row.GetAttribute("feature") != "SomeOffFeature");
    }

    private static readonly Dictionary<BasicAttribute, long> Stats = new() {
        [BasicAttribute.Health] = 1000,
        [BasicAttribute.PhysicalAtk] = 200,
        [BasicAttribute.MagicalAtk] = 300,
        [BasicAttribute.Defense] = 40,
        [BasicAttribute.CriticalRate] = 80,
        [BasicAttribute.Evasion] = 7,
    };

    [Test]
    public void IngestSkipsEmptyAndFeatureOffRows() {
        NpcStatFactorTable table = Table();
        Assert.That(table.Entries.Keys, Is.EquivalentTo(new[] { 9101, 9102 }));
        Assert.That(table.Entries[9101][1].Keys, Is.EquivalentTo(new[] { 1, 2, 3 }));
        Assert.That(table.Entries[9101][1][3].HpValue, Is.EqualTo(10));
        Assert.That(table.Entries[9101][1][3].CapRate, Is.EqualTo(0.25));
    }

    [Test]
    public void RowLookupByIdClassAndCount() {
        NpcStatFactorTable table = Table();
        Assert.That(NpcStatFactorRules.Find(table, 9101, 1, 1)!.PapRate, Is.EqualTo(0.5));
        Assert.That(NpcStatFactorRules.Find(table, 9101, 1, 2)!.PapRate, Is.EqualTo(0.6));
        Assert.That(NpcStatFactorRules.Find(table, 9101, 6, 1)!.PapRate, Is.EqualTo(0.2));

        Dictionary<BasicAttribute, long> scaled = NpcStatFactorRules.Apply(Stats, NpcStatFactorRules.Find(table, 9101, 1, 3)!);
        Assert.That(scaled[BasicAttribute.Health], Is.EqualTo(3010));
        Assert.That(scaled[BasicAttribute.PhysicalAtk], Is.EqualTo(145));
        Assert.That(scaled[BasicAttribute.MagicalAtk], Is.EqualTo(240));
        Assert.That(scaled[BasicAttribute.Defense], Is.EqualTo(60));
        Assert.That(scaled[BasicAttribute.CriticalRate], Is.EqualTo(20));
        Assert.That(scaled[BasicAttribute.Evasion], Is.EqualTo(7));
        // Cut to whole numbers: 333 x 0.5 = 166.5.
        Assert.That(NpcStatFactorRules.Apply(new Dictionary<BasicAttribute, long> { [BasicAttribute.PhysicalAtk] = 333 },
            NpcStatFactorRules.Find(table, 9101, 1, 1)!)[BasicAttribute.PhysicalAtk], Is.EqualTo(166));
    }

    [Test]
    public void NoRowLeavesStatsUnchanged() {
        NpcStatFactorTable table = Table();
        Assert.That(NpcStatFactorRules.Find(table, 0, 1, 1), Is.Null);
        Assert.That(NpcStatFactorRules.Find(table, 9999, 1, 1), Is.Null);
        Assert.That(NpcStatFactorRules.Find(table, 9103, 1, 1), Is.Null);
    }

    [Test]
    public void ClassMismatchHasNoRow() {
        NpcStatFactorTable table = Table();
        Assert.That(NpcStatFactorRules.Find(table, 9101, 3, 1), Is.Null);
        Assert.That(NpcStatFactorRules.Find(table, 9102, 6, 4), Is.Null);
    }

    [Test]
    public void CountIsCappedAtTheTablesMax() {
        NpcStatFactorTable table = Table();
        Assert.That(NpcStatFactorRules.Find(table, 9101, 1, 10)!.PapRate, Is.EqualTo(0.7));
        Assert.That(NpcStatFactorRules.Find(table, 9101, 6, 4)!.PapRate, Is.EqualTo(0.2));
        Assert.That(NpcStatFactorRules.Find(table, 9102, 1, 9)!.HpRate, Is.EqualTo(6));
        // A gap takes the nearest lower row; below every row takes the lowest one.
        Assert.That(NpcStatFactorRules.Find(table, 9102, 1, 5)!.HpRate, Is.EqualTo(4));
        Assert.That(NpcStatFactorRules.Find(table, 9102, 1, 1)!.HpRate, Is.EqualTo(4));
    }

    [Test]
    public void RoomIdWinsAndCountIsRoomSizeOrPlayersPresent() {
        Assert.That(NpcStatFactorRules.FactorId(9101, 9102), Is.EqualTo(9101));
        Assert.That(NpcStatFactorRules.FactorId(0, 9102), Is.EqualTo(9102));
        Assert.That(NpcStatFactorRules.FactorId(0, 0), Is.EqualTo(0));
        Assert.That(NpcStatFactorRules.PlayerCount(1, 0), Is.EqualTo(1));
        Assert.That(NpcStatFactorRules.PlayerCount(0, 0), Is.EqualTo(1));
        Assert.That(NpcStatFactorRules.PlayerCount(4, 2), Is.EqualTo(4));
        Assert.That(NpcStatFactorRules.PlayerCount(1, 3), Is.EqualTo(3));
    }

    [Test]
    public void NpcDamageFactorIsUntouched() {
        Assert.That(Constant.NpcAttackDamageFactor, Is.EqualTo(2.85));
        Assert.That(DamageCalculator.AttackDamageFactor(true), Is.EqualTo(2.85));
    }
}
