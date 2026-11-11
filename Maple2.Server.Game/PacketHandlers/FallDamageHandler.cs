using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class FallDamageHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.StateFallDamage;

    private const float BASE_FALL_DISTANCE = Constant.BlockSize * 5;

    public override void Handle(GameSession session, IByteReader packet) {
        // The client sends nothing in a field without fall damage (pcbase.cpp:4356-4358).
        float distance = FallDamageRules.Distance(packet.ReadFloat(), session.Field.Metadata.Limit.FallDamage);
        if (distance <= 0) {
            return;
        }

        session.Player.FallDamage(distance);
    }
}
