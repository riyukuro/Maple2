using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model.Widget;

/// <summary>
/// widget_action TypingGame Start (widget_arg_num digits, widget_arg time in ms) opens the client's typing game
/// (Trigger Ui 12, CTriggerService::OnTypingGame); widget_value TypingGame Result is the client's answer.
/// </summary>
public class TypingGameWidget : Widget {
    public TypingGameState State { get; } = new();

    public TypingGameWidget(FieldManager field) : base(field) { }

    public override void Action(string function, int numericArg, string stringArg) {
        switch (function) {
            case "Start":
                if (!int.TryParse(stringArg, out int duration)) {
                    return;
                }
                State.Start();
                Field.Broadcast(TriggerPacket.UiTypingGame(numericArg, duration));
                break;
        }
    }

    public override bool Check(string name, string arg) {
        return name == "Result" && WidgetCondition.Matches(State.Result, arg);
    }
}
