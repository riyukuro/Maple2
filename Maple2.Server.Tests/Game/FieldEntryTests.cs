using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Util;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A player entering a field (GameSession's LOAD step calls FieldManager.OnAddPlayer): the newcomer gets the field's
// objects, the players and the field's state, and the players already there get the newcomer.
public class FieldEntryTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private FieldManager Field => test.Session.Field!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        Complete(Field);
        Prepare(test.Session);
        test.SetTable(new InsigniaTable(new Dictionary<int, InsigniaTable.Entry>()));
        test.TakeSent();
    }

    // The test session has no skill manager or ride: an empty skill info and no mount, as FieldAddUser writes them.
    public static void Prepare(GameSession session) {
        var skillInfo = (SkillInfo) RuntimeHelpers.GetUninitializedObject(typeof(SkillInfo));
        var skills = new IDictionary<int, SkillInfo.Skill>[SkillInfo.SKILL_TYPES, SkillInfo.SKILL_RANKS];
        var subSkills = new IDictionary<int, SkillInfo.Skill>[SkillInfo.SKILL_TYPES, SkillInfo.SKILL_RANKS];
        for (int i = 0; i < SkillInfo.SKILL_TYPES; i++) {
            for (int j = 0; j < SkillInfo.SKILL_RANKS; j++) {
                skills[i, j] = new Dictionary<int, SkillInfo.Skill>();
                subSkills[i, j] = new Dictionary<int, SkillInfo.Skill>();
            }
        }
        typeof(SkillInfo).GetField(nameof(SkillInfo.Skills))!.SetValue(skillInfo, skills);
        typeof(SkillInfo).GetField(nameof(SkillInfo.SubSkills))!.SetValue(skillInfo, subSkills);
        var skill = (SkillManager) RuntimeHelpers.GetUninitializedObject(typeof(SkillManager));
        typeof(SkillManager).GetField(nameof(SkillManager.SkillInfo))!.SetValue(skill, skillInfo);
        typeof(ConfigManager).GetField(nameof(ConfigManager.Skill))!.SetValue(session.Config, skill);
        session.Ride = new RideManager(session);
    }

    // The test field skips the FieldManager ctor and its initializers: the collections the entry reads are made here,
    // with no vibrate objects, the default instance and an empty trigger set.
    public static void Complete(FieldManager field) {
        for (Type? type = field.GetType(); type != null && type != typeof(object); type = type.BaseType) {
            foreach (FieldInfo info in type.GetFields(Any | BindingFlags.DeclaredOnly)) {
                if (info.GetValue(field) != null || !info.FieldType.IsGenericType) {
                    continue;
                }
                string? space = info.FieldType.Namespace;
                if (space is not ("System.Collections.Concurrent" or "System.Collections.Generic")
                    || info.FieldType.IsInterface || info.FieldType.GetConstructor(Type.EmptyTypes) == null) {
                    continue;
                }
                info.SetValue(field, Activator.CreateInstance(info.FieldType));
            }
        }
        typeof(FieldManager).GetField("<Vibrate>k__BackingField", Any)!.SetValue(field, new AiVibrate([]));
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        var entities = (MapEntityMetadata) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        typeof(MapEntityMetadata).GetField("<Trigger>k__BackingField", Any)!.SetValue(entities, new TriggerStorage([]));
        typeof(FieldManager).GetField("<Entities>k__BackingField", Any)!.SetValue(field, entities);
        typeof(FieldManager).GetField(nameof(FieldManager.TriggerObjects), Any)!.SetValue(field, new TriggerCollection(entities));
    }

    // An empty field: the lists of its objects, the player itself, then its triggers and properties.
    [Test]
    public void Enter_EmptyField_SendsTheLoadInClientOrder() {
        Field.OnAddPlayer(test.Session.Player);

        List<SendOp> ops = test.TakeSentOps();
        Assert.That(ops, Is.EqualTo(new[] {
            SendOp.Liftable, SendOp.Breakable, SendOp.InteractObject, SendOp.FunctionCube, SendOp.FieldAddUser,
            SendOp.ProxyGameObj, SendOp.Trigger, SendOp.FieldProperty,
        }));
    }
    // A player already in the field: the newcomer gets both players and both proxies, the other player gets the
    // newcomer's FieldAddUser and proxy.
    [Test]
    public void Enter_WithAnotherPlayer_EachSeesTheOther() {
        TestGameSession other = SecondPlayer();

        Field.OnAddPlayer(test.Session.Player);

        List<SendOp> ops = test.TakeSentOps();
        Assert.That(ops.Count(op => op == SendOp.FieldAddUser), Is.EqualTo(2));
        Assert.That(ops.Count(op => op == SendOp.ProxyGameObj), Is.EqualTo(2));
        Assert.That(other.TakeSentOps(), Is.EqualTo(new[] { SendOp.FieldAddUser, SendOp.ProxyGameObj }));
        Assert.That(test.Session.Player.Flag, Is.EqualTo(PlayerObjectFlag.All));
    }

    // The items on the ground and the field's portals; a quest portal goes only to its owner.
    [Test]
    public void Enter_SendsTheGroundItemsAndPortals_AQuestPortalOnlyToItsOwner() {
        TestGameSession other = SecondPlayer();
        Field.SpawnItem(new Vector3(10, 20, 0), Vector3.Zero, new Item(TestGameSession.Metadata(20000001), 1, 1), null);
        Field.SpawnPortal(new Portal(5, 0, 0, PortalType.Field, PortalActionType.Touch, new Vector3(50, 60, 0), Vector3.Zero,
            new Vector3(100, 100, 100), 0, 0, false, false, false));
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", Any)!;
        var questPortal = new FieldQuestPortal(other.Session.Player, Field, 900, new Portal(6, 0, 0, PortalType.Quest,
            PortalActionType.Interact, Vector3.Zero, Vector3.Zero, new Vector3(100, 100, 100), 0, 0, true, false, true));
        ((System.Collections.IDictionary) portals.GetValue(Field)!)[questPortal.ObjectId] = questPortal;
        test.TakeSent();
        other.TakeSent();

        Field.OnAddPlayer(test.Session.Player);
        Field.OnAddPlayer(other.Session.Player);

        List<SendOp> mine = test.TakeSentOps();
        Assert.That(mine.Count(op => op == SendOp.FieldAddItem), Is.EqualTo(1));
        Assert.That(mine.Count(op => op == SendOp.FieldPortal), Is.EqualTo(1));
        List<SendOp> theirs = other.TakeSentOps();
        Assert.That(theirs.Count(op => op == SendOp.FieldPortal), Is.EqualTo(2));
    }

    // A vibrate object of a hide group not shown yet is hidden for the newcomer; a shown one is not.
    [Test]
    public void Enter_HidesTheHiddenVibrateObjects() {
        typeof(FieldManager).GetField("<Vibrate>k__BackingField", Any)!.SetValue(Field, new AiVibrate([Vibrate("a", 1), Vibrate("b", 2)]));
        Field.Vibrate.Show(2);

        Field.OnAddPlayer(test.Session.Player);

        Assert.That(test.TakeSentOps().Count(op => op == SendOp.HideVibrate), Is.EqualTo(1));
    }

    // A displayed trigger timer is shown to the field again; a hidden one is not.
    [Test]
    public void Enter_ShowsTheDisplayedTimers() {
        Field.Timers["shown"] = new TickTimer(60000, display: true);
        Field.Timers["hidden"] = new TickTimer(60000);

        Field.OnAddPlayer(test.Session.Player);

        List<SendOp> ops = test.TakeSentOps();
        Assert.That(ops.Count(op => op == SendOp.Trigger), Is.EqualTo(2)); // Load, then the timer dialog
        Assert.That(ops[^1], Is.EqualTo(SendOp.Trigger));
    }

    private static FieldVibrateEntity Vibrate(string id, int hideGroup) {
        return new FieldVibrateEntity(new FieldEntityId(1, 2, id), Vector3.Zero, Vector3.Zero, 1, new BoundingBox3(),
            BreakDefense: 99, BreakTick: 0, VibrateIndex: 0, HideGroupId: hideGroup, MaterialTag: "wood", ShapeDimensions: default);
    }

    // A second player in the same field, ready for FieldAddUser.
    private TestGameSession SecondPlayer() {
        var other = new TestGameSession(test.Options);
        other.Start();
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(other.Session.Player, 2);
        Field.Players[2] = other.Session.Player;
        other.Session.Field = Field;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(other.Session.Player, Field);
        Prepare(other.Session);
        other.SetTable(new InsigniaTable(new Dictionary<int, InsigniaTable.Entry>()));
        return other;
    }
}
