using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// BadgeEquip 0x00BF modes 0 (put on) and 1 (take off): a badge goes on and off its slot, and a refusal answers the
// client's own error line (mode 4).
public class BadgeEquipPathTests {
    private const int BadgeId = 70400001;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private readonly BadgeEquipHandler handler = new();

    private Item Start(short badgeTab = 48) {
        test = new TestGameSession(gender: Gender.Male);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabBadgeCount = [badgeTab, badgeTab] });
        Item badge = test.Store(TestGameSession.Metadata(BadgeId, type: 7));
        test.Start();
        session.Player.Value.Account.PremiumTime = 0;
        test.TakeSent();
        return badge;
    }

    private void Equip(long uid) => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteByte(0);
        writer.WriteLong(uid);
        writer.WriteByte((byte) BadgeType.Damage);
    }));

    private void Unequip(byte slot) => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteByte(1);
        writer.WriteByte(slot);
    }));

    private string[] Errors() => test.TakeSent()
        .Where(packet => TestGameSession.Op(packet) == SendOp.BadgeEquip && packet[2] == 4)
        .Select(Convert.ToHexString).ToArray();

    private static string Hex(BadgeEquipError error) => Convert.ToHexString(EquipPacket.BadgeError(error).ToArray());

    [Test]
    public void PutOnThenTakeOff_ReturnsTheBadgeToTheBag() {
        Item badge = Start();

        Equip(badge.Uid);
        Assert.That(session.Item.Equips.Get(BadgeType.Damage)?.Uid, Is.EqualTo(badge.Uid));
        Assert.That(session.Item.Inventory.Get(badge.Uid), Is.Null);

        Unequip((byte) BadgeType.Damage);
        Assert.That(session.Item.Equips.Get(BadgeType.Damage), Is.Null);
        Assert.That(session.Item.Inventory.Get(badge.Uid), Is.Not.Null);
        Assert.That(Errors(), Is.Empty);
    }

    // An item not in the bag, or one that is no badge.
    [Test]
    public void PutOn_NotABadgeInTheBag_InvalidItem() {
        Start();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1));
        Assert.That(session.Item.Inventory.Add(cap), Is.True);
        test.TakeSent();

        Equip(cap.Uid);
        Equip(cap.Uid + 999);

        Assert.That(Errors(), Is.EqualTo(new[] { Hex(BadgeEquipError.InvalidItem), Hex(BadgeEquipError.InvalidItem) }));
        Assert.That(session.Item.Equips.Get(BadgeType.Damage), Is.Null);
    }

    [Test]
    public void PutOn_Expired_Expired() {
        Item badge = Start();
        session.Item.Inventory.Get(badge.Uid)!.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;

        Equip(badge.Uid);

        Assert.That(Errors(), Is.EqualTo(new[] { Hex(BadgeEquipError.Expired) }));
        Assert.That(session.Item.Inventory.Get(badge.Uid), Is.Not.Null);
    }

    [Test]
    public void TakeOff_UnknownSlot_InvalidSlot() {
        Start();

        Unequip(200);

        Assert.That(Errors(), Is.EqualTo(new[] { Hex(BadgeEquipError.InvalidSlot) }));
    }

    // The badge tab has no room for the badge that comes off: it stays on.
    [Test]
    public void TakeOff_BadgeTabFull_InventoryFull() {
        Item badge = Start(badgeTab: 1);
        Equip(badge.Uid);
        Assume.That(session.Item.Equips.Get(BadgeType.Damage)?.Uid, Is.EqualTo(badge.Uid));
        Item other = test.Store(TestGameSession.Metadata(BadgeId, type: 7));
        Assert.That(session.Item.Inventory.Add(other), Is.True);
        test.TakeSent();

        Unequip((byte) BadgeType.Damage);

        Assert.That(Errors(), Is.EqualTo(new[] { Hex(BadgeEquipError.InventoryFull) }));
        Assert.That(session.Item.Equips.Get(BadgeType.Damage)?.Uid, Is.EqualTo(badge.Uid));
    }
}
