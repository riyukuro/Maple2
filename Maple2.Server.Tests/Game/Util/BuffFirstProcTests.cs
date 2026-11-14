using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.Util;

// A buff's first proc: dotCondition 0 at the add (after the delay), 1 one interval later.
public class BuffFirstProcTests {
    private static AdditionalEffectMetadataProperty Property(int duration, int interval, int delay, BuffDotCondition condition) {
        return new AdditionalEffectMetadataProperty(BuffType.Buff, BuffSubType.Status, BuffCategory.None, BuffEventType.None, 0,
            duration, interval, delay, 1, false, false, false, false, false, false, false, 0, 0, 0, 0, 0,
            BuffKeepCondition.TimerDuration, BuffResetCondition.ResetEndTick, condition, 0);
    }

    [Test]
    public void IntervalZeroDotProcsOnAdd() {
        // An electric proc of 100 ms with no interval: its one dot lands with the add, inside the buff's duration.
        AdditionalEffectMetadataProperty property = Property(100, 0, 0, BuffDotCondition.Default);
        long start = 50_000;
        long first = Buff.FirstProcTick(property, start, property.DurationTick + 1000);
        Assert.That(first, Is.EqualTo(start));
        Assert.That(first, Is.LessThanOrEqualTo(start + property.DurationTick));
    }

    [Test]
    public void SplashOnAddThenEveryInterval() {
        // A shield whose splash procs every second: the first splash comes with the add.
        AdditionalEffectMetadataProperty property = Property(9000, 1000, 0, BuffDotCondition.Default);
        Assert.That(Buff.FirstProcTick(property, 10_000, property.IntervalTick), Is.EqualTo(10_000));
    }

    [Test]
    public void DelayStillApplies() {
        AdditionalEffectMetadataProperty property = Property(5000, 1000, 300, BuffDotCondition.Default);
        Assert.That(Buff.FirstProcTick(property, 10_000, property.IntervalTick), Is.EqualTo(10_300));
    }

    [Test]
    public void OnIntervalWaitsOneInterval() {
        AdditionalEffectMetadataProperty property = Property(5000, 800, 0, BuffDotCondition.OnInterval);
        Assert.That(Buff.FirstProcTick(property, 10_000, property.IntervalTick), Is.EqualTo(10_800));
        AdditionalEffectMetadataProperty delayed = Property(5000, 800, 300, BuffDotCondition.OnInterval);
        Assert.That(Buff.FirstProcTick(delayed, 10_000, delayed.IntervalTick), Is.EqualTo(11_100));
    }

    [Test]
    public void NonDefaultConditionWithoutIntervalNeverProcs() {
        // The client makes a level with dotCondition other than 0 and no interval no dot, recovery, exp or dot buff
        // (AdditionalLevelData::IsDotDamage and the others). In KR five levels have it (40443002, 40500016, 40600016:
        // dotCondition 1, 2.5 s; 40500035 and 40600035 level 3: dotCondition 2, 1 s), none with such values; the
        // server's first proc of such a level falls after its end, so it never procs.
        foreach ((int duration, BuffDotCondition condition) in new[] { (2500, BuffDotCondition.OnInterval), (1000, BuffDotCondition.Stack) }) {
            AdditionalEffectMetadataProperty property = Property(duration, 0, 0, condition);
            long start = 50_000;
            Assert.That(Buff.FirstProcTick(property, start, property.DurationTick + 1000), Is.GreaterThan(start + duration));
        }
    }
}
