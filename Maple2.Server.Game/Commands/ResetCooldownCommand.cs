using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

// "resetcooldown": ends all of the player's skill cooldowns and recharge uses, on the server and in the client
// (SkillResetCooldown 0x43, ConfigManager.ResetSkillCooldowns). The client reads the packet, but no item or game
// rule of the KR data sends it (the cool time reset stone, item function ResetAbilityCoolTime, is the character
// ability reset time), so it is a debug command.
public class ResetCooldownCommand : GameCommand {
    private readonly GameSession session;

    public ResetCooldownCommand(GameSession session) : base(AdminPermissions.Debug, "resetcooldown", "End all skill cooldowns.") {
        this.session = session;
        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext ctx) {
        int count = session.Config.ResetSkillCooldowns();
        ctx.Console.Out.WriteLine($"Ended {count} skill cooldowns.");
    }
}
