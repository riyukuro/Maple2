using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.PacketHandlers;

public class FallDamageHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.StateFallDamage;

    private const float BASE_FALL_DISTANCE = Constant.BlockSize * 5;

    public override void Handle(GameSession session, IByteReader packet) {
        float height = packet.ReadFloat();
        long now = Environment.TickCount64;
        if (session.Config.FallWatch.Fell(height, session.Player.Position, now) is { } excess) {
            LogExcess(Logger, session, excess, now);
        }
        // The client sends nothing in a field without fall damage (pcbase.cpp:4356-4358).
        float distance = FallDamageRules.Distance(height, session.Field.Metadata.Limit.FallDamage);
        if (distance <= 0) {
            return;
        }

        session.Player.FallDamage(distance);
    }

    // A fall height the records did not allow (FallHeightWatch). Logged only until honest play shows none; the damage is
    // taken as before.
    public static void LogExcess(ILogger logger, GameSession session, FallHeightWatch.Excess excess, long now) {
        if (session.Config.FallLog.Count(now) is > 0 and int unlogged) {
            logger.Information("StateFallDamage above the records (logged only): a height of {Height}, the records allow {Allowed}, {Unlogged} since the last line, {Player} ({CharacterId})",
                excess.Height, excess.Allowed, unlogged, session.PlayerName, session.CharacterId);
        }
    }
}
