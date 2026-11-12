using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.Util;

// Applying a buff again: which buffs restart, where the procs of the restarted buff fall, and the stack counts.
public class BuffReapplyTests {
    private static AdditionalEffectMetadataProperty Property(int duration, int interval, BuffResetCondition reset, BuffDotCondition dot, int maxCount = 1) {
        return new AdditionalEffectMetadataProperty(BuffType.Buff, BuffSubType.Status, BuffCategory.None, BuffEventType.None, 0,
            duration, interval, 0, maxCount, false, false, false, false, false, false, false, 0, 0, 0, 0, 0,
            BuffKeepCondition.TimerDuration, reset, dot, 0);
    }

    // The ticks a buff procs at when it starts at start and nothing ends it early, by the rules Buff.Proc uses.
    private static System.Collections.Generic.List<long> Procs(AdditionalEffectMetadataProperty property, long start) {
        long interval = property.IntervalTick > 0 ? property.IntervalTick : property.DurationTick + 1000;
        long end = start + property.DurationTick;
        int limit = Buff.ProcLimit(property, property.DurationTick, interval);
        var ticks = new System.Collections.Generic.List<long>();
        for (long next = Buff.FirstProcTick(property, start, interval); next <= end && ticks.Count < limit; next += interval) {
            ticks.Add(next);
        }
        return ticks;
    }

    [Test]
    public void ResetConditionsThatRestart() {
        Assert.That(Buff.RestartsOnReapply(Property(8000, 1000, BuffResetCondition.ResetEndTick, BuffDotCondition.Default), 8000), Is.True);
        Assert.That(Buff.RestartsOnReapply(Property(8000, 1000, BuffResetCondition.Reset2, BuffDotCondition.Default, 5), 8000), Is.True);
        Assert.That(Buff.RestartsOnReapply(Property(8000, 1000, BuffResetCondition.PersistEndTick, BuffDotCondition.Default), 8000), Is.False);
        Assert.That(Buff.RestartsOnReapply(Property(8000, 1000, BuffResetCondition.Replace, BuffDotCondition.Default), 8000), Is.False);
    }

    [Test]
    public void NoDurationKeepsItsStart() {
        Assert.That(Buff.RestartsOnReapply(Property(0, 0, BuffResetCondition.Reset2, BuffDotCondition.Default, 10), 0), Is.False);
        // A buff whose duration a skill lengthened restarts with the lengthened one.
        Assert.That(Buff.RestartsOnReapply(Property(0, 0, BuffResetCondition.ResetEndTick, BuffDotCondition.Default), 5000), Is.True);
    }

    [Test]
    public void AuraHealProcsOnEveryApplication() {
        // 1.1 s long, no interval, applied again every second: one heal per application, at the application.
        AdditionalEffectMetadataProperty aura = Property(1100, 0, BuffResetCondition.ResetEndTick, BuffDotCondition.Default);
        Assert.That(Procs(aura, 10_000), Is.EqualTo(new long[] { 10_000 }));
        Assert.That(Procs(aura, 11_000), Is.EqualTo(new long[] { 11_000 }));
    }

    [Test]
    public void RestartedDotRunsItsFullCount() {
        // An 8 s dot with a 1 s interval applied again 3 s in: 8 more hits from the new start.
        AdditionalEffectMetadataProperty dot = Property(8000, 1000, BuffResetCondition.ResetEndTick, BuffDotCondition.Default);
        System.Collections.Generic.List<long> procs = Procs(dot, 13_000);
        Assert.That(procs, Has.Count.EqualTo(8));
        Assert.That(procs[0], Is.EqualTo(13_000));
        Assert.That(procs[^1], Is.EqualTo(20_000));
    }

    [Test]
    public void RestartedIntervalBuffWaitsOneInterval() {
        // A 28 s herb that heals every second, used again: the next heal one second after the new start, 28 in all.
        AdditionalEffectMetadataProperty herb = Property(28000, 1000, BuffResetCondition.ResetEndTick, BuffDotCondition.OnInterval);
        System.Collections.Generic.List<long> procs = Procs(herb, 50_000);
        Assert.That(procs, Has.Count.EqualTo(28));
        Assert.That(procs[0], Is.EqualTo(51_000));
    }

    [Test]
    public void StacksStartAtOne() {
        Assert.That(Buff.AddStacks(0, 1), Is.EqualTo(1));
        Assert.That(Buff.AddStacks(0, 10), Is.EqualTo(1));
        Assert.That(Buff.AddStacks(1, 10), Is.EqualTo(1));
    }

    [Test]
    public void StacksStayInsideTheMaximum() {
        Assert.That(Buff.AddStacks(3, 10), Is.EqualTo(3));
        Assert.That(Buff.AddStacks(12, 10), Is.EqualTo(10));
        Assert.That(Buff.AddStacks(-2, 10), Is.EqualTo(1));
    }

    [Test]
    public void NoMaximumNoStacks() {
        Assert.That(Buff.AddStacks(0, 0), Is.EqualTo(0));
        Assert.That(Buff.AddStacks(4, 0), Is.EqualTo(0));
    }
}
