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

        foreach (GameEvent gameEvent in gameEvents) {
            if (gameEvent?.Metadata.Data is not AttendGift attendGift || !RequirementMet(session, attendGift)) {
                continue;
            }

            // The play time the event asks for, counted as the client counts it down: the client sends this
            // claim by itself once its countdown is over (UIDTAttendPopup::Request, uidtattendpopup.cpp:97), and
            // again every 5 seconds until the day's completion time (101) arrives.
            if (session.GameEvent.AttendancePlaySeconds(gameEvent.Id, DateTime.Now) < attendGift.RequiredPlaySeconds) {
                continue;
            }

            DateTime completeTime = session.GameEvent.Get(GameEventUserValueType.AttendanceCompletedTimestamp, gameEvent.Id, gameEvent.EndTime).Long().FromEpochSeconds();
            if (DateTime.Now < completeTime || completeTime.Date == DateTime.Now.Date) {
                session.Send(AttendancePacket.Error(AttendanceError.s_attendGift_item_attend_already_used));
                return;
            }

            AttendanceRules.GiveThenTake(() => GetRewards(session, attendGift, gameEvent),
                () => session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.AttendanceCompletedTimestamp, DateTime.Now.ToEpochSeconds()));
        }
    }

    // The event's requirement, tested on the player's user values with id 0 as the client tests them.
    private static bool RequirementMet(GameSession session, AttendGift attendGift) {
        return AttendanceRules.RequirementMet(attendGift.Requirement, type => session.GameEvent.Peek((GameEventUserValueType) type, 0));
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
        }
    }

    // Mails the next reward. The claim is counted only after the mail and its item are stored; false
    // when nothing was given (no reward left, no item, or the mail could not be stored).
    private bool GetRewards(GameSession session, AttendGift attendGift, GameEvent gameEvent) {
        int rewardsClaimed = session.GameEvent.Get(GameEventUserValueType.AttendanceRewardsClaimed, gameEvent.Id, gameEvent.EndTime).Int();
        int index = AttendanceRules.RewardIndex(rewardsClaimed, attendGift.Items.Length);
        if (index < 0) {
            return false;
        }

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

        using GameStorage.Request db = session.GameStorage.Context();
        try {
            receiverMail = db.CreateMail(receiverMail);
            if (receiverMail == null) {
                Logger.Error("Failed to create mail for attendance reward to user {CharacterId}", session.CharacterId);
                return false;
            }

            item = db.CreateItem(receiverMail.Id, item);
            if (item == null) {
                Logger.Error("Failed to create attendance reward item {ItemId}", reward.ItemId);
                return false;
            }
        } catch (Exception ex) {
            Logger.Error(ex, "Failed to store attendance reward {ItemId} for {CharacterId}", reward.ItemId, session.CharacterId);
            return false;
        }

        receiverMail.Items.Add(item);
        session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.AttendanceRewardsClaimed, rewardsClaimed + 1);

        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = session.CharacterId,
                MailId = receiverMail.Id,
            });
        } catch { /* ignored */ }
        return true;
    }

    private void HandleEarlyParticipation(GameSession session, IByteReader packet) {
        // uid of the pay-attend coupon item when the box's coupon checkbox is ticked, else 0
        (int eventId, long couponUid) = AttendanceRules.ReadEarlyParticipation(packet);
        GameEvent? gameEvent = session.FindEvent(eventId);
        if (gameEvent?.Metadata.Data is not AttendGift attendGift || !RequirementMet(session, attendGift)) {
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

        // The gift first: the price is taken and the bought day counted only when the mail is stored,
        // so a failed gift costs nothing. The coupon was tested above, so its consume does not fail.
        AttendanceRules.GiveThenTake(() => GetRewards(session, attendGift, gameEvent), () => {
            switch (step) {
                case AttendanceRules.PayStep.Coupon:
                    session.Item.Inventory.Consume(couponUid, 1);
                    break;
                case AttendanceRules.PayStep.Meso:
                    session.Currency.Meso -= attendGift.Pay!.Price;
                    break;
                case AttendanceRules.PayStep.Meret:
                    session.Currency.Meret -= attendGift.Pay!.Price;
                    break;
            }
            session.GameEvent.Set(gameEvent.Id, GameEventUserValueType.AttendanceEarlyParticipationRemaining, paid + 1);
        });
    }
}
