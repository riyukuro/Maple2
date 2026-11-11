using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// The client's requests for the PlayerHost notices (s2c 0x0105).
public class ItemBillboardHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemBillboard;

    private enum Command : byte {
        ClaimHongBao = 1,
        // 8: the staff's "remove" button of the billboard window (i64, the last field of the window
        // packet, which is sent as 0). Not built.
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = (Command) packet.ReadByte();
        switch (command) {
            case Command.ClaimHongBao:
                HandleClaimHongBao(session, packet);
                return;
        }
    }

    // The chat link of a red envelope (uimessenger.cpp:4739-4749). The answer is mode 2 of PlayerHost:
    // a line with the reward, or "That can no longer be opened".
    private static void HandleClaimHongBao(GameSession session, IByteReader packet) {
        if (session.Field is null) {
            return;
        }

        int hongBaoId = HongBaoRules.ReadClaim(packet);
        bool found = session.Field.TryGetHongBao(hongBaoId, out HongBao? hongBao);
        bool expired = found && session.Field.FieldTick >= hongBao!.EndTick;
        // The taken and emptied tests are made again, with the entry, under the envelope's lock.
        if (HongBaoRules.Claim(found, expired, false, hongBao?.Shares.Count ?? 0, hongBao?.MaxUserCount ?? 0) == HongBaoRules.ClaimStep.Refuse) {
            session.Send(PlayerHostPacket.InactiveHongBao());
            return;
        }

        Item? item = hongBao!.Claim(session.Player);
        if (item == null) {
            session.Send(PlayerHostPacket.InactiveHongBao());
            return;
        }

        session.Send(PlayerHostPacket.GiftHongBao(session.Player, hongBao, item.Amount));
        if (!session.Item.Inventory.Add(item, true)) {
            session.Item.MailItem(item);
        }
    }
}
