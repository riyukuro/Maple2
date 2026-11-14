using System.Collections.Generic;
using System.Xml;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game;

// The data pack's final stats (FinalStatusProperty, StatusProperty finalXxxV/R), which the parser does not carry, are
// taken from the client's SpecialAbility rows of the level (ClientSpecialAbility), else from the pack's rows.
public class FinalStatusTests {
    // Rows of the client's table/specialabilitystringtable.xml.
    private const string Table = """
        <ms2>
          <key id="5" option="finalAttackPower" valueType="2" />
          <key id="6" option="wapRate" valueType="2" />
          <key id="10" option="atpToBap" valueType="2" />
          <key id="19" option="fireDamage" valueType="2" />
          <key id="44" option="hpRate" valueType="2" />
          <key id="53" option="mspRate" valueType="2" />
          <key id="124" option="jmpRate" valueType="2" />
          <key id="175" option="defensive_physicaldamage" valueType="2" />
          <key id="177" option="offensive_physicaldamage" valueType="2" />
          <key id="187" option="msp" valueType="1" />
          <key id="202" option="cap_ignore_limit" valueType="1" />
          <key id="203" option="cad_ignore_limit" valueType="1" />
          <key id="204" option="aevp" valueType="2" />
          <key id="205" option="cap_ignore_limit_extra" valueType="2" />
          <key id="195" option="pap" valueType="1" />
          <key id="196" option="map" valueType="1" />
          <key id="199" option="pen" valueType="2" />
          <key id="201" option="bap" valueType="1" />
        </ms2>
        """;

    private static ClientSpecialAbility SpecialAbility() {
        var document = new XmlDocument();
        document.LoadXml(Table);
        return new ClientSpecialAbility(document);
    }

    private static XmlElement Element(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document.DocumentElement!;
    }

    // Rates are thousandths on the client and fractions on the server: the pack's penrate 0.08 is the client's pen 80,
    // its firedamagerate 0.01 (10900121) the client's fireDamage 10, its hprate 11.5 (70000027) the client's hpRate 11500.
    [Test]
    public void RatesAreThousandths() {
        ClientSpecialAbility specialAbility = SpecialAbility();
        var stats = new ClientSpecialAbility.Stats();
        Assert.That(specialAbility.Add(stats, "pen", 80), Is.True);
        Assert.That(specialAbility.Add(stats, "fireDamage", 10), Is.True);
        Assert.That(specialAbility.Add(stats, "hpRate", 11500), Is.True);
        Assert.That(specialAbility.Add(stats, "msp", -50), Is.True);
        Assert.That(specialAbility.Add(stats, "offensive_physicaldamage", 1500), Is.True);
        Assert.That(specialAbility.Add(stats, "defensive_physicaldamage", 300), Is.True);
        Assert.That(stats.Rates[BasicAttribute.Piercing], Is.EqualTo(0.08f).Within(1e-6));
        Assert.That(stats.SpecialRates[SpecialAttribute.FireDamage], Is.EqualTo(0.01f).Within(1e-6));
        Assert.That(stats.Rates[BasicAttribute.Health], Is.EqualTo(11.5f).Within(1e-6));
        Assert.That(stats.Values[BasicAttribute.MovementSpeed], Is.EqualTo(-50));
        Assert.That(stats.SpecialRates[SpecialAttribute.OffensivePhysicalDamage], Is.EqualTo(1.5f).Within(1e-6));
        Assert.That(stats.PhysicalDamageTakenRate, Is.EqualTo(0.3f).Within(1e-6));
    }

    // The crit specials 202, 203 and 205 at the played pack's values: cap_ignore_limit 20 (10700181 level 1),
    // cad_ignore_limit 200 (10400243), cap_ignore_limit_extra 280 (10800041 level 1, thousandths).
    [Test]
    public void CritIgnoreLimitSpecials() {
        ClientSpecialAbility specialAbility = SpecialAbility();
        var stats = new ClientSpecialAbility.Stats();
        Assert.That(specialAbility.Add(stats, "cap_ignore_limit", 20), Is.True);
        Assert.That(specialAbility.Add(stats, "cad_ignore_limit", 200), Is.True);
        Assert.That(specialAbility.Add(stats, "cap_ignore_limit_extra", 280), Is.True);
        Assert.That(specialAbility.Unmapped, Is.Empty);
        Assert.That(stats.SpecialValues[(SpecialAttribute) 202], Is.EqualTo(20));
        Assert.That(stats.SpecialValues[(SpecialAttribute) 203], Is.EqualTo(200));
        Assert.That(stats.SpecialRates[(SpecialAttribute) 205], Is.EqualTo(0.28f).Within(1e-6));
    }

