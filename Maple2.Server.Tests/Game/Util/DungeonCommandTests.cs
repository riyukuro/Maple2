using System.CommandLine.Parsing;
using Maple2.Model.Enum;
using Maple2.Server.Game.Commands;

namespace Maple2.Server.Tests.Game.Util;

// The GM dungeon command: only an account with the warp permission gets it, and it takes a room id and --party.
public class DungeonCommandTests {
    [Test]
    public void Dungeon_NeedsWarp() {
        var command = new DungeonCommand(null!);

        Assert.That(command.Name, Is.EqualTo("dungeon"));
        Assert.That(command.RequiredPermission, Is.EqualTo(AdminPermissions.Warp));
        Assert.That(CommandRouter.Allowed(AdminPermissions.None, command), Is.False);
        Assert.That(CommandRouter.Allowed(null, command), Is.False);
        Assert.That(CommandRouter.Allowed(AdminPermissions.GameMaster, command), Is.True);
        Assert.That(CommandRouter.Allowed(AdminPermissions.Admin, command), Is.True);
    }

    [Test]
    public void Dungeon_TakesARoomIdAndParty() {
        var command = new DungeonCommand(null!);

        Assert.That(new Parser(command).Parse("dungeon 29900001").Errors, Is.Empty);
        Assert.That(new Parser(command).Parse("dungeon 29900001 --party").Errors, Is.Empty);
        Assert.That(new Parser(command).Parse("dungeon").Errors, Is.Not.Empty);
        Assert.That(new Parser(command).Parse("dungeon abc").Errors, Is.Not.Empty);
    }
}
