using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// A region's hit on a spawned npc through FieldNpc.ApplyDamage (the entry Actor.ApplyDamage makes, the damage it takes
// and the hit it queues for the npc's movement), on the field TriggerNpcSpawnTests sets up.
public class RegionHitApplyTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MobId = 21000001;

    private FieldNpc SpawnMob(Vector3 position) {
        Start();
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field,
            TriggerNpcSpawnTests.NpcTable(TriggerNpcSpawnTests.Npc(MobId, 0)));
        Assert.That(Field.NpcMetadata.TryGet(MobId, out NpcMetadata? metadata), Is.True);
        return Field.SpawnNpc(metadata!, position, Vector3.Zero)!;
    }

    // value: a constant damage (no evasion roll, so the hit always lands).
    private static SkillMetadataAttack Attack(float rate, int pushType, long value = 0) {
        return (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: rate, HitSpeed: 1, HitDelay: 0, IsConstDamage: value > 0, Value: value,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false,
                Push: new SkillMetadataPush(Type: pushType, EaseType: 1, ApplyField: 1, Distance: 100, UpDistance: 0, Down: 0,
                    Fall: false, Duration: 0.4f, Probability: 1, Priority: 0, PriorityHitImmune: 0)),
        };
    }

    // The region of a magnetic bomb (10600062) at one fire point, cast by the session's player from casterPosition,
    // hits the npc: the npc's entry and the hits its movement got.
    private (DamageRecordTarget Hit, List<NpcControlRules.HitSource> Queued) Fire(FieldNpc npc, Vector3 casterPosition, SkillMetadataAttack attack,
            IActor? casterOverride = null) {
        IActor caster = casterOverride ?? test.Session.Player;
        if (casterOverride is null) {
            test.Session.Player.Position = casterPosition;
        }
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(10600062, 1, null, property, null!, null!);
        var region = new FieldSkill(Field, 900, caster, skill, 1100, new Vector3(100, 0, 0)) { Position = new Vector3(100, 0, 0) };
        DamageRecord damage = region.Damage(new SkillRecord(skill, 0, caster), attack);
        npc.ApplyDamage(caster, damage, attack);

        var pending = (NpcControlRules.PendingHits) npc.MovementState.GetType().GetField("pendingHits", Any)!.GetValue(npc.MovementState)!;
        var queued = new List<NpcControlRules.HitSource>();
        pending.Apply((source, _) => {
            queued.Add(source);
            return true;
        });
        return (damage.Targets[npc.ObjectId], queued);
    }

    // SkillDamage::HasDamage: any of value, constDamageByTargetHP, rate and damageByTargetMaxHP above 0.
    [Test]
    public void HasDamage_AnyOfTheFourAboveZero() {
        SkillMetadataDamage none = Attack(0, 4).Damage;
        Assert.Multiple(() => {
            Assert.That(DamageCalculator.HasDamage(none), Is.False);
            Assert.That(DamageCalculator.HasDamage(none with { Rate = -1, Value = -5 }), Is.False);
            Assert.That(DamageCalculator.HasDamage(none with { IsConstDamage = true }), Is.False);
            Assert.That(DamageCalculator.HasDamage(none with { Rate = 0.01f }), Is.True);
            Assert.That(DamageCalculator.HasDamage(none with { Value = 1 }), Is.True);
            Assert.That(DamageCalculator.HasDamage(none with { ConstDamageByTargetHp = 1 }), Is.True);
            Assert.That(DamageCalculator.HasDamage(none with { DamageByTargetMaxHp = 0.1f }), Is.True);
        });
    }

    // An attack with no rate, value, constDamageByTargetHP or damageByTargetMaxHP (SkillDamage::HasDamage false) takes
    // nothing: one hit of 0, the health kept. A total damage of 0 pushes nothing (PCBase::vfunc_107), so the npc stays.
    [Test]
    public void AHitWithoutDamageTakesNothingAndDoesNotMoveTheNpc() {
        FieldNpc npc = SpawnMob(new Vector3(400, 0, 0));
        long health = npc.Stats.Values[BasicAttribute.Health].Current;
        SkillMetadataAttack attack = Attack(0, 4);
        // superArmorBreak: never evaded, so the hit lands
        attack = attack with { Damage = attack.Damage with { SuperArmorBreak = true } };
        (DamageRecordTarget hit, List<NpcControlRules.HitSource> queued) = Fire(npc, new Vector3(600, 0, 0), attack);
        Assert.Multiple(() => {
            Assert.That(hit.Damage, Is.EqualTo(new[] { (DamageType.Normal, 0L) }));
            Assert.That(npc.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(health));
            Assert.That(queued, Is.Empty);
        });
    }

    // An attack without damage still rolls evasion: a caster with no accuracy (hit rate below 0) always misses, and the
    // miss takes nothing and moves nothing; a superArmorBreak attack is never evaded and comes back a hit of 0.
    [Test]
    public void AHitWithoutDamageRollsEvasion() {
        FieldNpc npc = SpawnMob(new Vector3(400, 0, 0));
        test.Session.Player.Stats.Values[BasicAttribute.Accuracy].Total = 0;
        (DamageRecordTarget missed, List<NpcControlRules.HitSource> queued) = Fire(npc, new Vector3(600, 0, 0), Attack(0, 4));
        SkillMetadataAttack armorBreak = Attack(0, 4);
        armorBreak = armorBreak with { Damage = armorBreak.Damage with { SuperArmorBreak = true } };
        (DamageRecordTarget landed, _) = Fire(npc, new Vector3(600, 0, 0), armorBreak);
        Assert.Multiple(() => {
            Assert.That(missed.Damage, Is.EqualTo(new[] { (DamageType.Miss, 0L) }));
            Assert.That(queued, Is.Empty);
            Assert.That(landed.Damage, Is.EqualTo(new[] { (DamageType.Normal, 0L) }));
        });
    }

    // A trap's attack without damage (cast by the field's own actor, which has no stats): no roll, a hit of 0 each time.
    [Test]
    public void ATrapHitWithoutDamageAlwaysLands() {
        FieldNpc npc = SpawnMob(new Vector3(400, 0, 0));
        var trap = new FieldActor(Field, 1, Field.Metadata, null!);
        Assert.That(trap.Stats.Values[BasicAttribute.Accuracy].Total, Is.EqualTo(0));
        for (int i = 0; i < 20; i++) {
            (DamageRecordTarget hit, List<NpcControlRules.HitSource> queued) = Fire(npc, Vector3.Zero, Attack(0, 4), trap);
            Assert.That(hit.Damage, Is.EqualTo(new[] { (DamageType.Normal, 0L) }));
            Assert.That(queued, Is.Empty);
        }
    }

    // A damaging type 4 hit pulls the npc onto the fire point.
    [Test]
    public void ADamagingHitPullsTheNpcToTheFirePoint() {
        FieldNpc npc = SpawnMob(new Vector3(400, 0, 0));
        (_, List<NpcControlRules.HitSource> queued) = Fire(npc, new Vector3(600, 0, 0), Attack(0, 4, value: 10));
        Assert.That(queued.Select(source => source.Origin), Is.EqualTo(new[] { new Vector3(100, 0, 0) }));
        Assert.That(NpcControlRules.PushOffset(4, queued[0], npc.Position, 100), Is.EqualTo(new Vector3(-300, 0, 0)));
    }

    // A damaging hit takes health and moves the npc; the caster is the attacker a type 2 pull stops 150 short of.
    [Test]
    public void ADamagingHitMovesTheNpcHeldShortOfTheCaster() {
        FieldNpc npc = SpawnMob(new Vector3(400, 0, 0));
        (DamageRecordTarget hit, List<NpcControlRules.HitSource> queued) = Fire(npc, new Vector3(300, 0, 0), Attack(0, 2, value: 10));
        Assert.Multiple(() => {
            Assert.That(hit.Damage, Is.EqualTo(new[] { (DamageType.Normal, 10L) }));
            Assert.That(queued.Single().Attacker, Is.EqualTo(new Vector3(300, 0, 0)));
        });
        // 100 from the caster: put back out to 150
        Assert.That(NpcControlRules.PushOffset(2, queued[0], npc.Position, 100).X, Is.EqualTo(50).Within(0.01));
    }

    // A caster at the map origin (a trigger's field actor) is no object the client finds: no attacker, so a type 2
    // pull takes the full pushdistance.
    [Test]
    public void ACasterAtTheMapOriginIsNoAttacker() {
        FieldNpc npc = SpawnMob(new Vector3(400, 0, 0));
        (_, List<NpcControlRules.HitSource> queued) = Fire(npc, Vector3.Zero, Attack(0, 2, value: 10));
        Assert.That(queued.Single().Attacker, Is.Null);
        Assert.That(NpcControlRules.PushOffset(2, queued[0], npc.Position, 100).X, Is.EqualTo(-100).Within(0.01));
    }
}
