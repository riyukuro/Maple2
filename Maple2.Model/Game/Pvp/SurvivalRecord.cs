using Maple2.Model.Metadata;

namespace Maple2.Model.Game.Pvp;

// A character's Maple Survival record of one season (the survival-record table).
public sealed class SurvivalSeasonRecord {
    public int SeasonId { get; init; }
    public int Rating { get; set; }
    public int Games { get; set; }
    public int Wins { get; set; }
    public int Kills { get; set; }
}

// What a game gives and what the pass gives, against the NA tables (MapleSurvivalTable). The account keeps the
// survival level, its exp, the paid pass and the pass levels claimed (Account.Survival*, as the restored client's 0x17
// reads them: exp is the exp inside the level). Chosen here (no table or client settles them): the season rating
// starts at StartRating and moves by RatingChange; a game gives GameExp.
public static class SurvivalRules {
    public const int StartRating = 1000;
    // A squad game's record is kept apart from the solo one: under the squad season (SeasonData_MapleSurvivalSquad)
    // plus this offset in the same table (chosen; the restored windows show only the solo season).
    public const int SquadSeasonOffset = 1_000_000;

    // The season whose span holds the time (end inclusive), 0 when none does.
    public static int SeasonAt(IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, DateTime now) {
        return seasons.Values.Where(season => season.StartTime <= now && now <= season.EndTime)
            .Select(season => season.Id).DefaultIfEmpty(0).Max();
    }

    // The open period (season) of the time: the one whose span holds it, 0 when none does.
    public static MapleSurvivalTable.Period? CurrentPeriod(MapleSurvivalTable table, DateTime now) {
        return table.Periods.Values.Where(period => period.StartTime <= now && now <= period.EndTime)
            .OrderBy(period => period.Id).FirstOrDefault();
    }

    // Chosen: the winner +30, the top half +10, the others -10; not below 0.
    public static int RatingChange(int rank, int participants) {
        if (rank == 1) return 30;
        return rank <= (participants + 1) / 2 ? 10 : -10;
    }

    // Chosen: 1000 a game, 300 a kill and 200 for each participant ranked below.
    public static long GameExp(int rank, int participants, int kills) {
        return 1000 + 300L * Math.Max(0, kills) + 200L * Math.Max(0, participants - rank);
    }

    // Adds exp at a level: level L goes up to L + 1 once its exp reaches survivallevel's reqExp of L + 1; at the
    // last level the exp stays 0. The levels reached, in order.
    public static List<int> AddExp(MapleSurvivalTable table, ref int level, ref long exp, long gain) {
        var reached = new List<int>();
        level = Math.Max(1, level);
        exp += Math.Max(0, gain);
        while (table.LevelExp.TryGetValue(level + 1, out long required) && exp >= required) {
            exp -= required;
            level++;
            reached.Add(level);
        }
        if (!table.LevelExp.ContainsKey(level + 1)) {
            exp = 0;
        }
        return reached;
    }

    // The pass levels a claim takes (the client sends how many free and paid levels, UIMapleSurvivalPassDialog
    // OnReceive): the free ones after those claimed up to the level, the paid ones only with the paid pass.
    public static (int FreeTo, int PaidTo) Claim(int level, int freeClaimed, int paidClaimed, bool paidPass, int free, int paid) {
        int freeTo = Math.Clamp(freeClaimed + Math.Max(0, free), freeClaimed, Math.Max(freeClaimed, level));
        int paidTo = paidPass ? Math.Clamp(paidClaimed + Math.Max(0, paid), paidClaimed, Math.Max(paidClaimed, level)) : paidClaimed;
        return (freeTo, paidTo);
    }

    // The 16 ints of Survival 0x11 as the restored result window passes them to the movie (survivalgameresultdialog,
    // GMS 0x89a4d0; the movie's own code says what each shows):
    // - setGameRecord(r4, r5, time of r14, r15): the ranking text "r5 / r4" (rank / participants), the kill text r15;
    // - setResult(r2, r3, delta, r11, r0): the rating r3 with the delta from r2; r0 0 shows "not season rating"
    //   instead (r0: the game counted for a season); r11 is not read by the movie, sent as 0;
    // - setSurvivalLevelResult(r6..r10, ...): chosen order level before, exp before, exp gained, exp after, level after
    //   (r10 is compared with the last level; the next level's exp comes from the client's own table);
    // - setCharLevelResult(r12), setHeroLevelResult(r13): the character's level and its hero (prestige) level.
    public static int[] ResultValues(int rank, int participants, int kills, bool inSeason, int ratingBefore, int ratingAfter, int levelBefore,
                                     long expBefore, long gained, int levelAfter, long expAfter, int seconds, int charLevel, int heroLevel) {
        int[] values = new int[16];
        values[0] = inSeason ? 1 : 0;
        values[2] = ratingBefore;
        values[3] = ratingAfter;
        values[4] = participants;
        values[5] = rank;
        values[6] = levelBefore;
        values[7] = (int) Math.Min(expBefore, int.MaxValue);
        values[8] = (int) Math.Min(gained, int.MaxValue);
        values[9] = (int) Math.Min(expAfter, int.MaxValue);
        values[10] = levelAfter;
        values[12] = charLevel;
        values[13] = heroLevel;
        values[14] = seconds;
        values[15] = kills;
        return values;
    }
}
