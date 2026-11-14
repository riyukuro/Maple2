using System;
using System.Collections.Generic;
using System.Reflection;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The passive wedding skills' effects (weddingskill.xml type passive) on a married player: the levels of the
// marriage's grade are given, a higher grade replaces them, and a marriage that no longer counts takes them.
public class WeddingSkillBuffTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // KR: effect id and its levels (maxCount + 1).
    private static readonly (int Id, short Levels)[] Effects = [(100000056, 5), (100000057, 4), (100000058, 3), (100000059, 2)];

    private void Setup() {
        Start();
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).GetField("EffectCache", Any)!.GetValue(skills)!;
        var always = new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, null);
        foreach ((int id, short levels) in Effects) {
            for (short level = 1; level <= levels; level++) {
                cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(id, level), DisabledBuffTests.Effect(id) with {
                    Level = level,
                    Condition = always,
                    Reflect = new AdditionalEffectMetadataReflect(0, 0, 0, 0, 0, 0, new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>()),
                    Update = new AdditionalEffectMetadataUpdate(null, [], [], [], []),
                    Consume = new AdditionalEffectMetadataConsume(0, 0),
                }]);
            }
        }
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(Field, skills);
        typeof(FieldManager).GetField(nameof(FieldManager.FieldActor), Any)!.SetValue(Field, new FieldActor(Field, 99, Field.Metadata, null!));
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        test.SetTable(new WeddingTable(new Dictionary<MarriageExpType, WeddingReward>(), new Dictionary<int, WeddingPackage>(),
            new Dictionary<int, WeddingSkill> {
                [1] = new WeddingSkill(1, WeddingSkillType.Passive, 1, 0, 0, 0, AdditionalEffectId: 100000056),
                [2] = new WeddingSkill(2, WeddingSkillType.Passive, 2, 0, 0, 0, AdditionalEffectId: 100000057),
                [3] = new WeddingSkill(3, WeddingSkillType.Passive, 3, 0, 0, 0, AdditionalEffectId: 100000058),
                [4] = new WeddingSkill(4, WeddingSkillType.Passive, 4, 0, 0, 0, AdditionalEffectId: 100000059),
                [5] = new WeddingSkill(5, WeddingSkillType.Item, 5, 20303183, 4, 1),
            },
            new Dictionary<int, long> { [1] = 0, [2] = 1300, [3] = 2600, [4] = 4000, [5] = 5400, [6] = 7000 }));
    }

    // The level held of each effect, 0 for none.
    private short[] Held() {
        var held = new short[Effects.Length];
        for (int i = 0; i < Effects.Length; i++) {
            for (short level = Effects[i].Levels; level >= 1; level--) {
                if (test.Session.Player.Buffs.HasBuff(Effects[i].Id, level)) {
                    held[i] = level;
                    break;
                }
            }
        }
        return held;
    }

    [Test]
    public void TheGradesLevelsAreHeldWhileTheMarriageCounts() {
        Setup();
        test.Session.Marriage.RefreshSkillBuffs();
        Assert.That(Held(), Is.EqualTo(new short[] { 0, 0, 0, 0 }));

        test.Session.Marriage.Marriage = new Marriage {
            Id = 1, Status = MaritalStatus.Married, Partner1 = new MarriagePartner(), Partner2 = new MarriagePartner(),
            ExpHistory = [new MarriageExp { Type = MarriageExpType.WeddingMissionGroom, Amount = 2600 }],
        };
        test.Session.Marriage.RefreshSkillBuffs();
        Assert.That(Held(), Is.EqualTo(new short[] { 3, 2, 1, 0 }));

        test.Session.Marriage.Marriage.ExpHistory.Add(new MarriageExp { Type = MarriageExpType.WeddingMissionBride, Amount = 4400 });
        test.Session.Marriage.RefreshSkillBuffs();
        Assert.That(Held(), Is.EqualTo(new short[] { 5, 4, 3, 2 }));

        test.Session.Marriage.Marriage.Status = MaritalStatus.DivorceFiledByBride;
        test.Session.Marriage.RefreshSkillBuffs();
        Assert.That(Held(), Is.EqualTo(new short[] { 5, 4, 3, 2 }));

        // A removed marriage's effects are taken on the field's thread.
        test.Session.Marriage.RemoveMarriage();
        Assert.That(Held(), Is.EqualTo(new short[] { 5, 4, 3, 2 }));
        test.RunFieldQueue();
        Assert.That(Held(), Is.EqualTo(new short[] { 0, 0, 0, 0 }));
    }
}
