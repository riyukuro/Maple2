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

    // Client: UIItemExtractionScrollDialog::vfunc_79 (uiitemextractionscrolldialog.cpp:244): byte 0, long source uid,
    // long result uid, short remaining extraction count of the source item.
    [Test]
    public void GlamourAnvilExtractCarriesRemainingCount() {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(11300001, "11300001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        var result = new Item(metadata, 1) { Uid = 77 };

        ByteWriter writer = ItemExtractionPacket.Extract(55, 2, result);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.GlamourAnvil));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(55));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadShort(), Is.EqualTo(2));
    }

    // Client: CSceneStateGame::OnAutoRevive (scenestategame.cpp:4332): one int, the countdown in milliseconds.
    [Test]
    public void AutoReviveCountdownIsOneInt() {
        ByteWriter writer = AutoRevivePacket.Countdown(10000);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.AutoRevive));
        Assert.That(reader.ReadInt(), Is.EqualTo(10000));
        Assert.That(writer.Length, Is.EqualTo(2 + 4));
    }
}
