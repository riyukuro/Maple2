using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Quest 7 (expired), 9 (tracking), 12 (go to npc), 19 (re-enter the hidden map), 34 and 35 (alliance all): the
// answers and refusals of the quest window's smaller buttons. Made-up quest ids.
public class QuestHandlerListTests {
    private const int Timed = 90000011, Plain = 90000012, Hidden = 90000013;

    private TestGameSession test = null!;
    private QuestHandler handler = null!;

    private GameSession Login() {
        test = new TestGameSession();
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        GameSession session = test.Start();
        handler = new QuestHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata };
        test.TakeSent();
        return session;
    }

    private void Send(GameSession session, byte mode, params int[] ints) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            foreach (int value in ints) {
                writer.WriteInt(value);
            }
        }));
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter writer) => Convert.ToHexString(writer.ToArray());

    private Quest Hold(int id, Func<QuestMetadataBasic, QuestMetadataBasic> basic, QuestState state = QuestState.Started) {
        QuestMetadata data = TestGameSession.QuestData(id);
        data = data with { Basic = basic(data.Basic) };
        test.Register(data);
        Quest quest = test.StartQuest(data);
        quest.State = state;
        return quest;
    }

    // Only a quest in progress that has an end time can expire, and only once the end has come; the answer names
    // those that were dropped.
    [Test]
    public void Expired_DropsOnlyTheTimedQuestsPastTheirEnd() {
        GameSession session = Login();
        Quest timed = Hold(Timed, basic => basic with { DurationMinutes = 30 });
        timed.StartTime = DateTimeOffset.UtcNow.AddMinutes(-40).ToUnixTimeSeconds();
        Quest plain = Hold(Plain, basic => basic);
        plain.StartTime = timed.StartTime;

        Send(session, 7, 3, Timed, Plain, 12345);

        Assert.Multiple(() => {
            Assert.That(session.Quest.TryGetQuest(Timed, out _), Is.False);
            Assert.That(session.Quest.TryGetQuest(Plain, out _), Is.True);
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(QuestPacket.Expired([Timed])) }));
        });
    }

    // A timed quest whose end is still ahead stays and nothing is sent.
    [Test]
    public void Expired_OfAQuestInsideItsTimeSendsNothing() {
        GameSession session = Login();
        Quest timed = Hold(Timed, basic => basic with { DurationMinutes = 30 });
        timed.StartTime = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();

        Send(session, 7, 1, Timed);

        Assert.Multiple(() => {
            Assert.That(session.Quest.TryGetQuest(Timed, out _), Is.True);
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    // The tracking toggle is echoed for a quest the player holds that is not completed.
    [TestCase(true)]
    [TestCase(false)]
    public void Tracking_IsEchoedForAQuestInProgress(bool tracked) {
        GameSession session = Login();
        Hold(Plain, basic => basic);

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(9);
            writer.WriteInt(Plain);
            writer.WriteBool(tracked);
        }));

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(QuestPacket.SetTracking(Plain, tracked)) }));
    }

    // A completed quest, a quest not held and a quest the played client lacks are not tracked.
    [TestCase(false, QuestState.Completed)]
    [TestCase(true, QuestState.Started)]
    public void Tracking_OfACompletedOrUnknownQuestIsIgnored(bool notInClient, QuestState state) {
        GameSession session = Login();
        Hold(Plain, basic => basic with { NotInPlayedClient = notInClient }, state);

        foreach (int id in new[] { Plain, 12345 }) {
            handler.Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(9);
                writer.WriteInt(id);
                writer.WriteBool(true);
            }));
        }

        Assert.That(test.TakeSent(), Is.Empty);
    }

    // A quest not held, or one without the go-to-npc button, moves nobody.
    [Test]
    public void GoToNpc_WithoutTheButtonMovesNobody() {
        GameSession session = Login();
        Hold(Plain, basic => basic);

        Send(session, 12, 12345);
        Send(session, 12, Plain);

        Assert.That(test.World.Moves, Is.Empty);
    }

    // The re-entry button of a quest whose hidden field cannot be entered again, or that is not held, does nothing.
    [Test]
    public void ReEnterHiddenMap_RefusesAQuestWithoutTheFlagOrHeld() {
        GameSession session = Login();
        test.EnterMap(TestGameSession.Map(2000001));
        Hold(Hidden, basic => basic with { ProgressMaps = [2000009] });

        Send(session, 19, Hidden);
        Send(session, 19, 12345);

        Assert.That(test.World.Moves, Is.Empty);
    }

    // The window's accept all and complete all outside an alliance npc's talk are answered with the client's refusal.
    [TestCase((byte) 34, QuestError.s_quest_error_accept_fail)]
    [TestCase((byte) 35, QuestError.s_quest_error_consume_fail)]
    public void AllianceAll_WithoutAnAllianceNpcIsRefused(byte mode, QuestError error) {
        GameSession session = Login();

        Send(session, mode);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(QuestPacket.Error(error)) }));
    }

    // Quest 33 with a quest that is not held is refused with the completion ticket error.
    [Test]
    public void CompleteWithTickets_OfAnUnheldQuestIsRefused() {
        GameSession session = Login();

        Send(session, 33, 12345);

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Hex(QuestPacket.Error(QuestError.s_alliance_quest_completion_ticket_error)) }));
    }
}
