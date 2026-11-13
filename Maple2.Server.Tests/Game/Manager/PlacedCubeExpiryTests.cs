using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Common;
using Maple2.Model.Enum;
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

    // A cube placed in an earlier session: its row on the player's home holds the expiry, and no storage stack of
    // the item is left (the case of a stack used up and gone).
    private static long PlacedCubeRow(TestGameSession test, ItemMetadata metadata, long expiry = Expiry) {
        long mapId;
        using (var context = new Ms2Context(test.Options)) {
            mapId = context.UgcMap.Single(map => map.OwnerId == test.AccountId && map.Indoor).Id;
        }
        using Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context();
        PlotInfo info = Info();
        info.Id = mapId;
        info.OwnerId = test.AccountId;
        PlotCube cube = Cube(metadata, 0);
        cube.ExpiryTime = expiry;
        return db.CreateCube(info, cube)!.Id;
    }

    [Test]
    public void PickingUp_KeepsTheCubeExpiry() {
        var test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(CubeItem);
        test.Register(metadata);
        long cubeId = PlacedCubeRow(test, metadata);
        GameSession session = test.Start();
        test.EnterHome();

        long itemUid = session.Item.Furnishing.RetrieveCube(cubeId);
        Assert.That(itemUid, Is.Not.Zero);
        Assert.That(session.Item.Furnishing.GetCube(itemUid)?.ExpiryTime, Is.EqualTo(Expiry));
    }

    // A stack of the same item with another expiry is not joined: the cube goes back to its own stack.
    [Test]
    public void PickingUp_DoesNotJoinAStackOfAnotherExpiry() {
        const long Other = Expiry + 86400;
        var test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(CubeItem);
        test.Register(metadata);
        long cubeId = PlacedCubeRow(test, metadata);
        GameSession session = test.Start();
        test.EnterHome();
        long otherUid = session.Item.Furnishing.AddCube(new Maple2.Model.Game.Item(metadata) { ExpiryTime = Other });

        long itemUid = session.Item.Furnishing.RetrieveCube(cubeId);
        Assert.That(itemUid, Is.Not.EqualTo(otherUid));
        Assert.That(session.Item.Furnishing.GetCube(itemUid)?.ExpiryTime, Is.EqualTo(Expiry));
        Assert.That((session.Item.Furnishing.GetCube(otherUid)?.Amount, session.Item.Furnishing.GetCube(otherUid)?.ExpiryTime),
            Is.EqualTo(((int?) 1, (long?) Other)));
    }

    private static PlotCube StoredCube(TestGameSession test, long cubeId) {
        using Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context();
        return db.GetCube(cubeId)!;
    }

    // A cube of an item without a lifetime has no expiry (0), and keeps that: it does not join a stack of the item
    // with an expiry.
    [Test]
    public void PickingUp_ACubeOfNoExpiry_DoesNotJoinAStackWithOne() {
        var test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(CubeItem);
        test.Register(metadata);
        long cubeId = PlacedCubeRow(test, metadata, expiry: 0);
        GameSession session = test.Start();
        test.EnterHome();
        long otherUid = session.Item.Furnishing.AddCube(new Maple2.Model.Game.Item(metadata) { ExpiryTime = Expiry });

        long itemUid = session.Item.Furnishing.RetrieveCube(cubeId);
        Assert.That(itemUid, Is.Not.Zero.And.Not.EqualTo(otherUid));
        Assert.That(session.Item.Furnishing.GetCube(itemUid)?.ExpiryTime, Is.Zero);
    }

    // Storage full, and the cube (of its own expiry) joins no stack: the pickup is refused and the cube stays placed,
    // in the plot and in the furnishing inventory.
    [Test]
    public void PickingUp_WithStorageFull_LeavesTheCubePlaced() {
        var test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(CubeItem);
        test.Register(metadata);
        long cubeId = PlacedCubeRow(test, metadata);
        GameSession session = test.Start();
        Plot plot = test.EnterHome();
        for (int i = 1; i <= Constant.FurnishingStorageMaxSlot; i++) {
            Assert.That(session.Item.Furnishing.AddStorage(new Maple2.Model.Game.Item(metadata) { ExpiryTime = Expiry + i }, keepExpiry: true),
                Is.Not.Zero);
        }
        PlotCube cube = StoredCube(test, cubeId);
        plot.Cubes[cube.Position] = cube;
        test.TakeSent();

        Assert.That(session.Housing.TryRemoveCube(plot, cube.Position, out _), Is.False);
        Assert.That(plot.Cubes.ContainsKey(cube.Position), Is.True);
        Assert.That(session.Item.Furnishing.RetrieveCube(cubeId), Is.Zero, "still in the inventory, storage still full");
        Assert.That(plot.Cubes.ContainsKey(cube.Position), Is.True);
    }

    // A model house over the home returns its cubes with their expiry: one does not join a stack of another expiry,
    // the item made for it has the cube's.
    [Test]
    public void ReturnPlan_KeepsTheCubeExpiry() {
        const long Other = Expiry + 86400;
        var test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(CubeItem);
        test.Register(metadata);
        long cubeId = PlacedCubeRow(test, metadata);
        GameSession session = test.Start();
        test.EnterHome();
        session.Item.Furnishing.AddCube(new Maple2.Model.Game.Item(metadata) { ExpiryTime = Other });

        Maple2.Database.Storage.CubeReturn plan = session.Item.Furnishing.PlanReturn([StoredCube(test, cubeId)], test.CharacterId)!;
        Assert.That(plan.Joins, Is.Empty);
        Assert.That(plan.NewStacks.Select(add => add.Item.ExpiryTime), Is.EqualTo(new[] { Expiry }));
    }

    private static (TestGameSession Test, GameSession Session, Plot Plot, ItemMetadata Metadata) StartInBuiltHome(
        Dictionary<int, FurnishingShopTable.Entry>? shop = null) {
        var test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetTable(new FurnishingShopTable(shop ?? new Dictionary<int, FurnishingShopTable.Entry>()));
        ItemMetadata metadata = TestGameSession.Metadata(CubeItem) with {
            Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false),
            Install = new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none),
        };
        test.Register(metadata);
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        session.Player.Value.Home.SetArea(5);
        session.Player.Value.Home.SetHeight(4);
        Plot plot = test.EnterHome();
        return (test, session, plot, metadata);
    }

    // The cube row is made with the expiry: a server stop before the next plot save does not leave the row at 0.
    [Test]
    public void Placing_MakesTheRowWithTheExpiry() {
        (TestGameSession test, GameSession session, Plot plot, ItemMetadata metadata) = StartInBuiltHome();
        long uid = session.Item.Furnishing.AddCube(new Maple2.Model.Game.Item(metadata) { ExpiryTime = Expiry });

        Assert.That(session.Housing.TryPlaceCube(Cube(metadata, uid), plot, metadata, new Vector3B(-4, -4, 1), 0, out PlotCube? placed), Is.True);

        using Maple2.Database.Storage.GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetCube(placed!.Id)?.ExpiryTime, Is.EqualTo(Expiry));
    }

    // A cube bought from the palette that the player cannot pay for leaves no storage item behind.
    [Test]
    public void BuyingWithoutTheMesos_LeavesNoStorageItem() {
        (TestGameSession test, GameSession session, Plot plot, ItemMetadata metadata) = StartInBuiltHome(
            new Dictionary<int, FurnishingShopTable.Entry> { [CubeItem] = new(CubeItem, true, FurnishingCurrencyType.Meso, 1000) });
        Assert.That(session.Currency.Meso, Is.Zero);

        Assert.That(session.Housing.TryPlaceCube(Cube(metadata, 0), plot, metadata, new Vector3B(-4, -4, 1), 0, out _), Is.False);
        Assert.That(session.Item.Furnishing.GetItem(CubeItem), Is.Null);
        Assert.That(plot.Cubes, Is.Empty);
    }
}
