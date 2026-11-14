using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Game;

// A completed guild quest (QuestType.GuildQuest) is worth the contribution row of its kind to the member: basic
// repeatable 2 (again on another day) a dailyQuest, 3 (again from the week's reset day) a weeklyQuest. World reads
// the row and tells the guild (Guild 53). Made-up quest id and values.
public class GuildQuestContributionTests {
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int QuestId = 90000011;
    private const long GuildId = 77;

    private sealed class GuildWorld : WorldClient {
        public readonly ConcurrentQueue<GuildRequest> Requests = new();
        public readonly SemaphoreSlim Received = new(0);

        public override GuildResponse Guild(GuildRequest request, CallOptions options) {
            Requests.Enqueue(request);
            Received.Release();
            return new GuildResponse();
        }

        // The account lock the completion's save takes.
        public override LockResponse AcquireLock(LockRequest request, CallOptions options) => new();
        public override LockResponse ReleaseLock(LockRequest request, CallOptions options) => new();
    }

    private TestGameSession test = null!;
    private GuildWorld world = null!;

    private static void Set<T>(object target, string field, object? value) {
        (typeof(T).GetField(field, Flags) ?? throw new MissingFieldException(typeof(T).Name, field)).SetValue(target, value);
    }

    private GameSession Start(bool inGuild = true, Action<TestGameSession>? before = null) {
        test = new TestGameSession();
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        test.SetTable(new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property>(),
            Contributions: new Dictionary<string, int> {
                [GuildContributionKind.DailyQuest] = 5,
                [GuildContributionKind.WeeklyQuest] = 20,
            }));
        before?.Invoke(test);

        GameSession session = test.Start();
        world = new GuildWorld();
        Set<GameSession>(session, "<World>k__BackingField", world);

        var manager = (GuildManager) RuntimeHelpers.GetUninitializedObject(typeof(GuildManager));
        GC.SuppressFinalize(manager);
        Set<GuildManager>(manager, "session", session);
        Set<GuildManager>(manager, "logger", new LoggerConfiguration().CreateLogger());
        if (inGuild) {
            var guild = new Guild(GuildId, "Quest", test.AccountId, test.CharacterId, "Master");
            var info = new PlayerInfo(new CharacterInfo(test.AccountId, test.CharacterId, "Master", "", "", Gender.Male, Job.Newbie, 20), "",
                new AchievementInfo(), []);
            guild.Members[test.CharacterId] = new GuildMember { GuildId = GuildId, Info = info };
            Set<GuildManager>(manager, "<Guild>k__BackingField", guild);
        }
        session.Guild = manager;
        test.TakeSent();
        return session;
    }

    private Quest Started(QuestType type, int repeat) {
        QuestMetadata data = TestGameSession.QuestData(QuestId, type);
        Quest quest = test.StartQuest(data with {
            Basic = data.Basic with { Repeat = repeat },
            Conditions = [new ConditionMetadata(ConditionType.jump, 1, null, null)],
        });
        quest.Conditions[0].Counter = 1;
        return quest;
    }

    [TestCase(2, GuildContributionKind.DailyQuest)]
    [TestCase(3, GuildContributionKind.WeeklyQuest)]
    public void GuildQuest_SendsItsKindToWorld(int repeat, string kind) {
        GameSession session = Start();
        Quest quest = Started(QuestType.GuildQuest, repeat);

        Assert.That(session.Quest.Complete(quest), Is.True);

        Assert.That(world.Received.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(world.Requests.TryDequeue(out GuildRequest? request), Is.True);
        Assert.That(request!.RequestorId, Is.EqualTo(test.CharacterId));
        Assert.That((request.AddContribution.GuildId, request.AddContribution.Kind), Is.EqualTo((GuildId, kind)));
        Assert.That(world.Requests, Is.Empty);
    }

    // The guild's exp and fund of the completeReward (KR guild daily quest 73000001: guildExp 120, guildFund 20000)
    // go to World for the guild before the quest's contribution row; without the guild's quest reward factors no
    // bonus is added.
    [Test]
    public void GuildQuest_SendsTheGuildExpAndFund() {
        GameSession session = Start();
        QuestMetadata data = TestGameSession.QuestData(73000001, QuestType.GuildQuest);
        Quest quest = test.StartQuest(data with {
            Basic = data.Basic with { Repeat = 2 },
            CompleteReward = data.CompleteReward with { GuildExp = 120, GuildFund = 20000 },
            Conditions = [new ConditionMetadata(ConditionType.jump, 1, null, null)],
        });
        quest.Conditions[0].Counter = 1;

        Assert.That(session.Quest.Complete(quest), Is.True);

        // World is told on another thread: each request is waited for before it is taken.
        Assert.That(world.Received.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(world.Requests.TryDequeue(out GuildRequest? first), Is.True);
        Assert.That((first!.RequestorId, first.AddExpFund?.GuildId, first.AddExpFund?.Exp, first.AddExpFund?.Fund),
            Is.EqualTo((test.CharacterId, (long?) GuildId, (int?) 120, (int?) 20000)));
        Assert.That(world.Received.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(world.Requests.TryDequeue(out GuildRequest? second), Is.True);
        Assert.That(second!.AddContribution?.Kind, Is.EqualTo(GuildContributionKind.DailyQuest));
        Assert.That(world.Requests, Is.Empty);
    }

    // The quest reward event of guild daily quests (KR row guild_fund 10000, guild_exp 10000 for (6, 2)) doubles the
    // guild's exp and fund the completion sends; the event of guild weekly quests (6, 3) does not.
    [TestCase(2, 240, 40000)]
    [TestCase(3, 120, 20000)]
    public void GuildQuest_RewardEventRaisesTheGuildExpAndFund(int eventRepeat, int exp, int fund) {
        int[] rates = new int[8];
        rates[QuestRewardEventRules.GuildFund] = 10000;
        rates[QuestRewardEventRules.GuildExp] = 10000;
        GameSession session = Start(before: prepared => prepared.AddEvent(90200001, GameEventType.QuestAdditionalReward,
            new QuestAdditionalReward((int) QuestType.GuildQuest, eventRepeat, rates, [])));
        QuestMetadata data = TestGameSession.QuestData(73000001, QuestType.GuildQuest);
        Quest quest = test.StartQuest(data with {
            Basic = data.Basic with { Repeat = 2 },
            CompleteReward = data.CompleteReward with { GuildExp = 120, GuildFund = 20000 },
            Conditions = [new ConditionMetadata(ConditionType.jump, 1, null, null)],
        });
        quest.Conditions[0].Counter = 1;

        Assert.That(session.Quest.Complete(quest), Is.True);

        Assert.That(world.Received.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(world.Requests.TryDequeue(out GuildRequest? first), Is.True);
        Assert.That((first!.AddExpFund?.Exp, first.AddExpFund?.Fund), Is.EqualTo(((int?) exp, (int?) fund)));
    }

    // A guild quest done once, another quest type of a daily repeat, or a player without a guild: nothing is sent.
    [TestCase(QuestType.GuildQuest, 0, true)]
    [TestCase(QuestType.EpicQuest, 2, true)]
    [TestCase(QuestType.GuildQuest, 2, false)]
    public void OtherQuests_SendNothing(QuestType type, int repeat, bool inGuild) {
        GameSession session = Start(inGuild);
        Quest quest = Started(type, repeat);

        Assert.That(session.Quest.Complete(quest), Is.True);

        Assert.That(world.Received.Wait(TimeSpan.FromMilliseconds(300)), Is.False);
        Assert.That(world.Requests, Is.Empty);
    }
}
