namespace Maple2.Model.Metadata;

// The client's own default guild rank names, master first (CGuildInfo::ResetGrades, guilddef_1.cpp:2269): the values of
// s_guild_grade_default_master and _group1 to _group5 in the stringcommon.xml of the region's client (string/kr on KR,
// string/en on NA; the played client's first, a key it lacks from string/en; TableMapper.ParseGuildRankNames). The
// client prints a rank name as the server sends it (CGuildGrade::ReadPacket; the player info window's
// s_char_info_guild_name), so a guild made on another region's data showed that region's default names, e.g. Korean on
// KR data with an English client. An empty name: the strings lack that key.
public record GuildRankNameTable(IReadOnlyList<string> Names) : Table {
    public static readonly GuildRankNameTable Empty = new(Array.Empty<string>());

    public static readonly string[] Keys = [
        "s_guild_grade_default_master", "s_guild_grade_default_group1", "s_guild_grade_default_group2",
        "s_guild_grade_default_group3", "s_guild_grade_default_group4", "s_guild_grade_default_group5",
    ];

    // The name shown for rank |rank|'s stored name: a rank that still has the data's default name for its place
    // (|dataDefaults|, the names a new guild is made with) shows the client's own default; a name a player typed is
    // kept as typed.
    public string Shown(int rank, string stored, IReadOnlyList<string> dataDefaults) {
        if (rank < 0 || rank >= Names.Count || rank >= dataDefaults.Count || stored != dataDefaults[rank]) {
            return stored;
        }
        return Names[rank].Length > 0 ? Names[rank] : stored;
    }

    // The name to store for rank |rank|'s |sent| name (the reverse of Shown): the client's default for that place is
    // stored as the data's default, so a rank the master only changed the rights of keeps its default name.
    public string Stored(int rank, string sent, IReadOnlyList<string> dataDefaults) {
        if (rank < 0 || rank >= Names.Count || rank >= dataDefaults.Count || Names[rank].Length == 0 || sent != Names[rank]) {
            return sent;
        }
        return dataDefaults[rank];
    }
}
