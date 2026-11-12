namespace Maple2.Model.Game.Pvp;

// The PvP points (Pvp 0x64) of two fighters. A PC with a point takes it as its team (PC::vfunc_116), and the client's
// hit test for skill targets (basecollision.cpp @0x1409ff8d0) never hits a target of team 2 and lets an
// attacker of team 2 hit anyone; a PC without a point is team 1. So 1 and 2 are no points to give: the fighters are
// numbered from 101, as the free-for-all numbers its participants.
public static class PvpPoints {
    public const int First = 101;
    public const int Second = 102;
}
