using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Quest 2 (accept) and Quest 4 (complete): each refusal is answered with the client's own text (Quest 0) and gives
// nothing, a repeated request gives its reward once, and a completion is sent in the order KMS2 sends it. Made-up
// quest and item ids.
public class QuestHandlerTests {
    private const int QuestId = 90000001;
    private const int RewardId = 20000001;
    private const int Npc = 11000001;

    private TestGameSession test = null!;
    private QuestHandler handler = null!;

    private static QuestMetadata Data(bool acceptItem = false) {
        QuestMetadata data = TestGameSession.QuestData(QuestId);
        return data with {
            Basic = data.Basic with { CompleteNpc = Npc },
            RemoteAccept = new QuestRemoteAccept(QuestRemoteType.Popup, 0),
            AcceptReward = data.AcceptReward with { EssentialItem = acceptItem ? [new QuestMetadataReward.Item(RewardId, 1, 1)] : [] },
            CompleteReward = data.CompleteReward with { Meso = 100, Exp = 10, EssentialItem = [new QuestMetadataReward.Item(RewardId, 1, 2)] },
            Conditions = [new ConditionMetadata(ConditionType.jump, 1, null, null)],
        };
    }

    private GameSession Login(bool fullBag = false) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(RewardId));
        if (fullBag) {
            test.Store(TestGameSession.Metadata(20000009));
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = [1, 0] });
        }
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        GameSession session = test.Start();
        handler = new QuestHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata };
        test.TakeSent();
        return session;
    }

    // A started quest with its one condition met or open.
    private Quest Started(bool met = true) {
        Quest quest = test.StartQuest(Data());
        quest.Conditions[0].Counter = met ? 1 : 0;
        return quest;
    }

    private void Accept(GameSession session, int questId = QuestId) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(questId);
            writer.WriteInt(0);
        }));
    }

    private void Complete(GameSession session, int questId = QuestId) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(questId);
            writer.WriteInt(0);
            writer.WriteInt(0);
        }));
    }

    private static string Error(QuestError error) => Convert.ToHexString(QuestPacket.Error(error).ToArray());

    private static int Held(GameSession session) => session.Item.Inventory.Find(RewardId).Sum(item => item.Amount);

    // KMS2 order: the quest's completion, the meso, the exp, then the items.
    [Test]
    public void Complete_SendsTheQuestThenMesoExpAndItems() {
        GameSession session = Login();
        Quest quest = Started();
        long meso = session.Currency.Meso;

        Complete(session);

        List<byte[]> sent = test.TakeSent();
        List<SendOp> ops = sent.Select(TestGameSession.Op).ToList();
        Assert.Multiple(() => {
            Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Convert.ToHexString(QuestPacket.Complete(quest).ToArray())));
            Assert.That(ops.IndexOf(SendOp.Meso), Is.GreaterThan(0));
            Assert.That(ops.IndexOf(SendOp.ExpUp), Is.GreaterThan(ops.IndexOf(SendOp.Meso)));
            Assert.That(ops.IndexOf(SendOp.ItemInventory), Is.GreaterThan(ops.IndexOf(SendOp.ExpUp)));
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso + 100));
            Assert.That(Held(session), Is.EqualTo(2));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // The same request again: the refusal text, and the reward is given once.
    [Test]
    public void Complete_TwiceGivesTheRewardOnce() {
        GameSession session = Login();
        Quest quest = Started();
        long meso = session.Currency.Meso;

        Complete(session);
        test.TakeSent();
        Complete(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(QuestError.s_quest_error_consume_fail) }));
            Assert.That(quest.CompletionCount, Is.EqualTo(1));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso + 100));
            Assert.That(Held(session), Is.EqualTo(2));
        });
    }

    // A quest that is not held: the refusal text, nothing given.
    [Test]
    public void Complete_OfAQuestNotAcceptedGivesNothing() {
        GameSession session = Login();
        test.Register(Data());
        long meso = session.Currency.Meso;

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(QuestError.s_quest_error_consume_fail) }));
            Assert.That(session.Quest.TryGetQuest(QuestId, out _), Is.False);
            Assert.That(session.Currency.Meso, Is.EqualTo(meso));
            Assert.That(Held(session), Is.EqualTo(0));
        });
    }

    // A quest with an open condition: the refusal text, the quest stays in progress.
    [Test]
    public void Complete_WithAnOpenConditionGivesNothing() {
        GameSession session = Login();
        Quest quest = Started(met: false);
        long meso = session.Currency.Meso;

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(QuestError.s_quest_error_consume_fail) }));
            Assert.That(quest.State, Is.EqualTo(QuestState.Started));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso));
            Assert.That(Held(session), Is.EqualTo(0));
        });
    }

    // A full bag: the bag's notice alone, nothing given (no meso either), and the quest can still be completed.
    [Test]
    public void Complete_WithAFullBagStaysInProgress() {
        GameSession session = Login(fullBag: true);
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Misc), Is.EqualTo(0));
        Quest quest = Started();
        long meso = session.Currency.Meso;

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Convert.ToHexString(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory).ToArray()),
            }));
            Assert.That(quest.State, Is.EqualTo(QuestState.Started));
            Assert.That(quest.CompletionCount, Is.EqualTo(0));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso));
            Assert.That(Held(session), Is.EqualTo(0));
        });
    }

    // A dead player: its own text, the quest stays in progress.
    [Test]
    public void Complete_ByADeadPlayerGivesNothing() {
        GameSession session = Login();
        Quest quest = Started();
        long meso = session.Currency.Meso;
        typeof(FieldPlayer).GetField("isDead", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Player, true);

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(QuestError.s_quest_error_fail_complete_by_dead) }));
            Assert.That(quest.State, Is.EqualTo(QuestState.Started));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso));
            Assert.That(Held(session), Is.EqualTo(0));
        });
    }

    // An accept starts the quest; the same request again is refused with the text and starts nothing more.
    [Test]
    public void Accept_TwiceStartsTheQuestOnce() {
        GameSession session = Login();
        test.Register(Data(acceptItem: true));

        Accept(session);

        List<SendOp> ops = test.TakeSentOps();
        Assert.That(ops, Has.Member(SendOp.Quest));
        Assert.That(session.Quest.TryGetQuest(QuestId, out Quest? quest), Is.True);
        Assert.That(quest!.State, Is.EqualTo(QuestState.Started));
        Assert.That(Held(session), Is.EqualTo(1));

        Accept(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(QuestError.s_quest_error_accept_fail) }));
            Assert.That(Held(session), Is.EqualTo(1));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A quest the server does not know, and a quest that is already done: the refusal text.
    [Test]
    public void Accept_OfAnUnknownOrCompletedQuestIsRefused() {
        GameSession session = Login();
        Quest quest = Started();
        quest.State = QuestState.Completed;
        test.Register(Data());

        Accept(session, QuestId + 1);
        Accept(session);

        Assert.Multiple(() => {
            string error = Error(QuestError.s_quest_error_accept_fail);
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { error, error }));
            Assert.That(session.Quest.TryGetQuest(QuestId + 1, out _), Is.False);
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
        });
    }

    // A quest given at an npc that is not in the field: refused.
    [Test]
    public void Accept_WithoutItsNpcIsRefused() {
        GameSession session = Login();
        test.Register(Data() with { RemoteAccept = new QuestRemoteAccept(QuestRemoteType.None, 0) });

        Accept(session);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(QuestError.s_quest_error_accept_fail) }));
        Assert.That(session.Quest.TryGetQuest(QuestId, out _), Is.False);
    }

    // The quest's own items do not fit: the full-bag text, and the quest is not started.
    [Test]
    public void Accept_WithAFullBagDoesNotStartTheQuest() {
        GameSession session = Login(fullBag: true);
        test.Register(Data(acceptItem: true));

        Accept(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(QuestError.s_quest_error_inventory_full) }));
            Assert.That(session.Quest.TryGetQuest(QuestId, out _), Is.False);
            Assert.That(Held(session), Is.EqualTo(0));
        });
    }

    private static ScriptState State(int id) => new(id, ScriptStateType.Quest, false, null, []);

    // The talk of the quest's script at an npc: the progress talk (200) at the complete npc while a condition is
    // open, the complete talk (300) there once all are met, the progress talk elsewhere, none once it is done and
    // the accept talk (100) for a quest that is not held.
    [Test]
    public void Talk_OpensWithTheStateOfTheQuest() {
        GameSession session = Login();
        var script = new ScriptMetadata(QuestId, ScriptType.Quest, new Dictionary<int, ScriptState> {
            [100] = State(100), [200] = State(200), [201] = State(201), [300] = State(300),
        });

        Assert.That(NpcTalkUtil.GetQuestScriptState(session, script, Npc)?.Id, Is.EqualTo(100));

        Quest quest = Started(met: false);
        Assert.That(NpcTalkUtil.GetQuestScriptState(session, script, Npc)?.Id, Is.EqualTo(200));
        quest.Conditions[0].Counter = 1;
        Assert.Multiple(() => {
            Assert.That(NpcTalkUtil.GetQuestScriptState(session, script, Npc)?.Id, Is.EqualTo(300));
            Assert.That(NpcTalkUtil.GetQuestScriptState(session, script, Npc + 1)?.Id, Is.EqualTo(200));
        });
        quest.State = QuestState.Completed;
        Assert.That(NpcTalkUtil.GetQuestScriptState(session, script, Npc), Is.Null);

        // A script without a state in the range: none, and no exception.
        var bare = new ScriptMetadata(QuestId, ScriptType.Quest, new Dictionary<int, ScriptState> { [100] = State(100) });
        quest.State = QuestState.Started;
        Assert.That(NpcTalkUtil.GetQuestScriptState(session, bare, Npc), Is.Null);
    }
}
