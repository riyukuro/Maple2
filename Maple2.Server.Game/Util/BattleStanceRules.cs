using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The client's battle stance (PCBase::vfunc_65, the emotion it sets at a battle skill, stateskill.cpp:432, and takes
/// from the server's user battle packet, scenestategame.cpp:3277) refuses some sends before they go out: the closet
/// apply (UIClosetDialog::SendApply, uiclosetdialog.cpp:1497) and the skill tree apply (UISkillBookTreeDialog::CanApply,
/// uiskillbookautodistributedialog.cpp:612). The server refuses the same sends while its own InBattle is on, but only
/// when the client must already show it: a stance the server turned on less than <see cref="RaceGraceMs"/> ago may
/// not have reached the client yet, and the client drops its stance on its own until the next battle action: at an
/// instrument play (stateplayinstrumentdata.cpp:244) and in three idle sub-states (<see cref="DropsStance"/>).
/// </summary>
public static class BattleStanceRules {
    public const int RaceGraceMs = 2000;

    /// <summary>The idle sub-states whose animation drops the client's stance (StateIdle::vfunc_9, stateidle.cpp:386-408:
    /// 0xa fitting, 0xb fitting basic, 0x16 listen music). The client's sync carries the idle sub-state
    /// (StateIdle::vfunc_22 syncs when it changes), so the server sees the player enter one.</summary>
    public static bool DropsStance(ActorState state, ActorSubState subState) {
        return state == ActorState.Idle
               && subState is ActorSubState.Idle_Fitting or ActorSubState.Idle_Fitting_Basic or ActorSubState.Idle_ListenMusic;
    }

    public static bool Refuses(bool inBattle, bool clientDroppedStance, long stanceHeldMs) {
        return inBattle && !clientDroppedStance && stanceHeldMs >= RaceGraceMs;
    }
}