    // A type with no server stat, or none in the table, is counted and left out.
    [Test]
    public void CountsTypesWithoutAServerStat() {
        ClientSpecialAbility specialAbility = SpecialAbility();
        var stats = new ClientSpecialAbility.Stats();
        Assert.That(specialAbility.Add(stats, "aevp", 100), Is.False);
        Assert.That(specialAbility.Add(stats, "notAType", 100), Is.False);
        Assert.That(specialAbility.Unmapped, Is.EqualTo(new Dictionary<string, int> { ["aevp"] = 1, ["notAType"] = 1 }));
        Assert.That(stats.Rates, Is.Empty);
    }

    // The client keeps the last nonzero row of a type.
    [Test]
    public void ClientKeepsTheLastNonzeroRow() {
        Dictionary<string, int> rows = PlayedClientAdditionalEffects.SpecialAbility(Element(
            """<SpecialAbility><v type="hpRate" value="100" /><v type="hpRate" value="200" /><v type="msp" value="0" /></SpecialAbility>"""));
        Assert.That(rows, Is.EqualTo(new Dictionary<string, int> { ["hpRate"] = 200 }));
    }

    [Test]
    public void ReadsThePacksFinalRows() {
        List<FinalStatusRows.Row> rows = FinalStatusRows.Read(
            Element("""<StatusProperty statChangeRate="0" finalCapV="50000017" finalCapR="0" finalAbpV="0" />"""),
            Element("""<Stat finalwapminrate="0.16" finalpapvalue="1" finalhprate="0" hprate="3" />"""));
        Assert.That(rows, Is.EqualTo(new[] {
            new FinalStatusRows.Row("finalCapV", BasicAttribute.CriticalRate, false, 50000017, Status: true),
            new FinalStatusRows.Row("finalwapminrate", BasicAttribute.MinWeaponAtk, true, 0.16f),
            new FinalStatusRows.Row("finalpapvalue", BasicAttribute.PhysicalAtk, false, 1),
        }));
    }

    // 70002184: finalhprate -0.9 and finalmsprate -0.9 are the client's hpRate -900 and msp -90, a flat value.
    // 73000001: finalmsprate 0.5 and finaljmprate 0.5 are its mspRate 500 and jmpRate 500, rates.
    [Test]
    public void TakesTheClientsRowsOnTheFinalAttributes() {
        ClientSpecialAbility specialAbility = SpecialAbility();
        var stats = new ClientSpecialAbility.Stats();
        FinalStatusRows.Row[] rows = [
            new("finalhprate", BasicAttribute.Health, true, -0.9f),
            new("finalmsprate", BasicAttribute.MovementSpeed, true, -0.9f),
        ];
        Assert.That(FinalStatusRows.Apply(specialAbility, stats, rows, new Dictionary<string, int> { ["hpRate"] = -900, ["msp"] = -90 }), Is.True);
        Assert.That(stats.Rates, Is.EqualTo(new Dictionary<BasicAttribute, float> { [BasicAttribute.Health] = -0.9f }));
        Assert.That(stats.Values, Is.EqualTo(new Dictionary<BasicAttribute, long> { [BasicAttribute.MovementSpeed] = -90 }));

        stats = new ClientSpecialAbility.Stats();
        rows = [
            new("finalmsprate", BasicAttribute.MovementSpeed, true, 0.5f),
            new("finaljmprate", BasicAttribute.JumpHeight, true, 0.5f),
        ];
        FinalStatusRows.Apply(specialAbility, stats, rows, new Dictionary<string, int> { ["mspRate"] = 500, ["jmpRate"] = 500 });
        Assert.That(stats.Rates, Is.EqualTo(new Dictionary<BasicAttribute, float> { [BasicAttribute.MovementSpeed] = 0.5f, [BasicAttribute.JumpHeight] = 0.5f }));
        Assert.That(stats.Values, Is.Empty);
    }

