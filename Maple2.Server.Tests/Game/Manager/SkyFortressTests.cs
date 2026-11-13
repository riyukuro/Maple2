using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Database.Extensions;

namespace Maple2.Server.Tests.Game.Manager;

// CanEnterSkyFortress (questmanager_2.cpp:1723), the server's part of the sky fortress moves (Quest 0x0E and 0x0F):
// the quest of famedef.xml's sky fortress season is rewarded, and the player is not on the target field. Made-up ids.
public class SkyFortressTests {
    private const int RequireQuest = 90000013;
    private const int Target = 2000422;

    private const int AttackQuest = 90000501;
    private const int AttackLimit = 3;

    private TestGameSession test = null!;

    private void Login(FameTable fame, int mapId = 2000001) {
        test = new TestGameSession();
        test.SetTable(fame);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { FameContentsMissionAttackCount = AttackLimit });
        test.Start();
        test.EnterMap(TestGameSession.Map(mapId));
        test.TakeSent();
    }

    private static FameTable Seasons(int skyFortressQuest) =>
        new([], new Dictionary<int, AllianceType>(), SeasonRequireQuests: new Dictionary<int, int> { [1] = skyFortressQuest, [2] = 50101050 });

    private void Hold(int questId, QuestState state) {
        test.StartQuest(TestGameSession.QuestData(questId)).State = state;
    }

    [TestCase(QuestState.Completed, true)]
    [TestCase(QuestState.Started, false)]
    public void RequireQuestRewarded(QuestState state, bool enter) {
        Login(Seasons(RequireQuest));
        Hold(RequireQuest, state);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.EqualTo(enter));
    }

    [Test]
    public void RequireQuestNotHeld() {
        Login(Seasons(RequireQuest));
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.False);
    }

    // The season's quest decides, not the constant (91000013, the same quest in KR).
    [Test]
    public void SeasonQuestNotConstant() {
        Login(Seasons(RequireQuest));
        Hold(91000013, QuestState.Completed);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.False);
    }

    // A table stored before the seasons were read: the constant's quest.
    [Test]
    public void OldTableUsesConstant() {
        Login(new FameTable([], new Dictionary<int, AllianceType>()));
        Hold(test.ServerTableMetadata.ConstantsTable.FameContentsRequireQuestID, QuestState.Completed);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.True);
    }

    // A table whose seasons lack the sky fortress: quest 0, never rewarded.
    [Test]
    public void NoSkyFortressSeason() {
        Login(new FameTable([], new Dictionary<int, AllianceType>(), SeasonRequireQuests: new Dictionary<int, int>()));
        Hold(test.ServerTableMetadata.ConstantsTable.FameContentsRequireQuestID, QuestState.Completed);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.False);
    }

    [Test]
    public void AlreadyOnTheTarget() {
        Login(Seasons(RequireQuest), Target);
        Hold(RequireQuest, QuestState.Completed);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.False);
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target + 1), Is.True);
    }

    // famedef.xml's seasons by name (any case), of the features that are on; a later season of the same name wins.
    [Test]
    public void ReadFame_Seasons() {
        var empty = new XmlDocument();
        empty.LoadXml("<ms2 />");
        var fameDef = new XmlDocument();
        fameDef.LoadXml("""
            <ms2>
              <season name="SkyFotress" requireQuest="91000013" requireLv="50" />
              <season name="KritiasExped" requireQuest="50101050" requireLv="70" feature="Off" />
              <season name="georg" requireQuest="91001010" />
              <season name="Georg" requireQuest="91001011" />
            </ms2>
            """);
        FameTable table = TableMapper.ReadFame(empty, fameDef, "KR", feature => feature != "Off");
        Assert.Multiple(() => {
            Assert.That(table.SeasonRequireQuest(1), Is.EqualTo(91000013));
            Assert.That(table.SeasonRequireQuest(2), Is.Zero);
            Assert.That(table.SeasonRequireQuest(3), Is.EqualTo(91001011));
        });
    }

    // A field quest of the target field (its first progress map) with an alliance, in this state.
    private void HoldFieldQuest(QuestState state, AllianceType alliance = AllianceType.DarkWind, int questId = AttackQuest, int mapId = Target) {
        QuestMetadata data = TestGameSession.QuestData(questId, QuestType.FieldQuest);
        data = data with { Basic = data.Basic with { ProgressMaps = [mapId], Alliance = alliance } };
        test.StartQuest(data).State = state;
    }

    private void Attack(int mapId = Target) {
        new QuestHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata }
            .Handle(test.Session, TestGameSession.Packet(writer => {
                writer.WriteByte(15);
                writer.WriteInt(mapId);
            }));
    }

    private void AttacksToday(int count) {
        test.Session.Player.Value.Character.MissionAttackCount = count;
        test.Session.Player.Value.Character.MissionAttackTime = DateTime.Now.ToEpochSeconds();
    }

    private void ReadyToAttack() {
        Login(Seasons(RequireQuest));
        Hold(RequireQuest, QuestState.Completed);
        HoldFieldQuest(QuestState.Started);
    }

    [Test]
    public void MissionAttack_Allowed() {
        ReadyToAttack();
        Assert.That(test.Session.Quest.CanMissionAttack(Target), Is.True);
    }

    // FUN_1416d0800: the alliance of the field's field quest in progress; none, another field's, an ended one or one
    // without an alliance gives 0.
    [Test]
    public void MissionAttack_NeedsAllianceFieldQuest() {
        Login(Seasons(RequireQuest));
        Hold(RequireQuest, QuestState.Completed);
        Assert.That(test.Session.Quest.CanMissionAttack(Target), Is.False);
        HoldFieldQuest(QuestState.Completed, questId: AttackQuest);
        HoldFieldQuest(QuestState.Started, AllianceType.AnyFaction, AttackQuest + 1);
        HoldFieldQuest(QuestState.Started, questId: AttackQuest + 2, mapId: Target + 1);
        Assert.Multiple(() => {
            Assert.That(test.Session.Quest.FieldQuestAlliance(Target), Is.EqualTo(AllianceType.AnyFaction));
            Assert.That(test.Session.Quest.CanMissionAttack(Target), Is.False);
            Assert.That(test.Session.Quest.FieldQuestAlliance(Target + 1), Is.EqualTo(AllianceType.DarkWind));
        });
    }

    // The first quest in progress decides, even when a later one has an alliance.
    [Test]
    public void MissionAttack_FirstQuestDecides() {
        Login(Seasons(RequireQuest));
        HoldFieldQuest(QuestState.Started, AllianceType.AnyFaction, AttackQuest);
        HoldFieldQuest(QuestState.Started, AllianceType.DarkWind, AttackQuest + 1);
        Assert.That(test.Session.Quest.FieldQuestAlliance(Target), Is.EqualTo(AllianceType.AnyFaction));
    }

    [Test]
    public void MissionAttack_NeedsSkyFortressMove() {
        Login(Seasons(RequireQuest));
        HoldFieldQuest(QuestState.Started);
        Assert.That(test.Session.Quest.CanMissionAttack(Target), Is.False);
    }

    // FameContentsMissionAttackCount a day; a count from before the last reset is 0.
    [Test]
    public void MissionAttack_DailyLimit() {
        ReadyToAttack();
        AttacksToday(AttackLimit - 1);
        Assert.That(test.Session.Quest.CanMissionAttack(Target), Is.True);
        AttacksToday(AttackLimit);
        Assert.That(test.Session.Quest.CanMissionAttack(Target), Is.False);
        test.Session.Player.Value.Character.MissionAttackTime = PetFusionRules.LastResetTime() - 1;
        Assert.Multiple(() => {
            Assert.That(test.Session.Quest.MissionAttackCount(), Is.Zero);
            Assert.That(test.Session.Quest.CanMissionAttack(Target), Is.True);
        });
    }

    [Test]
    public void MissionAttack_CountAddsOne() {
        ReadyToAttack();
        test.AddAchievement(TestGameSession.Trophy(22500041, ConditionType.mission_attack_system, [50]));
        AttacksToday(1);
        test.Session.Quest.CountMissionAttack();
        Assert.That(test.Session.Quest.MissionAttackCount(), Is.EqualTo(2));
        test.Session.Player.Value.Character.MissionAttackTime = PetFusionRules.LastResetTime() - 1;
        test.Session.Quest.CountMissionAttack();
        Assert.Multiple(() => {
            Assert.That(test.Session.Player.Value.Character.MissionAttackCount, Is.EqualTo(1));
            Assert.That(test.TrophyCounter(22500041), Is.EqualTo(2));
        });
    }

    // A refused attack: no answer, nothing counted.
    [Test]
    public void MissionAttack_RefusedNoAnswer() {
        ReadyToAttack();
        test.AddAchievement(TestGameSession.Trophy(22500041, ConditionType.mission_attack_system, [50]));
        AttacksToday(AttackLimit);
        test.TakeSent();
        Attack();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(test.Session.Player.Value.Character.MissionAttackCount, Is.EqualTo(AttackLimit));
            Assert.That(test.TrophyCounter(22500041), Is.Null);
        });
    }

    // A move that cannot be prepared (a field the played client lacks): the move error, nothing counted.
    [Test]
    public void MissionAttack_FailedMoveNotCounted() {
        ReadyToAttack();
        MapMetadata map = TestGameSession.Map(Target, notInPlayedClient: true);
        test.AddMap(map with { Limit = (MapMetadataLimit) RuntimeHelpers.GetUninitializedObject(typeof(MapMetadataLimit)) });
        test.AddAchievement(TestGameSession.Trophy(22500041, ConditionType.mission_attack_system, [50]));
        test.TakeSent();
        Attack();
        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Notice, SendOp.RequestFieldEnter }));
            Assert.That(test.Session.Quest.MissionAttackCount(), Is.Zero);
            Assert.That(test.TrophyCounter(22500041), Is.Null);
        });
    }

    // Mode 0x15: the stars, then today's attacks.
    [Test]
    public void LoadExploration_SendsAttackCount() {
        var writer = QuestPacket.LoadExploration(7, 2);
        byte[] packet = writer.Buffer.AsSpan(0, writer.Length).ToArray();
        Assert.That(packet, Is.EqualTo(new byte[] { 0x52, 0x00, 0x15, 7, 0, 0, 0, 2, 0, 0, 0 }));
    }

    // The reset at its midnight: the old day's attacks go (the time is the midnight), 0x10B tells the client.
    [Test]
    public void DailyReset_ClearsAttackCount() {
        ReadyToAttack();
        test.Session.Player.Value.Character.MissionAttackCount = AttackLimit;
        test.Session.Player.Value.Character.MissionAttackTime = PetFusionRules.LastResetTime() - 60;
        test.TakeSent();
        test.Session.Quest.ResetMissionAttacks(DateTime.Today);
        Assert.Multiple(() => {
            Assert.That(test.Session.Player.Value.Character.MissionAttackCount, Is.Zero);
            Assert.That(test.Session.Player.Value.Character.MissionAttackTime, Is.EqualTo(PetFusionRules.LastResetTime()));
            Assert.That(test.TakeSent(), Is.EqualTo(new[] { new byte[] { 0x0B, 0x01 } }));
        });
    }

    // Attacks counted after the midnight (a reset that runs late) belong to the new day and stay.
    [Test]
    public void DailyReset_KeepsNewDayAttacks() {
        ReadyToAttack();
        AttacksToday(2);
        test.TakeSent();
        test.Session.Quest.ResetMissionAttacks(DateTime.Today);
        Assert.Multiple(() => {
            Assert.That(test.Session.Quest.MissionAttackCount(), Is.EqualTo(2));
            Assert.That(test.TakeSent(), Is.EqualTo(new[] { new byte[] { 0x0B, 0x01 } }));
        });
    }

    // A reset that fires before its midnight waits for it on the session's queue: nothing cleared, nothing sent.
    [Test]
    public void DailyReset_EarlyFireWaitsForMidnight() {
        ReadyToAttack();
        var scheduler = new Maple2.Tools.Scheduler.EventQueue(Serilog.Log.Logger);
        scheduler.Start();
        typeof(Maple2.Server.Game.Session.GameSession).GetField(nameof(Maple2.Server.Game.Session.GameSession.Scheduler))!.SetValue(test.Session, scheduler);
        AttacksToday(AttackLimit);
        test.TakeSent();
        test.Session.Quest.ResetMissionAttacks(DateTime.Today.AddHours(-1));
        Assert.Multiple(() => {
            Assert.That(test.Session.Quest.MissionAttackCount(), Is.EqualTo(AttackLimit));
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(scheduler.Count, Is.EqualTo(1));
            Assert.That(scheduler.Queued, Is.Zero);
        });
    }

    private void Die() {
        typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("isDead", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(test.Session.Player, true);
        Assert.That(test.Session.Player.IsDead, Is.True);
    }

    // FUN_1409312f0: off the home field a dead player cannot move (0x0E or 0x0F); the attack is not answered or
    // counted.
    [Test]
    public void Dead_RefusedOffHomeField() {
        ReadyToAttack();
        test.AddAchievement(TestGameSession.Trophy(22500041, ConditionType.mission_attack_system, [50]));
        Die();
        test.TakeSent();
        Attack();
        Assert.Multiple(() => {
            Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.False);
            Assert.That(test.Session.Quest.CanMissionAttack(Target), Is.False);
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(test.Session.Quest.MissionAttackCount(), Is.Zero);
            Assert.That(test.TrophyCounter(22500041), Is.Null);
        });
    }

    // On the home field (62000000) the client skips the check.
    [Test]
    public void Dead_AllowedOnHomeField() {
        Login(Seasons(RequireQuest), Constant.DefaultHomeMapId);
        Hold(RequireQuest, QuestState.Completed);
        Die();
        Assert.That(test.Session.Quest.CanEnterSkyFortress(Target), Is.True);
    }

    // A field quest the played client has no row of decides nothing.
    [Test]
    public void MissionAttack_NotInPlayedClientQuestSkipped() {
        Login(Seasons(RequireQuest));
        Hold(RequireQuest, QuestState.Completed);
        QuestMetadata data = TestGameSession.QuestData(AttackQuest, QuestType.FieldQuest);
        data = data with { Basic = data.Basic with { ProgressMaps = [Target], Alliance = AllianceType.DarkWind, NotInPlayedClient = true } };
        test.StartQuest(data).State = QuestState.Started;
        Assert.Multiple(() => {
            Assert.That(test.Session.Quest.FieldQuestAlliance(Target), Is.EqualTo(AllianceType.AnyFaction));
            Assert.That(test.Session.Quest.CanMissionAttack(Target), Is.False);
        });
        HoldFieldQuest(QuestState.Started, AllianceType.DarkWind, AttackQuest + 1);
        Assert.That(test.Session.Quest.FieldQuestAlliance(Target), Is.EqualTo(AllianceType.DarkWind));
    }

    // The move to the sky fortress (Quest 0x0E) raises skyfortress_system only when it is prepared: a refused or failed
    // move raises nothing.
    [Test]
    public void SkyFortressMove_FailedMoveNoTrophy() {
        Login(Seasons(RequireQuest));
        test.AddAchievement(TestGameSession.Trophy(22500040, ConditionType.skyfortress_system, [100]));
        var handler = new QuestHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata };
        handler.Handle(test.Session, TestGameSession.Packet(writer => writer.WriteByte(14)));
        Assert.That(test.TakeSent(), Is.Empty);
        Hold(RequireQuest, QuestState.Completed);
        MapMetadata map = TestGameSession.Map(Target, notInPlayedClient: true);
        test.AddMap(map with { Limit = (MapMetadataLimit) RuntimeHelpers.GetUninitializedObject(typeof(MapMetadataLimit)) });
        handler.Handle(test.Session, TestGameSession.Packet(writer => writer.WriteByte(14)));
        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Notice, SendOp.RequestFieldEnter }));
            Assert.That(test.TrophyCounter(22500040), Is.Null);
        });
    }
}
