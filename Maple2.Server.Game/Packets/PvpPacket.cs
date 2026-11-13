using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Model.Game.Pvp;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// Pvp 0xB6, read by CSceneStateGame::OnPvp (@0x141732350) and CPvpRankDuelComponent::OnMessage
// (pvprankduelcomponent.cpp, @0x140dbde70). The arena only gets the answer that takes the client out of its
// queue; the rank duel has its queue, match and result.
public static class PvpPacket {
    private enum Command : byte {
        ChampionGuild = 4,
        ArenaRegistered = 9,
        GameRuleValues = 12,
        ArenaUnregistered = 10,
        ArenaMatchFound = 11,
        RankDuelRegistered = 14,
        RankDuelUnregistered = 15,
        RankDuelMatch = 16,
        RankDuelInArenaField = 17,
        RankDuelResult = 18,
        RankDuelObserving = 19,
        RankDuelOwnRecord = 22,
        RankDuelRecords = 23,
        RankDuelObserveOffer = 25,
        RankDuelFight = 26,
        RankDuelRound = 27,
        RankDuelWin = 28,
        RankDuelLose = 29,
        RankDuelDraw = 30,
        Point = 100,
        WinLose = 101,
        ClearRanking = 102,
        RankRow = 103,
        LastRank = 104,
        KillNotice = 105,
        Damage = 106,
        TeamReset = 107,
        TeamScore = 108,
        TeamRecord = 109,
        TeamResult = 110,
        TeamGauge = 111,
        TeamFlag = 112,
        TeamPlunder = 113,
        GuildTeams = 0x72,
        GuildResult = 0x75,
    }

    // One guild of the guild championship result (Pvp 'u'): the name, the guild, the rounds won, the new score and its
    // change, the emblem.
    public readonly record struct GuildSide(string Name, long GuildId, int Rounds, int Score, int ScoreChange, string Emblem);

    // One side of the rank duel result (UIPvPRankDuelResultDialog::FUN_140f90b10): the name, the character,
    // the rounds won, the rating and its change (the dialog shows the change only when it is not 0) and the
    // valor tokens (90000006) won, shown when not 0.
    public readonly record struct RankDuelSide(string Name, long CharacterId, int Score, int Rating, int RatingChange, int Reward);

