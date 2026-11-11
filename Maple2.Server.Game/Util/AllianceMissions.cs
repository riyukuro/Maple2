using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The alliance missions: the alliance quests (type 9) of repeat 2 (daily) and of repeat 3 without a group (weekly) a
// player may take. The client offers and starts such a quest only when it is in the list Quest 0x1F (daily) or 0x20
// (weekly) sent (CQuestManager::vfunc_1, questmanager_2.cpp:555); the lists are the server's pick by the methods of
// table/famepickmethod.xml (FameTable). How the original server picked is not in the client: here each list is a
// shuffle of all the missions seeded by the character and the day (daily) or the start of the week (weekly). The
// missions held that were started in the period are picked first and use up the counts, so a list grows by no more
// than the counts within a period when the missions the player can start change (level, gear score, quests).
public static class AllianceMissions {
    // A mission of the list's repeat: Held, started in the list's period (any state); Eligible, not held and the
    // player can start it.
    public readonly record struct Candidate(int Id, AllianceType Alliance, int Rank, bool Held = false, bool Eligible = true);

    // The quests whose repeat a list holds: 2 daily, 3 weekly without a group.
    public static bool IsMission(QuestMetadata metadata) {
        QuestMetadataBasic basic = metadata.Basic;
        return basic.Type == QuestType.AllianceQuest
               && (basic.Repeat == 2 || basic.Repeat == 3 && metadata.Require.GroupId == 0);
    }

    // The missions the daily reset drops when they are not completed, the client's RemoveQuestsByTypeAndState(9, repeat,
    // 1) (questmanager_2.cpp:2294): alliance quests of repeat 2 or 3 without a group.
    public static bool DropsAtReset(QuestMetadata metadata) {
        QuestMetadataBasic basic = metadata.Basic;
        return basic.Type == QuestType.AllianceQuest && basic.Repeat is 2 or 3 && metadata.Require.GroupId == 0;
    }

    // A held quest the reset or the login drops: a mission it drops, not completed, started before the period.
    public static bool Dropped(QuestMetadata metadata, QuestState state, long startTime, DateTime periodStart) {
        return DropsAtReset(metadata) && state != QuestState.Completed && startTime < periodStart.ToEpochSeconds();
    }

    // Start of a mission without bypass: only one in the list of its repeat that was sent.
    public static bool Listed(QuestMetadata metadata, IReadOnlySet<int> daily, IReadOnlySet<int> weekly) {
        return !IsMission(metadata) || (metadata.Basic.Repeat == 2 ? daily : weekly).Contains(metadata.Id);
    }

    // Accept all (Quest 34): the listed missions of the alliance, by id.
    public static IEnumerable<int> AcceptAll(IEnumerable<QuestMetadata> listed, AllianceType alliance) {
        return listed.Where(metadata => metadata.Basic.Alliance == alliance).Select(metadata => metadata.Id).Order();
    }

    // Complete all (Quest 35): a held alliance quest of the alliance, of repeat 2 or 3, not completed.
    public static bool CompleteAll(QuestMetadataBasic basic, QuestState state, AllianceType alliance) {
        return basic.Type == QuestType.AllianceQuest && basic.Alliance == alliance && basic.Repeat is 2 or 3
               && state != QuestState.Completed;
    }

    // One method's pick from the missions of its repeat. All of them are shuffled, so the order does not depend on
    // which the player can start; the held ones are taken first, then the eligible ones in that order up to the
    // counts. Combination: each rank's count from the listed alliances together, an alliance at most its MaxCount (0
    // no limit); otherwise each listed alliance gets each rank's count. A held mission is kept even over a count.
    public static List<int> Pick(FameTable.PickMethod method, IEnumerable<Candidate> missions, int seed) {
        var random = new Random(seed);
        Dictionary<AllianceType, int> maxCounts = method.Alliances
            .GroupBy(alliance => alliance.Type)
            .ToDictionary(group => group.Key, group => group.First().MaxCount);
        List<Candidate> pool = missions.Where(candidate => maxCounts.ContainsKey(candidate.Alliance))
            .DistinctBy(candidate => candidate.Id)
            .OrderBy(candidate => candidate.Id)
            .ToList();

        var picks = new List<int>();
        if (method.Combination) {
            var taken = new Dictionary<AllianceType, int>();
            for (int rank = 0; rank < method.PickCountByRank.Length; rank++) {
                List<Candidate> order = Shuffle(pool.Where(candidate => candidate.Rank == rank).ToList(), random);
                int left = method.PickCountByRank[rank];
                foreach (Candidate candidate in order.Where(candidate => candidate.Held)) {
                    taken[candidate.Alliance] = taken.GetValueOrDefault(candidate.Alliance) + 1;
                    picks.Add(candidate.Id);
                    left--;
                }
                foreach (Candidate candidate in order.Where(candidate => !candidate.Held && candidate.Eligible)) {
                    if (left <= 0) {
                        break;
                    }
                    int max = maxCounts[candidate.Alliance];
                    int count = taken.GetValueOrDefault(candidate.Alliance);
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
                List<Candidate> order = Shuffle(pool.Where(candidate => candidate.Alliance == alliance && candidate.Rank == rank).ToList(), random);
                List<int> held = order.Where(candidate => candidate.Held).Select(candidate => candidate.Id).ToList();
                picks.AddRange(held);
                picks.AddRange(order.Where(candidate => !candidate.Held && candidate.Eligible)
                    .Take(Math.Max(0, method.PickCountByRank[rank] - held.Count))
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
    // reset weekday (1 sun .. 7 sat; 0, none, taken as sunday) at or before now. The login drop, the reset and the
    // seed all use it.
    public static DateTime PeriodStart(int repeat, int resetWeekday, DateTime now) {
        if (repeat != 3) {
            return now.Date;
        }
        return QuestRules.NextWeekdayStart(resetWeekday == 0 ? 1 : resetWeekday, now).AddDays(-7);
    }

    // The world runs the daily reset every 24 hours from the first midnight, so after a clock change, or when the
    // timer fires a moment early, it comes before midnight. The reset takes the period that starts within this
    // margin as the new one.
    public static readonly TimeSpan ResetMargin = TimeSpan.FromHours(2);

    // The period the daily reset starts: the one at now plus the margin.
    public static DateTime ResetPeriodStart(int repeat, int resetWeekday, DateTime now) {
        return PeriodStart(repeat, resetWeekday, now + ResetMargin);
    }

    private static List<Candidate> Shuffle(List<Candidate> list, Random random) {
        for (int i = list.Count - 1; i > 0; i--) {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
