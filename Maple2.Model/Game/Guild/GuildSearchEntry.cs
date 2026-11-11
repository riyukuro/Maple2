namespace Maple2.Model.Game;

// One row of the guild search result (client CSearchGuildJoin).
public record GuildSearchEntry(long Id, string Name, string Emblem, int MemberCount, int Experience,
    long LeaderAccountId, long LeaderCharacterId, string LeaderName);
