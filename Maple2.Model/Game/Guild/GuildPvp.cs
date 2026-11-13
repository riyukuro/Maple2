using System.Globalization;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Game;

// The guild championship (guild PvP) record of a guild: the score, wins and losses of the season it last fought in
// (CGuildInfo after the create time), its last matches (Guild 0x4a) and the seasons before (Guild 0x4b). Written by
// the World under the guild's lock; the channels get a copy with the guild.
public class GuildPvp {
    // CGuildInfo's score default: a guild without a match in the season.
    public const int DefaultScore = 1000;
    // The client keeps the newest 10 matches (CGuildManager::OnPacketPvpHistory).
    public const int HistoryCapacity = 10;

    public int SeasonId { get; set; }
    public int Score { get; set; } = DefaultScore;
    public int Wins { get; set; }
    public int Losses { get; set; }
    public List<GuildPvpMatch> History { get; set; } = [];
    public List<GuildPvpSeason> Seasons { get; set; } = [];
    // The matches whose reward the guild's fighters got on RewardDay (yyyymmdd, local), at most DailyRewardCap.
    public int RewardDay { get; set; }
    public int RewardCount { get; set; }

    // Chosen here (the client has no limit): a guild's matches pay the reward at most this many times a day, and a
    // character gets it at most this many times a day too (whatever its guild, so a guild hop or a new guild gives
    // no more).
    public const int DailyRewardCap = 10;

    // The grade of a score: the first index whose threshold is above it, else the count (CGuildInfo::GetGrade).
    public static int Grade(int score, IReadOnlyList<int> grades) {
        for (int i = 0; i < grades.Count; i++) {
            if (grades[i] > score) {
                return i;
            }
        }
        return grades.Count;
    }

    // The season a time falls in (CSeasonDataTable::GetCurrent), null outside all of them.
    public static SeasonDataTable.Entry? CurrentSeason(IReadOnlyDictionary<int, SeasonDataTable.Entry> seasons, DateTime now) {
        return seasons.Values.Where(season => season.StartTime <= now && now < season.EndTime)
            .OrderBy(season => season.Id).FirstOrDefault();
    }

    // The season's name: its start month, the form the season table's seasonName attribute uses ("March 2026").
    public static string SeasonName(SeasonDataTable.Entry season) {
        return season.StartTime.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    }

    // Moves the record into the season: a record of an earlier season is closed into a season row and starts again.
    // The rank is the guild's place among the guilds of the closed season. True when the record changed.
    public bool Roll(int seasonId, Func<int, (SeasonDataTable.Entry? Season, int Rank)> closed) {
        if (SeasonId == seasonId) {
            return false;
        }
        if (SeasonId != 0 && (Wins > 0 || Losses > 0)) {
            (SeasonDataTable.Entry? season, int rank) = closed(SeasonId);
            Seasons.Add(new GuildPvpSeason {
                SeasonId = SeasonId,
                StartTime = season == null ? 0 : new DateTimeOffset(season.StartTime).ToUnixTimeSeconds(),
                EndTime = season == null ? 0 : new DateTimeOffset(season.EndTime).ToUnixTimeSeconds(),
                Name = season == null ? string.Empty : SeasonName(season),
                Score = Score,
                Rank = rank,
                Grade = season == null ? 0 : Grade(Score, season.Grades),
                Wins = Wins,
                Losses = Losses,
            });
        }
        SeasonId = seasonId;
        Score = DefaultScore;
        Wins = 0;
        Losses = 0;
        return true;
    }

    // Takes one of the day's rewards; false when the cap is reached.
    public bool TakeReward(DateTime now) {
        int day = RewardDay;
        int count = RewardCount;
        bool taken = TakeDaily(ref day, ref count, now);
        RewardDay = day;
        RewardCount = count;
        return taken;
    }

    // A day's count (day yyyymmdd, local): a new day starts at 0; false at DailyRewardCap, else one more.
    public static bool TakeDaily(ref int day, ref int count, DateTime now) {
        int today = now.Year * 10000 + now.Month * 100 + now.Day;
        if (day != today) {
            day = today;
            count = 0;
        }
        if (count >= DailyRewardCap) {
            return false;
        }
        count++;
        return true;
    }

    // The record's side of a played match: only a fought match (a fought round won) of a guild with a fighter in it
    // that is under the guild's daily cap pays; only such a match moves the score, by point. Returns the change and
    // whether the guild's fighters get the reward.
    public (int Delta, bool Reward) Settle(bool fought, bool hasFighter, int point, DateTime now) {
        if (!fought || !hasFighter || !TakeReward(now)) {
            return (0, false);
        }
        Score += point;
        return (point, true);
    }

    // Whether the character has one of the day's DailyRewardCap left (nothing taken): the field sends the World
    // only such fighters as payable, so a guild whose fighters are all at their cap takes no guild slot or score.
    public static bool HasFighterReward(Character character, DateTime now) {
        int day = character.GuildPvpRewardDay;
        int count = character.GuildPvpRewardCount;
        return TakeDaily(ref day, ref count, now);
    }

