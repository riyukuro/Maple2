namespace Maple2.Database.Model.Ranking;

// A row of the Dark Stream board: the character, its best score and the time it was set (epoch seconds).
public record DarkStreamRankInfo(int Rank, long CharacterId, string Name, string Profile, int Score, long SetTime);
