using System;
using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// PlayInstrument 0xC7: CInstrumentManager::OnPacket (client/ms2/instrumentmanager.cpp:335) reads the mode byte, then per mode.
public class InstrumentLayoutTests {
    private static ByteReader Open(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.PlayInstrument));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    private static FieldInstrument Instrument() {
        return new FieldInstrument(null!, 0x0D2F22, new InstrumentMetadata(1, 0, 0, 0, 11, 22)) {
            OwnerId = 0x09C060,
            Position = new Vector3(1, 2, 3),
            StartTick = 0x1_0000_0005,
            Ensemble = true,
        };
    }

    private static Item ScoreItem(ItemCustomMusicScore? music) {
        var property = new ItemMetadataProperty(false, 0, 1, 0, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 50, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(35000001, "35000001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null,
            new ItemMetadataMusic(10, 0, 0, music != null, 0, "file.mml", 0), null, null);
        return new Item(metadata, 1) { Uid = 0x0102030405060708, Music = music };
    }

    private static void ReadKeyOwner(ByteReader reader) {
        Assert.That(reader.ReadInt(), Is.EqualTo(0x0D2F22));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x09C060));
    }

    // Mode 0: i32 key, i32 owner, Vector3, i32 program, i32 percussion.
    [Test]
    public void StartImproviseIsKeyOwnerPosProgramPercussion() {
        ByteWriter writer = InstrumentPacket.StartImprovise(Instrument());
        ByteReader reader = Open(writer, 0);
        Assert.Multiple(() => {
            ReadKeyOwner(reader);
            Assert.That(reader.Read<Vector3>(), Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(reader.ReadInt(), Is.EqualTo(11));
            Assert.That(reader.ReadInt(), Is.EqualTo(22));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Modes 2 and 4: i32 key, i32 owner.
    [Test]
    public void StopImproviseAndStopScoreAreKeyOwner() {
        ByteWriter writer = InstrumentPacket.StopImprovise(Instrument());
        ByteReader reader = Open(writer, 2);
        ReadKeyOwner(reader);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));

        writer = InstrumentPacket.StopScore(Instrument());
        reader = Open(writer, 4);
        ReadKeyOwner(reader);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 3: u8 kind, i32 key, i32 owner, Vector3, i32 start, i32 program, i32 percussion, bool remote, then kind 0:
    // the score file name (ReadUnicodeStringA), kind 1: the score text (ReadString).
    [TestCase(false)]
    [TestCase(true)]
    public void StartScoreIsKindThenFileOrText(bool custom) {
        Item score = ScoreItem(custom ? new ItemCustomMusicScore { Mml = "MML@cde;" } : null);
        ByteWriter writer = InstrumentPacket.StartScore(Instrument(), score);
        ByteReader reader = Open(writer, 3);
        Assert.Multiple(() => {
            Assert.That(reader.ReadByte(), Is.EqualTo(custom ? 1 : 0));
            ReadKeyOwner(reader);
            Assert.That(reader.Read<Vector3>(), Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(reader.ReadInt(), Is.EqualTo(5));
            Assert.That(reader.ReadInt(), Is.EqualTo(11));
            Assert.That(reader.ReadInt(), Is.EqualTo(22));
            Assert.That(reader.ReadBool(), Is.True);
            Assert.That(custom ? reader.ReadString() : reader.ReadUnicodeString(), Is.EqualTo(custom ? "MML@cde;" : "file.mml"));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Mode 6: nothing after the mode.
    [Test]
    public void LeaveEnsembleIsModeOnly() {
        ByteWriter writer = InstrumentPacket.LeaveEnsemble();
        ByteReader reader = Open(writer, 6);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 8: i64 uid, then the item block (CItem::ReadFromStream).
    [Test]
    public void ComposeScoreIsUidThenItem() {
        Item score = ScoreItem(new ItemCustomMusicScore { Mml = "MML@c;" });
        ByteWriter writer = InstrumentPacket.ComposeScore(score);
        ByteReader reader = Open(writer, 8);
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        var item = new ByteWriter();
        item.WriteClass<Item>(score);
        Assert.That(writer.ToArray().Skip(reader.Position).ToArray(), Is.EqualTo(item.ToArray()));
    }

    // Mode 9: i64 uid, i32 remaining uses.
    [Test]
    public void RemainUsesIsUidThenCount() {
        ByteWriter writer = InstrumentPacket.RemainUses(0x0102030405060708, 9);
        ByteReader reader = Open(writer, 9);
        Assert.Multiple(() => {
            Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
            Assert.That(reader.ReadInt(), Is.EqualTo(9));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Mode 10: i64 uid, then the score text (ReadString).
    [Test]
    public void ViewScoreIsUidThenText() {
        ByteWriter writer = InstrumentPacket.ViewScore(0x0102030405060708, "MML@c;");
        ByteReader reader = Open(writer, 10);
        Assert.Multiple(() => {
            Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
            Assert.That(reader.ReadString(), Is.EqualTo("MML@c;"));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
        Assert.That(Convert.ToHexString(writer.ToArray()), Does.EndWith("0600" + "4D4D4C40633B"));
    }

    // Mode 14: i32 player object id; mode 17: u8 compose result.
    [Test]
    public void FireworksIsIdAndComposeResultIsOneByte() {
        ByteWriter writer = InstrumentPacket.Fireworks(0x0D2F22);
        ByteReader reader = Open(writer, 14);
        Assert.That(reader.ReadInt(), Is.EqualTo(0x0D2F22));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));

        writer = InstrumentPacket.ComposeResult(ComposeResult.BannedWord);
        reader = Open(writer, 17);
        Assert.That(reader.ReadByte(), Is.EqualTo(0x10));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }
}
