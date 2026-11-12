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
        ArenaUnregistered = 10,
        RankDuelRegistered = 14,
        RankDuelUnregistered = 15,
        RankDuelMatch = 16,
        RankDuelInArenaField = 17,
        RankDuelResult = 18,
        RankDuelOwnRecord = 22,
        RankDuelRecords = 23,
        RankDuelFight = 26,
        RankDuelRound = 27,
        RankDuelWin = 28,
        RankDuelLose = 29,
        RankDuelDraw = 30,
    }

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

    // The round intro of UIEventIntroduce (0x4f06): the client reads two ints and passes them to onRound in
    // the other order, as the massive event's round (round, goal) is passed (uimassiveevent.cpp
    // FUN_1419f60a0). So the goal (rounds in the match) goes first, the round second.
    public static ByteWriter RankDuelRound(int round, int rounds) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) Command.RankDuelRound);
        pWriter.WriteInt(rounds);
        pWriter.WriteInt(round);
        return pWriter;
    }

    // The fight, win, lose and draw intros: each int goes to the intro's Flash call (onFight, onWin, onLose,
    // onDraw) unread by the client code; 0 is sent.
    public static ByteWriter RankDuelFight() => Intro(Command.RankDuelFight);
    public static ByteWriter RankDuelWin() => Intro(Command.RankDuelWin);
    public static ByteWriter RankDuelLose() => Intro(Command.RankDuelLose);
    public static ByteWriter RankDuelDraw() => Intro(Command.RankDuelDraw);

    private static ByteWriter Intro(Command command) {
        var pWriter = Packet.Of(SendOp.Pvp);
        pWriter.WriteByte((byte) command);
        pWriter.WriteInt(0);
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
}
