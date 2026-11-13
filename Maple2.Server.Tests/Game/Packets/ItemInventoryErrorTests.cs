using System;
using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// ItemInventory mode 0x0d against the client's reader (scenestategame.cpp:2973-3022): int error; errors 0x28 (drop) and
// 0x29 (pickup) then read int item id and a 2-byte value, the level of the message.
public class ItemInventoryErrorTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    [Test]
    public void APlainErrorIsTheModeAndTheCode() {
        Assert.That(Body(ItemInventoryPacket.Error(ItemInventoryError.s_itemdrop_error_restricted_by_user_level_ex - 1)),
            Is.EqualTo(new byte[] { 0x0d, 0x27, 0, 0, 0 }));
    }

    [Test]
    public void TheDropLevelErrorCarriesTheItemAndTheLevel() {
        Assert.That(Body(ItemInventoryPacket.ErrorRestrictedByLevel(false, 0x01020304, 0x0506)),
            Is.EqualTo(new byte[] { 0x0d, 0x28, 0, 0, 0, 0x04, 0x03, 0x02, 0x01, 0x06, 0x05 }));
    }

    [Test]
    public void ThePickupLevelErrorCarriesTheItemAndTheLevel() {
        Assert.That(Body(ItemInventoryPacket.ErrorRestrictedByLevel(true, 0x01020304, 60)),
            Is.EqualTo(new byte[] { 0x0d, 0x29, 0, 0, 0, 0x04, 0x03, 0x02, 0x01, 60, 0 }));
    }

    [TestCase(ItemInventoryError.s_itemdrop_error_restricted_by_user_level_ex)]
    [TestCase(ItemInventoryError.s_itempickup_error_restricted_by_user_level_ex)]
    public void TheLevelErrorsAreRefusedWithoutTheirFields(ItemInventoryError error) {
        Assert.Throws<ArgumentException>(() => ItemInventoryPacket.Error(error));
    }
}
