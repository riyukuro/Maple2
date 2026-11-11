using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class TombstoneHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Tombstone;

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        int objectId = packet.ReadInt();
        int hits = packet.ReadInt();

        if (!session.Field.TryGetPlayer(objectId, out FieldPlayer? player) || player.Tombstone == null) {
            return;
        }

        // The count used to be cut to a byte and subtracted in a byte: a count above what was left wrapped around
        // and filled the tombstone again, and 255 broke it in one packet.
        if (!TombstoneRules.IsHit(hits, objectId, session.Player.ObjectId)) {
            return;
        }
        if (!TombstoneRules.CanHit(player.Tombstone.HitsRemaining)) {
            return;
        }

        player.Tombstone.HitsRemaining = TombstoneRules.AfterHit(player.Tombstone.HitsRemaining);
        session.ConditionUpdate(ConditionType.hit_tombstone);
    }
}
