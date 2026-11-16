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
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// KR skill effect rows the other tests leave out: a skill's own effect with skillTarget 0 (351 KR rows, 10100051), an
// attack's effect with skillOwner 2 (307 KR rows, 10100041), and a skill hit on a player in a dungeon room, which counts
// the room's DamageBySkill missions (KR 20014004: hits by 50100784, 4 for the full penalty).
public class SkillEffectRowTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private FieldManager Field => test.Session.Field!;
    private FieldPlayer Player => test.Session.Player;
    private FieldActor Other => Field.FieldActor;

    private static BeginCondition Always => new(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
        new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null);

    private void Start(DungeonRoomMetadata? room = null) {
        test = new TestGameSession();
        test.Start();
        if (room != null) {
            test.EnterDungeon(room);
        } else {
            test.EnterMap(TestGameSession.Map(2000001));
        }
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", Any)!.GetValue(skills)!;
        foreach (int id in new[] { 10100041, 10100051 }) {
            AdditionalEffectMetadata effect = BuffAttributeTests.Effect(id, DisabledBuffTests.Property with { DurationTick = 10000 });
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
    public void TearDown() => Other.Dispose();

    private static SkillEffectMetadata Row(int buffId, SkillTargetType owner, SkillTargetType target) =>
        new(0, [new SkillEffectMetadata.Skill(buffId, 1)], new SkillEffectMetadataCondition(Always, owner, target, 0, false), null);

    // 10100051's own effect (skillTarget 0, skillOwner 1): the actor whose effects run holds it, whatever the target
    // (SkillHandler.HandleUse runs it on the caster with the caster as its target).
    [Test]
    public void SkillTargetNone_IsTheActorTheEffectsRunOn() {
        Start();
        Player.ApplyEffects([Row(10100051, SkillTargetType.Owner, SkillTargetType.None)], Player, Player, skillId: 10100051, targets: [Other]);
        Assert.That(Player.Buffs.EnumerateBuffs(10100051).Select(buff => buff.Caster), Is.EqualTo(new IActor[] { Player }));
    }

    // 10100041's attack effect (skillTarget 2, skillOwner 2): each hit target holds it, and its caster is the attack's
    // owner (Actor.TargetAttack names the caster as the effects' owner), as skillOwner 1 would.
    [Test]
    public void SkillOwnerTarget_IsTheAttacksCaster() {
        Start();
        Other.ApplyEffects([Row(10100041, SkillTargetType.Target, SkillTargetType.Target)], Other, Other, skillId: 10100041, targets: [Player]);
        Assert.That(Player.Buffs.EnumerateBuffs(10100041).Select(buff => buff.Caster), Is.EqualTo(new IActor[] { Other }));
        Assert.That(Other.Buffs.EnumerateBuffs(10100041), Is.Empty);
    }

    private void HitBy(int skillId) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(skillId, 1, null, property, null!, null!);
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: 10,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null, ConstDamageByTargetHp: 0, ConstDamageByTargetSp: 0),
        };
        Player.ApplyDamage(Other, new DamageRecord(skill, attack) { SkillId = skillId, Level = 1 }, attack);
    }

    // A hit by a listed skill counts the player's DamageBySkill mission and sends the mission's update; a hit by
    // another skill counts nothing.
    [Test]
    public void ADungeonSkillHit_CountsTheDamageBySkillMission() {
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(20014001, 2000001);
        Start(room);
        var table = new Dictionary<int, DungeonMissionMetadata> {
            [20014004] = new(20014004, DungeonMissionType.DamageBySkill, [50100784], 0, 40, 4, true),
        };
        var record = (DungeonRoomRecord) typeof(DungeonFieldManager).GetProperty(nameof(DungeonFieldManager.DungeonRoomRecord))!.GetValue(Field)!;
        DungeonUserRecord user = test.Session.Dungeon.UserRecord!;
        DungeonManager.FillMissions(user, [], [20014004], table);
        record.UserResults[test.CharacterId] = user;

        HitBy(50100785);
        Assert.That(user.Missions[20014004].Counter, Is.EqualTo(0));
        Assert.That(test.TakeSent().Select(TestGameSession.Op), Does.Not.Contain(SendOp.DungeonMission));

        HitBy(50100784);
        Assert.That(test.Errors, Is.Empty);
        Assert.That(user.Missions[20014004].Counter, Is.EqualTo(1));
        Assert.That(test.TakeSent().Select(TestGameSession.Op), Does.Contain(SendOp.DungeonMission));
    }
}
