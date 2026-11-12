using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The bag expansion coupon (item function ExpandInven) through ItemUseHandler.Handle.
public class ExpandInventoryHandlerTests {
    private static readonly ItemMetadata Coupon = TestGameSession.Metadata(20300046,
        function: new ItemMetadataFunction(ItemFunction.ExpandInven, "ExpandInven", "6,misc", false), type: 2);

    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;
    private Item coupon = null!;

    private GameSession Login(long meret, short[]? miscCount = null) {
        test = new TestGameSession();
        if (miscCount != null) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = miscCount });
        }
        coupon = test.Store(Coupon, amount: 2);
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        session.Currency.Meret = meret;
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session) => handler.Handle(session, TestGameSession.Packet(writer => writer.WriteLong(coupon.Uid)));

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Use_ExpandsTheTabAndConsumesOneCoupon() {
        GameSession session = Login(meret: 1000);
        short before = session.Item.Inventory.FreeSlots(InventoryType.Misc);

        Use(session);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(CurrencyPacket.UpdateMeret(session.Player.Value.Currency, -390)),
            Hex(ItemInventoryPacket.ExpandCount(InventoryType.Misc, 6)),
            Hex(ItemInventoryPacket.ExpandComplete()),
            Hex(ItemUsePacket.ExpandInventory()),
            Hex(ItemInventoryPacket.UpdateAmount(coupon.Uid, 1)),
        }));
        Assert.That(session.Currency.Meret, Is.EqualTo(1000 - 390));
        Assert.That(session.Player.Value.Unlock.Expand[InventoryType.Misc], Is.EqualTo(6));
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Misc), Is.EqualTo(before + 6));
        Assert.That(session.Item.Inventory.Get(coupon.Uid)?.Amount, Is.EqualTo(1));
    }

    [Test]
    public void Use_WithoutMeretKeepsTheCoupon() {
        GameSession session = Login(meret: 389);
        short before = session.Item.Inventory.FreeSlots(InventoryType.Misc);

        Use(session);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ItemInventory, SendOp.ItemUse }));
        Assert.That(session.Currency.Meret, Is.EqualTo(389));
        Assert.That(session.Player.Value.Unlock.Expand[InventoryType.Misc], Is.EqualTo(0));
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Misc), Is.EqualTo(before));
        Assert.That(session.Item.Inventory.Get(coupon.Uid)?.Amount, Is.EqualTo(2));
    }

    [Test]
    public void Use_AtTheTabMaximumKeepsTheCouponAndTheMeret() {
        GameSession session = Login(meret: 1000, miscCount: [48, 0]);

        Use(session);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Notice, SendOp.ItemUse }));
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
        Assert.That(session.Item.Inventory.Get(coupon.Uid)?.Amount, Is.EqualTo(2));
    }
}
