namespace Maple2.Model.Validators;

// The world map's crowd view (RequestWorldmap 1, answered with Worldmap 1). The client reads {field, ranking, channel}
// records (WorldMapManager::LoadAddtionalInfos) and its IconTile draws only rankings "1", "2" and "3" (the "1st",
// "2nd" and "3rd" badge, its tooltip the channel); any other value draws nothing. Nothing in the client says how the
// three are picked, so this is chosen here: an online player outside a dungeon counts for their field and channel, a
// field's count is that of its busiest channel (the one the tooltip names; the lower channel on a tie), and the three
// busiest fields of the asked world with at least one player rank 1 to 3 (the lower field id on a tie).
public static class WorldMapPopulationRules {
    public const int Ranked = 3;

    public static IEnumerable<(int MapId, short Channel, int Count)> Count(IEnumerable<(int MapId, short Channel, int DungeonId)> online) {
        return online.Where(player => player.Channel >= 0 && player.MapId > 0 && player.DungeonId == 0)
            .GroupBy(player => (player.MapId, player.Channel))
            .Select(group => (group.Key.MapId, group.Key.Channel, group.Count()));
    }

    public static IList<(int MapId, int Rank, short Channel)> Rank(IEnumerable<(int MapId, short Channel, int Count)> counts, Func<int, bool> onWorld) {
        return counts.Where(entry => entry.Count > 0 && onWorld(entry.MapId))
            .GroupBy(entry => entry.MapId)
            .Select(group => group.OrderByDescending(entry => entry.Count).ThenBy(entry => entry.Channel).First())
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.MapId)
            .Take(Ranked)
            .Select((entry, index) => (entry.MapId, index + 1, entry.Channel))
            .ToList();
    }
}
