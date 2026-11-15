namespace Maple2.Model.Metadata;

// table/mentoringconfig.xml as MentoringDataTable::Init reads it (commonfiles/mentoringdatatable.cpp): the MentoringConfig
// element's maxMentorDailyRewardCoin, maxMenteeDailyRewardCoin (uint) and isShowIcon, its AttendReward's RewardCoin v
// values in order and its MentorReward coin. KMS2: 400, 1500, 1, 100/200/300, 1000.
public record MentoringConfigTable(int MaxMentorDailyRewardCoin, int MaxMenteeDailyRewardCoin, bool ShowIcon,
        IReadOnlyList<int> AttendRewardCoins, int MentorRewardCoin) : Table {
    // The client's values without the file (MentoringDataTable's constructor): no cap, no coin, the icon shown.
    public static readonly MentoringConfigTable Empty = new(0, 0, true, Array.Empty<int>(), 0);

    // The KMS2 file's values, for a metadata db ingested before this table was.
    public static readonly MentoringConfigTable Kms2 = new(400, 1500, true, [100, 200, 300], 1000);

    // MentoringDataTable::GetAttendRewardCoin: the coin of the streak day, the last one past the end; 0 without coins.
    public int AttendRewardCoin(int day) => AttendRewardCoins.Count == 0 ? 0 : AttendRewardCoins[Math.Clamp(day, 0, AttendRewardCoins.Count - 1)];

    // MentoringDataTable::GetRewardableCoin: the part of coin still given today when today coins were given already,
    // under the mentee's or the mentor's daily cap.
    public long RewardableCoin(long coin, long today, bool mentee) {
        long max = mentee ? MaxMenteeDailyRewardCoin : MaxMentorDailyRewardCoin;
        return today >= max ? 0 : Math.Min(Math.Max(coin, 0), max - today);
    }
}
