using Grpc.Core;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;

namespace Maple2.Server.Game.PacketHandlers;

// Client: UIWorldMapDialog::MoveByPortal (uiworldmapdialog.cpp:1510) sends the field id of a time event portal
// picked on the world map, after its own checks and a confirm box.
public class MoveEventFieldHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestMoveEventField;

    public override void Handle(GameSession session, IByteReader packet) {
        int fieldId = packet.ReadInt();
        if (session.Field == null) {
            return;
        }

        var eventFields = new HashSet<int>();
        // Only asked for a request the client could have sent.
        if (EventFieldRules.Decide(true, session.Player.IsDead, session.Field.MapId, fieldId, [fieldId]) == EventFieldRules.Result.Move) {
            try {
                TimeEventResponse response = session.World.TimeEvent(new TimeEventRequest {
                    GetActiveWorldBosses = new TimeEventRequest.Types.GetActiveWorldBosses(),
                });
                foreach (TimeEventResponse.Types.ActiveWorldBoss active in response.ActiveWorldBosses) {
                    if (active.AliveChannels.Count == 0) {
                        continue;
                    }
                    if (session.ServerTableMetadata.TimeEventTable.WorldBoss.TryGetValue(active.MetadataId, out WorldBossMetadata? metadata)) {
                        eventFields.UnionWith(metadata.TargetMapIds);
                    }
                }
            } catch (RpcException ex) {
                Logger.Warning(ex, "Event field move: the World server did not answer");
            }
        }

        switch (EventFieldRules.Decide(true, session.Player.IsDead, session.Field.MapId, fieldId, eventFields)) {
            case EventFieldRules.Result.NoEvent:
                session.Send(MoveEventFieldPacket.Refuse(MoveEventFieldPacket.NO_EVENT));
                return;
            case EventFieldRules.Result.Move:
                session.Send(session.PrepareField(fieldId)
                    ? FieldEnterPacket.Request(session.Player)
                    : FieldEnterPacket.Error(MigrationError.s_move_err_default));
                return;
        }
    }
}
