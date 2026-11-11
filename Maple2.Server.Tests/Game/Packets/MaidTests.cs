using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Home maids. Client: CFieldMaidManager::OnPacket mode 0 reads a mode byte and CMaid::ReadFromPacket, mode 1 the
// maid uid. CMaid::ReadFromPacket: five longs, item id, maid id, a byte, mood, level, exp, the expiry, a counted
// list of longs and the 13 profile strings.
public class MaidTests {
    private const int CubeItemId = 50400001;
    private const int MaidId = 3;

    private static ItemMetadata Metadata(int maidId) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var housing = new ItemMetadataHousing(0, 0, 0, default, true, maidId);
        return new ItemMetadata(CubeItemId, null, [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [],
            null, null, housing, null);
    }

    [Test]
    public void AddIsModeByteThenCMaid() {
        var maid = new Maid {
            Uid = 77,
            OwnerId = 5,
            ItemId = CubeItemId,
            MaidId = MaidId,
            Mood = 4,
            Level = 2,
            Exp = 30,
            CreationTime = 1000,
            ExpiryTime = 2000,
            Profile = new MaidProfile { Name = "n", BelongTo = "b" },
        };
        ByteWriter writer = FieldMaidPacket.Add(maid);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.FieldMaid));
            Assert.That(reader.ReadByte(), Is.EqualTo(0)); // add
            Assert.That(reader.ReadByte(), Is.EqualTo(0)); // the maid's mode
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            Assert.That(reader.ReadLong(), Is.EqualTo(77));
            Assert.That(reader.ReadLong(), Is.EqualTo(1000));
            Assert.That(reader.ReadLong(), Is.EqualTo(1000));
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(CubeItemId));
            Assert.That(reader.ReadInt(), Is.EqualTo(MaidId));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(4));
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
            Assert.That(reader.ReadInt(), Is.EqualTo(30));
            Assert.That(reader.ReadLong(), Is.EqualTo(2000));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadUnicodeString(), Is.Empty); // title
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("n"));
            for (int i = 0; i < 10; i++) {
                Assert.That(reader.ReadUnicodeString(), Is.Empty);
            }
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("b"));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    [Test]
    public void RemoveIsModeByteThenUid() {
        ByteWriter writer = FieldMaidPacket.Remove(77);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.FieldMaid));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadLong(), Is.EqualTo(77));
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8));
        });
    }

    [Test]
    public void TableRow_NeedsPositiveMaidAndNpc() {
        Assert.Multiple(() => {
            Assert.That(MaidTable.Row("3", "12", "11001234"), Is.EqualTo(new MaidTable.Entry(3, 12, 11001234)));
            Assert.That(MaidTable.Row("3", "", "11001234"), Is.EqualTo(new MaidTable.Entry(3, 0, 11001234)));
            Assert.That(MaidTable.Row("0", "12", "11001234"), Is.Null);
            Assert.That(MaidTable.Row("3", "12", ""), Is.Null);
            Assert.That(MaidTable.Row("x", "12", "11001234"), Is.Null);
        });
    }

    [Test]
    public void MaidId_IsZeroOnAPlannerPlotAndForOtherCubes() {
        Assert.Multiple(() => {
            Assert.That(MaidRules.MaidId(Metadata(MaidId), planner: false), Is.EqualTo(MaidId));
            Assert.That(MaidRules.MaidId(Metadata(MaidId), planner: true), Is.EqualTo(0));
            Assert.That(MaidRules.MaidId(Metadata(0), planner: false), Is.EqualTo(0));
        });
    }

    [Test]
    public void Hire_StartsAtLevelOneForThePeriod() {
        var cube = new PlotCube(Metadata(MaidId), id: 77) { Type = PlotCube.CubeType.Construction };
        Maid maid = MaidRules.Hire(cube, MaidId, ownerId: 5, now: 1000, periodDays: 30);
        Assert.Multiple(() => {
            Assert.That(maid.Uid, Is.EqualTo(77));
            Assert.That(maid.OwnerId, Is.EqualTo(5));
            Assert.That(maid.ItemId, Is.EqualTo(CubeItemId));
            Assert.That(maid.MaidId, Is.EqualTo(MaidId));
            Assert.That(maid.Level, Is.EqualTo(1));
            Assert.That(maid.Mood, Is.EqualTo(0));
            Assert.That(maid.CreationTime, Is.EqualTo(1000));
            Assert.That(maid.ExpiryTime, Is.EqualTo(1000 + 30L * 86400));
        });
    }

    [Test]
    public void Orphans_AreTheMaidsWhoseCubeIsGone() {
        Assert.Multiple(() => {
            Assert.That(MaidRules.Orphans([77, 78, 79], [78, 90]), Is.EqualTo(new long[] { 77, 79 }));
            Assert.That(MaidRules.Orphans([77], [77]), Is.Empty);
            Assert.That(MaidRules.Orphans([77], []), Is.EqualTo(new long[] { 77 }));
        });
    }
}
