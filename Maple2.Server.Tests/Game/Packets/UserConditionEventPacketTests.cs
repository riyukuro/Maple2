using System;
using Maple2.Database.Extensions;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

public class UserConditionEventPacketTests {
    // The login packet as KMS2 sends it: opcode 0x00BD, mode 0, id 0.
    [Test]
    public void Load_ModeZeroAndNoId() {
        ByteWriter writer = UserConditionEventPacket.Load();
        Assert.That(Convert.ToHexString(writer.Buffer, 0, writer.Length), Is.EqualTo("BD00" + "00" + "00000000"));
    }

    // The play-time reward's step: both counts grow by the seconds since the last step, and the row is due when its
    // seconds reach its end.
    [Test]
    public void DtRewardStep_GrowsByTheElapsedSeconds() {
        Assert.That(GameEventManager.DtRewardStep(10, 40, 1, 1800), Is.EqualTo((11L, 41L, false)));
        Assert.That(GameEventManager.DtRewardStep(10, 40, 2, 1800), Is.EqualTo((12L, 42L, false)));
        Assert.That(GameEventManager.DtRewardStep(1799, 1799, 1, 1800), Is.EqualTo((1800L, 1800L, true)));
        Assert.That(GameEventManager.DtRewardStep(1798, 3000, 5, 1800), Is.EqualTo((1803L, 3005L, true)));
    }

    [Test]
    public void DtRewardExpiry_IsTheLastSecondOfTheDay() {
        var now = new DateTime(2021, 9, 5, 5, 1, 0);
        Assert.That(GameEventManager.DtRewardExpiry(now), Is.EqualTo(new DateTime(2021, 9, 5, 23, 59, 59).ToEpochSeconds()));
    }
}
