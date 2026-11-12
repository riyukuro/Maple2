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
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The bank and black market call items (item functions OpenBank and CallBlackMarket): at the end of the client's
// cash call the item use comes with the price, "0", as text. The answer opens the window and the pass is kept.
public class CashCallItemHandlerTests {
    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;
    private Item pass = null!;

    private GameSession Start(ItemFunction function, int itemId, bool marketAllowed = true, bool medicAllowed = true,
                              int slotMax = 1, int amount = 1) {
        test = new TestGameSession();
        pass = test.Store(TestGameSession.Metadata(itemId,
            function: new ItemMetadataFunction(function, function.ToString(), string.Empty, false), type: 10, slotMax: slotMax), amount: amount);
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023) with { CashCall = new MapMetadataCashCall(true, true, medicAllowed, marketAllowed, true) });
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

    // UseItem's CanUseItem: an expired pass is refused by the client (notice 0xe1) and opens nothing here either.
    [TestCase(ItemFunction.OpenBank, 20300087)]
    [TestCase(ItemFunction.CallBlackMarket, 20300009)]
    public void Expired_GetsNothing(ItemFunction function, int itemId) {
        GameSession session = Start(function, itemId);
        session.Item.Inventory.Get(pass.Uid)!.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1;

        Use(session);

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(session.Item.Inventory.Get(pass.Uid)!.Amount, Is.EqualTo(1));
    }

    // A pass with a term that has not run out still opens.
    [Test]
    public void NotYetExpired_Opens() {
        GameSession session = Start(ItemFunction.OpenBank, 20300087);
        session.Item.Inventory.Get(pass.Uid)!.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;

        Use(session);

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(StorageInventoryPacket.OpenDialog().ToArray()) }));
    }

    // CItem::FUN_140528980: expired items are not used, except pets (data type 11) and furnishings with a maid id.
    [TestCase(false, 10, 0, true)]
    [TestCase(true, 10, 0, false)]
    [TestCase(true, 11, 0, true)]
    [TestCase(true, 10, 5, true)]
    public void ExpiryAllowsUse(bool expired, int propertyType, int maidId, bool expected) {
        Assert.That(ItemLockRules.ExpiryAllowsUse(expired, propertyType, maidId), Is.EqualTo(expected));
    }

    [TestCase(false, true, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    public void Rules(bool dead, bool marketAllowed, bool market) {
        Assert.That(CashCallRules.CanCallBlackMarket(dead, marketAllowed), Is.EqualTo(market));
        Assert.That(CashCallRules.CanOpenBank(dead), Is.EqualTo(!dead));
    }

    private GameSession StartMedic(bool medicAllowed = true, int deaths = 2, int msRemaining = 600000) {
        GameSession session = Start(ItemFunction.CallMedic, 20300011, medicAllowed: medicAllowed, slotMax: 999, amount: 3);
        // The test session's config manager is not loaded; it needs only its session here.
        typeof(Maple2.Server.Game.Manager.ConfigManager).GetField("session",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(session.Config, session);
        session.Config.SetDeathPenalty(new DeathInfo { Count = deaths, MsRemaining = msRemaining }, session.Field!.FieldTick);
        return session;
    }

    // The medic cures the death penalty: RevivalConfirm with end 0 and count 0 (the client plays its cure effect)
    // and one item of the stack is used.
    [Test]
    public void CallMedic_CuresThePenalty() {
        GameSession session = StartMedic();

        Use(session);

        Assert.That(session.Config.DeathCount, Is.EqualTo(0));
        Assert.That(session.Config.DeathPenaltyEndTick, Is.EqualTo(0));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.RevivalConfirm).Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(RevivalPacket.UpdatePenalty(session.Player.ObjectId, 0, 0).ToArray()) }));
        Assert.That(session.Item.Inventory.Get(pass.Uid)!.Amount, Is.EqualTo(2));
    }

    // The penalty time ran out but the count stays (it is cleared at the next death or revival): the client asks only
    // the count, casts the call, and the cure clears it.
    [Test]
    public void CallMedic_RunOutPenalty_IsCured() {
        GameSession session = StartMedic(msRemaining: -1000);

        Use(session);

        Assert.That(session.Config.DeathCount, Is.EqualTo(0));
        Assert.That(test.TakeSent().Count(packet => TestGameSession.Op(packet) == SendOp.RevivalConfirm), Is.EqualTo(1));
        Assert.That(session.Item.Inventory.Get(pass.Uid)!.Amount, Is.EqualTo(2));
    }

    // No penalty count, a field with the medic flag off, or a dead player: nothing is cured or used.
    [TestCase("none")]
    [TestCase("field")]
    [TestCase("dead")]
    public void CallMedic_Refused(string why) {
        GameSession session = why switch {
            "none" => StartMedic(deaths: 0, msRemaining: 0),
            "field" => StartMedic(medicAllowed: false),
            _ => StartMedic(),
        };
        if (why == "dead") {
            Kill(session);
        }
        short deaths = session.Config.DeathCount;

        Use(session);

        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.RevivalConfirm), Is.Empty);
        Assert.That(session.Config.DeathCount, Is.EqualTo(deaths));
        Assert.That(session.Item.Inventory.Get(pass.Uid)!.Amount, Is.EqualTo(3));
    }

    [TestCase(false, true, true, true)]
    [TestCase(true, true, true, false)]
    [TestCase(false, false, true, false)]
    [TestCase(false, true, false, false)]
    public void MedicRules(bool dead, bool penalty, bool medicAllowed, bool expected) {
        Assert.That(CashCallRules.CanCallMedic(dead, penalty, medicAllowed), Is.EqualTo(expected));
    }
}
