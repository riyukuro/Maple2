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
using Maple2.Tools.Collision;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game;

// A range's includeCaster adds the caster only when he passes the range's own target filter (CBaseCollision::Detect
// runs FilterTarget on him too): a friendly range takes him, a hostile-only one takes him only when his team is 2.
// 60036032 (Lazy Stumpy's splash, the actionSkill of 60036031) is a box of distance 40, width 80, height 140, rangeAdd
// (0, 50, 0), includeCaster 1, applyTarget 1, castTarget 1, 10 targets: it hits the players around Stumpy, not Stumpy.
public class IncludeCasterTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Charm = 40500014;

    private static readonly Prism[] Around = [new Prism(new Circle(Vector2.Zero, 500), -100, 300)];

    private sealed class Scene {
        public required FieldManager Field;
        public required FieldPlayer A;
        public required FieldNpc Mob;
    }

    // The player A at (30, 0, 0) and a mob of the given friendly at the origin.
    private static Scene Start(int friendly = 0) {
        var a = new TestGameSession();
        a.Start();
        FieldManager field = a.Session.Field!;
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(a.Session.Player, field);
        a.Session.Player.Position = new Vector3(30, 0, 0);
        return new Scene { Field = field, A = a.Session.Player, Mob = Mob(field, 10, friendly) };
    }

    // A living mob at the origin with only what the target search reads.
    private static FieldNpc Mob(FieldManager field, int objectId, int friendly) {
        var basic = (NpcMetadataBasic) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadataBasic));
        typeof(NpcMetadataBasic).GetField("<Friendly>k__BackingField", Any)!.SetValue(basic, friendly);
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Basic>k__BackingField", Any)!.SetValue(metadata, basic);
        typeof(NpcMetadata).GetField("<Property>k__BackingField", Any)!.SetValue(metadata,
            new NpcMetadataProperty([], new NpcMetadataCapsule(50, 100), null));
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(npc, new Npc(metadata, null));
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(npc, objectId);
        typeof(Actor<Npc>).GetField("<Transform>k__BackingField", Any)!.SetValue(npc, new Transform());
        typeof(Actor<Npc>).GetField("<Buffs>k__BackingField", Any)!.SetValue(npc, new BuffManager(npc));
        npc.Position = Vector3.Zero;
        field.Mobs[objectId] = npc;
        return npc;
    }

    // The 60036032 range, with the given includeCaster and applyTarget.
    private static SkillMetadataRange Splash(IncludeCasterType include = IncludeCasterType.Priority,
            ApplyTargetType apply = ApplyTargetType.Hostile) {
        return new SkillMetadataRange(SkillRegion.Box, 40, 140, 80, 0, 0, new Vector3(0, 50, 0), Vector3.Zero, include,
            apply, SkillTargetType.Owner, null);
    }

    private static int[] Targets(Scene scene, IActor caster, SkillMetadataRange range) {
        return scene.Field.GetTargets(caster, Around, range, 10).Select(target => target.ObjectId).ToArray();
    }

    // The KR row: a hostile mob's hostile splash with includeCaster 1 takes the player only, first or last.
    [Test]
    public void HostileSplash_LeavesTheMonsterOut() {
        Scene scene = Start();
        Assert.Multiple(() => {
            Assert.That(Targets(scene, scene.Mob, Splash()), Is.EqualTo(new[] { scene.A.ObjectId }));
            Assert.That(Targets(scene, scene.Mob, Splash(IncludeCasterType.Last)), Is.EqualTo(new[] { scene.A.ObjectId }));
            Assert.That(Targets(scene, scene.Mob, Splash(apply: ApplyTargetType.RegionBuff)), Has.No.Member(scene.Mob.ObjectId));
        });
    }

    // The friendly bit takes the caster (alone or with the hostile one, 3 and 7).
    [Test]
    public void FriendlyBit_TakesTheCaster() {
        Scene scene = Start();
        Assert.Multiple(() => {
            Assert.That(Targets(scene, scene.Mob, Splash(apply: ApplyTargetType.Friendly)).First(), Is.EqualTo(scene.Mob.ObjectId));
            Assert.That(Targets(scene, scene.Mob, Splash(apply: ApplyTargetType.Player1)).First(), Is.EqualTo(scene.Mob.ObjectId));
            Assert.That(Targets(scene, scene.Mob, Splash(apply: ApplyTargetType.Player4)).First(), Is.EqualTo(scene.Mob.ObjectId));
        });
    }

    // An npc of team 2 (friendly 2) is hostile to every side, himself too: his hostile range takes him.
    [Test]
    public void TeamTwoNpc_HostileTakesHimself() {
        Scene scene = Start(friendly: 2);
        MethodInfo takes = typeof(FieldManager).GetMethod("TakesCaster", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Multiple(() => {
            Assert.That(takes.Invoke(null, [scene.Mob, Splash()]), Is.True);
            Assert.That(takes.Invoke(null, [Start(friendly: 1).Mob, Splash()]), Is.False);
        });
    }

    // A player's hostile range leaves him out, unless a charm (forceApplyTarget 2) adds the friendly kind.
    [Test]
    public void Player_HostileLeavesHimOutUntilCharmed() {
        Scene scene = Start();
        Assert.That(Targets(scene, scene.A, Splash()), Is.EqualTo(new[] { scene.Mob.ObjectId }));

        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(scene.A.Buffs)!;
        Buff charm = DisabledBuffTests.Held(DisabledBuffTests.Effect(Charm,
            property: DisabledBuffTests.Property with { ForceApplyTarget = 2 }), true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(charm, scene.A);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(charm, scene.A);
        buffs[Charm] = [charm];
        Assert.That(Targets(scene, scene.A, Splash()).First(), Is.EqualTo(scene.A.ObjectId));
    }
}
