using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The quests of a require groupID the client accepts in the period: QuestDataTable::FUN_14074e6c0
// (commonfiles/questdata.cpp:2238). CQuestManagerCommon::vfunc_o10_21 (questdata.cpp:333) accepts a grouped quest only
// when it is one of the group's picks, and CanAcceptQuest's unrequireGroup (questdata.cpp:520) reads the same picks.
// The pick is the client's own random draw, so it is copied exactly: the server and the client then agree on the
// day's (or week's) quest of every group without any packet.
public static class QuestGroupPick {
    // questgrouptable.xml gives a pickCount only to groups 5022-5024, with 1 (GetGroupPickCount, questdata.cpp:2212,
    // gives 1 for any other group). Their dependency "character" adds a per-character seed (FUN_140747000,
    // questdata.cpp:702), but no quest the server has is in them, so the seed's addend is always 0 here.
    public const int PickCount = 1;

    // The picked quest ids. ids: every quest of the group the client has, in id order (the client builds its list
    // from its quest map, questdata.cpp:2421, locked quests too); repeat: the first one's repeat. now: the local time,
    // the client's CMsTime date (the TimeSync zone is the server's local zone).
    public static List<int> Pick(IReadOnlyList<int> ids, int repeat, int groupId, DateTime now, int pickCount = PickCount) {
        var random = new ClientRandom(Seed(repeat, groupId, now));
        var candidates = new List<int>(ids);
        var picked = new List<int>();
        for (int i = 0; i < pickCount && candidates.Count > 0; i++) {
            int index = (int) (random.Next() % (uint) candidates.Count);
            picked.Add(candidates[index]);
            candidates.RemoveAt(index);
        }
        return picked;
    }

    // The seed with no selector (m_selector is set only by a debug user command): daily (repeat 2) the date as
    // yyyymmdd times the group; weekly (3) the year * 10000 plus the week of year of the next Friday (NextWeekdayStart(6)),
    // times the group; any other repeat 0. The int arithmetic wraps as the client's.
    public static int Seed(int repeat, int groupId, DateTime now) {
        unchecked {
            switch (repeat) {
                case 2:
                    return (now.Year * 10000 + now.Month * 100 + now.Day) * groupId;
                case 3:
                    DateTime weekStart = QuestRules.NextWeekdayStart(6, now);
                    return (weekStart.Year * 10000 + WeekOfYear(weekStart.Year, weekStart.Month, weekStart.Day)) * groupId;
                default:
                    return 0;
            }
        }
    }

    // GetWeekOfYear (commonfiles/timeutil.cpp:463): the whole days since Jan 1, plus 1, plus an offset by Jan 1's weekday
    // (1 sun: 5, else weekday - 3), over 7, plus 1.
    public static int WeekOfYear(int year, int month, int day) {
        var first = new DateTime(year, 1, 1);
        int days = (int) (new DateTime(year, month, day) - first).TotalDays;
        int dayOfWeek = (int) first.DayOfWeek + 1;
        int offset = dayOfWeek == 1 ? 5 : dayOfWeek - 3;
        return (days + 1 + offset) / 7 + 1;
    }

    // Unk_Random (commonfiles/questmasktable.cpp:373-461): three registers seeded by an LCG (each step returns the
    // value before it moves), then a three-part Tausworthe step per draw.
    public struct ClientRandom {
        private uint s1, s2, s3;

        public ClientRandom(int seed) {
            unchecked {
                uint a = (uint) seed;
                seed = seed * 0x343fd + 0x269ec3;
                uint b = (uint) seed;
                seed = seed * 0x343fd + 0x269ec3;
                uint c = (uint) seed;
                s1 = a | 0x100000;
                s2 = b | 0x1000;
                s3 = c | 0x10;
            }
        }

        public uint Next() {
            s1 = (s1 & 0xfffffffe) << 12 ^ (s1 << 13 ^ s1) >> 19;
            s2 = (s2 & 0xfffffff8) << 4 ^ (s2 << 2 ^ s2) >> 25;
            s3 = (s3 & 0xfffffff0) << 17 ^ (s3 << 3 ^ s3) >> 11;
            return s1 ^ s2 ^ s3;
        }
    }
}
