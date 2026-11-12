using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Database.Extensions;

namespace Maple2.Server.Game.PacketHandlers;

// Microgame 0x00A8: the rock-paper-scissors game of the RPS event, one round per game (modes 0 to 0xC, RpsRules), and
// the couple dance of the CoupleDance event (modes 0xD to 0x19, CoupleDanceRules). The pending invites and games live
// in the field (FieldManager.Rps and FieldManager.CoupleDance); a player is in at most one of them.
public class MicrogameHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Microgame;

    // The handlers below are static; the base class Logger is per instance.
    private static readonly Serilog.ILogger Log = Serilog.Log.Logger.ForContext<MicrogameHandler>();

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
        if (mode is < CoupleDanceRules.OpenBanner or > CoupleDanceRules.ClaimReward) {
            return;
        }
        int eventId = packet.ReadInt();
        switch (mode) {
            case CoupleDanceRules.OpenBanner:
                HandleDanceBanner(session, eventId);
                return;
            case CoupleDanceRules.Invite:
                HandleDanceInvite(session, eventId, packet.ReadLong());
                return;
            case CoupleDanceRules.InviteAck:
                HandleDanceInviteAck(session, eventId, packet.ReadLong());
                return;
            case CoupleDanceRules.AutoReject:
                HandleDanceAutoReject(session, eventId, packet.ReadLong());
                packet.ReadByte(); // always 0
                return;
            case CoupleDanceRules.Accept:
                HandleDanceAccept(session, eventId, packet.ReadLong());
                return;
            case CoupleDanceRules.Decline:
                HandleDanceDecline(session, eventId, packet.ReadLong());
                return;
            case CoupleDanceRules.Cancel:
                HandleDanceCancel(session, eventId, packet.ReadLong());
                return;
            case CoupleDanceRules.Answer: {
                byte round = packet.ReadByte();
                short dance = packet.ReadShort();
                HandleDanceAnswer(session, eventId, round, dance);
                return;
            }
            case CoupleDanceRules.ClaimReward:
                HandleDanceClaimReward(session, eventId, packet.ReadInt());
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

    // Creates the reward items; an item that cannot be created is logged and left out.
    private static List<Item> CreateRewards(GameSession session, IEnumerable<RewardItem> rewards, int eventId) {
        var items = new List<Item>();
        foreach (RewardItem reward in rewards) {
            Item? item = session.Field?.ItemDrop.CreateItem(reward.ItemId, reward.Rarity, reward.Amount);
            if (item == null) {
                Log.Error("Microgame reward of event {EventId} for {CharacterId}: item {ItemId} could not be created",
                    eventId, session.CharacterId, reward.ItemId);
                continue;
            }
            items.Add(item);
        }
        return items;
    }

    // Puts the items in the inventory, or in the mail when the inventory is full; the items delivered either way.
    private static List<Item> Deliver(GameSession session, List<Item> items, int eventId) {
        var delivered = new List<Item>();
        foreach (Item item in items) {
            if (session.Item.Inventory.Add(item, true) || session.Item.MailItem(item)) {
                delivered.Add(item);
                continue;
            }
            Log.Error("Microgame reward of event {EventId} for {CharacterId}: item {ItemId} could not be added or mailed",
                eventId, session.CharacterId, item.Id);
        }
        return delivered;
    }

    // An invite or game of either microgame.
    private static bool IsBusy(FieldManager field, long characterId) {
        return field.Rps.IsBusy(characterId) || field.CoupleDance.IsBusy(characterId);
    }

    private static void HandleOpenBanner(GameSession session) {
        if (session.Field == null || FindRps(session) is not (_, Rps rps)) {
            return;
        }
        if (!HasTicket(session, rps) || IsBusy(session.Field, session.CharacterId)) {
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
        if (session.Field.CoupleDance.IsBusy(session.CharacterId) || session.Field.CoupleDance.IsBusy(otherId)) {
            return false;
        }
        return Vector3.Distance(session.Player.Position, other.Position) <= RpsRules.MaxDistance;
    }

    private static void HandleInvite(GameSession session, long inviteeId) {
        if (session.Field == null) {
            return;
        }
        if (!CanPlay(session, inviteeId, out FieldPlayer? invitee) || !session.Field.Rps.Invite(session.CharacterId, inviteeId, session.Field.FieldTick)) {
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
        if (!CanPlay(session, inviterId, out FieldPlayer? inviter) || !session.Field.Rps.Start(inviterId, session.CharacterId, session.Field.FieldTick)) {
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
        // Both tickets were found above; a consume that still fails ends the game without a count (the other ticket
        // is not taken when the first one failed).
        bool inviterPaid = inviter.Session.Item.Inventory.Consume(inviterTicket.Uid, 1);
        bool inviteePaid = inviterPaid && invitee.Session.Item.Inventory.Consume(inviteeTicket.Uid, 1);
        if (!inviteePaid) {
            if (inviterPaid) {
                Log.Error("RPS event {EventId}: ticket of {Invitee} not consumed after the ticket of {Inviter} was",
                    gameEvent.Id, game.Invitee, game.Inviter);
            }
            inviter.Session.Send(MicrogamePacket.Failed(game.Invitee, RpsRules.ReasonFailed));
            invitee.Session.Send(MicrogamePacket.Failed(game.Inviter, RpsRules.ReasonFailed));
            return;
        }

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

        // The reward counts as claimed only when at least one item reached the player; otherwise it stays open.
        List<Item> delivered = Deliver(session, CreateRewards(session, rps.Rewards[index].Rewards, gameEvent.Id), gameEvent.Id);
        if (delivered.Count == 0) {
            return;
        }
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.RPSRewardsClaimed, RpsRules.AddClaimed(claimed.String(), index));
        session.Send(MicrogamePacket.Rewards(delivered.Select(item => (RewardItem) item).ToList()));
    }

    // The client keeps one couple dance event (CGameEventValueCoupleDance) and sends its id with every mode; the
    // server takes the active event of that id.
    private static (GameEvent, CoupleDance)? FindDance(GameSession session, int eventId) {
        foreach (GameEvent gameEvent in session.FindEvent(GameEventType.CoupleDance)) {
            if (gameEvent.Id == eventId && gameEvent.IsActive() && gameEvent.Metadata.Data is CoupleDance dance) {
                return (gameEvent, dance);
            }
        }
        return null;
    }

    // A mode about the player's invite or dance that names another event than the invite was made for.
    private static bool OtherEvent(FieldManager field, long characterId, int eventId) {
        int matchEvent = field.CoupleDance.EventOf(characterId);
        return matchEvent != 0 && matchEvent != eventId;
    }

    private static void HandleDanceBanner(GameSession session, int eventId) {
        if (session.Field == null || FindDance(session, eventId) == null || IsBusy(session.Field, session.CharacterId)) {
            return;
        }
        session.Send(MicrogamePacket.CoupleDanceBanner(eventId));
    }

    // Both players can dance: the event runs, both are in this field within MicroGameMaxDistance (the client checks
    // the same before it sends) and neither has an RPS invite or game.
    private static bool CanDance(GameSession session, int eventId, long otherId, out FieldPlayer? other) {
        other = null;
        if (session.Field == null || otherId == session.CharacterId || FindDance(session, eventId) == null) {
            return false;
        }
        if (!session.Field.TryGetPlayerById(otherId, out other)) {
            return false;
        }
        if (session.Field.Rps.IsBusy(session.CharacterId) || session.Field.Rps.IsBusy(otherId)) {
            return false;
        }
        return Vector3.Distance(session.Player.Position, other.Position) <= RpsRules.MaxDistance;
    }

    private static void HandleDanceInvite(GameSession session, int eventId, long inviteeId) {
        if (session.Field == null) {
            return;
        }
        if (!CanDance(session, eventId, inviteeId, out FieldPlayer? invitee) || !session.Field.CoupleDance.Invite(session.CharacterId, inviteeId, eventId, session.Field.FieldTick)) {
            session.Send(MicrogamePacket.CoupleDanceFailed(eventId, inviteeId, CoupleDanceRules.ReasonPeerState));
            return;
        }
        invitee!.Session.Send(MicrogamePacket.CoupleDanceInvite(eventId, session.CharacterId));
    }

    // The invitee shows the request box: the inviter waits.
    private static void HandleDanceInviteAck(GameSession session, int eventId, long inviterId) {
        if (session.Field == null || !session.Field.CoupleDance.IsPending(inviterId, session.CharacterId)
            || OtherEvent(session.Field, session.CharacterId, eventId)) {
            return;
        }
        if (session.Field.TryGetPlayerById(inviterId, out FieldPlayer? inviter)) {
            inviter.Session.Send(MicrogamePacket.CoupleDanceInviteSent(eventId, session.CharacterId));
        }
    }

    // The invitee cannot show the request box (not on the banner, a box open, the inviter blocked, ...).
    private static void HandleDanceAutoReject(GameSession session, int eventId, long inviterId) {
        if (session.Field == null || OtherEvent(session.Field, session.CharacterId, eventId) || !session.Field.CoupleDance.RemovePending(inviterId, session.CharacterId)) {
            return;
        }
        if (session.Field.TryGetPlayerById(inviterId, out FieldPlayer? inviter)) {
            inviter.Session.Send(MicrogamePacket.CoupleDanceFailed(eventId, session.CharacterId, CoupleDanceRules.ReasonPeerState));
        }
    }

    // Accept: the invitee is in the dance state now; the stages go to both and the dance starts.
    private static void HandleDanceAccept(GameSession session, int eventId, long inviterId) {
        if (session.Field == null || !session.Field.CoupleDance.IsPending(inviterId, session.CharacterId)
            || OtherEvent(session.Field, session.CharacterId, eventId)) {
            return;
        }
        short[] stages = FindDance(session, eventId) is (_, CoupleDance dance)
            ? CoupleDanceRules.PickStages(dance.Dances.Length, dance.MaxRound, Random.Shared)
            : [];
        if (!CanDance(session, eventId, inviterId, out FieldPlayer? inviter) || !session.Field.CoupleDance.Start(inviterId, session.CharacterId, stages, session.Field.FieldTick)) {
            session.Field.CoupleDance.RemovePending(inviterId, session.CharacterId);
            session.Send(MicrogamePacket.CoupleDanceFailed(eventId, inviterId, CoupleDanceRules.ReasonAcceptPeerState));
            if (session.Field.TryGetPlayerById(inviterId, out FieldPlayer? waiting)) {
                waiting.Session.Send(MicrogamePacket.CoupleDanceFailed(eventId, session.CharacterId, CoupleDanceRules.ReasonPeerState));
            }
            return;
        }
        inviter!.Session.Send(MicrogamePacket.CoupleDanceAccepted(eventId, session.CharacterId));
        inviter.Session.Send(MicrogamePacket.CoupleDanceStart(eventId, stages));
        session.Send(MicrogamePacket.CoupleDanceStart(eventId, stages));
    }

    private static void HandleDanceDecline(GameSession session, int eventId, long inviterId) {
        if (session.Field == null || OtherEvent(session.Field, session.CharacterId, eventId) || !session.Field.CoupleDance.RemovePending(inviterId, session.CharacterId)) {
            return;
        }
        if (session.Field.TryGetPlayerById(inviterId, out FieldPlayer? inviter)) {
            inviter.Session.Send(MicrogamePacket.CoupleDanceDeclined(eventId, session.CharacterId));
        }
    }

    // The inviter's waiting box closed (cancel or time out), or a player left a running dance.
    private static void HandleDanceCancel(GameSession session, int eventId, long otherId) {
        if (session.Field == null || OtherEvent(session.Field, session.CharacterId, eventId)) {
            return;
        }
        if (session.Field.CoupleDance.RemovePending(session.CharacterId, otherId)) {
            if (session.Field.TryGetPlayerById(otherId, out FieldPlayer? invitee)) {
                invitee.Session.Send(MicrogamePacket.CoupleDanceCancelled(eventId, session.CharacterId));
            }
            return;
        }
        long partnerId = session.Field.CoupleDance.GamePartner(session.CharacterId);
        if (partnerId == 0 || partnerId != otherId) {
            return;
        }
        session.Field.CoupleDance.Remove(session.CharacterId);
        if (session.Field.TryGetPlayerById(partnerId, out FieldPlayer? partner)) {
            partner.Session.Send(MicrogamePacket.CoupleDanceFailed(eventId, session.CharacterId, CoupleDanceRules.ReasonFailed));
        }
    }

    // One answer per player and round; when both are in, each gets its own dance and the partner's. After the last
    // round both players record the finished dance. An answer for another event than the dance's, or for an event
    // that ended, ends the dance for both (neither would get out of the dance state otherwise).
    private static void HandleDanceAnswer(GameSession session, int eventId, int round, int danceIndex) {
        if (session.Field == null || session.Field.CoupleDance.GamePartner(session.CharacterId) == 0) {
            return;
        }
        if (OtherEvent(session.Field, session.CharacterId, eventId) || FindDance(session, eventId) is not (GameEvent gameEvent, CoupleDance dance)) {
            int matchEvent = session.Field.CoupleDance.EventOf(session.CharacterId);
            long partnerId = session.Field.CoupleDance.Remove(session.CharacterId);
            session.Send(MicrogamePacket.CoupleDanceFailed(matchEvent, partnerId, CoupleDanceRules.ReasonFailed));
            if (session.Field.TryGetPlayerById(partnerId, out FieldPlayer? partner)) {
                partner.Session.Send(MicrogamePacket.CoupleDanceFailed(matchEvent, session.CharacterId, CoupleDanceRules.ReasonFailed));
            }
            return;
        }
        if (!session.Field.CoupleDance.Answer(session.CharacterId, round, danceIndex, dance.Dances.Length, session.Field.FieldTick,
                out CoupleDanceMatchTable.RoundResult? answered)
            || answered is not { } result) {
            return;
        }
        session.Field.TryGetPlayerById(result.Inviter, out FieldPlayer? inviter);
        session.Field.TryGetPlayerById(result.Invitee, out FieldPlayer? invitee);
        if (inviter == null || invitee == null) {
            session.Field.CoupleDance.Remove(session.CharacterId);
            inviter?.Session.Send(MicrogamePacket.CoupleDanceFailed(eventId, result.Invitee, CoupleDanceRules.ReasonFailed));
            invitee?.Session.Send(MicrogamePacket.CoupleDanceFailed(eventId, result.Inviter, CoupleDanceRules.ReasonFailed));
            return;
        }
        inviter.Session.Send(MicrogamePacket.CoupleDanceResult(eventId, result.Round, result.Good, result.InviterDance, result.InviteeDance));
        invitee.Session.Send(MicrogamePacket.CoupleDanceResult(eventId, result.Round, result.Good, result.InviteeDance, result.InviterDance));
        if (!result.Finished) {
            return;
        }
        bool goodEnd = CoupleDanceRules.IsGoodEnd(result.Score, dance.GoodEndCount);
        FinishDance(inviter.Session, gameEvent, dance, goodEnd);
        FinishDance(invitee.Session, gameEvent, dance, goodEnd);
    }

    // The next open daily reward becomes completed (with the bonus on a good end); none left today records nothing,
    // and only a recorded dance counts for couple_dance_event, so the count stops with the day's rewards.
    private static void FinishDance(GameSession player, GameEvent gameEvent, CoupleDance dance, bool goodEnd) {
        GameEventUserValue states = player.GameEvent.Get(GameEventUserValueType.CoupleDanceRewardState, gameEvent.Id, Expiration());
        string? value = CoupleDanceRules.Complete(states.String(), dance.DailyRewards.Length, goodEnd);
        if (value == null) {
            return;
        }
        player.GameEvent.Set(gameEvent.Id, GameEventUserValueType.CoupleDanceRewardState, value);
        player.ConditionUpdate(ConditionType.couple_dance_event);
    }

    // The reward of a completed step, with the good-end bonus when it was a good end.
    private static void HandleDanceClaimReward(GameSession session, int eventId, int step) {
        if (session.Field == null || FindDance(session, eventId) is not (GameEvent gameEvent, CoupleDance dance)) {
            return;
        }
        GameEventUserValue states = session.GameEvent.Get(GameEventUserValueType.CoupleDanceRewardState, gameEvent.Id, Expiration());
        if (CoupleDanceRules.Claim(states.String(), dance.DailyRewards.Length, step) is not (string value, bool bonus)) {
            return;
        }
        IEnumerable<RewardItem> rewards = dance.DailyRewards[step];
        if (bonus) {
            rewards = rewards.Concat(dance.GoodEndRewards);
        }
        // The step counts as received only when at least one item reached the player; otherwise it stays claimable.
        List<Item> delivered = Deliver(session, CreateRewards(session, rewards, gameEvent.Id), gameEvent.Id);
        if (delivered.Count == 0) {
            return;
        }
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.CoupleDanceRewardState, value);
    }
}
