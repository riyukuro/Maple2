using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class InsigniaHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Insignia;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Field == null) {
            return;
        }

        // An id the table does not hold changes nothing: the worn insignia keeps its buff.
        short insigniaId = InsigniaRules.ReadId(packet, TableMetadata.InsigniaTable.Entries, out InsigniaTable.Entry? newInsigniaMetadata);
        if (newInsigniaMetadata == null) {
            return;
        }

        // Check and remove any existing insignia buffs
        if (TableMetadata.InsigniaTable.Entries.TryGetValue(session.Player.Value.Character.Insignia, out InsigniaTable.Entry? oldInsigniaMetadata) && oldInsigniaMetadata.BuffId > 0) {
            session.Player.Buffs.Remove(oldInsigniaMetadata.BuffId, session.Player.ObjectId);
        }

        session.Player.Value.Character.Insignia = insigniaId;

        if (!InsigniaRules.Handled(newInsigniaMetadata.Type)) {
            Logger.Information("Unhandled insignia condition type: {type}", newInsigniaMetadata.Type);
        }
        bool display = InsigniaRules.Shown(session, newInsigniaMetadata);

        if (display && newInsigniaMetadata.BuffId > 0) {
            session.Player.Buffs.AddBuff(session.Player, session.Player, newInsigniaMetadata.BuffId, newInsigniaMetadata.BuffLevel, session.Field.FieldTick);
        }

        session.Field.Broadcast(InsigniaPacket.Update(session.Player.ObjectId, insigniaId, display));
    }
}
