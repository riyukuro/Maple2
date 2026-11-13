namespace Maple2.Server.Game.Util;

/// <summary>
/// The rules of an AI ride node (&lt;node name="ride" type="slave" .../&gt;). Only the Blackbean boss AIs use it
/// (11 nodes in each of AI_Blackbean_Phase01_Normal/Hard): the boss summons its car as a slave, rides it with
/// rideNpcIDs listing the car ids, and rides off (isRideOff="true") before each jump down. The client has no
/// npc-on-npc ride (its ride packets are for players on the riding table), so the server keeps the rider on its car.
/// </summary>
public static class AiRide {
    // The index of the slave the rider sits on: the first slave in summon order whose npc id is one of the node's
    // rideNpcIDs; -1 when none is alive (the rider then stays where it is until a car is summoned again).
    public static int Vehicle(IReadOnlyList<int> slaveNpcIds, IReadOnlyCollection<int> rideNpcIds) {
        for (int i = 0; i < slaveNpcIds.Count; i++) {
            if (Rides(slaveNpcIds[i], rideNpcIds)) {
                return i;
            }
        }
        return -1;
    }

    public static bool Rides(int npcId, IReadOnlyCollection<int> rideNpcIds) => rideNpcIds.Contains(npcId);

    // What a ride node leaves on the rider: isRideOff="true" clears the ride, any other node rides the listed ids
    // (an empty list rides nothing).
    public static int[]? Riding(bool isRideOff, int[] rideNpcIds) => isRideOff || rideNpcIds.Length == 0 ? null : rideNpcIds;
}
