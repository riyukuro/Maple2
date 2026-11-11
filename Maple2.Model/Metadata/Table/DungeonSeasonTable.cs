namespace Maple2.Model.Metadata;

// table/dungeonseason.xml: the dungeon rank seasons (the client's DungeonSeason::Load, dungeonroom.cpp). Each
// row holds its period, the season's maximum total score, the mail sent with the weekly reward, the day of the
// week the season week ends on (dayOfWeeks, Sunday = 1 in the client's CMsTime::GetDayOfWeek convention) and
// its rewards, highest requireScore first.
public record DungeonSeasonTable(IReadOnlyList<DungeonSeasonTable.Entry> Entries) : Table {
    public record Entry(
        int SeasonId,
        DateTime StartTime,
        DateTime EndTime,
        int MaxSeasonScore,
        int SystemMailId,
        int DayOfWeeks,
        Reward[] Rewards);

    public record Reward(int RequireScore, int ItemId);

    // DungeonSeason::GetCurrentSeason: the first row whose period holds the time.
    public Entry? Current(DateTime now) {
        foreach (Entry entry in Entries) {
            if (entry.StartTime <= now && now <= entry.EndTime) {
                return entry;
            }
        }
        return null;
    }
}
