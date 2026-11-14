using System;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Game;

// GameEventSaleAutoPlayInstrument and GameEventSaleAutoFishing (commonfiles/gameevent.cpp) read the same body: the
// event id, the discount (value1) and the auto action content (value2). The event ids are made up.
public class SaleAutoActionEventTests {
    [Test]
    public void Instrument_WritesTheDiscount() {
        var gameEvent = new GameEvent(new GameEventMetadata(990101, GameEventType.SaleAutoPlayInstrument, DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddSeconds(1000), TimeSpan.Zero, TimeSpan.Zero, [], new SaleAutoPlayInstrument(30, "AutoPlayInstrumentMesoEvent"),
            "30", "AutoPlayInstrumentMesoEvent", "", ""));
        Assert.That(gameEvent.SentToClient, Is.True);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("SaleAutoPlayInstrument"));
        Assert.That(reader.ReadInt(), Is.EqualTo(990101)); // GameEvent::vfunc_4
        Assert.That(reader.ReadInt(), Is.EqualTo(30)); // the discount (m_d0)
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("AutoPlayInstrumentMesoEvent")); // the content (m_d8)
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void Fishing_WritesTheDiscount() {
        var gameEvent = new GameEvent(new GameEventMetadata(990102, GameEventType.SaleAutoFishing, DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddSeconds(1000), TimeSpan.Zero, TimeSpan.Zero, [], new SaleAutoFishing(20, "AutoFishingMesoEvent"),
            "20", "AutoFishingMesoEvent", "", ""));
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("SaleAutoFishing"));
        Assert.That(reader.ReadInt(), Is.EqualTo(990102));
        Assert.That(reader.ReadInt(), Is.EqualTo(20));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("AutoFishingMesoEvent"));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }
}
