using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Serilog;

namespace Maple2.Server.Game.Util;

// The PvP quest and trophy conditions (the client's conditionutil.cpp names them; it checks none itself), raised by
// the PvP zones and the rank duel with the map of the fight as the code:
// - pvp_participation for each participant when the fight starts (chosen here: the start, not the end, so a player
//   who leaves or falls still took part);
// - pvp_kill for the killer and pvp_die for the victim of a death in the fight;
// - pvp_win for the winners at the end (the free-for-all's first place, the Red Arena's champion, the winning team,
//   the duel's winner; a draw has no winner), with pvp_win_score (the team's lead) and pvp_win_time (the fight's
//   seconds) for a team win, and pvp_win_perfect (full health), pvp_win_with_buff (each effect held) and
//   pvp_win_with_grade (the grade after the win) for a rank duel win.
public static class PvpConditions {
    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(PvpConditions));

    public readonly record struct Update(ConditionType Type, long Target = 0);

    public static void Participation(FieldPlayer player, int mapId) => Raise(player, mapId, [new Update(ConditionType.pvp_participation)]);

    public static void Kill(FieldPlayer killer, int mapId) => Raise(killer, mapId, [new Update(ConditionType.pvp_kill)]);

    public static void Death(FieldPlayer victim, int mapId) => Raise(victim, mapId, [new Update(ConditionType.pvp_die)]);

    public static void Win(FieldPlayer winner, int mapId) => Raise(winner, mapId, [new Update(ConditionType.pvp_win)]);

    public static void TeamWin(FieldPlayer winner, int mapId, int lead, long fightMs) => Raise(winner, mapId, TeamWinUpdates(lead, fightMs));

    public static void DuelWin(FieldPlayer winner, int mapId, int? grade) {
        bool fullHealth = winner.Stats.Values[BasicAttribute.Health].Current >= winner.Stats.Values[BasicAttribute.Health].Total;
        IEnumerable<int> effects = winner.Buffs.EnumerateBuffs().Where(buff => buff.Enabled).Select(buff => buff.Id);
        Raise(winner, mapId, DuelWinUpdates(fullHealth, effects, grade));
    }

    // A team win: the win, its lead in points and the fight's whole seconds.
    public static List<Update> TeamWinUpdates(int lead, long fightMs) => [
        new Update(ConditionType.pvp_win),
        new Update(ConditionType.pvp_win_score, Math.Max(0, lead)),
        new Update(ConditionType.pvp_win_time, Math.Max(0, fightMs) / 1000),
    ];

    // A rank duel win: the win, perfect at full health, one with_buff per effect held, the grade when the result was
    // saved (null: no grade, the record did not change).
    public static List<Update> DuelWinUpdates(bool fullHealth, IEnumerable<int> effects, int? grade) {
        var updates = new List<Update> { new(ConditionType.pvp_win) };
        if (fullHealth) {
            updates.Add(new Update(ConditionType.pvp_win_perfect));
        }
        updates.AddRange(effects.Distinct().Select(effectId => new Update(ConditionType.pvp_win_with_buff, effectId)));
        if (grade is { } id) {
            updates.Add(new Update(ConditionType.pvp_win_with_grade, id));
        }
        return updates;
    }

    private static void Raise(FieldPlayer player, int mapId, IEnumerable<Update> updates) {
        foreach (Update update in updates) {
            try {
                player.Session.ConditionUpdate(update.Type, targetLong: update.Target, codeLong: mapId);
            } catch (Exception ex) {
                Logger.Error(ex, "PvP condition {Type} of {CharacterId} not raised", update.Type, player.Value.Character.Id);
            }
        }
    }
}
