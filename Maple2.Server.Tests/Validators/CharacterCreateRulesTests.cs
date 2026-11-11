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

    // CharacterManagement 0x0009 sub-type 1 equips: u8 count, then per equip i32 item id, u8 slot, its look.
    [TestCase(new byte[] { 1, 2, 8, 9, 11 }, true)]
    [TestCase(new byte[] { 1, 2, 8 }, true)]
    [TestCase(new byte[] { }, false)]
    [TestCase(new byte[] { 2, 8, 9, 11 }, false)] // no hair
    [TestCase(new byte[] { 1, 2, 9, 11 }, false)] // no clothes
    [TestCase(new byte[] { 1, 8, 9 }, false)] // no face
    [TestCase(new byte[] { 1, 1, 2, 8 }, false)] // hair twice
    public void CreateOutfitSlots(byte[] wireSlots, bool expected) {
        var slots = new System.Collections.Generic.List<EquipSlot>();
        var packet = new ByteReader(wireSlots, 0);
        while (packet.Available > 0) {
            slots.Add((EquipSlot) packet.ReadByte());
        }
        Assert.That(CharacterCreateRules.IsCreateOutfit(slots), Is.EqualTo(expected));
    }

    // RequestItemUse with the name change item: after the item uid, the new name.
    [Test]
    public void NameChangeRefusesBlankAndBannedNames() {
        System.Func<string, bool> banned = name => name.Contains("bad");
        // Two spaces: 02 00 20 00 20 00.
        var blank = new ByteReader(new byte[] { 0x02, 0x00, 0x20, 0x00, 0x20, 0x00 }, 0);
        string name = blank.ReadUnicodeString();
        Assert.That(CharacterCreateRules.IsBlankName(name), Is.True);
        Assert.That(CharacterCreateRules.IsAllowedName(name, banned), Is.False);

        var writer = new ByteWriter();
        writer.WriteUnicodeString("sobad");
        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(CharacterCreateRules.IsAllowedName(packet.ReadUnicodeString(), banned), Is.False);
        Assert.That(CharacterCreateRules.IsAllowedName("Good", banned), Is.True);
    }
}
