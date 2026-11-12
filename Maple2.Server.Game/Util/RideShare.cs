using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

// The seat a player boards on another player's multi-seat mount, or -1. The client asks only for a mount the
// joiner may board (MyPC::FUN_1415279d0, HoldPCAction): the joiner is alive, the owner is a live player (its
// nearest-actor search skips dead ones) on a mount with a free seat. Its friend / party / same-guild check runs
// only with the RideShareAuthority feature, whose feature.xml level is 99 for KR and NA (above the live setting),
// so in those locales anyone may board, and the server keeps no such check either.
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
