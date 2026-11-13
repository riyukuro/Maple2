using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

// What a massive event pays a guild at its level (give_guild_exp type 2).
public class GuildGainExpRulesTests {
    // The first three KR guildproperty.xml rows: accumExp 10000, 30000, 70000; loseMiniGameGuildExp 2000, 2500,
    // 3000; loseMiniGameGuildFund 400000, 500000, 600000.
    private static readonly Dictionary<short, GuildTable.Property> Properties = new() {
        [3] = Row(3, 70000, 3000, 600000),
        [1] = Row(1, 10000, 2000, 400000),
        [2] = Row(2, 30000, 2500, 500000),
    };

    private static GuildTable.Property Row(short level, int accumExp, int loseExp, int loseFund) {
        var property = (GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property));
        return property with {
            Level = level,
            Experience = accumExp,
            WinMiniGameExp = loseExp * 2,
            LoseMiniGameExp = loseExp,
            WinMiniGameFund = loseFund * 5,
            LoseMiniGameFund = loseFund,
        };
    }

    [Test]
    public void GainExpFund_MassiveEventPaysTheLoseMiniGameRow() {
        Assert.That(GuildRules.GainExpFund(Properties[2], GuildRules.MassiveEventGainExp, InstanceType.massiveEvent), Is.EqualTo((2500, 500000)));
        Assert.That(GuildRules.GainExpFund(Properties[1], 2, InstanceType.massiveEvent), Is.EqualTo((2000, 400000)));
    }

    // Only a massive event field pays: an arena (no instancefield row) or another instance type does not.
    [Test]
    public void GainExpFund_OnlyMassiveEventFieldsPay() {
        Assert.That(GuildRules.GainExpFund(Properties[1], 2, null), Is.EqualTo((0, 0)));
        Assert.That(GuildRules.GainExpFund(Properties[1], 2, InstanceType.FieldWar), Is.EqualTo((0, 0)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void GainExpFund_OtherTypesPayNothing(int type) {
        Assert.That(GuildRules.GainExpFund(Properties[1], type, InstanceType.massiveEvent), Is.EqualTo((0, 0)));
    }

    [Test]
    public void GainExpFund_NoRowPaysNothing() {
        Assert.That(GuildRules.GainExpFund(null, GuildRules.MassiveEventGainExp, InstanceType.massiveEvent), Is.EqualTo((0, 0)));
    }
}
