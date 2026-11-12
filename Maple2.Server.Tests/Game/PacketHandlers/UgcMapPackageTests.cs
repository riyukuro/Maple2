using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The interior packages (item function UGCMapPackage): the item use names an exported UGC map, "false" from the
// plain use and "true" after the client's question. The package is used up and its cubes are put in the owner's
// indoor home, each from storage. The item and package ids are made up.
public class UgcMapPackageTests {
    private const int Cube = 50200902, PackageId = 59300901;

    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;
    private Item package = null!;

    // A home of area 5, height 4 and a package of three cubes for 5 x 5 x 4 (byte area x area x height).
    private (GameSession Session, Plot Plot) Start(byte area = 5, byte height = 4, string name = "59300901", bool inHome = true) {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetTable(new FurnishingShopTable(new Dictionary<int, FurnishingShopTable.Entry>()));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        test.Register(TestGameSession.Metadata(Cube) with {
            Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false),
            Install = new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none),
        });
        var cache = (Dictionary<string, ExportedUgcMapMetadata>) typeof(MapMetadataStorage)
            .GetField("ExportedUgcCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(test.MapMetadata)!;
        cache["59300901"] = new ExportedUgcMapMetadata("59300901", new Vector3B(-4, -4, 1), [5, 5, 4], [
            new ExportedUgcMapMetadata.Cube(Cube, new Vector3B(0, 0, 0), 0, 0),
            new ExportedUgcMapMetadata.Cube(Cube, new Vector3B(4, 4, 0), 90, 0),
            new ExportedUgcMapMetadata.Cube(Cube, new Vector3B(2, 1, 3), 180, 0),
        ]);
        package = test.Store(TestGameSession.Metadata(PackageId,
            function: new ItemMetadataFunction(ItemFunction.UGCMapPackage, "UGCMapPackage", name, false), type: 10), amount: 2);
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        session.Player.Value.Home.SetArea(area);
        session.Player.Value.Home.SetHeight(height);
        Plot plot = inHome ? test.EnterHome() : null!;
        test.TakeSent();
        return (session, plot);
    }

    private void Use(GameSession session, string text) => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteLong(package.Uid);
        writer.WriteUnicodeString(text);
    }));

    private int Packages(GameSession session) => session.Item.Inventory.Get(package.Uid)?.Amount ?? 0;

    // A cube where the package puts one: its own cells from the base cell (1 - area, 1 - area, 0).
    private static void Place(GameSession session, Plot plot, sbyte x, sbyte y, sbyte z) {
        session.ItemMetadata.TryGet(Cube, out ItemMetadata? metadata);
        long uid = session.Item.Furnishing.AddCube(Cube);
        Assert.That(session.Housing.TryPlaceCube(new PlotCube(metadata!, uid) { Type = PlotCube.CubeType.Default }, plot, metadata!,
            new Vector3B(x, y, z), 0, out _), Is.True);
    }

    [Test]
    public void Install_IntoAnEmptyHome() {
        (GameSession session, Plot plot) = Start();

        Use(session, "false");

        Assert.That(plot.Cubes.Keys.OrderBy(p => (p.X, p.Y, p.Z)).Select(p => (p.X, p.Y, p.Z)), Is.EqualTo(new[] {
            ((sbyte) -4, (sbyte) -4, (sbyte) 0), ((sbyte) -2, (sbyte) -3, (sbyte) 3), ((sbyte) 0, (sbyte) 0, (sbyte) 0),
        }));
        Assert.That(plot.Cubes[new Vector3B(0, 0, 0)].Rotation, Is.EqualTo(90));
        Assert.That(plot.Cubes.Values.All(cube => cube.ItemId == Cube), Is.True);
        Assert.That(Packages(session), Is.EqualTo(1));
        Assert.That(test.Errors, Is.Empty);
    }

    // Cubes in the home: the plain use is refused (the client asks first); after a yes they go back to storage.
    [Test]
    public void Install_IntoAHomeWithCubes() {
        (GameSession session, Plot plot) = Start();
        Place(session, plot, -1, -1, 1);

        Use(session, "false");
        Assert.That(plot.Cubes.Keys, Is.EqualTo(new[] { new Vector3B(-1, -1, 1) }));
        Assert.That(Packages(session), Is.EqualTo(2));

        Use(session, "true");
        Assert.That(plot.Cubes, Has.Count.EqualTo(3));
        Assert.That(plot.Cubes.ContainsKey(new Vector3B(-1, -1, 1)), Is.False);
        Assert.That(Packages(session), Is.EqualTo(1));
        // The cleared cube is in storage again; the three placed ones are not.
        Assert.That(session.Item.Furnishing.GetItem(Cube)?.Amount, Is.EqualTo(1));
    }

    // A smaller home: the plain use is refused; after a yes the home grows to the package's size.
    [TestCase((byte) 4, (byte) 4)]
    [TestCase((byte) 5, (byte) 3)]
    public void Install_IntoASmallerHome(byte area, byte height) {
        (GameSession session, Plot plot) = Start(area, height);

        Use(session, "false");
        Assert.That(plot.Cubes, Is.Empty);
        Assert.That(Packages(session), Is.EqualTo(2));

        Use(session, "true");
        Home home = session.Player.Value.Home;
        Assert.That((home.Area, home.Height), Is.EqualTo(((byte) 5, (byte) 4)));
        Assert.That(plot.Cubes, Has.Count.EqualTo(3));
        Assert.That(Packages(session), Is.EqualTo(1));
    }

    // A larger home keeps its size; the cells start from its own base cell.
    [Test]
    public void Install_IntoALargerHome() {
        (GameSession session, Plot plot) = Start(area: 8, height: 6);

        Use(session, "false");

        Home home = session.Player.Value.Home;
        Assert.That((home.Area, home.Height), Is.EqualTo(((byte) 8, (byte) 6)));
        Assert.That(plot.Cubes.ContainsKey(new Vector3B(-7, -7, 0)), Is.True);
        Assert.That(plot.Cubes.ContainsKey(new Vector3B(-3, -3, 0)), Is.True);
    }

    // Out of the home, an unknown package and a bad text: nothing is used.
    [TestCase("field")]
    [TestCase("package")]
    [TestCase("text")]
    public void Install_Refused(string why) {
        (GameSession session, Plot plot) = Start(name: why == "package" ? "59300999" : "59300901", inHome: why != "field");

        Use(session, why == "text" ? "yes" : "true");

        Assert.That(Packages(session), Is.EqualTo(2));
        if (plot != null) {
            Assert.That(plot.Cubes, Is.Empty);
        }
    }

    [TestCase(true, 4, 3, 5, null)]
    [TestCase(false, 5, 4, 0, null)]
    [TestCase(false, 4, 4, 0, StringCode.s_err_ugcmap_package_not_a_indoor_for_this_package_item)]
    [TestCase(false, 5, 3, 0, StringCode.s_err_ugcmap_package_not_a_indoor_for_this_package_item)]
    [TestCase(false, 9, 9, 1, StringCode.s_err_ugcmap_package_clear_indoor_first)]
    public void Refusal(bool confirmed, int area, int height, int cubes, StringCode? expected) {
        Assert.That(UgcMapPackageRules.Refusal(confirmed, area, height, cubes, (5, 4)), Is.EqualTo(expected));
    }

    [Test]
    public void SizeAndOrigin() {
        Assert.That(UgcMapPackageRules.Size(new ExportedUgcMapMetadata("x", default, [13, 13, 4], [])), Is.EqualTo(((byte) 13, (byte) 4)));
        Assert.That(UgcMapPackageRules.Size(new ExportedUgcMapMetadata("x", default, [], [])), Is.Null);
        Assert.That(UgcMapPackageRules.Origin(10), Is.EqualTo(new Vector3B(-9, -9, 0)));
        Assert.That(UgcMapPackageRules.Confirmed("true"), Is.True);
        Assert.That(UgcMapPackageRules.Confirmed("x"), Is.Null);
    }
}
