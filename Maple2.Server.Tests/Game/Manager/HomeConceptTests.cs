using System.Collections.Generic;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The concept of a plot purchase (RequestCube 2) builds the buyer's unnamed indoor home: model house c (0..2) is
// exported UGC map c + 1, -1 and any other value make the empty home, and a home with a name is left alone. The
// three templates here are made up and told apart by their size; they have no cubes.
public class HomeConceptTests {
    private const int CubeItem = 50400001;
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        GameSession session = test.Start();
        var cache = (Dictionary<string, ExportedUgcMapMetadata>) typeof(MapMetadataStorage)
            .GetField("ExportedUgcCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.MapMetadata)!;
        for (byte i = 1; i <= 3; i++) {
            cache[i.ToString()] = new ExportedUgcMapMetadata(i.ToString(), new Vector3B(-3, -3, 0), [(byte) (10 + i), (byte) (10 + i), (byte) (4 + i)], []);
        }
        return session;
    }

    [TestCase(-1, null)]
    [TestCase(0, "1")]
    [TestCase(1, "2")]
    [TestCase(2, "3")]
    [TestCase(3, null)]
    [TestCase(-2, null)]
    [TestCase(int.MaxValue, null)]
    public void ConceptTemplate_IsTheExportedMapOneUp(int concept, string? template) {
        Assert.That(HousingManager.ConceptTemplate(concept), Is.EqualTo(template));
    }

    [TestCase(0, 11, 5)]
    [TestCase(1, 12, 6)]
    [TestCase(2, 13, 7)]
    public void UnnamedHome_GetsTheModelHouse(int concept, int area, int height) {
        GameSession session = Start();
        Assert.That(session.Player.Value.Home.Indoor.Name, Is.Empty);

        Assert.That(session.Housing.InitHomeForConcept(concept), Is.True);
        Assert.That(session.Player.Value.Home.Indoor.Name, Is.EqualTo(session.Player.Value.Character.Name));
        Assert.That(session.Player.Value.Home.Area, Is.EqualTo(area));
        Assert.That(session.Player.Value.Home.Height, Is.EqualTo(height));
    }

    // -1 is the dialog's empty house; an out-of-range concept is taken the same way: the home is made with the
    // empty-home size (area 10, height 4) and no template.
    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(-7)]
    public void EmptyOrOutOfRange_MakesTheEmptyHome(int concept) {
        GameSession session = Start();

        Assert.That(session.Housing.InitHomeForConcept(concept), Is.True);
        Assert.That(session.Player.Value.Home.Indoor.Name, Is.EqualTo(session.Player.Value.Character.Name));
        Assert.That(session.Player.Value.Home.Area, Is.EqualTo(10));
        Assert.That(session.Player.Value.Home.Height, Is.EqualTo(4));
    }

    [Test]
    public void NamedHome_IsLeftAlone() {
        GameSession session = Start();
        session.Housing.InitNewHome("home", null);
        session.Player.Value.Home.SetArea(20);

        Assert.That(session.Housing.InitHomeForConcept(1), Is.False);
        Assert.That(session.Player.Value.Home.Indoor.Name, Is.EqualTo("home"));
        Assert.That(session.Player.Value.Home.Area, Is.EqualTo(20));
    }

    // An unnamed indoor home that holds a cube keeps it: no model house, no name, the cube row stays.
    [Test]
    public void UnnamedHomeWithCubes_IsLeftAlone() {
        GameSession session = Start();
        test.Register(TestGameSession.Metadata(CubeItem));
        session.ItemMetadata.TryGet(CubeItem, out ItemMetadata? metadata);
        PlotInfo indoor = session.Player.Value.Home.Indoor;
        long cubeId;
        using (GameStorage.Request db = test.Storage.Context()) {
            PlotCube? cube = db.CreateCube(indoor, new PlotCube(metadata!) { Position = new Vector3B(1, 1, 0), Type = PlotCube.CubeType.Construction });
            Assert.That(cube, Is.Not.Null);
            cubeId = cube!.Id;
        }

        Assert.That(session.Housing.InitHomeForConcept(0), Is.False);
        Assert.That(indoor.Name, Is.Empty);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetCube(cubeId), Is.Not.Null);
            Assert.That(db.HasCubes(indoor.Id), Is.True);
        }
    }
}
