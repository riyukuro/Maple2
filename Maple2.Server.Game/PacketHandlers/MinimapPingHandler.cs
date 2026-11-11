using System.Numerics;
using Maple2.Model.Game.Party;
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
        packet.ReadLong(); // character id; the session's own id is used
        byte kind = packet.ReadByte();
        var position = new Vector3(packet.ReadFloat(), packet.ReadFloat(), packet.ReadFloat());

        if (session.Field == null) {
            return;
        }

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
