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

// The bag expansion: by coupon (item function ExpandInven) through ItemUseHandler.Handle, which costs the coupon
// only, and by the bag window's Meret button through ItemInventoryHandler.Handle.
public class ExpandInventoryHandlerTests {
    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;
    private Item coupon = null!;

    private GameSession Login(long meret, short[]? miscCount = null, string parameters = "6,misc") {
        test = new TestGameSession();
        if (miscCount != null) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = miscCount });
        }
        coupon = test.Store(TestGameSession.Metadata(20300046,
            function: new ItemMetadataFunction(ItemFunction.ExpandInven, "ExpandInven", parameters, false), type: 2), amount: 2);
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        session.Currency.Meret = meret;
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session) => handler.Handle(session, TestGameSession.Packet(writer => writer.WriteLong(coupon.Uid)));

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    // The coupon is the price: no Meret is taken, and none is needed. The client shows its "expanded" message for
    // the ItemUse answer, so ExpandComplete (the same message) is not sent.
    [TestCase(1000)]
    [TestCase(0)]
    public void Coupon_ExpandsTheTabForNoMeret(long meret) {
        GameSession session = Login(meret);
        short before = session.Item.Inventory.FreeSlots(InventoryType.Misc);

        Use(session);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(ItemInventoryPacket.ExpandCount(InventoryType.Misc, 6)),
            Hex(ItemUsePacket.ExpandInventory()),
            Hex(ItemInventoryPacket.UpdateAmount(coupon.Uid, 1)),
        }));
        Assert.That(session.Currency.Meret, Is.EqualTo(meret));
        Assert.That(session.Player.Value.Unlock.Expand[InventoryType.Misc], Is.EqualTo(6));
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Misc), Is.EqualTo(before + 6));
        Assert.That(session.Item.Inventory.Get(coupon.Uid)?.Amount, Is.EqualTo(1));
    }

    [Test]
    public void Coupon_AtTheTabMaximumIsKept() {
        GameSession session = Login(meret: 1000, miscCount: [48, 0]);

        Use(session);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Notice, SendOp.ItemUse }));
        Assert.That(session.Currency.Meret, Is.EqualTo(1000));
        Assert.That(session.Item.Inventory.Get(coupon.Uid)?.Amount, Is.EqualTo(2));
    }

    // The data names the consumable tab "activeskill"; it is not the Misc tab.
    [Test]
    public void Coupon_ExpandsTheTabItNames() {
        GameSession session = Login(meret: 0, parameters: "6,activeskill");

        Use(session);

        Assert.That(Convert.ToHexString(test.TakeSent()[0]), Is.EqualTo(Hex(ItemInventoryPacket.ExpandCount(InventoryType.Consumable, 6))));
        Assert.That(session.Player.Value.Unlock.Expand[InventoryType.Consumable], Is.EqualTo(6));
        Assert.That(session.Player.Value.Unlock.Expand.TryGetValue(InventoryType.Misc, out short misc) ? misc : 0, Is.EqualTo(0));
    }

    [Test]
    public void Coupon_WithAnUnknownTabNameDoesNothing() {
        GameSession session = Login(meret: 0, parameters: "6,bank");

        Use(session);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Item.Inventory.Get(coupon.Uid)?.Amount, Is.EqualTo(2));
    }

    // The client's tab names (CItemTabNameTable), tab 0 to 14.
    [Test]
    public void TabNames() {
        string[] names = ["game", "skin", "summon", "material", "life", "quest", "gem", "misc", "mastery", "pet", "activeskill", "coin", "badge", "lapenshard", "piece"];
        Assert.That(names.Select(name => (int?) ItemUseHandler.ExpandInventoryTab(name)?.ToClient()), Is.EqualTo(Enumerable.Range(0, 15).Select(tab => (int?) tab)));
        Assert.That(ItemUseHandler.ExpandInventoryTab("equip"), Is.EqualTo(InventoryType.Gear));
        Assert.That(ItemUseHandler.ExpandInventoryTab("gemstone"), Is.EqualTo(InventoryType.Gemstone));
        Assert.That(ItemUseHandler.ExpandInventoryTab("etc"), Is.EqualTo(InventoryType.Misc));
        Assert.That(ItemUseHandler.ExpandInventoryTab("activeSkill"), Is.EqualTo(InventoryType.Consumable));
        Assert.That(ItemUseHandler.ExpandInventoryTab(""), Is.Null);
    }

    private static void PressExpand(GameSession session, InventoryType tab) {
        new ItemInventoryHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(9);
            writer.WriteByte(tab.ToClient());
        }));
    }

    // The Meret button, as KMS2 answers it: the Meret update, the tab's new count, then the completion.
    [Test]
    public void MeretButton_ChargesOneRowPrice() {
        GameSession session = Login(meret: 911);

        PressExpand(session, InventoryType.Gear);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(CurrencyPacket.UpdateMeret(session.Player.Value.Currency, -390)),
            Hex(ItemInventoryPacket.ExpandCount(InventoryType.Gear, 6)),
            Hex(ItemInventoryPacket.ExpandComplete()),
        }));
        Assert.That(session.Currency.Meret, Is.EqualTo(521));
        Assert.That(session.Player.Value.Unlock.Expand[InventoryType.Gear], Is.EqualTo(6));
    }

    [Test]
    public void MeretButton_WithoutMeretExpandsNothing() {
        GameSession session = Login(meret: 389);

        PressExpand(session, InventoryType.Gear);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ItemInventory }));
        Assert.That(session.Currency.Meret, Is.EqualTo(389));
        Assert.That(session.Player.Value.Unlock.Expand.TryGetValue(InventoryType.Gear, out short gear) ? gear : 0, Is.EqualTo(0));
    }
}