    // A character's reward of a match: a fighter of a guild the World paid gets it while the character has one of
    // the day's DailyRewardCap left (taken here). False: no reward and no championship count.
    public static bool TakeFighterReward(Character character, bool fighter, bool guildPaid, DateTime now) {
        if (!fighter || !guildPaid) {
            return false;
        }
        int day = character.GuildPvpRewardDay;
        int count = character.GuildPvpRewardCount;
        if (!TakeDaily(ref day, ref count, now)) {
            return false;
        }
        character.GuildPvpRewardDay = day;
        character.GuildPvpRewardCount = count;
        return true;
    }

    // A match's member counts on a channel's guild copy, before the copy takes the new record: a record of another
    // season (the World rolled it, GuildManager.RollPvp) starts every member's season count again, then the counts
    // the World sent are set. Returns the members whose counts changed, in member order (Guild 0x26).
    public static List<GuildMember> ApplyMemberCounts(Guild guild, int seasonId, IEnumerable<(long CharacterId, int Season, int Total)> counts) {
        var changed = new List<GuildMember>();
        if (guild.Pvp.SeasonId != seasonId) {
            foreach (GuildMember member in guild.Members.Values) {
                if (member.PvpSeasonCount != 0) {
                    member.PvpSeasonCount = 0;
                    changed.Add(member);
                }
            }
        }
        foreach ((long characterId, int season, int total) in counts) {
            if (!guild.Members.TryGetValue(characterId, out GuildMember? member)) {
                continue;
            }
            if (member.PvpSeasonCount == season && member.PvpTotalCount == total) {
                continue;
            }
            member.PvpSeasonCount = season;
            member.PvpTotalCount = total;
            if (!changed.Contains(member)) {
                changed.Add(member);
            }
        }
        return changed;
    }

    public void AddMatch(GuildPvpMatch match) {
        History.Add(match);
        while (History.Count > HistoryCapacity) {
            History.RemoveAt(0);
        }
    }

    public GuildPvp Clone() {
        return new GuildPvp {
            SeasonId = SeasonId,
            Score = Score,
            Wins = Wins,
            Losses = Losses,
            History = History.ToList(),
            Seasons = Seasons.ToList(),
            RewardDay = RewardDay,
            RewardCount = RewardCount,
        };
    }
}

// One match (CGuildPVPHistoryData, guilddef_1.cpp ReadStream), as the guild saw it: Guild A is the guild itself.
public record GuildPvpMatch {
    public long GuildA { get; init; }
    public long GuildB { get; init; }
    public string NameA { get; init; } = string.Empty;
    public string NameB { get; init; } = string.Empty;
    // f20: 1 for the loser of a match without a fought round (CGuildDialog's history row: no round results is a
    // no-fight match, 0x3ea a win, with f20 1 0x3eb a loss).
    public bool NoFight { get; init; }
    // f58/f80: each round's fighters, "id,name:id,name" (the page shows the names).
    public string[] PlayersA { get; init; } = new string[5];
    public string[] PlayersB { get; init; } = new string[5];
    // fa8: each round's result for Guild A, 'W', 'L' or 0 (not fought).
    public byte[] Results { get; init; } = new byte[5];
    // fb8: the end time; the page shows its age, the client asks again from the last one it has.
    public long Time { get; init; }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt();
        writer.WriteInt();
        writer.WriteBool(NoFight);
        writer.WriteLong(GuildA);
        writer.WriteLong(GuildB);
        writer.WriteUnicodeString(NameA);
        writer.WriteUnicodeString(NameB);
        writer.WriteInt();
        writer.WriteInt();
        writer.WriteInt();
        writer.WriteInt();
        for (int i = 0; i < 5; i++) {
            writer.WriteUnicodeString(i < PlayersA.Length ? PlayersA[i] ?? string.Empty : string.Empty);
            writer.WriteUnicodeString(i < PlayersB.Length ? PlayersB[i] ?? string.Empty : string.Empty);
            writer.WriteByte(i < Results.Length ? Results[i] : (byte) 0);
        }
        writer.WriteLong(Time);
        writer.WriteLong(Time);
    }
}

// One closed season (CGuildPVPSeasonHistoryData, guilddef_2.cpp): the page shows the name, the wins and losses,
// the rank (0x3ef) and the grade.
public record GuildPvpSeason {
    public int SeasonId { get; init; }
    public long StartTime { get; init; }
    public long EndTime { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Score { get; init; }
    public int Rank { get; init; }
    public int Grade { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(SeasonId);
        writer.WriteLong(StartTime);
        writer.WriteLong(EndTime);
        writer.WriteUnicodeString(Name);
        writer.WriteInt(Score);
        writer.WriteInt(Rank);
        writer.WriteInt(Grade);
        writer.WriteString();
        writer.WriteInt();
        writer.WriteInt(Wins);
        writer.WriteInt(Losses);
        writer.WriteInt();
        writer.WriteInt();
        writer.WriteInt();
        writer.WriteInt();
    }
}

// A guild's place on a season's championship board: what the board row shows, and the losses that break a tie.
public record GuildPvpStanding(long GuildId, string Name, string Emblem, string LeaderName, int Score, int Losses);
