using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// An npc's totals are rebuilt as StatsManager.RefreshNpc does: its own stats, the held buffs' status, Total, EndReset.
public class NpcBuffStatsTests {
    private static readonly Dictionary<BasicAttribute, long> Own = new() {
        { BasicAttribute.Health, 1000 },
        { BasicAttribute.Defense, 200 },
        { BasicAttribute.Evasion, 30 },
        { BasicAttribute.CriticalEvasion, 50 },
        { BasicAttribute.PhysicalAtk, 400 },
    };

    private static AdditionalEffectMetadataStatus Status(Dictionary<BasicAttribute, long>? values = null, Dictionary<BasicAttribute, float>? rates = null) {
        return new AdditionalEffectMetadataStatus(values ?? new Dictionary<BasicAttribute, long>(), rates ?? new Dictionary<BasicAttribute, float>(),
            new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>(), new Dictionary<BasicAttribute, float>(),
            false, null, null, 0, false);
    }

    private static void Rebuild(Stats stats, bool dead, params (AdditionalEffectMetadataStatus Status, int Stacks)[] buffs) {
        stats.Reset(Own);
        foreach ((AdditionalEffectMetadataStatus status, int stacks) in buffs) {
            StatsManager.AddStatus(stats, status, stacks);
        }
        stats.Total();
        stats.EndReset(dead);
    }

    private static Stats Npc() => new(new NpcMetadataStat(Own, [], [], [], []));

    [Test]
    public void DebuffsLowerTheNpcStatsAndComeOffWithTheBuff() {
        Stats stats = Npc();
        // 10100181 defense -30 %, 11100242 level 1 evasion -3 and critical evasion -20, 10200061 attack -9 %.
        Rebuild(stats, false,
            (Status(rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.Defense, -0.3f } }), 1),
            (Status(values: new Dictionary<BasicAttribute, long> { { BasicAttribute.Evasion, -3 }, { BasicAttribute.CriticalEvasion, -20 } }), 1),
            (Status(rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.PhysicalAtk, -0.09f } }), 1));
        Assert.That(stats[BasicAttribute.Defense].Total, Is.EqualTo(140));
        Assert.That(stats[BasicAttribute.Evasion].Total, Is.EqualTo(27));
        Assert.That(stats[BasicAttribute.CriticalEvasion].Total, Is.EqualTo(30));
        Assert.That(stats[BasicAttribute.PhysicalAtk].Total, Is.EqualTo(364));

        Rebuild(stats, false);
        Assert.That(stats[BasicAttribute.Defense].Total, Is.EqualTo(200));
        Assert.That(stats[BasicAttribute.Evasion].Total, Is.EqualTo(30));
        Assert.That(stats[BasicAttribute.PhysicalAtk].Total, Is.EqualTo(400));
    }

    [Test]
    public void StacksCountAndHealthKeepsItsShare() {
        Stats stats = Npc();
        stats[BasicAttribute.Health].Add(-500);
        Rebuild(stats, false, (Status(rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.Health, 0.1f } }), 2));
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1200));
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(600));

        Rebuild(stats, true);
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1000));
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(0));
    }
}
