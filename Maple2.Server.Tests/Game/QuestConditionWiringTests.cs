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
using Maple2.Model.Metadata.FieldEntity;
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
        // A spawn checks the field's broken breakables (none here).
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
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
    #region map (a portal move)
    private static MapMetadata LimitedMap(int id) =>
        TestGameSession.Map(id) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) };

    // A channel's field factory that already runs the given field.
    private static FieldManager.Factory RunningFactory(TestGameSession test, FieldManager running) {
        var factory = (FieldManager.Factory) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager.Factory));
        void Set(string name, object value) => typeof(FieldManager.Factory).GetField(name, Any)!.SetValue(factory, value);
        Set("mapLocks", new ConcurrentDictionary<int, SemaphoreSlim>());
        Set("fields", new ConcurrentDictionary<int, ConcurrentDictionary<int, FieldManager>> {
            [running.MapId] = new ConcurrentDictionary<int, FieldManager> { [0] = running },
        });
        Set("<ServerTableMetadata>k__BackingField", test.ServerTableMetadata);
        return factory;
    }

    // 10001001: <condition type="map" code="02000062" />. A portal move into the map is one entry: the portal
    // itself does not count, the field entry that follows does (GameSession.EnterField counts every entry, a portal's
    // included). Before, the portal counted too, so a move by portal counted twice.
    [Test]
    public void Map_10001001_APortalMoveIsNotCountedByThePortal() {
        Start();
        test.EnterMap(LimitedMap(2000062));
        FieldManager target = test.Session.Field!;
        test.EnterMap(LimitedMap(2000001));
        SetSkills();
        typeof(Maple2.Server.Game.Session.GameSession).GetField("<FieldFactory>k__BackingField", Any)!
            .SetValue(test.Session, RunningFactory(test, target));
        // The two fields' entity tables and locks (the test fields leave them unset).
        foreach (FieldManager field in new[] { target, Field }) {
            foreach (FieldInfo info in typeof(FieldManager).GetFields(Any)) {
                if (info.GetValue(field) == null
                    && (info.FieldType == typeof(object) || info.FieldType.IsGenericType
                        && info.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))) {
                    info.SetValue(field, Activator.CreateInstance(info.FieldType));
                }
            }
        }
        // What a field change clears on the session (the uninitialized session skips the field initializers).
        void SetSession(string name, object value) =>
            typeof(Maple2.Server.Game.Session.GameSession).GetField(name, Any)!.SetValue(test.Session, value);
        SetSession(nameof(test.Session.ItemLockStaging), new long[1]);
        SetSession(nameof(test.Session.DismantleStaging), new (long, int)[1]);
        SetSession(nameof(test.Session.InteractStarted), new HashSet<string>());
        SetSession(nameof(test.Session.CubeStarted), new HashSet<string>());
        SetSession(nameof(test.Session.Scheduler), new Maple2.Tools.Scheduler.EventQueue(Serilog.Log.Logger));
        FieldPortal portal = Field.SpawnPortal(new Portal(3, 2000062, 1, PortalType.Field, PortalActionType.Touch, Vector3.Zero, Vector3.Zero,
            Vector3.Zero, 0, 0, true, true, true));
        Quest quest = StartQuest(Condition(ConditionType.map, 1, ["02000062"], []));

        // The arrival portal (1) is not one of the map's: the player comes in at the default place.
        var entities = (MapEntityMetadata) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        typeof(MapEntityMetadata).GetField("<Portals>k__BackingField", Any)!.SetValue(entities, new Dictionary<int, Portal>());
        typeof(FieldManager).GetField("<Entities>k__BackingField", Any)!.SetValue(target, entities);
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(target,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, false, 0, 0, 0, 0));
        test.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata>()));
        test.SetTable(new CharacterAbilityTable(new Dictionary<int, CharacterAbilityTable.Entry>()));
        test.SetTable(new PremiumClubTable(new Dictionary<int, PremiumClubTable.Buff>(), new Dictionary<int, PremiumClubTable.Item>(),
            new Dictionary<int, PremiumClubTable.Package>()));
        test.SetTable(new InsigniaTable(new Dictionary<int, InsigniaTable.Entry>()));
        // The config's empty tables the arrival's buff refresh reads (lapenshards, abilities and the like).
        foreach (FieldInfo info in typeof(ConfigManager).GetFields(Any)) {
            if (info.GetValue(test.Session.Config) != null || !info.FieldType.IsGenericType) {
                continue;
            }
            Type type = info.FieldType;
            Type generic = type.GetGenericTypeDefinition();
            if (generic == typeof(IDictionary<,>) || generic == typeof(IReadOnlyDictionary<,>)) {
                type = typeof(Dictionary<,>).MakeGenericType(type.GetGenericArguments());
            } else if (generic == typeof(IList<>) || generic == typeof(ICollection<>) || generic == typeof(IReadOnlyList<>)) {
                type = typeof(List<>).MakeGenericType(type.GetGenericArguments());
            }
            if (!type.IsInterface && type.GetConstructor(Type.EmptyTypes) != null) {
                info.SetValue(test.Session.Config, Activator.CreateInstance(type));
            }
        }
        typeof(GuildManager).GetField("session", Any)!.SetValue(test.Session.Guild, test.Session); // no guild
        Field.Players.Clear(); // the leave of the old field is not what is asked here
        typeof(FieldPlayer).GetField("scheduler", Any)!.SetValue(test.Session.Player, new Maple2.Tools.Scheduler.EventQueue(Serilog.Log.Logger));
        Assert.That(Field.UsePortal(test.Session, portal.Value.Id, ""), Is.True);

        Assert.Multiple(() => {
            Assert.That(test.Session.Field, Is.SameAs(target));
            Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        });
    }
    #endregion
    #region fish (FishingManager.Catch, a catch)
    // 40001960: <condition type="fish" value="3" />, each fish caught counts one; a lost cast does not.
    [Test]
    public void Fish_40001960_EachCatch() {
        Start();
        test.EnterMap(LimitedMap(2000001));
        SetSkills();
        var fish = new FishTable.Fish(10000001, LiquidType.water, 80, 6, 1, 3, 1, 54, 15000, 10000, 8000,
            new FishTable.Range<int>(20, 30), new FishTable.Range<int>(40, 50), [], 0, false);
        test.SetServerTable(new FishTable(new Dictionary<int, FishTable.Fish> { [fish.Id] = fish },
            new Dictionary<int, FishTable.Spot> { [2000001] = new(2000001, 0, 1189, [LiquidType.water], 0, 0, 0, 0, 1, 1) },
            new Dictionary<int, IReadOnlyDictionary<short, FishTable.Lure>>(), new Dictionary<int, FishTable.FishBox>(),
            new Dictionary<int, FishTable.FishBox>()));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        test.Session.Fishing = new FishingManager(test.Session, test.TableMetadata, test.ServerTableMetadata);
        var tile = new FishingTile((FieldFluidEntity) RuntimeHelpers.GetUninitializedObject(typeof(FieldFluidEntity)), 0);
        Quest quest = StartQuest(Condition(ConditionType.fish, 3, [], []));

        test.Session.Fishing.Cast(tile, fish, fishBites: false, fishBiteTick: 0, fightGame: false);
        test.Session.Fishing.Catch(true);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        test.Session.Fishing.Cast(tile, fish, fishBites: true, fishBiteTick: 0, fightGame: false);
        test.Session.Fishing.Catch(true);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
    #endregion
    #region controller (FunctionCubeHandler, furniture taken into use)
    // 10002669: <condition type="controller" code="50400096" value="1" />, the placed furniture's item id is the code:
    // taking it into use counts, leaving it does not, another furniture item does not.
    [Test]
    public void Controller_10002669_TakingTheFurnitureIntoUse() {
        Start();
        test.EnterMap(LimitedMap(2000001));
        FieldFunctionInteract Place(int itemId, int x) {
            var position = new Vector3B(x, 0, 0);
            var cube = new PlotCube(TestGameSession.Metadata(itemId)) {
                Position = position,
                Type = PlotCube.CubeType.Construction,
                Interact = new InteractCube(position, new FunctionCubeMetadata(itemId, 0, ConfigurableCubeType.None,
                    InteractCubeState.Available, InteractCubeControlType.None, [], 0, null)),
            };
            return Field.AddFieldFunctionInteract(cube)!;
        }
        FieldFunctionInteract other = Place(50400097, 1);
        FieldFunctionInteract furniture = Place(50400096, 2);
        typeof(Maple2.Server.Game.Session.GameSession).GetField("<SeatRelease>k__BackingField", Any)!
            .SetValue(test.Session, new SeatReleaseEcho()); // the uninitialized session skips its initializers
        Quest quest = StartQuest(Condition(ConditionType.controller, 1, ["50400096"], []));
        var handler = new FunctionCubeHandler {
            FunctionCubeMetadataStorage = test.Session.FunctionCubeMetadata, ServerTableMetadataStorage = test.ServerTableMetadata,
        };
        void Use(FieldFunctionInteract interact) => handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteInt(1);
            writer.WriteUnicodeString(interact.InteractCube.Id);
            writer.WriteByte(0);
        }));

        Use(other);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Use(furniture);
        Use(furniture); // leaving it

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
    #endregion
    #region level (ExperienceManager.LevelUp)
    // 70000002: <condition type="level" value="1" target="15" />, a level-up to the target or above counts; one below does not.
    [Test]
    public void Level_70000002_ALevelUpToTheTarget() {
        Start();
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        test.SetTable(new PrestigeLevelAbilityTable(new Dictionary<int, PrestigeLevelAbilityMetadata>()));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(l => l, l => l * 10L),
        }, Enumerable.Range(1, 99).ToDictionary(l => l, _ => 100L)));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { characterMaxLevel = 99 });
        PacketHandlers.ProgressionOrderTests.LoadConfig(test, test.Session);
        test.Session.Player.Value.Character.Level = 13;
        Quest quest = StartQuest(Condition(ConditionType.level, 1, [], ["15"]));

        test.Session.Player.Value.Character.Exp = 100;
        test.Session.Exp.LevelUp();
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        test.Session.Player.Value.Character.Exp = 100;
        test.Session.Exp.LevelUp();

        Assert.That(test.Errors, Is.Empty);
        Assert.That(test.Session.Player.Value.Character.Level, Is.EqualTo(15));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
    #endregion
    #region emotion (EmoteHandler, an emote's use)
    // 10002691: <condition type="emotion" code="hello" value="10" target="02000001" />, the emote's motion in the
    // target map counts; another motion or another map does not.
    [Test]
    public void Emotion_10002691_TheMotionInTheMap() {
        Start();
        test.EnterMap(LimitedMap(2000001));
        test.Session.Player.Value.Character.MapId = 2000001; // the field entry sets it; the test field does not
        test.Session.Player.Value.Unlock.LearnEmote(90200001, 0);
        Quest quest = StartQuest(Condition(ConditionType.emotion, 10, ["hello"], ["02000001"]));
        void Use(string aniKey) => new EmoteHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(90200001);
            writer.WriteUnicodeString(aniKey);
        }));

        Use("bow");
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Use("hello");
        test.Session.Player.Value.Character.MapId = 2000062;
        Use("hello");

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
    #endregion
    #region quest completion (QuestManager.Complete)
    private void StartCompletion() {
        Start();
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
    }

    private Quest Completed(int id, QuestMetadataBasic basic) =>
        test.StartQuest(TestGameSession.QuestData(id) with { Basic = basic });

    // 10002504: <condition type="quest" code="10002540" value="1" />, completing the quest of the code counts;
    // completing another quest does not.
    [Test]
    public void Quest_10002504_TheQuestOfTheCode() {
        StartCompletion();
        Quest quest = StartQuest(Condition(ConditionType.quest, 1, ["10002540"], []));
        QuestMetadataBasic basic = TestGameSession.QuestData(0).Basic;

        Assert.That(test.Session.Quest.Complete(Completed(10002541, basic)), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Assert.That(test.Session.Quest.Complete(Completed(10002540, basic)), Is.True);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }

    // 71000132: <condition type="quest_daily" value="5" />, each completed daily quest (repeat 2) counts; a quest
    // without the daily repeat does not.
    [Test]
    public void QuestDaily_71000132_EachDailyQuest() {
        StartCompletion();
        Quest quest = StartQuest(Condition(ConditionType.quest_daily, 5, [], []));
        QuestMetadataBasic basic = TestGameSession.QuestData(0).Basic;

        Assert.That(test.Session.Quest.Complete(Completed(10002541, basic)), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Assert.That(test.Session.Quest.Complete(Completed(10002542, basic with { Repeat = 2 })), Is.True);
        Assert.That(test.Session.Quest.Complete(Completed(10002543, basic with { Repeat = 2 })), Is.True);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(2));
    }

    // 74000017: <condition type="quest_guide" value="1" />, a completed daily mission counts; an epic quest does not.
    [Test]
    public void QuestGuide_74000017_ADailyMission() {
        StartCompletion();
        Quest quest = StartQuest(Condition(ConditionType.quest_guide, 1, [], []));
        QuestMetadataBasic basic = TestGameSession.QuestData(0).Basic;

        Assert.That(test.Session.Quest.Complete(Completed(10002541, basic)), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Assert.That(test.Session.Quest.Complete(Completed(10002542, basic with { Type = QuestType.DailyMission })), Is.True);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }

    // 89010120: <condition type="quest_alliance" value="10" />, each completed alliance quest of an alliance counts;
    // one of no alliance does not.
    [Test]
    public void QuestAlliance_89010120_EachAllianceMission() {
        StartCompletion();
        Quest quest = StartQuest(Condition(ConditionType.quest_alliance, 10, [], []));
        QuestMetadataBasic alliance = TestGameSession.QuestData(0, QuestType.AllianceQuest).Basic;

        Assert.That(test.Session.Quest.Complete(Completed(10002541, alliance)), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Assert.That(test.Session.Quest.Complete(Completed(10002542, alliance with { Alliance = AllianceType.MapleAlliance })), Is.True);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }

    // 70700001: quest_clear_by_chapter codes 2010 and 3074, value 2: a completed quest of either chapter counts; one
    // of another chapter, or of none, does not.
    [Test]
    public void QuestClearByChapter_70700001_TheChaptersQuests() {
        StartCompletion();
        Quest quest = StartQuest(Condition(ConditionType.quest_clear_by_chapter, 2, ["2010", "3074"], []));
        QuestMetadataBasic basic = TestGameSession.QuestData(0).Basic;

        Assert.That(test.Session.Quest.Complete(Completed(10002541, basic)), Is.True);
        Assert.That(test.Session.Quest.Complete(Completed(10002542, basic with { ChapterId = 2011 })), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Assert.That(test.Session.Quest.Complete(Completed(10002543, basic with { ChapterId = 2010 })), Is.True);
        Assert.That(test.Session.Quest.Complete(Completed(10002544, basic with { ChapterId = 3074 })), Is.True);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(2));
    }
    #endregion
    #region buff (BuffManager.AddBuff, a buff taken on)
    // 90000501: <condition type="buff" code="90000342" value="1" />, the player taking on the buff of the code counts;
    // another buff does not.
    [TestCase(90000342, 1)]
    [TestCase(90000343, 0)]
    public void Buff_90000501_TheBuffOfTheCode(int buffId, int expected) {
        Start();
        WithEffect(buffId);
        Quest quest = StartQuest(Condition(ConditionType.buff, 1, ["90000342"], []));

        test.Session.Player.Buffs.AddBuff(test.Session.Player, test.Session.Player, buffId, 1, Field.FieldTick);

        Assert.That(test.Session.Player.Buffs.HasBuff(buffId), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(expected));
    }
    #endregion
    #region dialogue (NpcTalkHandler.BeginTalk, a talk's start)
    private FieldNpc TalkNpc(int id) {
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(id, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(npc, new Npc(metadata, null));
        typeof(FieldNpc).GetField(nameof(FieldNpc.SpawnAnimation), Any)!.SetValue(npc, "spawn");
        return npc;
    }

    // 80010042: <condition type="dialogue" code="11000005" value="1" />, a talk with the npc of the code counts; a
    // talk with another npc does not.
    [Test]
    public void Dialogue_80010042_ATalkWithTheNpc() {
        Start();
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>()));
        Quest quest = StartQuest(Condition(ConditionType.dialogue, 1, ["11000005"], []));

        NpcTalkHandler.BeginTalk(test.Session, TalkNpc(11000006));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        NpcTalkHandler.BeginTalk(test.Session, TalkNpc(11000005));

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
    #endregion
    #region riding (FieldPlayer.OnStateSync, a ride's distance)
    // 40001880: <condition type="riding" value="500" />, each meter (150 units) moved in state Ride counts; the same
    // distance on foot does not.
    [Test]
    public void Riding_40001880_EachMeterRidden() {
        Start();
        FieldPlayer player = test.Session.Player;
        player.Value.Character.MapId = Field.MapId;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, Field);
        typeof(FieldPlayer).GetField("stateSyncDistanceTracking", Any)!.SetValue(player, new Dictionary<ActorState, float>());
        // The map's bounds hold the player, so the syncs are not moved back (HoldTimeTrophyTests).
        typeof(MapEntityMetadata).GetField("<BoundingBox>k__BackingField", Any)!.SetValue(Field.Entities,
            new Maple2.Tools.Collision.Prism(new Maple2.Tools.Collision.Circle(Vector2.Zero, 100000), -100000, 200000));
        Quest quest = StartQuest(Condition(ConditionType.riding, 500, [], []));
        Vector3 start = player.Position;
        void Sync(ActorState state, float x) => player.OnStateSync(new StateSync { State = state, Position = start + new Vector3(x, 0, 0) });

        Sync(ActorState.Walk, 0);
        Sync(ActorState.Walk, 100);
        Sync(ActorState.Walk, 200);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Sync(ActorState.Ride, 200);
        Sync(ActorState.Ride, 300);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Sync(ActorState.Ride, 400);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(player.Position.X, Is.EqualTo(start.X + 400));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
    #endregion
    #region run, swim, glide, climb, fall, crawl, swimtime, laddertime, ropetime (FieldPlayer.OnStateSync)
    // The player on the given map, with the syncs kept where they are sent (as for riding).
    private FieldPlayer StateSyncField(int mapId) {
        Start();
        typeof(FieldManager).GetField("<MapId>k__BackingField", Any)!.SetValue(Field, mapId);
        FieldPlayer player = test.Session.Player;
        player.Value.Character.MapId = mapId;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, Field);
        typeof(FieldPlayer).GetField("stateSyncDistanceTracking", Any)!.SetValue(player, new Dictionary<ActorState, float>());
        typeof(MapEntityMetadata).GetField("<BoundingBox>k__BackingField", Any)!.SetValue(Field.Entities,
            new Maple2.Tools.Collision.Prism(new Maple2.Tools.Collision.Circle(Vector2.Zero, 100000), -100000, 200000));
        return player;
    }

    // 40001860 run 500, 40001730 swim 500, 40001670 glide 200, 40001650 climb 100, 40002260 fall 100, 40001740
    // crawl 50: each meter (150 units) moved in the state counts (both swim states for swim); the same distance in
    // another state does not.
    [TestCase(ConditionType.run, ActorState.Walk, 500)]
    [TestCase(ConditionType.swim, ActorState.Swim, 500)]
    [TestCase(ConditionType.swim, ActorState.SwimDash, 500)]
    [TestCase(ConditionType.glide, ActorState.Glide, 200)]
    [TestCase(ConditionType.climb, ActorState.Climb, 100)]
    [TestCase(ConditionType.fall, ActorState.Fall, 100)]
    [TestCase(ConditionType.crawl, ActorState.Crawl, 50)]
    public void Distance_EachMeterInItsState(ConditionType type, ActorState state, long value) {
        FieldPlayer player = StateSyncField(2000001);
        Quest quest = StartQuest(Condition(type, value, [], []));
        Vector3 start = player.Position;
        void Sync(ActorState syncState, float x) => player.OnStateSync(new StateSync { State = syncState, Position = start + new Vector3(x, 0, 0) });

        Sync(ActorState.Ride, 0);
        Sync(ActorState.Ride, 100);
        Sync(ActorState.Ride, 200);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Sync(state, 200);
        Sync(state, 300);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Sync(state, 400);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }

    // 60110210: <condition type="run" code="82000012,82000001,82000003" value="1000" />, a meter run on one of the
    // maps counts; on another map it does not.
    [TestCase(82000001, 1)]
    [TestCase(82000002, 0)]
    public void Run_60110210_OnTheMapsOfTheCode(int mapId, int expected) {
        FieldPlayer player = StateSyncField(mapId);
        Quest quest = StartQuest(Condition(ConditionType.run, 1000, ["82000012", "82000001", "82000003"], []));
        Vector3 start = player.Position;
        player.OnStateSync(new StateSync { State = ActorState.Walk, Position = start });
        player.OnStateSync(new StateSync { State = ActorState.Walk, Position = start + new Vector3(150, 0, 0) });

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(expected));
    }

    // One second in the state: 40001710 swimtime 120, 40001690 laddertime 300 (and ropetime, 12 rows like it).
    [TestCase(ConditionType.swimtime, ActorState.Swim)]
    [TestCase(ConditionType.swimtime, ActorState.SwimDash)]
    [TestCase(ConditionType.laddertime, ActorState.Ladder)]
    [TestCase(ConditionType.ropetime, ActorState.Rope)]
    public void Time_EachSecondInItsState(ConditionType type, ActorState state) {
        FieldPlayer player = StateSyncField(2000001);
        Quest quest = StartQuest(Condition(type, 120, [], []));
        typeof(FieldPlayer).GetProperty("StateSyncTimeTracking", Any)!.SetValue(player, 999L);
        player.OnStateSync(new StateSync { State = ActorState.Walk, Position = player.Position });
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        // Already in the state: a change of state starts the second again.
        typeof(FieldPlayer).GetField("state", Any)!.SetValue(player, state);
        typeof(FieldPlayer).GetProperty("StateSyncTimeTracking", Any)!.SetValue(player, 1000L);
        player.OnStateSync(new StateSync { State = state, Position = player.Position });

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }

    // swimtime names its map either way: 10003442 code="02000554" value="20", 80000591 code="63000088", and
    // 50001583 target="52000133". A second swum on that map counts, on another map it does not.
    [TestCase("02000554", "", 2000554, 1)]
    [TestCase("02000554", "", 2000555, 0)]
    [TestCase("63000088", "", 63000088, 1)]
    [TestCase("", "52000133", 52000133, 1)]
    [TestCase("", "52000133", 52000134, 0)]
    public void Swimtime_TheMapOfTheCodeOrTarget(string code, string target, int mapId, int expected) {
        FieldPlayer player = StateSyncField(mapId);
        Quest quest = StartQuest(Condition(ConditionType.swimtime, 20, code == "" ? [] : [code], target == "" ? [] : [target]));
        typeof(FieldPlayer).GetField("state", Any)!.SetValue(player, ActorState.Swim);
        typeof(FieldPlayer).GetProperty("StateSyncTimeTracking", Any)!.SetValue(player, 1000L);
        player.OnStateSync(new StateSync { State = ActorState.Swim, Position = player.Position });

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(expected));
    }
    #endregion
    #region breakable_object (BreakableHandler, a breakable broken)
    // 10002689: <condition type="breakable_object" value="10" />, each breakable the player's attack breaks counts;
    // a repeat of the packet for the box already broken does not.
    [Test]
    public void BreakableObject_10002689_EachBreak() {
        Start();
        foreach (string name in new[] { "fieldBreakables", "triggerBreakable" }) {
            FieldInfo info = typeof(FieldManager).GetField(name, Any)!;
            info.SetValue(Field, Activator.CreateInstance(info.FieldType));
        }
        SkillMetadata cast = CastSkill(10000001);
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack));
        SkillMetadata skill = cast with {
            Data = cast.Data with { Motions = [cast.Data.Motions[0] with { Attacks = [attack] }] },
        };
        var storage = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(storage)!;
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(skill.Id, skill.Level), skill]);
        Field.AddBreakable("bb1", new BreakableActor(true, 7001, 1000, 1000, 0, Vector3.Zero, Vector3.Zero));
        Field.AddBreakable("bb2", new BreakableActor(true, 7002, 1000, 1000, 0, Vector3.Zero, Vector3.Zero));
        Quest quest = StartQuest(Condition(ConditionType.breakable_object, 10, [], []));
        var handler = new BreakableHandler { SkillMetadata = storage };
        void Break(string entityId) => handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteString(entityId);
            writer.WriteLong(1);
            writer.WriteInt(skill.Id);
            writer.WriteShort(skill.Level);
            writer.WriteByte(0);
            writer.WriteByte(0);
        }));

        Break("bb1");
        Break("bb1");
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
        Break("bb2");

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(2));
    }
    #endregion
    #region mastery_gathering (MasteryManager.Gather, a gather's end)
    private static MasteryRecipeTable.Entry Recipe(int id, MasteryType type) =>
        new(id, type, false, 0, 0, [], 0, 0, 0, 0, 0, [], [], []);

    // 70500014: <condition type="mastery_gathering" code="30000001" value="30" />, each gather of the recipe of the
    // code counts (the gathering and mining recipes both); another recipe does not.
    [Test]
    public void MasteryGathering_70500014_TheRecipesGathers() {
        Start();
        typeof(ConfigManager).GetField(nameof(ConfigManager.GatheringCounts), Any)!.SetValue(test.Session.Config,
            new Dictionary<int, int> { [30000001] = 0, [30000002] = 0, [40000012] = 0 });
        Quest quest = StartQuest(Condition(ConditionType.mastery_gathering, 30, ["30000001"], []));
        Quest mining = test.StartQuest(TestGameSession.QuestData(QuestId + 1) with {
            Conditions = [Condition(ConditionType.mastery_gathering, 30, ["40000012"], [])],
        });

        test.Session.Mastery.Gather(Recipe(30000002, MasteryType.Gathering), Vector3.Zero, Vector3.Zero);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        test.Session.Mastery.Gather(Recipe(30000001, MasteryType.Gathering), Vector3.Zero, Vector3.Zero);
        test.Session.Mastery.Gather(Recipe(40000012, MasteryType.Mining), Vector3.Zero, Vector3.Zero);

        Assert.That(test.Errors, Is.Empty);
        Assert.Multiple(() => {
            Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
            Assert.That(mining.Conditions[0].Counter, Is.EqualTo(1));
            Assert.That(test.Session.Config.GatheringCounts[30000001], Is.EqualTo(1));
        });
    }
    #endregion
    #region emotiontime (FieldPlayer.UpdateTimeConditions, the seconds held in an emotion)
    // 10002698: <condition type="emotiontime" code="Sit_Ground_A,Sit_Chair_A" value="300" />, and the rows with
    // target="63000072": the seconds held in an emotion of the code count, on the target's map when it has one;
    // another emotion, or another map, does not.
    [TestCase("Sit_Chair_A", "", 2000001, 5)]
    [TestCase("Sit_Ground_A", "63000072", 63000072, 5)]
    [TestCase("Sit_Ground_A", "63000072", 2000001, 0)]
    [TestCase("Dance_A", "", 2000001, 0)]
    public void EmotionTime_TheEmotionsSeconds(string emotion, string target, int mapId, long expected) {
        FieldPlayer player = StateSyncField(mapId);
        SkillMetadata cast = CastSkill(90000001);
        SkillMetadata skill = cast with { Property = cast.Property with { Emotion = emotion } };
        var storage = new SkillMetadataStorage(new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(SkillMetadataStorage).BaseType!.GetField("Cache", Any)!.GetValue(storage)!;
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(skill.Id, skill.Level), skill]);
        typeof(FieldManager).GetField("<SkillMetadata>k__BackingField", Any)!.SetValue(Field, storage);
        Quest quest = StartQuest(Condition(ConditionType.emotiontime, 300, ["Sit_Ground_A", "Sit_Chair_A"], target == "" ? [] : [target]));
        // Five seconds and a half in the emotion since the last count.
        typeof(FieldPlayer).GetField("emotionSkillId", Any)!.SetValue(player, skill.Id);
        typeof(FieldPlayer).GetField("emotionTick", Any)!.SetValue(player, Environment.TickCount64 - 5500);
        typeof(FieldPlayer).GetField("emotionMs", Any)!.SetValue(player, 0L);
        typeof(FieldPlayer).GetMethod("UpdateTimeConditions", Any)!.Invoke(player, null);

        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(expected));
    }
    #endregion
    #region item_pickup (ItemPickupHandler, a drop taken into the bag)
    // 72000320: <condition type="item_pickup" code="33000131" value="20" />, the amount of each drop of the item
    // picked up counts; another item does not. 74000023: <condition type="item_pickup" value="50" />, any item.
    [Test]
    public void ItemPickup_72000320_TheItemsAmount() {
        Start();
        test.Register(TestGameSession.Metadata(33000131), TestGameSession.Metadata(33000132));
        Quest quest = StartQuest(Condition(ConditionType.item_pickup, 20, ["33000131"], []));
        Quest any = test.StartQuest(TestGameSession.QuestData(QuestId + 1) with {
            Conditions = [Condition(ConditionType.item_pickup, 50, [], [])],
        });
        void Pickup(int itemId, int amount) {
            Item item = Field.ItemDrop.CreateItem(itemId)!;
            item.Amount = amount;
            FieldItem drop = Field.SpawnItem(test.Session.Player.Position, test.Session.Player.Rotation, item, null);
            new ItemPickupHandler().Handle(test.Session, TestGameSession.Packet(writer => {
                writer.WriteInt(drop.ObjectId);
                writer.WriteByte(0);
            }));
        }

        Pickup(33000132, 2);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Pickup(33000131, 3);

        Assert.That(test.Errors, Is.Empty);
        Assert.Multiple(() => {
            Assert.That(quest.Conditions[0].Counter, Is.EqualTo(3));
            Assert.That(any.Conditions[0].Counter, Is.EqualTo(5));
        });
    }
    #endregion
    #region pet_catch_id, pet_catch_category (FieldPet.DropItem, a wild pet caught)
    // 94000006: <condition type="pet_catch_id" code="61000153" value="2">, a catch of the pet of the item counts;
    // 70600038: <condition type="pet_catch_category" code="5" value="1" />, a catch of a pet of the collection
    // group. A catch of another pet, of another group, counts for neither.
    [Test]
    public void PetCatch_94000006_70600038_TheCaughtPet() {
        StartField(2000001, Npc(21000001), Npc(21000002));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> { [ExpType.petTaming] = new(1, 1f) }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, level => level * 10L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long> { [ExpType.petTaming] = 15000 }));
        test.Register(TestGameSession.Metadata(61000153), TestGameSession.Metadata(61000154));
        Quest byId = StartQuest(Condition(ConditionType.pet_catch_id, 2, ["61000153"], []));
        Quest byGroup = test.StartQuest(TestGameSession.QuestData(QuestId + 1) with {
            Conditions = [Condition(ConditionType.pet_catch_category, 1, ["5"], [])],
        });
        void Catch(int itemId, int npcId, int group) {
            Assert.That(Field.NpcMetadata.TryGet(npcId, out NpcMetadata? npc), Is.True);
            var petMetadata = (PetMetadata) RuntimeHelpers.GetUninitializedObject(typeof(PetMetadata)) with {
                Id = npcId, NpcId = npcId, CollectGroup = group,
            };
            Item item = Field.ItemDrop.CreateItem(itemId)!;
            var pet = new FieldPet(Field, 900 + npcId % 100, Field.Navigation.AddAgent(npc!, new Vector3(100, 0, 0)), new Npc(npc!, null),
                item, petMetadata, string.Empty) { Origin = new Vector3(100, 0, 0) };
            typeof(FieldPet).GetMethod("DropItem", Any)!.Invoke(pet, [test.Session.Player]);
        }

        Catch(61000154, 21000002, 3);
        Assert.That(byId.Conditions[0].Counter + byGroup.Conditions[0].Counter, Is.EqualTo(0));
        Catch(61000153, 21000001, 5);

        Assert.That(test.Errors, Is.Empty);
        Assert.Multiple(() => {
            Assert.That(byId.Conditions[0].Counter, Is.EqualTo(1));
            Assert.That(byGroup.Conditions[0].Counter, Is.EqualTo(1));
        });
    }
    #endregion
    #region item_add (InventoryManager.Add)
    // KR 70600053: code 20301082, value 2. Each add counts the amount it adds, not the stack it lands on: one more
    // on a held one counts 1, and another item counts nothing.
    [Test]
    public void ItemAdd_70600053_TheAmountAdded() {
        Start();
        Assert.That(test.Session.Item.Inventory.Add(test.Store(TestGameSession.Metadata(20301082))), Is.True);
        Quest quest = StartQuest(Condition(ConditionType.item_add, 2, ["20301082"], []));

        Assert.That(test.Session.Item.Inventory.Add(test.Store(TestGameSession.Metadata(20301083))), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Assert.That(test.Session.Item.Inventory.Add(test.Store(TestGameSession.Metadata(20301082))), Is.True);
        Assume.That(test.Session.Item.Inventory.Filter(item => item.Id == 20301082).Single().Amount, Is.EqualTo(2), "one stack");
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
        Assert.That(test.Session.Item.Inventory.Add(test.Store(TestGameSession.Metadata(20301082))), Is.True);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(2));
    }
    #endregion

    #region item_move (FieldManager.PlaceCube, a liftable put down)
    // 10001060: <condition type="item_move" code="30000030" value="3" target="400" />, the liftable item of the code
    // put down on a target box of the target counts; one put down off the box, or another item, does not.
    [Test]
    public void ItemMove_10001060_TheLiftableOnItsTargetBox() {
        Start();
        Field.Plots[0] = new Plot(new UgcMapGroup(0, 0, 0, new UgcMapGroup.Cost(0, 0, 0), new UgcMapGroup.Cost(0, 0, 0),
            new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0)));
        Vector3B box = new(3, 0, 0), other = new(4, 0, 0);
        typeof(MapEntityMetadata).GetField("<LiftableTargetBoxes>k__BackingField", Any)!.SetValue(Field.Entities,
            new Dictionary<Vector3B, LiftableTargetBox> {
                [box] = new(Vector3.Zero, Vector3.Zero, false, 400),
                [other] = new(Vector3.Zero, Vector3.Zero, false, 400),
            });
        test.Register(TestGameSession.Metadata(30000030), TestGameSession.Metadata(30000031));
        FieldInfo liftables = typeof(FieldManager).GetField("fieldLiftables", Any)!;
        liftables.SetValue(Field, Activator.CreateInstance(liftables.FieldType));
        Quest quest = StartQuest(Condition(ConditionType.item_move, 3, ["30000030"], ["400"]));
        void Put(int itemId, Vector3B position) {
            var cube = new LiftableCube(new Liftable(itemId, 1, 0, 0, 0, "", "", "", "", false, Vector3.Zero, Vector3.Zero));
            test.Session.HeldCube = cube;
            Field.PlaceCube(test.Session, cube, position, 0);
        }

        Put(30000030, new Vector3B(1, 0, 0));
        Put(30000031, other);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Put(30000030, box);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }
    #endregion
}
