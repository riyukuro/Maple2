using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The Dark Stream score and result (client CSceneStateGame::OnDarkStream, 0xC5). A byte command:
// 0 opens the score dialog at 0 points, 1 sets the points (an int; the dialog shows a score below 0),
// 2 closes the score dialog and opens the result dialog. The result is five ints (the run's score, the best
// score before the run, the exp, the meso and the habi; the dialog shows a new record when the score is
// above the best one and no habi when it is 0 or less), then a byte count of reward items, each as the client's
// ItemEntity::Read reads it: an int item id, a short rank, an int count and four bools (sent as false). The
// dialog shows the items the item table knows, with their count.
public static class DarkStreamPacket {
    private enum Command : byte {
        Open = 0,
        Score = 1,
        Result = 2,
    }

    public static ByteWriter Open() {
        var pWriter = Packet.Of(SendOp.DarkStream);
        pWriter.WriteByte((byte) Command.Open);
        return pWriter;
    }

    public static ByteWriter Score(int score) {
        var pWriter = Packet.Of(SendOp.DarkStream);
        pWriter.WriteByte((byte) Command.Score);
        pWriter.WriteInt(score);
        return pWriter;
    }

    public static ByteWriter Result(int score, int bestScore, int exp, int meso, int habi, IReadOnlyList<RewardItem> items) {
        var pWriter = Packet.Of(SendOp.DarkStream);
        pWriter.WriteByte((byte) Command.Result);
        pWriter.WriteInt(score);
        pWriter.WriteInt(bestScore);
        pWriter.WriteInt(exp);
        pWriter.WriteInt(meso);
        pWriter.WriteInt(habi);
        pWriter.WriteByte((byte) items.Count);
        foreach (RewardItem item in items) {
            pWriter.WriteInt(item.ItemId);
            pWriter.WriteShort(item.Rarity);
            pWriter.WriteInt(item.Amount);
            pWriter.WriteBool(item.Unknown1);
            pWriter.WriteBool(item.Unknown2);
            pWriter.WriteBool(item.Unknown3);
            pWriter.WriteBool(item.Unknown4);
        }
        return pWriter;
    }
}
