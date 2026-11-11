using Maple2.Model.Enum;

namespace Maple2.Database.Model.Ranking;

// A party member of a boss clear row, as the board shows it (the job code picks the job icon).
public record BossClearMemberInfo(long CharacterId, string Name, string Profile, short Level, JobCode JobCode);

// A row of the boss boards 0x51 and 0x5b (and of the world boss boards 0x79 and 0x84): one clear of the board's
// rooms (clear time epoch seconds, the party's damage to the bosses).
public record BossClearRankInfo(int Rank, long ClearId, int RankingId, long LeaderId, int Seconds, long ClearTime, IList<BossClearMemberInfo> Members, long Damage = 0);

// A row of the boss board 0x48: a character and the clears it has on the board.
public record BossClearCountRankInfo(int Rank, long CharacterId, string Name, string Profile, int Clears);
