using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class CharacterCreateRulesTests {
    [TestCase((byte) 0, true)]
    [TestCase((byte) 1, true)]
    [TestCase((byte) 2, false)]
    [TestCase((byte) 0xFF, false)]
    public void GenderByteOfCreatePacket(byte wire, bool expected) {
        // CharacterManagement 0x0009 sub-type 1 starts with u8 gender, i16 job.
        var packet = new ByteReader(new byte[] { wire, 10, 0 }, 0);
        var gender = packet.Read<Gender>();
        Assert.That(CharacterCreateRules.IsCreateGender(gender), Is.EqualTo(expected));
        Assert.That(packet.ReadShort(), Is.EqualTo(10));
    }

    [TestCase(0, 4, 8, true)]
    [TestCase(3, 4, 8, true)]
    [TestCase(4, 4, 8, false)]
    [TestCase(8, 20, 8, false)]
    [TestCase(7, 20, 8, true)]
    [TestCase(0, 0, 8, false)]
    public void FreeSlot(int owned, int accountMax, int serverMax, bool expected) {
        Assert.That(CharacterCreateRules.HasFreeSlot(owned, accountMax, serverMax), Is.EqualTo(expected));
    }
}
