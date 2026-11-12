using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The bank and black market call items (item functions OpenBank and CallBlackMarket): at the end of the client's
// cash call the item use comes with the price, "0", as text. The answer opens the window and the pass is kept.
public class CashCallItemHandlerTests {
    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;
    private Item pass = null!;

    private GameSession Start(ItemFunction function, int itemId, bool marketAllowed = true) {
        test = new TestGameSession();
        pass = test.Store(TestGameSession.Metadata(itemId,
            function: new ItemMetadataFunction(function, function.ToString(), string.Empty, false), type: 10, slotMax: 1));
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023) with { CashCall = new MapMetadataCashCall(true, true, true, marketAllowed, true) });
        test.TakeSent();
        return session;
    }

    private void Use(GameSession session) => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteLong(pass.Uid);
        writer.WriteUnicodeString("0");
    }));

    private static void Kill(GameSession session) {
        typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("isDead",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(session.Player, true);
    }

    // StorageInventory 14 (CStore::OnReset): the client clears its copy and opens the bank, which then asks for it.
    [Test]
    public void OpenBank_OpensTheBank() {
        GameSession session = Start(ItemFunction.OpenBank, 20300087);

        Use(session);

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(StorageInventoryPacket.OpenDialog().ToArray()) }));
        Assert.That(Convert.ToHexString(StorageInventoryPacket.OpenDialog().ToArray()[2..]), Is.EqualTo("0E"));
        Assert.That(session.Item.Inventory.Get(pass.Uid)!.Amount, Is.EqualTo(1));
    }

    // BlackMarket 9 (CBlackMarketMan::OnBlackMarket) opens the black market window.
    [Test]
    public void CallBlackMarket_OpensTheMarket() {
        GameSession session = Start(ItemFunction.CallBlackMarket, 20300009);

        Use(session);

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(BlackMarketPacket.Open().ToArray()) }));
        Assert.That(Convert.ToHexString(BlackMarketPacket.Open().ToArray()), Is.EqualTo(Convert.ToHexString(
            new[] { (byte) ((ushort) SendOp.BlackMarket & 0xff), (byte) ((ushort) SendOp.BlackMarket >> 8), (byte) 9 })));
        Assert.That(session.Item.Inventory.Get(pass.Uid)!.Amount, Is.EqualTo(1));
    }

    // A field with the cashCall market flag off (cashCallMarketProhibit) gets no market; the bank still opens there.
    [Test]
    public void CallBlackMarket_FieldProhibits() {
        GameSession session = Start(ItemFunction.CallBlackMarket, 20300009, marketAllowed: false);

        Use(session);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [TestCase(ItemFunction.OpenBank, 20300087)]
    [TestCase(ItemFunction.CallBlackMarket, 20300009)]
    public void Dead_GetsNothing(ItemFunction function, int itemId) {
        GameSession session = Start(function, itemId);
        Kill(session);

        Use(session);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Item.Inventory.Get(pass.Uid)!.Amount, Is.EqualTo(1));
    }

    [TestCase(false, true, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    public void Rules(bool dead, bool marketAllowed, bool market) {
        Assert.That(CashCallRules.CanCallBlackMarket(dead, marketAllowed), Is.EqualTo(market));
        Assert.That(CashCallRules.CanOpenBank(dead), Is.EqualTo(!dead));
    }
}
