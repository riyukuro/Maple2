using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Validators;

// RequestSetCraftMode 0x0036: u8 mode, u8 has cube, and only when that is set the cube (i32 item id,
// i64 uid, i64, u8 has look, look). The client starts the mode with the first cube of its palette and
// writes 0 for the second byte when the palette has none; it stops with 0, 0.
public static class CraftModeRules {
    // The cube after the mode byte, or null when the client sent none. Nothing more is read then.
    public static PlotCube? ReadCube(IByteReader packet) {
        return packet.ReadBool() ? packet.ReadClass<PlotCube>() : null;
    }
}
