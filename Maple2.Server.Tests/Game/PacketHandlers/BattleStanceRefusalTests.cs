using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Sends the client makes only out of its battle stance (the closet apply, Wardrobe 1; the skill tree apply, Job 9
// and 11) are dropped in a stance the client must show: one held past the race grace, with no instrument play
// since the last battle action. Job 9 and 11 also stay off a map with limit content 0x10. Before, all were served.
public class BattleStanceRefusalTests {
    private const int Town = 90000100;
    private const int SkillId = 10500001;

    private static void HeldStance(GameSession session) {
        session.Player.InBattle = true;
        session.Player.BackdateBattleStart(BattleStanceRules.RaceGraceMs + 1000);
    }

    [TestCase(true, false, 5000, ExpectedResult = true)]
    [TestCase(true, false, BattleStanceRules.RaceGraceMs - 1, ExpectedResult = false)]
    [TestCase(true, true, 5000, ExpectedResult = false)]
    [TestCase(false, false, 5000, ExpectedResult = false)]
    public bool Refuses(bool inBattle, bool dropped, long heldMs) => BattleStanceRules.Refuses(inBattle, dropped, heldMs);

    private static (TestGameSession, GameSession, Item) Closet() {
        var test = new TestGameSession();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1));
        GameSession session = test.Start();
        PetLapenshardWardrobeHandlerTests.Closet(session, 0, (cap, EquipSlot.CP));
        test.TakeSent();
        return (test, session, cap);
    }

    [Test]
    public void ClosetApply_InAHeldStance_IsDropped() {
        (TestGameSession test, GameSession session, Item cap) = Closet();
        HeldStance(session);
        test.TakeSent();

        PetLapenshardWardrobeHandlerTests.ClosetRequest(session, 1, layer: true);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(session.Item.Equips.Get(cap.Uid), Is.Null);
            Assert.That(session.Item.Inventory.Get(cap.Uid), Is.Not.Null);
        });
    }

    // The stance turned on just now may not have reached the client, which sent the apply out of it.
    [Test]
    public void ClosetApply_StanceJustOn_IsServed() {
        (_, GameSession session, Item cap) = Closet();
        session.Player.InBattle = true;

        PetLapenshardWardrobeHandlerTests.ClosetRequest(session, 1, layer: true);

        Assert.That(session.Item.Equips.Get(cap.Uid), Is.Not.Null);
    }

    // An instrument play drops the client's stance; the next battle action brings it back.
    [Test]
    public void ClosetApply_AfterAnInstrumentPlay_IsServedUntilTheNextBattleAction() {
        (_, GameSession session, Item cap) = Closet();
        HeldStance(session);
        session.Player.DropBattleStance();

        PetLapenshardWardrobeHandlerTests.ClosetRequest(session, 1, layer: true);
        Assert.That(session.Item.Equips.Get(cap.Uid), Is.Not.Null);

        (_, GameSession again, Item cap2) = Closet();
        HeldStance(again);
        again.Player.DropBattleStance();
        again.Player.InBattle = true;
        PetLapenshardWardrobeHandlerTests.ClosetRequest(again, 1, layer: true);
        Assert.That(again.Item.Equips.Get(cap2.Uid), Is.Null);
    }

    // The player's own idle sync with a sub-state, through FieldPlayer.OnStateSync on the session's field (the setup of
    // QuestConditionWiringTests' riding test).
    private static void IdleSync(GameSession session, ActorSubState subState) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldPlayer player = session.Player;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", any)!.SetValue(player, session.Field);
        typeof(FieldPlayer).GetField("stateSyncDistanceTracking", any)!.SetValue(player, new Dictionary<ActorState, float>());
        // The map's bounds hold the player, so the sync is not moved back.
        var entities = (MapEntityMetadata) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        typeof(MapEntityMetadata).GetField("<BoundingBox>k__BackingField", any)!.SetValue(entities,
            new Maple2.Tools.Collision.Prism(new Maple2.Tools.Collision.Circle(Vector2.Zero, 100000), -100000, 200000));
        typeof(FieldManager).GetField("<Entities>k__BackingField", any)!.SetValue(session.Field, entities);
        player.OnStateSync(new StateSync { State = ActorState.Idle, SubState = subState, Position = player.Position });
    }

    // The idle sub-states whose animation drops the client's stance (stateidle.cpp:386-408): the player's own sync
    // entering one drops it on the server too, until the next battle action. A plain idle sync does not.
    [TestCase(ActorSubState.Idle_Fitting, true)]
    [TestCase(ActorSubState.Idle_Fitting_Basic, true)]
    [TestCase(ActorSubState.Idle_ListenMusic, true)]
    [TestCase(ActorSubState.Idle_Idle, false)]
    public void ClosetApply_AfterAnIdleSyncThatDropsTheStance(ActorSubState subState, bool served) {
        (_, GameSession session, Item cap) = Closet();
        HeldStance(session);
        IdleSync(session, subState);

        PetLapenshardWardrobeHandlerTests.ClosetRequest(session, 1, layer: true);

        Assert.That(session.Item.Equips.Get(cap.Uid) != null, Is.EqualTo(served));
    }

    [Test]
    public void SkillTreeApply_AfterAListenMusicSync_IsStored() {
        var test = new TestGameSession();
        GameSession session = SkillBookSaveTests.Setup(test);
        test.EnterMap(TestGameSession.Map(Town));
        HeldStance(session);
        IdleSync(session, ActorSubState.Idle_ListenMusic);
        test.TakeSent();

        new JobHandler().Handle(session, Job9());

        Assert.That(session.Config.Skill.SkillInfo.GetMainSkill(SkillId)!.Level, Is.EqualTo(1));
    }

    private static IByteReader Job9() => TestGameSession.Packet(writer => {
        writer.WriteByte(9);
        writer.WriteInt(1);
        writer.WriteInt(SkillId);
        writer.WriteShort(1);
        writer.WriteBool(true);
    });

    private static int JobReplies(TestGameSession test) => test.TakeSent().Count(packet => TestGameSession.Op(packet) == SendOp.Job);

    // Refused or not, the reply carries the stored tree, so the skill book re-reads it and does not wait.
    [TestCase(true, false, 0)]
    [TestCase(false, true, 0)]
    [TestCase(false, false, 1)]
    public void SkillTreeApply(bool heldStance, bool limitContentMap, int storedLevel) {
        var test = new TestGameSession();
        GameSession session = SkillBookSaveTests.Setup(test);
        test.EnterMap(TestGameSession.Map(Town, limitContent: limitContentMap ? JobHandler.SkillTreeLimitContent : 0));
        if (heldStance) {
            HeldStance(session);
        }
        test.TakeSent();

        new JobHandler().Handle(session, Job9());

        Assert.Multiple(() => {
            Assert.That(JobReplies(test), Is.EqualTo(1));
            Assert.That(session.Config.Skill.SkillInfo.GetMainSkill(SkillId)!.Level, Is.EqualTo(storedLevel));
        });
    }
}
