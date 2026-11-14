using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class AttendanceHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Attendance;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    private enum Command : byte {
        Claim = 0,
        BeginTimer = 1,
        Unknown2 = 2,
        Unknown3 = 3,
        EarlyParticipation = 4,
        Unknown8 = 8,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Claim:
                HandleClaim(session);
                return;
            case Command.BeginTimer:
                HandleBeginTimer(session);
                return;
            case Command.EarlyParticipation:
                HandleEarlyParticipation(session, packet);
                return;
            case Command.Unknown8:
                // The selected event's id (uiattendgiftdialog.cpp:385, uicashattenddialog.cpp:591); no reply is known.
                AttendanceRules.ReadSelectedEvent(packet);
                return;
        }
    }

    private void HandleClaim(GameSession session) {
        IList<GameEvent> gameEvents = session.FindEvent(GameEventType.AttendGift);
        if (gameEvents.Count == 0) {
            return;
        }

        // Each event is attended on its own: one already attended today does not hold back the others.
        bool claimed = false;
        bool alreadyUsed = false;
        foreach (GameEvent gameEvent in gameEvents) {
            if (gameEvent?.Metadata.Data is not AttendGift attendGift || !RequirementMet(session, attendGift, gameEvent)) {
                continue;
            }

            // The play time the event asks for, counted as the client counts it down: the client sends this
            // claim by itself once its countdown is over (UIDTAttendPopup::Request, uidtattendpopup.cpp:97), and
            // again every 5 seconds until the day's completion time (101) arrives.
            if (session.GameEvent.AttendancePlaySeconds(gameEvent.Id, DateTime.Now) < attendGift.RequiredPlaySeconds) {
                continue;
            }

            if (AttendedToday(session, gameEvent)) {
                alreadyUsed = true;
                continue;
            }

            claimed |= Attend(session, attendGift, gameEvent);
        }

        if (!claimed && alreadyUsed) {
            session.Send(AttendancePacket.Error(AttendanceError.s_attendGift_item_attend_already_used));
        }
    }

    private static bool AttendedToday(GameSession session, GameEvent gameEvent) {
        DateTime completeTime = session.GameEvent.Get(GameEventUserValueType.AttendanceCompletedTimestamp, gameEvent.Id, gameEvent.EndTime).Long().FromEpochSeconds();
        return AttendanceRules.AttendedToday(completeTime, DateTime.Now);
    }

    // Gives the day's gift and marks the day attended, stored together; false when nothing was given. resetRunning:
    // a reset being stored refused it (nothing was stored).
    private bool Attend(GameSession session, AttendGift attendGift, GameEvent gameEvent, out bool resetRunning) {
        return GetRewards(session, attendGift, gameEvent, GameEventUserValueType.AttendanceCompletedTimestamp,
            DateTime.Now.ToEpochSeconds(), couponUid: 0, out _, out resetRunning);
    }

    private bool Attend(GameSession session, AttendGift attendGift, GameEvent gameEvent) => Attend(session, attendGift, gameEvent, out _);

    // The wait before an attend at game start that a reset being stored refused comes back, as a purchase save does.
    internal static readonly TimeSpan AttendRetryDelay = TimeSpan.FromSeconds(2);

    // The event's requirement, tested on the player's user values with id 0 as the client tests them; a kind 2
    // event on the player's unexpired item start (user value 104 of the event).
    private static bool RequirementMet(GameSession session, AttendGift attendGift, GameEvent gameEvent) {
        return AttendanceRules.RequirementMet(attendGift.Requirement, type => session.GameEvent.Peek((GameEventUserValueType) type, 0),
            session.GameEvent.Peek(GameEventUserValueType.AttendanceItemStartEnd, gameEvent.Id) != null);
    }

    private void HandleBeginTimer(GameSession session) {
        IList<GameEvent> gameEvents = session.FindEvent(GameEventType.AttendGift);
        if (gameEvents.Count == 0) {
            return;
        }

        foreach (GameEvent gameEvent in gameEvents) {
            long expirationTime = session.GameEvent.Get(GameEventUserValueType.AttendanceAccumulatedTime, gameEvent.Id, DateTime.Now.AddDays(1).Date.ToEpochSeconds()).ExpirationTime;
            if (expirationTime < DateTime.Now.ToEpochSeconds()) {
                session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.AttendanceAccumulatedTime, 0);
            }

            // An event without play time is attended here: the client sends no claim for it (AttendsAtGameStart).
            AttendAtGameStart(session, gameEvent);
        }
    }

    // A reset being stored refuses the attend (its save does not wait on the field thread); the client sends nothing
    // more for this event, so it comes back 2 s later on the field's queue while the session plays on that field, as
    // a purchase save does. A session that left the field is attended at its next BeginTimer.
    private void AttendAtGameStart(GameSession session, GameEvent gameEvent) {
        if (gameEvent.Metadata.Data is not AttendGift attendGift || !AttendanceRules.AttendsAtGameStart(attendGift.RequiredPlaySeconds)
            || !RequirementMet(session, attendGift, gameEvent) || AttendedToday(session, gameEvent)) {
            return;
        }
        if (!Attend(session, attendGift, gameEvent, out bool resetRunning) && resetRunning && session.Field is { } field) {
            int eventId = gameEvent.Id;
            field.Scheduler.Schedule(() => AttendAtGameStartRetry(session, field, eventId), AttendRetryDelay);
        }
    }

    private void AttendAtGameStartRetry(GameSession session, FieldManager field, int eventId) {
        if (!session.PlaysOn(field) || session.FindEvent(eventId) is not { } gameEvent) {
            return;
        }
        AttendAtGameStart(session, gameEvent);
    }

    // Mails the next reward. The mail, its item, the claim count and the day's mark (markType set to markValue: the
    // attended time, or the bought days) are stored in one transaction with the session's currency (a bought day's
    // price, already held) and the used coupon, so a crash cannot keep the mail and lose the claim (a second gift
    // after the restart) or the other way round. Memory takes the values once the transaction committed. False when
    // nothing was given (no reward left, no item, or the transaction was not stored). couponTaken is false when the
    // coupon left the bag between its save and the commit.
    private bool GetRewards(GameSession session, AttendGift attendGift, GameEvent gameEvent, GameEventUserValueType markType,
                            long markValue, long couponUid, out bool couponTaken, out bool resetRunning) {
        couponTaken = true;
        resetRunning = false;
        GameEventUserValue claimedValue = session.GameEvent.Get(GameEventUserValueType.AttendanceRewardsClaimed, gameEvent.Id, gameEvent.EndTime);
        int rewardsClaimed = claimedValue.Int();
        int index = AttendanceRules.RewardIndex(rewardsClaimed, attendGift.Items.Length);
        if (index < 0) {
            return false;
        }
        GameEventUserValue markCurrent = session.GameEvent.Get(markType, gameEvent.Id, gameEvent.EndTime);

        RewardItem reward = attendGift.Items[index];

        Item? item = session.Field?.ItemDrop.CreateItem(reward.ItemId, reward.Rarity, reward.Amount);
        if (item == null) {
            return false;
        }

        var receiverMail = new Mail() {
            ReceiverId = session.CharacterId,
            Type = MailType.System,
            SenderName = "MapleStory 2",
            Title = attendGift.MailTitle,
            Content = attendGift.MailContent,
        };

        var claimed = new GameEventUserValue(GameEventUserValueType.AttendanceRewardsClaimed, claimedValue.ExpirationTime, gameEvent.Id);
        claimed.SetValue((rewardsClaimed + 1).ToString());
        var mark = new GameEventUserValue(markType, markCurrent.ExpirationTime, gameEvent.Id);
        mark.SetValue(markValue.ToString());

        Mail? storedMail = null;
        Item? storedItem = null;
        bool taken = true;
        bool stored = session.TrySavePurchase(db => {
            storedMail = db.CreateMail(receiverMail);
            if (storedMail == null) {
                Logger.Error("Failed to create mail for attendance reward to user {CharacterId}", session.CharacterId);
                return false;
            }
            storedItem = db.CreateItem(storedMail.Id, item);
            if (storedItem == null) {
                Logger.Error("Failed to create attendance reward item {ItemId}", reward.ItemId);
                return false;
            }
            if (!db.SaveGameEventUserValues(session.CharacterId, [claimed, mark])) {
                return false;
            }
            if (couponUid != 0 && !session.Item.Inventory.SaveConsumed(db, couponUid, 1)) {
                return false;
            }
            // Under the account lock, so no session save between the commit and memory writes the old values.
            db.AfterCommit(() => {
                session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.AttendanceRewardsClaimed, rewardsClaimed + 1);
                session.GameEvent.Set(gameEvent.Id, markType, markValue);
                if (couponUid != 0) {
                    taken = session.Item.Inventory.ConsumeStored(couponUid, 1);
                }
            });
            return true;
        }, out resetRunning);
        couponTaken = taken;
        if (!stored && resetRunning) {
            Logger.Information("Attendance reward {ItemId} of event {EventId} not stored for {CharacterId}: a reset is being stored",
                reward.ItemId, gameEvent.Id, session.CharacterId);
            return false;
        }
        if (!stored || storedMail == null || storedItem == null) {
            Logger.Error("Attendance reward {ItemId} of event {EventId} not stored for {CharacterId}", reward.ItemId, gameEvent.Id, session.CharacterId);
            return false;
        }

        storedMail.Items.Add(storedItem);
        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = session.CharacterId,
                MailId = storedMail.Id,
            });
        } catch { /* ignored */ }
        return true;
    }

    private void HandleEarlyParticipation(GameSession session, IByteReader packet) {
        // uid of the pay-attend coupon item when the box's coupon checkbox is ticked, else 0
        (int eventId, long couponUid) = AttendanceRules.ReadEarlyParticipation(packet);
        GameEvent? gameEvent = session.FindEvent(eventId);
        if (gameEvent?.Metadata.Data is not AttendGift attendGift || !RequirementMet(session, attendGift, gameEvent)) {
            return;
        }

        int attended = session.GameEvent.Get(GameEventUserValueType.AttendanceRewardsClaimed, gameEvent.Id, gameEvent.EndTime).Int();
        int paid = session.GameEvent.Get(GameEventUserValueType.AttendanceEarlyParticipationRemaining, gameEvent.Id, gameEvent.EndTime).Int();
        Item? coupon = couponUid != 0 ? session.Item.Inventory.Get(couponUid) : null;
        if (coupon != null && (coupon.IsExpired() || coupon.Amount < 1)) {
            coupon = null; // cannot be consumed: answered as no coupon
        }
        AttendanceRules.PayStep step = AttendanceRules.PayCheck(attendGift.Pay, attended, attendGift.Items.Length, paid, couponUid != 0, coupon?.Id ?? 0, session.Currency.Meso, session.Currency.Meret);
        switch (step) {
            case AttendanceRules.PayStep.NoCoupon:
                session.Send(AttendancePacket.Error(AttendanceError.s_attendGift_payAttend_result_hasNotCoupon));
                return;
            case AttendanceRules.PayStep.NoMeso:
                session.Send(AttendancePacket.Error(AttendanceError.s_attendGift_payAttend_result_lackMoney));
                return;
            case AttendanceRules.PayStep.NoMeret:
                session.Send(AttendancePacket.Error(AttendanceError.s_attendGift_payAttend_result_lackMerat));
                return;
            case AttendanceRules.PayStep.Coupon:
            case AttendanceRules.PayStep.Meso:
            case AttendanceRules.PayStep.Meret:
                break;
            default:
                return;
        }

        // The merets are held before the gift without a packet (another thread can take merets too: a wedding hall
        // paid by the partner) and given back when the gift fails; the packet goes where the take was.
        long meretPrice = step == AttendanceRules.PayStep.Meret ? attendGift.Pay!.Price : 0;
        if (meretPrice > 0 && !session.Currency.TryTakeMeret(meretPrice, notify: false)) {
            session.Send(AttendancePacket.Error(AttendanceError.s_attendGift_payAttend_result_lackMerat));
            return;
        }
        // The mesos the same way: held here without a packet, given back when the gift fails.
        long mesoPrice = step == AttendanceRules.PayStep.Meso ? attendGift.Pay!.Price : 0;
        if (mesoPrice > 0 && !session.Currency.TryTakeMeso(mesoPrice, notify: false)) {
            session.Send(AttendancePacket.Error(AttendanceError.s_attendGift_payAttend_result_lackMoney));
            return;
        }

        // The gift, the held price, the used coupon and the bought day are stored together (GetRewards), so a failed
        // gift costs nothing and a crash cannot keep one without the others.
        long usedCoupon = step == AttendanceRules.PayStep.Coupon ? couponUid : 0;
        bool given = GetRewards(session, attendGift, gameEvent, GameEventUserValueType.AttendanceEarlyParticipationRemaining,
            paid + 1, usedCoupon, out bool couponTaken, out _);
        if (given) {
            switch (step) {
                case AttendanceRules.PayStep.Coupon:
                    // The row is stored as used; the item that left the bag before the commit keeps its amount in
                    // memory and its next save writes it back (as the cash attend item does). Logged so it is seen.
                    if (!couponTaken) {
                        Logger.Error("Attendance {EventId}: the coupon {ItemUid} was stored as used but not taken from the bag of {CharacterId}",
                            gameEvent.Id, couponUid, session.CharacterId);
                    }
                    break;
                case AttendanceRules.PayStep.Meso:
                    if (mesoPrice > 0) {
                        session.Currency.NotifyMeso();
                    } else {
                        session.Currency.ChangeMeso(-attendGift.Pay!.Price);
                    }
                    break;
                case AttendanceRules.PayStep.Meret:
                    if (meretPrice > 0) {
                        session.Currency.NotifyMeretTaken(meretPrice);
                    } else {
                        session.Currency.ChangeMeret(-attendGift.Pay!.Price);
                    }
                    break;
            }
        }
        if (!given && meretPrice > 0) {
            session.Currency.ReturnMeret(meretPrice);
        }
        if (!given && mesoPrice > 0) {
            session.Currency.ReturnMeso(mesoPrice, notify: false); // no meso packet was sent for the take
        }
    }
}
