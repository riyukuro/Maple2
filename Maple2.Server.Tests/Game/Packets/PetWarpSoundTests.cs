using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Pet mode 2 (petmanager.cpp CPetManager::OnPetSummonSound): int owner object id and nothing else. KMS2 sends
// these 5 bytes to the owner when the summoned pet is put next to them.
public class PetWarpSoundTests {
    [Test]
    public void WarpSound_IsPetMode2WithTheOwner() {
        ByteWriter writer = PetPacket.WarpSound(0x11223344);
        byte[] body = writer.Buffer.AsSpan(writer.Length - 5, 5).ToArray();

        Assert.That(body, Is.EqualTo(new byte[] {
            0x02, // mode
            0x44, 0x33, 0x22, 0x11, // owner object id
        }));
        Assert.That(writer.Length, Is.EqualTo(2 + 5)); // opcode, then nothing else
    }
}
