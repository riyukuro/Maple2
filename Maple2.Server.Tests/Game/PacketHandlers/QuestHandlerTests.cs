using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
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

    // A quest of the popup talk: accepted and completed without an npc on the field.
    private static QuestMetadata Data(bool acceptItem = false, bool remote = true, int remoteMap = 0, int repeat = 0) {
        QuestMetadata data = TestGameSession.QuestData(QuestId);
        return data with {
            Basic = data.Basic with { CompleteNpc = Npc, Forfeitable = true, Repeat = repeat },
            RemoteAccept = new QuestRemoteAccept(QuestRemoteType.Popup, 0),
            RemoteComplete = new QuestRemoteComplete(remote ? QuestRemoteType.Popup : QuestRemoteType.None, remoteMap, false),
            AcceptReward = data.AcceptReward with { EssentialItem = acceptItem ? [new QuestMetadataReward.Item(RewardId, 1, 1)] : [] },
            CompleteReward = data.CompleteReward with { Meso = 100, Exp = 10, EssentialItem = [new QuestMetadataReward.Item(RewardId, 1, 2)] },
            Conditions = [new ConditionMetadata(ConditionType.jump, 1, null, null)],
        };
    }

    private GameSession Login(bool fullBag = false, TestGameSession? prepared = null) {
        test = prepared ?? new TestGameSession();
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
    private Quest Started(bool met = true, bool remote = true, int remoteMap = 0) {
        Quest quest = test.StartQuest(Data(remote: remote, remoteMap: remoteMap));
        quest.Conditions[0].Counter = met ? 1 : 0;
        return quest;
    }

    private void Accept(GameSession session, int questId = QuestId, int npcObjectId = 0) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(questId);
            writer.WriteInt(npcObjectId);
        }));
    }

    private void Complete(GameSession session, int questId = QuestId, int npcObjectId = 0) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(questId);
            writer.WriteInt(npcObjectId);
            writer.WriteInt(0);
        }));
    }

    private void Abandon(GameSession session) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(6);
            writer.WriteInt(QuestId);
        }));
    }

    // A quest of an npc is not completed without the npc (the popup talk's request) or with an npc that is not on
    // the field; twice the same.
    [TestCase(0)]
    [TestCase(77)]
    public void Complete_AwayFromTheQuestsNpcGivesNothing(int npcObjectId) {
        GameSession session = Login();
        Quest quest = Started(remote: false);
        long meso = session.Currency.Meso;

        Complete(session, npcObjectId: npcObjectId);
        Complete(session, npcObjectId: npcObjectId);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Error(QuestError.s_quest_error_consume_fail), Error(QuestError.s_quest_error_consume_fail),
            }));
            Assert.That(quest.State, Is.EqualTo(QuestState.Started));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso));
            Assert.That(Held(session), Is.EqualTo(0));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // An npc of the session's field with this object id (only its id is set).
    private static void AddNpc(GameSession session, int objectId, int npcId) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Id>k__BackingField", any)!.SetValue(metadata, npcId);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));
        session.Field!.Npcs[objectId] = npc;
    }

    // The talk names its npc by object id: the quest's complete npc on the field completes the quest.
    [Test]
    public void Complete_AtTheQuestsNpcOnTheField() {
        GameSession session = Login();
        Quest quest = Started(remote: false);
        AddNpc(session, 55, Npc);

        Complete(session, npcObjectId: 55);

        Assert.Multiple(() => {
            Assert.That(Convert.ToHexString(test.TakeSent()[0]), Is.EqualTo(Convert.ToHexString(QuestPacket.Complete(quest).ToArray())));
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(Held(session), Is.EqualTo(2));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // Another npc of the field is not the quest's.
    [Test]
    public void Complete_AtAnotherNpcOfTheFieldGivesNothing() {
        GameSession session = Login();
        Quest quest = Started(remote: false);
        AddNpc(session, 55, Npc + 1);

        Complete(session, npcObjectId: 55);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(QuestError.s_quest_error_consume_fail) }));
            Assert.That(quest.State, Is.EqualTo(QuestState.Started));
            Assert.That(Held(session), Is.EqualTo(0));
        });
    }

    // A remote completion that names a field is taken on that field only (the client offers it only there).
    [TestCase(0, true)]
    [TestCase(1, false)]
    public void Complete_RemoteOnItsField(int mapOffset, bool expected) {
        GameSession session = Login();
        Quest quest = Started(remoteMap: session.Player.Value.Character.MapId + mapOffset);

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(expected ? QuestState.Completed : QuestState.Started));
            Assert.That(Held(session), Is.EqualTo(expected ? 2 : 0));
            if (!expected) {
                Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(QuestError.s_quest_error_consume_fail) }));
            }
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // Abandon (Quest 6 with the quest id, answered with the same): the quest is dropped; a second request and a
    // request for a completed quest have no answer and change nothing.
    [Test]
    public void Abandon_DropsAStartedQuestOnce() {
        GameSession session = Login();
        test.Register(Data());
        Accept(session);
        Assert.That(session.Quest.TryGetQuest(QuestId, out _), Is.True);
        test.TakeSent();

        Abandon(session);
        Abandon(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { "5200" + "06" + "814A5D05" }));
            Assert.That(session.Quest.TryGetQuest(QuestId, out _), Is.False);
            Assert.That(test.Errors, Is.Empty);
        });
    }

    [Test]
    public void Abandon_OfACompletedQuestKeepsIt() {
        GameSession session = Login();
        Quest quest = Started();
        quest.State = QuestState.Completed;
        quest.CompletionCount = 1;

        Abandon(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(session.Quest.TryGetQuest(QuestId, out _), Is.True);
        });
    }

    // A daily quest completed on an earlier day, accepted again and abandoned goes back to the row it was: completed
    // with its count and the times of that completion, so the accept the client offers again is taken again.
    [Test]
    public void Abandon_OfAQuestStartedAgainPutsItsCompletionBack() {
        GameSession session = Login();
        QuestMetadata daily = Data(repeat: 2);
        test.Register(daily);
        Quest quest = test.StartQuest(daily);
        quest.State = QuestState.Completed;
        quest.CompletionCount = 3;
        quest.StartTime = 1700000000;
        quest.EndTime = 1700000600;

        Accept(session);
        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Started));
            Assert.That(quest.StartTime, Is.GreaterThan(1700000600));
            Assert.That(quest.EndTime, Is.EqualTo(0));
        });
        test.TakeSent();

        Abandon(session);
        Abandon(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { "5200" + "06" + "814A5D05" }));
            Assert.That(session.Quest.TryGetQuest(QuestId, out Quest? held), Is.True);
            Assert.That(held, Is.SameAs(quest));
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(quest.CompletionCount, Is.EqualTo(3));
            Assert.That(quest.StartTime, Is.EqualTo(1700000000));
            Assert.That(quest.EndTime, Is.EqualTo(1700000600));
        });

        // The client dropped the quest and offers it again.
        Accept(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(QuestPacket.Start(quest).ToArray()) }));
            Assert.That(quest.State, Is.EqualTo(QuestState.Started));
            Assert.That(quest.CompletionCount, Is.EqualTo(3));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A completion clears the kept times: they belong to the start it ended.
    [Test]
    public void Complete_OfAQuestStartedAgainDropsTheKeptTimes() {
        GameSession session = Login();
        QuestMetadata daily = Data(repeat: 2);
        test.Register(daily);
        Quest quest = test.StartQuest(daily);
        quest.State = QuestState.Completed;
        quest.CompletionCount = 1;
        quest.StartTime = 1700000000;
        quest.EndTime = 1700000600;
        Accept(session);
        Assert.That((quest.LastStartTime, quest.LastEndTime), Is.EqualTo((1700000000L, 1700000600L)));
        quest.Conditions[0].Counter = 1;

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(quest.CompletionCount, Is.EqualTo(2));
            Assert.That((quest.LastStartTime, quest.LastEndTime), Is.EqualTo((0L, 0L)));
            Assert.That(quest.EndTime, Is.GreaterThan(1700000600));
        });
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

    // A guild quest's relativeMeso (GuildQuest, rewardcontent 1003): the meso of its row at the player's level instead
    // of the fixed money. Without a guild there is no guild bonus. Made-up amounts.
    [Test]
    public void Complete_OfARelativeMesoQuestGivesTheTablesMeso() {
        GameSession session = Login();
        int level = session.Player.Value.Character.Level;
        test.SetTable(new RewardContentTable(
            new Dictionary<int, RewardContentTable.Base> { [1003] = new(1003, MesoTableId: 2, ExpTableId: 0, MesoFactor: 1f, ExpFactor: 0f, ItemTableId: 0, PrestigeExpTableId: 0) },
            new Dictionary<int, RewardContentTable.Item>(), new Dictionary<int, long>(),
            new Dictionary<int, Dictionary<int, long>> { [2] = new() { [level] = 4100 } }, new Dictionary<int, long>()));
        QuestMetadata data = Data();
        Quest quest = test.StartQuest(data with {
            Basic = data.Basic with { Type = QuestType.GuildQuest },
            CompleteReward = data.CompleteReward with { Meso = 0, RelativeMeso = 1003 },
        });
        quest.Conditions[0].Counter = 1;
        long meso = session.Currency.Meso;

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(session.Currency.Meso, Is.EqualTo(meso + 4100));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // completeReward menteeCoin pays the mentee token, capped by its max; weddingExp gives a single player nothing
    // and no error.
    [TestCase(0L, 100L)]
    [TestCase(34950L, 35000L)]
    public void Complete_PaysTheMenteeCoin(long held, long expected) {
        GameSession session = Login();
        session.Currency[CurrencyType.MenteeToken] = held;
        QuestMetadata data = Data();
        Quest quest = test.StartQuest(data with { CompleteReward = data.CompleteReward with { MenteeCoin = 100, WeddingExp = 5 } });
        quest.Conditions[0].Counter = 1;

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(session.Currency[CurrencyType.MenteeToken], Is.EqualTo(expected));
            Assert.That(session.Marriage.Marriage.ExpHistory, Is.Empty);
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // The quest that ends a job change row (the awakening): the job, the skill tree to the field (Job 2) after the
    // quest's packet, the job trophies' count and the flag that updates the other players' copy.
    [Test]
    public void Complete_OfTheJobChangeQuestChangesTheJob() {
        GameSession session = Login();
        ProgressionOrderTests.LoadConfig(test, session);
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata> {
            [Job.Newbie] = new(Job.Newbie, Job.Knight, 0, QuestId),
        }));
        Started();
        session.Player.Flag = PlayerObjectFlag.None;
        test.TakeSent();

        Complete(session);

        List<byte[]> sent = test.TakeSent();
        List<SendOp> ops = sent.Select(TestGameSession.Op).ToList();
        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Character.Job, Is.EqualTo(Job.Knight));
            Assert.That(ops.IndexOf(SendOp.Job), Is.GreaterThan(ops.IndexOf(SendOp.Quest)));
            Assert.That(sent.First(packet => TestGameSession.Op(packet) == SendOp.Job)[6], Is.EqualTo(2));
            Assert.That(session.Player.Flag.HasFlag(PlayerObjectFlag.Job), Is.True);
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

    // The accept talk names its npc (cinematiccomponent.cpp:584): only the quest's start npc gives it
    // (CQuestManagerCommon::vfunc_o10_21, commonfiles/questdata.cpp:330), another npc of the field does not.
    [TestCase(Npc, true)]
    [TestCase(Npc + 1, false)]
    public void Accept_AtTheQuestsStartNpcOnly(int npcId, bool expected) {
        GameSession session = Login();
        QuestMetadata data = Data();
        test.Register(data with { Basic = data.Basic with { StartNpc = Npc }, RemoteAccept = new QuestRemoteAccept(QuestRemoteType.None, 0) });
        AddNpc(session, 55, npcId);

        Accept(session, npcObjectId: 55);

        Assert.Multiple(() => {
            Assert.That(session.Quest.TryGetQuest(QuestId, out _), Is.EqualTo(expected));
            if (!expected) {
                Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Error(QuestError.s_quest_error_accept_fail) }));
            }
        });
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

    // A quest scroll whose quest's items do not fit: the full-bag text, and the scroll stays. With room, the quest
    // starts and the scroll is used.
    [TestCase(true)]
    [TestCase(false)]
    public void Scroll_OfAQuestThatDoesNotStartIsKept(bool fullBag) {
        const int scrollId = 40000001;
        ItemMetadata scroll = TestGameSession.Metadata(scrollId, type: 2,
            function: new ItemMetadataFunction(ItemFunction.QuestScroll, "QuestScroll", $"<v questID=\"{QuestId}\" />", false));
        test = new TestGameSession();
        test.Register(scroll);
        Item held = test.Store(scroll);
        GameSession session = Login(fullBag, test);
        test.Register(Data(acceptItem: true));
        Assert.That(session.Item.Inventory.Get(held.Uid), Is.Not.Null);

        new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! }
            .Handle(session, TestGameSession.Packet(writer => writer.WriteLong(held.Uid)));

        List<string> sent = test.TakeSent().Select(Convert.ToHexString).ToList();
        Assert.Multiple(() => {
            Assert.That(session.Quest.TryGetQuest(QuestId, out _), Is.EqualTo(!fullBag));
            Assert.That(session.Item.Inventory.Get(held.Uid), fullBag ? Is.Not.Null : Is.Null);
            Assert.That(sent.Contains(Error(QuestError.s_quest_error_inventory_full)), Is.EqualTo(fullBag));
            Assert.That(sent.Contains(Convert.ToHexString(ItemUsePacket.QuestScroll(scrollId).ToArray())), Is.EqualTo(!fullBag));
        });
    }

    // A forced accept (a map script's) is not refused by a full bag: the quest starts and its item is not lost.
    [Test]
    public void ForcedStart_WithAFullBagStartsTheQuest() {
        GameSession session = Login(fullBag: true);
        test.Register(Data(acceptItem: true));

        QuestError error = session.Quest.Start(QuestId, bypassRequirements: true);

        using Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(error, Is.EqualTo(QuestError.none));
            Assert.That(session.Quest.TryGetQuest(QuestId, out _), Is.True);
            Assert.That(Held(session), Is.EqualTo(0));
            Assert.That(db.GetAllMail(session.CharacterId).SelectMany(mail => mail.Items).Sum(item => item.Id == RewardId ? item.Amount : 0), Is.EqualTo(1));
        });
    }

    // The talk itself is the quest's last condition (talk_in): it is counted before the script is picked, so the
    // same click opens the complete talk (300), not the progress talk.
    [Test]
    public void Talk_CountsTheTalkBeforeThePick() {
        GameSession session = Login();
        test.Register(new ScriptMetadata(QuestId, ScriptType.Quest, new Dictionary<int, ScriptState> {
            [200] = State(200),
            [300] = State(300),
        }));
        QuestMetadata data = Data() with {
            Conditions = [new ConditionMetadata(ConditionType.talk_in, 1, new ConditionMetadata.Parameters(Integers: [Npc]), null)],
        };
        Quest quest = test.StartQuest(data);
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>()));

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(Npc, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));
        typeof(FieldNpc).GetField(nameof(FieldNpc.SpawnAnimation), any)!.SetValue(npc, "spawn"); // no movement state to play the talk on

        NpcTalkHandler.BeginTalk(session, npc);

        Assert.Multiple(() => {
            Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
            Assert.That(session.NpcScript?.State?.Id, Is.EqualTo(300));
        });
    }

    // talk_in's target is the npc script the talk reaches (22200019: target 41, the script whose function collects
    // the item): the talk opens script 40 and its distractor goes to 41. A row with the matching script counts when
    // the talk enters it, one with another script (30) does not, and a row without a target counts the talk once.
    [Test]
    public void Talk_CountsTheNpcScriptTheTalkReaches() {
        const int talker = Npc + 1;
        GameSession session = Login();
        var ask = new CinematicContent("", NpcTalkButton.None, 0, [new CinematicDistractor([41], [])], []);
        var deliver = new CinematicContent("", NpcTalkButton.None, 1, [], []);
        test.Register(new ScriptMetadata(talker, ScriptType.Npc, new Dictionary<int, ScriptState> {
            [40] = new(40, ScriptStateType.Script, true, null, [ask]),
            [41] = new(41, ScriptStateType.Script, false, null, [deliver]),
        }));
        ConditionMetadata TalkIn(int[]? target) => new(ConditionType.talk_in, 1, new ConditionMetadata.Parameters(Integers: [talker]),
            target == null ? null : new ConditionMetadata.Parameters(Integers: target));
        Quest quest = test.StartQuest(Data() with { Conditions = [TalkIn([41]), TalkIn([40]), TalkIn([30]), TalkIn(null)] });
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>()));

        NpcTalkHandler.BeginTalk(session, TalkNpc(talker));
        Assert.That(Enumerable.Range(0, 4).Select(i => quest.Conditions[i].Counter), Is.EqualTo(new long[] { 0, 1, 0, 1 }));

        Assert.That(session.NpcScript!.Continue(0), Is.True);
        Assert.Multiple(() => {
            Assert.That(session.NpcScript.State?.Id, Is.EqualTo(41));
            Assert.That(Enumerable.Range(0, 4).Select(i => quest.Conditions[i].Counter), Is.EqualTo(new long[] { 1, 1, 0, 1 }));
        });
    }

    // NpcTalk 7 opens a quest's script at the talking npc. The client sends only the quest it has just accepted
    // there or the following quest of one it has just completed there; another quest's talk is refused, as its
    // script's functions run in it. Before, any quest id opened its accept talk (100) at any npc.
    [TestCase("unrelated", null)]
    [TestCase("held", 200)]
    [TestCase("following", 100)]
    public void QuestTalk_OpensOnlyAQuestOfThisNpc(string kind, int? opened) {
        GameSession session = Login();
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        test.SetServerTable(new ScriptFunctionTable(new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>()));
        int questId = QuestId;
        switch (kind) {
            case "unrelated":
                questId = QuestId + 1;
                QuestMetadata other = TestGameSession.QuestData(questId);
                test.Register(other with { Basic = other.Basic with { StartNpc = Npc + 1, CompleteNpc = Npc + 1 } });
                break;
            case "held":
                Started(met: false);
                break;
            case "following":
                Started().State = QuestState.Completed;
                questId = QuestId + 1;
                QuestMetadata next = TestGameSession.QuestData(questId);
                test.Register(next with {
                    Basic = next.Basic with { StartNpc = Npc + 1, CompleteNpc = Npc + 1 },
                    Require = next.Require with { Quest = [QuestId] },
                });
                break;
        }
        test.Register(new ScriptMetadata(questId, ScriptType.Quest, new Dictionary<int, ScriptState> {
            [100] = State(100),
            [200] = State(200),
        }));
        session.NpcScript = new NpcScriptManager(session, TalkNpc(Npc), null, null, NpcTalkType.Talk);

        var talk = new NpcTalkHandler { NpcMetadata = null!, ScriptMetadata = null!, ServerTableMetadata = test.ServerTableMetadata };
        talk.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(7);
            writer.WriteInt(questId);
            writer.WriteShort(0);
        }));

        Assert.That(session.NpcScript?.State?.Id, Is.EqualTo(opened));
    }

    // A field npc of the given id with no movement state to play the talk on.
    private static FieldNpc TalkNpc(int id) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var basic = new NpcMetadataBasic(0, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
        var metadata = new NpcMetadata(id, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));
        typeof(FieldNpc).GetField(nameof(FieldNpc.SpawnAnimation), any)!.SetValue(npc, "spawn");
        return npc;
    }

    private static ScriptState State(int id) => new(id, ScriptStateType.Quest, false, null, []);

    // The talk of the quest's script at an npc: the progress talk (200) at the complete npc while a condition is
    // open, the complete talk (300) there once all are met, the progress talk elsewhere, none once it is done and
    // the accept talk (100) for a quest that is not held.
    [Test]
    public void Talk_OpensWithTheStateOfTheQuest() {
        GameSession session = Login();
        var script = new ScriptMetadata(QuestId, ScriptType.Quest, new Dictionary<int, ScriptState> {
            [100] = State(100),
            [200] = State(200),
            [201] = State(201),
            [300] = State(300),
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

    // A timed quest whose end time has come (a duration that ran out, an event tag whose term is over) gets
    // invalid_date (4) and nothing, as the shared completion check gives it; inside its time it completes.
    [TestCase(30, -10, false)]
    [TestCase(30, -40, true)]
    public void Complete_OfATimedQuestAfterItsEndGivesInvalidDate(int minutes, int startedMinutesAgo, bool expired) {
        GameSession session = Login();
        Quest quest = test.StartQuest(Data() with { Basic = Data().Basic with { DurationMinutes = minutes } });
        quest.Conditions[0].Counter = 1;
        quest.StartTime = DateTimeOffset.UtcNow.AddMinutes(startedMinutesAgo).ToUnixTimeSeconds();
        long meso = session.Currency.Meso;

        Complete(session);

        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            if (expired) {
                Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] { Error(QuestError.s_quest_error_invalid_date) }));
            } else {
                Assert.That(Convert.ToHexString(sent[0]), Is.EqualTo(Convert.ToHexString(QuestPacket.Complete(quest).ToArray())));
            }
            Assert.That(quest.State, Is.EqualTo(expired ? QuestState.Started : QuestState.Completed));
            Assert.That(session.Currency.Meso, Is.EqualTo(expired ? meso : meso + 100));
            Assert.That(Held(session), Is.EqualTo(expired ? 0 : 2));
        });
    }

    // The term of an eventTag quest is the term tag of that name (the client's GetEndTime, questdata.cpp:274): a
    // running term completes it, a term that is over gives invalid_date. A QuestTag game event is no term.
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Complete_OfAnEventTagQuestNeedsItsTerm(bool live, bool expired) {
        var prepared = new TestGameSession();
        prepared.AddEvent(1, GameEventType.QuestTag, new QuestTag("tag"));
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        prepared.SetServerTable(new TermTagTable(new Dictionary<int, TermTagTable.Entry> {
            [1] = new(1, "tag", now - 3600, live ? now + 3600 : now - 60),
        }));
        GameSession session = Login(prepared: prepared);
        Quest quest = test.StartQuest(Data() with { Basic = Data().Basic with { EventTag = "tag" } });
        quest.Conditions[0].Counter = 1;
        quest.StartTime = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString).First() == Error(QuestError.s_quest_error_invalid_date), Is.EqualTo(expired));
            Assert.That(quest.State, Is.EqualTo(expired ? QuestState.Started : QuestState.Completed));
        });
    }

    // An eventTag quest can start only while the term tag of its name runs on the channel (questdata.cpp:358-363), as
    // the client offers it; a live QuestTag game event of that tag does not open it.
    [TestCase(true, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, false, false)]
    public void CanStart_AnEventTagQuestFollowsItsTermTag(bool termRunning, bool questTagEvent, bool expected) {
        var prepared = new TestGameSession();
        if (questTagEvent) {
            prepared.AddEvent(1, GameEventType.QuestTag, new QuestTag("tag"));
        }
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        prepared.SetServerTable(new TermTagTable(new Dictionary<int, TermTagTable.Entry> {
            [1] = new(1, "tag", now - 3600, termRunning ? now + 3600 : now - 60),
        }));
        GameSession session = Login(prepared: prepared);

        Assert.That(session.Quest.CanStart(Data() with { Basic = Data().Basic with { EventTag = "tag" } }), Is.EqualTo(expected));
    }

    // The channel's GM start of a term opens its eventTag quests; its end closes them.
    [Test]
    public void CanStart_AGmStartedTermOpensItsQuests() {
        var prepared = new TestGameSession();
        prepared.SetServerTable(new TermTagTable(new Dictionary<int, TermTagTable.Entry> { [1] = new(1, "tag", 1000, 2000) }));
        GameSession session = Login(prepared: prepared);
        QuestMetadata metadata = Data() with { Basic = Data().Basic with { EventTag = "tag" } };
        Assert.That(session.Quest.CanStart(metadata), Is.False);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.TermTags.Set(new TermTagTable.Entry(1, "tag", now - 10, now + 600), now);
        Assert.That(session.Quest.CanStart(metadata), Is.True);

        session.TermTags.Set(new TermTagTable.Entry(1, "tag", now - 10, now - 1), now);
        Assert.That(session.Quest.CanStart(metadata), Is.False);
    }

    // An event quest held from an earlier run of its term (started before the running run began) is taken again, in
    // progress or completed and without a repeat: the client offers it when the held start time is not within the
    // term (questdata.cpp:366-369, before the state). One held from this run is not.
    [TestCase(QuestState.Completed, true, true)]
    [TestCase(QuestState.Started, true, true)]
    [TestCase(QuestState.Completed, false, false)]
    public void Start_AnEventQuestOncePerRunOfItsTerm(QuestState state, bool earlierRun, bool taken) {
        var prepared = new TestGameSession();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        prepared.SetServerTable(new TermTagTable(new Dictionary<int, TermTagTable.Entry> { [1] = new(1, "tag", now - 3600, now + 3600) }));
        GameSession session = Login(prepared: prepared);
        QuestMetadata metadata = Data() with { Basic = Data().Basic with { EventTag = "tag" } };
        test.Register(metadata);
        Quest quest = test.StartQuest(metadata);
        quest.State = state;
        quest.StartTime = earlierRun ? now - 7200 : now - 60;

        QuestError error = session.Quest.Start(QuestId);

        Assert.Multiple(() => {
            Assert.That(error, Is.EqualTo(taken ? QuestError.none : QuestError.s_quest_error_accept_fail));
            Assert.That(quest.State, Is.EqualTo(taken ? QuestState.Started : state));
            Assert.That(quest.StartTime, taken ? Is.AtLeast(now) : Is.EqualTo(now - 60));
        });
    }

    // A held event quest is expired (its completion gives invalid_date) when no term of its tag is known, or when it
    // was started outside the running run (CQuestCommon::GetEndTime gives 0, questdata.cpp:278-284); not when it was
    // started within it. The tag matches the term name in its case, as the client's map does (gamerule.cpp:1323).
    [TestCase("tag", 60, false)]
    [TestCase("tag", 7200, true)]
    [TestCase("Tag", 60, true)]
    [TestCase("other", 60, true)]
    public void IsExpired_AnEventQuestOutsideItsTerm(string termName, int startedAgo, bool expired) {
        var prepared = new TestGameSession();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        prepared.SetServerTable(new TermTagTable(new Dictionary<int, TermTagTable.Entry> { [1] = new(1, termName, now - 3600, now + 3600) }));
        GameSession session = Login(prepared: prepared);
        Quest quest = test.StartQuest(Data() with { Basic = Data().Basic with { EventTag = "tag" } });
        quest.StartTime = now - startedAgo;

        Assert.That(session.Quest.IsExpired(quest), Is.EqualTo(expired));
    }

    // Quest 26 (the world map's exploration reward): the round's item, "id,grade,count" of fieldMission.xml as the
    // client reads it (ItemEntity), is given in its count, once, after that many field missions are completed.
    // Made-up round and count.
    [TestCase(5, 10)]
    [TestCase(4, 0)]
    public void FieldMission_GivesTheRoundsItemCount(int completed, int expected) {
        GameSession session = Login();
        test.SetTable(new FieldMissionTable(new Dictionary<int, FieldMissionTable.Entry> {
            [5] = new(5, new ItemComponent(RewardId, 1, 10, ItemTag.None), 0),
        }));
        for (int i = 0; i < completed; i++) {
            test.StartQuest(TestGameSession.QuestData(QuestId + 1 + i, QuestType.FieldMission)).State = QuestState.Completed;
        }

        for (int i = 0; i < 2; i++) {
            handler.Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(26);
                writer.WriteInt(5);
            }));
        }

        Assert.Multiple(() => {
            Assert.That(Held(session), Is.EqualTo(expected));
            Assert.That(session.Config.ExplorationProgress, Is.EqualTo(expected > 0 ? 5 : 0));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // KR kr_fieldmission.xml rounds 5 (an item) and 10: the client claims the round after the last one claimed
    // (CQuestManager::GetNextRound), so with 10 missions completed a claim of round 10 before round 5 gives
    // nothing; then 5 and 10 each give theirs. Made-up items.
    [Test]
    public void FieldMission_TakesOnlyTheNextRound() {
        GameSession session = Login();
        test.SetTable(new FieldMissionTable(new Dictionary<int, FieldMissionTable.Entry> {
            [10] = new(10, new ItemComponent(RewardId, 1, 3, ItemTag.None), 0),
            [5] = new(5, new ItemComponent(RewardId, 1, 1, ItemTag.None), 0),
        }));
        for (int i = 0; i < 10; i++) {
            test.StartQuest(TestGameSession.QuestData(QuestId + 1 + i, QuestType.FieldMission)).State = QuestState.Completed;
        }
        void Claim(int round) => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(26);
            writer.WriteInt(round);
        }));

        Claim(10);
        Assert.That((Held(session), session.Config.ExplorationProgress), Is.EqualTo((0, 0)));
        Claim(5);
        Assert.That((Held(session), session.Config.ExplorationProgress), Is.EqualTo((1, 5)));
        Claim(10);
        Assert.That((Held(session), session.Config.ExplorationProgress), Is.EqualTo((4, 10)));
        Assert.That(test.Errors, Is.Empty);
    }

    // A started quest of the KR row's type with this complete reward, its one condition met.
    private Quest StartedWith(QuestType type, Func<QuestMetadataReward, QuestMetadataReward> reward, AllianceType alliance = AllianceType.AnyFaction,
                              int rank = 5) {
        QuestMetadata data = Data();
        Quest quest = test.StartQuest(data with {
            Basic = data.Basic with { Type = type, Alliance = alliance, Rank = rank },
            CompleteReward = reward(data.CompleteReward),
        });
        quest.Conditions[0].Counter = 1;
        return quest;
    }

    // KR 10002660 (a world quest): completeReward lu 1, the rue (a made-up cap).
    [Test]
    public void Complete_PaysTheRue() {
        GameSession session = Login();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { LuTokenMax = 30000 });
        long rue = session.Currency[CurrencyType.Rue];
        Quest quest = StartedWith(QuestType.WorldQuest, reward => reward with { Rue = 1 });

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(session.Currency[CurrencyType.Rue], Is.EqualTo(rue + 1));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // KR 70200001 (a daily mission): completeReward missionPoint 30 counts 30 on the mission point rows (70900001).
    [Test]
    public void Complete_CountsTheMissionPoints() {
        GameSession session = Login();
        Quest points = test.StartQuest(TestGameSession.QuestData(70900001, QuestType.DailyMission) with {
            Conditions = [new ConditionMetadata(ConditionType.mission_point, 100, null, null)],
        });
        Quest quest = StartedWith(QuestType.DailyMission, reward => reward with { MissionPoint = 30 });

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(points.Conditions[0].Counter, Is.EqualTo(30));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // KR 73000001 (a guild quest): completeReward guildCoin 8, the guild coin item; without a guild no bonus.
    [Test]
    public void Complete_GivesTheGuildCoin() {
        var prepared = new TestGameSession();
        prepared.Register(TestGameSession.Metadata(Constant.GuildCoinId));
        GameSession session = Login(prepared: prepared);
        Quest quest = StartedWith(QuestType.GuildQuest, reward => reward with { GuildCoin = 8 });

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(session.Item.Inventory.Find(Constant.GuildCoinId).Sum(item => item.Amount), Is.EqualTo(8));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // KR 10001172: essentialJobItem lists a weapon of each job (rarity 3); the player gets the ones recommended to its
    // job. 13000020 recommends the newbie (JobCode 1), 13200072 the knight (10), 13100069 the thief (70). The client
    // takes the newbie as a knight (ItemLimit::IsJobRecommended), so the test's newbie gets the knight's weapon and
    // the row of job 1 goes to nobody.
    [Test]
    public void Complete_GivesTheJobItemsOfThePlayersJob() {
        var prepared = new TestGameSession();
        foreach ((int id, JobCode job) in new[] { (13000020, JobCode.Newbie), (13200072, JobCode.Knight), (13100069, JobCode.Thief) }) {
            ItemMetadata metadata = TestGameSession.Metadata(id);
            prepared.Register(metadata with { Limit = metadata.Limit with { JobRecommends = [job] } });
        }
        GameSession session = Login(prepared: prepared);
        Assume.That(session.Player.Value.Character.Job, Is.EqualTo(Job.Newbie));
        Quest quest = StartedWith(QuestType.EpicQuest, reward => reward with {
            EssentialJobItem = [new QuestMetadataReward.Item(13000020, 3, 1), new QuestMetadataReward.Item(13100069, 3, 1),
                new QuestMetadataReward.Item(13200072, 3, 1)],
        });

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(session.Item.Inventory.Find(13200072).Sum(item => item.Amount), Is.EqualTo(1));
            Assert.That(session.Item.Inventory.Find(13200072).Single().Rarity, Is.EqualTo(3));
            Assert.That(session.Item.Inventory.Find(13000020), Is.Empty);
            Assert.That(session.Item.Inventory.Find(13100069), Is.Empty);
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // KR 91000080 (an alliance quest of the Kritias Maple Alliance, rank S 0): completeReward useMainFamePoint 1 adds
    // the Lua fame points of the later alliances' table, 230, to the alliance and sends them.
    [Test]
    public void Complete_AddsTheAlliancesFame() {
        GameSession session = Login();
        Quest quest = StartedWith(QuestType.AllianceQuest, reward => reward with { UseMainFamePoint = 1 }, AllianceType.KritiasMapleAlliance, rank: 0);

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(session.Player.Value.Unlock.FamePoints[AllianceType.KritiasMapleAlliance], Is.EqualTo(230));
            Assert.That(test.TakeSent().Select(Convert.ToHexString),
                Has.Member(Convert.ToHexString(UserEnvPacket.FamePoint(AllianceType.KritiasMapleAlliance, 230).ToArray())));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // KR 50000001 (a wedding mission): completeReward weddingExp 5 adds the completing partner's entry of 5 to the
    // marriage's exp log, and the player's marriage is read again with it.
    [Test]
    public void Complete_AddsTheWeddingMissionExp() {
        GameSession session = Login();
        using (GameStorage.Request db = test.Storage.Context()) {
            Account account = db.CreateAccount(new Account { Username = "partner" }, "pw");
            var partner = new Character { AccountId = account.Id, Name = "partner", Mastery = new Mastery() };
            partner.ReturnMaps.Push(2000062);
            long partnerId = db.CreateCharacter(partner)!.Id;
            Marriage marriage = db.CreateMarriage(test.CharacterId, partnerId)!;
            Assert.That(db.SetMarried(marriage.Id, DateTime.Now), Is.True);
            session.Marriage.Marriage = db.GetMarriage(test.CharacterId)!;
        }
        Quest quest = StartedWith(QuestType.WeddingMission, reward => reward with { WeddingExp = 5 });

        Complete(session);

        Assert.Multiple(() => {
            Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
            Assert.That(session.Marriage.Marriage.ExpHistory.Select(exp => exp.Amount), Is.EqualTo(new[] { 5L }));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    private void Dispatch(GameSession session, short kind) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(20);
            writer.WriteInt(QuestId);
            writer.WriteShort(kind);
        }));
    }

    private QuestMetadata Dispatching(QuestDispatchType type, int mapId, int portalId) {
        // The test's field is one the played client does not have, so the move stops in PrepareField.
        test.AddMap(TestGameSession.Map(mapId, notInPlayedClient: true) with {
            Limit = new MapMetadataLimit(0, 0, 0, 0, [], false, false, false, false, false, false, false),
        });
        return Data() with { Dispatch = new QuestDispatch(type, mapId, portalId, 0) };
    }

    // The move's last packet (after the not-in-the-played-client line), or null when nothing was sent.
    private string? Moved() => test.TakeSent().Select(Convert.ToHexString).LastOrDefault();

    private static readonly string MoveError = Convert.ToHexString(FieldEnterPacket.Error(MigrationError.s_move_err_default).ToArray());

    // KR 10003240 (dispatch type directComplete, field 52100202, portal 6001): the client's box after the
    // completion sends Quest 20 with kind 4 and moves the player once (to the move error, see Dispatching). A second send, or a kind 3, does nothing.
    [Test]
    public void Dispatch_AfterADirectCompleteMovesOnce() {
        GameSession session = Login();
        QuestMetadata data = Dispatching(QuestDispatchType.DirectComplete, 52100202, 6001);
        test.Register(data);
        Quest quest = test.StartQuest(data);
        quest.Conditions[0].Counter = 1;
        Complete(session);
        Assert.That(quest.State, Is.EqualTo(QuestState.Completed));
        test.TakeSent();

        Dispatch(session, 3);
        Assert.That(Moved(), Is.Null);
        Dispatch(session, 4);
        Assert.That(Moved(), Is.EqualTo(MoveError));
        Dispatch(session, 4);
        Assert.That(Moved(), Is.Null);
    }

    // KR 10003230 (directAccept, field 52100300, portal 2): the box after the start sends kind 3. Before the
    // start, with kind 4, or for a monologue dispatch (the client shows no box) nothing moves.
    [TestCase(QuestDispatchType.DirectAccept, (short) 3, true)]
    [TestCase(QuestDispatchType.DirectAccept, (short) 4, false)]
    [TestCase(QuestDispatchType.MonologueAccept, (short) 3, false)]
    [TestCase(QuestDispatchType.MonologueAccept, (short) 0, false)]
    public void Dispatch_AfterADirectAcceptStart(QuestDispatchType type, short kind, bool moves) {
        GameSession session = Login();
        test.Register(Dispatching(type, 52100300, 2));
        Dispatch(session, 3);
        Assert.That(test.TakeSent(), Is.Empty);
        Accept(session);
        Assert.That(session.Quest.TryGetQuest(QuestId, out Quest? quest) && quest.State == QuestState.Started, Is.True);
        test.TakeSent();

        Dispatch(session, kind);

        Assert.That(Moved(), Is.EqualTo(moves ? MoveError : null));
    }

    // Quest 8 (the self-starting quests of the field, client/ms2/questmanager_2.cpp:2206): KR 70000001 (daily
    // mission, guideType level) starts; KR 70100000 (daily mission, guideType level_boost) and KR 91000016 (field
    // quest), both autoStart, are ones the client never lists (questdata.cpp:673), so they do not.
    [Test]
    public void StartList_TakesOnlyTheQuestsTheClientLists() {
        GameSession session = Login();
        QuestMetadata Auto(int id, QuestType type, int guideType) {
            QuestMetadata data = TestGameSession.QuestData(id, type);
            return data with { Basic = data.Basic with { AutoStart = true, GuideType = guideType } };
        }
        test.Register(Auto(70000001, QuestType.DailyMission, 1));
        test.Register(Auto(70100000, QuestType.DailyMission, 2));
        test.Register(Auto(91000016, QuestType.FieldQuest, 0));

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(8);
            writer.WriteInt(3);
            writer.WriteInt(70000001);
            writer.WriteInt(70100000);
            writer.WriteInt(91000016);
        }));

        Assert.Multiple(() => {
            Assert.That(session.Quest.TryGetQuest(70000001, out _), Is.True);
            Assert.That(session.Quest.TryGetQuest(70100000, out _), Is.False);
            Assert.That(session.Quest.TryGetQuest(91000016, out _), Is.False);
            Assert.That(test.Errors, Is.Empty);
        });
    }
}
