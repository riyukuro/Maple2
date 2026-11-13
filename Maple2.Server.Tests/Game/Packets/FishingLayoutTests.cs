using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Fishing 0xC4: CFishingManager::FUN_140cd9840 (client/ms2/fishingmanager.cpp:546) reads the mode byte, then per mode.
public class FishingLayoutTests {
    private static ByteReader Open(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Fishing));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    private static Item ItemOf(int id, int amount) {
        var property = new ItemMetadataProperty(false, 0, 1, 0, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 50, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(id, id.ToString(), [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Item(metadata, 1, amount);
    }

    // Mode 0: the rod uid (i64).
    [Test]
    public void PrepareIsRodUid() {
        ByteWriter writer = FishingPacket.Prepare(0x0102030405060708);
        ByteReader reader = Open(writer, 0);
        Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 1: nothing after the mode.
    [Test]
    public void StopIsModeOnly() {
        ByteWriter writer = FishingPacket.Stop();
        ByteReader reader = Open(writer, 1);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 3: i32 fish, i32 exp, i16 kind (1/2/3 pick texts 0xbe7-0xbe9), i16 level.
    [Test]
    public void IncreaseMasteryIsFishExpKindLevel() {
        ByteWriter writer = FishingPacket.IncreaseMastery(10000001, 7, 300, CaughtFishType.Prize);
        ByteReader reader = Open(writer, 3);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(10000001));
            Assert.That(reader.ReadInt(), Is.EqualTo(300));
            Assert.That(reader.ReadShort(), Is.EqualTo(3));
            Assert.That(reader.ReadShort(), Is.EqualTo(7));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Mode 4: u8 append, i32 n, n x FUN_1403b0a40 (cell key x | y<<8 | z<<16, i32, i32, i32, i16).
    [TestCase(true)]
    [TestCase(false)]
    public void LoadTilesIsAppendCountThenTiles(bool append) {
        var fluid = new FieldFluidEntity(new FieldEntityId(0, 0, string.Empty), new Vector3(150 * 2, 150 * -3, 150 * 4), default, 1f,
            LiquidType.water, default, 0, false, true, MapAttribute.none);
        ByteWriter writer = FishingPacket.LoadTiles(new[] { new FishingTile(fluid, 1000) }, append);
        ByteReader reader = Open(writer, 4);
        Assert.Multiple(() => {
            Assert.That(reader.ReadBool(), Is.EqualTo(append));
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(2 | (0xFD << 8) | (4 << 16)));
            Assert.That(reader.ReadInt(), Is.EqualTo(10000001));
            Assert.That(reader.ReadInt(), Is.EqualTo(25));
            Assert.That(reader.ReadInt(), Is.EqualTo(14000));
            Assert.That(reader.ReadShort(), Is.EqualTo(1));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Mode 5: i32 n, n x (i32 item, i16 count).
    [Test]
    public void CatchItemIsCountThenIdAmount() {
        ByteWriter writer = FishingPacket.CatchItem(new[] { ItemOf(20000001, 3), ItemOf(20000002, 1) });
        ByteReader reader = Open(writer, 5);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
            Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
            Assert.That(reader.ReadShort(), Is.EqualTo(3));
            Assert.That(reader.ReadInt(), Is.EqualTo(20000002));
            Assert.That(reader.ReadShort(), Is.EqualTo(1));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Mode 6: str name, i32 fish, i32 count.
    [Test]
    public void PrizeFishIsNameFishCount() {
        ByteWriter writer = FishingPacket.PrizeFish("ab", 10000002);
        ByteReader reader = Open(writer, 6);
        Assert.Multiple(() => {
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ab"));
            Assert.That(reader.ReadInt(), Is.EqualTo(10000002));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Mode 8: i32 fish, i32 size, u8 success, u8 auto, then a CFishCube (4 x i32) only on success.
    [Test]
    public void CatchFishSuccessCarriesEntry() {
        var entry = new FishEntry(10000003) { TotalCaught = 5, TotalPrizeFish = 1, LargestSize = 77 };
        ByteWriter writer = FishingPacket.CatchFish(10000003, 60, true, entry);
        ByteReader reader = Open(writer, 8);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(10000003));
            Assert.That(reader.ReadInt(), Is.EqualTo(60));
            Assert.That(reader.ReadBool(), Is.True);
            Assert.That(reader.ReadBool(), Is.True);
            Assert.That(reader.ReadInt(), Is.EqualTo(10000003));
            Assert.That(reader.ReadInt(), Is.EqualTo(5));
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(77));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    [Test]
    public void CatchFishMissHasNoEntry() {
        ByteWriter writer = FishingPacket.CatchFish(10000003, 60);
        ByteReader reader = Open(writer, 8);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(10000003));
            Assert.That(reader.ReadInt(), Is.EqualTo(60));
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(reader.ReadBool(), Is.False);
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Mode 9: u8 mini game, i32 bite end tick.
    [Test]
    public void StartIsMiniGameThenTick() {
        ByteWriter writer = FishingPacket.Start(12345, true);
        ByteReader reader = Open(writer, 9);
        Assert.Multiple(() => {
            Assert.That(reader.ReadBool(), Is.True);
            Assert.That(reader.ReadInt(), Is.EqualTo(12345));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    // Mode 10: u8 auto.
    [Test]
    public void AutoIsOneBool() {
        ByteWriter writer = FishingPacket.Auto(true);
        ByteReader reader = Open(writer, 10);
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // Mode 0xb: i32 n, n x CFishSimulteData::Read (commonfiles/fishsimultedata.cpp:78): 3 x i32, then 4 x i32.
    [Test]
    public void SimulateIsCountThenSevenInts() {
        ByteWriter writer = FishingPacket.Simulate(new[] { new FishEntry(10000004) });
        ByteReader reader = Open(writer, 0xb);
        Assert.Multiple(() => {
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(10000004));
            for (int i = 0; i < 6; i++) {
                Assert.That(reader.ReadInt(), Is.EqualTo(0));
            }
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }
}
