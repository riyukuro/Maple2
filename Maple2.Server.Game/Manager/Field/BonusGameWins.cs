using System.Numerics;

namespace Maple2.Server.Game.Manager.Field;

// The BonusGame wins a trigger's bonus_game_reward condition (52000006/7 box 101, 80000003 box 100, all type 1) waits
// for: a spin in this field that won a notice row (notice="1", the only row flag) is kept for the character, and the
// condition takes the first one whose player is inside the box, which plays the celebration once. Chosen here: type 1
// is a notice row; one win per spin request, however many notice rows it won; a win is used up by the condition that
// sees it; the wins of players no longer in the field are dropped when the condition looks; at most MaxWins are kept.
public sealed class BonusGameWins {
    public const int NoticeType = 1;
    public const int MaxWins = 32;

    private readonly object mutex = new();
    private readonly List<(long CharacterId, int Type)> wins = [];

    public void Add(long characterId, int type) {
        lock (mutex) {
            if (wins.Count >= MaxWins) {
                wins.RemoveAt(0);
            }
            wins.Add((characterId, type));
        }
    }

    // position: where the character is in the field, null when it is not there. inBox: whether a position is in the box.
    public bool Take(int type, Func<long, Vector3?> position, Func<Vector3, bool> inBox) {
        lock (mutex) {
            wins.RemoveAll(win => position(win.CharacterId) == null);
            int index = wins.FindIndex(win => win.Type == type && inBox(position(win.CharacterId)!.Value));
            if (index < 0) {
                return false;
            }
            wins.RemoveAt(index);
            return true;
        }
    }

    public int Count {
        get {
            lock (mutex) return wins.Count;
        }
    }
}
