using System.Numerics;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class BuddyEmoteStartTests {
    // Client: BuddyEmote mode 5 (networkservice.cpp:1150) reads int emote, 2 longs, 2 vectors, then the int it
    // hands to CEffectService::SetTick as the random effect seed.
    [Test]
    public void BuddyEmoteStartEndsWithTheEffectSeed() {
        ByteWriter writer = BuddyEmotePacket.Start(90200011, 5, 6, new Vector3(1, 2, 3), new Vector3(0, 0, 90), 0x01020304);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.BuddyEmote));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(90200011));
        Assert.That(reader.ReadLong(), Is.EqualTo(5));
        Assert.That(reader.ReadLong(), Is.EqualTo(6));
        Assert.That(reader.Read<Vector3>(), Is.EqualTo(new Vector3(1, 2, 3)));
        Assert.That(reader.Read<Vector3>(), Is.EqualTo(new Vector3(0, 0, 90)));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x01020304));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 8 + 8 + 12 + 12 + 4));
    }
}
