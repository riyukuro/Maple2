using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Validators;

namespace Maple2.Server.Tests.Game.Manager;

// The quest reward event (GameEventQuestAdditionalReward, client gameevent.cpp:3121): value1 the quest type, value2 the
// repeat type, value3 one rate for every kind or a reward row of rates per kind and per item, in ten-thousandths.
// The value3 forms are the KR rows'; quest ids are made up.
public class QuestRewardEventTests {
    private static int[] Rates(params (int Kind, int Rate)[] rates) {
        int[] all = new int[QuestRewardEventRules.Kinds.Length];
        foreach ((int kind, int rate) in rates) {
            all[kind] = rate;
        }
        return all;
    }

    // "10000": every kind doubled (QuestAdditionalRewardContext::Load, gameeventcontext.cpp:938, _wtoi of value3).
    [Test]
    public void Reader_OneRateForAll() {
        QuestAdditionalReward? reward = EventXmlReader.QuestAdditionalReward("6", "2", "10000");
        Assert.That(reward, Is.Not.Null);
        Assert.That((reward!.QuestType, reward.Repeat), Is.EqualTo((6, 2)));
        Assert.That(reward.Rates, Is.EqualTo(Enumerable.Repeat(10000, 8).ToArray()));
        Assert.That(reward.Items, Is.Empty);
        // _wtoi: leading blanks and the digits before the first other character.
        Assert.That(EventXmlReader.QuestAdditionalReward(" 9x", "2", " 20abc")!.Rates, Is.EqualTo(Enumerable.Repeat(20, 8).ToArray()));
        Assert.That(EventXmlReader.QuestAdditionalReward(" 9x", "2", " 20abc")!.QuestType, Is.EqualTo(9));
    }

    // The reward row: each kind its attribute, the item rows by id (a later row of the same id wins).
    [Test]
    public void Reader_RewardRow() {
        QuestAdditionalReward? reward = EventXmlReader.QuestAdditionalReward("6", "3",
            "<reward exp=\"0\" meso=\"0\" guild_fund=\"10000\" guild_exp=\"10000\" guild_coin=\"0\" lu=\"0\" karma=\"0\" menteeCoin=\"0\" />");
        Assert.That(reward!.Rates, Is.EqualTo(Rates((QuestRewardEventRules.GuildFund, 10000), (QuestRewardEventRules.GuildExp, 10000))));

        reward = EventXmlReader.QuestAdditionalReward("1", "2",
            "<reward exp=\"5000\" lu=\"3\" karma=\"4\" menteeCoin=\"7\" meso=\"-5\"><item id=\"30000925\" rate=\"1\" /><item id=\"30000925\" rate=\"10000\" />"
            + "<item id=\"0\" rate=\"10000\" /><item id=\"30000926\" rate=\"0\" /></reward>");
        Assert.That(reward!.Rates, Is.EqualTo(Rates((QuestRewardEventRules.Exp, 5000), (QuestRewardEventRules.Rue, 3),
            (QuestRewardEventRules.Treva, 4), (QuestRewardEventRules.MenteeCoin, 7))));
        Assert.That(reward.Items, Is.EqualTo(new[] { new QuestAdditionalReward.ItemRate(30000925, 10000) }));

        // Only an item rate.
        reward = EventXmlReader.QuestAdditionalReward("1", "2", "<reward exp=\"0\"><item id=\"30000925\" rate=\"10000\" /></reward>");
        Assert.That((reward!.Rates.Sum(), reward.Items.Length), Is.EqualTo((0, 1)));
    }

    // Nothing raised: no event data.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("0")]
    [TestCase("-100")]
    [TestCase("<reward exp=\"0\" meso=\"0\" />")]
    [TestCase("<reward exp=")]
    public void Reader_NothingRaised(string? value3) {
        Assert.That(EventXmlReader.QuestAdditionalReward("6", "2", value3), Is.Null);
    }

