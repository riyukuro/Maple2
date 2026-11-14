using Maple2.Model.Metadata;

namespace Maple2.Model.Game;

// The rules of a world champion season (WorldChampionTable) the World and Game servers share. The step is the npc's
// step that is out (1 = the first); a season whose last step's npc lost its health has Steps.Count + 1, which the
// client shows as cleared (index >= size: s_world_champion_clear_title, uiworldchampionrewarddialog.cpp:320).
public static class WorldChampionRules {
    // KR's constants.xml: worldChampionRewardDays 7, the days after a season's end its rewards are mailed
    // (s_world_champion_explain3: "received by mail Monday to Sunday of the next week").
    public const int DefaultRewardDays = 7;

    // Feature 199 (featuremanager.cpp:527): the client's menu entry (gameeventmanagercli.cpp:936) shows only with it on.
    public const string Feature = "WorldChampionMushroom";

    public static bool On(FeatureTable features) => features.IsOn(Feature);

    // The season on at the time (begin and end included); of two at once the lower id.
    public static WorldChampionTable.Season? Current(WorldChampionTable table, DateTime now) {
        return table.Entries.Values.Concat(Rolled(table, now, now))
            .Where(season => season.Begin <= now && now <= season.End)
            .MinBy(season => season.Id);
    }

    // The next season to begin after the time. The rolled seasons repeat each week, so one begins within the week.
    public static WorldChampionTable.Season? Next(WorldChampionTable table, DateTime now) {
        return table.Entries.Values.Concat(Rolled(table, now, now.AddDays(7)))
            .Where(season => season.Begin > now)
            .OrderBy(season => season.Begin)
            .ThenBy(season => season.Id)
            .FirstOrDefault();
    }

    // A season by its id: a row of the table or a rolled one.
    public static WorldChampionTable.Season? Find(WorldChampionTable table, int id) {
        if (table.Entries.TryGetValue(id, out WorldChampionTable.Season? season)) {
            return season;
        }
        (int lastId, List<WorldChampionTable.Season> pattern) = Pattern(table);
        if (pattern.Count == 0 || id <= lastId) {
            return null;
        }
        int index = id - lastId - 1;
        return RollWeek(pattern[index % pattern.Count], index / pattern.Count + 1, id);
    }

    // The schedule after the table, the server's (the client has none: every KR and NA season ended in 2020, and the
    // season window shows the id only as the season's number, s_world_champion_season_info,
    // uiworldchampionrewarddialog.cpp:295): the seasons that begin in the week up to the last season's begin (KR: 11
    // and 12, Saturday and Sunday 19:00-22:00) repeat every week after it, with their map, steps, rewards and mail;
    // the ids go on from the table's highest id in begin order (KR: 13 the Saturday after season 12). The feature
    // is switched off with WorldChampionMushroom.
    private static (int LastId, List<WorldChampionTable.Season> Pattern) Pattern(WorldChampionTable table) {
        if (table.Entries.Count == 0) {
            return (0, []);
        }
        DateTime last = table.Entries.Values.Max(season => season.Begin);
        List<WorldChampionTable.Season> pattern = table.Entries.Values
            .Where(season => season.Begin > last.AddDays(-7))
            .OrderBy(season => season.Begin)
            .ThenBy(season => season.Id)
            .ToList();
        return (table.Entries.Keys.Max(), pattern);
    }

    private static WorldChampionTable.Season RollWeek(WorldChampionTable.Season season, int weeks, int id) {
        return season with { Id = id, Begin = season.Begin.AddDays(7 * weeks), End = season.End.AddDays(7 * weeks) };
    }

    // The rolled seasons that are on at some time from from to to.
    private static IEnumerable<WorldChampionTable.Season> Rolled(WorldChampionTable table, DateTime from, DateTime to) {
        (int lastId, List<WorldChampionTable.Season> pattern) = Pattern(table);
        if (pattern.Count == 0) {
            yield break;
        }
        // The first week in which some season can still be on at from: the one of the latest end.
        DateTime latestEnd = pattern.Max(season => season.End);
        int week = Math.Max(1, (int) Math.Floor((from - latestEnd).TotalDays / 7));
        for (; ; week++) {
            if (pattern[0].Begin.AddDays(7 * week) > to) {
                yield break;
            }
            for (int i = 0; i < pattern.Count; i++) {
                WorldChampionTable.Season season = RollWeek(pattern[i], week, lastId + (week - 1) * pattern.Count + i + 1);
                if (season.Begin <= to && season.End >= from) {
                    yield return season;
                }
            }
        }
    }

    // The World server saves a season's last damage within seconds of its end (WorldChampionLookup.SaveInterval); the
    // rewards wait this long after the end, so that a login reads the season's final step and damage.
    public static readonly TimeSpan RewardDelay = TimeSpan.FromMinutes(1);

    // The seasons whose rewards are mailed at the time: ended (RewardDelay before), and within the reward days after
    // the end.
    public static IEnumerable<WorldChampionTable.Season> RewardDue(WorldChampionTable table, DateTime now, int rewardDays) {
        int days = rewardDays > 0 ? rewardDays : DefaultRewardDays;
        return table.Entries.Values.Concat(Rolled(table, now.AddDays(-days), now))
            .Where(season => season.End + RewardDelay < now && now <= season.End.AddDays(days))
            .OrderBy(season => season.Id);
    }

    public static bool Cleared(WorldChampionTable.Season season, int step) => step > season.Steps.Count;

    // The npc of the step, or null once the season is cleared.
    public static WorldChampionTable.Step? StepOf(WorldChampionTable.Season season, int step) {
        return step >= 1 && step <= season.Steps.Count ? season.Steps[step - 1] : null;
    }

    // The step after damage dealt to the npc of damageStep, whose health is maxHealth: damage to a step that is no
    // longer out (another channel cleared it first) changes nothing, and the damage past the health is not carried
    // to the next step's npc.
    public static (int Step, long StepDamage) Apply(WorldChampionTable.Season season, int step, long stepDamage, int damageStep, long damage, long maxHealth) {
        if (Cleared(season, step) || damageStep != step || damage <= 0) {
            return (step, stepDamage);
        }
        stepDamage += damage;
        if (maxHealth > 0 && stepDamage >= maxHealth) {
            return (step + 1, 0);
        }
        return (step, stepDamage);
    }

    // The health the step's npc shows: its health less the damage all channels dealt to it, never under 1 (the npc
    // goes once the World server counts the step cleared).
    public static long Health(long maxHealth, long stepDamage) {
        return Math.Max(1, maxHealth - Math.Max(0, stepDamage));
    }

    // The reward content row of a season reached at the step: the row of the last step cleared (each row holds the
    // rewards of the steps cleared up to it, s_world_champion_reward_title), 0 when none was cleared.
    public static int RewardId(WorldChampionTable.Season season, int step) {
        int cleared = Math.Min(step - 1, season.Steps.Count);
        return cleared >= 1 ? season.Steps[cleared - 1].RewardId : 0;
    }

    // A player is rewarded for a season at the season's damage (rewardDamage) or more.
    public static bool Rewarded(WorldChampionTable.Season season, long damage) => damage > 0 && damage >= season.RewardDamage;
}

// A character's damage to the npcs of a world champion season.
public record WorldChampionDamage(long AccountId, long CharacterId, long Damage);
