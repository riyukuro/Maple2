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
        // The attack speed cuts a short cooldown: 450 ms at 100 needs 337 ms, at 140 it needs 241 ms.
        Assert.That(SkillCastRules.RequiredGap(450), Is.EqualTo(337));
        Assert.That(SkillCastRules.RequiredGap(450, 140), Is.EqualTo(241));
        Assert.That(SkillCastRules.RequiredGap(450, 50), Is.EqualTo(337), "under 100 counts as 100");
        Assert.That(SkillCastRules.RequiredGap(8000, 140), Is.EqualTo(7500), "1 s or more is not cut");
        // 10500001 (0.45 s, motion speed 1): the client's cooldown is 450 ms at 100 and 412 ms at 109.
        Assert.That(SkillCastRules.RequiredGap(450, 100), Is.EqualTo(337));
        Assert.That(SkillCastRules.RequiredGap(450, 109), Is.EqualTo(309));
        SkillCastRules.LastCast[] at109 = [new SkillCastRules.LastCast(10500001, 0, 450, 100_000, 50_000, 109)];
        Assert.That(SkillCastRules.CooldownRefuses(at109, 10500001, 0, 50_412, 100_412, 109), Is.False, "the client's own cooldown end");
        Assert.That(SkillCastRules.CooldownRefuses(at109, 10500001, 0, 50_309, 100_309, 109), Is.False);
        Assert.That(SkillCastRules.CooldownRefuses(at109, 10500001, 0, 50_308, 100_308, 109), Is.True);
        // The cast motion's own speed cuts it too: 0.25 s at motion speed 1.5 is 166 ms in the client.
        Assert.That(SkillCastRules.RequiredGap(250, 100, 1.5f), Is.EqualTo(125));
        Assert.That(SkillCastRules.RequiredGap(250, 109, 1.5f), Is.EqualTo(114));
        Assert.That(SkillCastRules.RequiredGap(450, 100, 0.8f), Is.EqualTo(421), "a slower motion lengthens it");
        Assert.That(SkillCastRules.RequiredGap(450, 140, 1f, SkillCastRules.UnscaledMoveType), Is.EqualTo(337), "move type 3: the motion's own speed only");
        SkillCastRules.LastCast[] quick = [new SkillCastRules.LastCast(10500173, 0, 250, 100_000, 50_000, 100, 1.5f)];
        Assert.That(SkillCastRules.CooldownRefuses(quick, 10500173, 0, 50_166, 100_166), Is.False);
        Assert.That(SkillCastRules.CooldownRefuses(quick, 10500173, 0, 50_124, 100_124), Is.True);
        SkillCastRules.LastCast[] strafe = [new SkillCastRules.LastCast(10500001, 0, 450, 100_000, 50_000)];
        Assert.That(SkillCastRules.CooldownRefuses(strafe, 10500001, 0, 50_300, 100_300), Is.True);
        Assert.That(SkillCastRules.CooldownRefuses(strafe, 10500001, 0, 50_300, 100_300, 140), Is.False);
        // The attack speed fell since the last cast: the higher one counts.
        SkillCastRules.LastCast[] fast = [new SkillCastRules.LastCast(10500001, 0, 450, 100_000, 50_000, 140)];
        Assert.That(SkillCastRules.CooldownRefuses(fast, 10500001, 0, 50_300, 100_300), Is.False);
    }

    [Test]
    public void CooldownIsNotShortenedByTheClientTick() {
        // Cooldown 8 s, taken from 7.5 s on. The client's tick says 8 s, the arrivals 1 s apart: 6.5 s early.
        SkillCastRules.LastCast[] casts = [new SkillCastRules.LastCast(10900061, 0, 8000, 100_000, 50_000)];
        Assert.That(SkillCastRules.CooldownShortfall(casts, 10900061, 0, 58_000, 101_000), Is.EqualTo(6500));
        Assert.That(SkillCastRules.CooldownRefuses(casts, 10900061, 0, 58_000, 101_000), Is.True);
        // Arrivals 7.2 s apart: 300 ms early, for the lag budget.
        Assert.That(SkillCastRules.CooldownShortfall(casts, 10900061, 0, 58_000, 107_200), Is.EqualTo(300));
        // The ticks closer than that (a time sync answer held up): the same.
        Assert.That(SkillCastRules.CooldownShortfall(casts, 10900061, 0, 55_500, 107_200), Is.EqualTo(300));
        // More than 2 s closer.
        Assert.That(SkillCastRules.CooldownShortfall(casts, 10900061, 0, 55_499, 107_200), Is.EqualTo(SkillCastRules.ClientRefused));
        // Long enough by the arrivals: taken whatever the tick says.
        Assert.That(SkillCastRules.CooldownShortfall(casts, 10900061, 0, 57_000, 107_500), Is.EqualTo(0));
        Assert.That(SkillCastRules.CooldownShortfall(casts, 10900062, 0, 50_001, 100_001), Is.EqualTo(0), "another skill");
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
    public void SpiritCostTakesInvokesAndBuffRates() {
        Assert.That(SkillCastRules.SpiritCost(40, 0, 0f, 0f), Is.EqualTo(40));
        // Cost invokes: a flat value and a rate of the full cost.
        Assert.That(SkillCastRules.SpiritCost(40, 5, 0.25f, 0f), Is.EqualTo(25));
        // A buff's consume rate: -1 makes the cast free, -0.5 halves it, 0.5 adds half.
        Assert.That(SkillCastRules.SpiritCost(40, 0, 0f, -1f), Is.EqualTo(0));
        Assert.That(SkillCastRules.SpiritCost(40, 0, 0f, -0.5f), Is.EqualTo(20));
        Assert.That(SkillCastRules.SpiritCost(40, 0, 0f, 0.5f), Is.EqualTo(60));
        Assert.That(SkillCastRules.SpiritCost(15, 0, 0f, -0.7f), Is.EqualTo(4));
        Assert.That(SkillCastRules.SpiritCost(40, 0, 0.5f, -1f), Is.EqualTo(0));
        Assert.That(SkillCastRules.SpiritCost(10, 30, 0f, 0f), Is.EqualTo(0));
    }

    [Test]
    public void BuffListDisablesOnlyItsSkills() {
        int[]?[] held = [null, [11000101, 11000121], []];
        Assert.That(SkillCastRules.DisabledByBuff(held, 11000121), Is.True);
        Assert.That(SkillCastRules.DisabledByBuff(held, 11000122), Is.False);
        Assert.That(SkillCastRules.DisabledByBuff([], 11000121), Is.False);
    }

    [Test]
    public void SkillOfNamedFieldsIsCastOnlyThere() {
        Assert.That(SkillCastRules.MapRefuses([], 2000001), Is.False);
        Assert.That(SkillCastRules.MapRefuses([0], 2000001), Is.False);
        Assert.That(SkillCastRules.MapRefuses([2000001, 2000002], 2000002), Is.False);
        Assert.That(SkillCastRules.MapRefuses([0, 2000001], 2000003), Is.True);
    }

    [Test]
    public void ItemOfACastMustBeThereBeforePaying() {
        Assert.That(SkillCastRules.ItemMissing(false, 0, false, false), Is.False);
        Assert.That(SkillCastRules.ItemMissing(true, 0, true, true), Is.True);
        Assert.That(SkillCastRules.ItemMissing(true, 7, false, false), Is.True);
        Assert.That(SkillCastRules.ItemMissing(true, 7, true, false), Is.False);
        Assert.That(SkillCastRules.ItemMissing(true, 7, false, true), Is.False);
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
        // The client's tick cannot lengthen the time: the arrivals count, the rest is for the lag budget.
        Assert.That(SkillCastRules.MotionShortfall(last, 11, false, 100, 9000, 10000), Is.EqualTo(500), "arrivals 0 ms apart");
        // On time by the arrivals is on time, whatever the ticks say.
        Assert.That(SkillCastRules.MotionShortfall(last, 11, false, 100, 5001, 10500), Is.EqualTo(0));
        // A motion of move type 3 is not sped up: 0.6 s at attack speed 120, from 500 ms on.
        var unscaled = last with { MoveType = 3 };
        Assert.That(SkillCastRules.MotionRefuses(unscaled, 11, false, 120, 5499, 10499), Is.True);
        Assert.That(SkillCastRules.MotionRefuses(unscaled, 11, false, 120, 5500, 10500), Is.False);
        var slow = new SkillCastRules.LastMotion(10, 1.2f, 1.2f, 1f, 100, ServerTick: 10000, CastTick: 5000);
        Assert.That(SkillCastRules.MotionShortfall(slow, 11, false, 100, 9000, 10000), Is.EqualTo(1100));
        Assert.That(SkillCastRules.MotionShortfall(slow, 11, false, 100, 9000, 10100), Is.EqualTo(1000), "two casts held up together");
        // The attack speed fell since the last cast: the higher one counts.
        var fast = last with { AttackSpeed = 150 };
        Assert.That(SkillCastRules.MotionRefuses(fast, 11, false, 100, 5300, 10300), Is.False, "0.6 / 1.5 = 400 ms");
        // Arriving in a burst: a tick more than 2 s short of the time needed is refused outright.
        Assert.That(SkillCastRules.MotionShortfall(slow, 11, false, 100, 6099, 10600), Is.EqualTo(500));
        Assert.That(SkillCastRules.MotionShortfall(slow, 11, false, 100, 4100, 10600), Is.EqualTo(500));
        Assert.That(SkillCastRules.MotionShortfall(slow, 11, false, 100, 4099, 10600), Is.EqualTo(SkillCastRules.ClientRefused));
        Assert.That(SkillCastRules.MotionShortfall(slow, 11, false, 100, 6100, 10100), Is.EqualTo(1000));
    }

    [Test]
    public void CancelledCastStopsTakingAttacks() {
        Assert.That(SkillCastRules.CancelledRefuses(0, 99999), Is.False, "not cancelled");
        Assert.That(SkillCastRules.CancelledRefuses(5000, 5000), Is.False);
        Assert.That(SkillCastRules.CancelledRefuses(5000, 7000), Is.False, "an attack already on its way");
        Assert.That(SkillCastRules.CancelledRefuses(5000, 7001), Is.True);
    }

    [Test]
    public void AClientsOwnCancelIsNotSentBackToIt() {
        Assert.That(SkillCastRules.CancelGoesToCaster(endedByServer: false), Is.False);
        Assert.That(SkillCastRules.CancelGoesToCaster(endedByServer: true), Is.True, "a state skill the server ends");
    }

    [Test]
    public void CooldownIsCutByGroupAndCasterRates() {
        // Made-up ids. Two buffs held cut group 90000001 by 0.2 each; another group and no group get nothing.
        CooldownGroupRate[]?[] held = [[new CooldownGroupRate(90000001, 0.2f)], null, [new CooldownGroupRate(90000005, 0.5f), new CooldownGroupRate(90000001, 0.2f)]];
        Assert.That(SkillCastRules.GroupReduceRate(held, 90000001), Is.EqualTo(0.4f).Within(1e-6));
        Assert.That(SkillCastRules.GroupReduceRate(held, 90000002), Is.EqualTo(0f));
        Assert.That(SkillCastRules.GroupReduceRate(held, 0), Is.EqualTo(0f));

        Assert.That(SkillCastRules.CooldownMs(30f, false, 0f, 0), Is.EqualTo(30000));
        Assert.That(SkillCastRules.CooldownMs(30f, false, 0.2f, 0), Is.EqualTo(24000).Within(1));
        Assert.That(SkillCastRules.CooldownMs(30f, false, 0.2f, 0.1), Is.EqualTo(21000).Within(1), "the rates add up");
        Assert.That(SkillCastRules.CooldownMs(30f, true, 0.2f, 0.1), Is.EqualTo(24000).Within(1), "the skill ignores the caster's rate only");
        Assert.That(SkillCastRules.CooldownMs(30f, false, 0.8f, 0.5), Is.EqualTo(0));
        Assert.That(SkillCastRules.CooldownMs(60f, false, 0f, 0), Is.EqualTo(60000), "no rate: the full length, nothing off");
        Assert.That(SkillCastRules.CooldownMs(1f, false, 0f, 0.99), Is.EqualTo(10).Within(1));
    }

    [Test]
    public void RechargeUsesComeBackOnePerCooldown() {
        // Two uses out, 10 s each, the first cooldown ends at 10000.
        Assert.That(SkillCastRules.SettleRecharge(10_000, 2, 10_000, 9_999), Is.EqualTo((10_000L, 2)));
        Assert.That(SkillCastRules.SettleRecharge(10_000, 2, 10_000, 10_000), Is.EqualTo((20_000L, 1)), "one back, the next runs from the end");
        Assert.That(SkillCastRules.SettleRecharge(10_000, 2, 10_000, 19_999), Is.EqualTo((20_000L, 1)));
        Assert.That(SkillCastRules.SettleRecharge(10_000, 2, 10_000, 25_000), Is.EqualTo((20_000L, 0)));
        Assert.That(SkillCastRules.SettleRecharge(10_000, 3, 10_000, 25_000), Is.EqualTo((30_000L, 1)));
        Assert.That(SkillCastRules.SettleRecharge(10_000, 0, 10_000, 25_000), Is.EqualTo((10_000L, 0)));
        Assert.That(SkillCastRules.SettleRecharge(10_000, 3, 0, 25_000), Is.EqualTo((10_000L, 0)), "no length: nothing left out");
    }

    [Test]
    public void GroupReductionIsReadFromTheSpecialEffect() {
        // Made-up rows: type 4 with probability 1 is taken; another type or a rolled one is not.
        const string values = "<v groupID=\"90000001\" reduceRate=\"0.20\" />";
        List<CooldownGroupRate> found = Maple2.File.Ingest.Helpers.CooldownGroupRates.Read([
            ("4", "1", values), ("3", "1", values), ("4", "0.5", values), ("4", "1", "<v groupID=\"0\" reduceRate=\"0.2\" />"), ("4", "1", "<v"),
        ]);
        Assert.That(found, Is.EqualTo(new[] { new CooldownGroupRate(90000001, 0.2f) }));
        found = Maple2.File.Ingest.Helpers.CooldownGroupRates.Read([
            ("4", "1", values + "<v groupID=\"90000002\" reduceRate=\"0.1\" />"), ("4", "1", "<v groupID=\"90000003\" reduceRate=\"0.1\" />"),
        ]);
        Assert.That(found, Is.EqualTo(new[] { new CooldownGroupRate(90000001, 0.2f) }), "one per effect");
    }

    [Test]
    public void UnrideSkillIsNotCastFromOwnRide() {
        Assert.That(SkillCastRules.RideRefuses(unrideOnUse: true, mainType: 0, onOwnItemRide: true), Is.True);
        Assert.That(SkillCastRules.RideRefuses(unrideOnUse: false, mainType: 0, onOwnItemRide: true), Is.False, "a skill cast from the ride");
        Assert.That(SkillCastRules.RideRefuses(unrideOnUse: true, mainType: 0, onOwnItemRide: false), Is.False);
    }

    [Test]
    public void UnrideMainTypesLeaveTheRideWithoutTheFlag() {
        int[] unride = [1, 4, 7, 11, 12, 13, 14, 15, 17, 18, 19];
        for (int mainType = 0; mainType <= 40; mainType++) {
            Assert.That(SkillCastRules.RideRefuses(unrideOnUse: false, mainType, onOwnItemRide: true), Is.EqualTo(unride.Contains(mainType)), $"main type {mainType}");
            Assert.That(SkillCastRules.RideRefuses(unrideOnUse: false, mainType, onOwnItemRide: false), Is.False);
        }
    }

    [Test]
    public void PlayedClientSkillDataIsReadFromTheFirstBasic() {
        // Made-up rows.
        var document = new System.Xml.XmlDocument();
        document.LoadXml("""
            <ms2>
              <skill id="100"><basic mainType="4"><stateAttr rechargeMaxCount="6" /></basic></skill>
              <skill id="101" originID="100"><basic><stateAttr rechargeMaxCount="6" rechargeShareOrigin="1" /></basic></skill>
              <skill id="102" originID="100"><basic mainType="2" feature="X"><stateAttr rechargeMaxCount="3" /></basic></skill>
              <skill id="103"><basic><stateAttr rechargeShareOrigin="1" /></basic></skill>
              <skill id="0"><basic mainType="1" /></skill>
              <skill id="100"><basic mainType="9" /></skill>
              <skill id="104" />
            </ms2>
            """);
        Dictionary<int, PlayedClientSkills.Entry> read = PlayedClientSkills.Read([document]);
        Assert.That(read.Keys.OrderBy(id => id), Is.EqualTo(new[] { 100, 101, 102, 103, 104 }));
        Assert.That(read[100], Is.EqualTo(new PlayedClientSkills.Entry(4, 6, false)));
        Assert.That(read[101], Is.EqualTo(new PlayedClientSkills.Entry(0, 6, true)), "uses counted on the origin skill");
        Assert.That(read[102], Is.EqualTo(new PlayedClientSkills.Entry(2, 3, false)));
        Assert.That(read[103], Is.EqualTo(new PlayedClientSkills.Entry(0, 0, false)), "no origin: its own uses");
        Assert.That(read[104], Is.EqualTo(new PlayedClientSkills.Entry(0, 0, false)));
        Assert.That(PlayedClientSkills.IsSkillFile("skilldata/010.xml"), Is.True);
        Assert.That(PlayedClientSkills.IsSkillFile("skill/10/10000001.xml"), Is.False);
    }

    [Test]
    public void RechargeSkillIsNotCastWithNoUseLeft() {
        // 3 uses, the running cooldown ends at 10000.
        Assert.That(SkillCastRules.RechargeEmptyShortfall(2, 3, 10_000, 5_000), Is.EqualTo(0), "a use is left");
        Assert.That(SkillCastRules.RechargeEmptyShortfall(3, 3, 10_000, 5_000), Is.EqualTo(10_000 - SkillCastRules.JobCooldownSlackMs - 5_000));
        Assert.That(SkillCastRules.RechargeEmptyShortfall(3, 3, 10_000, 10_000 - SkillCastRules.JobCooldownSlackMs), Is.EqualTo(0));
        Assert.That(SkillCastRules.RechargeEmptyShortfall(3, 0, 10_000, 5_000), Is.EqualTo(0), "no recharge skill");
        // The uses the server counts out come back as the client's do.
        (long end, int uses) = SkillCastRules.SettleRecharge(10_000, 3, 10_000, 10_000);
        Assert.That(SkillCastRules.RechargeEmptyShortfall(uses, 3, end, 10_000), Is.EqualTo(0));
    }

    [Test]
    public void RechargeSkillWaitsItsGapAfterAUse() {
        // A gap of 4 s (longer than the clock's cast-back allowance, ClockRate.CastBackMs); the last use at
        // client tick 1000, arrived at 50000.
        long required = (long) (4000 * SkillCastRules.ShortCooldownShare);
        Assert.That(required - ClockRate.CastBackMs, Is.GreaterThan(200));
        Assert.That(SkillCastRules.RechargeGapShortfall(4f, 1000 + 4000, 1000, 54_000, 50_000), Is.EqualTo(0));
        Assert.That(SkillCastRules.RechargeGapShortfall(4f, 1000 + 200, 1000, 50_200, 50_000), Is.EqualTo(SkillCastRules.ClientRefused), "early by the client's own ticks");
        Assert.That(SkillCastRules.RechargeGapShortfall(4f, 1000 + 4000, 1000, 50_200, 50_000), Is.EqualTo(required - 200), "only arrives early");
        Assert.That(SkillCastRules.RechargeGapShortfall(0, 1000 + 1, 1000, 50_001, 50_000), Is.EqualTo(0), "no gap");
    }

    [Test]
    public void CancelIsCastUidThenCaster() {
        // Made-up ids. 12 bytes after the opcode.
        Maple2.PacketLib.Tools.ByteWriter writer = Maple2.Server.Game.Packets.SkillPacket.Cancel(0x0102030405060708, 0x00112233);
        byte[] expected = [0x3F, 0x00, 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, 0x33, 0x22, 0x11, 0x00];
        Assert.That(writer.Buffer.Take(writer.Length).ToArray(), Is.EqualTo(expected));
    }
}
