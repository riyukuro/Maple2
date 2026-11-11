using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class BadgeRulesTests {
    [TestCase(false, false, false, BadgeEquipError.InvalidItem)]
    [TestCase(true, false, false, BadgeEquipError.InvalidItem)]
    [TestCase(true, true, true, BadgeEquipError.Expired)]
    public void EquipRefusals(bool owned, bool isBadge, bool expired, BadgeEquipError expected) {
        Assert.That(BadgeRules.EquipError(owned, isBadge, expired), Is.EqualTo(expected));
    }

    [Test]
    public void EquipAllowed() {
        Assert.That(BadgeRules.EquipError(true, true, false), Is.Null);
    }

    // BadgeEquip 0x0047 mode 1: u8 slot.
    [Test]
    public void UnequipRefusals() {
        Assert.That(BadgeRules.UnequipError(false, false, 5), Is.EqualTo(BadgeEquipError.InvalidSlot));
        Assert.That(BadgeRules.UnequipError(true, true, 0), Is.EqualTo(BadgeEquipError.InventoryFull));
        Assert.That(BadgeRules.UnequipError(true, true, 1), Is.Null);
        Assert.That(BadgeRules.UnequipError(true, false, 0), Is.Null);
    }

    // The answer the client reads: u8 mode 4, i16 error.
    [TestCase(BadgeEquipError.InventoryFull, (byte) 0)]
    [TestCase(BadgeEquipError.Expired, (byte) 3)]
    public void ErrorAnswerBytes(BadgeEquipError error, byte low) {
        ByteWriter writer = EquipPacket.BadgeError(error);
        byte[] bytes = writer.ToArray();
        Assert.That(bytes.Length, Is.EqualTo(2 + 1 + 2));
        Assert.That(bytes[2], Is.EqualTo(4));
        Assert.That(bytes[3], Is.EqualTo(low));
        Assert.That(bytes[4], Is.EqualTo(0));
    }
}
