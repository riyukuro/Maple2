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

    // Each int is how long the intro movie stays (ms; addMovie's setTimeout to onTimeOut, which plays "out"), so
    // 0 hides it at once. 0x1b: the client passes its two ints to onRound the other way round; onRound draws its
    // first argument as the round and times the movie with its second, so the wire has the time, then the round.
    [Test]
    public void Intros() {
        Assert.That(Hex(PvpPacket.RankDuelRound(1)), Is.EqualTo("B600" + "1B" + "C4090000" + "01000000"));
        Assert.That(Hex(PvpPacket.RankDuelRound(12, 1000)), Is.EqualTo("B600" + "1B" + "E8030000" + "0C000000"));
        Assert.That(Hex(PvpPacket.RankDuelFight()), Is.EqualTo("B600" + "1A" + "DC050000"));
        Assert.That(Hex(PvpPacket.RankDuelWin()), Is.EqualTo("B600" + "1C" + "B80B0000"));
        Assert.That(Hex(PvpPacket.RankDuelLose()), Is.EqualTo("B600" + "1D" + "B80B0000"));
        Assert.That(Hex(PvpPacket.RankDuelDraw()), Is.EqualTo("B600" + "1E" + "B80B0000"));
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
