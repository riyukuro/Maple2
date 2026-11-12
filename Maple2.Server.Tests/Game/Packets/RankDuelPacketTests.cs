using System;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The rank duel packets (Pvp 0xB6) as CPvpRankDuelComponent::OnMessage (pvprankduelcomponent.cpp) and
// UIPvPRankDuelResultDialog::FUN_140f90b10 read them.
public class RankDuelPacketTests {
    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void QueueAnswers() {
        Assert.That(Hex(PvpPacket.RankDuelRegistered()), Is.EqualTo("B600" + "0E"));
        Assert.That(Hex(PvpPacket.RankDuelUnregistered()), Is.EqualTo("B600" + "0F"));
        Assert.That(Hex(PvpPacket.RankDuelMatchFound()), Is.EqualTo("B600" + "10" + "01"));
        Assert.That(Hex(PvpPacket.RankDuelMatchFailed()), Is.EqualTo("B600" + "10" + "00"));
        // CSceneStateGame::OnPvp 0x11 reads one int.
        Assert.That(Hex(PvpPacket.RankDuelInArenaField()), Is.EqualTo("B600" + "11" + "00000000"));
    }

    // 0x1b reads two ints and passes them to onRound the other way round: the rounds first, the round second.
    [Test]
    public void Intros() {
        Assert.That(Hex(PvpPacket.RankDuelRound(1, 3)), Is.EqualTo("B600" + "1B" + "03000000" + "01000000"));
        Assert.That(Hex(PvpPacket.RankDuelFight()), Is.EqualTo("B600" + "1A" + "00000000"));
        Assert.That(Hex(PvpPacket.RankDuelWin()), Is.EqualTo("B600" + "1C" + "00000000"));
        Assert.That(Hex(PvpPacket.RankDuelLose()), Is.EqualTo("B600" + "1D" + "00000000"));
        Assert.That(Hex(PvpPacket.RankDuelDraw()), Is.EqualTo("B600" + "1E" + "00000000"));
    }

    // The winner, then per side: name, character, score, rating, rating change, reward.
    [Test]
    public void Result() {
        var left = new PvpPacket.RankDuelSide("A", 0x11, 1, 1000, 0, 0);
        var right = new PvpPacket.RankDuelSide("Bc", 0x22, 0, 1000, -10, 5);
        Assert.That(Hex(PvpPacket.RankDuelResult(0x11, left, right)), Is.EqualTo("B600" + "12"
            + "1100000000000000"
            + "0100" + "4100" + "1100000000000000" + "01000000" + "E8030000" + "00000000" + "00000000"
            + "0200" + "42006300" + "2200000000000000" + "00000000" + "E8030000" + "F6FFFFFF" + "05000000"));
    }
}
