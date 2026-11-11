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
}
