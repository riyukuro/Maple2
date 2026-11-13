using Maple2.Model.Enum;
using Maple2.Model.Game.Pvp;
using Maple2.Server.Game.Model;
using Serilog;

namespace Maple2.Server.Game.Util;

// The PvP quest and trophy conditions (the client's conditionutil.cpp names them; it checks none itself), raised by
// the PvP zones and the rank duel with the map of the fight as the code:
// - pvp_participation for each participant when the fight starts (chosen here: the start, not the end, so a player
//   who leaves or falls still took part);
// - pvp_kill for the killer and pvp_die for the victim of a death in the fight (in the rank duel and the Red Arena
//   face-off the killer is the opponent only when it hit the victim within PvpZoneState.KillCreditMs, as in the
//   free-for-all and Treasure Island);
// - pvp_win for the winners at the end (the free-for-all's first place, the Red Arena's champion, the winning team,
//   the duel's winner; a draw has no winner), with pvp_win_score (the team's lead) and pvp_win_time (the fight's
//   seconds) for a team win, and pvp_win_perfect (full health), pvp_win_with_buff (each effect held) and
//   pvp_win_with_grade (the grade after the win) for a rank duel win. A rank duel counts only once the fight started:
//   a walkover (the opponent never came or left before the fight) raises nothing, and an opponent that left during
//   the fight gives pvp_win alone (chosen here: no proof the rest was earned).
public static class PvpConditions {
    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(PvpConditions));

    public readonly record struct Update(ConditionType Type, long Target = 0);

    public static void Participation(FieldPlayer player, int mapId) => Raise(player, mapId, [new Update(ConditionType.pvp_participation)]);

    public static void Kill(FieldPlayer killer, int mapId) => Raise(killer, mapId, [new Update(ConditionType.pvp_kill)]);

    public static void Death(FieldPlayer victim, int mapId) => Raise(victim, mapId, [new Update(ConditionType.pvp_die)]);

    public static void Win(FieldPlayer winner, int mapId) => Raise(winner, mapId, [new Update(ConditionType.pvp_win)]);

    public static void TeamWin(FieldPlayer winner, int mapId, int lead, long fightMs) => Raise(winner, mapId, TeamWinUpdates(lead, fightMs));

    public static void DuelWin(FieldPlayer winner, int mapId, RankDuelWinBy winBy, int? grade) {
        if (winBy is not (RankDuelWinBy.Death or RankDuelWinBy.LeftFight)) {
            return;
        }
        bool fullHealth = winner.Stats.Values[BasicAttribute.Health].Current >= winner.Stats.Values[BasicAttribute.Health].Total;
        IEnumerable<int> effects = winner.Buffs.EnumerateBuffs().Where(buff => buff.Enabled).Select(buff => buff.Id);
        Raise(winner, mapId, DuelWinUpdates(winBy, fullHealth, effects, grade));
    }

    // A team win: the win, its lead in points and the fight's whole seconds.
    public static List<Update> TeamWinUpdates(int lead, long fightMs) => [
        new Update(ConditionType.pvp_win),
        new Update(ConditionType.pvp_win_score, Math.Max(0, lead)),
        new Update(ConditionType.pvp_win_time, Math.Max(0, fightMs) / 1000),
    ];

    // A rank duel win by the loser's death: the win, perfect at full health, one with_buff per effect held, the grade
    // when the result was saved (null: no grade, the record did not change). A loser that left the fight: the win
    // only. A walkover: nothing.
    public static List<Update> DuelWinUpdates(RankDuelWinBy winBy, bool fullHealth, IEnumerable<int> effects, int? grade) {
        switch (winBy) {
            case RankDuelWinBy.LeftFight:
                return [new Update(ConditionType.pvp_win)];
            case not RankDuelWinBy.Death:
                return [];
        }
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
