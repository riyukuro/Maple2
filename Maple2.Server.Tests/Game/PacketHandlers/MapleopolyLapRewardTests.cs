using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Database.Context;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The lap rewards of the Mapleopoly board (BlueMarble value1 rounds): the reward of every lap (round 0) and the
// reward of a lap count, each given at the row's count.
public class MapleopolyLapRewardTests {
    private const int EventId = 7101;
    private const int EveryLap = 20000001;
    private const int FirstLap = 20000002;
    private const int Ticket = 20000003;

    private static int Held(GameSession session, int itemId) => session.Item.Inventory.Find(itemId).Sum(item => item.Amount);

    // A two-tile board: the start and a forward tile of 1. The token on the forward tile (position 1) walks to 2,
    // which is the first lap.
    [Test]
    public void Mapleopoly_LapReward_IsGivenAtItsCount() {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(EveryLap), TestGameSession.Metadata(FirstLap), TestGameSession.Metadata(Ticket));
        var none = new ItemComponent(0, 0, 0, ItemTag.None);
        var board = new BlueMarble(new ItemComponent(Ticket, -1, 1, ItemTag.None), [
            new BlueMarble.Round(0, new ItemComponent(EveryLap, -1, 2, ItemTag.None)),
            new BlueMarble.Round(1, new ItemComponent(FirstLap, -1, 3, ItemTag.None)),
        ], [
            new BlueMarble.Slot(BlueMarbleSlotType.Start, 0, none),
            new BlueMarble.Slot(BlueMarbleSlotType.Forward, 1, none),
        ]);
        GameEvent gameEvent = test.AddEvent(EventId, GameEventType.BlueMarble, board);
        GameSession session = test.Start();
        session.GameEvent.Get(GameEventUserValueType.MapleopolyTotalSlotCount, gameEvent.Id, gameEvent.EndTime);
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.MapleopolyTotalSlotCount, 1);
        session.GameEvent.Mapleopoly.Rolled();

        new MapleopolyHandler { ItemMetadata = null! }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(Held(session, EveryLap), Is.EqualTo(2));
        Assert.That(Held(session, FirstLap), Is.EqualTo(3));
        Assert.That(session.GameEvent.Peek(GameEventUserValueType.MapleopolyTotalTrips, EventId), Is.EqualTo("1"));
        // Stored with the lap reward, not at logout: a mailed lap reward must not be given again after a restart.
        using (var context = new Ms2Context(test.Options)) {
            Assert.That(context.GameEventUserValue
                .Where(value => value.CharacterId == test.CharacterId && value.EventId == EventId && value.Type == GameEventUserValueType.MapleopolyTotalTrips)
                .Select(value => value.Value).FirstOrDefault(), Is.EqualTo("1"));
        }
        Assert.That(test.Errors, Is.Empty);
    }

    // The time run's value2: the step rows at their distances and the final row (the CN row has both, KR only the final).
    [Test]
    public void TimeRun_IngestReadsTheStepRewards() {
        var xml = new XmlDocument();
        xml.LoadXml("<ms><reward distance=\"20\" itemID=\"59200323\" grade=\"1\" count=\"1\" /><reward distance=\"80\" itemID=\"40400068\" grade=\"3\" count=\"2\" /><final itemID=\"20301043\" grade=\"1\" count=\"1\" /></ms>");

        (Dictionary<int, RewardItem> steps, RewardItem? final) = ServerTableMapper.ReadTimeRunRewards(xml.FirstChild!);

        Assert.That(steps, Is.EquivalentTo(new Dictionary<int, RewardItem> {
            [20] = new RewardItem(59200323, 1, 1),
            [80] = new RewardItem(40400068, 3, 2),
        }));
        Assert.That(final, Is.EqualTo(new RewardItem(20301043, 1, 1)));
    }

    // The KR rows write the amount as count (<v round="5" itemID="20303095" count="10" />) and no rank.
    [Test]
    public void Mapleopoly_IngestReadsTheRoundCount() {
        var xml = new XmlDocument();
        xml.LoadXml("<round consumeItemID=\"30001179\" consumeItemCount=\"1\"><v round=\"5\" itemID=\"20303095\" count=\"10\" /><v itemID=\"20302686\" /></round>");
        XmlNode root = xml.FirstChild!;

        BlueMarble.Round fifth = ServerTableMapper.ParseBlueMarbleRound(root.ChildNodes[0]!);
        Assert.That(fifth, Is.EqualTo(new BlueMarble.Round(5, new ItemComponent(20303095, -1, 10, ItemTag.None))));

        BlueMarble.Round everyLap = ServerTableMapper.ParseBlueMarbleRound(root.ChildNodes[1]!);
        Assert.That(everyLap, Is.EqualTo(new BlueMarble.Round(0, new ItemComponent(20302686, -1, 1, ItemTag.None))));
    }

    // Every KR and NA board starts with <v type="starting" />. Read as the start tile, a roll that ends on it (a lap
    // walked to its end) is answered with its result (kind 6) and the lap reward; read as an item tile of item 0,
    // the result request was dropped and the client kept its dice button off.
    [Test]
    public void Mapleopoly_StartingTile_IsTheStartTile() {
        var xml = new XmlDocument();
        xml.LoadXml("<slots><v type=\"starting\" /><v type=\"forward\" arg1=\"1\" /></slots>");
        BlueMarble.Slot[] slots = xml.FirstChild!.ChildNodes.Cast<XmlNode>().Select(ServerTableMapper.ParseBlueMarbleSlot).ToArray();
        Assert.That(slots[0].Type, Is.EqualTo(BlueMarbleSlotType.Start));
        Assert.That(slots[1], Is.EqualTo(new BlueMarble.Slot(BlueMarbleSlotType.Forward, 1, new ItemComponent(0, 0, 0, ItemTag.None))));

        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(EveryLap), TestGameSession.Metadata(Ticket));
        var board = new BlueMarble(new ItemComponent(Ticket, -1, 1, ItemTag.None), [
            new BlueMarble.Round(0, new ItemComponent(EveryLap, -1, 2, ItemTag.None)),
        ], slots);
        GameEvent gameEvent = test.AddEvent(EventId, GameEventType.BlueMarble, board);
        GameSession session = test.Start();
        session.GameEvent.Get(GameEventUserValueType.MapleopolyTotalSlotCount, gameEvent.Id, gameEvent.EndTime);
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.MapleopolyTotalSlotCount, 2);
        session.GameEvent.Mapleopoly.Rolled();
        test.TakeSent();

        new MapleopolyHandler { ItemMetadata = null! }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(3)));

        byte[] result = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.Mapleopoly);
        Assert.That(result[2], Is.EqualTo(4));
        Assert.That(BitConverter.ToInt16(result, 3), Is.EqualTo((short) BlueMarbleSlotType.Start));
        Assert.That(Held(session, EveryLap), Is.EqualTo(2));
        Assert.That(test.Errors, Is.Empty);
    }

    // The client's tile names (its slot type parser): an empty or unknown name is its fail tile (1), not an item tile.
    [TestCase("fail", BlueMarbleSlotType.Lose)]
    [TestCase("backward", BlueMarbleSlotType.Backward)]
    [TestCase("forward", BlueMarbleSlotType.Forward)]
    [TestCase("gotostarting", BlueMarbleSlotType.GoToStarting)]
    [TestCase("dicemore", BlueMarbleSlotType.Roll)]
    [TestCase("Starting", BlueMarbleSlotType.Start)]
    [TestCase("trap", BlueMarbleSlotType.Trap)]
    [TestCase("worldtour", BlueMarbleSlotType.WorldTour)]
    [TestCase("paradise", BlueMarbleSlotType.Paradise)]
    [TestCase("item", BlueMarbleSlotType.Item)]
    [TestCase("", BlueMarbleSlotType.Lose)]
    [TestCase(null, BlueMarbleSlotType.Lose)]
    [TestCase("lose", BlueMarbleSlotType.Lose)]
    [TestCase("bonus", BlueMarbleSlotType.Lose)]
    public void Mapleopoly_TileName_IsTheClientsKind(string? name, BlueMarbleSlotType expected) {
        Assert.That(ServerTableMapper.ParseBlueMarbleSlotType(name), Is.EqualTo(expected));
    }

    // With no live board the load, roll and result requests are answered with the client's error 5 (kind 6), which
    // shows its notice and turns the dice button back on.
    [TestCase((byte) 0)]
    [TestCase((byte) 1)]
    [TestCase((byte) 3)]
    public void Mapleopoly_NoBoard_IsTheClientsError5(byte mode) {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(Ticket));
        GameSession session = test.Start();
        test.TakeSent();

        new MapleopolyHandler { ItemMetadata = null! }.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(mode)));

        byte[] reply = test.TakeSent().Single(packet => TestGameSession.Op(packet) == SendOp.Mapleopoly);
        Assert.That(reply.Skip(2).ToArray(), Is.EqualTo(new byte[] { 6, 5 }));
        Assert.That(test.Errors, Is.Empty);
    }
}
