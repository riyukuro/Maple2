using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class BuddyBadgeHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.BuddyBadge;

    private enum Command : byte {
        Start = 0,
        Stop = 1,
        ShowToNewcomer = 2,
        PartnerName = 3,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Start:
                HandleStart(session, packet);
                return;
            case Command.Stop:
                HandleStop(session, packet);
                return;
            case Command.ShowToNewcomer:
                HandleShowToNewcomer(session, packet);
                return;
            case Command.PartnerName:
                HandlePartnerName(session, packet);
                return;
        }
    }

    // Sent when another player appears on the field while the sender's couple effect is on:
    // own character id, then the newcomer's. Only the newcomer is told.
    private static void HandleShowToNewcomer(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        long newcomerId = packet.ReadLong();
        if (characterId != session.CharacterId || newcomerId == session.CharacterId || session.Field == null) {
            return;
        }

        if (!session.Item.Equips.Badge.TryGetValue(BadgeType.Buddy, out Item? buddyBadge) || buddyBadge.CoupleInfo == null) {
            return;
        }
        if (!session.Field.TryGetPlayerById(buddyBadge.CoupleInfo.CharacterId, out FieldPlayer? partner) ||
            !HasBuddyBadgeEquipped(session, partner.Session)) {
            return;
        }
        if (!session.Field.TryGetPlayerById(newcomerId, out FieldPlayer? newcomer)) {
            return;
        }

        newcomer.Session.Send(BuddyBadgePacket.ShowToNewcomer(characterId));
    }

    // Sent when the partner name on the sender's badge differs from the name of the partner standing on
    // the field (the partner was renamed). The badge takes the current name and the field is told.
    private static void HandlePartnerName(GameSession session, IByteReader packet) {
        long partnerId = packet.ReadLong();
        if (session.Field == null) {
            return;
        }

        if (!session.Item.Equips.Badge.TryGetValue(BadgeType.Buddy, out Item? buddyBadge) || buddyBadge.CoupleInfo == null) {
            return;
        }
        if (!session.Field.TryGetPlayerById(partnerId, out FieldPlayer? partner)) {
            return;
        }

        ItemCoupleInfo? renamed = BuddyBadgeRule.Renamed(buddyBadge.CoupleInfo, partnerId, partner.Value.Character.Name);
        if (renamed == null) {
            return;
        }

        buddyBadge.CoupleInfo = renamed;
        session.Field.Broadcast(BuddyBadgePacket.PartnerName(session.CharacterId, buddyBadge.Uid, renamed.Name));
    }

    private static void HandleStart(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        if (characterId != session.CharacterId) {
            return;
        }

        if (session.Field == null) {
            return;
        }

        if (!session.Item.Equips.Badge.TryGetValue(BadgeType.Buddy, out Item? buddyBadge) || buddyBadge.CoupleInfo == null) {
            return;
        }

        if (!session.Field.TryGetPlayerById(buddyBadge.CoupleInfo.CharacterId, out FieldPlayer? partner)) {
            return;
        }

        if (HasBuddyBadgeEquipped(session, partner.Session)) {
            session.Field?.Broadcast(BuddyBadgePacket.Start(characterId), session);
        }
    }

    private static void HandleStop(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();

        session.Field?.Broadcast(BuddyBadgePacket.Stop(characterId), session);
    }

    private static bool HasBuddyBadgeEquipped(GameSession sender, GameSession receiver) {
        if (!sender.Item.Equips.Badge.TryGetValue(BadgeType.Buddy, out Item? senderBadge)) {
            return false;
        }
        if (!receiver.Item.Equips.Badge.TryGetValue(BadgeType.Buddy, out Item? receiverBadge)) {
            return false;
        }

        return receiverBadge.Id == senderBadge.Id &&
               receiverBadge.CoupleInfo?.CharacterId == sender.CharacterId &&
               senderBadge.CoupleInfo?.CharacterId == receiver.CharacterId;
    }
}
