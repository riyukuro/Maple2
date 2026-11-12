namespace Maple2.Model.Metadata;

// table/pvprankingduelmode.xml: the rank duel grades 1 to 7 (CRankingArenaDataTable::Load, rankugcmarketbuy.cpp).
public record RankDuelGradeTable(IReadOnlyDictionary<int, RankDuelGradeTable.Grade> Grades) : Table {
    public record Grade(
        int Id,
        int GradeRating,
        int WinnerReward,
        int LoserReward,
        int WeeklyRewardMax,
        int WinnerRating,
        int LoserRating);
}
