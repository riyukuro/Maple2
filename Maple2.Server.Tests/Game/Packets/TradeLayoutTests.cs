using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Byte layouts of the trade and shop s2c writers, read in the client's order: CTradeManagerEx::OnTrade
// (trademanagerex_1.cpp), UIShopDialog::OnShopPacket (uishopdialog.cpp), CSceneStateGame::OnSystemShop,
// the BlackMarket error and the MesoMarket buy result.
public class TradeLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op, byte mode) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(op));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    [Test]
    public void TradeComplete_KeepsTheTrailingSideByte() {
        // FUN_14185d720 reads nothing after the mode; KMS2 sends the side byte anyway (FORK.md), so it stays.
        ByteReader reader = Read(TradePacket.Complete(true), SendOp.Trade, 13);
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void TradeError_ReadsCodeNameIntAndLevel() {
        ByteReader reader = Read(TradePacket.Error(TradeError.s_trade_error_distance, "Name", 7, 60), SendOp.Trade, 1);
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) TradeError.s_trade_error_distance));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Name"));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(60));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void TradeRemoveAndMesos_ReadSideThenValues() {
        ByteReader reader = Read(TradePacket.RemoveItem(true, 3, 0x1122334455), SendOp.Trade, 9);
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x1122334455));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TradePacket.SetMesos(false, 123456789), SendOp.Trade, 10);
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadLong(), Is.EqualTo(123456789));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void ShopResultModes_MatchTheDialogReaders() {
        // ReadProductUpdate: serial, bought count.
        ByteReader reader = Read(ShopPacket.Update(4, 20), SendOp.Shop, 2);
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(20));
        Assert.That(reader.Available, Is.Zero);

        // ReadRepurchaseStart: an unsigned short count.
        reader = Read(ShopPacket.BuyBackItemCount(12), SendOp.Shop, 6);
        Assert.That(reader.ReadShort(), Is.EqualTo(12));
        Assert.That(reader.Available, Is.Zero);

        // ReadResetResult: the two ints follow only a set flag.
        reader = Read(ShopPacket.InstantRestock(), SendOp.Shop, 9);
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.Zero);
        reader = Read(ShopPacket.InstantRestock(true), SendOp.Shop, 9);
        Assert.That(reader.ReadBool(), Is.True);
        reader.ReadInt();
        reader.ReadInt();
        Assert.That(reader.Available, Is.Zero);

        // ReadShopError: int code, byte command, int value.
        reader = Read(ShopPacket.Error(ShopError.s_err_lack_shopitem, 4, 9), SendOp.Shop, 15);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) ShopError.s_err_lack_shopitem));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(9));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void SystemShop_ReadsTypeAndFlag() {
        ByteReader reader = Read(SystemShopPacket.Fishing(), SendOp.SystemShop, 4);
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void BlackMarketError_ReadsModeCodeListingAndTwoInts() {
        ByteReader reader = Read(BlackMarketPacket.Error(BlackMarketError.s_blackmarket_error_fail_register, 2, 99, 5, 6), SendOp.BlackMarket, 0);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) BlackMarketError.s_blackmarket_error_fail_register));
        Assert.That(reader.ReadLong(), Is.EqualTo(99));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(6));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void MesoMarketPurchase_ReadsCodeListingAndMonthlyCount() {
        ByteReader reader = Read(MesoMarketPacket.Purchase(77, 3), SendOp.MesoMarket, 8);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) MesoMarketError.none));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.Zero);
    }

    // CTradeManagerEx::OnTrade modes: 0 (OnTradeRequest) unicode name and int64 character id; 2 (request sent) and
    // 14 (FUN_14185d750) read nothing; 4 (FUN_14185cc60) the unicode name; 5 (OnTradeStarted) an int64 id; 6
    // (OnTradeFinished) a byte; 11 and 12 (confirm and unconfirm) the side byte.
    [Test]
    public void TradeRequestAcknowledgeDeclineStartEndAndLatch_FollowTheReaders() {
        var test = new TestGameSession();
        test.Start();
        Player player = test.Session.Player.Value;

        ByteReader reader = Read(TradePacket.Request(player), SendOp.Trade, 0);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo(player.Character.Name));
        Assert.That(reader.ReadLong(), Is.EqualTo(player.Character.Id));
        Assert.That(reader.Available, Is.Zero);

        Assert.That(Read(TradePacket.Acknowledge(), SendOp.Trade, 2).Available, Is.Zero);
        Assert.That(Read(TradePacket.AlreadyRequest(), SendOp.Trade, 14).Available, Is.Zero);

        reader = Read(TradePacket.Decline("Other"), SendOp.Trade, 4);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Other"));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TradePacket.StartTrade(0x1122334455), SendOp.Trade, 5);
        Assert.That(reader.ReadLong(), Is.EqualTo(0x1122334455));
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TradePacket.EndTrade(true), SendOp.Trade, 6);
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TradePacket.Finalize(false), SendOp.Trade, 11);
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.Zero);

        reader = Read(TradePacket.UnFinalize(true), SendOp.Trade, 12);
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Available, Is.Zero);
    }

    // OnTradeItemAdded (:561): byte side, int item id, int64 uid, int rarity, int slot (SetSlot), int count
    // (SetCountClamped), int trade slot, then the item body.
    [Test]
    public void TradeAddItem_FollowsOnTradeItemAdded() {
        var item = new Item(TestGameSession.Metadata(20000001)) { Uid = 0x0102030405, Rarity = 3, Slot = 4, Amount = 7 };
        ByteReader reader = Read(TradePacket.AddItem(true, item), SendOp.Trade, 8);
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
    }
}
