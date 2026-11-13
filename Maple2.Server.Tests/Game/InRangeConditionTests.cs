using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game;

// targetCheckRange / targetInRangeCount / targetFriendly / targetCountSign: the count of the checked actor's enemies
// (friendly 1) or allies (2) within the range, compared with the count (90050886: 4 or more enemies within 9 m).
public class InRangeConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Scene {
        public required TestGameSession A;
        public required TestGameSession B;
        public required TestGameSession C;
        public required FieldManager Field;
        public FieldPlayer PlayerA => A.Session.Player;
    }

    // A at the origin, B and C (A's party-less allies) in A's field at the given distances along x.
    private static Scene Start(float b, float c) {
        var a = new TestGameSession();
        a.Start();
        var tb = new TestGameSession(a.Options);
        tb.Start();
        var tc = new TestGameSession(a.Options);
        tc.Start();
        FieldManager field = a.Session.Field!;
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc>());
        int objectId = 2;
        foreach ((TestGameSession other, float x) in new[] { (tb, b), (tc, c) }) {
            typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(other.Session.Player, objectId);
            field.Players[objectId++] = other.Session.Player;
            other.Session.Field = field;
            other.Session.Player.Position = new Vector3(x, 0, 0);
        }
        a.Session.Player.Position = Vector3.Zero;
        // Each player's own field, which the condition counts in.
        foreach (TestGameSession test in new[] { a, tb, tc }) {
            typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, field);
        }
        return new Scene { A = a, B = tb, C = tc, Field = field };
    }

    // A living mob of the field at (x, 0, 0), with only what the target pool reads.
    private static FieldNpc Mob(FieldManager field, int objectId, float x) {
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Property>k__BackingField", Any)!.SetValue(metadata,
            new NpcMetadataProperty([], new NpcMetadataCapsule(50, 100), null));
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(npc, new Npc(metadata, null));
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(npc, objectId);
        typeof(Actor<Npc>).GetField("<Transform>k__BackingField", Any)!.SetValue(npc, new Transform());
        npc.Position = new Vector3(x, 0, 0);
        field.Mobs[objectId] = npc;
        return npc;
    }

    private static void Dead(FieldPlayer player) {
        typeof(FieldPlayer).GetField("isDead", Any)!.SetValue(player, true);
    }

    private static void Dead(FieldNpc npc) {
        typeof(Actor<Npc>).GetField("<IsDead>k__BackingField", Any)!.SetValue(npc, true);
    }

    // An effect condition with only the owner's range check (battle mounts allowed: a test session has no ride).
    private static BeginCondition Condition(int range, int count, int friendly, CompareType compare, int minRange = 0) {
        var owner = new BeginConditionTarget([], new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], [], []), [], [], [],
            new Dictionary<MasteryType, int>(), [], [], new BeginConditionTarget.TargetCheck(range, minRange, count, friendly, compare));
        return new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, owner, null);
    }

    [Test]
    public void CountHolds_EverySign() {
        Assert.Multiple(() => {
            Assert.That(SkillUtils.CountHolds(CompareType.Equals, 1, 1), Is.True);
            Assert.That(SkillUtils.CountHolds(CompareType.Equals, 2, 1), Is.False);
            Assert.That(SkillUtils.CountHolds(CompareType.Less, 0, 1), Is.True);
            Assert.That(SkillUtils.CountHolds(CompareType.Less, 1, 1), Is.False);
            Assert.That(SkillUtils.CountHolds(CompareType.LessEquals, 1, 1), Is.True);
            Assert.That(SkillUtils.CountHolds(CompareType.LessEquals, 2, 1), Is.False);
            Assert.That(SkillUtils.CountHolds(CompareType.Greater, 4, 3), Is.True);
            Assert.That(SkillUtils.CountHolds(CompareType.Greater, 3, 3), Is.False);
            Assert.That(SkillUtils.CountHolds(CompareType.GreaterEquals, 3, 3), Is.True);
            Assert.That(SkillUtils.CountHolds(CompareType.GreaterEquals, 2, 3), Is.False);
        });
    }

    // Allies are the other players (A itself is not counted), within the range and past its minimum; a dead one is not.
    [Test]
    public void Allies_WithinTheRange() {
        Scene scene = Start(300, 900);
        Assert.Multiple(() => {
            Assert.That(scene.Field.CountAround(scene.PlayerA, 0, 600, hostile: false), Is.EqualTo(1));
            Assert.That(scene.Field.CountAround(scene.PlayerA, 0, 1000, hostile: false), Is.EqualTo(2));
            Assert.That(scene.Field.CountAround(scene.PlayerA, 500, 1000, hostile: false), Is.EqualTo(1));
            Assert.That(scene.Field.CountAround(scene.PlayerA, 0, 1000, hostile: true), Is.Zero);
        });
        Dead(scene.C.Session.Player);
        Assert.That(scene.Field.CountAround(scene.PlayerA, 0, 1000, hostile: false), Is.EqualTo(1));
    }

    // A player's enemies are the field's mobs; a dead mob (a corpse) is not counted.
    [Test]
    public void Enemies_AreTheMobsWithinTheRange() {
        Scene scene = Start(5000, 5000);
        Mob(scene.Field, 100, 200);
        FieldNpc far = Mob(scene.Field, 101, 700);
        Assert.Multiple(() => {
            Assert.That(scene.Field.CountAround(scene.PlayerA, 0, 600, hostile: true), Is.EqualTo(1));
            Assert.That(scene.Field.CountAround(scene.PlayerA, 0, 900, hostile: true), Is.EqualTo(2));
            Assert.That(scene.Field.CountAround(scene.PlayerA, 0, 900, hostile: false), Is.Zero);
        });
        Dead(far);
        Assert.That(scene.Field.CountAround(scene.PlayerA, 0, 900, hostile: true), Is.EqualTo(1));
    }

    // 90050886's condition (4 or more enemies within 9 m) holds only while enough mobs are near: it turns off as one
    // walks away. 10500111's (no enemy within 4 m) holds with none near. 90051222's (exactly one within 10 m).
    [Test]
    public void OwnerCondition_FollowsTheCount() {
        Scene scene = Start(5000, 5000);
        FieldPlayer a = scene.PlayerA;
        BeginCondition four = Condition(900, 4, 1, CompareType.GreaterEquals);
        BeginCondition none = Condition(400, 0, 1, CompareType.Equals);
        BeginCondition one = Condition(1000, 1, 1, CompareType.Equals);
        Assert.Multiple(() => {
            Assert.That(four.Check(a, a, a), Is.False);
            Assert.That(none.Check(a, a, a), Is.True);
            Assert.That(one.Check(a, a, a), Is.False);
        });

        var mobs = new List<FieldNpc>();
        for (int i = 0; i < 4; i++) {
            mobs.Add(Mob(scene.Field, 100 + i, 100 + 100 * i));
        }
        Assert.Multiple(() => {
            Assert.That(four.Check(a, a, a), Is.True);
            Assert.That(none.Check(a, a, a), Is.False);
            Assert.That(one.Check(a, a, a), Is.False);
        });

        mobs[3].Position = new Vector3(950, 0, 0);
        Assert.That(four.Check(a, a, a), Is.False);
        for (int i = 1; i < 4; i++) {
            mobs[i].Position = new Vector3(2000, 0, 0);
        }
        Assert.That(one.Check(a, a, a), Is.True);
    }

    // Allies (friendly 2): 40356011 holds with exactly two others within 1.5 m.
    [Test]
    public void OwnerCondition_CountsAllies() {
        Scene scene = Start(100, 140);
        FieldPlayer a = scene.PlayerA;
        BeginCondition two = Condition(150, 2, 2, CompareType.Equals);
        Assert.That(two.Check(a, a, a), Is.True);
        scene.C.Session.Player.Position = new Vector3(160, 0, 0);
        Assert.That(two.Check(a, a, a), Is.False);
    }
}
