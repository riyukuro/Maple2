using System.CommandLine;
using System.CommandLine.Invocation;
using Maple2.Model.Enum;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

// GM check of the rank duel intro HUD (UIEventIntroduce 0x4f06): sends the player's own client one intro, anywhere,
// with a time on screen in ms (and the round number for the round intro).
public class PvpIntroCommand : GameCommand {
    private readonly GameSession session;

    public PvpIntroCommand(GameSession session) : base(AdminPermissions.GameMaster, "pvpintro", "Show a rank duel intro to yourself") {
        this.session = session;

        var kind = new Argument<string>("kind", "round, fight, win, lose or draw");
        var durationMs = new Option<int>(["--ms", "-m"], () => 5000, "Time on screen (ms)");
        var round = new Option<int>(["--round", "-r"], () => 1, "The round number (round)");
        AddArgument(kind);
        AddOption(durationMs);
        AddOption(round);
        this.SetHandler<InvocationContext, string, int, int>(Handle, kind, durationMs, round);
    }

    private void Handle(InvocationContext context, string kind, int durationMs, int round) {
        Maple2.PacketLib.Tools.ByteWriter? packet = kind.ToLowerInvariant() switch {
            "round" => PvpPacket.RankDuelRound(round, durationMs),
            "fight" => PvpPacket.RankDuelFight(durationMs),
            "win" => PvpPacket.RankDuelWin(durationMs),
            "lose" => PvpPacket.RankDuelLose(durationMs),
            "draw" => PvpPacket.RankDuelDraw(durationMs),
            _ => null,
        };
        if (packet == null) {
            context.Console.WriteLine("Kind: round, fight, win, lose or draw.");
            return;
        }
        session.Send(packet);
    }
}
