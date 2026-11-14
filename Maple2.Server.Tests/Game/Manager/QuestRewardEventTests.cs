using System;
using System.Linq;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

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
}
