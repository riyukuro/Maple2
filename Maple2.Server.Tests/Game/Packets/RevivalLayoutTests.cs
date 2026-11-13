using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// RevivalConfirm (CSceneStateGame::OnRevivalConfirm, scenestategame.cpp:7356): int object id, int penalty end tick
// (PC::FUN_1416619d0), int penalty count (PC::FUN_141661a60). Revival (OnRevival :7387): int object id, byte death
// flag (PC m_n10f0, 0 clears the revival block). RevivalCount (OnRevivalCount :4092): int meso revivals used today
// (MyPC::GetMesoRevivalRemainingCount subtracts it from the daily max).
public class RevivalLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    [Test]
    public void ConfirmIsIdEndCount() {
        ByteReader reader = Read(RevivalPacket.UpdatePenalty(5, 90000, 3), SendOp.RevivalConfirm);
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(90000));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void ReviveIsIdAndClearedFlag() {
        ByteReader reader = Read(RevivalPacket.Revive(5), SendOp.Revival);
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void CountIsOneInt() {
        ByteReader reader = Read(RevivalPacket.RevivalCount(2), SendOp.RevivalCount);
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
