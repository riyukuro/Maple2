using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The MiniGameBonusReward game event: the reader of its two values, the events that name a game, and the winners'
// items at end_mini_game. Rows as the KR data has them (99000030, a reference=MAPLES2 private-server row: the oxquiz bonus, 40 of item 30001258).
public class MiniGameBonusReaderTests {
    private const string Items = """<items><v itemID="30001258" grade="1" count="40" /></items>""";

    [Test]
    public void Reader_NamesAndItems() {
        MiniGameBonusReward? bonus = EventXmlReader.MiniGameBonusReward("oxquiz", Items);
        Assert.That(bonus, Is.Not.Null);
        Assert.That(bonus!.GameNames, Is.EqualTo(new[] { "oxquiz" }));
        Assert.That(bonus.Items, Is.EqualTo(new[] { new RewardItem(30001258, 1, 40) }));
    }

    // 580001: two games in one row; several items, one without a grade (rank 1) and one without a count (skipped).
    [Test]
    public void Reader_TwoGamesSeveralItems() {
        MiniGameBonusReward? bonus = EventXmlReader.MiniGameBonusReward(" crazyrunner, oxquiz,,oxquiz",
            """<items><v itemID="30000610" grade="2" count="1" /><v itemID="30000611" count="3" /><v itemID="30000612" grade="1" /></items>""");
        Assert.That(bonus!.GameNames, Is.EqualTo(new[] { "crazyrunner", "oxquiz" }));
        Assert.That(bonus.Items, Is.EqualTo(new[] { new RewardItem(30000610, 2, 1), new RewardItem(30000611, 1, 3) }));
    }

    // No name, no item, or a value2 that is not an <items> document: nothing is stored.
    [TestCase("", Items)]
    [TestCase(" , ", Items)]
    [TestCase(null, Items)]
    [TestCase("oxquiz", "")]
    [TestCase("oxquiz", "<items />")]
    [TestCase("oxquiz", """<ms2><v itemID="30001258" grade="1" count="40" /></ms2>""")]
    [TestCase("oxquiz", "<items>")]
    public void Reader_NothingToGive(string? value1, string value2) {
        Assert.That(EventXmlReader.MiniGameBonusReward(value1, value2), Is.Null);
    }

    // The KR bonus rows end at "2099-04-20-23-59-59": event times to the second are read like those to the minute.
    [TestCase("2099-04-20-23-59-59", 2099, 4, 20, 23, 59, 59)]
    [TestCase("2023-03-20-00-00", 2023, 3, 20, 0, 0, 0)]
    public void EventTime_MinuteOrSecond(string text, int year, int month, int day, int hour, int minute, int second) {
        Assert.That(AttendGiftReplay.ParseTime(text), Is.EqualTo(new DateTime(year, month, day, hour, minute, second)));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("2099-04-20")]
    public void EventTime_Unreadable(string? text) {
        Assert.That(AttendGiftReplay.ParseTime(text), Is.EqualTo(DateTime.MinValue));
    }

    // The client has no class for it: the event list leaves it out.
    [Test]
    public void NotSentToClient() {
        var gameEvent = new GameEvent(new GameEventMetadata(1, GameEventType.MiniGameBonusReward, DateTime.MinValue, DateTime.MaxValue,
            TimeSpan.Zero, TimeSpan.Zero, [], new MiniGameBonusReward(["oxquiz"], [new RewardItem(1, 1, 1)]), "", "", "", ""));
        Assert.That(gameEvent.SentToClient, Is.False);
    }

