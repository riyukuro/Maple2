using System.Collections.Generic;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemSocketSystem 8 then 10: a gem put into a socket and taken out again is the same gem, with its binding, trade
// count, lock and stats. The take-off created a fresh gem, so put-on + take-off unbound a bound gem and reset its
// trades. Its stats are rolled for its id, as before.
public class GemstoneSocketStateTests {
    private TestGameSession test = null!;
    private Item gear = null!;
    private Item gem = null!;

    private GameSession Start(TransferType transferType = TransferType.Tradable) {
        test = new TestGameSession();
        test.SetTable(new GemstoneUpgradeTable(new Dictionary<int, GemstoneUpgradeTable.Entry> {
            [40200001] = new(1, 40200002, [], 3),
        }));
        ItemMetadata gearData = TestGameSession.Metadata(11200001, type: 1);
        gear = test.Store(gearData with { Property = gearData.Property with { SkinType = 1 } });
        ItemMetadata gemData = TestGameSession.Metadata(40200001, type: 13);
        gem = test.Store(gemData with { Limit = gemData.Limit with { TransferType = transferType } });
        GameSession session = test.Start();
        test.TakeSent();
        gear = session.Item.Inventory.Get(gear.Uid)!;
        gear.Socket = new ItemSocket(3, 1);
        gem = session.Item.Inventory.Get(gem.Uid, InventoryType.Gemstone)!;
        gem.Transfer = new ItemTransfer(TransferFlag.Trade | TransferFlag.Split, remainTrades: 3, repackageCount: 1,
            binding: new ItemBinding(session.CharacterId, "Bound"));
        gem.Stats = new ItemStats();
        gem.Stats[ItemStats.Type.Static].Basic[BasicAttribute.Strength] = new BasicOption(7); // not the id's roll
        gem.UnlockTime = 1234;
        return session;
    }