    // GameEventQuestAdditionalReward::vfunc_4: the event id, the quest type and the repeat type as shorts, then
    // QuestAdditionalRewardContext::Read (gameeventcontext.cpp:984): 8 int rates, the item count and each item's id
    // and rate.
    [Test]
    public void Packet_KeyRatesItems() {
        var data = new QuestAdditionalReward(6, 2, [1, 2, 3, 4, 5, 6, 7, 8], [new QuestAdditionalReward.ItemRate(30000925, 10000)]);
        var gameEvent = new GameEvent(new GameEventMetadata(90200002, GameEventType.QuestAdditionalReward, DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddDays(1), TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", ""));
        Assert.That(gameEvent.SentToClient, Is.True);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("QuestAdditionalReward"));
        Assert.That(reader.ReadInt(), Is.EqualTo(90200002));
        Assert.That((reader.ReadShort(), reader.ReadShort()), Is.EqualTo(((short) 6, (short) 2)));
        Assert.That(Enumerable.Range(0, 8).Select(_ => reader.ReadInt()), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.That((reader.ReadInt(), reader.ReadInt(), reader.ReadInt()), Is.EqualTo((1, 30000925, 10000)));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // The client keeps one event per key, the last one started.
    [Test]
    public void Pick_LatestStartOfTheKey() {
        GameEventMetadata Row(int id, int type, int repeat, int startDay, int rate) =>
            new(id, GameEventType.QuestAdditionalReward, DateTime.UnixEpoch.AddDays(startDay), DateTime.UnixEpoch.AddDays(30), TimeSpan.Zero,
                TimeSpan.Zero, [], new QuestAdditionalReward(type, repeat, Rates((0, rate)), []), "", "", "", "");
        GameEventMetadata[] rows = [Row(1, 6, 2, 2, 100), Row(2, 6, 2, 1, 200), Row(3, 6, 3, 5, 300), Row(4, 6, 2, 2, 400)];
        Assert.That(QuestRewardEventRules.Rate(QuestRewardEventRules.Pick(rows, QuestType.GuildQuest, 2), 0), Is.EqualTo(400));
        Assert.That(QuestRewardEventRules.Rate(QuestRewardEventRules.Pick(rows, QuestType.GuildQuest, 3), 0), Is.EqualTo(300));
        Assert.That(QuestRewardEventRules.Pick(rows, QuestType.GuildQuest, 0), Is.Null);
        Assert.That(QuestRewardEventRules.Pick(rows, QuestType.EpicQuest, 2), Is.Null);
    }

    // As the client's reward line: amount * (1 + rate / 10000) in float.
    [TestCase(1000, 0, 1000)]
    [TestCase(1000, 10000, 2000)]
    [TestCase(1000, 5000, 1500)]
    [TestCase(3, 3333, 3)]
    [TestCase(0, 10000, 0)]
    public void Raise_ByTheRate(long amount, int rate, long expected) {
        Assert.That(QuestRewardEventRules.Raise(amount, rate), Is.EqualTo(expected));
    }

    private const int ItemId = 20000031;
    private const int OtherItemId = 20000032;
    private const int JobItemId = 20000033;
    private TestGameSession test = null!;

    private GameSession Login(QuestAdditionalReward? data, DateTime? start = null, DateTime? end = null) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ItemId), TestGameSession.Metadata(OtherItemId), TestGameSession.Metadata(JobItemId), TestGameSession.Metadata(Constant.GuildCoinId));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetTable(new FameTable([], new Dictionary<int, AllianceType>()));
        test.SetTable(new ChangeJobTable(new Dictionary<Job, ChangeJobMetadata>()));
        test.SetTable(new ChapterBookTable(new Dictionary<int, ChapterBookTable.Entry>()));
        if (data != null) {
            test.AddEvent(90200003, GameEventType.QuestAdditionalReward, data, start, end);
        }
        GameSession session = test.Start();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { LuTokenMax = 30000, KarmaTokenMax = 30000 });
        test.TakeSent();
        return session;
    }

    private record Paid(long Exp, long Meso, long Rue, long Treva, long MenteeCoin, int Items, int OtherItems, int JobItems, int GuildCoins);

    private Paid Complete(GameSession session, int repeat = 2) {
        QuestMetadata data = TestGameSession.QuestData(90000031, QuestType.WorldQuest);
        Quest quest = test.StartQuest(data with {
            Basic = data.Basic with { Repeat = repeat, StandardLevel = 60 },
            CompleteReward = data.CompleteReward with {
                Exp = 1000, Meso = 1000, Rue = 10, Treva = 20, MenteeCoin = 30, GuildCoin = 4,
                EssentialItem = [new QuestMetadataReward.Item(ItemId, 1, 3), new QuestMetadataReward.Item(OtherItemId, 1, 3)],
                EssentialJobItem = [new QuestMetadataReward.Item(JobItemId, 1, 5)],
            },
        });
        long exp = session.Player.Value.Character.Exp, meso = session.Currency.Meso, rue = session.Currency[CurrencyType.Rue],
            treva = session.Currency[CurrencyType.Treva], mentee = session.Currency[CurrencyType.MenteeToken];
        Assert.That(session.Quest.Complete(quest), Is.True);
        Assert.That(test.Errors, Is.Empty);
        return new Paid(session.Player.Value.Character.Exp - exp, session.Currency.Meso - meso, session.Currency[CurrencyType.Rue] - rue,
            session.Currency[CurrencyType.Treva] - treva, session.Currency[CurrencyType.MenteeToken] - mentee,
            session.Item.Inventory.Find(ItemId).Sum(item => item.Amount), session.Item.Inventory.Find(OtherItemId).Sum(item => item.Amount),
            session.Item.Inventory.Find(JobItemId).Sum(item => item.Amount),
            session.Item.Inventory.Find(Constant.GuildCoinId).Sum(item => item.Amount));
    }

    // A world quest (1, 2) while an event of its key runs: each kind raised by its rate, the item it names and the guild
    // coin too, also an item of the job rows.
    [Test]
    public void Complete_RaisesEachKind() {
        Paid plain = Complete(Login(null));
        var data = new QuestAdditionalReward(1, 2, Rates((QuestRewardEventRules.Exp, 5000), (QuestRewardEventRules.Meso, 10000),
            (QuestRewardEventRules.Rue, 10000), (QuestRewardEventRules.Treva, 5000), (QuestRewardEventRules.MenteeCoin, 10000),
            (QuestRewardEventRules.GuildCoin, 5000)),
            [new QuestAdditionalReward.ItemRate(ItemId, 10000), new QuestAdditionalReward.ItemRate(JobItemId, 5000)]);
        Paid raised = Complete(Login(data));
        Assert.That(plain.Meso, Is.GreaterThan(0));
        Assert.That(raised, Is.EqualTo(new Paid(plain.Exp * 3 / 2, plain.Meso * 2, plain.Rue * 2, plain.Treva * 3 / 2, plain.MenteeCoin * 2,
            plain.Items * 2, plain.OtherItems, 7, plain.GuildCoins * 3 / 2)));
        Assert.That(plain, Is.EqualTo(new Paid(1000, plain.Meso, 10, 20, 30, 3, 3, 5, 4)));
    }

    // Another quest type or repeat type, or an event that ended: nothing raised.
    [Test]
    public void Complete_OtherKeyOrEndedPaysThePlainReward() {
        Paid plain = Complete(Login(null));
        int[] all = Enumerable.Repeat(10000, 8).ToArray();
        Assert.That(Complete(Login(new QuestAdditionalReward(1, 3, all, []))), Is.EqualTo(plain), "repeat type");
        Assert.That(Complete(Login(new QuestAdditionalReward(6, 2, all, []))), Is.EqualTo(plain), "quest type");
        Assert.That(Complete(Login(new QuestAdditionalReward(1, 2, all, []), DateTime.Now.AddDays(-5), DateTime.Now.AddDays(-1))),
            Is.EqualTo(plain), "ended");
        Assert.That(Complete(Login(new QuestAdditionalReward(1, 2, all, []))).Exp, Is.EqualTo(2000), "running");
    }

    // The event's share, the quest exp rate's and a type 7 effect's (110001021, 4 times more for a daily quest) each come
    // from the same exp and add: 1000 + 500 + 300 + 4000.
    [Test]
    public void Complete_EventAddsToTheOtherExpBonuses() {
        GameSession session = Login(new QuestAdditionalReward(1, 2, Rates((QuestRewardEventRules.Exp, 5000)), []));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { QuestRewardSAIgnoreLevel = 10 });
        session.Player.Stats.Values[SpecialAttribute.QuestExp].AddTotal(300);
        SpecialEffectWiringTests.Hold(session.Player, SpecialEffectRulesTests.QuestExp);
        Assert.That(Complete(session).Exp, Is.EqualTo(5800L));
    }
}
