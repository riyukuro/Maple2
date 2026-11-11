namespace Maple2.Server.Game.Util;

/// <summary>
/// What the home owner's three player-menu entries (HomeAction 1, 2, 3) do to a visitor.
/// The client shows them only to the owner standing in the own home, never on the owner's own name:
/// 1 and 2 on a living visitor, 3 and 2 on a dead one.
/// </summary>
public static class HomeVisitorRules {
    public enum Action {
        None,
        Kill,
        Kick,
        Revive,
    }

    /// <param name="command">the sub-type byte: 1 kill, 2 send out, 3 revive</param>
    /// <param name="ownerInOwnHome">the sender is in a home field of the sender's own account</param>
    /// <param name="targetInField">a player with the sent name is in that field</param>
    /// <param name="targetIsSender">the name is the sender's own</param>
    /// <param name="targetDead">the target is dead</param>
    public static Action Pick(byte command, bool ownerInOwnHome, bool targetInField, bool targetIsSender, bool targetDead) {
        if (!ownerInOwnHome || !targetInField || targetIsSender) {
            return Action.None;
        }

        return command switch {
            1 => targetDead ? Action.None : Action.Kill,
            2 => Action.Kick,
            3 => targetDead ? Action.Revive : Action.None,
            _ => Action.None,
        };
    }
}
