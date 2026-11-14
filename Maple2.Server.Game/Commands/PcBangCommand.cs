using System.CommandLine;
using System.CommandLine.Invocation;
using Maple2.Model.Enum;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

// Sets the session's PC bang grade (0 = not at a PC bang), as an address in PCBANG_IP_RANGES does at login.
public class PcBangCommand : GameCommand {
    private readonly GameSession session;
    public PcBangCommand(GameSession session) : base(AdminPermissions.GameMaster, "pcbang", "Set the PC bang grade") {
        this.session = session;

        var grade = new Argument<int>("grade", "The PC bang grade (0 = none)");
        AddArgument(grade);
        this.SetHandler<InvocationContext, int>(Handle, grade);
    }

    private void Handle(InvocationContext context, int grade) {
        if (!session.PcBang.On) {
            context.Console.WriteLine("The PCBang feature is off.");
            return;
        }
        session.PcBang.SetGrade(grade);
        context.Console.WriteLine($"PC bang grade {session.PcBang.Grade}.");
    }
}
