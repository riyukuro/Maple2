using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Util;

// A player who arrives by portal is invincible for UserPortalInvincibleTick (5000 ms in the server constants): the
// client reads no body for InvincibleEffect (CSceneStateGame::OnInvincibleEffect) and ends the effect only when the
// time is over.
public class PortalInvincibleTests {
    [Test]
    public void InvincibleEffect_IsTheOpcodeOnly() {
        ByteWriter packet = PortalPacket.InvincibleEffect();
        Assert.That(packet.Length, Is.EqualTo(2));
        var reader = new ByteReader(packet.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.InvincibleEffect));
    }

    [TestCase(10_000, false)]
    [TestCase(14_999, false)]
    [TestCase(15_000, true)]
    [TestCase(20_000, true)]
    public void PortalWindow(long tick, bool takes) {
        long until = FieldPlayer.InvincibleUntil(0, 10_000, 5000);
        Assert.That(FieldPlayer.TakesDamage(false, tick, until), Is.EqualTo(takes));
    }

    [Test]
    public void ANewWindowDoesNotShortenARunningOne() {
        Assert.That(FieldPlayer.InvincibleUntil(30_000, 10_000, 5000), Is.EqualTo(30_000));
        Assert.That(FieldPlayer.InvincibleUntil(12_000, 10_000, 5000), Is.EqualTo(15_000));
    }
}
