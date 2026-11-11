using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

// "/petevolutionpoint <uid> <point>" (tabcontentspetevolution.cpp FUN_140acb560) sets the points; the client
// reads the answer in pet mode 0x12 (petmanager.cpp FUN_140c8a640): int owner id, int points, long pet uid.
public class PetEvolvePointsTests {
    [Test]
    public void Reply_IsPetMode18() {
        ByteWriter writer = PetPacket.EvolvePoints(0x11223344, 80, 0x0102030405060708);
        byte[] body = writer.Buffer.AsSpan(writer.Length - 17, 17).ToArray();

        Assert.That(body, Is.EqualTo(new byte[] {
            0x12, // mode
            0x44, 0x33, 0x22, 0x11, // owner object id
            0x50, 0x00, 0x00, 0x00, // points
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, // pet item uid
        }));
        Assert.That(writer.Length, Is.EqualTo(2 + 17)); // opcode, then nothing else
    }

    [Test]
    public void Points_AreClamped() {
        Assert.That(PetEvolveRules.ClampPoints(-5), Is.EqualTo(0));
        Assert.That(PetEvolveRules.ClampPoints(0), Is.EqualTo(0));
        Assert.That(PetEvolveRules.ClampPoints(30), Is.EqualTo(30));
        Assert.That(PetEvolveRules.ClampPoints(4294967295L), Is.EqualTo(int.MaxValue)); // the client formats an unsigned
        Assert.That(PetEvolveRules.ClampPoints(30, needed: 80), Is.EqualTo(30));
        Assert.That(PetEvolveRules.ClampPoints(200, needed: 80), Is.EqualTo(80));
        Assert.That(PetEvolveRules.ClampPoints(200, needed: 0), Is.EqualTo(200));
    }
}
