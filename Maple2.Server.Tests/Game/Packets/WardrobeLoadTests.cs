using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class WardrobeLoadTests {
    // Client: mode 5 (client/ms2/uiclosetdialog.cpp:685, :963) reads the tab index and the tab (type, key,
    // name, equips of 20 bytes) and replaces the tab at that index, so the stored name comes back.
    [Test]
    public void Mode5CarriesTheStoredName() {
        var wardrobe = new Wardrobe(1, "Raid") { KeyId = 7 };
        wardrobe.Equips[EquipSlot.CL] = new Wardrobe.Equip(5000, 11400001, EquipSlot.CL, 4);
        ByteWriter writer = WardrobePacket.Load(2, wardrobe);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Wardrobe));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Raid"));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(5000));
        Assert.That(reader.ReadInt(), Is.EqualTo(11400001));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) EquipSlot.CL));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 4 + 4 + 4 + (2 + 8) + 4 + 20));
    }
}
