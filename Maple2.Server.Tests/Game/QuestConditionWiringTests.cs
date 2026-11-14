using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Caching;
using Maple2.Database.Storage;
using Maple2.File.Ingest;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Model.Common;
using Maple2.Database.Context;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The quest conditions driven from the code that reports them, with the KR quest rows' conditions as the ingest
// reads them (QuestMapper: ConvertCodes of the code and the target). Each test fails when its report is taken out.
public class QuestConditionWiringTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int QuestId = 91000001;

    private static ConditionMetadata Condition(ConditionType type, long value, string[] code, string[] target) =>
        new(type, value == 0 ? 1 : value, code.ConvertCodes(), target.ConvertCodes());

    private Quest StartQuest(params ConditionMetadata[] conditions) =>
        test.StartQuest(TestGameSession.QuestData(QuestId) with { Conditions = conditions });

    #region npc kills (FieldNpc.HandleDamageDealers)
    // A field with the flat test navmesh and the given npcs, as TriggerNpcSpawnTests sets it up; the player is on it.
    private void StartField(int mapId, params NpcMetadata[] npcs) {
        Start();
        typeof(FieldManager).GetField("<MapId>k__BackingField", Any)!.SetValue(Field, mapId);
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field, NpcTable(npcs));
        test.TakeSent();
    }

    private static NpcMetadata Npc(int id, string[]? races = null) {
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 1, false, 0, 0, 0, 0, [], [], 0, RaceStrings: races);
        var stat = new NpcMetadataStat(new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = 100 }, [], [], [], []);
        return new NpcMetadata(id, "npc", "", new NpcMetadataModel("test_model", 1, 1), stat, basic,
            new NpcMetadataDistance(0, 0, 0, 0, 0, 0, 0), new NpcMetadataSkill([], 0),
            new NpcMetadataProperty([], new NpcMetadataCapsule(10, 100), null),
            new NpcMetadataDropInfo(0, 0, [], [], [], [], []), new NpcMetadataAction(0, 100, 100, [], 0, ""),
            new NpcMetadataDead(0, 0, 0, 0, 0), null, new NpcMetadataLookAtTarget("", false, false));
    }

    private static NpcMetadataStorage NpcTable(params NpcMetadata[] npcs) {
        var storage = (NpcMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadataStorage));
        var cache = new LRUCache<int, NpcMetadata>(100, 5);
        foreach (NpcMetadata npc in npcs) {
            cache.AddReplace(npc.Id, npc);
        }
        typeof(MetadataStorage<int, NpcMetadata>).GetField("Cache", Any)!.SetValue(storage, cache);
        var animations = new LRUCache<string, AnimationMetadata>(10, 1);
        animations.AddReplace("test_model", new AnimationMetadata("test_model", new Dictionary<string, AnimationSequenceMetadata>()));
        typeof(NpcMetadataStorage).GetField("AniCache", Any)!.SetValue(storage, animations);
        typeof(NpcMetadataStorage).GetField("tagLookup", Any)!.SetValue(storage, new Dictionary<string, HashSet<int>>());
        return storage;
    }

    // The npc dies with the player as its one damage dealer: the dealers' part of its death (HandleDamageDealers).
    private void Kill(int npcId) {
        Assert.That(Field.NpcMetadata.TryGet(npcId, out NpcMetadata? metadata), Is.True);
        FieldNpc npc = Field.SpawnNpc(metadata!, new Vector3(100, 0, 0), Vector3.Zero)!;
        var dealers = (ConcurrentDictionary<int, DamageRecordTarget>) typeof(Actor<Npc>).GetField("DamageDealers", Any)!.GetValue(npc)!;
        dealers[test.Session.Player.ObjectId] = new DamageRecordTarget(test.Session.Player);
        typeof(FieldNpc).GetMethod("HandleDamageDealers", Any)!.Invoke(npc, []);
    }

    // 10001420: <condition type="npc" code="22000002" value="1" />, a kill of that npc anywhere; another npc does not count.
    [Test]
    public void Npc_10001420_TheNpcsKill() {
        StartField(2000001, Npc(22000002), Npc(22000003));
        Quest quest = StartQuest(Condition(ConditionType.npc, 1, ["22000002"], []));

        Kill(22000003);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Kill(22000002);

        Assert.Multiple(() => {
            Assert.That(test.Errors, Is.Empty);
            Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
            Assert.That(test.TakeSentOps(), Does.Contain(SendOp.Quest));
        });
    }

    // 72000154: <condition type="npc" value="20" target="02000083" />, any npc's kill on that map; on another map none.
    [TestCase(2000083, 1)]
    [TestCase(2000084, 0)]
    public void Npc_72000154_AnyKillOnTheMap(int mapId, int counted) {
        StartField(mapId, Npc(21000001));
        Quest quest = StartQuest(Condition(ConditionType.npc, 20, [], ["02000083"]));

        Kill(21000001);

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(counted));
    }

    // 30000050: <condition type="npc_race" code="machine" value="100" />, a kill of an npc of that race.
    [Test]
    public void NpcRace_30000050_ARaceKill() {
        StartField(2000001, Npc(21000001, ["machine"]), Npc(21000002, ["animal"]), Npc(21000003));
        Quest quest = StartQuest(Condition(ConditionType.npc_race, 100, ["machine"], []));

        Kill(21000002);
        Kill(21000003);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Kill(21000001);

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }
    #endregion

    #region interact objects (InteractObjectHandler, the end of an interaction)
    private static InteractObjectMetadata InteractRow(int id, InteractType type) {
        return new InteractObjectMetadata(id, type, Collection: 0, ReactCount: 0, TargetPortalId: 0, GuildPosterId: 0, WeaponItemId: 0,
            new InteractObjectMetadataItem(0, 0, 0, 0, 0), new InteractObjectMetadataTime(0, 0, 0),
            new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);
    }

    // The player ends the interaction with a mesh object of the id (command 12).
    private void EndMesh(int objectId, string entityId) {
        test.AddInteract(InteractRow(objectId, InteractType.Mesh), new InteractMeshObject(entityId, new Ms2InteractMesh(objectId, Vector3.Zero, Vector3.Zero)));
        new InteractObjectHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteInt(1);
            writer.WriteString(entityId);
        }));
    }

    // 10001010: <condition type="interact_object" code="10000004,...,10000011" value="2" />, one count per object of
    // the list; another object does not count.
    [Test]
    public void InteractObject_10001010_TheListsObjects() {
        Start();
        Quest quest = StartQuest(Condition(ConditionType.interact_object, 2,
            ["10000004", "10000005", "10000006", "10000007", "10000008", "10000009", "10000010", "10000011"], []));

        EndMesh(10000012, "aa");
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        EndMesh(10000004, "ab");
        EndMesh(10000011, "ac");

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(2));
    }

    // 10003180: <condition type="interact_object_rep" code="20000237" value="1">, the object's use.
    [Test]
    public void InteractObjectRep_10003180_TheObjectsUse() {
        Start();
        Quest quest = StartQuest(Condition(ConditionType.interact_object_rep, 1, ["20000237"], []));

        EndMesh(20000238, "aa");
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        EndMesh(20000237, "ab");

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
    #endregion

    #region skills (SkillHandler, a cast's use)
    // A skill the field knows at level 1, with nothing to pay and no condition, as a cast of it goes through.
    private static SkillMetadata CastSkill(int id) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty)) with { MaxLevel = 1 };
        var condition = (BeginCondition) RuntimeHelpers.GetUninitializedObject(typeof(BeginCondition)) with {
            JobCode = [], DungeonGroupType = [], Stat = new Dictionary<BasicAttribute, long>(), Maps = [], MapTypes = [], Continents = [], ActiveSkill = [],
        };
        var motion = new SkillMetadataMotion(new SkillMetadataMotionProperty("", 1, 0, false), [], new Dictionary<string, byte>());
        var data = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Condition = condition,
            Consume = new SkillMetadataConsume(0, false, 0, new Dictionary<BasicAttribute, long>()),
            Skills = [],
            Motions = [motion],
        };
        var state = (SkillMetadataState) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataState));
        return new SkillMetadata(id, 1, null, property, state, data);
    }

    private void SetSkills(params SkillMetadata[] skills) {
        var storage = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(storage)!;
        foreach (SkillMetadata skill in skills) {
            cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(skill.Id, skill.Level), skill]);
        }
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(Field, storage);
        typeof(Maple2.Server.Game.Session.GameSession).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(test.Session, storage);
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        typeof(Actor<Player>).GetField("<ActiveSkills>k__BackingField", Any)!.SetValue(test.Session.Player, new SkillQueue());
        // The config as a login loads it, and a job tree without these skills (quest and item skills are in none).
        PacketHandlers.ProgressionOrderTests.LoadConfig(test, test.Session);
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [], [SkillRank.Awakening] = [],
            }, []),
        }));
        typeof(ConfigManager).GetField("hotBars", Any)!.SetValue(test.Session.Config, new List<HotBar> { new() });
        typeof(ConfigManager).GetField("skillPoints", Any)!.SetValue(test.Session.Config, new SkillPoint());
        typeof(ConfigManager).GetField(nameof(ConfigManager.Skill), Any)!.SetValue(test.Session.Config, new SkillManager(test.Session, new SkillBook()));
        // What a cast's timing check and its save read (the uninitialized config skips the field initializers).
        ConfigManager config = test.Session.Config;
        typeof(ConfigManager).GetField("session", Any)!.SetValue(config, test.Session);
        typeof(ConfigManager).GetField("skillCooldowns", Any)!.SetValue(config, new Dictionary<int, SkillCooldown>());
        typeof(ConfigManager).GetField("lastCasts", Any)!.SetValue(config, new Dictionary<int, SkillCastRules.LastCast>());
        typeof(ConfigManager).GetField(nameof(ConfigManager.CastClock), Any)!.SetValue(config, new ClockRate(ClockRate.CastBoundMs, ClockRate.CastBackMs));
        typeof(ConfigManager).GetField(nameof(ConfigManager.Lag), Any)!.SetValue(config, new LagBudget());
        typeof(ConfigManager).GetField(nameof(ConfigManager.CastLog), Any)!.SetValue(config, new LogGate());
        test.Session.Ride = new RideManager(test.Session);
        // No rig for the player here: the cast plays no sequence.
        test.Session.Animation = new AnimationManager((IActor) test.Session.Player);
        typeof(Actor<Player>).GetField("<Animation>k__BackingField", Any)!.SetValue(test.Session.Player, test.Session.Animation);
    }

    // The player's cast of the skill at level 1 (command 0, a use).
    private void Cast(int skillId, long uid) {
        var handler = new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! };
        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(uid);
            writer.WriteInt(test.Session.Field!.FieldTick > int.MaxValue ? 0 : (int) Field.FieldTick);
            writer.WriteInt(skillId);
            writer.WriteShort(1);
            writer.WriteByte(0);
            writer.Write(test.Session.Player.Position);
            writer.Write(Vector3.UnitY);
            writer.Write(Vector3.Zero);
            writer.WriteFloat(0);
            writer.WriteInt(0);
            writer.WriteBool(false);
            writer.WriteLong(0);
            writer.WriteLong(0);
        }));
    }

    // 10002676: <condition type="skill" code="90000023" value="10" />, each cast of the skill; another skill does not count.
    [Test]
    public void Skill_10002676_TheSkillsCasts() {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        SetSkills(CastSkill(90000023), CastSkill(90000024));
        Quest quest = StartQuest(Condition(ConditionType.skill, 10, ["90000023"], []));

        Cast(90000024, 1);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Cast(90000023, 2);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
    #endregion
}
