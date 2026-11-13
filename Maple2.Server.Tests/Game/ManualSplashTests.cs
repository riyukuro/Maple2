using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// A region of a skill whose level has splashManualActiveProperty onlyManualActiveSplash (10400292, placed by the
// priest skill 10400291 with fireCount 0): it does not fire on its own, lives splashLifeTick ms and fires when a hit of
// its caster's attack names it in splashActiveSkillID (the heal 10400071), at most once per splashInvokeCoolTick.
public class ManualSplashTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Region = 10400292;

    private static SkillMetadata Skill(int id, bool manual, int life = 6000, int cool = 100) {
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Skills = [],
            Motions = [new SkillMetadataMotion(new SkillMetadataMotionProperty("", 1, 0, false, SplashLifeTick: life, SplashInvokeCoolTick: cool),
                [], new Dictionary<string, byte>())],
            OnlyManualActiveSplash = manual,
        };
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        return new SkillMetadata(id, 1, null, property, null!, level);
    }

    // 10400291's conditionSkill: splash, interval 0, delay 0, no removeDelay, useDirection.
    private static readonly SkillEffectMetadataSplash Splash = new(Interval: 0, Delay: 0, RemoveDelay: 0, UseDirection: true,
        ImmediateActive: false, NonTargetActive: false, OnlySensingActive: false, DependOnCasterState: false, Independent: false, Chain: null);

    private static void SetTick(FieldManager field, long tick) {
        typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(field, tick);
    }

    private static FieldSkill Place(FieldManager field, int objectId, IActor caster, SkillMetadata skill, int fireCount = -1) {
        var placed = new FieldSkill(field, objectId, caster, skill, fireCount, Splash, Vector3.Zero) { Position = Vector3.Zero };
        FieldInfo info = typeof(FieldManager).GetField("fieldSkills", Any)!;
        if (info.GetValue(field) is not ConcurrentDictionary<int, FieldSkill> skills) {
            // The test field is made without its initializers.
            skills = new ConcurrentDictionary<int, FieldSkill>();
            info.SetValue(field, skills);
        }
        skills[objectId] = placed;
        return placed;
    }

    [Test]
    public void ManualRegionWaitsForItsInvoke() {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = test.Session.Field!;
        SetTick(field, 1_000);
        FieldSkill region = Place(field, 900, test.Session.Player, Skill(Region, true));
        Assert.Multiple(() => {
            Assert.That(region.ManualOnly, Is.True);
            Assert.That(region.NextTick, Is.EqualTo(long.MaxValue), "no fire of its own");
            Assert.That(region.Enabled, Is.True);
        });
        Assert.That(field.InvokeSplashes(test.Session.Player, [Region]), Is.EqualTo(1));
        Assert.That(field.InvokeSplashes(test.Session.Player, [Region]), Is.EqualTo(0), "within the invoke cool time");
        SetTick(field, 1_100);
        Assert.That(field.InvokeSplashes(test.Session.Player, [Region]), Is.EqualTo(1), "100 ms on");
        SetTick(field, 7_001);
        Assert.Multiple(() => {
            Assert.That(field.InvokeSplashes(test.Session.Player, [Region]), Is.EqualTo(0), "past its 6000 ms life");
            Assert.That(region.Enabled, Is.False);
        });
    }

    // An effect with a fire count (none in KR, which places 10400292 with fireCount 0) still ends at the region's life:
    // the count never runs down, as the region does not fire on its own.
    [Test]
    public void ManualRegionWithAFireCountEndsAtItsLife() {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = test.Session.Field!;
        SetTick(field, 1_000);
        FieldSkill region = Place(field, 904, test.Session.Player, Skill(Region, true), fireCount: 3);
        Assert.Multiple(() => {
            Assert.That(region.FireCount, Is.EqualTo(0));
            Assert.That(region.Enabled, Is.True);
        });
        SetTick(field, 7_000);
        Assert.That(region.Enabled, Is.True, "at the end of its life");
        SetTick(field, 7_001);
        Assert.That(region.Enabled, Is.False, "past its 6000 ms life");
    }

    [Test]
    public void InvokeTakesOnlyTheCastersNamedManualRegions() {
        var test = new TestGameSession();
        test.Start();
        var other = new TestGameSession(test.Options);
        other.Start();
        FieldManager field = test.Session.Field!;
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(other.Session.Player, 2);
        SetTick(field, 1_000);
        Place(field, 901, other.Session.Player, Skill(Region, true));
        Place(field, 902, test.Session.Player, Skill(Region + 1, true));
        FieldSkill plain = Place(field, 903, test.Session.Player, Skill(Region, false));
        Assert.Multiple(() => {
            Assert.That(field.InvokeSplashes(test.Session.Player, [Region]), Is.EqualTo(0));
            Assert.That(plain.ManualOnly, Is.False);
            Assert.That(plain.Invoke(), Is.False, "a region that fires on its own is not made to");
        });
    }

    [TestCase(1_000, 7_000, 1_000, true)]
    [TestCase(999, 7_000, 1_000, false)]
    [TestCase(7_000, 7_000, 1_000, true)]
    [TestCase(7_001, 7_000, 1_000, false)]
    public void InvokeNeedsLifeAndCoolTime(long now, long end, long next, bool fires) {
        Assert.That(SplashTiming.CanInvoke(now, end, next), Is.EqualTo(fires));
    }
}
