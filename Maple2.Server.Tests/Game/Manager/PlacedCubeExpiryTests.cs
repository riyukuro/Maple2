using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Manager;

// A placed cube keeps the expiry of the storage item it was placed from: the cube record's third field (the long the
// client's build palette fills from a bag item's expiry) carries it, and the cube row keeps it across a plot save and
// load. The item id and the expiry are made up.
public class PlacedCubeExpiryTests {
    private const int CubeItem = 50800001;
    private const long Expiry = 1_800_000_000;

    private static PlotInfo Info() => new(new UgcMapGroup(1, 0, 0, new UgcMapGroup.Cost(0, 0, 0), new UgcMapGroup.Cost(0, 0, 0),
        new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0))) { Id = 4242, MapId = 62000000, Number = 1 };

    private static PlotCube Cube(ItemMetadata metadata, long uid) =>
        new(metadata, uid) { Type = PlotCube.CubeType.Default, Position = new Vector3B(1, 2, 3) };

    private static long WrittenExpiry(HeldCube cube) {
        var writer = new ByteWriter();
        writer.WriteClass<HeldCube>(cube);
        return new ByteReader(writer.ToArray(), 12).ReadLong(); // after the item id (int) and the cube id (long)
    }

    [Test]
    public void Placing_KeepsTheItemExpiry_AndSendsIt() {
        var test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(CubeItem);
        test.Register(metadata);
        GameSession session = test.Start();
        test.EnterHome();
        long uid = session.Item.Furnishing.AddCube(new Maple2.Model.Game.Item(metadata) { ExpiryTime = Expiry });
        Assert.That(uid, Is.Not.Zero);

        PlotCube cube = Cube(metadata, uid);
        Assert.That(session.Item.Furnishing.TryAddCube(uid, cube), Is.True);
        Assert.That(cube.ExpiryTime, Is.EqualTo(Expiry));
        Assert.That(WrittenExpiry(cube), Is.EqualTo(Expiry));
    }

    [Test]
    public void ACubeOfNoExpiry_SendsZero() {
        PlotCube cube = Cube(TestGameSession.Metadata(CubeItem), 9);
        Assert.That(WrittenExpiry(cube), Is.Zero);
    }

    [Test]
    public void TheCubeRow_KeepsTheExpiry_AcrossAPlotSave() {
        var test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(CubeItem);
        test.Register(metadata);
        PlotCube created;
        using (Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context()) {
            created = db.CreateCube(Info(), Cube(metadata, 0))!;
            Assert.That(created, Is.Not.Null);
        }
        created.ExpiryTime = Expiry; // as TryAddCube sets it after the row is made
        using (Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.SaveCubes(Info(), [created], 1), Is.Not.Null);
        }
        using (Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetCube(created.Id)?.ExpiryTime, Is.EqualTo(Expiry));
        }
    }
}
