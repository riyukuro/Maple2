using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// Cubes taken off a plot go back to furnishing storage (FurnishingManager.PlanReturn): cubes of one item and design
// share one new stack, and a cube of its own design (a template) is a stack of its own carrying the design. KR cube
// 50200902.
public class FurnishingReturnPlanTests {
    private const int CubeItem = 50200902;

    private static PlotCube Cube(ItemMetadata metadata, long uid, string? url = null) =>
        new(metadata, uid) {
            Type = PlotCube.CubeType.Default, Position = new Vector3B(1, 2, (int) uid),
            Template = url == null ? null : new UgcItemLook { Url = url, Name = "d" },
        };

    [Test]
    public void SameItem_SharesOneNewStack_ADesignGetsItsOwn() {
        var test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(CubeItem);
        test.Register(metadata);
        GameSession session = test.Start();
        test.EnterHome();

        CubeReturn plan = session.Item.Furnishing.PlanReturn([Cube(metadata, 1), Cube(metadata, 2), Cube(metadata, 3, "itemicon/ugc/a.m2u")], test.CharacterId)!;

        Assert.That(plan.Joins, Is.Empty);
        Assert.That(plan.Mailed, Is.Empty);
        Assert.That(plan.NewStacks.Select(add => (add.Item.Id, add.Item.Amount, add.Item.Template?.Url)),
            Is.EquivalentTo(new[] { (CubeItem, 2, (string?) null), (CubeItem, 1, "itemicon/ugc/a.m2u") }));
    }
}
