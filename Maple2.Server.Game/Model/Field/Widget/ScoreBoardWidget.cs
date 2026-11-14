using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model.Widget;

/// <summary>
/// The ScoreBoard widget of the VIP dungeon (02000385) and the item dungeon (02000386): OpenBoard shows the score
/// dialog, SetScore and AddScore set and add to its score, CloseBoard closes it, and the Compare condition tests the
/// score ("&gt;=,1500": an operator, a comma, a value).
/// </summary>
/// <remarks>
/// The client's score dialog is the Dark Stream one (0x4ee4): 0xC5 command 0 opens it at 0 points, 0xD8 command 12
/// places it (a byte: 1 at the center, 2 at the top right, as the scripts' note on OpenBoard's argument says),
/// command 1 sets its points and 0xD8 command 13 closes it. While the board is open, the score of a dying
/// spawn_npc_range npc and of a picked-up score orb is added to it (FieldManager.AddScoreBoardScore); 02000385 gives
/// 100 a npc. The score stays after CloseBoard, so the Compare after it reads the run's score.
/// </remarks>
public class ScoreBoardWidget : Widget {
    public const string Type = "ScoreBoard";
    public const byte AlignCenter = 1;
    public const byte AlignTopRight = 2;

    private readonly object scoreLock = new();
    public int Score { get; private set; }
    // The align of the open board; null when none is open.
    public byte? Align { get; private set; }

    public ScoreBoardWidget(FieldManager field) : base(field) { }

    public override void Action(string function, int numericArg, string stringArg) {
        int value = int.TryParse(stringArg, out int parsed) ? parsed : numericArg;
        switch (function) {
            case "OpenBoard":
                lock (scoreLock) {
                    Align = value == AlignTopRight ? AlignTopRight : AlignCenter;
                    Score = 0;
                }
                foreach (ByteWriter packet in OpenPackets(Align.Value, 0)) {
                    Field.Broadcast(packet);
                }
                break;
            case "SetScore":
                SetScore(_ => value);
                break;
            case "AddScore":
                SetScore(score => (int) Math.Clamp((long) score + value, int.MinValue, int.MaxValue));
                break;
            case "CloseBoard":
                bool open;
                lock (scoreLock) {
                    open = Align != null;
                    Align = null;
                }
                if (open) {
                    Field.Broadcast(ArcadePacket.CloseScore());
                }
                break;
        }
    }

    public override bool Check(string name, string arg) {
        if (name != "Compare") {
            return false;
        }
        // "op,value": the client's own compare form; WidgetCondition reads "op value".
        return WidgetCondition.Matches(Score, arg.Replace(',', ' '));
    }

    /// <summary>Points for the open board (a kill or a score orb); false when no board is open.</summary>
    public bool AddPoints(int points) {
        int total;
        lock (scoreLock) {
            if (Align == null) {
                return false;
            }
            total = Score = (int) Math.Min((long) Score + points, int.MaxValue);
        }
        Field.Broadcast(DarkStreamPacket.Score(total));
        return true;
    }

    /// <summary>What a player who enters the field gets: the open board at its score, or nothing.</summary>
    public IReadOnlyList<ByteWriter> EnterPackets() {
        lock (scoreLock) {
            return Align == null ? [] : OpenPackets(Align.Value, Score);
        }
    }

    public static IReadOnlyList<ByteWriter> OpenPackets(byte align, int score) {
        var packets = new List<ByteWriter> { DarkStreamPacket.Open(), ArcadePacket.ScoreBoard(align) };
        if (score != 0) {
            packets.Add(DarkStreamPacket.Score(score));
        }
        return packets;
    }

    private void SetScore(Func<int, int> update) {
        int score;
        bool open;
        lock (scoreLock) {
            score = Score = update(Score);
            open = Align != null;
        }
        if (open) {
            Field.Broadcast(DarkStreamPacket.Score(score));
        }
    }
}
