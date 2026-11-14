using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Model.Widget;

/// <summary>
/// The OX quiz stadium's widget (map 61000011, trigger "main"): the script asks it for the host's moves and has it
/// show the host's question and answer. The game's state is the field's UgcOxEvent; a stadium without one (none is
/// made but by Factory.CreateUgcOxStadium) answers every value false and does nothing.
/// </summary>
public class OxQuizUgcWidget : Widget {
    private static readonly ILogger Logger = Log.Logger.ForContext<OxQuizUgcWidget>();

    public OxQuizUgcWidget(FieldManager field) : base(field) { }

    public override bool Check(string name, string arg) {
        if (Field.UgcOx is not { } ox) {
            return false;
        }
        lock (ox.Lock) {
            return name switch {
                "IsStarted" => ox.Phase == UgcOxPhase.Running,
                "IsCanceled" => ox.Canceled,
                "IsFinished" => ox.Finished,
                "IsQuizSubmit" => ox.Question != null,
                "IsAnswerSubmit" => ox.Answer != null,
                "IsRemoveWall" => ox.WallRemoved,
                "Correct" => ox.Answer == true,
                "Incorrect" => ox.Answer == false,
                _ => false,
            };
        }
    }

    public override void Action(string function, int numericArg, string stringArg) {
        if (Field.UgcOx is not { } ox) {
            return;
        }
        switch (function) {
            case "ShowHostUI":
                // The welcome dialog, while the event waits for the host's start.
                if (ox.Phase == UgcOxPhase.Welcome && !ox.Canceled) {
                    Field.Broadcast(Stage(ox, HomeActionPacket.UgcOxStageStep.Welcome));
                }
                break;
            case "StartRound":
                ox.NextRound();
                Field.Broadcast(Stage(ox, HomeActionPacket.UgcOxStageStep.Round));
                break;
            case "HostUIChange":
                HostUiChange(ox, stringArg);
                break;
            case "ShowQuiz":
                ox.ClearWallRemoved();
                if (ox.Question is { } question) {
                    Field.Broadcast(QuizEventPacket.Question(string.Empty, question, string.Empty, Duration(stringArg)));
                }
                break;
            case "ShowAnswer":
                if (ox.Answer is { } answer) {
                    Field.Broadcast(QuizEventPacket.Answer(answer, string.Empty, Duration(stringArg)));
                }
                break;
            case "EndRound":
                ox.EndRound();
                break;
            case "EndGame":
                EndGame(ox);
                break;
            case "MoveAllUser":
                // The way out of the stadium is the host's home (GameSession.ReturnField).
                foreach (FieldPlayer player in Field.Players.Values) {
                    player.Session.ReturnField();
                }
                break;
            case "CreateWall":
            case "PreJudge":
                // The script raises the walls and reads Correct and Incorrect itself; nothing is left to do here.
                break;
            default:
                Logger.Warning("Unknown function called on OxQuizUgcWidget: {Function}", function);
                break;
        }
    }

    // The client's view of the stadium (s2c 13) for the event's step; the prizes are 0 until fees are taken.
    public static Maple2.PacketLib.Tools.ByteWriter Stage(UgcOxEvent ox, HomeActionPacket.UgcOxStageStep step) {
        return HomeActionPacket.UgcOxStage(ox.HostAccountId, step, (byte) ox.Round, 0, 0);
    }

    private void HostUiChange(UgcOxEvent ox, string stepName) {
        if (!System.Enum.TryParse(stepName, out UgcOxHostStep step) || step == UgcOxHostStep.None) {
            Logger.Warning("Unknown OxQuizUGC host step {Step}", stepName);
            return;
        }
        ox.SetHostStep(step);
        // Only InputQuiz and SelectAnswer change the client's host dialog (sub 12 modes 1 and 3). Move and Judge
        // have no mode value the client acts on, so they are not sent (U4).
        HomeActionPacket.UgcOxHostMode mode;
        switch (step) {
            case UgcOxHostStep.InputQuiz:
                mode = HomeActionPacket.UgcOxHostMode.InputQuiz;
                break;
            case UgcOxHostStep.SelectAnswer:
                mode = HomeActionPacket.UgcOxHostMode.SelectAnswer;
                break;
            default:
                return;
        }
        FieldPlayer? host = Field.Players.Values.FirstOrDefault(player => player.Value.Character.Id == ox.HostCharacterId);
        host?.Session.Send(HomeActionPacket.UgcOxHostDialog(mode, (byte) ox.Round));
    }

    private void EndGame(UgcOxEvent ox) {
        // The script runs EndGame on each of its ends; the event ends once.
        if (!ox.End()) {
            return;
        }
        Logger.Information("OX quiz event of {HostCharacterId} ended at round {Round} (canceled: {Canceled}, entrants: {Count})",
            ox.HostCharacterId, ox.Round, ox.Canceled, ox.EntrantCount);
        Field.Broadcast(Stage(ox, HomeActionPacket.UgcOxStageStep.Close));
        Field.FieldFactory.RemoveUgcOx(ox);
    }

    private static int Duration(string arg) {
        return int.TryParse(arg, out int duration) ? duration : 15;
    }
}
