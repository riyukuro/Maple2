using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The snowman event (Snowman): the sources' conditions give snowflakes (user value 1700) to the snowmen that can
// proceed, EventReward 12 takes a snowman's daily reward at its goal (1701 today, 1702 in all), 13 its accum reward
// once (1703); all are kept for the account.
public class EventSnowmanTests {
    private const int EventId = 900070;
    private const int DailyItem = 20302321;
    private const int AccumItem = 20302322;

    private TestGameSession test = null!;
    private GameEvent gameEvent = null!;

    private static Snowman Data(params Snowman.Source[] sources) => new("ex", [
        new Snowman.Entry(10, 2, 3, [new RewardItem(DailyItem, 1, 2)], [new RewardItem(AccumItem, 1, 1)]),
        new Snowman.Entry(30, 1, 1, [new RewardItem(DailyItem, 1, 5)], [new RewardItem(AccumItem, 1, 3)]),
    ], "layout", sources);

    private GameSession Login(params Snowman.Source[] sources) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(DailyItem), TestGameSession.Metadata(AccumItem));
        gameEvent = test.AddEvent(EventId, GameEventType.Snowman,
            Data(sources.Length == 0 ? [new Snowman.Source(ConditionType.npc, 4, 10000)] : sources));
        GameSession session = test.Start();
        session.GameEvent.Roll = _ => 0;
        test.TakeSent();
        return session;
    }

    private static void Claim(GameSession session, bool accum, byte index) =>
        new EventRewardHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte((byte) (accum ? 13 : 12));
            writer.WriteByte(index);
        }));

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private List<string> SentHex() => test.TakeSent().Select(Convert.ToHexString).ToList();

    private static int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    private static string? Value(GameSession session, GameEventUserValueType type) => session.GameEvent.Peek(type, EventId);

    private Dictionary<GameEventUserValueType, string> StoredAccountValues() {
        using var db = test.Storage.Context();
        return db.GetAccountEventValues(test.AccountId).Where(value => value.EventId == EventId)
            .ToDictionary(value => value.Type, value => value.Value);
    }

    private static void Kill(GameSession session, int times) {
        for (int i = 0; i < times; i++) {
            session.ConditionUpdate(ConditionType.npc);
        }
    }

    [Test]
    public void Snowman_Condition_GivesSnowflakesUpToTheGoal() {
        GameSession session = Login();

        Kill(session, 3);

        // 4 a kill: the first snowman stops at its goal 10, the second goes on.
        Assert.That(Value(session, GameEventUserValueType.SnowflakeCount), Is.EqualTo("10,12"));
        Assert.That(SentHex().Count(hex => hex == Hex(TaskEventPacket.Snowflake())), Is.EqualTo(3));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Snowman_OtherConditionOrFailedRoll_GivesNothing() {
        GameSession session = Login(new Snowman.Source(ConditionType.npc, 4, 5000));
        session.GameEvent.Roll = _ => 5000;

        Kill(session, 1);
        session.ConditionUpdate(ConditionType.fish);

        Assert.That(Value(session, GameEventUserValueType.SnowflakeCount), Is.Null);
        Assert.That(SentHex(), Does.Not.Contain(Hex(TaskEventPacket.Snowflake())));
    }

    // The sources of one condition add up; a snowman that cannot proceed takes none.
    [Test]
    public void Snowman_Gain_Rules() {
        Snowman snowman = Data();
        int[] snowflakes = [10, 0];
        int[] daily = [0, 1];
        Assert.That(SnowmanRules.Gain(snowman, snowflakes, daily, 5), Is.False);
        Assert.That(snowflakes, Is.EqualTo(new[] { 10, 0 }));
        snowflakes = [3, 29];
        daily = [0, 0];
        Assert.That(SnowmanRules.Gain(snowman, snowflakes, daily, 5), Is.True);
        Assert.That(snowflakes, Is.EqualTo(new[] { 8, 30 }));
        Assert.Multiple(() => {
            Assert.That(SnowmanRules.Counts("4,x", 3), Is.EqualTo(new[] { 4, 0, 0 }));
            Assert.That(SnowmanRules.Counts(null, 2), Is.EqualTo(new[] { 0, 0 }));
            Assert.That(SnowmanRules.Indexes("2,0"), Is.EquivalentTo(new[] { 0, 2 }));
            Assert.That(SnowmanRules.Indexes(""), Is.Empty);
        });
    }

    [Test]
    public void Snowman_DailyClaim_GivesTheRewardAndStartsAgain() {
        GameSession session = Login();
        Kill(session, 3);
        test.TakeSent();

        Claim(session, false, 0);

        Assert.That(Held(session, DailyItem), Is.EqualTo(2));
        Assert.That(Value(session, GameEventUserValueType.SnowflakeCount), Is.EqualTo("0,12"));
        Assert.That(Value(session, GameEventUserValueType.DailyCompleteCount), Is.EqualTo("1,0"));
        Assert.That(Value(session, GameEventUserValueType.AccumCompleteCount), Is.EqualTo("1,0"));
        Assert.That(SentHex()[^1], Is.EqualTo(Hex(EventRewardPacket.ClaimSnowmanReward(false, [new RewardItem(DailyItem, 1, 2)]))));
        // Stored with the reward, for the account.
        Dictionary<GameEventUserValueType, string> stored = StoredAccountValues();
        Assert.That(stored.GetValueOrDefault(GameEventUserValueType.AccumCompleteCount), Is.EqualTo("1,0"));
        Assert.That(stored.GetValueOrDefault(GameEventUserValueType.DailyCompleteCount), Is.EqualTo("1,0"));
        Assert.That(test.StoredEventValues(EventId), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Snowman_DailyClaim_BelowTheGoalOrOverTheLimit_IsRefused() {
        GameSession session = Login();
        Kill(session, 1);
        test.TakeSent();

        Claim(session, false, 0);
        Claim(session, false, 5);
        Assert.That(SentHex(), Is.Empty);
        Assert.That(Held(session, DailyItem), Is.EqualTo(0));

        // Two completes a day for the first snowman: the third gets nothing and no snowflakes.
        for (int i = 0; i < 2; i++) {
            Kill(session, 3);
            Claim(session, false, 0);
        }
        test.TakeSent();
        Kill(session, 3);
        Assert.That(Value(session, GameEventUserValueType.SnowflakeCount), Does.StartWith("0,"));
        Claim(session, false, 0);
        Assert.That(Held(session, DailyItem), Is.EqualTo(4));
        Assert.That(Value(session, GameEventUserValueType.DailyCompleteCount), Is.EqualTo("2,0"));
    }

    // The next day the snowflakes and today's completes are gone; all completes stay.
    [Test]
    public void Snowman_NextDay_ResetsTheDayOnly() {
        GameSession session = Login();
        DateTime now = DateTime.Now;
        session.GameEvent.Clock = () => now;
        Kill(session, 3);
        Claim(session, false, 0);
        Kill(session, 3);
        Claim(session, false, 0);
        session.GameEvent.Clock = () => now.AddDays(1);

        Kill(session, 1);

        Assert.That(Value(session, GameEventUserValueType.SnowflakeCount), Is.EqualTo("4,4"));
        Assert.That(Value(session, GameEventUserValueType.DailyCompleteCount), Is.Null.Or.Empty);
        Assert.That(Value(session, GameEventUserValueType.AccumCompleteCount), Is.EqualTo("2,0"));
    }

    [Test]
    public void Snowman_AccumClaim_OnceAtTheAccumCount() {
        GameSession session = Login();
        DateTime now = DateTime.Now;
        for (int day = 0; day < 2; day++) {
            DateTime time = now.AddDays(day);
            session.GameEvent.Clock = () => time;
            for (int i = 0; i < 2; i++) {
                Kill(session, 3);
                Claim(session, false, 0);
            }
        }
        test.TakeSent();
        Assert.That(Value(session, GameEventUserValueType.AccumCompleteCount), Is.EqualTo("4,0"));

        Claim(session, true, 1);
        Assert.That(SentHex(), Is.Empty);

        Claim(session, true, 0);
        Assert.That(Held(session, AccumItem), Is.EqualTo(1));
        Assert.That(Value(session, GameEventUserValueType.AccumCompleteRewardReceived), Is.EqualTo("0"));
        Assert.That(SentHex()[^1], Is.EqualTo(Hex(EventRewardPacket.ClaimSnowmanReward(true, [new RewardItem(AccumItem, 1, 1)]))));
        Assert.That(StoredAccountValues().GetValueOrDefault(GameEventUserValueType.AccumCompleteRewardReceived), Is.EqualTo("0"));

        Claim(session, true, 0);
        Assert.That(SentHex(), Is.Empty);
        Assert.That(Held(session, AccumItem), Is.EqualTo(1));
    }

    // uisnowmaneventdialog.cpp: 12 or 13, an int count, then the item id and a short count each.
    [Test]
    public void Snowman_ReplyBytes() {
        Assert.That(Hex(EventRewardPacket.ClaimSnowmanReward(false, [new RewardItem(DailyItem, 1, 2)]))[4..],
            Is.EqualTo("0C" + "01000000" + "F1C93501" + "0200"));
        Assert.That(Hex(EventRewardPacket.ClaimSnowmanReward(true, []))[4..], Is.EqualTo("0D" + "00000000"));
        // TaskEvent: 4 (the snowman), no id, rank or count, the notice.
        Assert.That(Hex(TaskEventPacket.Snowflake())[4..], Is.EqualTo("04" + "00000000" + "0000" + "00000000" + "01"));
    }

    // SnowmanContext::Read after the id: the event id, thema, snowmen, the end; then the layout.
    [Test]
    public void Snowman_EventBytes() {
        var metadata = new GameEventMetadata(EventId, GameEventType.Snowman, DateTime.Now, DateTime.Now.AddDays(1),
            TimeSpan.Zero, TimeSpan.Zero, [], new Snowman("t", [
                new Snowman.Entry(10, 2, 3, [new RewardItem(DailyItem, 1, 2)], []),
            ], "l", []), "", "", "", "");
        var gameEvent = new GameEvent(metadata);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        string hex = Convert.ToHexString(writer.ToArray());
        string end = Convert.ToHexString(BitConverter.GetBytes(gameEvent.EndTime));
        Assert.That(hex, Does.EndWith("E6BB0D00" + "E6BB0D00" + "01007400" + "0100"
            + "0A000000" + "02000000" + "03000000"
            + "0100" + "F1C93501" + "0100" + "02000000" + "00000000" + "0000"
            + end + "01006C00"));
    }

    [Test]
    public void Snowman_IngestReadsTheSnowmen() {
        Snowman? snowman = ServerTableMapper.ReadSnowman(
            "<ms2><option thema=\"ex\"/><snowman><condition snowflakeCount=\"300\" dailyMaxCount=\"1\" accumMaxCount=\"7\"/>"
            + "<rewardItems><v itemID=\"20302321\" grade=\"1\" count=\"2\"/></rewardItems>"
            + "<accumRewardItems><v itemID=\"20302322\" grade=\"3\" count=\"1\"/></accumRewardItems></snowman></ms2>", "lay");
        Assert.That(snowman, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(snowman!.Theme, Is.EqualTo("ex"));
            Assert.That(snowman.Layout, Is.EqualTo("lay"));
            Assert.That(snowman.Snowmen, Has.Length.EqualTo(1));
            Snowman.Entry entry = snowman.Snowmen[0];
            Assert.That((entry.Goal, entry.DailyMax, entry.AccumMax), Is.EqualTo((300, 1, 7)));
            Assert.That(entry.Rewards, Is.EqualTo(new[] { new RewardItem(DailyItem, 1, 2) }));
            Assert.That(entry.AccumRewards, Is.EqualTo(new[] { new RewardItem(AccumItem, 3, 1) }));
            // No option: no thema.
            Assert.That(ServerTableMapper.ReadSnowman("<ms2><snowman><condition/><rewardItems/><accumRewardItems/></snowman></ms2>", "")?.Theme,
                Is.EqualTo(""));
            Assert.That(ServerTableMapper.ReadSnowman("<ms2><snowman><condition/><rewardItems/></snowman></ms2>", ""), Is.Null);
            Assert.That(ServerTableMapper.ReadSnowman("<ms2/>", ""), Is.Null);
            Assert.That(ServerTableMapper.ReadSnowman("", ""), Is.Null);
        });
    }

    [Test]
    public void Snowman_IngestReadsTheSources() {
        var document = new XmlDocument();
        document.LoadXml("<ms2>"
            + Row("npc", 1, 250, "2017-12-01-00-00-00")
            + Row("npc", 1, 250, "2017-12-01-00-00-00")
            + Row("dungeon_clear", 10, 20000, "2017-12-01-00-00-00")
            + Row("playinstrument_time", 1, 100, "2017-12-01-00-00-00")
            + Row("nosuchcondition", 1, 100, "2017-12-01-00-00-00")
            + Row("fish", 0, 100, "2017-12-01-00-00-00")
            + Row("fish", 1, 100, "2016-12-01-00-00-00")
            + "<promotion promotionType=\"Other\" type=\"npc\" point=\"1\" prop=\"1\" startTime=\"2017-12-01-00-00-00\" endTime=\"2018-01-01-00-00-00\"/>"
            + "</ms2>");

        Snowman.Source[] sources = ServerTableMapper.MapSnowmanSources(document, "2017-12-01-00-00-00", "2018-01-01-00-00-00", _ => true);

        Assert.That(sources, Is.EqualTo(new[] {
            new Snowman.Source(ConditionType.npc, 1, 250),
            new Snowman.Source(ConditionType.dungeon_clear, 10, 10000),
        }));
        Assert.That(ServerTableMapper.MapSnowmanSources(document, "2017-12-01-00-00-00", "2018-01-01-00-00-00", _ => false), Is.Empty);
    }

    private static string Row(string type, int point, int prop, string start) =>
        $"<promotion promotionType=\"Snowman\" type=\"{type}\" point=\"{point}\" prop=\"{prop}\" startTime=\"{start}\" endTime=\"2018-01-01-00-00-00\"/>";
}
