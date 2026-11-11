using System.Numerics;
using Maple2.Model.Game.Party;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class MinimapPingHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.MinimapPing;

    public override void Handle(GameSession session, IByteReader packet) {
        // UIMinimapDialog::SendMinimapPing: the sender's character id, the ping kind (0 or 1) and the
        // position. The client sends it only where the field data allows minimap pings.
        // The character id is not used; the session's own id is.
        (_, byte kind, Vector3 position) = MinimapPingRules.Read(packet);
        if (!MinimapPingRules.KindAllowed(kind) || !MinimapPingRules.PositionSane(position)) {
            return;
        }

        // The client's own check, repeated: pings only on a field whose data allows them.
        if (session.Field == null || !session.Field.Metadata.Property.MinimapPing) {
            return;
        }

        long now = Environment.TickCount64;
        if (!MinimapPingRules.RateAllowed(session.MinimapPingTick, now)) {
            return;
        }
        session.MinimapPingTick = now;

        ByteWriter ping = MinimapPingPacket.Ping(session.CharacterId, kind, position);
        Party? party = session.Party.Party;
        if (party == null) {
            session.Send(ping);
            return;
        }

        // The party members on the same field see the ping.
        foreach (long characterId in party.Members.Keys) {
            if (session.Field.TryGetPlayerById(characterId, out FieldPlayer? player)) {
                player.Session.Send(ping);
            }
        }
    }
}
