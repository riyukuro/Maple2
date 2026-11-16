using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using Maple2.File.Ingest.Mapper;
using Maple2.File.Parser.Xml.AdditionalEffect;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using NUnit.Framework;

namespace Maple2.Server.Tests.Ingest;

// An additional effect's StatusProperty <Stat> line (KR: <StatusProperty><Stat sp_rgpvalue="1" /></StatusProperty>
// on 10200081) as the ingest takes it into the effect's status values and rates: each stat name of the data lands on
// the basic attribute of that name, the one the server's stats and damage read (asp attack speed, par physical
// resistance, wapmax the weapon attack's top, ...). Each name gets its own number here, so a line read into the wrong
// attribute shows. The ingest also asks the parser for BasicAttribute.Unknown (30, the data pack's unnamed slot after
// dmg), and the parser answers it with the dmg line, so a dmg line lands on both Damage and Unknown. No KR effect has a
// dmg line, and no server code reads Unknown from an effect, so it is pinned here as it is.
public class EffectStatLineTests {
    private static readonly (string Name, BasicAttribute Attribute)[] Names = [
        ("str", BasicAttribute.Strength), ("dex", BasicAttribute.Dexterity), ("int", BasicAttribute.Intelligence),
        ("luk", BasicAttribute.Luck), ("hp", BasicAttribute.Health), ("hp_rgp", BasicAttribute.HpRegen),
        ("hp_inv", BasicAttribute.HpRegenInterval), ("sp", BasicAttribute.Spirit), ("sp_rgp", BasicAttribute.SpRegen),
        ("sp_inv", BasicAttribute.SpRegenInterval), ("ep", BasicAttribute.Stamina), ("ep_rgp", BasicAttribute.StaminaRegen),
        ("ep_inv", BasicAttribute.StaminaRegenInterval), ("asp", BasicAttribute.AttackSpeed), ("msp", BasicAttribute.MovementSpeed),
        ("atp", BasicAttribute.Accuracy), ("evp", BasicAttribute.Evasion), ("cap", BasicAttribute.CriticalRate),
        ("cad", BasicAttribute.CriticalDamage), ("car", BasicAttribute.CriticalEvasion), ("ndd", BasicAttribute.Defense),
        ("abp", BasicAttribute.PerfectGuard), ("jmp", BasicAttribute.JumpHeight), ("pap", BasicAttribute.PhysicalAtk),
        ("map", BasicAttribute.MagicalAtk), ("par", BasicAttribute.PhysicalRes), ("mar", BasicAttribute.MagicalRes),
        ("wapmin", BasicAttribute.MinWeaponAtk), ("wapmax", BasicAttribute.MaxWeaponAtk), ("dmg", BasicAttribute.Damage),
        ("pen", BasicAttribute.Piercing), ("rmsp", BasicAttribute.MountSpeed), ("bap", BasicAttribute.BonusAtk),
        ("bap_pet", BasicAttribute.PetBonusAtk),
    ];

    private static AdditionalEffectMetadataStatus Convert(string statLine, string defensive = "<DefensiveProperty />") {
        var status = (StatusProperty) new XmlSerializer(typeof(StatusProperty), new XmlRootAttribute("StatusProperty"))
            .Deserialize(new StringReader($"<StatusProperty>{statLine}</StatusProperty>"))!;
        var defensiveProperty = (DefensiveProperty) new XmlSerializer(typeof(DefensiveProperty), new XmlRootAttribute("DefensiveProperty"))
            .Deserialize(new StringReader(defensive))!;
        MethodInfo convert = typeof(AdditionalEffectMapper).GetMethod("Convert", BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(StatusProperty), typeof(OffensiveProperty), typeof(DefensiveProperty)])!;
        return (AdditionalEffectMetadataStatus) convert.Invoke(null, [status, new OffensiveProperty(), defensiveProperty])!;
    }

    // DefensiveProperty papDamageV / mapDamageV (KR: only 10800283, Secret Art: Mark's 3-hit debuff, -60 at each
    // level; its text: "decreases physical and magic resistance") are the holder's physical and magical resistance
    // values, added to a Stat line's par / mar value. The rates (papDamageR / mapDamageR) stay damage taken rates.
    [Test]
    public void DefensiveDamageValuesAreResistanceValues() {
        AdditionalEffectMetadataStatus mark = Convert("", "<DefensiveProperty papDamageV=\"-60\" mapDamageV=\"-60\" />");
        Assert.That(mark.Values, Is.EquivalentTo(new Dictionary<BasicAttribute, long> {
            [BasicAttribute.PhysicalRes] = -60, [BasicAttribute.MagicalRes] = -60,
        }));

        AdditionalEffectMetadataStatus both = Convert("<Stat parvalue=\"100\" />", "<DefensiveProperty papDamageV=\"-45\" papDamageR=\"0.3\" />");
        Assert.That(both.Values, Is.EquivalentTo(new Dictionary<BasicAttribute, long> { [BasicAttribute.PhysicalRes] = 55 }));
        Assert.That(both.PhysicalDamageTakenRate, Is.EqualTo(0.3f).Within(1e-6f));
    }

    [Test]
    public void EachStatValueLandsOnItsAttribute() {
        string line = "<Stat " + string.Join(" ", Names.Select((entry, i) => $"{entry.Name}value=\"{i + 1}\"")) + " />";
        AdditionalEffectMetadataStatus status = Convert(line);
        Dictionary<BasicAttribute, long> expected = Names.Select((entry, i) => (entry.Attribute, Value: (long) (i + 1)))
            .ToDictionary(entry => entry.Attribute, entry => entry.Value);
        expected[BasicAttribute.Unknown] = expected[BasicAttribute.Damage];
        Assert.That(status.Values, Is.EquivalentTo(expected));
        Assert.That(status.Rates, Is.Empty);
    }

    [Test]
    public void EachStatRateLandsOnItsAttribute() {
        string line = "<Stat " + string.Join(" ", Names.Select((entry, i) => $"{entry.Name}rate=\"{(i + 1) / 100f}\"")) + " />";
        AdditionalEffectMetadataStatus status = Convert(line);
        Assert.That(status.Values, Is.Empty);
        foreach (((string name, BasicAttribute attribute), int i) in Names.Select((entry, i) => (entry, i))) {
            Assert.That(status.Rates.GetValueOrDefault(attribute), Is.EqualTo((i + 1) / 100f).Within(1e-6f), name);
        }
        Assert.That(status.Rates.GetValueOrDefault(BasicAttribute.Unknown), Is.EqualTo(status.Rates[BasicAttribute.Damage]));
        Assert.That(status.Rates, Has.Count.EqualTo(Names.Length + 1));
    }

    // The Mushking Royale kill exp buffs (KR 70000175, 70000176 and 70000178-183: improve_survival_kill_exprate 5 to
    // 50, icons SurvivalKillExp_5 .. _50) carry the rate in percent on the special rate ImproveSurvivalKillExp. The
    // time exp rate beside it is not taken: the game's exp has no time part.
    [Test]
    public void SurvivalKillExpRateIsTaken() {
        AdditionalEffectMetadataStatus kill = Convert("<SpecialAbility improve_survival_kill_exprate=\"10\" />");
        Assert.That(kill.SpecialRates, Is.EquivalentTo(new Dictionary<SpecialAttribute, float> { [SpecialAttribute.ImproveSurvivalKillExp] = 10f }));
        Assert.That(Convert("<SpecialAbility improve_survival_time_exprate=\"10\" />").SpecialRates, Is.Empty);
    }

}
