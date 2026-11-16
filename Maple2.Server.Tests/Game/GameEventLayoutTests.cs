using System;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Tests.Game;

// GameEvent packet blocks as the client reads them, on KR rows. GameEventManagerCli::Packet0-3
// (gameeventmanagercli.cpp): Load and Add (1) and Reload (3) read an int count, then per event its type name and the
// class's vfunc_4 (GameEvent::vfunc_4 reads the id first, gameeventbase.cpp:196); Remove (2) reads a count of ids.
public class GameEventLayoutTests {
    private static GameEvent Event(int id, GameEventType type, GameEventData data, string value1 = "", string value2 = "") =>
        new(new GameEventMetadata(id, type, DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(1000), TimeSpan.Zero, TimeSpan.Zero, [], data,
            value1, value2, "", ""));

    private static ByteReader Written(GameEvent gameEvent, string name) {
        Assert.That(gameEvent.SentToClient, Is.True);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo(name));
        Assert.That(reader.ReadInt(), Is.EqualTo(gameEvent.Id));
        return reader;
    }

    private static void AssertEnd(ByteReader reader, GameEvent gameEvent) {
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // <gameEvent gameEventID="150017" eventType="EventFieldPopup" value1="63000060" />: GameEventEventFieldPopup
    // reads the field id.
    [Test]
    public void EventFieldPopup_TheField() {
        GameEvent gameEvent = Event(150017, GameEventType.EventFieldPopup, new EventFieldPopup(63000060), "63000060");
        ByteReader reader = Written(gameEvent, "EventFieldPopup");
        Assert.That(reader.ReadInt(), Is.EqualTo(63000060));
        AssertEnd(reader, gameEvent);
    }

    // <gameEvent gameEventID="30028" eventType="SaleChat" value1="10000" />: the world chat discount, then the channel
    // chat's (no value2: 0).
    [Test]
    public void SaleChat_BothDiscounts() {
        GameEvent gameEvent = Event(30028, GameEventType.SaleChat, new SaleChat(10000, 0), "10000");
        ByteReader reader = Written(gameEvent, "SaleChat");
        Assert.That(reader.ReadInt(), Is.EqualTo(10000));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        AssertEnd(reader, gameEvent);
    }

    // <gameEvent gameEventID="510002" eventType="StringBoardLink" value1="<a news page link>" />: the link (here a stand-in).
    [Test]
    public void StringBoardLink_TheLink() {
        const string link = "http://example.com/News/EventsView?sn=197";
        GameEvent gameEvent = Event(510002, GameEventType.StringBoardLink, new StringBoardLink(link), link);
        ByteReader reader = Written(gameEvent, "StringBoardLink");
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo(link));
        AssertEnd(reader, gameEvent);
    }

    // <gameEvent gameEventID="90000061" eventType="UGCMapContractSale" value1="5000" /> and 90000062
    // UGCMapExtensionSale value1="5000": the discount.
    [Test]
    public void UgcMapSales_TheDiscount() {
        GameEvent contract = Event(90000061, GameEventType.UGCMapContractSale, new UGCMapContractSale(5000), "5000");
        ByteReader reader = Written(contract, "UGCMapContractSale");
        Assert.That(reader.ReadInt(), Is.EqualTo(5000));
        AssertEnd(reader, contract);

        GameEvent extension = Event(90000062, GameEventType.UGCMapExtensionSale, new UGCMapExtensionSale(5000), "5000");
        reader = Written(extension, "UGCMapExtensionSale");
        Assert.That(reader.ReadInt(), Is.EqualTo(5000));
        AssertEnd(reader, extension);
    }

    // Add and Reload carry the events as Load does, under their command; Remove the ids alone.
    [TestCase(1)]
    [TestCase(3)]
    public void AddAndReload_CountThenEvents(byte command) {
        GameEvent gameEvent = Event(150017, GameEventType.EventFieldPopup, new EventFieldPopup(63000060), "63000060");
        ByteWriter packet = command == 1 ? GameEventPacket.Add(gameEvent) : GameEventPacket.Reload(gameEvent);
        var block = new ByteWriter();
        gameEvent.WriteTo(block);

        var reader = new ByteReader(packet.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.GameEvent));
        Assert.That(reader.ReadByte(), Is.EqualTo(command));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(Convert.ToHexString(packet.Buffer, reader.Position, packet.Length - reader.Position),
            Is.EqualTo(Convert.ToHexString(block.Buffer, 0, block.Length)));
    }

    [Test]
    public void Remove_CountThenIds() {
        ByteWriter packet = GameEventPacket.Remove(150017, 90000061);
        Assert.That(Convert.ToHexString(packet.Buffer, 2, packet.Length - 2), Is.EqualTo("02" + "02000000" + "014A0200" + "BD4A5D05"));
    }
}
