using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Util;

// The 0xE1 board kind of a score_board_create type. The client shows kind 1 as the boss gauge (cur*100/max %, so
// it needs most points above 0) and kinds 2/3 as the score dialog at the top center / top right (its own trigger
// timer maps "TR" to the same top-right align and anything else to the top-center one). Other types name no kind.
public static class ScoreBoard {
    public static byte? Kind(string? type, int maxScore) {
        return type switch {
            "ShadowGauge" when maxScore > 0 => ShadowExpeditionPacket.BossGauge,
            "ScoreBoardTopCenter" => ShadowExpeditionPacket.TopCenterScore,
            _ => null,
        };
    }
}
