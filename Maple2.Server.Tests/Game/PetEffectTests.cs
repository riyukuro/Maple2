using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// A summoned pet's effects (the pet's <effect> rows, KR: 18 pets with one each). Every one is an event effect fired on
// the pet's own landed hits (ownerEvent 6, OnOwnerAttackHit): 10 give the pet's owner a buff (skillTarget 4, PetOwner:
// 61100010 gives 61100011, movement speed +5), 8 give the hit target a debuff (skillTarget 2: 61100020 gives 61100021,
// attack -2%). The client loads skillTarget and skillOwner only (additionalactionadditional.cpp:596-599) and has no
// reader of the pet's effect list, so the server holds the effects and gives their buffs. The KR rows' probabilities
// (0.036 .. 0.3) are 1 here.
public class PetEffectTests : NpcCastFixture {
    private const int OwnerCarrier = 61100010, OwnerBuff = 61100011, TargetCarrier = 61100020, TargetDebuff = 61100021;

    private FieldPet pet = null!;

    [TearDown]
    public void DisposePet() => pet?.Dispose();

    private static BeginCondition Always => new(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
        new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null);

    private static BeginConditionTarget OnHit => new([], new BeginConditionTarget.EventCondition(EventConditionType.OnOwnerAttackHit, false, [], [], []),
        [], [], [], new Dictionary<MasteryType, int>(), [], []);

    // The pet's own effect: unlimited (keepCondition 99), its event skill gives the buff to the skillTarget.
    private static AdditionalEffectMetadata Carrier(int id, int gives, SkillTargetType target) {
        var proc = new SkillEffectMetadata(0, [new SkillEffectMetadata.Skill(gives, 1)],
            new SkillEffectMetadataCondition(Always with { Owner = OnHit }, SkillTargetType.Owner, target, 0, false), null);
        return BuffAttributeTests.Effect(id, DisabledBuffTests.Property with { KeepCondition = BuffKeepCondition.UnlimitedDuration, DurationTick = 0 })
            with { Skills = [proc] };
    }

