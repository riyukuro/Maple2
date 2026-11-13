using System;
using System.Numerics;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// ResponseCube mode 0xa (PlaceLiftable) tail against the client's reader (scenestategame.cpp:9995-10011): int, then a
// byte notice flag; with the flag 1, a unicode string (ReadUnicodeString) and a byte type.
public class CubePlaceLiftableNoticeTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    private static LiftableCube Cube() => new(new Liftable(50200001, 1, 0, 0, 0, "", "", "", "", false, Vector3.Zero, Vector3.Zero));

    [Test]
    public void ABoundLiftableEndsWithAnEmptyUnicodeNoticeAndItsType() {
        byte[] bound = Body(CubePacket.PlaceLiftable(1, Cube(), Vector3.Zero, 0f, isBound: true));
        byte[] free = Body(CubePacket.PlaceLiftable(1, Cube(), Vector3.Zero, 0f));
        Assert.Multiple(() => {
            Assert.That(free[^1], Is.EqualTo(0x00)); // no notice
            Assert.That(bound.Length, Is.EqualTo(free.Length + 3));
            Assert.That(bound[^4..], Is.EqualTo(new byte[] { 0x01, 0x00, 0x00, 0x00 })); // flag, length 0, type
        });
    }
}
