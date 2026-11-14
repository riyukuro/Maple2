using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// The seat a player boards on another player's multi-seat mount, or -1. The client asks only for a mount the
// joiner may board (MyPC::FUN_1415279d0, HoldPCAction): the joiner is alive, the owner is a live player (its
// nearest-actor search skips dead ones) on a mount with a free seat.
public static class RideShare {
    public static int Seat(bool joinerDead, bool ownerDead, bool ownerRidesAlone, IReadOnlyList<int> passengers) {
        if (joinerDead || ownerDead || ownerRidesAlone) {
            return -1;
        }

        for (int i = 0; i < passengers.Count; i++) {
            if (passengers[i] == 0) {
                return i;
            }
        }
        return -1;
    }

    // With the RideShareAuthority feature on (the ingested feature.xml), MyPC::FUN_1415279d0 lets the joiner board
    // only when the owner is an accepted buddy (CBuddyManager::FUN_140ae4d70, keyed by the buddy entry's character
    // id), a member of the joiner's party (FUN_1415278d0), or in the joiner's guild (equal, non-empty guild names).
    // With it off, anyone may board and the server does not ask.
    public static bool MayBoard(bool buddy, bool partyMember, long joinerGuildId, long ownerGuildId) {
        return buddy || partyMember || (joinerGuildId != 0 && joinerGuildId == ownerGuildId);
    }

    // RequestRide mode 3: the client boards only the owner FindHoldTarget finds (HoldPCAction, actionkeyobject.cpp:2320;
    // the ride-off path, mypc.cpp:3448, searches 20 further): one with a hold node within FindHoldTargetRange (KR 230)
    // of the joiner (WorldService::FindNearestActor, worldservice_1.cpp:5143; PC::vfunc_118, pc.cpp:1307). The nodes
    // sit on the mount, away from the owner's position, which is all the server holds.
    // The range is the ingested constant; 230 (KR) when a locale's table has none.
    public const float DefaultHoldTargetRange = 230f;
    public const float RideOffSearch = 20f;
    public const float JoinReach = DefaultHoldTargetRange + RideOffSearch;
    // Server rule: room for a hold node's offset from the owner on a large mount.
    public const float HoldNodeAllowance = 200f;

    public static float Reach(float findHoldTargetRange) =>
        (findHoldTargetRange > 0 ? findHoldTargetRange : DefaultHoldTargetRange) + RideOffSearch + HoldNodeAllowance;

    public static bool InJoinReach(System.Numerics.Vector3 joiner, System.Numerics.Vector3 owner, float findHoldTargetRange = 0f) =>
        Maple2.Model.Validators.InteractRules.InReach(joiner, owner, Reach(findHoldTargetRange));

    // RequestRide mode 0: a mount item is summoned by a living player who is not on a ride, in a field that
    // allows rides.
    public static bool CanStart(bool dead, bool fieldAllowsRide, bool riding) => !dead && fieldAllowsRide && !riding;

    // RequestRide mode 2 (swap the mount item): only the owner of a mount item ride. A passenger's request would
    // drop its ride while it still holds a seat, and a battle or object mount is not an item to swap.
    public static bool CanChange(bool owner, RideOnType type) => owner && type == RideOnType.Default;

    // The passengers on a mount's seats; the seats are emptied. The owner's dismount or field leave takes everyone
    // off: KMS2 sends ResponseRide mode 5 (the client's ClearPassengers) before the owner's Stop.
    public static int[] ClearSeats(int[] passengers) {
        int[] riders = passengers.Where(objectId => objectId != 0).ToArray();
        Array.Clear(passengers);
        return riders;
    }

    // In KMS2 a battle or object mount ends with ride-off type Dead before the DeadUser packet; a mount item
    // ride stays.
    public static bool DismountsOnDeath(RideOnType type) => type is RideOnType.Battle or RideOnType.Object;

    // A mount is kept across a field move (KMS2's own FieldAddUser carries the ride), except into a field
    // without Limit.Ride: KMS2 then sends ride-off type DisableField before the rider's FieldAddUser.
    public static bool DismountsOnEnter(bool riding, bool fieldAllowsRide) => riding && !fieldAllowsRide;

    // RideConsumeEp (empty body) is the owner's dash: KMS2 takes the ride's runXConsumeEp from stamina each time
    // (a 15 ride: 120, 105, 90, 75). A passenger pays nothing.
    public static int DashStamina(bool owner, long runXStamina) => owner && runXStamina > 0 ? (int) runXStamina : 0;
}
