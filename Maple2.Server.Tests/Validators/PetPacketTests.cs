using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class PetPacketTests {
    // The pet answer's mode 15: one bool, the client's pet auto flag.
    [TestCase(true, (byte) 1)]
    [TestCase(false, (byte) 0)]
    public void AutoChangedBytes(bool auto, byte expected) {
        byte[] bytes = PetPacket.AutoChanged(auto).ToArray();
        Assert.That(bytes.Length, Is.EqualTo(2 + 1 + 1));
        Assert.That(bytes[2], Is.EqualTo(15));
        Assert.That(bytes[3], Is.EqualTo(expected));
    }
}
