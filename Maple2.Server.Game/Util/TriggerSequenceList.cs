namespace Maple2.Server.Game.Util;

/// <summary>
/// set_npc_emotion_sequence gives one name or a comma list ("Attack_01_C,Attack_Idle_A"): 28 placed actions on 11
/// maps give a list. The client plays a trigger emotion list on the player one name at a time, each once, and goes
/// back to idle after the last (StateTriggerEmotion::EndOfSequence @0x1417c47d0, mode 0); an npc's list is played
/// the same way on the server. Before, the whole list was looked up as one name and nothing was played.
/// </summary>
public static class TriggerSequenceList {
    /// <summary>The names of a list in order; blanks are dropped.</summary>
    public static string[] Split(string? value) {
        if (string.IsNullOrEmpty(value)) {
            return [];
        }
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The names an npc can play, in list order (a repeated name plays again); the others go to missing.</summary>
    public static string[] Playable(IEnumerable<string> names, Func<string, string?> lookup, List<string> missing) {
        var playable = new List<string>();
        foreach (string name in names) {
            string? sequence = lookup(name);
            if (sequence == null) {
                missing.Add(name);
            } else {
                playable.Add(sequence);
            }
        }
        return playable.ToArray();
    }
}
