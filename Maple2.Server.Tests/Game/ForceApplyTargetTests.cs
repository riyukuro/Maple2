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

// forceApplyTarget: the target kinds a buff's holder's skills take on top of their own, OR'd over the enabled buffs
// (AdditionalCliList::FUN_140972a00) into the client's target query (FilterTarget). The charms (40500014 and the
// others, 5 s) give 2: the holder's hostile skills also take the other players. 1 would add the mobs to its friendly
// skills. Npc and pet casters are not affected.
public class ForceApplyTargetTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Charm = 40500014;

    private sealed class Scene {
        public required FieldManager Field;
        public required FieldPlayer A;
        public required FieldPlayer B;
        public required FieldNpc Mob;
    }

    // A at the origin, the other player B and a mob near it; no PvP on the field.
    private static Scene Start() {
        var a = new TestGameSession();
        a.Start();
        var tb = new TestGameSession(a.Options);
        tb.Start();
        FieldManager field = a.Session.Field!;
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(tb.Session.Player, 2);
        field.Players[2] = tb.Session.Player;
        tb.Session.Field = field;
        tb.Session.Player.Position = new Vector3(100, 0, 0);
        a.Session.Player.Position = Vector3.Zero;
        foreach (TestGameSession test in new[] { a, tb }) {
            typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, field);
        }
        return new Scene { Field = field, A = a.Session.Player, B = tb.Session.Player, Mob = Mob(field, 10, 200) };
    }

    // A living mob of the field at (x, 0, 0), with only what the target pool reads, and buffs it can hold.
    private static FieldNpc Mob(FieldManager field, int objectId, float x) {
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Property>k__BackingField", Any)!.SetValue(metadata,
            new NpcMetadataProperty([], new NpcMetadataCapsule(50, 100), null));
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(npc, new Npc(metadata, null));
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(npc, objectId);
        typeof(Actor<Npc>).GetField("<Transform>k__BackingField", Any)!.SetValue(npc, new Transform());
        typeof(Actor<Npc>).GetField("<Buffs>k__BackingField", Any)!.SetValue(npc, new BuffManager(npc));
        npc.Position = new Vector3(x, 0, 0);
        field.Mobs[objectId] = npc;
        return npc;
    }

    private static AdditionalEffectMetadata Effect(int id, int force) {
        return DisabledBuffTests.Effect(id, property: DisabledBuffTests.Property with { ForceApplyTarget = force });
    }

    private static ConcurrentDictionary<int, List<Buff>> Held(IActor actor) {
        return (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(actor.Buffs)!;
    }

    // The buff on the holder, as the field keeps it.
    private static void Give(IActor holder, int id, int force, bool enabled = true) {
        Buff buff = DisabledBuffTests.Held(Effect(id, force), enabled);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, holder);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, holder);
        Held(holder)[id] = [buff];
    }

    private static int[] Pool(Scene scene, IActor caster, ApplyTargetType type) {
        var prisms = new[] { new Prism(new Circle(Vector2.Zero, 500), -100, 300) };
        MethodInfo pool = typeof(FieldManager).GetMethod("GetTargetPool", Any)!;
        var targets = (IEnumerable<IActor>) pool.Invoke(scene.Field, [caster, prisms, type, 10, new List<IActor> { caster }])!;
        return targets.Select(target => target.ObjectId).ToArray();
    }

    // The bits of the enabled buffs, OR'd; a disabled buff gives none.
    [Test]
    public void Bits_OrOverEnabledBuffs() {
        Assert.Multiple(() => {
            Assert.That(BuffManager.ForceApplyTarget([DisabledBuffTests.Held(Effect(Charm, 2), true),
                DisabledBuffTests.Held(Effect(1, 1), true), DisabledBuffTests.Held(Effect(2, 0), true)]), Is.EqualTo(3));
            Assert.That(BuffManager.ForceApplyTarget([DisabledBuffTests.Held(Effect(Charm, 2), false)]), Is.EqualTo(0));
            Assert.That(BuffManager.ForceApplyTarget([]), Is.EqualTo(0));
        });
    }

    // A charmed player's hostile skills take the other player as well as the mob; its friendly skills are unchanged.
    // Without the charm, off PvP, they take the mob only.
    [Test]
    public void Charm_HostileTakesOtherPlayers() {
        Scene scene = Start();
        Assert.That(Pool(scene, scene.A, ApplyTargetType.Hostile), Is.EqualTo(new[] { scene.Mob.ObjectId }));

        Give(scene.A, Charm, 2);
        Assert.Multiple(() => {
            Assert.That(Pool(scene, scene.A, ApplyTargetType.Hostile), Is.EquivalentTo(new[] { scene.Mob.ObjectId, scene.B.ObjectId }));
            Assert.That(Pool(scene, scene.A, ApplyTargetType.Friendly), Is.EqualTo(new[] { scene.B.ObjectId }));
            // The other player's own skills stay as they were.
            Assert.That(Pool(scene, scene.B, ApplyTargetType.Hostile), Is.EqualTo(new[] { scene.Mob.ObjectId }));
        });
    }

    // Bit 1: the holder's friendly skills also take the mobs; its hostile ones are unchanged.
    [Test]
    public void Bit1_FriendlyTakesMobs() {
        Scene scene = Start();
        Give(scene.A, 1, 1);
        Assert.Multiple(() => {
            Assert.That(Pool(scene, scene.A, ApplyTargetType.Friendly), Is.EquivalentTo(new[] { scene.B.ObjectId, scene.Mob.ObjectId }));
            Assert.That(Pool(scene, scene.A, ApplyTargetType.Hostile), Is.EqualTo(new[] { scene.Mob.ObjectId }));
        });
    }

    // The window ends with the buff: once it is gone (or disabled), the pool is the plain one again.
    [Test]
    public void Charm_EndsWithTheBuff() {
        Scene scene = Start();
        Give(scene.A, Charm, 2);
        Assert.That(Pool(scene, scene.A, ApplyTargetType.Hostile), Has.Length.EqualTo(2));

        Held(scene.A).TryRemove(Charm, out _);
        Assert.That(Pool(scene, scene.A, ApplyTargetType.Hostile), Is.EqualTo(new[] { scene.Mob.ObjectId }));

        Give(scene.A, Charm, 2, enabled: false);
        Assert.That(Pool(scene, scene.A, ApplyTargetType.Hostile), Is.EqualTo(new[] { scene.Mob.ObjectId }));
    }

    // An npc caster holding the bits takes what it took before: hostile the players, friendly the mobs.
    [Test]
    public void NpcCaster_Unchanged() {
        Scene scene = Start();
        FieldNpc other = Mob(scene.Field, 11, 300);
        Give(scene.Mob, Charm, 3);
        Assert.Multiple(() => {
            Assert.That(Pool(scene, scene.Mob, ApplyTargetType.Hostile), Is.EquivalentTo(new[] { scene.A.ObjectId, scene.B.ObjectId }));
            Assert.That(Pool(scene, scene.Mob, ApplyTargetType.Friendly), Is.EqualTo(new[] { other.ObjectId }));
        });
    }
}
