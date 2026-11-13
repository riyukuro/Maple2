using System;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// ItemSocketSystem mode 5 for a gem in an equipment socket, against the client's reader (itemsocketsystemutil.cpp:1005):
// long item uid, byte slot, byte success, byte grade, long gem uid, bool, then the gem.
public class GemstoneUpgradeSlotTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    [Test]
    public void TheSlotIsTheSocketTheGemIsIn() {
        var gem = new ItemGemstone(40100002, stats: ItemStats.Default);
        byte[] body = Body(ItemSocketPacket.UpgradeGemstone(0x0102030405060708L, 2, true, 0x1112131415161718L, gem));
        Assert.That(body, Is.EqualTo(new byte[] {
            0x05,
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
            0x02,
            0x01,
            0x04,
            0x18, 0x17, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11,
            0x01,
            0xa2, 0xe0, 0x63, 0x02,
            0x00,
            0x00,
        }));
    }
}
