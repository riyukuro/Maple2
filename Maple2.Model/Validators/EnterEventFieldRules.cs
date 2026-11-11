namespace Maple2.Model.Validators;

// EnterEventField: the move from the event field popup. The packet has no fields (uientereventfielddialog.cpp:203).
public static class EnterEventFieldRules {
    public enum Result {
        Move,
        NoField,
        Dead,
        NoEvent,
        SameField,
        InBattle,
    }

    public static Result Decide(bool hasField, bool isDead, bool hasEvent, bool sameField, bool inBattle) {
        if (!hasField) {
            return Result.NoField;
        }
        if (isDead) {
            return Result.Dead;
        }
        if (!hasEvent) {
            return Result.NoEvent;
        }
        if (sameField) {
            return Result.SameField;
        }
        return inBattle ? Result.InBattle : Result.Move;
    }
}
