using System;
using System.Collections.Generic;
using System.Linq;
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

    [Test]
    public void LongCooldownTakesACastHalfASecondEarly() {
        Assert.That(SkillCastRules.RequiredGap(8000), Is.EqualTo(7500));
        Assert.That(SkillCastRules.RequiredGap(1000), Is.EqualTo(500));
        Assert.That(SkillCastRules.RequiredGap(0), Is.EqualTo(0));
        SkillCastRules.LastCast[] casts = [new SkillCastRules.LastCast(10900061, 0, 8000, 100_000, 50_000)];
        Assert.That(SkillCastRules.CooldownRefuses(casts, 10900061, 0, 57_499, 107_499), Is.True);
        Assert.That(SkillCastRules.CooldownRefuses(casts, 10900061, 0, 57_500, 107_500), Is.False);
        Assert.That(SkillCastRules.CooldownRefuses(casts, 10900061, 0, 58_000, 108_000), Is.False);
        // Another skill without a group is not held back.
        Assert.That(SkillCastRules.CooldownRefuses(casts, 10900081, 0, 50_100, 100_100), Is.False);
    }

    [Test]
    public void ShortCooldownTakesThreeQuarters() {
        Assert.That(SkillCastRules.RequiredGap(800), Is.EqualTo(600));
        Assert.That(SkillCastRules.RequiredGap(400), Is.EqualTo(300));
        SkillCastRules.LastCast[] casts = [new SkillCastRules.LastCast(10900001, 0, 800, 100_000, 50_000)];
        Assert.That(SkillCastRules.CooldownRefuses(casts, 10900001, 0, 50_599, 100_599), Is.True);
        Assert.That(SkillCastRules.CooldownRefuses(casts, 10900001, 0, 50_600, 100_600), Is.False);
    }

    [Test]
    public void CastGapIsNotLengthenedByTheClientTick() {
        // The client's tick says 8 s, the arrivals 1 s apart: the arrivals count, plus the allowance.
        Assert.That(SkillCastRules.CastGap(58_000, 50_000, 101_000, 100_000), Is.EqualTo(1000 + SkillCastRules.ArrivalAllowanceMs));
        // Arrivals bunched by up to the allowance do not shorten the client's own gap.
        Assert.That(SkillCastRules.CastGap(58_000, 50_000, 107_600, 100_000), Is.EqualTo(8000));
        // A tick that runs backwards is left out.
        Assert.That(SkillCastRules.CastGap(40_000, 50_000, 108_000, 100_000), Is.EqualTo(8000 + SkillCastRules.ArrivalAllowanceMs));
        // The 32-bit tick wraps.
        Assert.That(SkillCastRules.CastGap(int.MinValue + 3999, int.MaxValue - 4000, 108_000, 100_000), Is.EqualTo(8000));
        SkillCastRules.LastCast[] casts = [new SkillCastRules.LastCast(10900061, 0, 8000, 100_000, 50_000)];
        Assert.That(SkillCastRules.CooldownRefuses(casts, 10900061, 0, 58_000, 101_000), Is.True);
    }

    [Test]
    public void CooldownGroupHoldsBackItsOtherSkills() {
        SkillCastRules.LastCast[] casts = [new SkillCastRules.LastCast(10300021, 77, 6000, 100_000, 50_000)];
        Assert.That(SkillCastRules.CooldownRefuses(casts, 10300031, 77, 52_000, 102_000), Is.True);
        Assert.That(SkillCastRules.CooldownRefuses(casts, 10300031, 77, 55_500, 105_500), Is.False);
        Assert.That(SkillCastRules.CooldownRefuses(casts, 10300031, 78, 52_000, 102_000), Is.False);
        Assert.That(SkillCastRules.CooldownRefuses([], 10300031, 77, 52_000, 102_000), Is.False);
    }

    [Test]
    public void JobSkillsAreTheTreeAndTheJobIds() {
        Assert.That(SkillCastRules.IsJobSkill(10900061, false), Is.True);
        Assert.That(SkillCastRules.IsJobSkill(1000001, true), Is.True);
        Assert.That(SkillCastRules.IsJobSkill(90000036, false), Is.False);
        Assert.That(SkillCastRules.IsJobSkill(12000001, false), Is.False);
    }

    [Test]
    public void OnlyAHeldSkillPaysAtASync() {
        Assert.That(SkillCastRules.SyncPays(true), Is.True);
        Assert.That(SkillCastRules.SyncPays(false), Is.False);
    }

    [Test]
    public void HealthCostMustLeaveHealth() {
        Assert.That(SkillCastRules.HpCost(1000, -0.01f), Is.EqualTo(10));
        Assert.That(SkillCastRules.HpCost(50, -0.01f), Is.EqualTo(0));
        Assert.That(SkillCastRules.HpCost(1000, 0f), Is.EqualTo(0));
        Assert.That(SkillCastRules.HpCostRefused(1000, 10), Is.False);
        Assert.That(SkillCastRules.HpCostRefused(50, 0), Is.False);
        Assert.That(SkillCastRules.HpCostRefused(10, 10), Is.True);
        Assert.That(SkillCastRules.HpCostRefused(10, 25), Is.True);
    }

    private static JobTable.Skill Tree(int main, short maxLevel, params int[] sub) => new(main, sub, maxLevel, 0);

    private static JobTable.Entry Entry(JobTable.Skill[] basic, JobTable.Skill[] awakening, params int[] baseSkills) {
        return new JobTable.Entry(null!, new Dictionary<SkillRank, JobTable.Skill[]> {
            [SkillRank.Basic] = basic,
            [SkillRank.Awakening] = awakening,
        }, baseSkills);
    }

    // The Runeblade's and the Knight's trees of the job table.
    private static readonly JobTable.Entry RuneBlader = Entry([
        Tree(20000001, 1), Tree(20000011, 1), Tree(1000001, 1, 1000003, 1000005, 1000007), Tree(1000011, 1, 1000012, 1000013, 1000014),
        Tree(10900041, 1), Tree(10900051, 1), Tree(10900031, 10, 10900032, 10900033, 10900034),
        Tree(10900151, 10, 10900153, 10900155, 10900157), Tree(10900001, 10), Tree(10900061, 10, 10900062, 10900063, 10900065),
        Tree(10900081, 10), Tree(10900071, 10, 10900072, 10900074, 10900076), Tree(10900011, 10), Tree(10900111, 10),
        Tree(10900091, 10, 10900092, 10900094, 10900095), Tree(10900121, 10), Tree(10900021, 10), Tree(10900141, 10),
        Tree(10900131, 10), Tree(10900101, 10, 10900103, 10900105, 10900107), Tree(10900161, 10, 10900163),
    ], [
        Tree(10900211, 5, 10900212, 10900213, 10900214), Tree(10900221, 5, 10900222, 10900223, 10900225), Tree(10900241, 5),
        Tree(10900181, 5), Tree(10900271, 5), Tree(10900261, 5), Tree(10900201, 5, 10900203, 10900205, 10900207),
        Tree(10900171, 5, 10900173, 10900175, 10900177), Tree(10900251, 5), Tree(10900191, 5, 10900193, 10900196, 10900198),
        Tree(10900281, 5), Tree(10900291, 5, 10900292, 10900293, 10900294), Tree(10900231, 1, 10900232),
    ], 1000001, 1000011, 10900031, 10900041, 10900051, 10900151, 10900231, 20000001, 20000011);

    private static readonly JobTable.Entry Knight = Entry([
        Tree(20000001, 1), Tree(20000011, 1), Tree(10100061, 1, 10100062), Tree(10100091, 1), Tree(10100001, 10), Tree(10100011, 10),
        Tree(10100151, 10), Tree(10100021, 10), Tree(10100161, 10, 10100162),
    ], [Tree(10100171, 5), Tree(10100231, 1)], 10100001, 10100011, 10100061, 10100091, 10100231, 20000001, 20000011);

    private static readonly JobTable Jobs = new(new Dictionary<JobCode, JobTable.Entry> {
        [JobCode.RuneBlader] = RuneBlader,
        [JobCode.Knight] = Knight,
    });

    // A first-job Runeblade's tree with these points on top of its base skills, as SkillInfo holds it.
    private static void AssertRunebladeCasts(Dictionary<int, short> points) {
        HashSet<int> treeSkills = SkillCastRules.TreeSkills(Jobs);
        Dictionary<int, int> mainOf = SkillCastRules.MainSkills(RuneBlader, awakening: false);
        var levels = new Dictionary<int, short>();
        foreach (JobTable.Skill skill in RuneBlader.Skills[SkillRank.Basic]) {
            short level = (short) (RuneBlader.BaseSkills.Contains(skill.Main) ? 1 : 0);
            levels[skill.Main] = (short) Math.Min(level + points.GetValueOrDefault(skill.Main), skill.MaxLevel);
        }
        short? TreeLevel(int skillId) => mainOf.TryGetValue(skillId, out int main) ? levels[main] : null;

        foreach (JobTable.Skill skill in RuneBlader.Skills[SkillRank.Basic]) {
            foreach (int skillId in skill.Sub.Prepend(skill.Main)) {
                short learned = levels[skill.Main];
                Assert.That(treeSkills, Does.Contain(skillId));
                for (short level = 1; level <= learned; level++) {
                    Assert.That(SkillCastRules.TreeRefuses(true, TreeLevel(skillId), level), Is.False, $"{skillId} level {level}");
                }
                Assert.That(SkillCastRules.TreeRefuses(true, TreeLevel(skillId), (short) (learned + 1)), Is.True, $"{skillId} above {learned}");
            }
        }
        // The awakening rank is not in a first job's tree; another job's skill is in none of its own.
        Assert.That(SkillCastRules.TreeRefuses(treeSkills.Contains(10900211), TreeLevel(10900211), 1), Is.True);
        Assert.That(SkillCastRules.TreeRefuses(treeSkills.Contains(10100001), TreeLevel(10100001), 1), Is.True);
        Assert.That(SkillCastRules.TreeRefuses(treeSkills.Contains(10100162), TreeLevel(10100162), 1), Is.True);
        // Skills of no tree are never refused: an item skill, a mount skill.
        Assert.That(SkillCastRules.TreeRefuses(treeSkills.Contains(90000036), TreeLevel(90000036), 1), Is.False);
        Assert.That(SkillCastRules.TreeRefuses(treeSkills.Contains(80000001), TreeLevel(80000001), 3), Is.False);
    }

    [Test]
    public void RunebladeCastsItsTreeAtLevel13() {
        AssertRunebladeCasts(new Dictionary<int, short> {
            [10900001] = 3, [10900061] = 2, [10900081] = 1, [10900031] = 2, [10900151] = 1, [10900071] = 1,
        });
    }

    [Test]
    public void RunebladeCastsItsTreeAtLevel30() {
        AssertRunebladeCasts(new Dictionary<int, short> {
            [10900001] = 5, [10900061] = 4, [10900081] = 3, [10900031] = 4, [10900151] = 3, [10900071] = 3, [10900011] = 2,
            [10900111] = 2, [10900091] = 1, [10900121] = 1, [10900021] = 1,
        });
    }

    [Test]
    public void AwakenedJobHoldsItsAwakeningRank() {
        Dictionary<int, int> mainOf = SkillCastRules.MainSkills(RuneBlader, awakening: true);
        Assert.That(mainOf[10900212], Is.EqualTo(10900211));
        Assert.That(mainOf[10900231], Is.EqualTo(10900231));
        Assert.That(SkillCastRules.MainSkills(RuneBlader, awakening: false).ContainsKey(10900212), Is.False);
        Assert.That(SkillCastRules.TreeRefuses(true, 0, 1), Is.True);
        Assert.That(SkillCastRules.TreeRefuses(true, 3, 3), Is.False);
        Assert.That(SkillCastRules.TreeRefuses(true, 3, 4), Is.True);
    }

    [Test]
    public void MotionKeysPickTheNextCastKey() {
        var keys = new List<AnimationKey> { new("start", 0f), new("inputcombo", 0.3f), new("combo", 0.5f), new("selfinputcombo", 0.2f), new("end", 1.2f) };
        Assert.That(SkillCastRules.MotionKeys(keys), Is.EqualTo((0.3f, 0.2f)));
        keys = [new("combo", 0.5f), new("end", 1.2f)];
        Assert.That(SkillCastRules.MotionKeys(keys), Is.EqualTo((0.5f, 0.5f)));
        keys = [new("End", 1.2f)];
        Assert.That(SkillCastRules.MotionKeys(keys), Is.EqualTo((1.2f, 1.2f)));
        Assert.That(SkillCastRules.MotionKeys(new List<AnimationKey> { new("start", 0f) }), Is.Null);
        // A hold released early leaves the loop: a key after the loop is counted without the loop's 0.5 s.
        keys = [new("loopstart", 0.2f), new("selfinputcombo", 0.3f), new("loopend", 0.7f), new("inputcombo", 0.9f), new("end", 1.2f)];
        (float next, float self) = SkillCastRules.MotionKeys(keys)!.Value;
        Assert.That(next, Is.EqualTo(0.4f).Within(0.0001f));
        Assert.That(self, Is.EqualTo(0.3f), "inside the loop");
    }

    [Test]
    public void MotionSpeedIsFittedToTheAttackSpeed() {
        Assert.That(SkillCastRules.MotionSpeed(1f, 100), Is.EqualTo(1f));
        Assert.That(SkillCastRules.MotionSpeed(1.5f, 120), Is.EqualTo(1.8f).Within(0.0001f));
        Assert.That(SkillCastRules.MotionSpeed(0f, 100), Is.EqualTo(1f), "no speed in the data");
        Assert.That(SkillCastRules.MotionSpeed(1f, 80), Is.EqualTo(1f), "not slowed");
        Assert.That(SkillCastRules.MotionSpeed(1.5f, 120, 3), Is.EqualTo(1.5f), "move type 3 keeps its own speed");
        Assert.That(SkillCastRules.MotionSpeed(1.5f, 120, 2), Is.EqualTo(1.8f).Within(0.0001f));
        Assert.That(SkillCastRules.MotionRequiredGap(0.6f, 1.2f), Is.EqualTo(400));
        Assert.That(SkillCastRules.MotionRequiredGap(0.7f, 1f), Is.EqualTo(600), "rounded, not cut");
    }

    [Test]
    public void CastWaitsForTheLastMotion() {
        // inputcombo 0.6 s, selfinputcombo 0.3 s, speed 1: another skill from 500 ms on, the same from 200 ms on.
        var last = new SkillCastRules.LastMotion(10, 0.6f, 0.3f, 1f, 100, ServerTick: 10000, CastTick: 5000);
        Assert.That(SkillCastRules.MotionRefuses(last, 11, false, 100, 5499, 10499), Is.True);
        Assert.That(SkillCastRules.MotionRefuses(last, 11, false, 100, 5500, 10500), Is.False);
        Assert.That(SkillCastRules.MotionRefuses(last, 10, false, 100, 5199, 10199), Is.True);
        Assert.That(SkillCastRules.MotionRefuses(last, 10, false, 100, 5200, 10200), Is.False);
        Assert.That(SkillCastRules.MotionRefuses(last, 11, true, 100, 5001, 10001), Is.False, "active at once");
        // Attack speed 120 now: 0.6 / 1.2 = 500 ms, from 400 ms on.
        Assert.That(SkillCastRules.MotionRefuses(last, 11, false, 120, 5400, 10400), Is.False);
        Assert.That(SkillCastRules.MotionRefuses(last, 11, false, 120, 5399, 10399), Is.True);
        // The client's tick cannot lengthen the time: the arrivals plus the allowance count.
        Assert.That(SkillCastRules.MotionRefuses(last, 11, false, 100, 9000, 10000), Is.False, "arrivals 0 ms apart plus 1000");
        // A motion of move type 3 is not sped up: 0.6 s at attack speed 120, from 500 ms on.
        var unscaled = last with { MoveType = 3 };
        Assert.That(SkillCastRules.MotionRefuses(unscaled, 11, false, 120, 5499, 10499), Is.True);
        Assert.That(SkillCastRules.MotionRefuses(unscaled, 11, false, 120, 5500, 10500), Is.False);
        var slow = new SkillCastRules.LastMotion(10, 1.2f, 1.2f, 1f, 100, ServerTick: 10000, CastTick: 5000);
        Assert.That(SkillCastRules.MotionRefuses(slow, 11, false, 100, 9000, 10000), Is.True);
        Assert.That(SkillCastRules.MotionRefuses(slow, 11, false, 100, 9000, 10100), Is.False, "two casts held up together");
        // The attack speed fell since the last cast: the higher one counts.
        var fast = last with { AttackSpeed = 150 };
        Assert.That(SkillCastRules.MotionRefuses(fast, 11, false, 100, 5300, 10300), Is.False, "0.6 / 1.5 = 400 ms");
        // Arriving in a burst is taken by the client's own ticks.
        Assert.That(SkillCastRules.MotionRefuses(slow, 11, false, 100, 6099, 10600), Is.True);
        Assert.That(SkillCastRules.MotionRefuses(slow, 11, false, 100, 6100, 10100), Is.False);
    }

    [Test]
    public void CancelledCastStopsTakingAttacks() {
        Assert.That(SkillCastRules.CancelledRefuses(0, 99999), Is.False, "not cancelled");
        Assert.That(SkillCastRules.CancelledRefuses(5000, 5000), Is.False);
        Assert.That(SkillCastRules.CancelledRefuses(5000, 6000), Is.False, "an attack already on its way");
        Assert.That(SkillCastRules.CancelledRefuses(5000, 6001), Is.True);
    }
}