    // 89000002: finalpap/maprate 0.252 and finalpap/mapvalue 1 are the client's finalAttackPower 252, pap 1 and map 1.
    // 90009501: its bap 4120 is the pack's normal bapvalue, already in the stats, and not added again.
    [Test]
    public void FinalAttackPowerAndWeaponRates() {
        ClientSpecialAbility specialAbility = SpecialAbility();
        var stats = new ClientSpecialAbility.Stats();
        FinalStatusRows.Row[] rows = [
            new("finalpaprate", BasicAttribute.PhysicalAtk, true, 0.252f),
            new("finalmaprate", BasicAttribute.MagicalAtk, true, 0.252f),
            new("finalpapvalue", BasicAttribute.PhysicalAtk, false, 1),
            new("finalmapvalue", BasicAttribute.MagicalAtk, false, 1),
        ];
        FinalStatusRows.Apply(specialAbility, stats, rows, new Dictionary<string, int> { ["finalAttackPower"] = 252, ["pap"] = 1, ["map"] = 1 });
        Assert.That(stats.Rates[BasicAttribute.PhysicalAtk], Is.EqualTo(0.252f).Within(1e-6));
        Assert.That(stats.Rates[BasicAttribute.MagicalAtk], Is.EqualTo(0.252f).Within(1e-6));
        Assert.That(stats.Values, Is.EqualTo(new Dictionary<BasicAttribute, long> { [BasicAttribute.PhysicalAtk] = 1, [BasicAttribute.MagicalAtk] = 1 }));

        stats = new ClientSpecialAbility.Stats { Values = new Dictionary<BasicAttribute, long> { [BasicAttribute.BonusAtk] = 4120 } };
        rows = [
            new("finalwapminrate", BasicAttribute.MinWeaponAtk, true, -0.9f),
            new("finalwapmaxrate", BasicAttribute.MaxWeaponAtk, true, -0.9f),
        ];
        FinalStatusRows.Apply(specialAbility, stats, rows, new Dictionary<string, int> { ["wapRate"] = -900, ["bap"] = 4120 });
        Assert.That(stats.Rates, Is.EqualTo(new Dictionary<BasicAttribute, float> { [BasicAttribute.MinWeaponAtk] = -0.9f, [BasicAttribute.MaxWeaponAtk] = -0.9f }));
        Assert.That(stats.Values, Is.EqualTo(new Dictionary<BasicAttribute, long> { [BasicAttribute.BonusAtk] = 4120 }));
    }

    // 40500027: finalCapV 50000017 (an id) where the client's level is empty adds nothing; 120030062, which the client
    // does not have, takes the pack's rows (Chosen here).
    [Test]
    public void EmptyClientLevelAddsNothingAndAMissingOneFallsBack() {
        ClientSpecialAbility specialAbility = SpecialAbility();
        var stats = new ClientSpecialAbility.Stats();
        Assert.That(FinalStatusRows.Apply(specialAbility, stats, [new("finalCapV", BasicAttribute.CriticalRate, false, 50000017, Status: true)], new Dictionary<string, int>()), Is.True);
        Assert.That(stats.Values, Is.Empty);

        FinalStatusRows.Row[] rows = [
            new("finalpaprate", BasicAttribute.PhysicalAtk, true, 1),
            new("finalmaprate", BasicAttribute.MagicalAtk, true, 1),
        ];
        Assert.That(FinalStatusRows.Apply(specialAbility, stats, rows, null), Is.False);
        Assert.That(stats.Rates, Is.EqualTo(new Dictionary<BasicAttribute, float> { [BasicAttribute.PhysicalAtk] = 1, [BasicAttribute.MagicalAtk] = 1 }));
    }

    // 40500027 without a played client: its finalCapV 50000017 is an effect id, not a critical rate, and is skipped.
    [Test]
    public void StatusPropertyIdIsNotAStat() {
        var counts = new FinalStatusRows.Counts();
        var stats = new ClientSpecialAbility.Stats();
        FinalStatusRows.Row[] rows = [new("finalCapV", BasicAttribute.CriticalRate, false, 50000017, Status: true)];
        Assert.That(FinalStatusRows.Apply(SpecialAbility(), stats, rows, null, counts), Is.False);
        Assert.That(stats.Values, Is.Empty);
        Assert.That(stats.Rates, Is.Empty);
        Assert.That(counts.StatusRows, Is.EqualTo(1));
    }

