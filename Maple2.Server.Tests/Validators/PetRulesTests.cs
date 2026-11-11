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
    public void OnlyASendableBannedNameGetsTheFilterMessage() {
        System.Func<string, bool> banned = name => name.Contains("bad") || name.Trim().Length == 0;
        // "bad": 03 00 62 00 61 00 64 00.
        var packet = new ByteReader(new byte[] { 0x03, 0x00, 0x62, 0x00, 0x61, 0x00, 0x64, 0x00 }, 0);
        Assert.That(PetRules.IsBannedName(packet.ReadUnicodeString(), banned), Is.True);
        Assert.That(PetRules.IsBannedName("Bo", banned), Is.False);
        // Names the client's box never sends stay unanswered.
        Assert.That(PetRules.IsBannedName("", banned), Is.False);
        Assert.That(PetRules.IsBannedName("   ", banned), Is.False);
        Assert.That(PetRules.IsBannedName(new string('a', 29) + "bad", banned), Is.False);
        // The message is the client's s_ban_check_err_any, string 2234 of the KMS table.
        Assert.That(Maple2.Model.Enum.StringCodeMap.ToClient(Maple2.Model.Enum.StringCode.s_ban_check_err_any), Is.EqualTo(2234));
    }

    [Test]
    public void BannedAndBlankNamesAreRefused() {
        System.Func<string, bool> banned = name => name.Contains("bad");
        Assert.That(PetRules.IsAllowedName("Bo", banned), Is.True);
        Assert.That(PetRules.IsAllowedName("sobad", banned), Is.False);
        // Three spaces: 03 00 20 00 20 00 20 00.
        var packet = new ByteReader(new byte[] { 0x03, 0x00, 0x20, 0x00, 0x20, 0x00, 0x20, 0x00 }, 0);
        Assert.That(PetRules.IsAllowedName(packet.ReadUnicodeString(), banned), Is.False);
    }

    [Test]
    public void FirstRenameIsFreeLaterOnesCost() {
        Assert.That(PetRules.RenameCost(1, 100), Is.EqualTo(0));
        Assert.That(PetRules.RenameCost(0, 100), Is.EqualTo(100));
        Assert.That(PetRules.RenameCost(-1, 100), Is.EqualTo(100));
    }
}
