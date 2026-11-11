using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Tests.Game.Packets;

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

    // The pet answer's mode 16 (OnOtherPetInfo): owner object id, item id, uid, rarity, then the item.
    [Test]
    public void PetInfoHeader() {
        Item pet = PetFusionRulesTests.ItemOf(0x1122334455, rarity: 3);
        var reader = new ByteReader(PetPacket.PetInfo(42, pet).ToArray(), 0);
        reader.ReadShort(); // opcode
        Assert.That(reader.ReadByte(), Is.EqualTo(16));
        Assert.That(reader.ReadInt(), Is.EqualTo(42));
        Assert.That(reader.ReadInt(), Is.EqualTo(pet.Id));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x1122334455));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.GreaterThan(0));
    }
}
