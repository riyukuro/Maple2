using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class PetRulesTests {
    // RequestPet 4 after the mode byte: the new name.
    private static bool Read(byte[] body) {
        var packet = new ByteReader(body, 0);
        return PetRules.IsName(packet.ReadUnicodeString());
    }

    private static byte[] Body(string name) {
        var writer = new ByteWriter();
        writer.WriteUnicodeString(name);
        return writer.ToArray();
    }

    [Test]
    public void NameIsOneToThirtyOneCharacters() {
        // "Bo": 02 00 and two wide characters.
        Assert.That(Read(new byte[] { 0x02, 0x00, 0x42, 0x00, 0x6F, 0x00 }), Is.True);
        // Empty name: 00 00.
        Assert.That(Read(new byte[] { 0x00, 0x00 }), Is.False);
        Assert.That(Read(Body(new string('a', 31))), Is.True);
        Assert.That(Read(Body(new string('a', 32))), Is.False);
        Assert.That(Read(Body(new string('a', 4000))), Is.False);
    }

    [Test]
    public void FirstRenameIsFreeLaterOnesCost() {
        Assert.That(PetRules.RenameCost(1, 100), Is.EqualTo(0));
        Assert.That(PetRules.RenameCost(0, 100), Is.EqualTo(100));
        Assert.That(PetRules.RenameCost(-1, 100), Is.EqualTo(100));
    }
}
