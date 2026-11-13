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

    /// <summary>
    /// The owner's "send everyone out" (RequestCube 0x31): every player of another account leaves, and only
    /// when the sender owns the home.
    /// </summary>
    public static bool SentOutWithAll(long ownerAccountId, long senderAccountId, long visitorAccountId) {
        return ownerAccountId == senderAccountId && visitorAccountId != ownerAccountId;
    }

    /// <summary>
    /// How long a visitor told to leave (ResponseCube 0x1a, s_home_password_user_out_chat_msg) may stay before
    /// the server sends it out. The box's Exit button leaves at once (RequestMoveField 1).
    /// </summary>
    public static readonly TimeSpan SendOutDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The visitors the timer still sends out: those whose field entry is still in the home. An entry is the
    /// object id the home gave the visitor and the field player made for it; a visitor that left, or left and
    /// came back (a new entry), is not touched.
    /// </summary>
    public static List<T> StillToSendOut<T>(IEnumerable<KeyValuePair<int, T>> told, IReadOnlyDictionary<int, T> inField) where T : class {
        var result = new List<T>();
        foreach ((int objectId, T entry) in told) {
            if (inField.TryGetValue(objectId, out T? now) && ReferenceEquals(now, entry)) {
                result.Add(entry);
            }
        }
        return result;
    }
}
