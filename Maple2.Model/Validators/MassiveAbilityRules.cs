using Maple2.Model.Enum;

namespace Maple2.Model.Validators;

// The special abilities that count only in one massive event game's field. The client names them in tooltips and has
// no other reader (no CEquipment::GetAbilityRate call for 91-104, 161, 162); the server picks where they count
// (FORK.md). KR rolls the 91-104 pairs on seven gear items (ItemOptionRandom 11200069, 11800087, 11900089, 12088889,
// 12100073 and the others). specialabilitystringtable: the _exp lines are rates in thousandths ("exp {0:0.1f}% up"),
// the _msp lines a value ("move speed {0}% up"), added to MovementSpeed (100 points = the base speed).
// A game's field is the field its trigger scripts run the game in (the gameName of their StartMiniGame):
//  - OX Quiz (91, 98): 61000003, and the OX Quiz Stadium and Custom OX Quiz 61000011, 61000021 (oxquiz_ugc).
//  - Trap Master (92, 99): 61000001. Sole Survivor (93, 100): 61000002 (finalsurvivor).
//  - Crazy Runners (94, 101): 61000006 and the player-opened copy 81000003 (UserMassive_Crazyrunner).
//  - Ludibrium Escape (95, 102): 61000004 and 81000001. Spring Beach (96, 103): 61000007 and 81000002.
//  - Dance Dance Stop (97, 104): 61000008 and the holiday one 61000022 (christmasdancedancestop).
//  - Shanghai Crazy Runners (161, 162): 61000010 (shanghairunner).
// The wedding hall's dance game (84000007, WDdancedancestop) is a wedding room, not a massive event: no line counts there.
public static class MassiveAbilityRules {
    public static (SpecialAttribute Exp, SpecialAttribute Speed)? LinesOf(int mapId) => mapId switch {
        61000003 or 61000011 or 61000021 => (SpecialAttribute.MassiveOxExp, SpecialAttribute.MassiveOxSpeed),
        61000001 => (SpecialAttribute.MassiveTrapMasterExp, SpecialAttribute.MassiveTrapMasterSpeed),
        61000002 => (SpecialAttribute.MassiveFinalSurvivalExp, SpecialAttribute.MassiveFinalSurvivalSpeed),
        61000006 or 81000003 => (SpecialAttribute.MassiveCrazyRunnerExp, SpecialAttribute.MassiveCrazyRunnerSpeed),
        61000004 or 81000001 => (SpecialAttribute.MassiveEscapeExp, SpecialAttribute.MassiveEscapeSpeed),
        61000007 or 81000002 => (SpecialAttribute.MassiveSpringBeachExp, SpecialAttribute.MassiveSpringBeachSpeed),
        61000008 or 61000022 => (SpecialAttribute.MassiveDanceDanceExp, SpecialAttribute.MassiveDanceDanceSpeed),
        61000010 => (SpecialAttribute.MassiveShCrazyRunnerExp, SpecialAttribute.MassiveShCrazyRunnerSpeed),
        _ => null,
    };

    // The exp line of a mini game round's exp (miniGame and userMiniGame) in this field.
    public static SpecialAttribute? ExpAttribute(ExpType expType, int mapId) =>
        expType is ExpType.miniGame or ExpType.userMiniGame ? LinesOf(mapId)?.Exp : null;

    public static SpecialAttribute? SpeedAttribute(int? mapId) => mapId is { } id ? LinesOf(id)?.Speed : null;

    // The move speed lines are in the built stats only in their field, so entering a field whose line differs from
    // the one left rebuilds them.
    public static bool RebuildsOnEntry(int? leftMapId, int? enteredMapId) => SpeedAttribute(leftMapId) != SpeedAttribute(enteredMapId);
}
