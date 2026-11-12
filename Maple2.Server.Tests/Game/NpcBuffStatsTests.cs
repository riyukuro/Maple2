using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// An npc's totals are rebuilt by StatsManager.RebuildNpc, the body of RefreshNpc: its own stats, the enabled held buffs'
// status, Total, the floor, EndReset; true goes to the field as a health update.
public class NpcBuffStatsTests {
    private static readonly Dictionary<BasicAttribute, long> Own = new() {
        { BasicAttribute.Health, 1000 },
        { BasicAttribute.Defense, 200 },
        { BasicAttribute.Evasion, 30 },
        { BasicAttribute.CriticalEvasion, 50 },
        { BasicAttribute.PhysicalAtk, 400 },
    };

    private static int nextId = 1;

    private static (AdditionalEffectMetadata, int) Status(Dictionary<BasicAttribute, long>? values = null, Dictionary<BasicAttribute, float>? rates = null) {
        return (DisabledBuffTests.Effect(nextId++, values: values, rates: rates), 1);
    }

    private static bool Rebuild(Stats stats, bool dead, params (AdditionalEffectMetadata Metadata, int Stacks)[] buffs) {
        var held = new List<Buff>();
        foreach ((AdditionalEffectMetadata metadata, int stacks) in buffs) {
            held.Add(DisabledBuffTests.Held(metadata, true, stacks));
        }
        return StatsManager.RebuildNpc(stats, Own, held, dead);
    }

    private static Stats Npc() => new(new NpcMetadataStat(Own, [], [], [], []));

    [Test]
    public void DebuffsLowerTheNpcStatsAndComeOffWithTheBuff() {
        Stats stats = Npc();
        // 10100181 defense -30 %, 11100242 level 1 evasion -3 and critical evasion -20, 10200061 attack -9 %.
        Rebuild(stats, false,
            Status(rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.Defense, -0.3f } }),
            Status(values: new Dictionary<BasicAttribute, long> { { BasicAttribute.Evasion, -3 }, { BasicAttribute.CriticalEvasion, -20 } }),
            Status(rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.PhysicalAtk, -0.09f } }));
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
        Rebuild(stats, false, (Status(rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.Health, 0.1f } }).Item1, 2));
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1200));
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(600));

        Rebuild(stats, true);
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1000));
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(0));
    }

    [Test]
    public void TheFieldIsToldOnlyOfAMaximumHealthChange() {
        Stats stats = Npc();
        Assert.That(Rebuild(stats, false, Status(rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.Defense, -0.3f } })), Is.False);
        Assert.That(Rebuild(stats, false, Status(rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.Health, 0.1f } })), Is.True);
        // The buff comes off (BuffManager.Remove refreshes a buff with status): the health goes back, and the field is told.
        Assert.That(Rebuild(stats, false), Is.True);
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1000));
    }

    [Test]
    public void ADebuffWithOnlyRatesRefreshesOnRemove() {
        // 10100181 holds only a rate (defense -30 %): its removal must rebuild the npc's totals.
        Assert.That(BuffManager.HasStatus(Status(rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.Defense, -0.3f } }).Item1), Is.True);
        Assert.That(BuffManager.HasStatus(Status().Item1), Is.False);
    }

    [Test]
    public void ADebuffNeverTakesMaximumHealthOrSpeedToZero() {
        var own = new Dictionary<BasicAttribute, long>(Own) { { BasicAttribute.MovementSpeed, 100 } };
        var stats = new Stats(new NpcMetadataStat(own, [], [], [], []));
        stats[BasicAttribute.Health].Add(-400);
        // MovementSpeed -0.5 stacked 3 times (69000504, the data's own) and health -0.4 three times (no one buff in the
        // data reaches -1 of health, several together could): rates past -1. The share of 600 of 1000 rounds to 0, so
        // the living npc keeps 1.
        AdditionalEffectMetadata slow = DisabledBuffTests.Effect(69000504, rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.MovementSpeed, -0.5f } });
        AdditionalEffectMetadata drain = DisabledBuffTests.Effect(40501015, rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.Health, -0.4f } });
        StatsManager.RebuildNpc(stats, own, [DisabledBuffTests.Held(slow, true, 3), DisabledBuffTests.Held(drain, true, 3)], false);
        Assert.That(stats[BasicAttribute.MovementSpeed].Total, Is.EqualTo(1));
        Assert.That(stats[BasicAttribute.MovementSpeed].Current, Is.EqualTo(1));
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1));
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(1));

        // The buffs come off: the health keeps its share of the maximum it had (1 of 1), so it is full again.
        StatsManager.RebuildNpc(stats, own, [], false);
        Assert.That(stats[BasicAttribute.MovementSpeed].Total, Is.EqualTo(100));
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1000));
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(1000));
    }

    [Test]
    public void AnNpcWithNoSpeedOfItsOwnKeepsZero() {
        Stats stats = Npc();
        Rebuild(stats, false, Status(rates: new Dictionary<BasicAttribute, float> { { BasicAttribute.MovementSpeed, -1f } }));
        Assert.That(stats[BasicAttribute.MovementSpeed].Total, Is.EqualTo(0));
    }
}
