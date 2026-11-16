using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// A field stops its updates while nobody is in it, and so do its regions. When a player comes back, a region fires
// once and drops the fires it owes from that time; it does not send them one per frame. A live field showed this as
// 70000018 (a map region, interval 700) sending 32 fires within half a second when a player entered. KMS2 sends a
// map region's fires at least 500 ms apart. The region keeps its rhythm, and a region with a fixed number of fires
// spends the dropped ones.
public class RegionCatchUpTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // A level with no attack: a fire reaches no one and sends nothing.
    private static SkillMetadata Skill() {
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Skills = [],
            Motions = [new SkillMetadataMotion(new SkillMetadataMotionProperty("", 1, 0, false), [], new Dictionary<string, byte>())],
            OnlyManualActiveSplash = false,
        };
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        return new SkillMetadata(70000018, 1, null, property, null!, level);
    }

    private static FieldManager Field(TestGameSession test) {
        test.Start();
        FieldManager field = test.Session.Field!;
        typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(field, 1_000L);
        return field;
    }

    [Test]
    public void AMapRegionFiresOnceAfterAPause_AndKeepsItsRhythm() {
        var test = new TestGameSession();
        FieldManager field = Field(test);
        var region = new FieldSkill(field, 905, test.Session.Player, Skill(), 700, Vector3.Zero) { Position = Vector3.Zero };
        region.Update(1_700);
        Assert.That(region.NextTick, Is.EqualTo(2_400));

        // 39 fires owed between 2400 and 29000: one is sent, the next comes at 1700 + 700 * 40.
        region.Update(29_000);
        Assert.That(region.NextTick, Is.EqualTo(29_700));
        region.Update(29_015);
        Assert.That(region.NextTick, Is.EqualTo(29_700), "no second fire in the next frame");
        Assert.That(region.FireCount, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void ARegionOfAFewFires_SpendsTheDroppedOnes() {
        var test = new TestGameSession();
        FieldManager field = Field(test);
        var region = new FieldSkill(field, 905, test.Session.Player, Skill(), 500, 5, Vector3.Zero) { Position = Vector3.Zero };
        region.Update(1_500);
        Assert.That(region.FireCount, Is.EqualTo(4));

        // Owed at 2000, 2500, 3000: one is sent, two are dropped.
        region.Update(3_100);
        Assert.Multiple(() => {
            Assert.That(region.FireCount, Is.EqualTo(1));
            Assert.That(region.NextTick, Is.EqualTo(3_500));
        });

        // More owed than it has left: the fire sent is its last.
        region.Update(9_000);
        Assert.Multiple(() => {
            Assert.That(region.FireCount, Is.EqualTo(0));
            Assert.That(region.NextTick, Is.EqualTo(long.MaxValue));
        });
    }
}
