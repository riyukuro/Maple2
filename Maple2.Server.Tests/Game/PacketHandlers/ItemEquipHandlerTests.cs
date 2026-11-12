using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemEquipHandler.Handle in the order KMS2 sends its answers, and the gear score that follows a gear change.
public class ItemEquipHandlerTests {
    private TestGameSession test = null!;
    private readonly ItemEquipHandler handler = new();

    private void Equip(GameSession session, Item item, EquipSlot slot) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(item.Uid);
            writer.WriteByte((byte) slot);
            writer.WriteByte(0);
        }));
    }

    private void Unequip(GameSession session, Item item) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(item.Uid);
        }));
    }

    private static byte Mode(byte[] packet) => packet[2];

    // An item that is taken off: ItemPutOff, then the bag's add.
    [Test]
    public void Unequip_SendsPutOffBeforeTheBagAdd() {
        test = new TestGameSession();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1));
        GameSession session = test.Start();
        Equip(session, cap, EquipSlot.CP);
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ItemInventory, SendOp.ItemPutOn }));

        Unequip(session, cap);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ItemPutOff, SendOp.ItemInventory }));
        Assert.That(Mode(sent[1]), Is.EqualTo(0));
        Assert.That(session.Item.Inventory.Get(cap.Uid), Is.Not.Null);
        Assert.That(session.Item.Equips.Get(cap.Uid), Is.Null);
    }

    // An item that takes the place of a worn one: the new item leaves the bag, the old one enters it, then
    // ItemPutOff (old) and ItemPutOn (new).
    [Test]
    public void Equip_OverAWornItemAddsItToTheBagFirst() {
        test = new TestGameSession();
        Item first = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1));
        Item second = test.Store(TestGameSession.Metadata(11300002, EquipSlot.CP, slotMax: 1));
        GameSession session = test.Start();
        Equip(session, first, EquipSlot.CP);
        test.TakeSent();

        Equip(session, second, EquipSlot.CP);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] {
            SendOp.ItemInventory, SendOp.ItemInventory, SendOp.ItemPutOff, SendOp.ItemPutOn,
        }));
        Assert.That(sent.Take(2).Select(Mode), Is.EqualTo(new byte[] { 1, 0 }));
        Assert.That(session.Item.Equips.Get(second.Uid), Is.Not.Null);
        Assert.That(session.Item.Inventory.Get(first.Uid), Is.Not.Null);
    }

    private static ItemMetadata Bow(int id) {
        return TestGameSession.Metadata(id, EquipSlot.RH, type: 1, slotMax: 1) with { SlotNames = [EquipSlot.RH, EquipSlot.LH] };
    }

    // A two-handed weapon over a worn two-handed weapon with a full bag: the worn one takes the new one's bag slot.
    [TestCase(48)]
    [TestCase(47)]
    public void Equip_TwoHandedOverTwoHandedNeedsNoFreeSlot(int held) {
        test = new TestGameSession();
        Item worn = test.Store(Bow(15100304));
        Item next = test.Store(Bow(15100305));
        GameSession session = test.Start();
        Equip(session, worn, EquipSlot.RH);
        for (int i = 0; i < held - 1; i++) {
            Assert.That(session.Item.Inventory.Add(test.Store(TestGameSession.Metadata(11300001 + i, EquipSlot.CP, type: 1, slotMax: 1))), Is.True);
        }
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Gear), Is.EqualTo(48 - held));
        short slot = session.Item.Inventory.Get(next.Uid)!.Slot;
        test.TakeSent();

        Equip(session, next, EquipSlot.RH);

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(next.Uid), Is.Not.Null);
            Assert.That(session.Item.Inventory.Get(worn.Uid)?.Slot, Is.EqualTo(slot));
            Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Gear), Is.EqualTo(48 - held));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A two-handed weapon over a weapon and a shield: two items come off, so one slot beyond the new item's own
    // must be free. With a full bag the equip is refused with the bag's own notice (ItemInventory error 0).
    [TestCase(48, false)]
    [TestCase(47, true)]
    public void Equip_TwoHandedOverTwoItemsNeedsOneFreeSlot(int held, bool equipped) {
        test = new TestGameSession();
        Item sword = test.Store(TestGameSession.Metadata(13200001, EquipSlot.RH, type: 1, slotMax: 1));
        Item shield = test.Store(TestGameSession.Metadata(14100001, EquipSlot.LH, type: 1, slotMax: 1));
        Item bow = test.Store(Bow(15100304));
        GameSession session = test.Start();
        Equip(session, sword, EquipSlot.RH);
        Equip(session, shield, EquipSlot.LH);
        for (int i = 0; i < held - 1; i++) {
            Assert.That(session.Item.Inventory.Add(test.Store(TestGameSession.Metadata(11300001 + i, EquipSlot.CP, type: 1, slotMax: 1))), Is.True);
        }
        test.TakeSent();

        Equip(session, bow, EquipSlot.RH);

        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(bow.Uid) != null, Is.EqualTo(equipped));
            Assert.That(session.Item.Equips.Get(sword.Uid) == null, Is.EqualTo(equipped));
            Assert.That(session.Item.Equips.Get(shield.Uid) == null, Is.EqualTo(equipped));
            if (!equipped) {
                Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] {
                    Convert.ToHexString(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory).ToArray()),
                }));
            }
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // Guild mode 14, as KMS2 sends it: the member's name and the gear score.
    [Test]
    public void GuildGearScorePacket() {
        Assert.That(Convert.ToHexString(GuildPacket.UpdateMemberGearScore("Abcde", 187399).ToArray()),
            Is.EqualTo("8A00" + "0E" + "0500" + "41006200630064006500" + "07DC0200"));
    }

    // The gear score goes to the world once per change.
    [Test]
    public void GearScore_GoesToTheWorldWhenItChanges() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.World.PlayerUpdates.Clear();

        session.Stats.SendGearScore(session, 3382);
        session.Stats.SendGearScore(session, 3382);
        session.Stats.SendGearScore(session, 3400);

        Assert.That(test.World.PlayerUpdates.Select(update => (update.CharacterId, update.HasGearScore, update.GearScore)), Is.EqualTo(new[] {
            (session.CharacterId, true, 3382), (session.CharacterId, true, 3400),
        }));
    }
}
