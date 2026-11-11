namespace Maple2.Database.Model.Ranking;

// A row of the Colosseum board: the character, its best run of the season (rounds, seconds) and when it was set
// (epoch seconds).
public record ColosseumRankInfo(int Rank, long CharacterId, string Name, string Profile, int Rounds, int Seconds, long SetTime);
