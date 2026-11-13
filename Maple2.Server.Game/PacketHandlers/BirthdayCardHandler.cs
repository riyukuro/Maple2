using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// BirthdayCard 0xA6: the birthday card (npc kind 88, UIBirthdayCardDialog::SendBirthdayCard) sends mode 0 when it
// loads. The reply is the player's birthday (0x111 mode 1); the card gives no reward.
public class BirthdayCardHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.BirthdayCard;

    private enum Command : byte {
        Load = 0,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = (Command) packet.ReadByte();
        if (command != Command.Load) {
            return;
        }

        session.Send(BirthdayCardPacket.Set(session.Player.ObjectId, session.Player.Value.Account.Birthday));
    }
}
