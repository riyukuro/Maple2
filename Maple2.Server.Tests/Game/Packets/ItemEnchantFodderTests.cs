using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// ItemEnchant mode 8 against the client's reader (uiitemenchantdialog.cpp:1144-1156): the int the dialog shows as the
// copies it has (m_2bc), the uid count, then the uids.
public class ItemEnchantFodderTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    [Test]
    public void TheShownCountIsTheFodderWeight() {
        // One toolkit for a dagger weighs 2.
        byte[] body = Body(ItemEnchantPacket.UpdateFodder([0x0102030405060708L], 2));
        Assert.That(body, Is.EqualTo(new byte[] {
            0x08,
            0x02, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00,
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
        }));
    }
}
