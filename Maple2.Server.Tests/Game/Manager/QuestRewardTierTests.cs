using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Manager;

// The item rows of a quest's rewards against what the client shows (QuestRewardItem rows of one list, each shown to
// the jobs its item recommends: ItemLimit::IsJobRecommended, itemdatastruct.cpp, through FUN_140748050 of
// questdata.cpp). The KR rows are the enabled quests of the KR quest table and the recommendJobs of the server's
// and the client's item packs.
public class QuestRewardTierTests {
    private static readonly JobCode[] Jobs = [
        JobCode.Newbie, JobCode.Knight, JobCode.Berserker, JobCode.Wizard, JobCode.Priest, JobCode.Archer, JobCode.HeavyGunner,
        JobCode.Thief, JobCode.Assassin, JobCode.RuneBlader, JobCode.Striker, JobCode.SoulBinder,
    ];

    // Every recommendJobs set of the essentialJobItem rows (accept and complete) of the enabled KR quests, as
    // (server pack, client pack, rows): 853 rows, 19 sets; one row names an item neither pack has. The server pack
    // writes 0 for an item of all jobs, the client pack leaves the attribute out.
    private static readonly object[] JobItemSets = [
        new object[] { new[] { 0 }, new int[0], 30 },
        new object[] { new[] { 1 }, new[] { 1 }, 14 },
        new object[] { new[] { 1, 10 }, new[] { 1, 10 }, 7 },
        new object[] { new[] { 10 }, new[] { 10 }, 50 },
        new object[] { new[] { 10, 20, 90 }, new[] { 10, 20, 90 }, 82 },
        new object[] { new[] { 20 }, new[] { 20 }, 35 },
        new object[] { new[] { 30 }, new[] { 30 }, 35 },
        new object[] { new[] { 30, 40 }, new[] { 30, 40 }, 65 },
        new object[] { new[] { 40 }, new[] { 40 }, 58 },
        new object[] { new[] { 50 }, new[] { 50 }, 35 },
        new object[] { new[] { 50, 60 }, new[] { 50, 60 }, 68 },
        new object[] { new[] { 60 }, new[] { 60 }, 35 },
        new object[] { new[] { 70 }, new[] { 70 }, 35 },
        new object[] { new[] { 70, 80 }, new[] { 70, 80 }, 82 },
        new object[] { new[] { 80 }, new[] { 80 }, 35 },
        new object[] { new[] { 90 }, new[] { 90 }, 35 },
        new object[] { new[] { 100 }, new[] { 100 }, 35 },
        new object[] { new[] { 100, 110 }, new[] { 100, 110 }, 82 },
        new object[] { new[] { 110 }, new[] { 110 }, 35 },
    ];

    // The client's test: no recommended job is every job, and job 1 is looked up as 10.
    private static bool ClientShows(int[] recommends, JobCode job) =>
        recommends.Length == 0 || recommends.Contains(job == JobCode.Newbie ? 10 : (int) job);

    [TestCaseSource(nameof(JobItemSets))]
    public void JobItemRow_GoesToTheJobsTheClientShowsItTo(int[] server, int[] client, int rows) {
        JobCode[] recommends = server.Select(job => (JobCode) job).ToArray();

        Assert.That(Jobs.Where(job => QuestRules.IsRewardForJob(recommends, job)),
            Is.EqualTo(Jobs.Where(job => ClientShows(client, job))), $"{rows} rows");
    }

    // The job-limited essentialItem rows of the enabled KR quests that require a job, as (quest, required job, item,
    // server pack recommendJobs). The server gives an essentialItem row to everyone who completes the quest: the client
    // shows these to the job the quest requires, and 90000411 and 90000431 have an item the server pack recommends
    // to job 60 where the client pack has the required job, so a job test on the server pack would drop them.
    // 50001031 is the beginner's quest of the knight's weapon.
    private static readonly object[] JobOnlyEssentialItems = [
        new object[] { 20002297, 60, 15300219, 60 },
        new object[] { 50001031, 1, 13200043, 10 },
        new object[] { 50001032, 10, 13200043, 10 },
        new object[] { 50001033, 20, 15000041, 20 },
        new object[] { 50001034, 30, 15200142, 30 },
        new object[] { 50001035, 40, 13300138, 40 },
        new object[] { 50001035, 40, 14000118, 40 },
        new object[] { 50001036, 50, 15100136, 50 },
        new object[] { 50001037, 60, 15300136, 60 },
        new object[] { 50001038, 70, 13100028, 70 },
        new object[] { 50001039, 80, 13400136, 80 },
        new object[] { 50001040, 90, 15400064, 90 },
        new object[] { 50001340, 100, 15500064, 100 },
        new object[] { 50001341, 110, 15600064, 110 },
        new object[] { 90000411, 90, 15400274, 60 },
        new object[] { 90000417, 90, 11300417, 90 },
        new object[] { 90000417, 90, 11600373, 90 },
        new object[] { 90000417, 90, 15400003, 90 },
        new object[] { 90000431, 100, 15500514, 60 },
        new object[] { 90000450, 110, 15600116, 110 },
    ];

    private TestGameSession test = null!;

    private static ItemMetadata Item(int id, params JobCode[] recommends) {
        ItemMetadata metadata = TestGameSession.Metadata(id);
        return metadata with { Limit = metadata.Limit with { JobRecommends = recommends } };
    }

