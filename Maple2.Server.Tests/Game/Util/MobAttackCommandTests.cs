using System.CommandLine.Parsing;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The GM mob-attack command: debug permission only, the nearest living mob or --object, and a per-attack-point report.
public class MobAttackCommandTests {
    [Test]
    public void MobAttack_NeedsDebug() {
        var command = new MobAttackCommand(null!);

        Assert.That(command.Name, Is.EqualTo("mob-attack"));
        Assert.That(command.RequiredPermission, Is.EqualTo(AdminPermissions.Debug));
        Assert.That(CommandRouter.Allowed(AdminPermissions.None, command), Is.False);
        Assert.That(CommandRouter.Allowed(null, command), Is.False);
        Assert.That(CommandRouter.Allowed(AdminPermissions.Admin, command), Is.True);
    }

    [Test]
    public void MobAttack_TakesASkillLevelAndObject() {
        var command = new MobAttackCommand(null!);

        Assert.That(new Parser(command).Parse("mob-attack").Errors, Is.Empty);
        Assert.That(new Parser(command).Parse("mob-attack 50100116 -l 2 --object 12").Errors, Is.Empty);
        Assert.That(new Parser(command).Parse("mob-attack 50100116 -o abc").Errors, Is.Not.Empty);
    }

    private static readonly MobAttackRules.Candidate[] Mobs = [
        new(10, new Vector3(0, 0, 0), false), // first in the list, far
        new(11, new Vector3(900, 0, 0), true), // nearest, dead
        new(12, new Vector3(800, 0, 0), false),
    ];

    [Test]
    public void Pick_NearestLivingMob() {
        Assert.That(MobAttackRules.Pick(Mobs, null, new Vector3(1000, 0, 0)), Is.EqualTo(12));
        Assert.That(MobAttackRules.Pick([], null, Vector3.Zero), Is.EqualTo(0));
    }

    [Test]
    public void Pick_ObjectIdWhenGiven() {
        Assert.That(MobAttackRules.Pick(Mobs, 10, new Vector3(1000, 0, 0)), Is.EqualTo(10));
        Assert.That(MobAttackRules.Pick(Mobs, 99, new Vector3(1000, 0, 0)), Is.EqualTo(0));
    }

    [Test]
    public void Watch_ReportsTheWatchedSkillUntilItExpires() {
        var lines = new System.Collections.Generic.List<string>();
        var watch = new MobAttackRules.Watch(50100116, 1000, lines.Add);

        watch.Report(0, []);

        Assert.That(lines, Is.EqualTo(new[] { "mob-attack 50100116 point 0: nothing in range" }));
        Assert.That(MobAttackRules.Describe(50100116, 1, [(5, "Gm")]), Is.EqualTo("mob-attack 50100116 point 1: in range Gm (5)"));
        Assert.That(watch.Expired(1000 + MobAttackRules.WatchMillis), Is.False);
        Assert.That(watch.Expired(1001 + MobAttackRules.WatchMillis), Is.True);
    }
}
