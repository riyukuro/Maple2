using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The ArcadeOpen game event (client gameevent.cpp, ArcadeOpenContext): its value1 reader, its replay pools, the
// packet the client reads (the event id, then the arcade id), the lobby's enter of an event arcade and the
// event's reward at the end of a game. Made-up ids.
public class ArcadeEventTests {
    private const string Value1 = "<ms2><info arcadeId=\"4\" rewardCountPerDay=\"2\" /><reward itemID=\"30001437\" count=\"10\" grade=\"1\" />"
                                  + "<reward itemID=\"0\" count=\"1\" /><reward itemID=\"30000001\" count=\"2\" /></ms2>";

    [Test]
    public void Reader_InfoAndRewards() {
        ArcadeOpen? arcadeOpen = EventXmlReader.ArcadeOpen(Value1);
        Assert.That(arcadeOpen, Is.Not.Null);
        Assert.That((arcadeOpen!.ArcadeId, arcadeOpen.RewardCountPerDay), Is.EqualTo((4, 2)));
        // The row without an item id is skipped; a row without a grade keeps rank 1.
        Assert.That(arcadeOpen.Rewards, Is.EqualTo(new[] { new RewardItem(30001437, 1, 10), new RewardItem(30000001, 1, 2) }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("<ms2 />")]
    [TestCase("<ms2><info arcadeId=\"0\" rewardCountPerDay=\"2\" /></ms2>")]
    [TestCase("<ms2><info rewardCountPerDay=\"2\" /></ms2>")]
    [TestCase("<ms2><info arcadeId=")]
    public void Reader_NoArcade(string? value1) {
        Assert.That(EventXmlReader.ArcadeOpen(value1), Is.Null);
    }

    [Test]
    public void Reader_NegativeCountIsZero() {
        Assert.That(EventXmlReader.ArcadeOpen("<ms2><info arcadeId=\"3\" rewardCountPerDay=\"-1\" /></ms2>")!.RewardCountPerDay, Is.EqualTo(0));
    }

    private static GameEventMetadata Event(int id, GameEventType type, GameEventData? data) {
        return new GameEventMetadata(id, type, DateTime.UnixEpoch, DateTime.UnixEpoch.AddDays(1), TimeSpan.Zero, TimeSpan.Zero, [],
            data, "", "", "", "");
    }

    // One replay pool per arcade, in arcade id order; an event whose data was not read is left out. Another type
    // keeps one pool of all its events.
    [Test]
    public void Replay_OnePoolPerArcade() {
        GameEventMetadata[] events = [
            Event(900502, GameEventType.ArcadeOpen, new ArcadeOpen(5, 3, [])),
            Event(900500, GameEventType.ArcadeOpen, new ArcadeOpen(3, 3, [])),
            Event(900501, GameEventType.ArcadeOpen, new ArcadeOpen(4, 2, [])),
            Event(900503, GameEventType.ArcadeOpen, new ArcadeOpen(3, 1, [])),
            Event(900504, GameEventType.ArcadeOpen, null),
            Event(1, GameEventType.AttendGift, null),
        ];
        Assert.That(AttendGiftReplay.Types, Does.Contain(GameEventType.ArcadeOpen));
        Assert.That(AttendGiftReplay.Pools(events, GameEventType.ArcadeOpen).Select(pool => pool.Select(e => e.Id)),
            Is.EqualTo(new[] { new[] { 900500, 900503 }, new[] { 900501 }, new[] { 900502 } }));
        Assert.That(AttendGiftReplay.Pools(events, GameEventType.AttendGift).Single().Select(e => e.Id), Is.EqualTo(new[] { 1 }));
    }

    // A reward the client cannot show keeps the event from the replay.
    [Test]
    public void Replay_ClientRewards() {
        GameEventMetadata arcade = Event(900501, GameEventType.ArcadeOpen, new ArcadeOpen(4, 2, [new RewardItem(30001437, 1, 10)]));
        Assert.That(AttendGiftReplay.ClientHasRewards(arcade, _ => 1), Is.True);
        Assert.That(AttendGiftReplay.ClientHasRewards(arcade, _ => 0), Is.False);
    }

    // GameEventArcadeOpen::vfunc_4 reads the event id, then ArcadeOpenContext::Read the arcade id.
    [Test]
    public void Packet_NameIdArcade() {
        var writer = new ByteWriter();
        new GameEvent(Event(900501, GameEventType.ArcadeOpen, new ArcadeOpen(4, 2, [new RewardItem(30001437, 1, 10)]))).WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ArcadeOpen"));
        Assert.That(reader.ReadInt(), Is.EqualTo(900501));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // An event arcade opens with an event naming it; another arcade needs none. Of two events, the lower id.
    [Test]
    public void Open_AndCanEnter() {
        var three = new GameEvent(Event(900510, GameEventType.ArcadeOpen, new ArcadeOpen(3, 1, [])));
        var threeLow = new GameEvent(Event(900500, GameEventType.ArcadeOpen, new ArcadeOpen(3, 2, [])));
        var four = new GameEvent(Event(900501, GameEventType.ArcadeOpen, new ArcadeOpen(4, 2, [])));
        GameEvent[] events = [three, threeLow, four];
        Assert.That(ArcadeEvent.Open(events, 3), Is.SameAs(threeLow));
        Assert.That(ArcadeEvent.Open(events, 5), Is.Null);
        Assert.That(ArcadeEvent.CanEnter(new ArcadeGameTable.Entry(5, 5, 51000006, true), events), Is.False);
        Assert.That(ArcadeEvent.CanEnter(new ArcadeGameTable.Entry(4, 4, 51000005, true), events), Is.True);
        Assert.That(ArcadeEvent.CanEnter(new ArcadeGameTable.Entry(1, 1, 51000001, false), []), Is.True);
    }

    // Under the count of the day and with a round cleared; a game that cleared none gets nothing.
    [TestCase(0, 1, 2)]
    [TestCase(1, 3, 2)]
    [TestCase(2, 1, 0)]
    [TestCase(5, 1, 0)]
    [TestCase(0, 0, 0)]
    public void RewardsDue_UnderTheCountOfTheDay(int countToday, int roundsCleared, int expected) {
        var arcadeOpen = new ArcadeOpen(4, 2, [new RewardItem(1, 1, 1), new RewardItem(2, 1, 1)]);
        Assert.That(ArcadeEvent.RewardsDue(arcadeOpen, countToday, roundsCleared), Has.Length.EqualTo(expected));
    }
}

// The event arcade on a TestGameSession: the lobby's enter while the event runs, and the end's reward.
public class ArcadeEventSessionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int EventField = 51000005;
    private const int RewardItemId = 30001437;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Start(bool withEvent) {
        test = new TestGameSession();
        test.SetTable(new ArcadeGameTable(new Dictionary<int, ArcadeGameTable.Entry> {
            [4] = new(4, 4, EventField, true),
        }));
        var empty = new Dictionary<int, SeasonDataTable.Entry>();
        test.SetTable(new SeasonDataTable(empty, empty, empty, empty, empty, empty, empty, empty, empty));
        test.SetServerTable(new ArcadeRewardTable(new Dictionary<int, IReadOnlyDictionary<int, ArcadeRewardTable.Entry>>()));
        test.Register(TestGameSession.Metadata(RewardItemId));
        if (withEvent) {
            test.AddEvent(900501, GameEventType.ArcadeOpen, new ArcadeOpen(4, 2, [new RewardItem(RewardItemId, 1, 10)]));
        }
        test.Start();
        typeof(FieldManager).GetField("<GameStorage>k__BackingField", Any)!.SetValue(session.Field, test.Storage);
        test.TakeSent();
    }

    private void Enter(int arcadeId) {
        new PlayArcadeHandler { GameStorage = test.Storage }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(PlayArcadeHandler.Enter);
            writer.WriteInt(arcadeId);
        }));
    }

    // Without the event the enter is dropped; with it, the move reaches PrepareField (the test map is one the
    // played client lacks, so it stops at the map check with the move error).
    [TestCase(false)]
    [TestCase(true)]
    public void Enter_WhileTheEventRuns(bool withEvent) {
        Start(withEvent);
        MapMetadata map = TestGameSession.Map(EventField, notInPlayedClient: true);
        test.AddMap(map with { Limit = (MapMetadataLimit) RuntimeHelpers.GetUninitializedObject(typeof(MapMetadataLimit)) });

        Enter(4);

        Assert.That(test.TakeSentOps(), Is.EqualTo(withEvent ? new[] { SendOp.Notice, SendOp.RequestFieldEnter } : Array.Empty<SendOp>()));
        Assert.That(test.Errors, Is.Empty);
    }

    // A game of the given rounds won (the script's start_round, the pick, result_round and result_round2), then its end.
    private ByteReader EndGame(int wins = 1) {
        var game = new ThreeTwoOneGame(4, 1, 10000, [test.CharacterId]);
        for (int round = 1; round <= wins; round++) {
            game.StartRound(round);
            game.Pick(test.CharacterId, 1);
            game.ResultRound(2, [test.CharacterId]);
            game.ResultRound2();
        }
        Assert.That(game.Wins, Is.EqualTo(wins));
        session.Field!.ThreeTwoOne = game;
        session.Field.EndThreeTwoOne();
        List<byte[]> sent = test.TakeSent();
        byte[] result = sent.Last(packet => TestGameSession.Op(packet) == SendOp.PlayArcade);
        var reader = new ByteReader(result, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.PlayArcade));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        for (int i = 0; i < 5; i++) {
            reader.ReadInt();
        }
        return reader;
    }

    private int Held() => session.Item.Inventory.Find(RewardItemId).Sum(item => item.Amount);

    // Each end of a game gives the event's rows, listed in the result, up to its count of the day (2).
    [Test]
    public void End_GivesTheEventRewardsUpToTheCountOfTheDay() {
        Start(withEvent: true);
        for (int game = 1; game <= 3; game++) {
            ByteReader result = EndGame();
            byte count = result.ReadByte();
            Assert.That(count, Is.EqualTo(game <= 2 ? 1 : 0), $"game {game}");
            if (count == 1) {
                Assert.That(result.ReadInt(), Is.EqualTo(RewardItemId));
                Assert.That(result.ReadShort(), Is.EqualTo(1));
                Assert.That(result.ReadInt(), Is.EqualTo(10));
                result.ReadInt();
            }
            Assert.That(result.Available, Is.EqualTo(0));
            // The count is stored with the reward, before any logout.
            Assert.That(test.StoredEventValues(900501).GetValueOrDefault(GameEventUserValueType.ArcadeOpenRewardCount),
                Is.EqualTo(game <= 2 ? game.ToString() : "2"), $"game {game}");
        }
        Assert.That(Held(), Is.EqualTo(20));
        Assert.That(test.Errors, Is.Empty);
    }

    // A game with no round won (Three Two One lost from the first round, still at its init score) gives no event
    // reward and uses none of the day's count.
    [Test]
    public void End_NoWinNoReward() {
        Start(withEvent: true);
        Assert.That(EndGame(wins: 0).ReadByte(), Is.EqualTo(0));
        Assert.That(Held(), Is.EqualTo(0));
        Assert.That(ArcadeEvent.RewardCount(session, session.FindEvent(900501)!), Is.EqualTo(0));
        Assert.That(EndGame(wins: 1).ReadByte(), Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    // Without the event (an event arcade entered by a warp), the end gives no event reward.
    [Test]
    public void End_NoEventNoReward() {
        Start(withEvent: false);
        Assert.That(EndGame().ReadByte(), Is.EqualTo(0));
        Assert.That(Held(), Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }
}
