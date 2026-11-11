using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// InteractNpc 0x0032: i32 npc object id, sent when the player's interact cast at the npc completes (NpcInteractRules).
public class InteractNpcHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.InteractNpc;

    public override void Handle(GameSession session, IByteReader packet) {
        int objectId = packet.ReadInt();
        if (session.Field is null) {
            return;
        }
        if (!session.Field.Npcs.TryGetValue(objectId, out FieldNpc? npc) && !session.Field.Mobs.TryGetValue(objectId, out npc)) {
            return;
        }

        NpcInteractRules.Verdict verdict = NpcInteractRules.CanUse(npc.Value.Metadata.Interact, npc.IsDead, session.Player.Position, npc.Position,
            session.ServerTableMetadata.ConstantsTable.InteractionDistance, npc.InteractReadyTick, session.Field.FieldTick);
        if (verdict != NpcInteractRules.Verdict.Ok || npc.Value.Metadata.Interact is not { } interact) {
            Logger.Debug("InteractNpc {NpcId} ({ObjectId}) refused: {Verdict}", npc.Value.Id, objectId, verdict);
            return;
        }

        npc.Interact(interact);
    }
}
