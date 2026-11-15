using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// OnAttacked (eventCondition 4): every landed hit, normal or critical, fires it on the one hit, with the attacker as
// the event's target. Shaped on Ranger 10500281: the mark on the hit one procs its dot only when the attacker holds
// its own 10500283 (target test with hasBuffOwner), and on Soul Binder 11100311, whose dot's caster is the attacker
// (skillOwner 5).
public class AttackedEventTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Mark = 10500281, Proc = 10500282, AttackerOwn = 10500283, AttackerDot = 11100312;

    private TestGameSession test = null!;
    private FieldManager Field => test.Session.Field!;
    private FieldPlayer Player => test.Session.Player;
    private FieldActor Attacker => Field.FieldActor;

    private static readonly AdditionalEffectMetadataProperty Plain = DisabledBuffTests.Property with { DurationTick = 10000 };

    private static BeginCondition Always => new(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
        new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null);

    private static BeginConditionTarget Block(EventConditionType type, params BeginConditionTarget.HasBuff[] buffs) =>
        new(buffs, new BeginConditionTarget.EventCondition(type, false, [], [], []), [], [], [], new Dictionary<MasteryType, int>(), [], []);

    // The mark's event skills: Proc on the holder when the attacker holds its own AttackerOwn, and AttackerDot on the
    // holder with the attacker as its caster.
    private static AdditionalEffectMetadata MarkEffect() {
        var proc = new SkillEffectMetadata(0, [new SkillEffectMetadata.Skill(Proc, 1)], new SkillEffectMetadataCondition(Always with {
            Owner = Block(EventConditionType.OnAttacked),
            Target = Block(EventConditionType.Activate, new BeginConditionTarget.HasBuff(AttackerOwn, 0, true, 0, CompareType.Equals)),
        }, SkillTargetType.Caster, SkillTargetType.Owner, 0, false), null);
        var dot = new SkillEffectMetadata(0, [new SkillEffectMetadata.Skill(AttackerDot, 1)], new SkillEffectMetadataCondition(Always with {
            Owner = Block(EventConditionType.OnAttacked),
        }, SkillTargetType.Attacker, SkillTargetType.Owner, 0, false), null);
        return BuffAttributeTests.Effect(Mark, Plain) with { Skills = [proc, dot] };
    }

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", Any)!.GetValue(skills)!;
        foreach (AdditionalEffectMetadata effect in new[] { BuffAttributeTests.Effect(Proc, Plain), BuffAttributeTests.Effect(AttackerDot, Plain) }) {
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(effect.Id, effect.Level), effect]);
        }
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(Field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), Any)!.SetValue(Field, new FieldActor(Field, 99, Field.Metadata, null!));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(Player, Field);
        typeof(Actor<Player>).GetField("DamageDealers", Any)!.SetValue(Player, new ConcurrentDictionary<int, DamageRecordTarget>());
        typeof(FieldPlayer).GetField("healthSync", Any)!.SetValue(Player, new HealthSyncClock());
        typeof(FieldPlayer).GetField("regenClock", Any)!.SetValue(Player, new RegenClock());
        Stat health = Player.Stats.Values[BasicAttribute.Health];
        health.AddTotal(10000 - health.Total);
        health.Add(10000 - health.Current);
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() => Attacker.Dispose();

    private static void Hold(IActor holder, IActor caster, AdditionalEffectMetadata metadata) {
        Buff buff = DisabledBuffTests.Held(metadata, enabled: true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, caster);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, holder);
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(holder.Buffs)!;
        buffs.GetOrAdd(metadata.Id, _ => []).Add(buff);
    }

    // A normal hit of 10 from the attacker.
    private void Hit() {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(10500001, 1, null, property, null!, null!);
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: 10,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null, ConstDamageByTargetHp: 0, ConstDamageByTargetSp: 0),
        };
        var record = new DamageRecord(skill, attack) { SkillId = 10500001 };
        Player.ApplyDamage(Attacker, record, attack);
        Assert.That(record.Targets[Player.ObjectId].Damage.Select(hit => hit.Type), Is.EqualTo(new[] { DamageType.Normal }));
    }

    [Test]
    public void ANormalHit_FiresTheMark_WithTheAttackerAsTarget() {
        Hold(Player, Attacker, MarkEffect());
        Hold(Attacker, Attacker, BuffAttributeTests.Effect(AttackerOwn, Plain));
        Hit();
        Assert.Multiple(() => {
            Assert.That(Player.Buffs.EnumerateBuffs(Proc).Count(), Is.EqualTo(1), "the attacker holds its own 10500283");
            Assert.That(Player.Buffs.EnumerateBuffs(Proc).Select(buff => buff.Caster), Is.EqualTo(new IActor[] { Attacker }),
                "skillOwner 3: the mark's caster casts the dot, not the marked holder");
            Assert.That(Player.Buffs.EnumerateBuffs(AttackerDot).Select(buff => buff.Caster), Is.EqualTo(new IActor[] { Attacker }),
                "skillOwner 5: the attacker casts the dot");
        });
    }

    // skillOwner 3 is the buff's caster even when another actor lands the hit: the mark's caster owns the proc.
    [Test]
    public void SkillOwnerCaster_IsTheMarksCaster_NotTheHolder() {
        using var marker = new FieldActor(Field, 98, Field.Metadata, null!);
        Hold(Player, marker, BuffAttributeTests.Effect(Mark, Plain) with {
            Skills = [new SkillEffectMetadata(0, [new SkillEffectMetadata.Skill(Proc, 1)], new SkillEffectMetadataCondition(Always with {
                Owner = Block(EventConditionType.OnAttacked),
            }, SkillTargetType.Caster, SkillTargetType.Owner, 0, false), null)],
        });
        Hit();
        Assert.That(Player.Buffs.EnumerateBuffs(Proc).Select(buff => buff.Caster), Is.EqualTo(new IActor[] { marker }));
    }

    // Without the attacker's own copy the mark's proc stays off; the attacker-cast dot still lands.
    [Test]
    public void ANormalHit_WithoutTheAttackersCopy_NoProc() {
        Hold(Player, Attacker, MarkEffect());
        Hit();
        Assert.Multiple(() => {
            Assert.That(Player.Buffs.EnumerateBuffs(Proc), Is.Empty);
            Assert.That(Player.Buffs.EnumerateBuffs(AttackerDot).Count(), Is.EqualTo(1));
        });
    }
}
