namespace Maple2.Database.Model.Ranking;

// A row of the house commendation board: the home owner's character (its latest played one), the commendations of
// the board's span and the home's name.
public record HomeCommendRankInfo(int Rank, long CharacterId, string Name, string Profile, int Commends, long AccountId, string HomeName);
