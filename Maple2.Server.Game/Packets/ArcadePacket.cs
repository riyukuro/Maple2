using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The arcade games (client CSceneStateGame::OnPlayArcade, 0xD8). A byte mode, then the mode's body; every mode is
// also passed to the arcade lobby, the life bar, the couple dance and the text effect dialogs.
// 0: an int type; opens the score dialog (the Dark Stream one) at 0 points and hides the hud (type 3 puts the
//    dialog at the top). 1: an int score. 2: closes the score dialog and opens the result dialog in arcade mode,
//    which reads five ints (the game's score, the best score before it, the exp, the meso, the habi) and a byte
//    count of ItemEntity items, as the Dark Stream result. 5: the life bar's init, life and max life (ints).
//    6: life and max life. 9: the lobby's season (an int, kept when not 0) and a count of (arcade id, score) ints,
//    the player's own scores (ids above 6 are skipped). 13: closes the score dialog.
public static class ArcadePacket {
    private enum Command : byte {
        Open = 0,
        Score = 1,
        Result = 2,
        LifeInit = 5,
        Life = 6,
        Scores = 9,
        CloseScore = 13,
    }

    public static ByteWriter Open(int type) {
        var pWriter = Packet.Of(SendOp.PlayArcade);
        pWriter.WriteByte((byte) Command.Open);
        pWriter.WriteInt(type);
        return pWriter;
    }

    public static ByteWriter Score(int score) {
        var pWriter = Packet.Of(SendOp.PlayArcade);
        pWriter.WriteByte((byte) Command.Score);
        pWriter.WriteInt(score);
        return pWriter;
    }

    public static ByteWriter Result(int score, int bestScore, int exp, int meso, int habi, IReadOnlyList<RewardItem> items) {
        var pWriter = Packet.Of(SendOp.PlayArcade);
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

    // init above 0 makes the life bar's swf start over.
    public static ByteWriter LifeInit(int init, int life, int maxLife) {
        var pWriter = Packet.Of(SendOp.PlayArcade);
        pWriter.WriteByte((byte) Command.LifeInit);
        pWriter.WriteInt(init);
        pWriter.WriteInt(life);
        pWriter.WriteInt(maxLife);
        return pWriter;
    }

    public static ByteWriter Life(int life, int maxLife) {
        var pWriter = Packet.Of(SendOp.PlayArcade);
        pWriter.WriteByte((byte) Command.Life);
        pWriter.WriteInt(life);
        pWriter.WriteInt(maxLife);
        return pWriter;
    }

    public static ByteWriter Scores(int seasonId, IList<ArcadeBest> bests) {
        var pWriter = Packet.Of(SendOp.PlayArcade);
        pWriter.WriteByte((byte) Command.Scores);
        pWriter.WriteInt(seasonId);
        pWriter.WriteInt(bests.Count);
        foreach (ArcadeBest best in bests) {
            pWriter.WriteInt(best.ArcadeId);
            pWriter.WriteInt(best.Score);
        }
        return pWriter;
    }

    public static ByteWriter CloseScore() {
        var pWriter = Packet.Of(SendOp.PlayArcade);
        pWriter.WriteByte((byte) Command.CloseScore);
        return pWriter;
    }
}
