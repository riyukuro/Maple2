using Maple2.Model.Enum;
using Maple2.Server.Game.Util;
using static Maple2.Server.Game.Util.InsigniaRules;

namespace Maple2.Server.Tests.Game.Util;

// The shown flag of a name-tag symbol, by the conditions of the client's CheckCondition.
public class InsigniaRulesTests {
    private static readonly Facts Plain = new(false, 0, 0, false, 1, false, false);

    [Test]
    public void PlainPlayer_ShowsNothing() {
        foreach (InsigniaConditionType type in System.Enum.GetValues<InsigniaConditionType>()) {
            Assert.That(Shown(type, Plain), Is.False, type.ToString());
        }
    }

    [Test]
    public void EachConditionAtItsLimit() {
        Assert.That(Shown(InsigniaConditionType.level, Plain with { Level = 49 }), Is.False);
        Assert.That(Shown(InsigniaConditionType.level, Plain with { Level = 50 }), Is.True);
        Assert.That(Shown(InsigniaConditionType.trophy_point, Plain with { TrophyTotal = 999 }), Is.False);
        Assert.That(Shown(InsigniaConditionType.trophy_point, Plain with { TrophyTotal = 1000 }), Is.True);
        Assert.That(Shown(InsigniaConditionType.adventure_level, Plain with { PrestigeLevel = 99 }), Is.False);
        Assert.That(Shown(InsigniaConditionType.adventure_level, Plain with { PrestigeLevel = 100 }), Is.True);
        Assert.That(Shown(InsigniaConditionType.title, Plain with { HasTitle = true }), Is.True);
        Assert.That(Shown(InsigniaConditionType.enchant, Plain with { HighEnchant = true }), Is.True);
        Assert.That(Shown(InsigniaConditionType.vip, Plain with { Vip = true }), Is.True);
    }

    [Test]
    public void GmSymbol_NeedsAGmAccount() {
        Assert.That(Shown(InsigniaConditionType.GM, Plain with { Gm = true }), Is.True);
        Assert.That(Shown(InsigniaConditionType.GM, Plain with { Level = 99, Vip = true }), Is.False);
        // Being a GM shows no other symbol.
        Assert.That(Shown(InsigniaConditionType.level, Plain with { Gm = true }), Is.False);
    }

    [Test]
    public void TypesWithoutAServerRule_AreNotShown() {
        Facts all = new(true, 999, 99999, true, 99, true, true);
        foreach (InsigniaConditionType type in new[] { InsigniaConditionType.none, InsigniaConditionType.tencentvip, InsigniaConditionType.TGP, InsigniaConditionType.Burning, InsigniaConditionType.survivallevel }) {
            Assert.That(Handled(type), Is.False, type.ToString());
            Assert.That(Shown(type, all), Is.False, type.ToString());
        }
    }
}