    private void Effects(params AdditionalEffectMetadata[] effects) {
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", Any)!.GetValue(skills)!;
        foreach (AdditionalEffectMetadata effect in effects) {
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(effect.Id, effect.Level), effect]);
        }
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(Field, skills);
    }

    // A pet of the player (or a wild one) beside the npc, with the given effects as its pet effects.
    private FieldPet Pet(bool owned, params int[] effectIds) {
        StartCaster(["Idle_A"]);
        Effects(Carrier(OwnerCarrier, OwnerBuff, SkillTargetType.PetOwner), Carrier(TargetCarrier, TargetDebuff, SkillTargetType.Target),
            BuffAttributeTests.Effect(OwnerBuff, DisabledBuffTests.Property with { DurationTick = 5000 }),
            BuffAttributeTests.Effect(TargetDebuff, DisabledBuffTests.Property with { Type = BuffType.Debuff, DurationTick = 5000 }));
        Assert.That(Field.NpcMetadata.TryGet(CasterId, out NpcMetadata? metadata), Is.True);
        var petMetadata = (PetMetadata) RuntimeHelpers.GetUninitializedObject(typeof(PetMetadata)) with {
            Id = CasterId, NpcId = CasterId, Effect = effectIds.Select(id => new PetMetadataEffect(id, 1)).ToArray(),
        };
        pet = new FieldPet(Field, 950, Field.Navigation.AddAgent(metadata!, new Vector3(900, 0, 0)), new Npc(metadata!, null),
            new Item(TestGameSession.Metadata(61100010, type: 11, slotMax: 1)), petMetadata, string.Empty, owned ? test.Session.Player : null) {
            Position = new Vector3(900, 0, 0), Origin = new Vector3(900, 0, 0),
        };
        Field.Pets[pet.ObjectId] = pet;
        return pet;
    }

    // One landed hit of the pet on the npc.
    private void PetHits() {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(61100001, 1, null, property, null!, null!);
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: 10,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null, ConstDamageByTargetHp: 0, ConstDamageByTargetSp: 0),
        };
        var record = new DamageRecord(skill, attack) { SkillId = 61100001 };
        npc.ApplyDamage(pet, record, attack);
        Assert.That(record.Targets[npc.ObjectId].Damage.Select(hit => hit.Type), Is.EqualTo(new[] { DamageType.Normal }));
    }

    [Test]
    public void ASummonedPetHoldsItsPetEffects() {
        Pet(owned: true, OwnerCarrier).AddPetEffects();
        Buff carrier = pet.Buffs.EnumerateBuffs(OwnerCarrier).Single();
        Assert.That(carrier.Caster, Is.SameAs(pet));
        pet.Buffs.Update(Field.FieldTick + 3_600_000);
        Assert.That(pet.Buffs.EnumerateBuffs(OwnerCarrier).Count(), Is.EqualTo(1), "keepCondition 99: it stays while the pet is out");
    }

    [Test]
    public void AWildPetGetsNoPetEffects() {
        Pet(owned: false, OwnerCarrier).AddPetEffects();
        Assert.That(pet.Buffs.EnumerateBuffs(), Is.Empty);
    }

    [Test]
    public void APetsHit_GivesItsOwnerThePetOwnerBuff() {
        Pet(owned: true, OwnerCarrier).AddPetEffects();
        PetHits();
        Assert.That(test.Errors, Is.Empty);
        Assert.Multiple(() => {
            Assert.That(test.Session.Player.Buffs.EnumerateBuffs(OwnerBuff).Select(buff => buff.Caster), Is.EqualTo(new IActor[] { pet }),
                "skillOwner 1: the pet, the effect's holder, casts it");
            Assert.That(npc.Buffs.EnumerateBuffs(OwnerBuff), Is.Empty);
            Assert.That(pet.Buffs.EnumerateBuffs(OwnerBuff), Is.Empty);
        });
    }

    [Test]
    public void APetsHit_GivesTheHitTargetTheTargetDebuff() {
        Pet(owned: true, TargetCarrier).AddPetEffects();
        PetHits();
        Assert.That(test.Errors, Is.Empty);
        Assert.Multiple(() => {
            Assert.That(npc.Buffs.EnumerateBuffs(TargetDebuff).Select(buff => buff.Caster), Is.EqualTo(new IActor[] { pet }));
            Assert.That(test.Session.Player.Buffs.EnumerateBuffs(TargetDebuff), Is.Empty);
        });
    }

    // A pet whose owner is not in its field (or a wild pet given the effect another way) has no one to give a PetOwner
    // buff to: nothing is given, and nothing throws (before, PetOwner threw NotImplementedException).
    [TestCase(true)]
    [TestCase(false)]
    public void APetOwnerBuffWithNoOwnerHere_GivesNothing(bool owned) {
        Pet(owned, OwnerCarrier);
        pet.Buffs.AddBuff(pet, pet, OwnerCarrier, 1, Field.FieldTick);
        if (owned) {
            typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, null);
        }
        PetHits();
        Assert.That(test.Errors, Is.Empty);
        if (owned) {
            typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        }
        Assert.That(test.Session.Player.Buffs.EnumerateBuffs(OwnerBuff), Is.Empty);
        Assert.That(npc.Buffs.EnumerateBuffs(OwnerBuff), Is.Empty);
    }

    // PetOwner names only a pet's owner: a buff of another holder with that skillTarget gives nothing (an npc's landed
    // hit on the player here, as Actor.ApplyDamage fires it).
    [Test]
    public void APetOwnerBuffOfAnNpc_GivesNothing() {
        Pet(owned: true);
        npc.Buffs.AddBuff(npc, npc, OwnerCarrier, 1, Field.FieldTick);
        npc.Buffs.TriggerEvent(npc, npc, test.Session.Player, EventConditionType.OnOwnerAttackHit, skillId: 61100001);
        Assert.That(test.Errors, Is.Empty);
        Assert.That(test.Session.Player.Buffs.EnumerateBuffs(OwnerBuff), Is.Empty);
        Assert.That(npc.Buffs.EnumerateBuffs(OwnerBuff), Is.Empty);
    }
}
