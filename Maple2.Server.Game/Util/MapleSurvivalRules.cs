using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The Maple Survival match of the restored client.
public static class MapleSurvivalRules {
    // The match's id in the World's arena queue (the individual arenas are 1 to 3).
    public const int Arena = 100;

    // The survival map a match is played on. Chosen here: 82000000, the first of the survival maps (its script runs the
    // full round: boats, field game, storm).
    public const int MapId = 82000000;

    // The players a match takes: the survival script's CheckTheNumberOfPlayers needs 20 players in box 9000
    // (01_survival.xml), else the match fails. A server operator can set MS2_SURVIVAL_PLAYERS lower to try a match with
    // fewer clients; the script's 20 is then read as that count in a survival match's field.
    public const int ScriptPlayers = 20;

    public static int PlayerCount {
        get {
            string? value = Environment.GetEnvironmentVariable("MS2_SURVIVAL_PLAYERS");
            return int.TryParse(value, out int count) && count is >= 1 and <= ScriptPlayers ? count : ScriptPlayers;
        }
    }

    // The server serves the restored client (MS2_SURVIVAL_CLIENT=1): its lobby, season and queue state go out after each
    // game server entry. Off by default: the KR client has no reader for them, and it never sends op 0xC2, so nothing
    // else of the survival match reaches it.
    public static bool RestoredClient => Environment.GetEnvironmentVariable("MS2_SURVIVAL_CLIENT") == "1";

    public static IndividualArenaTable.Entry Entry => new(Arena, MapId, PlayerCount, 0, 0, 0, 0, 0, 0);

    // The script's user count in a survival match's field: its 20 is the match's own count.
    public static int ScriptCount(int scriptCount, bool survivalMatch) {
        return survivalMatch && scriptCount == ScriptPlayers ? Math.Min(scriptCount, PlayerCount) : scriptCount;
    }
}
