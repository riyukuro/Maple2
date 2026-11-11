using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class RideAlonePacketTests {
    [Test]
    public void RideAloneBytes() {
        ByteWriter writer = RidePacket.RideAlone(0x01020304, true);
        byte[] body = writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();
        // sub-type 6, player object id, option byte
        Assert.That(body, Is.EqualTo(new byte[] { 6, 0x04, 0x03, 0x02, 0x01, 1 }));
    }

    [Test]
    public void RideAloneOffBytes() {
        ByteWriter writer = RidePacket.RideAlone(7, false);
        byte[] body = writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();
        Assert.That(body, Is.EqualTo(new byte[] { 6, 7, 0, 0, 0, 0 }));
    }
}
