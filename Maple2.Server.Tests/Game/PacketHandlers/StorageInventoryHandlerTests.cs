using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// StorageInventoryHandler.Handle in the order KMS2 sends its answers.
public class StorageInventoryHandlerTests {
    private TestGameSession test = null!;
    private readonly StorageInventoryHandler handler = new();
    private Item stack = null!;

    private GameSession Open(long meso = 0) {
        test = new TestGameSession();
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
        GameSession session = Open(meso: Maple2.Model.Metadata.Constant.MaxMeso);
        Mesos(session, deposit: true, Maple2.Model.Metadata.Constant.MaxMeso);
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
