using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class TradeRulesTests {
    // Trade 0x0049 mode 8 after the mode byte: i64 item uid, i32 amount, i32 trade slot.
    [TestCase(1, true)]
    [TestCase(100, true)]
    [TestCase(0, false)]
    [TestCase(-1, false)]
    public void AmountOfAddItem(int amount, bool expected) {
        var writer = new ByteWriter();
        writer.WriteLong(0x1122334455667788);
        writer.WriteInt(amount);
        writer.WriteInt(3);
        byte[] bytes = writer.ToArray();
        Assert.That(bytes.Length, Is.EqualTo(16));

        var packet = new ByteReader(bytes, 0);
        Assert.That(packet.ReadLong(), Is.EqualTo(0x1122334455667788));
        Assert.That(TradeRules.IsOfferAmount(packet.ReadInt()), Is.EqualTo(expected));
        Assert.That(packet.ReadInt(), Is.EqualTo(3));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [TestCase(TransferFlag.Trade | TransferFlag.Split, 0, false, true)]
    [TestCase(TransferFlag.Trade, 0, true, false)] // bound
    [TestCase(TransferFlag.None, 0, false, false)]
    [TestCase(TransferFlag.Split | TransferFlag.Bind, 0, false, false)]
    [TestCase(TransferFlag.LimitTrade, 1, false, true)]
    [TestCase(TransferFlag.LimitTrade, 0, false, false)]
    [TestCase(TransferFlag.LimitTrade | TransferFlag.Trade, 0, false, true)]
    [TestCase(TransferFlag.LimitTrade, 1, true, false)]
    public void Tradeable(TransferFlag flag, int remainTrades, bool bound, bool expected) {
        Assert.That(TradeRules.IsTradeable(flag, remainTrades, bound), Is.EqualTo(expected));
    }

    // CItemSocket::IsGemTradeable: only an unbound, unlocked gem of transfer type Tradable goes with the item.
    [TestCase(false, true, TransferType.Tradable, true)]
    [TestCase(true, true, TransferType.Tradable, false)]
    [TestCase(false, false, TransferType.Tradable, false)]
    [TestCase(false, true, TransferType.BindOnTrade, false)]
    [TestCase(false, true, TransferType.BlackMarketOnly, false)]
    [TestCase(false, true, TransferType.Untradeable, false)]
    public void GemTradeable(bool bound, bool usable, TransferType type, bool expected) {
        Assert.That(TradeRules.IsGemTradeable(bound, usable, type), Is.EqualTo(expected));
    }

    [Test]
    public void GemsTradeable() {
        TransferType? Type(int id) => id switch {
            1 => TransferType.Tradable,
            2 => TransferType.BindOnLoot,
            _ => null,
        };
        var socket = new ItemSocket(1, 3);
        Assert.That(TradeRules.AreGemsTradeable(null, Type, 100), Is.True);
        Assert.That(TradeRules.AreGemsTradeable(socket, Type, 100), Is.True); // empty slots
        socket.Sockets[0] = new ItemGemstone(1, stats: new ItemStats());
        socket.Sockets[1] = new ItemGemstone(3, stats: new ItemStats()); // no data: skipped
        Assert.That(TradeRules.AreGemsTradeable(socket, Type, 100), Is.True);
        socket.Sockets[0]!.UnlockTime = 200;
        Assert.That(TradeRules.AreGemsTradeable(socket, Type, 100), Is.False);
        socket.Sockets[0]!.UnlockTime = 0;
        socket.Sockets[0]!.Binding = new ItemBinding(5, "a");
        Assert.That(TradeRules.AreGemsTradeable(socket, Type, 100), Is.False);
        socket.Sockets[0]!.Binding = new ItemBinding(0, "");
        Assert.That(TradeRules.AreGemsTradeable(socket, Type, 100), Is.True);
        socket.Sockets[2] = new ItemGemstone(2, stats: new ItemStats());
        Assert.That(TradeRules.AreGemsTradeable(socket, Type, 100), Is.False);
    }
}
