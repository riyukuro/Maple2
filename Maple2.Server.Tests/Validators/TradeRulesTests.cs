using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class TradeRulesTests {
    // Trade 0, a request: a block either way and oneself are answered with 6, past TradeDistance with 1.
    [TestCase(1L, 2L, false, false, 0f, null)]
    [TestCase(1L, 1L, false, false, 0f, TradeError.s_trade_error_decline)]
    [TestCase(1L, 2L, true, false, 0f, TradeError.s_trade_error_decline)]
    [TestCase(1L, 2L, false, true, 0f, TradeError.s_trade_error_decline)]
    [TestCase(1L, 2L, false, false, 500f, null)]
    [TestCase(1L, 2L, false, false, 501f, TradeError.s_trade_error_distance)]
    [TestCase(1L, 2L, true, false, 900f, TradeError.s_trade_error_decline)]
    public void Request(long requesterId, long targetId, bool blockedByRequester, bool blockedByTarget, float distance, TradeError? expected) {
        Assert.That(TradeRules.CheckRequest(requesterId, targetId, blockedByRequester, blockedByTarget,
            new Vector3(100, 200, 300), new Vector3(100 + distance, 200, 300), 500), Is.EqualTo(expected));
    }

    [Test]
    public void RequestDistanceIn3D() {
        // 300, 400 and 0 apart is 500: still near enough; 150 higher as well is past it.
        Assert.That(TradeRules.CheckRequest(1, 2, false, false, Vector3.Zero, new Vector3(300, 400, 0), 500), Is.Null);
        Assert.That(TradeRules.CheckRequest(1, 2, false, false, Vector3.Zero, new Vector3(300, 400, 150), 500),
            Is.EqualTo(TradeError.s_trade_error_distance));
    }

    [Test]
    public void RequestWithNoDistanceLimit() {
        // A constants table without TradeDistance reads 0: no distance limit then.
        Assert.That(TradeRules.CheckRequest(1, 2, false, false, Vector3.Zero, new Vector3(100000, 0, 0), 0), Is.Null);
    }

    [Test]
    public void RequestErrorPacket() {
        // OnTradeRequestFailed after the Trade sub-type: the code byte, the name (the text's {0}), an int and the level int.
        byte[] bytes = TradePacket.Error(TradeError.s_trade_error_distance, name: "Ab").ToArray();
        var packet = new ByteReader(bytes, 0);
        packet.ReadShort(); // opcode
        packet.ReadByte(); // sub-type
        Assert.That(packet.ReadByte(), Is.EqualTo(1));
        Assert.That(packet.ReadUnicodeString(), Is.EqualTo("Ab"));
        Assert.That(packet.ReadInt(), Is.EqualTo(0));
        Assert.That(packet.ReadInt(), Is.EqualTo(0));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

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

    // The fee the receiver pays (FUN_14087d150): mesos * percent / 100 in whole numbers, times (1 - the receiver's
    // reduce rate), cut to a whole number and never below 0.
    [TestCase(1000L, 10, 0f, 100L)]
    [TestCase(1000L, 10, 0.25f, 75L)]
    [TestCase(999L, 10, 0f, 99L)]
    [TestCase(1L, 10, 0f, 0L)]
    [TestCase(0L, 10, 0f, 0L)]
    [TestCase(1000L, 10, 1.5f, 0L)]
    [TestCase(1000L, 0, 0f, 0L)]
    public void Fee(long mesos, int percent, float rate, long expected) {
        Assert.That(TradeRules.Fee(mesos, percent, rate), Is.EqualTo(expected));
    }

    // Mode 10: 0 takes the offer back; any other amount lies in TradeMinMeso..TradeMaxMeso (FUN_14087d200).
    [TestCase(0L, true)]
    [TestCase(99L, false)]
    [TestCase(100L, true)]
    [TestCase(1000L, true)]
    [TestCase(1001L, false)]
    public void MesoOffer(long amount, bool expected) {
        Assert.That(TradeRules.IsMesoOffer(amount, 100, 1000), Is.EqualTo(expected));
    }
}
