namespace Maple2.Database.Model.Ranking;

// A row of an arcade game's board: the character, its best score of the game in the season and when it was set
// (epoch seconds).
public record ArcadeRankInfo(int Rank, long CharacterId, string Name, string Profile, int ArcadeId, int SeasonId, int JobCode, int Level, int Score, long SetTime);
