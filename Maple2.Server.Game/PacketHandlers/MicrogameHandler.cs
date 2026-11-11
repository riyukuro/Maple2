using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Database.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

// Microgame 0x00A8: the rock-paper-scissors game of the RPS event, one round per game. See RpsRules for the modes;
// the pending invites and games live in the field (FieldManager.Rps).
public class MicrogameHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Microgame;

    public override void Handle(GameSession session, IByteReader packet) {
        short mode = packet.ReadShort();
        switch (mode) {
            case RpsRules.OpenBanner:
                HandleOpenBanner(session);
                return;
            case RpsRules.Invite:
                HandleInvite(session, packet.ReadLong());
                return;
            case RpsRules.InviteAck:
                HandleInviteAck(session, packet.ReadLong());
                return;
            case RpsRules.AutoReject:
                HandleAutoReject(session, packet.ReadLong());
                packet.ReadByte(); // always 0
                return;
            case RpsRules.Accept:
                HandleAccept(session, packet.ReadLong());
                return;
            case RpsRules.Decline:
                HandleDecline(session, packet.ReadLong());
                return;
            case RpsRules.Cancel:
                HandleCancel(session, packet.ReadLong());
                return;
            case RpsRules.PlayCard:
                HandlePlayCard(session, packet.ReadInt());
                return;
            case RpsRules.ClaimReward:
                HandleClaimReward(session, packet.ReadInt());
                return;
        }
    }

    // The client keeps one RPS event (CGameRule SetRps); the server uses the first active one.
    private static (GameEvent, Rps)? FindRps(GameSession session) {
        foreach (GameEvent gameEvent in session.FindEvent(GameEventType.RPS)) {
            if (gameEvent.IsActive() && gameEvent.Metadata.Data is Rps rps) {
                return (gameEvent, rps);
            }
        }
        return null;
    }

    private static bool HasTicket(GameSession session, Rps rps) {
        return session.Item.Inventory.Find(rps.GameTicketId).Sum(item => item.Amount) > 0;
    }

    private static long Expiration() => DateTime.Now.AddDays(1).Date.ToEpochSeconds();

    private static void HandleOpenBanner(GameSession session) {
        if (session.Field == null || FindRps(session) is not (_, Rps rps)) {
            return;
        }
        if (!HasTicket(session, rps) || session.Field.Rps.IsBusy(session.CharacterId)) {
            return;
        }
        session.Send(MicrogamePacket.Banner());
    }

    // Both players can start a game: the event runs, both hold a ticket, both are in this field within
    // MicroGameMaxDistance (the client checks the same before it sends) and neither has an invite or a game.
    private static bool CanPlay(GameSession session, long otherId, out FieldPlayer? other) {
        other = null;
        if (session.Field == null || otherId == session.CharacterId || FindRps(session) is not (_, Rps rps)) {
            return false;
        }
        if (!session.Field.TryGetPlayerById(otherId, out other)) {
            return false;
        }
        if (!HasTicket(session, rps) || !HasTicket(other.Session, rps)) {
            return false;
        }
        return Vector3.Distance(session.Player.Position, other.Position) <= RpsRules.MaxDistance;
    }

    private static void HandleInvite(GameSession session, long inviteeId) {
        if (session.Field == null) {
            return;
        }
        if (!CanPlay(session, inviteeId, out FieldPlayer? invitee) || !session.Field.Rps.Invite(session.CharacterId, inviteeId)) {
            session.Send(MicrogamePacket.Failed(inviteeId, RpsRules.ReasonPeerState));
            return;
        }
        invitee!.Session.Send(MicrogamePacket.Invite(session.CharacterId));
    }

    // The invitee shows the request box: the inviter waits.
    private static void HandleInviteAck(GameSession session, long inviterId) {
        if (session.Field == null || !session.Field.Rps.IsPending(inviterId, session.CharacterId)) {
            return;
        }
        if (session.Field.TryGetPlayerById(inviterId, out FieldPlayer? inviter)) {
            inviter.Session.Send(MicrogamePacket.InviteSent(session.CharacterId));
        }
    }

    // The invitee cannot show the request box.
    private static void HandleAutoReject(GameSession session, long inviterId) {
        if (session.Field == null || !session.Field.Rps.RemovePending(inviterId, session.CharacterId)) {
            return;
        }
        if (session.Field.TryGetPlayerById(inviterId, out FieldPlayer? inviter)) {
            inviter.Session.Send(MicrogamePacket.Failed(session.CharacterId, RpsRules.ReasonPeerState));
        }
    }

    private static void HandleAccept(GameSession session, long inviterId) {
        if (session.Field == null || !session.Field.Rps.IsPending(inviterId, session.CharacterId)) {
            return;
        }
        if (!CanPlay(session, inviterId, out FieldPlayer? inviter) || !session.Field.Rps.Start(inviterId, session.CharacterId)) {
            session.Field.Rps.RemovePending(inviterId, session.CharacterId);
            session.Send(MicrogamePacket.Failed(inviterId, RpsRules.ReasonAcceptPeerState));
            if (session.Field.TryGetPlayerById(inviterId, out FieldPlayer? waiting)) {
                waiting.Session.Send(MicrogamePacket.Failed(session.CharacterId, RpsRules.ReasonPeerState));
            }
            return;
        }
        inviter!.Session.Send(MicrogamePacket.Accepted(session.CharacterId));
        inviter.Session.Send(MicrogamePacket.Round());
        session.Send(MicrogamePacket.Round());
    }

    private static void HandleDecline(GameSession session, long inviterId) {
        if (session.Field == null || !session.Field.Rps.RemovePending(inviterId, session.CharacterId)) {
            return;
        }
        if (session.Field.TryGetPlayerById(inviterId, out FieldPlayer? inviter)) {
            inviter.Session.Send(MicrogamePacket.Declined(session.CharacterId));
        }
    }

    // The inviter's waiting box closed (cancel or time out), or a player left a running game.
    private static void HandleCancel(GameSession session, long otherId) {
        if (session.Field == null) {
            return;
        }
        if (session.Field.Rps.RemovePending(session.CharacterId, otherId)) {
            if (session.Field.TryGetPlayerById(otherId, out FieldPlayer? invitee)) {
                invitee.Session.Send(MicrogamePacket.Cancelled(session.CharacterId));
            }
            return;
        }
        long partnerId = session.Field.Rps.GamePartner(session.CharacterId);
        if (partnerId == 0 || partnerId != otherId) {
            return;
        }
        session.Field.Rps.Remove(session.CharacterId);
        if (session.Field.TryGetPlayerById(partnerId, out FieldPlayer? partner)) {
            partner.Session.Send(MicrogamePacket.Failed(session.CharacterId, RpsRules.ReasonFailed));
        }
    }

    private static void HandlePlayCard(GameSession session, int card) {
        if (session.Field == null || !session.Field.Rps.Play(session.CharacterId, card, out RpsMatchTable.Played? played) || played is not { } game) {
            return;
        }
        session.Field.TryGetPlayerById(game.Inviter, out FieldPlayer? inviter);
        session.Field.TryGetPlayerById(game.Invitee, out FieldPlayer? invitee);
        if (inviter == null || invitee == null || FindRps(session) is not (GameEvent gameEvent, Rps rps)) {
            inviter?.Session.Send(MicrogamePacket.Failed(game.Invitee, RpsRules.ReasonFailed));
            invitee?.Session.Send(MicrogamePacket.Failed(game.Inviter, RpsRules.ReasonFailed));
            return;
        }
        // One ticket each, taken at the result; a player without one ends the game for both without a count.
        Item? inviterTicket = inviter.Session.Item.Inventory.Find(rps.GameTicketId).FirstOrDefault(item => item.Amount > 0);
        Item? inviteeTicket = invitee.Session.Item.Inventory.Find(rps.GameTicketId).FirstOrDefault(item => item.Amount > 0);
        if (inviterTicket == null || inviteeTicket == null) {
            inviter.Session.Send(MicrogamePacket.Failed(game.Invitee, RpsRules.ReasonFailed));
            invitee.Session.Send(MicrogamePacket.Failed(game.Inviter, RpsRules.ReasonFailed));
            return;
        }
        inviter.Session.Item.Inventory.Consume(inviterTicket.Uid, 1);
        invitee.Session.Item.Inventory.Consume(inviteeTicket.Uid, 1);

        Finish(inviter.Session, gameEvent, game.InviterCard, game.InviteeCard);
        Finish(invitee.Session, gameEvent, game.InviteeCard, game.InviterCard);
    }

    private static void Finish(GameSession player, GameEvent gameEvent, int mine, int theirs) {
        byte result = RpsRules.Result(mine, theirs);
        player.Send(MicrogamePacket.Result(result, mine, theirs));
        GameEventUserValue matches = player.GameEvent.Get(GameEventUserValueType.RPSDailyMatches, gameEvent.Id, Expiration());
        player.GameEvent.Set(gameEvent.Id, GameEventUserValueType.RPSDailyMatches, matches.Int() + 1);
        player.ConditionUpdate(ConditionType.play_rps);
        player.ConditionUpdate(RpsRules.ResultCondition(result));
    }

    private static void HandleClaimReward(GameSession session, int index) {
        if (session.Field == null || FindRps(session) is not (GameEvent gameEvent, Rps rps)) {
            return;
        }
        int playCount = session.GameEvent.Get(GameEventUserValueType.RPSDailyMatches, gameEvent.Id, Expiration()).Int();
        GameEventUserValue claimed = session.GameEvent.Get(GameEventUserValueType.RPSRewardsClaimed, gameEvent.Id, Expiration());
        int[] condPlayCounts = rps.Rewards.Select(reward => reward.PlayCount).ToArray();
        if (!RpsRules.CanClaim(index, condPlayCounts, playCount, claimed.String())) {
            return;
        }

        var items = new List<Item>();
        foreach (RewardItem reward in rps.Rewards[index].Rewards) {
            Item? item = session.Field.ItemDrop.CreateItem(reward.ItemId, reward.Rarity, reward.Amount);
            if (item != null) {
                items.Add(item);
            }
        }
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.RPSRewardsClaimed, RpsRules.AddClaimed(claimed.String(), index));
        var notified = new List<RewardItem>();
        foreach (Item item in items) {
            notified.Add(item);
            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }
        session.Send(MicrogamePacket.Rewards(notified));
    }
}