    private GameSession Login(Job job, bool fullBag = false, TestGameSession? prepared = null, params ItemMetadata[] items) {
        test = prepared ?? new TestGameSession();
        test.Register(items);
        if (fullBag) {
            test.Store(TestGameSession.Metadata(20000009));
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = [1, 0] });
        }
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        GameSession session = test.Start();
        session.Player.Value.Character.Job = job;
        test.TakeSent();
        return session;
    }

    private static int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    [TestCaseSource(nameof(JobOnlyEssentialItems))]
    public void JobOnlyEssentialItem_IsGivenToTheRequiredJob(int questId, int requiredJob, int itemId, int serverRecommends) {
        GameSession session = Login((Job) (requiredJob * 10), items: Item(itemId, (JobCode) serverRecommends));
        QuestMetadata data = TestGameSession.QuestData(questId);
        Quest quest = test.StartQuest(data with {
            Require = data.Require with { Job = [(JobCode) requiredJob] },
            CompleteReward = data.CompleteReward with { EssentialItem = [new QuestMetadataReward.Item(itemId, 1, 1)] },
        });

        Assert.Multiple(() => {
            Assert.That(session.Quest.Complete(quest), Is.True);
            Assert.That(Held(session, itemId), Is.EqualTo(1));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // KR 50100720 (level 60): its acceptReward has only essentialJobItem rows, two items of all jobs (41000041,
    // 43000041) and one of each job (42000451 the knight's .. 42000551 the soul binder's, every tenth id). The
    // client shows the accept rows as it shows the complete rows; before, the accept gave none of them.
    private const int AcceptJobItemsQuest = 50100720;
    private static readonly int[] AllJobAcceptItems = [41000041, 43000041];

    private static ItemMetadata[] AcceptItems() => AllJobAcceptItems.Select(id => Item(id, JobCode.None))
        .Concat(Jobs.Skip(1).Select((job, index) => Item(42000451 + index * 10, job))).ToArray();

    private static QuestMetadata AcceptJobItems() {
        QuestMetadata data = TestGameSession.QuestData(AcceptJobItemsQuest);
        return data with {
            AcceptReward = data.AcceptReward with {
                EssentialJobItem = AcceptItems().Select(item => new QuestMetadataReward.Item(item.Id, 3, 1)).ToList(),
            },
        };
    }

    [TestCase(Job.Knight, 42000451)]
    [TestCase(Job.Newbie, 42000451)]
    [TestCase(Job.Wizard, 42000471)]
    [TestCase(Job.SoulBinder, 42000551)]
    public void Accept_GivesTheJobRowsOfTheAcceptReward(Job job, int jobItem) {
        GameSession session = Login(job, items: AcceptItems());
        test.Register(AcceptJobItems());

        Assert.That(session.Quest.Start(AcceptJobItemsQuest), Is.EqualTo(QuestError.none));

        int[] given = AcceptItems().Select(item => item.Id).Where(id => Held(session, id) > 0).ToArray();
        Assert.Multiple(() => {
            Assert.That(given, Is.EqualTo(AllJobAcceptItems.Append(jobItem).ToArray()));
            Assert.That(session.Item.Inventory.Find(jobItem).Single().Rarity, Is.EqualTo(3));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // The job rows count for the bag too: they do not fit, so the quest is not started.
    [Test]
    public void Accept_WithAFullBagForTheJobRowsDoesNotStart() {
        GameSession session = Login(Job.Knight, fullBag: true, items: AcceptItems());
        test.Register(AcceptJobItems());

        Assert.Multiple(() => {
            Assert.That(session.Quest.Start(AcceptJobItemsQuest), Is.EqualTo(QuestError.s_quest_error_inventory_full));
            Assert.That(session.Quest.TryGetQuest(AcceptJobItemsQuest, out _), Is.False);
            Assert.That(AcceptItems().Sum(item => Held(session, item.Id)), Is.EqualTo(0));
        });
    }

    // Quest sub-type 33: an alliance quest of rank D takes 10 completion tickets (calcFameCompletionTicketCount) and
    // is completed without its conditions, with its reward. Without the tickets, nothing is taken or given.
    private const int TicketId = 20300001;
    private const int RewardId = 20000001;

    private Quest TicketQuest() {
        QuestMetadata data = TestGameSession.QuestData(91000001, QuestType.AllianceQuest);
        return test.StartQuest(data with {
            Basic = data.Basic with { Alliance = AllianceType.MapleAlliance, Rank = 4, CompletionTicket = true },
            CompleteReward = data.CompleteReward with { EssentialItem = [new QuestMetadataReward.Item(RewardId, 1, 1)] },
            Conditions = [new ConditionMetadata(ConditionType.jump, 1, null, null)],
        });
    }

    private static ItemMetadata Ticket() {
        ItemMetadata metadata = TestGameSession.Metadata(TicketId, slotMax: 999);
        return metadata with { Property = metadata.Property with { Tag = ItemTag.FameCompletionTicket } };
    }

    [TestCase(15, true, 5)]
    [TestCase(10, true, 0)]
    [TestCase(9, false, 9)]
    public void CompleteWithTickets_TakesTheRanksTickets(int tickets, bool completed, int left) {
        var prepared = new TestGameSession();
        prepared.Store(Ticket(), tickets);
        GameSession session = Login(Job.Knight, prepared: prepared, items: [Ticket(), TestGameSession.Metadata(RewardId)]);
        Quest quest = TicketQuest();

        Assert.Multiple(() => {
            Assert.That(session.Quest.CompleteWithTickets(quest.Id), Is.EqualTo(completed));
            Assert.That(quest.State, Is.EqualTo(completed ? QuestState.Completed : QuestState.Started));
            Assert.That(Held(session, TicketId), Is.EqualTo(left));
            Assert.That(Held(session, RewardId), Is.EqualTo(completed ? 1 : 0));
            Assert.That(test.Errors, Is.Empty);
        });
    }
}
