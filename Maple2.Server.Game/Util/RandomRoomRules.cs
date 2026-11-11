using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The AI createRandomRoom node (table/Server/room_random.xml). A randomroom row's prob is read out of 10000
/// (row 1 gives 10000, row 3 gives 1500); a room is then picked by its weight. The client sends no part of it.
/// </summary>
public static class RandomRoomRules {
    public const int ProbScale = 10000;

    /// <summary>Whether the portal opens, for a roll in [0, ProbScale).</summary>
    public static bool Opens(int prob, int roll) => roll < prob;

    public static int TotalWeight(IEnumerable<RandomRoomMetadata.Ref> refs) => refs.Where(entry => entry.Weight > 0).Sum(entry => entry.Weight);

    /// <summary>The room of a roll in [0, TotalWeight); null with no weight.</summary>
    public static int? PickRoom(IEnumerable<RandomRoomMetadata.Ref> refs, int roll) {
        foreach (RandomRoomMetadata.Ref entry in refs) {
            if (entry.Weight <= 0) {
                continue;
            }
            if (roll < entry.Weight) {
                return entry.RoomId;
            }
            roll -= entry.Weight;
        }
        return null;
    }

    /// <summary>The room takes maxUserCount players through its portal (0 = no limit).</summary>
    public static bool Full(int entered, int maxUserCount) => maxUserCount > 0 && entered >= maxUserCount;
}
