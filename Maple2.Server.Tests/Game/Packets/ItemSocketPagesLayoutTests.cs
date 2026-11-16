using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// ItemSocketSystem (0xDE) against the socket unlock page's ItemSocketSystemPageSocketUnlock::vfunc_5
// (unit_140f7d060_itemsocketsystempagesocketunlock.cpp:312): u8 operation; 3 (select item result): Unk_SockMsgF
// (FUN_140629d60, itemsocketsystemutil.cpp:1056: long, u8, long item uid, float success rate); 16: no body is read
// (FUN_140f826b0 refreshes the ingredient prices); 18: ItemSocketSystemPageBase::vfunc_8
// (unit_140f6c680_itemsocketsystempagesbase.cpp:456): u8 operation, int result code.
// ItemSocketScroll (0xE3) against UIItemSocketScrollDialog::OnItemSocketScrollPacket (uiitemsocketscrolldialog.cpp:195):
// u8 type; 0 (FUN_140629fa0): long scroll uid, int rate (shown divided by 100), u8 socket count; 2 (FUN_14062a230):
// u8 result, long item uid, u8, then for result 1: int trade count and the socket block; 3 (FUN_140f67650): u8, int code.
public class ItemSocketPagesLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    [Test]
    public void ItemSocketSystem_Stage16And18() {
        var item = new Item(TestGameSession.Metadata(11300001), 4, 1) { Uid = 8001 };
        ByteReader reader = Read(ItemSocketPacket.StageUnlockSocket(item, 0.5f), SendOp.ItemSocketSystem);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(0xFF));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadFloat(), Is.EqualTo(0.5f));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemSocketPacket.Unknown(), SendOp.ItemSocketSystem);
        Assert.That(reader.ReadByte(), Is.EqualTo(16));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemSocketPacket.Error(2, ItemSocketError.s_itemsocketsystem_error_socket_unlock_all), SendOp.ItemSocketSystem);
        Assert.That(reader.ReadByte(), Is.EqualTo(18));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) ItemSocketError.s_itemsocketsystem_error_socket_unlock_all));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void ItemSocketScroll() {
        var scroll = new Item(TestGameSession.Metadata(20000001), 1, 1) { Uid = 7001 };
        var metadata = new ItemSocketScrollMetadata(1, 99, [], [], 2, 0, 8500);
        ByteReader reader = Read(ItemSocketScrollPacket.UseScroll(scroll, metadata), SendOp.ItemSocketScroll);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(7001));
        Assert.That(reader.ReadInt(), Is.EqualTo(8500));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.EqualTo(0));

        var gear = new Item(TestGameSession.Metadata(11300001), 4, 1) { Uid = 8001, Socket = ItemSocket.Default };
        reader = Read(ItemSocketScrollPacket.Unlock(gear, false), SendOp.ItemSocketScroll);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(ItemSocketScrollPacket.Unlock(gear, true), SendOp.ItemSocketScroll);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(8001));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        var socket = new ByteWriter();
        socket.WriteClass<ItemSocket>(ItemSocket.Default);
        Assert.That(reader.ReadBytes(reader.Available), Is.EqualTo(socket.ToArray()));

        reader = Read(ItemSocketScrollPacket.Error(ItemSocketScrollError.s_itemsocket_scroll_error_invalid_target), SendOp.ItemSocketScroll);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) ItemSocketScrollError.s_itemsocket_scroll_error_invalid_target));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
