using System.Linq;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// A refused finish (a full bag) unlatches both sides for each trader: mode 12 with true (own side: the lock button
// shows again) and with false (the other side). The dialog's confirm click greys Confirm until both sides latch again.
public class TradeUnFinalizePacketTests {
    [Test]
    public void BothSidesUnlatched() {
        var sides = TradePacket.UnFinalizeBoth().Select(writer => {
            var reader = new ByteReader(writer.ToArray(), 0);
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Trade));
            Assert.That(reader.ReadByte(), Is.EqualTo(12));
            bool self = reader.ReadBool();
            Assert.That(reader.Available, Is.Zero);
            return self;
        });
        Assert.That(sides, Is.EqualTo(new[] { true, false }));
    }
}
