namespace Maple2.Database.Model.Ranking;

// A row of a Maple Survival season board: the character, its season rating, games, wins and kills, and the time of
// its last game (Unix seconds).
public record SurvivalRankInfo(int Rank, long CharacterId, string Name, string Profile, int Rating, int Games, int Wins, int Kills, long LastGame);
