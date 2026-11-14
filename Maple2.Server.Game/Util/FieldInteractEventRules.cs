using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The field interact objects of timeEventData type 4 (FORK.md "Field interact events"). The client reads only a
// row's targetFields and interactID (the field mission list, tableexporter.cpp); the server table's cycleTime,
// lifeTime, randomTime, prob, interactCount and variableCountByChannelCount settle the rest, read here.
public static class FieldInteractEventRules {
    // The row's points on a map: its targetSpawnPointIDs and the map's spawn points tagged with one of its
    // targetSpawnTags, only those the map has, in id order.
    public static List<int> Points(FieldInteractEventMetadata row, IEnumerable<MapMetadataSpawn> spawns, ICollection<int> mapPoints) {
        var points = new SortedSet<int>(row.SpawnPointIds.Where(mapPoints.Contains));
        if (row.SpawnTags.Length > 0) {
            foreach (MapMetadataSpawn spawn in spawns) {
                if (mapPoints.Contains(spawn.Id) && spawn.Tags.Any(tag => row.SpawnTags.Contains(tag.Trim()))) {
                    points.Add(spawn.Id);
                }
            }
        }
        return points.ToList();
    }

    // The objects a run puts on the channel's field: interactCount when the row has one, else
    // variableCountByChannelCount of the points (rounded up, at least 1); never more than the points.
    public static int Count(FieldInteractEventMetadata row, int points) {
        if (points <= 0) {
            return 0;
        }
        int count = row.InteractCount > 0 ? row.InteractCount : Math.Max(1, (int) Math.Ceiling(row.VariableCountByChannel * points - 1e-4));
        return Math.Min(count, points);
    }

    // count of the free points, at random, none twice.
    public static List<int> Pick(IReadOnlyList<int> free, int count, Random random) {
        var list = free.ToList();
        for (int i = list.Count - 1; i > 0; i--) {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list.Take(Math.Max(0, count)).ToList();
    }

    // The first run start after now on the row's grid (startTime, every cycleTime, before endTime); null when no
    // run is left. A row with no cycle runs once, at startTime.
    public static DateTime? NextRun(FieldInteractEventMetadata row, DateTime now) =>
        NextRun(row.StartTime, row.EndTime, row.CycleTime, now);

    // The same for any timeEventData row's grid (the type 6 field trigger events run on it too).
    public static DateTime? NextRun(DateTime startTime, DateTime endTime, TimeSpan cycleTime, DateTime now) {
        DateTime next;
        if (now < startTime) {
            next = startTime;
        } else if (cycleTime <= TimeSpan.Zero) {
            return null;
        } else {
            long k = (now - startTime).Ticks / cycleTime.Ticks + 1;
            next = startTime + TimeSpan.FromTicks(cycleTime.Ticks * k);
        }
        return next < endTime ? next : null;
    }

    // The run start on the grid at or before now whose objects still live (now before start + lifeTime); null when
    // none does.
    public static DateTime? CurrentRun(FieldInteractEventMetadata row, DateTime now) =>
        CurrentRun(row.StartTime, row.EndTime, row.CycleTime, row.LifeTime, now);

    public static DateTime? CurrentRun(DateTime startTime, DateTime endTime, TimeSpan cycleTime, TimeSpan lifeTime, DateTime now) {
        if (now < startTime || now >= endTime + lifeTime) {
            return null;
        }
        DateTime start = startTime;
        if (cycleTime > TimeSpan.Zero) {
            long k = (now - startTime).Ticks / cycleTime.Ticks;
            start = startTime + TimeSpan.FromTicks(cycleTime.Ticks * k);
        }
        return start < endTime && now < start + lifeTime ? start : null;
    }

    // Whether the hour of the day is in the row's eventDuration (StartHour:StartMin to EndHour:EndMin, the end minute
    // included, past midnight when the end comes first); all 0: the whole day.
    public static bool InWindow(FieldInteractEventMetadata row, DateTime time) {
        int start = row.DurationStartHour * 60 + row.DurationStartMin;
        int end = row.DurationEndHour * 60 + row.DurationEndMin;
        if (start == 0 && end == 0) {
            return true;
        }
        int minute = time.Hour * 60 + time.Minute;
        return start <= end ? minute >= start && minute <= end : minute >= start || minute <= end;
    }

    // prob: 100 every run, 0 (a row without prob) never, else the roll (0-99) under it.
    public static bool Runs(int probability, int roll) {
        return probability >= 100 || roll < probability;
    }
}
