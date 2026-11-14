using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// StorageInventoryHandler.Handle in the order KMS2 sends its answers.
public class StorageInventoryHandlerTests {
    private TestGameSession test = null!;
    private readonly StorageInventoryHandler handler = new();
    private Item stack = null!;

    private GameSession Open(long meso = 0, bool transferEvent = false) {
        test = new TestGameSession();
        if (transferEvent) {
            test.AddEvent(20203, Maple2.Model.Enum.GameEventType.TransferItemBound, new Maple2.Model.Metadata.TransferItemBound(true));
        }
        test.SetServerTable(new Maple2.Model.Metadata.ConstantsTable(
            bagSlotTabGameCount: [48, 48], bagSlotTabSkinCount: [48, 48], bagSlotTabSummonCount: [48, 48],
            bagSlotTabMaterialCount: [48, 48], bagSlotTabMasteryCount: [48, 48], bagSlotTabLifeCount: [48, 48],
            bagSlotTabQuestCount: [48, 48], bagSlotTabGemCount: [48, 48], bagSlotTabPetCount: [48, 48],
            bagSlotTabActiveSkillCount: [48, 48], bagSlotTabCoinCount: [48, 48], bagSlotTabBadgeCount: [48, 48],
            bagSlotTabMiscCount: [48, 48], bagSlotTabLapenShardCount: [48, 48], bagSlotTabPieceCount: [48, 48],
            StoreExpandMaxSlotCount: 12));
        stack = test.Store(TestGameSession.Metadata(20000001, type: 2), amount: 5);
        GameSession session = test.Start();
        session.Currency.Meso = meso;
        test.TakeSent();
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteLong(0);
        }));
        return session;
    }

    private void Item(GameSession session, byte command, long uid, int amount) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.WriteLong(0);
            writer.WriteLong(uid);
            writer.WriteShort(-1);
            writer.WriteInt(amount);
        }));
    }

    private void Mesos(GameSession session, bool deposit, long amount) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(0);
            writer.WriteBool(deposit);
            writer.WriteLong(amount);
        }));
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // Reset, the expansion, the mesos, the used slots; an empty storage has no item batch.
    [Test]
    public void Load_SendsTheStorageState() {
        Open();

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(StorageInventoryPacket.Reset()),
            Hex(StorageInventoryPacket.SlotsExpanded(0)),
            Hex(StorageInventoryPacket.UpdateMesos(0)),
            Hex(StorageInventoryPacket.SlotsUsed(0)),
        }));
    }

    // A deposit: the bag's change, then the storage's add. A withdrawal: the storage's removal, then the bag's add.
    [Test]
    public void DepositAndWithdraw_Order() {
        GameSession session = Open();
        test.TakeSent();

        Item(session, 0, stack.Uid, 5);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ItemInventory, SendOp.StorageInventory }));
        Assert.That(sent[0][2], Is.EqualTo(1)); // removed from the bag
        Assert.That(sent[1][2], Is.EqualTo(0)); // added to the storage
        Assert.That(session.Item.Inventory.Get(stack.Uid), Is.Null);

        Item(session, 1, stack.Uid, 5);

        sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.StorageInventory, SendOp.ItemInventory }));
        Assert.That(sent[0][2], Is.EqualTo(1)); // removed from the storage
        Assert.That(sent[1][2], Is.EqualTo(0)); // added to the bag
        Assert.That(session.Item.Inventory.Find(20000001).Sum(item => item.Amount), Is.EqualTo(5));
    }

    // Mesos in: the player's mesos, then the storage's. Mesos out: the storage's, then the player's.
    [Test]
    public void Mesos_Order() {
        GameSession session = Open(meso: 1000);
        test.TakeSent();

        Mesos(session, deposit: true, 400);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(CurrencyPacket.UpdateMeso(session.Player.Value.Currency)),
            Hex(StorageInventoryPacket.UpdateMesos(400)),
        }));
        Assert.That(session.Currency.Meso, Is.EqualTo(600));

        Mesos(session, deposit: false, 150);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(StorageInventoryPacket.UpdateMesos(250)),
            Hex(CurrencyPacket.UpdateMeso(session.Player.Value.Currency)),
        }));
        Assert.That(session.Currency.Meso, Is.EqualTo(750));
    }

    // The window asks again while the server still holds the storage open: the same state is sent again, with
    // what was put in.
    [Test]
    public void Load_OfAnOpenStorageSendsItAgain() {
        GameSession session = Open(meso: 1000);
        Mesos(session, deposit: true, 400);
        Item(session, 0, stack.Uid, 5);
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteLong(0);
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Take(4).Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(StorageInventoryPacket.Reset()),
            Hex(StorageInventoryPacket.SlotsExpanded(0)),
            Hex(StorageInventoryPacket.UpdateMesos(400)),
            Hex(StorageInventoryPacket.SlotsUsed(1)),
        }));
        Assert.That(sent, Has.Count.EqualTo(5));
        Assert.That(sent[4][2], Is.EqualTo(5)); // the item batch
    }

    // More mesos than the storage holds: the max money text, and nothing moves. A count below 0: the invalid text.
    [Test]
    public void MesosOverTheMax_AreRefusedWithTheirOwnText() {
        GameSession session = Open(meso: Maple2.Model.Validators.StorageRules.MaxMesos);
        Mesos(session, deposit: true, Maple2.Model.Validators.StorageRules.MaxMesos);
        session.Currency.Meso = 5;
        test.TakeSent();

        Mesos(session, deposit: true, 1);
        Mesos(session, deposit: true, -1);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(StorageInventoryPacket.Error(Maple2.Model.Error.StorageInventoryError.s_store_err_deposit_max_money)),
            Hex(StorageInventoryPacket.Error(Maple2.Model.Error.StorageInventoryError.s_store_err_deposit_invalid_money)),
        }));
        Assert.That(session.Currency.Meso, Is.EqualTo(5));
    }

    // A withdrawal the bag does not take (the player is in no field) goes back into the storage.
    [Test]
    public void Withdraw_TheBagDoesNotTakeStaysStored() {
        GameSession session = Open();
        Item(session, 0, stack.Uid, 5);
        Maple2.Server.Game.Manager.Field.FieldManager? field = session.Field;
        session.Field = null;
        test.TakeSent();

        Item(session, 1, stack.Uid, 5);
        session.Field = field;

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(packet => packet[2]), Is.EqualTo(new byte[] { 1, 0 })); // removed, added again
        Item(session, 1, stack.Uid, 5);
        Assert.That(session.Item.Inventory.Find(20000001).Sum(item => item.Amount), Is.EqualTo(5));
    }

    // A moveDisable item is stored with its character, which the item block ends with; another character of the
    // account is refused with the text of the client's own refusal, the storing one takes it out without the name.
    [Test]
    public void MoveDisableItem_LeavesWithTheCharacterThatStoredIt() {
        GameSession session = Open();
        Maple2.Model.Metadata.ItemMetadata metadata = TestGameSession.Metadata(20000002, type: 2, slotMax: 1);
        metadata = metadata with { Property = metadata.Property with { MoveDisable = true } };
        Item item = test.Store(metadata);
        session.Item.Inventory.Add(item);
        test.TakeSent();

        Item(session, 0, item.Uid, 1);
        byte[] added = test.TakeSent()[^1];
        var block = new Maple2.PacketLib.Tools.ByteWriter();
        block.WriteClass<ItemBinding>(new ItemBinding(test.CharacterId, session.Player.Value.Character.Name));
        Assert.That(Convert.ToHexString(added), Does.EndWith(Convert.ToHexString(block.ToArray())));

        typeof(ItemBinding).GetProperty(nameof(ItemBinding.CharacterId))!.SetValue(item.Binding, test.CharacterId + 1);
        Item(session, 1, item.Uid, 1);
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(StorageInventoryPacket.Error(Maple2.Model.Error.StorageInventoryError.s_item_err_moveDisableitem_store_out)),
        }));

        typeof(ItemBinding).GetProperty(nameof(ItemBinding.CharacterId))!.SetValue(item.Binding, test.CharacterId);
        Item(session, 1, item.Uid, 1);
        Assert.That(session.Item.Inventory.Get(item.Uid)?.Binding, Is.Null);
        Assert.That(test.Errors, Is.Empty);
    }

    // A gemless skin bound to another character leaves the storage while a TransferItemBound event runs (the
    // client's StoreSlot::vfunc_26 lets it through, storeslot.cpp:441) with the binding of the character that takes
    // it; without the event, or with a gem in it, it stays in with the bound-item refusal.
    [TestCase(true, false, true)]
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    public void BoundSkin_LeavesWhileTheTransferEventRuns(bool transferEvent, bool gem, bool moved) {
        GameSession session = Open(transferEvent: transferEvent);
        Maple2.Model.Metadata.ItemMetadata metadata = TestGameSession.Metadata(11400002, EquipSlot.CL);
        metadata = metadata with { Property = metadata.Property with { SkinType = 1 } };
        Item item = test.Store(metadata);
        item.Transfer = new ItemTransfer(binding: new ItemBinding(test.CharacterId, "other"));
        if (gem) {
            item.Socket = new ItemSocket(1, [new ItemGemstone(40000001, null, new ItemStats())]);
        }
        session.Item.Inventory.Add(item);
        Item(session, 0, item.Uid, 1);
        typeof(ItemBinding).GetProperty(nameof(ItemBinding.CharacterId))!.SetValue(item.Transfer.Binding, test.CharacterId + 1);
        test.TakeSent();

        Item(session, 1, item.Uid, 1);
        if (!moved) {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Hex(StorageInventoryPacket.Error(Maple2.Model.Error.StorageInventoryError.s_item_err_binditem_store_out)),
            }));
            Assert.That(session.Item.Inventory.Get(item.Uid), Is.Null);
            return;
        }
        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Get(item.Uid)?.Transfer?.Binding?.CharacterId, Is.EqualTo(test.CharacterId));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // Two characters store the same stackable moveDisable item: the copies stay on two stacks with their own
    // record, so each character takes out only its own and the other's copies stay stored.
    [Test]
    public void MoveDisableStacks_KeepTheirOwnRecord() {
        GameSession session = Open();
        Maple2.Model.Metadata.ItemMetadata metadata = TestGameSession.Metadata(20000003, type: 2, slotMax: 10);
        metadata = metadata with { Property = metadata.Property with { MoveDisable = true } };
        Item other = test.Store(metadata, amount: 3);
        Item mine = test.Store(metadata, amount: 3);
        session.Item.Inventory.Add(other);
        Item(session, 0, other.Uid, 3);
        typeof(ItemBinding).GetProperty(nameof(ItemBinding.CharacterId))!.SetValue(other.Binding, test.CharacterId + 1);
        session.Item.Inventory.Add(mine);
        test.TakeSent();

        Item(session, 0, mine.Uid, 3);

        Assert.That(other.Amount, Is.EqualTo(3));
        Assert.That(other.Binding!.CharacterId, Is.EqualTo(test.CharacterId + 1));
        Assert.That(mine.Binding!.CharacterId, Is.EqualTo(test.CharacterId));
        Item(session, 1, other.Uid, 3);
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(
            Hex(StorageInventoryPacket.Error(Maple2.Model.Error.StorageInventoryError.s_item_err_moveDisableitem_store_out))));
        Item(session, 1, mine.Uid, 3);
        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Get(mine.Uid)?.Amount, Is.EqualTo(3));
            Assert.That(session.Item.Inventory.Get(other.Uid), Is.Null);
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A dead player's destroy request of a stored item (10) is not answered and the item stays stored; the client
    // shows its own text and never sends it.
    [Test]
    public void Delete_WhileDeadIsIgnored() {
        GameSession session = Open();
        Item(session, 0, stack.Uid, stack.Amount);
        // The death itself needs a field's data; the flag alone is what the handler reads.
        System.Reflection.FieldInfo dead = typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("isDead",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        dead.SetValue(session.Player, true);
        Assert.That(session.Player.IsDead, Is.True);
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.WriteLong(stack.Uid);
        }));

        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.StorageInventory), Is.Empty);
        dead.SetValue(session.Player, false);
        test.TakeSent();
        Item(session, 1, stack.Uid, stack.Amount);
        Assert.That(session.Item.Inventory.Get(stack.Uid)?.Amount, Is.EqualTo(5));
    }

    // An expansion: the merets, then the new count of added slots and the expand effect (7, no payload); no
    // further than the constant.
    [Test]
    public void Expand_AnswersWithTheAddedSlots() {
        GameSession session = Open();
        session.Currency.Meret = 1000;
        test.TakeSent();
        void Expand() => handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(6);
            writer.WriteLong(0);
        }));

        Expand();
        Expand();
        Expand();

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Where(packet => TestGameSession.Op(packet) == SendOp.StorageInventory).Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(StorageInventoryPacket.SlotsExpanded(6)),
            Hex(StorageInventoryPacket.ExpandEffect()),
            Hex(StorageInventoryPacket.SlotsExpanded(12)),
            Hex(StorageInventoryPacket.ExpandEffect()),
            Hex(StorageInventoryPacket.Error(Maple2.Model.Error.StorageInventoryError.s_store_err_expand_max)),
        }));
        Assert.That(session.Currency.Meret, Is.EqualTo(1000 - 2 * 330));
        // 7 is the command byte alone.
        Assert.That(Convert.ToHexString(StorageInventoryPacket.ExpandEffect().ToArray()[2..]), Is.EqualTo("07"));
    }

    // The destroy request of a stored item (10, the item's uid): the storage's removal, and the item is in
    // neither the storage nor the bag.
    [Test]
    public void Delete_RemovesTheStoredItem() {
        GameSession session = Open();
        Item(session, 0, stack.Uid, stack.Amount);
        test.TakeSent();

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.WriteLong(stack.Uid);
        }));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(StorageInventoryPacket.Remove(stack.Uid)) }));
            Assert.That(session.Item.Inventory.Get(stack.Uid), Is.Null);
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // Deposits and withdrawals of parts of a stack, each side asked for more than it has in between: the five
    // pieces are all in the bag or the storage after every step.
    [Test]
    public void PartialMoves_KeepTheCount() {
        GameSession session = Open();
        Item(session, 0, stack.Uid, 2);
        Item(session, 0, stack.Uid, 4);
        Item(session, 0, stack.Uid, 0);
        Item(session, 0, stack.Uid, -1);
        test.TakeSent();
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteLong(0);
        }));
        byte[] batch = test.TakeSent()[^1];
        long stored = BitConverter.ToInt64(batch, 3 + 8 + 2 + 4);

        Assert.That(session.Item.Inventory.Find(20000001).Sum(item => item.Amount), Is.EqualTo(3));
        Item(session, 1, stored, 3);
        Item(session, 1, stored, 1);
        Item(session, 1, stored, 0);
        Assert.That(session.Item.Inventory.Find(20000001).Sum(item => item.Amount), Is.EqualTo(4));
        Item(session, 1, stored, 1);
        Assert.That(session.Item.Inventory.Find(20000001).Sum(item => item.Amount), Is.EqualTo(5));
        Item(session, 1, stored, 1);
        Assert.That(session.Item.Inventory.Find(20000001).Sum(item => item.Amount), Is.EqualTo(5));
        Assert.That(test.Errors, Is.Empty);
    }
}
