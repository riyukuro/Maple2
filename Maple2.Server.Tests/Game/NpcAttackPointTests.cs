using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Model.State;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// An npc's attack point (SkillState.SkillCastAttack, which the npc AI's cast task fires at each of the motion's
// attack points): the server finds the targets in the attack's range at the npc, hits them and broadcasts the
// damage, as the client never searches for an npc's cast. A spawned mob at the origin facing +x and the session's
// player.
public class NpcAttackPointTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MobId = 21000001;
    private const int SkillId = 21000011;

    private FieldNpc mob = null!;
    private FieldPlayer Player => test.Session.Player;
    private long Health => Player.Stats.Values[BasicAttribute.Health].Current;

    private static readonly SkillMetadataArrow NoArrow = new(false, false, false, SkillTargetType.None, BounceType.None, 0, 0,
        false, default, default, 0);

    [SetUp]
    public void SpawnMob() {
        Start();
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(Player, Field);
        typeof(Actor<Player>).GetField("DamageDealers", Any)!.SetValue(Player, new ConcurrentDictionary<int, DamageRecordTarget>());
        typeof(FieldPlayer).GetField("healthSync", Any)!.SetValue(Player, new HealthSyncClock());
        typeof(FieldPlayer).GetField("regenClock", Any)!.SetValue(Player, new RegenClock());
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        // The debug marker items every npc's movement makes.
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        test.SetTable(new MagicPathTable(new Dictionary<long, IReadOnlyList<MagicPath>>()));
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field,
            TriggerNpcSpawnTests.NpcTable(TriggerNpcSpawnTests.Npc(MobId, 0)));
        Assert.That(Field.NpcMetadata.TryGet(MobId, out NpcMetadata? metadata), Is.True);
        mob = Field.SpawnNpc(metadata!, Vector3.Zero, Vector3.Zero)!;

        Stat health = Player.Stats.Values[BasicAttribute.Health];
        health.AddTotal(10000 - health.Total);
        health.Add(10000 - health.Current);
    }

    // A cylinder of radius 300 around the npc that takes hostile targets, and a constant hit of 300 (no evasion roll).
    private static SkillMetadataAttack Attack(int targetCount = 1, float distance = 300, ApplyTargetType apply = ApplyTargetType.Hostile) {
        return (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            Point = "Attack_01_A",
            TargetCount = targetCount,
            CompulsionTypes = [],
            Skills = [],
            SkillsOnDamage = [],
            Arrow = NoArrow,
            Range = new SkillMetadataRange(SkillRegion.Cylinder, distance, 150, 0, 0, 0, Vector3.Zero, Vector3.Zero,
                IncludeCasterType.Exclude, apply, SkillTargetType.None),
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: 300,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };
    }

    private SkillRecord Cast(params SkillMetadataAttack[] attacks) {
        var motion = new SkillMetadataMotion(new SkillMetadataMotionProperty("Attack_01_A", 1, 0, false), attacks, new Dictionary<string, byte>());
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with { Motions = [motion] };
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var cast = new SkillRecord(new SkillMetadata(SkillId, 1, null, property, null!, level), 501, mob);
        Assert.That(cast.TrySetMotionPoint(0), Is.True);
        return cast;
    }

    // The field's effect table: plain 10 s buffs of one stack with no begin condition.
    private void Effects(params int[] ids) {
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", Any)!.GetValue(skills)!;
        foreach (int id in ids) {
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(id, (short) 1), BuffAttributeTests.Effect(id, DisabledBuffTests.Property with { DurationTick = 10000 })]);
        }
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(Field, skills);
    }

    private static readonly BeginCondition Always = new(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
        new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null);

    // A conditionSkill row: the effects, given to the actor skillTarget names.
    private static SkillEffectMetadata Condition(SkillTargetType target, bool randomCast = false, params int[] ids) =>
        new(0, ids.Select(id => new SkillEffectMetadata.Skill(id, 1)).ToArray(),
            new SkillEffectMetadataCondition(Always, SkillTargetType.Owner, target, 1, randomCast), null);

    private int DamageSent() => test.TakeSent().Count(packet => TestGameSession.Op(packet) == SendOp.SkillDamage);

    // A player within the range takes the hit, and the field hears the damage.
    [Test]
    public void APlayerInRangeTakesTheHit() {
        Player.Position = new Vector3(200, 0, 0);
        SkillRecord cast = Cast(Attack());
        test.TakeSent();

        mob.SkillState.SkillCastAttack(cast, 0, []);

        Assert.That(Health, Is.EqualTo(9700));
        Assert.That(cast.Targets.Keys, Is.EqualTo(new[] { Player.ObjectId }));
        Assert.That(DamageSent(), Is.GreaterThanOrEqualTo(1));
    }

    // A player past the range is not hit, whatever targets the AI passed in: the range search decides.
    [Test]
    public void APlayerOutOfRangeIsNotHit() {
        Player.Position = new Vector3(500, 0, 0);
        SkillRecord cast = Cast(Attack());
        test.TakeSent();

        mob.SkillState.SkillCastAttack(cast, 0, [Player]);

        Assert.That(Health, Is.EqualTo(10000));
        Assert.That(cast.Targets, Is.Empty);
        Assert.That(DamageSent(), Is.Zero);
    }

    // An attack that takes friendly targets does not hit the npc's enemies.
    [Test]
    public void AFriendlyRangeDoesNotTakeThePlayer() {
        Player.Position = new Vector3(100, 0, 0);
        SkillRecord cast = Cast(Attack(apply: ApplyTargetType.Friendly));

        mob.SkillState.SkillCastAttack(cast, 0, []);

        Assert.That(Health, Is.EqualTo(10000));
    }

    // An attack point the motion does not have does nothing.
    [Test]
    public void AnAttackPointPastTheMotionDoesNothing() {
        Player.Position = new Vector3(100, 0, 0);
        SkillRecord cast = Cast(Attack());
        test.TakeSent();

        mob.SkillState.SkillCastAttack(cast, 1, []);

        Assert.That(Health, Is.EqualTo(10000));
        Assert.That(cast.AttackPoint, Is.Zero);
        Assert.That(DamageSent(), Is.Zero);
    }

    // conditionSkill with dependOnDamageCount (the attack's SkillsOnDamage): each target the hit damaged gets the
    // effect (skillTarget 2), or the npc itself (skillTarget 1, the owner) once the hit landed.
    [TestCase(SkillTargetType.Target, true, false)]
    [TestCase(SkillTargetType.Owner, false, true)]
    public void AnOnDamageEffectFollowsTheHit(SkillTargetType target, bool onPlayer, bool onMob) {
        Effects(70000001);
        Player.Position = new Vector3(200, 0, 0);
        SkillRecord cast = Cast(Attack() with { SkillsOnDamage = [Condition(target, ids: 70000001)] });

        mob.SkillState.SkillCastAttack(cast, 0, []);

        Assert.That(Health, Is.EqualTo(9700));
        Assert.That(Player.Buffs.EnumerateBuffs(70000001).Count > 0, Is.EqualTo(onPlayer));
        Assert.That(mob.Buffs.EnumerateBuffs(70000001).Count > 0, Is.EqualTo(onMob));
    }

    // A hit that dealt nothing (an evaded one: True Evasion 1000 evades every rolled hit) gives no on-damage effect.
    [Test]
    public void AnEvadedHitGivesNoOnDamageEffect() {
        Effects(70000001);
        Player.Position = new Vector3(200, 0, 0);
        Player.Stats.Values[SpecialAttribute.TrueEvasion].AddTotal(1000);
        SkillMetadataAttack attack = Attack() with {
            SkillsOnDamage = [Condition(SkillTargetType.Target, ids: 70000001)],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 1, HitSpeed: 0, HitDelay: 0, IsConstDamage: false, Value: 0,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };

        mob.SkillState.SkillCastAttack(Cast(attack), 0, []);

        Assert.That(Health, Is.EqualTo(10000));
        Assert.That(Player.Buffs.EnumerateBuffs(70000001), Is.Empty);
    }

    // An attack with no hits (damage count 0) gives its on-damage effects to the targets it took all the same.
    [Test]
    public void AnAttackWithoutHitsGivesItsOnDamageEffects() {
        Effects(70000001);
        Player.Position = new Vector3(200, 0, 0);
        SkillMetadataAttack attack = Attack() with {
            SkillsOnDamage = [Condition(SkillTargetType.Target, ids: 70000001)],
            Damage = new SkillMetadataDamage(Count: 0, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: false, Value: 0,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };

        mob.SkillState.SkillCastAttack(Cast(attack), 0, []);

        Assert.That(Health, Is.EqualTo(10000));
        Assert.That(Player.Buffs.EnumerateBuffs(70000001), Has.Count.EqualTo(1));
    }

    // randomCast: an effect row that lists several effects gives one of them, picked at random, where a plain row
    // gives them all.
    [Test]
    public void ARandomCastRowGivesOneOfItsEffects() {
        int[] ids = [70000001, 70000002, 70000003];
        Effects(ids);
        for (int i = 0; i < 10; i++) {
            foreach (int id in ids) {
                Player.Buffs.Remove(id, mob.ObjectId);
            }
            mob.ApplyEffect(mob, Player, Condition(SkillTargetType.Target, randomCast: true, ids), Field.FieldTick);
            Assert.That(ids.Count(id => Player.Buffs.EnumerateBuffs(id).Count > 0), Is.EqualTo(1));
        }

        mob.ApplyEffect(mob, Player, Condition(SkillTargetType.Target, randomCast: false, ids), Field.FieldTick);
        Assert.That(ids.Count(id => Player.Buffs.EnumerateBuffs(id).Count > 0), Is.EqualTo(3));
    }

    // Each attack point hits again, with its own target set: the second point's narrower range misses the player the
    // first one hit.
    [Test]
    public void EachAttackPointSearchesAgain() {
        Player.Position = new Vector3(200, 0, 0);
        SkillRecord cast = Cast(Attack(), Attack(distance: 100));

        mob.SkillState.SkillCastAttack(cast, 0, []);
        Assert.That(Health, Is.EqualTo(9700));
        mob.SkillState.SkillCastAttack(cast, 1, []);
        Assert.That(Health, Is.EqualTo(9700));
        Assert.That(cast.Targets, Is.Empty);

        Player.Position = new Vector3(50, 0, 0);
        mob.SkillState.SkillCastAttack(cast, 1, []);
        Assert.That(Health, Is.EqualTo(9400));
    }
}
