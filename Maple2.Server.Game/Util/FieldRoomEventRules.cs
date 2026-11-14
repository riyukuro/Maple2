using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The field random room portals of timeEventData type 2 (FORK.md "Field room events"): the bonus map hats of the
// fields and the antlion holes. The client does not read these rows. Before, every bonus map region of a field opened
// its own hat at a random 5 minutes to 8 hours, to any pocket realm of the continent, and the antlion regions none.
public static class FieldRoomEventRules {
    // The row's points on a map: the map's spawn regions tagged with one of its targetSpawnTags, only those the map
    // has, in id order.
    public static List<int> Points(FieldRoomEventMetadata row, IEnumerable<MapMetadataSpawn> spawns, ICollection<int> mapPoints) {
        var points = new SortedSet<int>();
        foreach (MapMetadataSpawn spawn in spawns) {
            if (mapPoints.Contains(spawn.Id) && spawn.Tags.Any(tag => row.SpawnTags.Contains(tag.Trim()))) {
                points.Add(spawn.Id);
            }
        }
        return points.ToList();
    }

    public static DateTime? CurrentRun(FieldRoomEventMetadata row, DateTime now) =>
        FieldInteractEventRules.CurrentRun(row.StartTime, row.EndTime, row.CycleTime, row.LifeTime, now);

    // The next run start after now, late by a part of randomTime (randomPart, 0 to 1); null when none is left.
    public static DateTime? NextRun(FieldRoomEventMetadata row, DateTime now, double randomPart) =>
        FieldInteractEventRules.NextRun(row.StartTime, row.EndTime, row.CycleTime, row.RandomTime, now, randomPart);
}
