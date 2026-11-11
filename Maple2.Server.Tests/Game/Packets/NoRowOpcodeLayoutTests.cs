using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class NoRowOpcodeLayoutTests {
    // Client: WorldMapManager::LoadIconPacket (guess/worldmapmanager.cpp:646): boss list, byte second list, then a
    // byte that must be 1 for LoadAddtionalInfos (byte 3, int count, count x (int, int, short)) to be read.
    [Test]
    public void WorldMapLoadHasPopulationFlag() {
        ByteWriter writer = WorldMapPacket.Load([], [new MapPopulation { MapId = 2000023, Population = 7, Channel = 2 }]);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Worldmap));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(1)); // boss list present
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(0)); // no second list
        Assert.That(reader.ReadByte(), Is.EqualTo(1)); // population block follows
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 4 + 1 + 1 + 1 + 4 + 10));
    }

    // Client: UIItemRemakeOptionDialog::vfunc_79 (unnamed/unit_14129cfa0_itemremakeoptiondialog.cpp:324): kind 5 is
    // OnRemakeError (:1154): byte flag, int code.
    [Test]
    public void ChangeAttributesErrorIsKind5() {
        ByteWriter writer = ChangeAttributesPacket.Error(ChangeAttributesError.s_itemremake_error_server_lack_price);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ChangeAttributes));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(writer.Length, Is.EqualTo(2 + 1 + 1 + 4));
    }
}
