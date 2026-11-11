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
        HashSet<int> eventFields;
        if (session.Field == null) {
            return;
        }

        // Only checked and asked for a request the client could have sent.
        if (EventFieldRules.Decide(true, session.Player.IsDead, session.Field.MapId, fieldId, [fieldId]) != EventFieldRules.Result.Move) {
            return;
        }

        // The client refuses a field outside the continent group itself; a changed client gets its notice.
        // A field id the map data does not know is no event field.
        if (!session.MapMetadata.TryGet(fieldId, out MapMetadata? target)) {
            session.Send(MoveEventFieldPacket.Refuse(MoveEventFieldPacket.NO_EVENT));
            return;
        }

        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        if (!EventFieldRules.SameGroup((int) session.Field.Metadata.Property.Continent, (int) target.Property.Continent, constants.OpenContinents())) {
            session.Send(MoveEventFieldPacket.Refuse(MoveEventFieldPacket.OTHER_CONTINENT));
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (EventFieldRules.IsFresh(session.EventFieldsAskedAt, now)) {
            eventFields = session.EventFields;
        } else {
            eventFields = [];
            bool answered = true;
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
                answered = false;
            }
            // Only an answer is kept: after a failed call the next request asks again.
            if (answered) {
                session.EventFields = eventFields;
                session.EventFieldsAskedAt = now;
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
