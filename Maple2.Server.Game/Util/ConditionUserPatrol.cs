namespace Maple2.Server.Game.Util;

/// <summary>
/// The patrol_condition_user trigger action: move_user_path for only the players who carry the additional effect.
/// In 02020310_bf and 52100302_qd each of four interact objects (12000400 and its neighbours) gives one of the
/// buffs 73000005-73000008, and field_5 then sends the holders of each buff along that route's patrol
/// (MS2PatrolData_01-08). move_1-move_4 wait for a holder in box 10001; 73000009 cancels the four buffs.
/// patrol_index only repeats the patrol name's number. Without an effect id every player moves, as with move_user_path.
/// </summary>
public static class ConditionUserPatrol {
    public static bool Moves(int additionalEffectId, Func<int, bool> hasBuff) =>
        additionalEffectId <= 0 || hasBuff(additionalEffectId);
}
