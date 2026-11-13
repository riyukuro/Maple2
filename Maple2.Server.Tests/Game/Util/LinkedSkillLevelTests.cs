using System;
using System.Collections.Concurrent;
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
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Util;

// An effect's skill with a linkSkillID takes the caster's learned level of the linked skill (Holy Place's buff
// 10400291 follows Healing Prayer 10400071), else the level the data names.
public class LinkedSkillLevelTests {
    private static readonly Dictionary<int, short> Learned = new() {
        [10400071] = 5,
        [10400301] = 0,
    };

    private static short? Lookup(int id) => Learned.TryGetValue(id, out short level) ? level : null;

    [Test]
    public void NoLink_KeepsTheDataLevel() {
        Assert.That(SkillCastRules.LinkedLevel(1, 0, Lookup), Is.EqualTo(1));
    }

    [Test]
    public void LearnedLink_TakesTheLearnedLevel() {
        Assert.That(SkillCastRules.LinkedLevel(1, 10400071, Lookup), Is.EqualTo(5));
    }

    [Test]
    public void UnlearnedLink_KeepsTheDataLevel() {
        Assert.That(SkillCastRules.LinkedLevel(1, 10400301, Lookup), Is.EqualTo(1));
    }

    [Test]
    public void CasterWithoutTheSkill_KeepsTheDataLevel() {
        Assert.That(SkillCastRules.LinkedLevel(2, 10400281, Lookup), Is.EqualTo(2));
        Assert.That(SkillCastRules.LinkedLevel(2, 10400281, _ => null), Is.EqualTo(2));
    }

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Linked = 10400281;
    private const int Region = 10400187;

    private static SkillMetadata Skill(int id, short level, SkillType type) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty)) with {
            Type = type,
            MaxLevel = 10,
        };
        var motion = new SkillMetadataMotion(new SkillMetadataMotionProperty("", 1, 0, false), [], new Dictionary<string, byte>());
        var data = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Skills = [],
            Motions = [motion],
        };
        return new SkillMetadata(id, level, null, property, null!, data);
    }

    // A player who has learned the linked skill to the given level; the field knows the region skill at levels 1 and 4.
    private static GameSession Caster(TestGameSession test, short learned) {
        GameSession session = test.Start();
        PacketHandlers.ProgressionOrderTests.LoadConfig(test, session);
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [new JobTable.Skill(Linked, [], 10, 0)],
                [SkillRank.Awakening] = [],
            }, []),
        }));
        var skills = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(skills)!;
        MethodInfo add = cache.GetType().GetMethod("AddReplace")!;
        add.Invoke(cache, [(Linked, (short) 1), Skill(Linked, 1, SkillType.Active)]);
        add.Invoke(cache, [(Region, (short) 1), Skill(Region, 1, SkillType.Active)]);
        add.Invoke(cache, [(Region, (short) 4), Skill(Region, 4, SkillType.Active)]);
        typeof(GameSession).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(session, skills);
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(session.Field, skills);
        typeof(ConfigManager).GetField("hotBars", Any)!.SetValue(session.Config, new List<HotBar> { new() });
        typeof(ConfigManager).GetField("skillPoints", Any)!.SetValue(session.Config, new SkillPoint());
        typeof(ConfigManager).GetField(nameof(ConfigManager.Skill), Any)!.SetValue(session.Config, new SkillManager(session, new SkillBook()));
        session.Config.Skill.SkillInfo.GetMainSkill(Linked)!.SetLevel(learned, notify: false);
        return session;
    }

    private static readonly SkillEffectMetadataSplash Splash = new(Interval: 0, Delay: 0, RemoveDelay: 0, UseDirection: true,
        ImmediateActive: false, NonTargetActive: false, OnlySensingActive: false, DependOnCasterState: false, Independent: false, Chain: null);

    private static short PlacedLevel(GameSession session, int linkId) {
        var effect = new SkillEffectMetadata(0, [new SkillEffectMetadata.Skill(Region, 1, linkId)], null, Splash);
        // The test field is made without its initializers.
        var placed = new ConcurrentDictionary<int, FieldSkill>();
        typeof(FieldManager).GetField("fieldSkills", Any)!.SetValue(session.Field, placed);
        session.Field!.AddSkill(session.Player, effect, [Vector3.Zero]);
        return placed.Values.Single(skill => skill.Value.Id == Region).Value.Level;
    }

    // 10400186's region 10400187 links 10400281: a priest at 4 of 10 places it at level 4 (its rates are 10400281's).
    [Test]
    public void Region_OfAPlayerCaster_TakesTheLinkedSkillsLevel() {
        Assert.That(PlacedLevel(Caster(new TestGameSession(), 4), Linked), Is.EqualTo(4));
    }

    [Test]
    public void Region_WithoutALink_KeepsTheDataLevel() {
        Assert.That(PlacedLevel(Caster(new TestGameSession(), 4), 0), Is.EqualTo(1));
    }

    [Test]
    public void Region_LinkedSkillNotLearned_KeepsTheDataLevel() {
        Assert.That(PlacedLevel(Caster(new TestGameSession(), 0), Linked), Is.EqualTo(1));
    }
}
