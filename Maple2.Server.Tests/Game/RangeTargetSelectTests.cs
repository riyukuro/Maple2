using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Game;

// A skill range's target choice (targetSelectType with targetHasBuffID / targetHasBuffOwner / targetSelectParty /
// targetSelectGuild, and targetStatCompare / targetStatCompareFunction), applied to the whole pool before the target
// count is taken: 90000546 heals only the caster's party, 10600031 hits the targets with the caster's 10600151 first,
// 10400153 heals the ally with the lowest HP rate first.
public class RangeTargetSelectTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Scene {
        public required FieldManager Field;
        public required FieldPlayer A;
        public required FieldPlayer B;
        public required FieldPlayer C;
    }

    // A at the origin, B and C (other players, friendly to A) near it.
    private static Scene Start() {
        var a = new TestGameSession();
        a.Start();
        var tb = new TestGameSession(a.Options);
        tb.Start();
        var tc = new TestGameSession(a.Options);
        tc.Start();
        FieldManager field = a.Session.Field!;
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc>());
        int objectId = 2;
        foreach ((TestGameSession other, float x) in new[] { (tb, 100f), (tc, 200f) }) {
            typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(other.Session.Player, objectId);
            field.Players[objectId++] = other.Session.Player;
            other.Session.Field = field;
            other.Session.Player.Position = new Vector3(x, 0, 0);
        }
        a.Session.Player.Position = Vector3.Zero;
        foreach (TestGameSession test in new[] { a, tb, tc }) {
            typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, field);
            test.Session.Player.Value.Character.PartyId = 0;
            test.Session.Player.Value.Character.GuildId = 0;
        }
        return new Scene { Field = field, A = a.Session.Player, B = tb.Session.Player, C = tc.Session.Player };
    }

    private static SkillMetadataRange Range(SkillMetadataRangeSelect? select, IncludeCasterType includeCaster = IncludeCasterType.Exclude) {
        return new SkillMetadataRange(SkillRegion.Cylinder, 600, 150, 0, 0, 0, Vector3.Zero, Vector3.Zero, includeCaster,
            ApplyTargetType.Friendly, SkillTargetType.None, select);
    }

    private static SkillMetadataRangeSelect Select(int mode, int buffId = 0, bool fromCaster = false, bool party = false,
            bool guild = false, int statSort = 0) {
        return new SkillMetadataRangeSelect(mode, buffId, fromCaster, party, guild, BasicAttribute.Health, statSort);
    }

    private static readonly Prism[] Around = [new Prism(new Circle(Vector2.Zero, 600), -150, 300)];

    private static int[] Targets(Scene scene, SkillMetadataRange range, int count) {
        return scene.Field.GetTargets(scene.A, Around, range, count).Select(target => target.ObjectId).ToArray();
    }

    // The buff 10600151 on the target, cast by the given actor.
    private static void Mark(IActor target, IActor caster, bool enabled = true) {
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(target.Buffs)!;
        Buff buff = DisabledBuffTests.Held(DisabledBuffTests.Effect(10600151), enabled);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, caster);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, target);
        buffs[10600151] = [buff];
    }

    private static void Health(FieldPlayer player, long current, long total) {
        Stat health = player.Stats.Values[BasicAttribute.Health];
        health.Total = total;
        health.Current = current;
    }

    // Mode 2 with the party test: only the caster's party members, and no one at all for a caster with no party
    // (party 0 matches no one). The caster still comes first when the range includes it.
    [Test]
    public void PartyOnly_KeepsTheCastersParty() {
        Scene scene = Start();
        Assert.That(Targets(scene, Range(null), 5), Is.EquivalentTo(new[] { scene.B.ObjectId, scene.C.ObjectId }));
        Assert.That(Targets(scene, Range(Select(2, party: true)), 5), Is.Empty);

        scene.A.Value.Character.PartyId = 7;
        scene.B.Value.Character.PartyId = 7;
        scene.C.Value.Character.PartyId = 8;
        Assert.Multiple(() => {
            Assert.That(Targets(scene, Range(Select(2, party: true)), 5), Is.EqualTo(new[] { scene.B.ObjectId }));
            Assert.That(Targets(scene, Range(Select(2, party: true), IncludeCasterType.Priority), 5),
                Is.EqualTo(new[] { scene.A.ObjectId, scene.B.ObjectId }));
        });
    }

    // The guild test: the same non-zero guild.
    [Test]
    public void GuildOnly_KeepsTheCastersGuild() {
        Scene scene = Start();
        scene.A.Value.Character.GuildId = 30;
        scene.C.Value.Character.GuildId = 30;
        Assert.That(Targets(scene, Range(Select(2, guild: true)), 5), Is.EqualTo(new[] { scene.C.ObjectId }));
    }

    // Mode 3 with the caster's mark: the marked targets if there are any (the count is taken from them), else all.
    // Another caster's mark or a disabled one does not count; without targetHasBuffOwner anyone's does.
    [Test]
    public void MarkedFirst_FallsBackToAll() {
        Scene scene = Start();
        SkillMetadataRange own = Range(Select(3, 10600151, fromCaster: true));
        Assert.That(Targets(scene, own, 5), Is.EquivalentTo(new[] { scene.B.ObjectId, scene.C.ObjectId }));

        Mark(scene.C, scene.A);
        Assert.Multiple(() => {
            Assert.That(Targets(scene, own, 1), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Targets(scene, own, 5), Is.EqualTo(new[] { scene.C.ObjectId }));
        });

        Mark(scene.C, scene.B);
        Assert.Multiple(() => {
            Assert.That(Targets(scene, own, 5), Is.EquivalentTo(new[] { scene.B.ObjectId, scene.C.ObjectId }));
            Assert.That(Targets(scene, Range(Select(3, 10600151)), 5), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Targets(scene, Range(Select(2, 10600151)), 5), Is.EqualTo(new[] { scene.C.ObjectId }));
        });

        Mark(scene.C, scene.A, enabled: false);
        Assert.That(Targets(scene, Range(Select(2, 10600151, fromCaster: true)), 5), Is.Empty);
    }

    // targetHasNotBuffID (HasNotBuffId): a target with an enabled copy of the buff, whoever cast it, is no target, taken
    // out before the target count (so a count of 1 takes the next one), with or without a target choice.
    [Test]
    public void HasNotBuff_IsNoTarget() {
        Scene scene = Start();
        SkillMetadataRange plain = Range(null) with { HasNotBuffId = 10600151 };
        Mark(scene.B, scene.C);
        Assert.Multiple(() => {
            Assert.That(Targets(scene, plain, 5), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Targets(scene, plain, 1), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Targets(scene, Range(Select(0, statSort: 3)) with { HasNotBuffId = 10600151 }, 5), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Targets(scene, Range(null), 5), Is.EquivalentTo(new[] { scene.B.ObjectId, scene.C.ObjectId }), "no filter");
        });
        Mark(scene.B, scene.C, enabled: false);
        Assert.That(Targets(scene, plain, 5), Is.EquivalentTo(new[] { scene.B.ObjectId, scene.C.ObjectId }), "a disabled copy");
    }

    // applyTargetIgnoreNpcRanks (IgnoreNpcClasses): an npc whose class is listed is no target; other classes and players
    // are (60170002 lists 3, 4, 6 and 7).
    [Test]
    public void IgnoreNpcClasses_ExcludesTheListedClasses() {
        SkillMetadataRange range = Range(null) with { IgnoreNpcClasses = [3, 4, 6, 7] };
        Scene scene = Start();
        Assert.Multiple(() => {
            foreach (int npcClass in new[] { 3, 4, 6, 7 }) {
                Assert.That(RangeTargetSelect.Excludes(range, ClassNpc(npcClass)), Is.True, $"class {npcClass}");
            }
            foreach (int npcClass in new[] { 0, 1, 2, 5 }) {
                Assert.That(RangeTargetSelect.Excludes(range, ClassNpc(npcClass)), Is.False, $"class {npcClass}");
            }
            Assert.That(RangeTargetSelect.Excludes(Range(null), ClassNpc(3)), Is.False, "no list");
            Assert.That(RangeTargetSelect.Excludes(range, scene.B), Is.False, "a player");
            Assert.That(range.Filters() && !Range(null).Filters(), Is.True);
        });
    }

    // An npc with only its metadata's class.
    private static FieldNpc ClassNpc(int npcClass) {
        var basic = (NpcMetadataBasic) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadataBasic));
        typeof(NpcMetadataBasic).GetField("<Class>k__BackingField", Any)!.SetValue(basic, npcClass);
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Basic>k__BackingField", Any)!.SetValue(metadata, basic);
        var fieldNpc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(fieldNpc, new Npc(metadata, null));
        return fieldNpc;
    }

    // The stat sort: 3 picks the lowest HP rate first, 1 the lowest current HP, 2 and 4 the highest; with a count of 1
    // only the first is taken.
    [Test]
    public void StatSort_OrdersByHealth() {
        Scene scene = Start();
        Health(scene.B, 3000, 5000); // 60 %, 3000
        Health(scene.C, 1000, 1000); // 100 %, 1000
        Assert.Multiple(() => {
            Assert.That(Targets(scene, Range(Select(0, statSort: 3)), 1), Is.EqualTo(new[] { scene.B.ObjectId }));
            Assert.That(Targets(scene, Range(Select(0, statSort: 4)), 1), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Targets(scene, Range(Select(0, statSort: 1)), 1), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Targets(scene, Range(Select(0, statSort: 2)), 1), Is.EqualTo(new[] { scene.B.ObjectId }));
            Assert.That(Targets(scene, Range(Select(0, statSort: 3)), 5), Is.EqualTo(new[] { scene.B.ObjectId, scene.C.ObjectId }));
        });
    }

    // Modes other than 2 and 3 remove no one but put the passing targets first; a select with neither a filter nor a
    // stat sort is inactive.
    [Test]
    public void OtherModes_PutPassingFirst() {
        Scene scene = Start();
        scene.A.Value.Character.PartyId = 7;
        scene.C.Value.Character.PartyId = 7;
        Mark(scene.C, scene.A);
        TargetSort nearest = new(3, Vector3.Zero, 0);
        Assert.Multiple(() => {
            Assert.That(Ids(RangeTargetSelect.Apply(Select(1, party: true), scene.A, [scene.B, scene.C], SkillRegion.Cylinder, nearest)),
                Is.EqualTo(new[] { scene.C.ObjectId, scene.B.ObjectId }));
            Assert.That(Ids(RangeTargetSelect.Apply(Select(4, 10600151), scene.A, [scene.B, scene.C], SkillRegion.Cylinder, nearest)),
                Is.EqualTo(new[] { scene.C.ObjectId, scene.B.ObjectId }));
            Assert.That(Ids(RangeTargetSelect.Apply(Select(1, party: true), scene.A, [scene.C, scene.B], SkillRegion.Cylinder, nearest)),
                Is.EqualTo(new[] { scene.C.ObjectId, scene.B.ObjectId }));
            Assert.That(Targets(scene, Range(Select(1, 10600151)), 1), Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(Select(1, 10600151).Active(), Is.True);
            Assert.That(Select(0).Active(), Is.False);
            Assert.That(Select(3).Active(), Is.True);
            Assert.That(Select(0, statSort: 3).Active(), Is.True);
        });
    }

    private static int[] Ids(IEnumerable<IActor> targets) => targets.Select(target => target.ObjectId).ToArray();

    // A at the origin, B 100 to the search's right and 50 ahead, C 50 to its left and 200 ahead, for a search at the
    // origin facing 0 (ahead is -y, the right axis is -x).
    private static IActor[] Spread(Scene scene) {
        scene.B.Position = new Vector3(-100, -50, 0);
        scene.C.Position = new Vector3(50, -200, 0);
        return [scene.B, scene.C, scene.A];
    }

    private static int[] Sorted(Scene scene, IActor[] pool, int direction, SkillRegion shape, SkillMetadataRangeSelect? select = null) {
        return Ids(RangeTargetSelect.Apply(select, scene.A, pool, shape, new TargetSort(direction, Vector3.Zero, 0)));
    }

    // Box and frustum: 1 and 2 by the signed side offset (1 the most negative first), 3 and 4 by the forward
    // distance (3 nearest first). A cylinder measures neither, so 1 and 2 keep the pool order.
    [Test]
    public void Direction_BoxSideAndForward() {
        Scene scene = Start();
        IActor[] pool = Spread(scene);
        int a = scene.A.ObjectId, b = scene.B.ObjectId, c = scene.C.ObjectId;
        Assert.Multiple(() => {
            Assert.That(Sorted(scene, pool, 1, SkillRegion.Box), Is.EqualTo(new[] { c, a, b }));
            Assert.That(Sorted(scene, pool, 2, SkillRegion.Box), Is.EqualTo(new[] { b, a, c }));
            Assert.That(Sorted(scene, pool, 3, SkillRegion.Box), Is.EqualTo(new[] { a, b, c }));
            Assert.That(Sorted(scene, pool, 4, SkillRegion.Frustum), Is.EqualTo(new[] { c, b, a }));
            Assert.That(Sorted(scene, pool, 1, SkillRegion.Cylinder), Is.EqualTo(new[] { b, c, a }));
        });
    }

    // The side axis turns with the search: facing 90 the right axis is -y and ahead is +x.
    [Test]
    public void Direction_TurnsWithTheFacing() {
        Scene scene = Start();
        IActor[] pool = Spread(scene);
        var sort = new TargetSort(3, Vector3.Zero, 90);
        Assert.Multiple(() => {
            Assert.That(RangeTargetSelect.Measured(scene.C, SkillRegion.Box, sort).Forward, Is.EqualTo(50).Within(1e-3));
            Assert.That(RangeTargetSelect.Measured(scene.C, SkillRegion.Box, sort).Side, Is.EqualTo(200).Within(1e-3));
            Assert.That(Ids(RangeTargetSelect.Apply(null, scene.A, pool, SkillRegion.Box, sort)),
                Is.EqualTo(new[] { scene.A.ObjectId, scene.C.ObjectId, scene.B.ObjectId }));
        });
    }

    // Cylinder and hole cylinder: 3 and 4 by the distance to the target (a cylinder to the target's edge).
    [Test]
    public void Direction_CylinderDistance() {
        Scene scene = Start();
        IActor[] pool = Spread(scene);
        int a = scene.A.ObjectId, b = scene.B.ObjectId, c = scene.C.ObjectId;
        var sort = new TargetSort(3, Vector3.Zero, 0);
        Assert.Multiple(() => {
            Assert.That(Sorted(scene, pool, 3, SkillRegion.Cylinder), Is.EqualTo(new[] { a, b, c }));
            Assert.That(Sorted(scene, pool, 4, SkillRegion.HoleCylinder), Is.EqualTo(new[] { c, b, a }));
            Assert.That(RangeTargetSelect.Measured(scene.B, SkillRegion.Cylinder, sort).Distance,
                Is.EqualTo(System.MathF.Pow(System.MathF.Sqrt(12500) - 10, 2)).Within(0.1));
            Assert.That(RangeTargetSelect.Measured(scene.A, SkillRegion.Cylinder, sort).Distance, Is.Zero);
            Assert.That(RangeTargetSelect.Measured(scene.B, SkillRegion.HoleCylinder, sort).Distance,
                Is.EqualTo(System.MathF.Sqrt(12500)).Within(1e-3));
        });
    }

    // 5 and 6 by how straight ahead the target stands (5 most first); the target on the origin counts 0.
    [Test]
    public void Direction_Facing() {
        Scene scene = Start();
        IActor[] pool = Spread(scene);
        int a = scene.A.ObjectId, b = scene.B.ObjectId, c = scene.C.ObjectId;
        Assert.Multiple(() => {
            Assert.That(Sorted(scene, pool, 5, SkillRegion.Cylinder), Is.EqualTo(new[] { c, b, a }));
            Assert.That(Sorted(scene, pool, 6, SkillRegion.Box), Is.EqualTo(new[] { a, b, c }));
        });
    }

    // Ties keep the order the client's exchange sort leaves, which is not the pool order: B and C both 50 ahead.
    [Test]
    public void Direction_TiesFollowTheExchangeSort() {
        Scene scene = Start();
        scene.B.Position = new Vector3(-100, -50, 0);
        scene.C.Position = new Vector3(100, -50, 0);
        Assert.That(Sorted(scene, [scene.B, scene.C, scene.A], 3, SkillRegion.Box),
            Is.EqualTo(new[] { scene.A.ObjectId, scene.C.ObjectId, scene.B.ObjectId }));
    }

    // 0 shuffles the pool the client's way (from the last place down, each swapped with a random place up to it).
    [Test]
    public void Direction_ShuffleIsFisherYates() {
        Scene scene = Start();
        IActor[] pool = Spread(scene);
        var expected = pool.ToList();
        var random = new System.Random(5);
        for (int i = expected.Count - 1; i >= 1; i--) {
            int j = random.Next(i + 1);
            (expected[i], expected[j]) = (expected[j], expected[i]);
        }
        IEnumerable<IActor> shuffled = RangeTargetSelect.Apply(null, scene.A, pool, SkillRegion.Box,
            new TargetSort(0, Vector3.Zero, 0), new System.Random(5));
        Assert.That(Ids(shuffled), Is.EqualTo(Ids(expected)));
    }

    // A stat sort replaces the direction order.
    [Test]
    public void StatSort_OverridesDirection() {
        Scene scene = Start();
        IActor[] pool = Spread(scene);
        Health(scene.A, 500, 1000);
        Health(scene.B, 900, 1000);
        Health(scene.C, 100, 1000);
        Assert.That(Sorted(scene, pool, 4, SkillRegion.Box, Select(0, statSort: 3)),
            Is.EqualTo(new[] { scene.C.ObjectId, scene.A.ObjectId, scene.B.ObjectId }));
    }

    // Through the field: the count is taken after the order, and the search's origin is the cast position plus the
    // range offset turned to the facing, its facing the cast's plus the range's z rotation.
    [Test]
    public void GetTargets_TakesTheCountAfterTheOrder() {
        Scene scene = Start();
        SkillMetadataRange range = Range(null);
        int[] farthest = scene.Field.GetTargets(scene.A, Around, range, 1, sort: new TargetSort(4, Vector3.Zero, 0))
            .Select(target => target.ObjectId).ToArray();
        int[] nearest = scene.Field.GetTargets(scene.A, Around, range, 1, sort: new TargetSort(3, Vector3.Zero, 0))
            .Select(target => target.ObjectId).ToArray();
        TargetSort turned = (range with { RangeOffset = new Vector3(0, 100, 5), RotateZDegree = 30 })
            .GetTargetSort(5, new Vector3(10, 0, 0), 90);
        Assert.Multiple(() => {
            Assert.That(farthest, Is.EqualTo(new[] { scene.C.ObjectId }));
            Assert.That(nearest, Is.EqualTo(new[] { scene.B.ObjectId }));
            Assert.That(turned.Direction, Is.EqualTo(5));
            Assert.That(turned.Degrees, Is.EqualTo(120));
            // Facing 90 is forward (1, 0), so the offset's y of 100 moves the origin from x 10 to 110.
            Assert.That(turned.Origin.X, Is.EqualTo(110).Within(1e-3));
            Assert.That(turned.Origin.Y, Is.EqualTo(0).Within(1e-3));
            Assert.That(turned.Origin.Z, Is.EqualTo(5).Within(1e-3));
        });
    }

    // The offset's y runs along the facing and its x along the right axis (the client's MakeZRotation(pi - r)): at
    // facing 0, forward is (0, -1) and right (-1, 0).
    [Test]
    public void GetTargetSort_OffsetIsInFront() {
        SkillMetadataRange range = Range(null) with { RangeOffset = new Vector3(0, 150, 0) };
        TargetSort ahead = range.GetTargetSort(3, Vector3.Zero, 0);
        TargetSort aside = (range with { RangeOffset = new Vector3(40, 0, 0) }).GetTargetSort(3, Vector3.Zero, 0);
        Assert.Multiple(() => {
            Assert.That(ahead.Origin.X, Is.EqualTo(0).Within(1e-3));
            Assert.That(ahead.Origin.Y, Is.EqualTo(-150).Within(1e-3));
            Assert.That(aside.Origin.X, Is.EqualTo(-40).Within(1e-3));
            Assert.That(aside.Origin.Y, Is.EqualTo(0).Within(1e-3));
        });
    }

    // An attack of method type 1 searches from the cast position with the plain facing.
    [Test]
    public void GetTargetSort_ArrowAttackKeepsThePosition() {
        SkillMetadataRange range = Range(null) with { RangeOffset = new Vector3(0, 150, 0), RotateZDegree = 30 };
        SkillMetadataAttack Attack(int method) => new("p0", 0, 1, 0, 0, 0, 0, [], null, range, null!, null!, [], [],
            Direction: 4, AttackMethodType: method);
        TargetSort arrow = Attack(1).GetTargetSort(new Vector3(10, 20, 0), 90);
        TargetSort plain = Attack(0).GetTargetSort(new Vector3(10, 20, 0), 90);
        Assert.Multiple(() => {
            Assert.That(arrow, Is.EqualTo(new TargetSort(4, new Vector3(10, 20, 0), 90)));
            Assert.That(plain.Direction, Is.EqualTo(4));
            Assert.That(plain.Degrees, Is.EqualTo(120));
            Assert.That(plain.Origin.X, Is.EqualTo(160).Within(1e-3));
            Assert.That(plain.Origin.Y, Is.EqualTo(20).Within(1e-3));
        });
    }

    // The attack's area stands where its search does: the offset turned to the facing and the facing plus the range's z
    // rotation; an attack of method type 1 takes neither. A box 50 wide, 100 deep, its offset 150 ahead.
    [Test]
    public void GetPrism_OffsetIsInFront() {
        SkillMetadataRange range = Range(null) with {
            Type = SkillRegion.Box, Distance = 100, Width = 50, RangeOffset = new Vector3(0, 150, 0),
        };
        SkillMetadataAttack Attack(int method, float rotate = 0) => new("p0", 0, 1, 0, 0, 0, 0, [], null,
            range with { RotateZDegree = rotate }, null!, null!, [], [], AttackMethodType: method);
        Prism ahead = Attack(0).GetPrism(Vector3.Zero, 0);
        Prism east = Attack(0).GetPrism(Vector3.Zero, 90);
        Prism turned = Attack(0, rotate: 90).GetPrism(Vector3.Zero, 0);
        Prism arrow = Attack(1, rotate: 90).GetPrism(Vector3.Zero, 0);
        Assert.Multiple(() => {
            // Facing 0 is forward (0, -1): the box runs from y -150 to -250.
            Assert.That(ahead.Contains(new Vector3(0, -200, 10)), Is.True);
            Assert.That(ahead.Contains(new Vector3(0, 200, 10)), Is.False, "not behind");
            Assert.That(ahead.Contains(new Vector3(0, -50, 10)), Is.False, "not at the caster");
            // Facing 90 is forward (1, 0).
            Assert.That(east.Contains(new Vector3(200, 0, 10)), Is.True);
            Assert.That(east.Contains(new Vector3(0, -200, 10)), Is.False);
            // The z rotation turns the box, not the offset: from (0, -150) it runs along (1, 0).
            Assert.That(turned.Contains(new Vector3(50, -150, 10)), Is.True);
            Assert.That(turned.Contains(new Vector3(0, -200, 10)), Is.False);
            // Method type 1: at the caster, facing (0, -1).
            Assert.That(arrow.Contains(new Vector3(0, -50, 10)), Is.True);
            Assert.That(arrow.Contains(new Vector3(0, -200, 10)), Is.False);
        });
    }

    // A region skill's area keeps the offset as it is, not turned to the facing: the client never searches for it.
    [Test]
    public void GetRegionPrism_KeepsTheOffset() {
        SkillMetadataRange range = Range(null) with {
            Type = SkillRegion.Box, Distance = 100, Width = 50, RangeOffset = new Vector3(0, 150, 0),
        };
        SkillMetadataAttack attack = new("p0", 0, 1, 0, 0, 0, 0, [], null, range, null!, null!, [], []);
        Prism region = attack.GetRegionPrism(Vector3.Zero, 0);
        Assert.Multiple(() => {
            // From (0, 150) along (0, -1): y 150 down to 50.
            Assert.That(region.Contains(new Vector3(0, 100, 10)), Is.True);
            Assert.That(region.Contains(new Vector3(0, -200, 10)), Is.False);
        });
    }

    // An arrow step leaves out its excluded targets before the range's target choice, which it makes once, then puts
    // the hits in the step's order: B 100 and C 200 along a travel facing 90 (forward (1, 0)).
    [Test]
    public void GetStepTargets_ExcludesBeforeTheChoiceAndSorts() {
        Scene scene = Start();
        int b = scene.B.ObjectId, c = scene.C.ObjectId;
        int[] Step(SkillMetadataRange range, int direction, int? excluded = null) => Ids(scene.Field.GetStepTargets(scene.A, Around[0], range,
            target => target.ObjectId != excluded, new TargetSort(direction, Vector3.Zero, 90)));
        Mark(scene.B, scene.A);
        SkillMetadataRange marked = Range(Select(3, 10600151, fromCaster: true));
        Assert.Multiple(() => {
            Assert.That(Step(Range(null), 3), Is.EqualTo(new[] { b, c }), "nearest ahead first");
            Assert.That(Step(Range(null), 4), Is.EqualTo(new[] { c, b }), "farthest first");
            Assert.That(Step(marked, 4), Is.EqualTo(new[] { b }), "only the marked one");
            Assert.That(Step(marked, 4, excluded: b), Is.EqualTo(new[] { c }), "the marked one is left out, so all");
        });
    }
}
