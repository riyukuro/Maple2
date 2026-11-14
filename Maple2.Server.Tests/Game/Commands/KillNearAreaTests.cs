using System.Numerics;
using Maple2.Server.Game.Commands;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Game.Commands;

// "kill near -d N" kills the mobs within N of the player. A Pinkfluff (capsule radius 35, height 120) of Revoldic Dam
// spawns up to 250 off its spawn point, so the default 150 has to reach 150 on each side, and down to a mob on lower ground.
public class KillNearAreaTests {
    private static readonly Vector3 Player = new(7050, -1800, 3750);

    private static Prism Mob(float x, float y, float z) => new(new Circle(new Vector2(x, y), 35), z, 120);

    [Test]
    public void ReachesTheDistanceOnEachSide() {
        Prism area = KillCommand.NearArea(Player, 150);
        Assert.Multiple(() => {
            Assert.That(area.Intersects(Mob(Player.X + 140, Player.Y, Player.Z)), Is.True);
            Assert.That(area.Intersects(Mob(Player.X, Player.Y - 140, Player.Z)), Is.True);
            Assert.That(area.Intersects(Mob(Player.X - 100, Player.Y + 100, Player.Z)), Is.True);
            Assert.That(area.Intersects(Mob(Player.X + 200, Player.Y, Player.Z)), Is.False);
        });
    }

    [Test]
    public void ReachesBelowThePlayersFeet() {
        Prism area = KillCommand.NearArea(Player, 150);
        Assert.Multiple(() => {
            Assert.That(area.Intersects(Mob(Player.X, Player.Y, Player.Z - 200)), Is.True);
            Assert.That(area.Intersects(Mob(Player.X, Player.Y, Player.Z + 140)), Is.True);
            Assert.That(area.Intersects(Mob(Player.X, Player.Y, Player.Z - 400)), Is.False);
            Assert.That(area.Intersects(Mob(Player.X, Player.Y, Player.Z + 200)), Is.False);
        });
    }
}