    private void Handle(GameSession session, System.Action<Maple2.PacketLib.Tools.IByteWriter> write) {
        new ItemSocketHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata }
            .Handle(session, TestGameSession.Packet(write));
    }

    private void PutOn(GameSession session) => Handle(session, writer => {
        writer.WriteByte(8);
        writer.WriteLong(gear.Uid);
        writer.WriteLong(gem.Uid);
        writer.WriteByte(0);
    });

    private void TakeOff(GameSession session) => Handle(session, writer => {
        writer.WriteByte(10);
        writer.WriteLong(gear.Uid);
        writer.WriteByte(0);
    });

    private static Item? Single(GameSession session) {
        Item? found = null;
        foreach (Item item in session.Item.Inventory.Find(40200001)) {
            Assert.That(found, Is.Null, "one gem expected");
            found = item;
        }
        return found;
    }

    [Test]
    public void PutOnThenTakeOff_KeepsTheGemsState() {
        GameSession session = Start();

        PutOn(session);
        Assert.That(gear.Socket!.Sockets[0], Is.Not.Null);
        Assert.That(gear.Socket.Sockets[0]!.Binding?.CharacterId, Is.EqualTo(session.CharacterId));
        TakeOff(session);

        Item? back = Single(session);
        Assert.Multiple(() => {
            Assert.That(gear.Socket!.Sockets[0], Is.Null);
            Assert.That(back, Is.Not.Null);
            Assert.That(back!.Transfer?.Binding?.CharacterId, Is.EqualTo(session.CharacterId));
            Assert.That(back.Transfer?.RemainTrades, Is.EqualTo(3));
            Assert.That(back.Transfer?.RepackageCount, Is.EqualTo(1));
            Assert.That(back.Transfer?.Flag, Is.EqualTo(TransferFlag.Trade | TransferFlag.Split));
            Assert.That(back.UnlockTime, Is.EqualTo(1234));
            Assert.That(back.Stats?[ItemStats.Type.Static].Basic.ContainsKey(BasicAttribute.Strength), Is.Not.True);
        });
    }

    // A locked gem stays locked (the socketed record carries the lock to the client already).
    [Test]
    public void LockedGem_StaysLocked() {
        GameSession session = Start();
        gem.IsLocked = true;
        ItemGemstone socketed = new ItemGemstone(40200001, gem.Transfer!.Binding, new ItemStats(), isLocked: true, unlockTime: 0, transfer: gem.Transfer);
        gear.Socket!.Sockets[0] = socketed;
        Assert.That(session.Item.Inventory.Consume(gem.Uid, amount: 1), Is.True);

        TakeOff(session);

        Item? back = Single(session);
        Assert.Multiple(() => {
            Assert.That(back, Is.Not.Null);
            Assert.That(back!.IsLocked, Is.True);
            Assert.That(back.Transfer?.RemainTrades, Is.EqualTo(3));
        });
    }

    // A gem socketed before its transfer was kept: its record's binding was the storage binding, so the gem comes
    // back with the fresh transfer and the record's lock.
    [Test]
    public void OldRecord_KeepsItsLockOnly() {
        GameSession session = Start();
        gear.Socket!.Sockets[0] = new ItemGemstone(40200001, new ItemBinding(session.CharacterId, "Stored"), new ItemStats(), isLocked: true);
        Assert.That(session.Item.Inventory.Consume(gem.Uid, amount: 1), Is.True);

        TakeOff(session);

        Item? back = Single(session);
        Assert.Multiple(() => {
            Assert.That(back, Is.Not.Null);
            Assert.That(back!.IsLocked, Is.True);
            Assert.That(back.Transfer?.Binding, Is.Null);
        });
    }

    // A socket row as written before the gem's transfer was kept: the same fields, no Transfer key, and the record's
    // binding is the bag gem's storage binding. It is read through the column's own converter and written back
    // unchanged.
    private Maple2.Model.Game.ItemSocket OldRow(GameSession session) {
        using var context = new Ms2Context(test.Options);
        Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter converter = context.Model
            .FindEntityType(typeof(Maple2.Database.Model.Item))!.FindProperty(nameof(Maple2.Database.Model.Item.Socket))!.GetValueConverter()!;
        var written = new Maple2.Model.Game.ItemSocket(3, 1);
        written.Sockets[0] = new ItemGemstone(40200001, new ItemBinding(session.CharacterId, "Stored"), new ItemStats(), isLocked: true);
        var json = (string) converter.ConvertToProvider((Maple2.Database.Model.ItemSocket) written)!;
        Assert.That(json, Does.Not.Contain("Transfer"));
        var row = (Maple2.Database.Model.ItemSocket) converter.ConvertFromProvider(json)!;
        Assert.That(converter.ConvertToProvider(row), Is.EqualTo(json));
        return row;
    }

    // An old row's gem whose rule binds (BindOnEquip here) cannot be told from a bound one: it comes back bound to
    // the character, never looser than it went in.
    [Test]
    public void OldRow_BindingRule_BindsTheGem() {
        GameSession session = Start(TransferType.BindOnEquip);
        gear.Socket = OldRow(session);
        Assert.That(session.Item.Inventory.Consume(gem.Uid, amount: 1), Is.True);

        TakeOff(session);

        Item? back = Single(session);
        Assert.Multiple(() => {
            Assert.That(back, Is.Not.Null);
            Assert.That(back!.Transfer?.Binding?.CharacterId, Is.EqualTo(session.CharacterId));
            Assert.That(back.IsLocked, Is.True);
        });
    }

    // An old row's tradable gem stays unbound: its rule has no binding.
    [Test]
    public void OldRow_TradableGem_StaysUnbound() {
        GameSession session = Start();
        gear.Socket = OldRow(session);
        Assert.That(session.Item.Inventory.Consume(gem.Uid, amount: 1), Is.True);

        TakeOff(session);

        Item? back = Single(session);
        Assert.Multiple(() => {
            Assert.That(back, Is.Not.Null);
            Assert.That(back!.Transfer?.Binding, Is.Null);
            Assert.That(back.IsLocked, Is.True);
        });
    }

    // The socket is saved with the gem's transfer and read back with it.
    [Test]
    public void SocketRow_KeepsTheTransfer() {
        GameSession session = Start();
        PutOn(session);

        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveItems(session.CharacterId, gear), Is.True);
        }
        Item? read;
        using (GameStorage.Request db = test.Storage.Context()) {
            read = db.GetItem(gear.Uid);
        }

        ItemGemstone? socketed = read?.Socket?.Sockets[0];
        Assert.Multiple(() => {
            Assert.That(socketed, Is.Not.Null);
            Assert.That(socketed!.Transfer?.RemainTrades, Is.EqualTo(3));
            Assert.That(socketed.Transfer?.Binding?.CharacterId, Is.EqualTo(session.CharacterId));
            Assert.That(socketed.Binding?.CharacterId, Is.EqualTo(session.CharacterId));
        });
    }
}
