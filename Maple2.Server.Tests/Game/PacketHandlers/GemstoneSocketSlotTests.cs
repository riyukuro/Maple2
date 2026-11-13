using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using static Maple2.Model.Error.ItemSocketError;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemSocketSystem 8 and 10 (gem put-on and take-off) name a socket slot from 0. The item's Sockets array holds the
// open sockets only, so slot UnlockSlots is the first locked one and slot MaxSlots is past the last.
public class GemstoneSocketSlotTests {
    private TestGameSession test = null!;
    private Item gear = null!;
    private Item gem = null!;

    private GameSession Start() {
        test = new TestGameSession();
        gear = test.Store(TestGameSession.Metadata(11200001, type: 1));
        gem = test.Store(TestGameSession.Metadata(40200001, type: 13), amount: 1);
        GameSession session = test.Start();
        test.TakeSent();
        gear = session.Item.Inventory.Get(gear.Uid)!;
        gear.Socket = new ItemSocket(3, 1);
        return session;
    }

    private void Equip(GameSession session, byte slot) {
        new ItemSocketHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(8);
            writer.WriteLong(gear.Uid);
            writer.WriteLong(gem.Uid);
            writer.WriteByte(slot);
        }));
    }

    private void Unequip(GameSession session, byte slot) {
        new ItemSocketHandler { ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.WriteLong(gear.Uid);
            writer.WriteByte(slot);
        }));
    }

    private string[] Sent() {
        return test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemSocketSystem).Select(Convert.ToHexString).ToArray();
    }

    [Test]
    public void PutOn_TheFirstLockedSlot_IsRefusedAsLocked() {
        GameSession session = Start();

        Equip(session, 1);

        Assert.That(Sent(), Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_lock).ToArray()) }));
        Assert.That(session.Item.Inventory.Get(gem.Uid, InventoryType.Gemstone), Is.Not.Null);
        Assert.That(gear.Socket!.Sockets, Has.Length.EqualTo(1));
    }

    [Test]
    public void PutOn_PastTheLastSlot_IsRefused() {
        GameSession session = Start();

        Equip(session, 3);

        Assert.That(Sent(), Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.Error(code: 8).ToArray()) }));
        Assert.That(session.Item.Inventory.Get(gem.Uid, InventoryType.Gemstone), Is.Not.Null);
    }

    [Test]
    public void TakeOff_TheFirstLockedSlot_IsRefusedAsLocked() {
        GameSession session = Start();

        Unequip(session, 1);

        Assert.That(Sent(), Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_lock).ToArray()) }));
    }

    [Test]
    public void TakeOff_PastTheLastSlot_IsRefused() {
        GameSession session = Start();

        Unequip(session, 3);

        Assert.That(Sent(), Is.EqualTo(new[] { Convert.ToHexString(ItemSocketPacket.Error(code: 10).ToArray()) }));
    }
}