    // The level's own hp rate is replaced by the client's hpRate (the client keeps one value per type), not added to.
    [Test]
    public void ClientRowReplacesThePackStat() {
        var counts = new FinalStatusRows.Counts();
        var stats = new ClientSpecialAbility.Stats { Rates = new Dictionary<BasicAttribute, float> { [BasicAttribute.Health] = 0.1f } };
        FinalStatusRows.Row[] rows = [new("finalhprate", BasicAttribute.Health, true, 0.5f)];
        FinalStatusRows.Apply(SpecialAbility(), stats, rows, new Dictionary<string, int> { ["hpRate"] = 500, ["offensive_x"] = 1 }, counts);
        Assert.That(stats.Rates[BasicAttribute.Health], Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(counts.ReplacedPackStats, Is.EqualTo(1));
        Assert.That(counts.DroppedClientRows, Is.EqualTo(1));
    }

    // finalAttackPower is over two attributes: it adds to the level's pap rate rather than replacing it.
    [Test]
    public void TwoAttributeTypeAddsBesideThePackStat() {
        var counts = new FinalStatusRows.Counts();
        var stats = new ClientSpecialAbility.Stats { Rates = new Dictionary<BasicAttribute, float> { [BasicAttribute.PhysicalAtk] = 0.1f } };
        FinalStatusRows.Row[] rows = [new("finalpaprate", BasicAttribute.PhysicalAtk, true, 0.252f)];
        FinalStatusRows.Apply(SpecialAbility(), stats, rows, new Dictionary<string, int> { ["finalAttackPower"] = 252 }, counts);
        Assert.That(stats.Rates[BasicAttribute.PhysicalAtk], Is.EqualTo(0.352f).Within(1e-6));
        Assert.That(stats.Rates[BasicAttribute.MagicalAtk], Is.EqualTo(0.252f).Within(1e-6));
        Assert.That(counts.ReplacedPackStats, Is.EqualTo(0));
    }

    // With the client's level, a StatusProperty id is still left out and counted; only the client's hp rate is added.
    [Test]
    public void StatusPropertyIdIsCountedWithTheClient() {
        var counts = new FinalStatusRows.Counts();
        var stats = new ClientSpecialAbility.Stats();
        FinalStatusRows.Row[] rows = [
            new("finalCapV", BasicAttribute.CriticalRate, false, 50000017, Status: true),
            new("finalhprate", BasicAttribute.Health, true, 0.5f),
        ];
        Assert.That(FinalStatusRows.Apply(SpecialAbility(), stats, rows, new Dictionary<string, int> { ["cap"] = 5, ["hpRate"] = 500 }, counts), Is.True);
        Assert.That(stats.Values, Is.Empty);
        Assert.That(stats.Rates, Is.EqualTo(new Dictionary<BasicAttribute, float> { [BasicAttribute.Health] = 0.5f }));
        Assert.That(counts.StatusRows, Is.EqualTo(1));
        Assert.That(counts.DroppedClientRows, Is.EqualTo(1));
    }

    // A fractional pack value is cut to a whole one and counted.
    [Test]
    public void FractionalPackValueIsCounted() {
        var counts = new FinalStatusRows.Counts();
        var stats = new ClientSpecialAbility.Stats();
        FinalStatusRows.Apply(SpecialAbility(), stats, [new("finalpapvalue", BasicAttribute.PhysicalAtk, false, 2.5f)], null, counts);
        Assert.That(stats.Values[BasicAttribute.PhysicalAtk], Is.EqualTo(2));
        Assert.That(counts.TruncatedValues, Is.EqualTo(1));
    }

    // KR 10700221, an effect only the client has: its one row is aevp 30, which the client only shows (the info
    // dialog's defense score), so the level counts as mapped and adds no stat (before: unmapped, the effect left out).
    [Test]
    public void AShownOnlyTypeDoesNotHoldAnEffect() {
        var rows = new Dictionary<string, int> { ["aevp"] = 30 };
        var unmapped = new List<string>();
        ClientSpecialAbility.Stats stats = PlayedAdditionalRows.SpecialAbilityStats(SpecialAbility(), rows, unmapped);
        Assert.Multiple(() => {
            Assert.That(AdditionalEffectMapper.Mapped(SpecialAbility(), rows), Is.True);
            Assert.That(unmapped, Is.EqualTo(new[] { "aevp" }));
            Assert.That(stats.Values, Is.Empty);
            Assert.That(stats.Rates, Is.Empty);
            Assert.That(stats.SpecialRates, Is.Empty);
        });
    }
}
