using System.Numerics;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// MinimapPing: i64 character id, u8 kind, three floats (uiminimapentity.cpp:2557-2560).
public static class MinimapPingRules {
    // The client sends kind 0 or 1 only (uiminimapentity.cpp:2476, 2486).
    public const byte MaxKind = 1;
    // No field reaches this far from its origin; a point outside it did not come from the minimap.
    public const float MaxCoordinate = 1_000_000f;
    // The client has no wait of its own between two pings; this is the server's.
    public const long MinIntervalMs = 500;

    public static (long CharacterId, byte Kind, Vector3 Position) Read(IByteReader packet) {
        long characterId = packet.ReadLong();
        byte kind = packet.ReadByte();
        var position = new Vector3(packet.ReadFloat(), packet.ReadFloat(), packet.ReadFloat());
        return (characterId, kind, position);
    }

    public static bool KindAllowed(byte kind) {
        return kind <= MaxKind;
    }

    public static bool PositionSane(Vector3 position) {
        return InRange(position.X) && InRange(position.Y) && InRange(position.Z);
    }

    private static bool InRange(float value) {
        return float.IsFinite(value) && System.Math.Abs(value) <= MaxCoordinate;
    }

    public static bool RateAllowed(long lastTickMs, long nowTickMs) {
        return lastTickMs == 0 || nowTickMs - lastTickMs >= MinIntervalMs;
    }
}
