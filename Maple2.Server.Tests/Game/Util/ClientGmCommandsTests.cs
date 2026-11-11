using System.CommandLine.Parsing;
using Maple2.Model.Enum;
using Maple2.Server.Game.Commands;

namespace Maple2.Server.Tests.Game.Util;

// The router lowers the command name the client sends and registers a command only for an account that
// holds its permission.
public class ClientGmCommandsTests {
    [Test]
    public void MoveFieldAndAddField_NeedWarp() {
        var move = new MoveFieldCommand(null!, null!);
        var add = new AddFieldCommand(null!, null!);

        Assert.That(move.Name, Is.EqualTo("MoveField".ToLower()));
        Assert.That(add.Name, Is.EqualTo("AddField".ToLower()));
        Assert.That(move.RequiredPermission, Is.EqualTo(AdminPermissions.Warp));
        Assert.That(add.RequiredPermission, Is.EqualTo(AdminPermissions.Warp));
        Assert.That(AdminPermissions.None.HasFlag(move.RequiredPermission), Is.False);
    }

    [Test]
    public void MoveField_TakesTheWorldMapsSecondArgument() {
        var move = new MoveFieldCommand(null!, null!);

        Assert.That(new Parser(move).Parse("movefield 2000062").Errors, Is.Empty);
        Assert.That(new Parser(move).Parse("movefield 2000062 1").Errors, Is.Empty);
        Assert.That(new Parser(move).Parse("movefield").Errors, Is.Not.Empty);
    }

    [Test]
    public void GmOnlyCommands_NeedGameMaster() {
        GameCommand[] commands = [new ClearInvenCommand(null!), new ToggleInvisibleFromNpcCommand(null!), new ToggleDeathPowerCommand(null!)];
        string[] names = ["ClearInven", "ToggleInvisibleFromNpc", "ToggleDeathPower"];

        for (int i = 0; i < commands.Length; i++) {
            Assert.That(commands[i].Name, Is.EqualTo(names[i].ToLower()));
            Assert.That(commands[i].RequiredPermission, Is.EqualTo(AdminPermissions.GameMaster));
            Assert.That(AdminPermissions.Warp.HasFlag(commands[i].RequiredPermission), Is.False);
            Assert.That(AdminPermissions.Admin.HasFlag(commands[i].RequiredPermission), Is.True);
        }
    }
}