    // The arena state goes back to 0; the client shows its "unregistered" notice only when it was queued.
    public static ByteWriter ArenaUnregistered() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.ArenaUnregistered);
        return pWriter;
    }

    // The rank duel state goes back to 0 and the register button comes back.
    public static ByteWriter RankDuelUnregistered() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelUnregistered);
        return pWriter;
    }

    // False: the state goes back to 0 with the client's s_rank_duel_matching_fail box.
    public static ByteWriter RankDuelMatchFailed() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelMatch);
        pWriter.WriteBool(false);
        return pWriter;
    }

    // The state goes to 1 (queued): the register button turns into the unregister one, with the
    // guildBattle_Apply sound.
    public static ByteWriter RankDuelRegistered() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelRegistered);
        return pWriter;
    }

    // True: the state goes to 2 with the s_rank_duel_matching_complete box, whose button sends the join
    // (Pvp 16); it counts down 15 s.
    public static ByteWriter RankDuelMatchFound() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelMatch);
        pWriter.WriteBool(true);
        return pWriter;
    }

    // CSceneStateGame::OnPvp 0x11: with 0 the client shows s_rank_duel_ui_error_in_arena_field.
    public static ByteWriter RankDuelInArenaField() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelInArenaField);
        pWriter.WriteInt(0);
        return pWriter;
    }

    // Opens the result dialog (0x4f05). The dialog fills its sides only with a winner that is not 0.
    public static ByteWriter RankDuelResult(long winnerId, RankDuelSide left, RankDuelSide right) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelResult);
        pWriter.WriteLong(winnerId);
        WriteSide(pWriter, left);
        WriteSide(pWriter, right);
        return pWriter;
    }

    private static void WriteSide(ByteWriter pWriter, in RankDuelSide side) {
        pWriter.WriteUnicodeString(side.Name);
        pWriter.WriteLong(side.CharacterId);
        pWriter.WriteInt(side.Score);
        pWriter.WriteInt(side.Rating);
        pWriter.WriteInt(side.RatingChange);
        pWriter.WriteInt(side.Reward);
    }

    // The intros of UIEventIntroduce (0x4f06, uieventintroduce.gfx). Each movie stays on screen for the int it
    // is given in milliseconds: the movie's addMovie(movie, ms) arms setTimeout(onTimeOut, ms), which plays the
    // movie's "out" frames, so 0 takes it away at once. The round intro: the client reads two ints and passes
    // them to onRound in the other order (pvprankduelcomponent.cpp 0x1b); onRound draws its first argument as the
    // round number (digit1/digit10) and gives the second to addMovie. So the time goes first, the round second.
    public const int RoundIntroMs = 2_500;
    public const int FightIntroMs = 1_500;
    public const int ResultIntroMs = 3_000;

    public static ByteWriter RankDuelRound(int round, int durationMs = RoundIntroMs) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelRound);
        pWriter.WriteInt(durationMs);
        pWriter.WriteInt(round);
        return pWriter;
    }

    public static ByteWriter RankDuelFight(int durationMs = FightIntroMs) => Intro(Command.RankDuelFight, durationMs);
    public static ByteWriter RankDuelWin(int durationMs = ResultIntroMs) => Intro(Command.RankDuelWin, durationMs);
    public static ByteWriter RankDuelLose(int durationMs = ResultIntroMs) => Intro(Command.RankDuelLose, durationMs);
    public static ByteWriter RankDuelDraw(int durationMs = ResultIntroMs) => Intro(Command.RankDuelDraw, durationMs);

    private static ByteWriter Intro(Command command, int durationMs) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) command);
        pWriter.WriteInt(durationMs);
        return pWriter;
    }

    // 0x16: the season (league), the rating, the season's best rating and the valor tokens won this week. The
    // client starts the rating over at 1000 when the season is not its own current one (RefreshLeague).
    public static ByteWriter RankDuelOwnRecord(RankDuelRecord record) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelOwnRecord);
        pWriter.WriteInt(record.Season);
        pWriter.WriteInt(record.Rating);
        pWriter.WriteInt(record.BestRating);
        pWriter.WriteInt(record.WeeklyReward);
        return pWriter;
    }

    // 0x17: the season's rows by the opponent's job code, each as CIngredientInfo::Read reads it (the job code, the
    // wins, the losses); the dialog's statistics page sums them.
    public static ByteWriter RankDuelRecords(RankDuelRecord record) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelRecords);
        pWriter.WriteInt(record.Jobs.Count);
        foreach ((int jobCode, RankDuelJobRecord job) in record.Jobs.OrderBy(entry => entry.Key)) {
            pWriter.WriteInt(jobCode);
            pWriter.WriteInt(job.Wins);
            pWriter.WriteInt(job.Losses);
        }
        return pWriter;
    }

    // CSceneStateGame::OnPvp 9: state 1 (queued) for the arena, with the client's queued notice.
    public static ByteWriter ArenaRegistered(int arena) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.ArenaRegistered);
        pWriter.WriteInt(arena);
        return pWriter;
    }

    // CSceneStateGame::OnPvp 11: state 2 for the arena and the individualArenaJoin box (yes/no, 25 s), whose yes
    // sends the join (Pvp 11, CPVPManager::SendCancelRequest).
    public static ByteWriter ArenaMatchFound(int arena) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.ArenaMatchFound);
        pWriter.WriteInt(arena);
        return pWriter;
    }

    // 0x64: a player's PvP point, team and mode (CPvPInfo::ReadPointTeamMode on the player of this object id). Two
    // players with points above 0 that differ are enemies (PC::vfunc_122, vfunc_116); point 0 ends it.
    public static ByteWriter Point(int objectId, int point, byte team, byte mode) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.Point);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(point);
        pWriter.WriteByte(team);
        pWriter.WriteByte(mode);
        return pWriter;
    }

    // 0x65: the local player's win (true) or loss pose (MyPC state 0x28 with 0x73 or 0x74).
    public static ByteWriter WinLose(bool win) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.WinLose);
        pWriter.WriteBool(win);
        return pWriter;
    }

    // 0x66: CPVPManager::Clear, the ranking rows and team tables start over.
    public static ByteWriter ClearRanking() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.ClearRanking);
        return pWriter;
    }

    // 0x67: one ranking row (Unk_PvpTimer::OnMessage): character, score, kills, deaths and an int the free-for-all
    // order does not read (sent as 0). Opens the ranking dialog (0x4e9a).
    public static ByteWriter RankRow(long characterId, int score, int kill, int death) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankRow);
        pWriter.WriteLong(characterId);
        pWriter.WriteInt(score);
        pWriter.WriteInt(kill);
        pWriter.WriteInt(death);
        pWriter.WriteInt(0);
        return pWriter;
    }

    // 0x68: the player's last rank; opens the result dialog (0x4e9b) with the rows and the rank's reward.
    public static ByteWriter LastRank(int rank) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.LastRank);
        pWriter.WriteInt(rank);
        return pWriter;
    }

    // 0x6a: a row's damage (the client reads an int and keeps it unsigned).
    public static ByteWriter Damage(long characterId, long damage) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.Damage);
        pWriter.WriteLong(characterId);
        pWriter.WriteInt(unchecked((int) (uint) Math.Clamp(damage, 0, uint.MaxValue)));
        return pWriter;
    }

    // Pvp 12: the game rule values (CGameRule::ReadValues: a count byte, then key and value ints). The arena window
    // reads them as arenaType and the wait in milliseconds (UIIndividualPvPDialog::UpdateWaitTimeLabels; 0 shows the
    // unknown wait, more than 10 minutes its own text; the client skips arena 2).
    public static ByteWriter GameRuleValues(IReadOnlyCollection<(int Key, int Value)> values) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.GameRuleValues);
        pWriter.WriteByte((byte) Math.Min(values.Count, byte.MaxValue));
        foreach ((int key, int value) in values.Take(byte.MaxValue)) {
            pWriter.WriteInt(key);
            pWriter.WriteInt(value);
        }
        return pWriter;
    }

    // 0x69: a kill notice by its string id (PvpKillNotice) with the player's name, as a system notice.
    public static ByteWriter KillNotice(int stringId, string name) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.KillNotice);
        pWriter.WriteInt(stringId);
        pWriter.WriteUnicodeString(name);
        return pWriter;
    }

    // 0x6b: the team result dialog starts over and takes the player's team from its point (Unk_PvpEntryBlock).
    public static ByteWriter TeamReset() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.TeamReset);
        return pWriter;
    }

    // 0x6c: the red and the blue team's scores.
    public static ByteWriter TeamScore(int red, int blue) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.TeamScore);
        pWriter.WriteInt(red);
        pWriter.WriteInt(blue);
        return pWriter;
    }

    // 0x6d: a player's row in its team's table (the team is its point): CPvPInfo::ReadRecordFromPacket's three ints,
    // the first two shown as kills and deaths by the team result dialog.
    public static ByteWriter TeamRecord(long characterId, int kill, int death) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.TeamRecord);
        pWriter.WriteLong(characterId);
        pWriter.WriteInt(kill);
        pWriter.WriteInt(death);
        pWriter.WriteInt(0);
        return pWriter;
    }

    // 0x6e: the winning team (201 red, 202 blue, other a draw) and the reward count the dialog shows the player (of
    // the client constant's reward item, s_team_pvp_winner_reward_one_type or the loser's).
    public static ByteWriter TeamResult(int winnerTeam, int rewardCount) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.TeamResult);
        pWriter.WriteInt(winnerTeam);
        pWriter.WriteInt(rewardCount);
        return pWriter;
    }

    // 0x6f: an int the client does not use, then the red team's gauge in tenths of a percent.
    public static ByteWriter TeamGauge(int redPerMille) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.TeamGauge);
        pWriter.WriteInt(0);
        pWriter.WriteInt(redPerMille);
        return pWriter;
    }

    // 0x70: "the <team> is capturing the <flag> flag": team 1 red or 2 blue, flag 0 north, 1 center, 2 south.
    public static ByteWriter TeamFlag(int team, int flag) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.TeamFlag);
        pWriter.WriteInt(team);
        pWriter.WriteInt(flag);
        return pWriter;
    }

    // 0x71: "<name> plundered the <team>'s treasure": team 1 red or 2 blue.
    public static ByteWriter TeamPlunder(string name, byte team) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.TeamPlunder);
        pWriter.WriteUnicodeString(name);
        pWriter.WriteByte(team);
        return pWriter;
    }

    // 4: the championship guild the player cheers for (SetTargetGuildAndNotify, message 0x3c8): the guild and its name.
    public static ByteWriter ChampionGuild(long guildId, string name) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.ChampionGuild);
        pWriter.WriteLong(guildId);
        pWriter.WriteUnicodeString(name);
        return pWriter;
    }

    // 'r': the guild championship's two guilds (each the guild, its name and emblem); the client opens the guild team
    // dialog (UIPvPGuildTeamDialog) with them.
    public static ByteWriter GuildTeams(long guildA, string nameA, string emblemA, long guildB, string nameB, string emblemB) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.GuildTeams);
        pWriter.WriteLong(guildA);
        pWriter.WriteUnicodeString(nameA);
        pWriter.WriteUnicodeString(emblemA);
        pWriter.WriteLong(guildB);
        pWriter.WriteUnicodeString(nameB);
        pWriter.WriteUnicodeString(emblemB);
        return pWriter;
    }

    // 'u': the guild championship result, the winner first, then the valor tokens (90000006) the player won.
    public static ByteWriter GuildResult(GuildSide winner, GuildSide loser, int rewardCount) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.GuildResult);
        foreach (GuildSide side in new[] { winner, loser }) {
            pWriter.WriteUnicodeString(side.Name);
            pWriter.WriteLong(side.GuildId);
            pWriter.WriteInt(side.Rounds);
            pWriter.WriteInt(side.Score);
            pWriter.WriteInt(side.ScoreChange);
            pWriter.WriteUnicodeString(side.Emblem);
        }
        pWriter.WriteInt(rewardCount);
        return pWriter;
    }

    // 0x19: a rank duel can be watched. The client shows its s_rank_duel_matching_complete_to_observe box only in state
    // 0 with its observe option on; the box's yes sends the join (Pvp 16, CPvpRankDuelComponent::RequestCancelMatch).
    public static ByteWriter RankDuelObserveOffer() {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelObserveOffer);
        return pWriter;
    }

    // 0x13: false makes the local player an onlooker (CPvpRankDuelComponent::OnMessage: it goes to the origin with
    // state flag 1), true takes the flag off.
    public static ByteWriter RankDuelObserving(bool done) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelObserving);
        pWriter.WriteBool(done);
        return pWriter;
    }
}
