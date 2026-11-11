using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model.Widget;

/// <summary>Round settings, results and the CurrentRound, GameClear and GameFail values (see <see cref="RoundCounter"/>).</summary>
public class RoundWidget : Widget {
    public RoundCounter Rounds { get; } = new();

    public RoundWidget(FieldManager field) : base(field) { }

    public override void Action(string function, int numericArg, string stringArg) {
        int value = int.TryParse(stringArg, out int parsed) ? parsed : numericArg;
        Rounds.Action(function, value);
    }

    public override bool Check(string name, string arg) {
        return WidgetCondition.Matches(Rounds.Value(name), arg);
    }
}
