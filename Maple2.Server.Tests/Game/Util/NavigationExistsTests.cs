using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game.Util;

// The field factory asks this before it builds a field, so a map without a navmesh file is refused
// instead of throwing while the player is being moved.
public class NavigationExistsTests {
    [Test]
    public void MissingFile_IsReported() {
        Assert.That(Navigation.Exists("no_such_map_for_the_test"), Is.False);
        Assert.That(Navigation.Exists(string.Empty), Is.False);
    }
}
