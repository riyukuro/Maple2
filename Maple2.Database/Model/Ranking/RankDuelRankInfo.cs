namespace Maple2.Database.Model.Ranking;

// A row of a PvP season's rank duel board: the character and the rating it holds (the current season) or ended with
// (a season before), with the season's best.
public record RankDuelRankInfo(int Rank, long CharacterId, string Name, string Profile, int Rating, int BestRating);
