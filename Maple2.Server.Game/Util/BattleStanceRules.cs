namespace Maple2.Server.Game.Util;

/// <summary>
/// The client's battle stance (PCBase::vfunc_65, the emotion it sets at a battle skill, stateskill.cpp:432, and takes
/// from the server's user battle packet, scenestategame.cpp:3277) refuses some sends before they go out: the closet
/// apply (UIClosetDialog::SendApply, uiclosetdialog.cpp:1497) and the skill tree apply (UISkillBookTreeDialog::CanApply,
/// uiskillbookautodistributedialog.cpp:612). The server refuses the same sends while its own InBattle is on, but only
/// when the client must already show it: a stance the server turned on less than <see cref="RaceGraceMs"/> ago may
/// not have reached the client yet, and an instrument play drops the client's stance on its own
/// (stateplayinstrumentdata.cpp:244) until the next battle action.
/// </summary>
public static class BattleStanceRules {
    public const int RaceGraceMs = 2000;

    public static bool Refuses(bool inBattle, bool clientDroppedStance, long stanceHeldMs) {
        return inBattle && !clientDroppedStance && stanceHeldMs >= RaceGraceMs;
    }
}
