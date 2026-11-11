using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The alliance missions: the alliance quests (type 9) of repeat 2 (daily) and of repeat 3 without a group (weekly) a
// player may take. The client offers and starts such a quest only when it is in the list Quest 0x1F (daily) or 0x20
// (weekly) sent (CQuestManager::vfunc_1, questmanager_2.cpp:555); the lists are the server's pick by the methods of
// table/famepickmethod.xml (FameTable). How the original server picked is not in the client: here each list is a
// shuffle seeded by the character and the day (daily) or the start of the week (weekly), so the picks stay the same
// until the period ends, with no stored state.
public static class AllianceMissions {
    public readonly record struct Candidate(int Id, AllianceType Alliance, int Rank);

    // The quests whose repeat a list holds: 2 daily, 3 weekly without a group.
    public static bool IsMission(QuestMetadata metadata) {
        QuestMetadataBasic basic = metadata.Basic;
        return basic.Type == QuestType.AllianceQuest
               && (basic.Repeat == 2 || basic.Repeat == 3 && metadata.Require.GroupId == 0);
    }

    // One method's pick from the candidates (the quests of its repeat the player can start). Combination: each rank's
    // count from the listed alliances together, an alliance at most its MaxCount (0 no limit); otherwise each listed
    // alliance gets each rank's count. Fewer candidates than the count gives them all.
    public static List<int> Pick(FameTable.PickMethod method, IEnumerable<Candidate> candidates, int seed) {
        var random = new Random(seed);
        Dictionary<AllianceType, int> maxCounts = method.Alliances
            .GroupBy(alliance => alliance.Type)
            .ToDictionary(group => group.Key, group => group.First().MaxCount);
        List<Candidate> pool = candidates.Where(candidate => maxCounts.ContainsKey(candidate.Alliance))
            .DistinctBy(candidate => candidate.Id)
            .OrderBy(candidate => candidate.Id)
            .ToList();

        var picks = new List<int>();
        if (method.Combination) {
            var taken = new Dictionary<AllianceType, int>();
            for (int rank = 0; rank < method.PickCountByRank.Length; rank++) {
                int left = method.PickCountByRank[rank];
                foreach (Candidate candidate in Shuffle(pool.Where(candidate => candidate.Rank == rank).ToList(), random)) {
                    if (left <= 0) {
                        break;
                    }
                    int max = maxCounts[candidate.Alliance];
                    taken.TryGetValue(candidate.Alliance, out int count);
                    if (max > 0 && count >= max) {
                        continue;
                    }
                    taken[candidate.Alliance] = count + 1;
                    picks.Add(candidate.Id);
                    left--;
                }
            }
            return picks;
        }

        foreach (AllianceType alliance in maxCounts.Keys) {
            for (int rank = 0; rank < method.PickCountByRank.Length; rank++) {
                picks.AddRange(Shuffle(pool.Where(candidate => candidate.Alliance == alliance && candidate.Rank == rank).ToList(), random)
                    .Take(method.PickCountByRank[rank])
                    .Select(candidate => candidate.Id));
            }
        }
        return picks;
    }

    // The seed of a list: the character, the period's start day and the method's place in the table. Not
    // HashCode.Combine, which differs per process.
    public static int Seed(long characterId, DateTime periodStart, int methodIndex) {
        unchecked {
            long value = characterId * 1_000_003L + periodStart.Date.Ticks / TimeSpan.TicksPerDay * 7_919L + methodIndex;
            return (int) (value ^ (value >> 32));
        }
    }

    // The start of the period a list of this repeat holds now: the day for daily; for weekly the start of the last
    // reset weekday (1 sun .. 7 sat; 0 taken as sunday) at or before now, the same rule as QuestRules.CanStartAgain.
    public static DateTime PeriodStart(int repeat, int resetWeekday, DateTime now) {
        if (repeat != 3) {
            return now.Date;
        }
        return QuestRules.NextWeekdayStart(resetWeekday == 0 ? 1 : resetWeekday, now).AddDays(-7);
    }

    private static List<Candidate> Shuffle(List<Candidate> list, Random random) {
        for (int i = list.Count - 1; i > 0; i--) {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
