using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class SkillCastRulesTests {
    private static AdditionalEffectMetadataProperty Property(int duration, int interval, BuffDotCondition condition) {
        return new AdditionalEffectMetadataProperty(BuffType.Buff, BuffSubType.Status, BuffCategory.None, BuffEventType.None, 0,
            duration, interval, 0, 1, false, false, false, false, false, false, false, 0, 0, 0, 0, 0,
            BuffKeepCondition.TimerDuration, BuffResetCondition.ResetEndTick, condition, 0);
    }

    private static int Procs(int duration, int interval, BuffDotCondition condition = BuffDotCondition.Default) {
        AdditionalEffectMetadataProperty property = Property(duration, interval, condition);
        // The buff's own interval: one past the duration when the data has none.
        long intervalTick = interval > 0 ? interval : duration + 1000;
        return Buff.ProcLimit(property, duration, intervalTick);
    }

    [Test]
    public void InstantEffectProcsOnce() {
        // A potion: 100 ms long, 100 ms interval.
        Assert.That(Procs(100, 100), Is.EqualTo(1));
        Assert.That(Procs(200, 150), Is.EqualTo(1));
        Assert.That(Procs(100, 0), Is.EqualTo(1));
        Assert.That(Procs(0, 100), Is.EqualTo(1));
    }

    [Test]
    public void DotProcsDurationOverInterval() {
        Assert.That(Procs(8000, 1000), Is.EqualTo(8));
        Assert.That(Procs(4000, 1000), Is.EqualTo(4));
        Assert.That(Procs(11000, 1000), Is.EqualTo(11));
        Assert.That(Procs(25000, 1500), Is.EqualTo(16));
    }

    [Test]
    public void ProcsStopAtTheLimit() {
        // The ticks of an 8 s dot with a 1 s interval: 0 to 7 s; the tick at 8 s, still inside the end tick, is not made.
        AdditionalEffectMetadataProperty property = Property(8000, 1000, BuffDotCondition.Default);
        long start = 10_000, end = start + 8000;
        long next = Buff.FirstProcTick(property, start, 1000);
        int count = 0;
        long last = 0;
        while (true) {
            last = next;
            count++;
            next += 1000;
            if (next > end || count >= Buff.ProcLimit(property, end - start, 1000)) {
                break;
            }
        }
        Assert.That(count, Is.EqualTo(8));
        Assert.That(last, Is.EqualTo(start + 7000));
    }

    [Test]
    public void OnIntervalBuffHasNoCountLimit() {
        // A herb, 28 s with a 1 s interval, first tick after one interval: its 28 ticks end with the end tick.
        Assert.That(Procs(28000, 1000, BuffDotCondition.OnInterval), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void DeadCastNeedsAllowDead() {
        Assert.That(SkillCastRules.RefusedDead(true, false), Is.True);
        Assert.That(SkillCastRules.RefusedDead(true, true), Is.False);
        Assert.That(SkillCastRules.RefusedDead(false, false), Is.False);
    }

    [Test]
    public void OwnCooldownRefusesUntilSlack() {
        // Used at 0 with a 5 s cooldown.
        SkillCooldown[] cooldowns = [new SkillCooldown(90000036, 1) { EndTick = 5000, GroupId = 90000001 }];
        Assert.That(SkillCastRules.BlockingCooldown(cooldowns, 90000036, 90000001, 2000), Is.SameAs(cooldowns[0]));
        Assert.That(SkillCastRules.BlockingCooldown(cooldowns, 90000036, 90000001, 3499), Is.SameAs(cooldowns[0]));
        Assert.That(SkillCastRules.BlockingCooldown(cooldowns, 90000036, 90000001, 3500), Is.Null);
        Assert.That(SkillCastRules.BlockingCooldown(cooldowns, 90000036, 90000001, 4000), Is.Null);
        Assert.That(SkillCastRules.BlockingCooldown(cooldowns, 90000036, 90000001, 6000), Is.Null);
    }

    [Test]
    public void GroupCooldownIsShared() {
        SkillCooldown[] cooldowns = [
            new SkillCooldown(90000032, 1) { EndTick = 5000, GroupId = 90000001 },
            new SkillCooldown(90000038, 1) { EndTick = 9000, GroupId = 0 },
        ];
        // Another potion of the group is refused; a herb (no group) and a skill of another group are not.
        Assert.That(SkillCastRules.BlockingCooldown(cooldowns, 90000036, 90000001, 1000), Is.SameAs(cooldowns[0]));
        Assert.That(SkillCastRules.BlockingCooldown(cooldowns, 90000039, 0, 1000), Is.Null);
        Assert.That(SkillCastRules.BlockingCooldown(cooldowns, 90000010, 90000005, 1000), Is.Null);
        Assert.That(SkillCastRules.BlockingCooldown(cooldowns, 90000038, 0, 1000), Is.SameAs(cooldowns[1]));
    }

    [Test]
    public void LatestBlockingCooldownIsReported() {
        SkillCooldown[] cooldowns = [
            new SkillCooldown(90000032, 1) { EndTick = 5000, GroupId = 90000001 },
            new SkillCooldown(90000033, 1) { EndTick = 7000, GroupId = 90000001 },
        ];
        Assert.That(SkillCastRules.BlockingCooldown(cooldowns, 90000032, 90000001, 1000), Is.SameAs(cooldowns[1]));
    }

    [Test]
    public void ItemIsTakenOncePerCast() {
        Assert.That(SkillCastRules.ConsumesItem(true, false), Is.True);
        Assert.That(SkillCastRules.ConsumesItem(true, true), Is.False);
        Assert.That(SkillCastRules.ConsumesItem(false, false), Is.False);
    }

    [Test]
    public void ResistanceMatchesTheAttackStat() {
        Assert.That(SkillCastRules.ResistanceOf(BasicAttribute.PhysicalAtk), Is.EqualTo(BasicAttribute.PhysicalRes));
        Assert.That(SkillCastRules.ResistanceOf(BasicAttribute.MagicalAtk), Is.EqualTo(BasicAttribute.MagicalRes));
    }
}
