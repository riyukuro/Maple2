using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// A region that fires only once something enters it (onlySensingActive), as a pet snare's 93000002 (delay 30000,
// interval 0): until then it keeps looking, a look that finds no one takes none of its fires, and its life
// (delay + removeDelay) ends it.
public class SensingRegionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // A level with no attack: no look finds anyone.
    private static SkillMetadata Skill() {
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Skills = [],
            Motions = [new SkillMetadataMotion(new SkillMetadataMotionProperty("", 1, 0, false), [], new Dictionary<string, byte>())],
            OnlyManualActiveSplash = false,
        };
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        return new SkillMetadata(93000002, 1, null, property, null!, level);
    }

    private static void SetTick(FieldManager field, long tick) {
        typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(field, tick);
    }

    [TestCase(0)]
    [TestCase(500)]
    public void EmptyLooksKeepTheRegionWaiting(int interval) {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = test.Session.Field!;
        SetTick(field, 1_000);
        var splash = new SkillEffectMetadataSplash(Interval: interval, Delay: 30_000, RemoveDelay: 0, UseDirection: true,
            ImmediateActive: false, NonTargetActive: false, OnlySensingActive: true, DependOnCasterState: false, Independent: false, Chain: null);
        var region = new FieldSkill(field, 905, test.Session.Player, Skill(), 1, splash, Vector3.Zero) { Position = Vector3.Zero };
        Assert.That(region.Active, Is.False);

        for (long tick = 1_000 + interval; tick <= 31_000; tick += 10_000) {
            SetTick(field, tick);
            region.Update(tick);
            Assert.Multiple(() => {
                Assert.That(region.Active, Is.False, $"no one found at {tick}");
                Assert.That(region.FireCount, Is.EqualTo(1), $"no fire taken at {tick}");
                Assert.That(region.NextTick, Is.EqualTo(tick + interval), $"looks again at {tick}");
                Assert.That(region.Enabled, Is.True, $"alive at {tick}");
            });
        }

        SetTick(field, 31_001);
        region.Update(31_001);
        Assert.Multiple(() => {
            Assert.That(region.FireCount, Is.EqualTo(0));
            Assert.That(region.NextTick, Is.EqualTo(long.MaxValue));
            Assert.That(region.Enabled, Is.False, "past its 30000 ms life");
        });
    }
}