    // The events naming the game, lowest id first, whatever the case; other games and other types are left out.
    [Test]
    public void For_EventsNamingTheGame() {
        GameEvent Event(int id, GameEventData data) => new(new GameEventMetadata(id, GameEventType.MiniGameBonusReward,
            DateTime.MinValue, DateTime.MaxValue, TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", ""));
        GameEvent high = Event(9, new MiniGameBonusReward(["crazyrunner", "oxquiz"], []));
        GameEvent low = Event(3, new MiniGameBonusReward(["OXQuiz"], []));
        GameEvent other = Event(5, new MiniGameBonusReward(["trapmaster"], []));
        GameEvent arcade = Event(1, new ArcadeOpen(3, 1, []));
        GameEvent[] events = [high, other, low, arcade];
        Assert.That(MiniGameBonus.For(events, "oxquiz"), Is.EqualTo(new[] { low, high }));
        Assert.That(MiniGameBonus.For(events, "trapmaster"), Is.EqualTo(new[] { other }));
        Assert.That(MiniGameBonus.For(events, "oxquiz_ugc"), Is.Empty);
        Assert.That(MiniGameBonus.For(events, ""), Is.Empty);
    }
}

// end_mini_game on a TestGameSession with a running bonus event.
public class MiniGameBonusSessionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int BonusItemId = 30001258;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Start(params string[] gameNames) {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(BonusItemId));
        test.AddEvent(99000030, GameEventType.MiniGameBonusReward, new MiniGameBonusReward(gameNames, [new RewardItem(BonusItemId, 1, 40)]));
        test.Start();
        test.TakeSent();
    }

    private TriggerContext Context() {
        var owner = (FieldTrigger) RuntimeHelpers.GetUninitializedObject(typeof(FieldTrigger));
        GC.SuppressFinalize(owner);
        typeof(FieldEntity<TriggerModel>).GetField("<Field>k__BackingField", Any)!.SetValue(owner, session.Field);
        typeof(FieldEntity<TriggerModel>).GetField("<Value>k__BackingField", Any)!.SetValue(owner, new TriggerModel(1, "main", Vector3.Zero, Vector3.Zero));
        // The test field has no map entities: an empty box list (winner box 0 needs none, the losers' check reads it).
        if (session.Field!.TriggerObjects == null) {
            var objects = (TriggerCollection) RuntimeHelpers.GetUninitializedObject(typeof(TriggerCollection));
            typeof(TriggerCollection).GetField(nameof(TriggerCollection.Boxes), Any)!.SetValue(objects, new Dictionary<int, TriggerBox>());
            typeof(FieldManager).GetField(nameof(FieldManager.TriggerObjects), Any)!.SetValue(session.Field, objects);
        }
        TriggerContext context = TriggerTestBase.UninitializedContext(owner);
        return context;
    }

    // The script's action as the trigger mapping reads it; winner box 0 is every player of the field.
    private void EndMiniGame(string gameName) {
        var document = new XmlDocument();
        document.LoadXml($"""<action name="end_mini_game" winner_box_id="0" game_name="{gameName}" />""");
        XmlElement node = document.DocumentElement!;
        TriggerFunctionMapping.ActionMap[node.GetAttribute("name")](node.Attributes).Execute(Context());
    }

    private void StartRecord() {
        session.MiniGameRecord = new MiniGameUserRecord(session.CharacterId) { TotalRounds = 1, ShowResult = true, MinRound = 1 };
    }

    private int Held() => session.Item.Inventory.Find(BonusItemId).Sum(item => item.Amount);

    // The result's item list: the count, then each item's id, count, rank and three bools (DungeonRewardInfo::Read).
    private static (int Count, int ItemId, int Amount, int Rank) ResultItems(byte[] packet) {
        var reader = new ByteReader(packet, packet.Length - 19);
        return (reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt());
    }

    [Test]
    public void End_WinnerGetsTheBonusInTheResult() {
        Start("crazyrunner", "oxquiz");
        StartRecord();

        EndMiniGame("oxquiz");

        Assert.That(Held(), Is.EqualTo(40));
        Assert.That(session.MiniGameRecord, Is.Null);
        byte[] result = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.DungeonReward);
        Assert.That(ResultItems(result), Is.EqualTo((1, BonusItemId, 40, 1)));
        Assert.That(test.Errors, Is.Empty);
    }

    // Another game, or a player whose record already ended (a second end_mini_game), gets nothing.
    [Test]
    public void End_OtherGameOrNoRecord() {
        Start("oxquiz");
        StartRecord();
        EndMiniGame("trapmaster");
        Assert.That(Held(), Is.EqualTo(0));
        Assert.That(session.MiniGameRecord, Is.Null);

        EndMiniGame("oxquiz");
        Assert.That(Held(), Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }
}
