using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
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
        Assert.That(test.Errors, Is.Empty);
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
}
