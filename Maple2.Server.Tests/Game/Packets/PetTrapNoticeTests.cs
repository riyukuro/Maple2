using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// ResponsePet 0x14: CPetManager::OnPetTrapSkillConsumed reads one int, the item id, and shows its name.
public class PetTrapNoticeTests {
    [Test]
    public void MasterSnareIsModeTwentyThenItemId() {
        var reader = new ByteReader(PetPacket.MasterSnare(63100000).ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponsePet));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x14));
        Assert.That(reader.ReadInt(), Is.EqualTo(63100000));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void SnareItemsArePetTraps() {
        // 63100000 and 63100500 are KR snare items whose skill is cast on a wild pet.
        Assert.That(new ItemType(63100000).IsPetTrap, Is.True);
        Assert.That(new ItemType(63100500).IsPetTrap, Is.True);
        Assert.That(new ItemType(20000001).IsPetTrap, Is.False);
        Assert.That(new ItemType(60000001).IsPetTrap, Is.False);
    }
}
