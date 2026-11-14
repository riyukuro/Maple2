using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RecvOp 0x8F ItemCompare, the GM item window's compare button: the repeat tool's request with its count of 100. A GM
// with SpawnItem gets the rolls through 0x101 ItemOptionSimulator (start, one batch of 100, end) and no item; any other
// account gets nothing. The item id is made up.
public class ItemCompareHandlerTests {
    private const int ItemId = 11300001;

    private static List<byte[]> Compare(AdminPermissions permissions, int count = 100, byte rank = 4) {
        var test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ItemId, EquipSlot.CP, type: 1, slotMax: 1));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(90000100));
        session.Player.AdminPermissions = permissions;
        int bagBefore = session.Item.Inventory.Find(ItemId).Count();
        test.TakeSent();

        // CCCreateItemPacket::vfunc_0, then CCCompareItemPacket's int.
        new ItemCompareHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteInt(ItemId);
            writer.WriteInt(1); // amount
            writer.WriteByte(rank);
            writer.WriteInt(0); // period
            writer.WriteInt(20); // level
            writer.WriteInt(5); // enchant
            writer.WriteBool(false);
            writer.WriteByte(0);
            writer.WriteLong(-1);
            writer.WriteInt(0);
            writer.WriteBytes(new byte[10]); // five empty option sets
            writer.WriteInt(count);
        }));
        Assert.That(session.Item.Inventory.Find(ItemId).Count(), Is.EqualTo(bagBefore), "no item is given");
        return test.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.ItemOptionSimulator).ToList();
    }

    [Test]
    public void AGmGetsTheRolls() {
        List<byte[]> sent = Compare(AdminPermissions.Admin);
        Assert.Multiple(() => {
            Assert.That(sent.Select(p => p[2]), Is.EqualTo(new byte[] { 0, 1, 2 }));
            Assert.That(BitConverter.ToInt32(sent[1], 3), Is.EqualTo(ItemId));
            Assert.That(BitConverter.ToInt32(sent[1], 7), Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(sent[1], 11), Is.EqualTo(100));
        });
    }

    [TestCase(AdminPermissions.None)]
    [TestCase(AdminPermissions.GameMaster)]
    public void WithoutGmAndSpawnItem_NothingIsSent(AdminPermissions permissions) {
        Assert.That(Compare(permissions), Is.Empty);
    }

    [TestCase(0, (byte) 4)]
    [TestCase(100, (byte) 7)]
    public void ABadCountOrRankIsRefused(int count, byte rank) {
        Assert.That(Compare(AdminPermissions.Admin, count, rank), Is.Empty);
    }
}
