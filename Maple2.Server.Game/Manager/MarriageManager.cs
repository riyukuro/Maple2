using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Sync;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.Util.Sync;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class MarriageManager {
    private readonly GameSession session;

    public Marriage Marriage;
    public WeddingHall WeddingHall;
    private readonly CancellationTokenSource tokenSource;

    public MarriagePartner Partner => session.CharacterId == Marriage.Partner1.CharacterId ? Marriage.Partner2 : Marriage.Partner1;

    public WeddingHall? UnconfirmedReservation;
    private long couponItemUid;

    private readonly ILogger logger = Log.Logger.ForContext<MarriageManager>();

    public MarriageManager(GameSession session) {
        this.session = session;
        tokenSource = new CancellationTokenSource();
        using GameStorage.Request db = session.GameStorage.Context();
        Marriage = db.GetMarriage(session.CharacterId) ?? Marriage.Default;
        Marriage.Exp = GetExp();

        WeddingHall = db.GetWeddingHall(Marriage) ?? WeddingHall.Default;
    }

    // A proposal waiting for its answer. The proposer keeps the target's character id, the target keeps
    // the proposer's. Both sides are set together and cleared together: on the answer, on the proposer's
    // cancel, when either player leaves the field (also run on disconnect), and after the timeout.
    private static readonly object ProposalLock = new();
    // The target's answer box sends answer 6 (no answer) by itself after 20 s, which ends the proposal;
    // the 30 s timeout only covers a client that never sends it.
    private const long ProposalTimeoutMs = 30000;
    private long proposalTo;
    private long proposalFrom;
    private long proposalExpireTick;

    // Stores the proposal on both players. False when either already has one waiting.
    public bool BeginProposal(FieldPlayer target) {
        MarriageManager other = target.Session.Marriage;
        lock (ProposalLock) {
            ExpireProposal();
            other.ExpireProposal();
            if (proposalTo != 0 || proposalFrom != 0 || other.proposalTo != 0 || other.proposalFrom != 0) {
                return false;
            }
            long expireTick = Environment.TickCount64 + ProposalTimeoutMs;
            proposalTo = target.Value.Character.Id;
            proposalExpireTick = expireTick;
            other.proposalFrom = session.CharacterId;
            other.proposalExpireTick = expireTick;
            return true;
        }
    }

    // Ends the proposal this player sent to the target. False when there is no such proposal waiting.
    public bool EndProposalTo(FieldPlayer target) {
        MarriageManager other = target.Session.Marriage;
        lock (ProposalLock) {
            ExpireProposal();
            if (proposalTo == 0 || proposalTo != target.Value.Character.Id || other.proposalFrom != session.CharacterId) {
                return false;
            }
            proposalTo = 0;
            other.proposalFrom = 0;
            return true;
        }
    }

    // Drops any proposal this player is part of, on both sides (field leave and disconnect). The other
    // player is found by character id among this channel's sessions, so it does not matter which field
    // either is on now; a player who moved to another channel lost the record with that move.
    // The couple message of this player: stored, saved and shown to both partners.
    // The partner is found among this channel's sessions; on another channel they see it at their next load.
    public void SetCoupleMessage(string message) {
        if (!CoupleMessageRule.Apply(Marriage, session.CharacterId, message)) {
            return;
        }

        // Only this player's column is written, and both messages are then read back: the copy held here is
        // stale when the partner changed theirs on another channel.
        using (GameStorage.Request db = session.GameStorage.Context()) {
            db.SaveCoupleMessage(Marriage.Id, session.CharacterId, CoupleMessageRule.Clean(message));
            Marriage? fresh = db.GetMarriage(session.CharacterId);
            if (fresh != null) {
                CoupleMessageRule.Merge(Marriage, fresh);
            }
        }
        session.Send(WeddingPacket.UpdateMarriage(Marriage));

        long partnerId = Partner.CharacterId;
        if (partnerId != 0 && session.FindSession(partnerId, out GameSession? partnerSession)) {
            Marriage other = partnerSession.Marriage.Marriage;
            if (CoupleMessageRule.Apply(other, session.CharacterId, message)) {
                partnerSession.Send(WeddingPacket.UpdateMarriage(other));
            }
        }
    }

    public void ClearProposal() {
        lock (ProposalLock) {
            long partnerId = proposalTo != 0 ? proposalTo : proposalFrom;
            if (partnerId != 0 && session.FindSession(partnerId, out GameSession? partnerSession)) {
                MarriageManager other = partnerSession.Marriage;
                if (other.proposalTo == session.CharacterId) other.proposalTo = 0;
                if (other.proposalFrom == session.CharacterId) other.proposalFrom = 0;
            }
            proposalTo = 0;
            proposalFrom = 0;
            divorceAskFrom = 0;
        }
    }

    // Both sides of a proposal carry the same expiry, so each side drops its own record.
    private void ExpireProposal() {
        if ((proposalTo != 0 || proposalFrom != 0) && Environment.TickCount64 >= proposalExpireTick) {
            proposalTo = 0;
            proposalFrom = 0;
        }
    }

    public void Load() {
        CheckEngagementExpiration();
        CheckMarriageHallExpiration();
        CheckDivorceExpiration();
        session.Send(WeddingPacket.UpdateMarriage(Marriage));
        session.Send(WeddingPacket.UpdateHall(WeddingHall));
        // The client shows a ticket again when it gets it again, so a field entry sends them all anew.
        lock (ticketsSent) {
            ticketsSent.Clear();
        }
        SendTickets();
        if (!ticketCheckScheduled) {
            ticketCheckScheduled = true;
            session.Scheduler.ScheduleRepeated(SendTickets, TimeSpan.FromSeconds(TicketCheckSeconds), skipFirst: true);
        }
    }

    // The client offers the way in only at the moment a ticket arrives (WeddingManager::OnInvitationTicket
    // marks the list, RefreshTickets then drops a ticket whose time is more than 1800 s away), so a ticket
    // sent before the window opens shows nothing. The session's own event queue looks once a minute for a
    // window that opened since, and sends each ticket once.
    private const int TicketCheckSeconds = 60;
    private readonly HashSet<(long MarriageId, long CeremonyTime)> ticketsSent = [];
    private bool ticketCheckScheduled;

    private void SendTicket(long marriageId, WeddingHall hall) {
        lock (ticketsSent) {
            if (!WeddingInviteRules.NewTicket(ticketsSent, marriageId, hall.CeremonyTime)) {
                return;
            }
        }
        session.Send(WeddingPacket.Ticket(marriageId, hall));
    }

    // The ticket is the client's way into the hall (Wedding 0x1a). The couple gets its own while the
    // ceremony time is inside the client's window.
    private void SendTickets() {
        if (WeddingHall.Id != 0 && Marriage.Id != 0
            && WeddingCeremony.InEnterWindow(WeddingHall.CeremonyTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
            SendTicket(Marriage.Id, WeddingHall);
        }
        SendGuestTickets();
    }

    // An invited guest gets the hall's ticket the same way, under the marriage id that Wedding 0x1a sends back.
    private void SendGuestTickets() {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        List<WeddingHall> halls;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            halls = db.GetGuestWeddingHalls(session.CharacterId, now - WeddingCeremony.EnterWindowSeconds, now + WeddingCeremony.EnterWindowSeconds);
        }
        foreach (WeddingHall hall in halls) {
            if (WeddingInviteRules.GuestTicket(hall.GuestList, session.CharacterId, hall.MarriageId, Marriage.Id, hall.CeremonyTime, now, WeddingCeremony.EnterWindowSeconds)) {
                SendTicket(hall.MarriageId, hall);
            }
        }
    }

    // Wedding 0x1c: the guests are stored on the hall, the meso for them is taken, each gets the invitation
    // mail (mail type 112, whose wedding text the client's mail list reads, WeddingManager::ReadTicketMail) and
    // the sender gets Wedding 0x1d and the new invited list. Either of the couple may send.
    public WeddingError SendInvitations(IReadOnlyCollection<long> guests) {
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using GameStorage.Request db = session.GameStorage.Context();
        if (WeddingHall.Id != 0) {
            // The partner may have sent invitations since this session loaded the hall.
            WeddingHall? stored = db.GetWeddingHall(Marriage);
            if (stored != null) {
                WeddingHall.GuestList = stored.GuestList;
            }
        }

        Dictionary<long, long> accounts = guests.Count == 0 ? new Dictionary<long, long>() : db.GetCharacterAccounts(guests.ToList());
        (bool send, WeddingError error, long cost) = WeddingInviteRules.Check(WeddingHall.Id, WeddingHall.CeremonyTime, now, WeddingHall.GuestList,
            guests, accounts.Count, constants.WeddingInvitationMaxCount, session.Currency.Meso, constants.WeddingInvitationMeso);
        if (!send) {
            return error;
        }

        // The stored list is tested again under the row lock; nothing is paid or mailed when it refuses.
        Dictionary<long, long>? list = db.AddWeddingGuests(WeddingHall.Id, accounts, constants.WeddingInvitationMaxCount, out WeddingError addError);
        if (list == null) {
            return addError != WeddingError.none ? addError : WeddingError.s_wedding_result_not_find_weddinghall;
        }
        WeddingHall.GuestList = list;
        session.Currency.Meso -= cost;

        MailGuests(db, WeddingHall, guests, WeddingGuestMail.Invite);

        session.Send(WeddingPacket.InvitationOk());
        session.Send(WeddingPacket.UpdateHall(WeddingHall));
        long partnerId = Partner.CharacterId;
        if (partnerId != 0 && session.FindSession(partnerId, out GameSession? partnerSession) && partnerSession.Marriage.WeddingHall.Id == WeddingHall.Id) {
            partnerSession.Marriage.WeddingHall.GuestList = new Dictionary<long, long>(list);
            partnerSession.Send(WeddingPacket.UpdateHall(partnerSession.Marriage.WeddingHall));
        }
        return WeddingError.none;
    }

    // A mail of type 112 to each guest, from this player. The mail list shows the title and text the server
    // sends (UIMailListDialog::SetMailInfo) and reads a text given as a string key from the client's own table
    // (MailTextValue::Load); the table has a pair of keys for the invitation, the change and the cancel.
    private void MailGuests(GameStorage.Request db, WeddingHall hall, IEnumerable<long> guests, WeddingGuestMail kind) {
        string senderName = session.Player.Value.Character.Name;
        (StringCode title, StringCode content) = WeddingInviteRules.MailKeys(kind);
        foreach (long guestId in guests) {
            var mail = new Mail {
                ReceiverId = guestId,
                SenderId = session.CharacterId,
                SenderName = senderName,
                Type = MailType.WeddingInvite,
            };
            mail.SetTitle(title);
            mail.SetContent(content);
            mail.SetWeddingInvite(hall, cancel: kind == WeddingGuestMail.Cancel);
            mail = db.CreateMail(mail);
            if (mail == null) {
                logger.Error("Failed to create the wedding guest mail for {CharacterId}", guestId);
                continue;
            }

            try {
                session.World.MailNotification(new MailNotificationRequest {
                    CharacterId = guestId,
                    MailId = mail.Id,
                });
            } catch { /* ignored */ }
        }
    }

    // The vow was completed in the hall (trigger WeddingVowComplete) and the marriage is stored as married.
    public void SetMarried(long marriageTime) {
        if (Marriage.Id == 0) {
            return;
        }

        Marriage.Status = MaritalStatus.Married;
        Marriage.MarriageTime = marriageTime;
        MarriageInfo info = session.Player.Value.Character.MarriageInfo;
        info.Status = MaritalStatus.Married;
        info.MarriageTime = marriageTime;
        session.Send(WeddingPacket.UpdateMarriage(Marriage));
    }

    private void CheckEngagementExpiration() {
        if (Marriage.CreationTime <= 0 || Marriage.Status != MaritalStatus.Engaged ||
            Marriage.CreationTime.FromEpochSeconds().AddDays(7) >= DateTime.Now) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.DeleteMarriage(Marriage.Id)) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_err_system));
            return;
        }

        var mail = new Mail {
            ReceiverId = Marriage.Partner2.CharacterId,
            SenderId = Marriage.Partner2.CharacterId,
            Type = MailType.System,
            Content = 40000006.ToString(),
        };

        mail = db.CreateMail(mail);
        if (mail == null) {
            logger.Error("Failed to create mail for expired engagement for {CharacterId}", Marriage.Partner2.CharacterId);
        } else {
            try {
                session.World.MailNotification(new MailNotificationRequest {
                    CharacterId = Marriage.Partner2.CharacterId,
                    MailId = mail.Id,
                });
            } catch { /* ignored */
            }
        }

        var partnerMail = new Mail {
            ReceiverId = Marriage.Partner1.CharacterId,
            SenderId = Marriage.Partner1.CharacterId,
            Type = MailType.System,
            Content = 40000006.ToString(),
        };

        partnerMail = db.CreateMail(partnerMail);
        if (partnerMail == null) {
            logger.Error("Failed to create mail for expired engagement for {CharacterId}", Marriage.Partner1.CharacterId);
        } else {
            try {
                session.World.MailNotification(new MailNotificationRequest {
                    CharacterId = Marriage.Partner1.CharacterId,
                    MailId = partnerMail.Id,
                });
            } catch { /* ignored */
            }
        }


        try {
            session.World.Marriage(new MarriageRequest {
                ReceiverId = Marriage.Partner1.CharacterId,
                RemoveMarriage = new MarriageRequest.Types.RemoveMarriage(),
            });
        } catch { /* ignored */
        }

        RemoveMarriage(false);
    }

    // WeddingManager::IsInProgress: the wedding recall works while married, also during a divorce.
    public static bool CanRecall(MaritalStatus status) {
        return status is MaritalStatus.Married or MaritalStatus.DivorceFiledByGroom or MaritalStatus.DivorceFiledByBride;
    }

    // How long the partner's recall notice stays open. The server pack has no keep-time row for it.
    private const long RecallKeepSeconds = 300;

    /// <summary>
    /// The wedding recall scroll (RecallWedding): offers the recall to the partner, who must be online
    /// (WeddingManager::ApplyPartner). False when the field forbids recalls or the partner cannot get it.
    /// </summary>
    public bool Recall() {
        if (session.Field == null || !session.Field.Metadata.CashCall.Recall || !CanRecall(Marriage.Status)) {
            return false;
        }
        long partnerId = Partner.CharacterId;
        if (partnerId == 0 || !session.PlayerInfo.GetOrFetch(partnerId, out PlayerInfo? partnerInfo) || !partnerInfo.Online) {
            return false;
        }

        try {
            MarriageResponse response = session.World.Marriage(new MarriageRequest {
                ReceiverId = partnerId,
                Recall = new MarriageRequest.Types.Recall {
                    CallerId = session.CharacterId,
                    Name = session.PlayerName,
                    MapId = session.Field.MapId,
                    EndTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + RecallKeepSeconds,
                },
            });
            return response.Error == 0;
        } catch (Grpc.Core.RpcException ex) {
            logger.Error(ex, "Wedding recall by {CharacterId} failed", session.CharacterId);
            return false;
        }
    }

    // The reserved hall ends WeddingCeremony.HallKeepSeconds after the ceremony time. Only the hall goes: the
    // couple stays what it is, engaged when the vow was not reached and married when it was.
    public void CheckMarriageHallExpiration() {
        if (!WeddingCeremony.HallExpired(WeddingHall.Id, WeddingHall.CeremonyTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.DeleteWeddingHall(WeddingHall.Id)) {
            logger.Error("Failed to delete wedding hall for {CharacterId}. Hall Id: {WeddingHallId}", session.CharacterId, WeddingHall.Id);
            return;
        }

        try {
            session.World.Marriage(new MarriageRequest {
                ReceiverId = Partner.CharacterId,
                RemoveWeddingHall = new MarriageRequest.Types.RemoveWeddingHall(),
            });
        } catch { /* ignored */ }

        (Marriage, WeddingHall) = WeddingCeremony.AfterHallExpiry(Marriage);
    }

    #region Divorce
    private const int MailFiled = 40000001; // to the filer: the partner's name, the end of the cooling off
    private const int MailFiledOn = 40000002; // to the other: the filer's name, the end of the cooling off
    private const int MailAgreedFiler = 40000003; // to the filer: the other agreed, the fee comes back
    private const int MailAgreed = 40000004; // to the one who agreed
    private const int MailCoolingOffEnd = 40000005; // to both: the cooling off ended, divorced
    private const int MailCancelled = 40000007; // to both: the filer's name
    private const int MailMutual = 40000011; // to both

    // A mutual divorce the partner asked for and this player has not answered: the partner's character id.
    // Taken under ProposalLock; dropped when this player leaves the field and after DivorceRules.AskTimeoutMs.
    private long divorceAskFrom;
    private long divorceAskExpireTick;

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // This player filed the divorce that is in its cooling off.
    private bool IsFiler => DivorceRules.FilerId(Marriage.Status, Marriage.Partner1.CharacterId, Marriage.Partner2.CharacterId) == session.CharacterId;

    // Reads the stored marriage again and shows it. Used after every change of its state, on both partners.
    public void Reload() {
        using (GameStorage.Request db = session.GameStorage.Context()) {
            Marriage = db.GetMarriage(session.CharacterId) ?? Marriage.Default;
            session.Player.Value.Character.MarriageInfo = db.GetMarriageInfo(session.CharacterId);
        }
        Marriage.Exp = GetExp();
        session.Send(WeddingPacket.UpdateMarriage(Marriage));
    }

    // The partner reads the marriage again: directly on this channel, through the world server on another.
    // An offline partner reads it at the next login.
    private void ReloadPartner(long partnerId) {
        if (partnerId == 0) {
            return;
        }
        if (session.FindSession(partnerId, out GameSession? partnerSession)) {
            partnerSession.Marriage.Reload();
            return;
        }

        try {
            session.World.Marriage(new MarriageRequest {
                ReceiverId = partnerId,
                Reload = new MarriageRequest.Types.Reload(),
            });
        } catch { /* ignored */ }
    }

    private void SendDivorceMail(GameStorage.Request db, long receiverId, int mailId, long meso = 0, params string[] args) {
        var mail = new Mail {
            ReceiverId = receiverId,
            SenderId = receiverId,
            Type = MailType.System,
            Content = mailId.ToString(),
            ContentArgs = args.Select(arg => ("str", arg)).ToList(),
            Meso = meso,
        };

        mail = db.CreateMail(mail);
        if (mail == null) {
            logger.Error("Failed to create divorce mail {MailId} for {CharacterId}", mailId, receiverId);
            return;
        }

        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = receiverId,
                MailId = mail.Id,
            });
        } catch { /* ignored */ }
    }

    private static string MailDate(long time) {
        return DateTimeOffset.FromUnixTimeSeconds(time).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    }

    // The two ends that need no request: a filed divorce is complete when its cooling off ends, and a divorced
    // marriage is gone when the propose cooltime ends. Checked when the marriage is loaded and before anything
    // that depends on its state.
    public void CheckDivorceExpiration() {
        if (Marriage.Id == 0) {
            return;
        }

        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        long now = Now;
        long marriageId = Marriage.Id;
        long partnerId = Partner.CharacterId;
        if (DivorceRules.CoolingOffOver(Marriage.Status, Marriage.DivorceTime, constants.WeddingCoolingOffDay, now)) {
            // Divorced at the end of the cooling off, whenever that is noticed.
            long end = DivorceRules.CoolingOffEnd(Marriage.DivorceTime, constants.WeddingCoolingOffDay);
            using (GameStorage.Request db = session.GameStorage.Context()) {
                if (db.CompleteDivorce(marriageId, filed: true, end.FromEpochSeconds())) {
                    SendDivorceMail(db, session.CharacterId, MailCoolingOffEnd);
                    SendDivorceMail(db, partnerId, MailCoolingOffEnd);
                }
            }
            Reload();
            ReloadPartner(partnerId);
        }

        if (Marriage.Id == marriageId && DivorceRules.ProposeCooltimeOver(Marriage.Status, Marriage.DivorceTime, constants.WeddingProposeCooltime, now)) {
            using (GameStorage.Request db = session.GameStorage.Context()) {
                db.DeleteDivorced(marriageId);
            }
            Reload();
            ReloadPartner(partnerId);
        }
    }

    // weddingState, marriageDate and coolingOff of an npc script condition.
    public bool ScriptCondition(MaritalStatus? userState, int marriageDays, string? coolingOff) {
        CheckDivorceExpiration();
        return DivorceRules.ScriptCondition(userState, marriageDays, coolingOff, Marriage.Status, IsFiler, Marriage.MarriageTime, Now);
    }

    // A divorce step of an npc script function. False when it is refused; the client was then sent the reason.
    public bool DivorceFunction(string function) {
        if (session.Field == null) {
            return false;
        }

        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        long now = Now;
        long fee = constants.WeddingDivorceMeso;
        WeddingError error;
        switch (function) {
            case DivorceRules.FunctionFile:
                error = DivorceRules.CheckFile(Marriage.Status, Marriage.MarriageTime, constants.WeddingDivorceRequireMarriageDay, session.Currency.Meso, fee, now);
                break;
            case DivorceRules.FunctionAgree:
                error = DivorceRules.CheckAgree(Marriage.Status, IsFiler, Marriage.DivorceTime, constants.WeddingCoolingOffDay, now);
                break;
            case DivorceRules.FunctionCancel:
                error = DivorceRules.CheckCancel(Marriage.Status, IsFiler, Marriage.DivorceTime, constants.WeddingCoolingOffDay, now);
                break;
            case DivorceRules.FunctionMutual:
                error = DivorceRules.CheckMutual(Marriage.Status);
                break;
            default:
                logger.Warning("Unknown divorce function {Function} for {CharacterId}", function, session.CharacterId);
                return false;
        }

        if (error != WeddingError.none) {
            session.Send(WeddingPacket.Error(error));
            CheckDivorceExpiration();
            return false;
        }

        long marriageId = Marriage.Id;
        long partnerId = Partner.CharacterId;
        string partnerName = Partner.Name;
        if (function == DivorceRules.FunctionMutual) {
            // The partner is asked (Wedding 8) and answers with Wedding 9. Both stand at the npc.
            if (!session.Field.TryGetPlayerById(partnerId, out FieldPlayer? partner)) {
                session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_err_same_field));
                return false;
            }

            lock (ProposalLock) {
                MarriageManager other = partner.Session.Marriage;
                long tick = Environment.TickCount64;
                // One ask at a time, in either direction: the box is not opened again while one waits.
                if (DivorceRules.AskOpen(other.divorceAskFrom, other.divorceAskExpireTick, tick)
                    || DivorceRules.AskOpen(divorceAskFrom, divorceAskExpireTick, tick)) {
                    return false;
                }
                other.divorceAskFrom = session.CharacterId;
                other.divorceAskExpireTick = tick + DivorceRules.AskTimeoutMs;
            }
            partner.Session.Send(WeddingPacket.DivorceRequest());
            return true;
        }

        bool stored;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            switch (function) {
                case DivorceRules.FunctionFile:
                    // The fee is taken first and saved in the transaction of the filing, so that no filed
                    // divorce is stored without its fee paid; a filing that is not stored gives it back.
                    session.Currency.Meso -= fee;
                    stored = db.FileDivorce(marriageId, session.CharacterId, now.FromEpochSeconds(), session.Player);
                    if (!stored) {
                        session.Currency.Meso = DivorceRules.MesoAfterFiling(session.Currency.Meso, fee, stored);
                    } else {
                        string end = MailDate(DivorceRules.CoolingOffEnd(now, constants.WeddingCoolingOffDay));
                        SendDivorceMail(db, session.CharacterId, MailFiled, 0, partnerName, end);
                        SendDivorceMail(db, partnerId, MailFiledOn, 0, session.PlayerName, end);
                    }
                    break;
                case DivorceRules.FunctionAgree:
                    stored = db.CompleteDivorce(marriageId, filed: true, now.FromEpochSeconds());
                    if (stored) {
                        // The filer paid the fee when filing and gets it back with the mail.
                        SendDivorceMail(db, partnerId, MailAgreedFiler, fee);
                        SendDivorceMail(db, session.CharacterId, MailAgreed);
                    }
                    break;
                default:
                    stored = db.CancelDivorce(marriageId, session.CharacterId);
                    if (stored) {
                        SendDivorceMail(db, session.CharacterId, MailCancelled, 0, session.PlayerName);
                        SendDivorceMail(db, partnerId, MailCancelled, 0, session.PlayerName);
                    }
                    break;
            }
        }

        if (!stored) {
            // The stored marriage is not what this session held (the partner changed it on another channel).
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_invaild_state_divorce));
        }
        Reload();
        ReloadPartner(partnerId);
        return stored;
    }

    // Wedding 9: the answer to the partner's ask for a mutual divorce.
    public void DivorceAnswer(short answer) {
        long askerId;
        long expireTick;
        lock (ProposalLock) {
            askerId = divorceAskFrom;
            expireTick = divorceAskExpireTick;
            divorceAskFrom = 0;
        }

        bool askerInField = askerId != 0 && session.Field != null && session.Field.TryGetPlayerById(askerId, out _);
        if (!DivorceRules.CanAnswerAsk(askerId, expireTick, Environment.TickCount64, Partner.CharacterId, askerInField)) {
            // Nothing is asked (any more): the client's text for a marriage that is in no state to divorce.
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_invaild_state_divorce));
            return;
        }
        if (answer != DivorceRules.AnswerYes) {
            return;
        }

        WeddingError error = DivorceRules.CheckMutual(Marriage.Status);
        if (error == WeddingError.none) {
            using GameStorage.Request db = session.GameStorage.Context();
            if (db.CompleteDivorce(Marriage.Id, filed: false, Now.FromEpochSeconds())) {
                SendDivorceMail(db, session.CharacterId, MailMutual);
                SendDivorceMail(db, askerId, MailMutual);
            } else {
                error = WeddingError.s_wedding_result_only_marriage_user;
            }
        }

        if (error != WeddingError.none) {
            session.Send(WeddingPacket.Error(error));
        }
        Reload();
        ReloadPartner(askerId);
    }

    // Wedding 7: the partner who did not file goes to the divorce field, where the npc of the agree step is.
    public void DivorceVisit() {
        CheckDivorceExpiration();
        WeddingError error = DivorceRules.CheckVisit(Marriage.Status, IsFiler);
        if (error != WeddingError.none) {
            session.Send(WeddingPacket.Error(error));
            return;
        }
        if (!DivorceRules.CanVisitFrom(session.Field != null, session.Player.IsDead, session.Field is Field.DungeonFieldManager, session.Field?.Wedding != null)) {
            session.Send(FieldEnterPacket.Error(MigrationError.s_move_err_default));
            return;
        }

        session.Send(session.PrepareField(session.ServerTableMetadata.ConstantsTable.WeddingDivorceFieldID)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }
    #endregion

    public void RemoveMarriage(bool update = true) {
        session.Marriage.Marriage = Marriage.Default;
        session.Player.Value.Character.MarriageInfo = new MarriageInfo();
        if (update) {
            session.Send(WeddingPacket.UpdateMarriage(Marriage));
        }
    }

    public void RemoveWeddingHall(bool update = true) {
        session.Marriage.WeddingHall = WeddingHall.Default;
        if (update) {
            session.Send(WeddingPacket.UpdateHall(WeddingHall));
        }
    }

    private long GetExp() {
        DateTime creationTime = DateTimeOffset.FromUnixTimeSeconds(Marriage.CreationTime).LocalDateTime;
        int daysMarried = (DateTime.Now - creationTime).Days;

        return daysMarried * 5 + Marriage.ExpHistory.Sum(exp => exp.Amount);
    }

    public WeddingError Propose(FieldPlayer target) {
        if (!session.Item.Inventory.Find(Constant.ProposalItemId).Any()) {
            return WeddingError.s_wedding_result_err_not_find_propose_item;
        }

        // Neither side may be engaged, married, in a divorce or in the propose cooltime after one.
        CheckDivorceExpiration();
        WeddingError stateError = DivorceRules.ProposeFrom(Marriage.Status);
        if (stateError != WeddingError.none) {
            return stateError;
        }

        target.Session.Marriage.CheckDivorceExpiration();
        stateError = DivorceRules.ProposeTo(target.Session.Marriage.Marriage.Status);
        if (stateError != WeddingError.none) {
            return stateError;
        }

        if (!session.Buddy.IsBuddy(target.Value.Character.Id)) {
            return WeddingError.s_wedding_result_err_not_buddy;
        }

        if (!target.Session.Item.Inventory.Find(Constant.ProposalItemId).Any()) {
            return WeddingError.s_wedding_result_err_not_find_propose_item;
        }

        return WeddingError.s_wedding_msg_box_propose_to;
    }

    public WeddingError AcceptProposal(FieldPlayer target) {
        // Find the proposal items of each player
        Item? proposalItem = session.Item.Inventory.Find(Constant.ProposalItemId).FirstOrDefault();
        if (proposalItem == null) {
            return WeddingError.s_wedding_result_err_not_find_propose_item;
        }

        Item? otherProposalItem = target.Session.Item.Inventory.Find(Constant.ProposalItemId).FirstOrDefault();
        if (otherProposalItem == null) {
            return WeddingError.s_wedding_result_err_not_find_propose_item;
        }

        // Remove the proposal items from each player
        if (!session.Item.Inventory.Consume(proposalItem.Uid, 1)) {
            return WeddingError.s_wedding_result_err_not_find_propose_item;

        }

        if (!target.Session.Item.Inventory.Consume(otherProposalItem.Uid, 1)) {
            // TODO: Something is broken about this
            //return WeddingError.s_wedding_result_err_not_find_propose_item;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        Marriage? marriage = db.CreateMarriage(session.CharacterId, target.Session.CharacterId);
        if (marriage == null) {
            return WeddingError.s_wedding_result_err_system;
        }
        Marriage = marriage;
        session.Send(WeddingPacket.Engaged(Marriage));

        Marriage? targetMarriage = db.GetMarriage(target.Session.CharacterId);
        if (targetMarriage == null) {
            return WeddingError.s_wedding_result_err_system;
        }

        target.Session.Marriage.Marriage = targetMarriage;
        target.Session.Send(WeddingPacket.Engaged(targetMarriage));
        return WeddingError.none;
    }

    public bool ValidHallTime(long ceremonyTime, out WeddingError error) {
        using GameStorage.Request db = session.GameStorage.Context();
        if (ceremonyTime > 0) {
            if (!db.WeddingHallTimeIsAvailable(ceremonyTime)) {
                error = WeddingError.s_wedding_result_already_same_reservation;
                return false;
            }

            if (ceremonyTime < DateTime.Now.ToEpochSeconds()) {
                error = WeddingError.s_wedding_result_early_wedding_reserve;
                return false;
            }
        }
        error = WeddingError.none;
        return true;
    }

    public WeddingError ReserveHall(WeddingHall hall, bool useCoupon = false) {
        if (session.Field is null) return WeddingError.s_wedding_result_err_system;
        if (!ValidHallTime(hall.CeremonyTime, out WeddingError error)) {
            return error;
        }

        if (!session.TableMetadata.WeddingTable.Packages.TryGetValue(hall.PackageId, out WeddingPackage? package) ||
            !package.Halls.TryGetValue(hall.PackageHallId, out WeddingPackage.HallData? hallData)) {
            return WeddingError.s_wedding_result_err_invalid_wedding_hall;
        }

        if (useCoupon) {
            ItemTag couponTag = package.Id switch {
                1 => ItemTag.WeddingHallCoupon_Grade1,
                2 => ItemTag.WeddingHallCoupon_Grade2,
                3 => ItemTag.WeddingHallCoupon_Grade3,
                _ => ItemTag.None,
            };
            IList<Item> items = session.Item.Inventory.Find(couponTag).ToList();
            if (!items.Any()) {
                return WeddingError.s_wedding_result_lack_coupon;
            }

            couponItemUid = items.First().Uid;
        } else {
            if (session.Currency.CanAddMeret(-hallData.MeretCost) != -hallData.MeretCost) {
                return WeddingError.s_wedding_result_err_lack_meratal;
            }
        }

        if (!session.Field.TryGetPlayer(session.Marriage.Marriage.Partner1.Name, out FieldPlayer? partner)) {
            return WeddingError.s_wedding_result_err_same_field;
        }

        session.Marriage.UnconfirmedReservation = hall;
        partner.Session.Marriage.UnconfirmedReservation = hall;

        partner.Session.Send(WeddingPacket.Reserve(hall));
        return WeddingError.s_wedding_result_send_wedding_hall_reservation;
    }

    public WeddingError ConfirmReservation() {
        if (session.Field is null) return WeddingError.s_wedding_result_err_system;
        if (UnconfirmedReservation == null) {
            return WeddingError.s_wedding_result_err_invalid_wedding_hall;
        }

        if (!session.TableMetadata.WeddingTable.Packages.TryGetValue(UnconfirmedReservation.PackageId, out WeddingPackage? package) ||
            !package.Halls.TryGetValue(UnconfirmedReservation.PackageHallId, out WeddingPackage.HallData? hallData)) {
            return WeddingError.s_wedding_result_err_invalid_wedding_hall;
        }

        if (!session.Field.TryGetPlayerById(session.Marriage.Marriage.Partner1.CharacterId, out FieldPlayer? partner)) {
            return WeddingError.s_wedding_result_err_same_field;
        }

        if (!PayWeddingHall(partner, hallData, 0, out WeddingError payError)) {
            return payError;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        WeddingHall? weddingHall = db.CreateWeddingHall(UnconfirmedReservation, Marriage);
        if (weddingHall == null) {
            logger.Error("Failed to create wedding hall for {CharacterId}", session.CharacterId);
            return WeddingError.s_wedding_result_err_system;
        }

        WeddingHall = weddingHall;
        partner.Session.Marriage.WeddingHall = weddingHall;

        partner.Session.Send(WeddingPacket.ConfirmReservation(WeddingHall));
        session.Send(WeddingPacket.ConfirmReservation(WeddingHall));

        session.ConditionUpdate(ConditionType.wedding_hall_reserve);
        partner.Session.ConditionUpdate(ConditionType.wedding_hall_reserve);

        return WeddingError.s_wedding_propose_ok;
    }

    private bool PayWeddingHall(FieldPlayer partner, WeddingPackage.HallData hallData, long differenceCost, out WeddingError error) {
        if (couponItemUid != 0) {
            if (!partner.Session.Item.Inventory.Consume(couponItemUid, 1)) {
                error = WeddingError.s_wedding_result_lack_coupon;
                return false;
            }
            couponItemUid = 0;
        } else {
            long cost = differenceCost > 0 ? differenceCost : hallData.MeretCost;
            if (partner.Session.Currency.CanAddMeret(-cost) != -cost) {
                error = WeddingError.s_wedding_result_err_lack_meratal;
                return false;
            }
            partner.Session.Currency.Meret -= cost;

            // Mail gift only when paying with Meret, otherwise users can exploit.
            MailReservationGifts(partner, hallData);
        }

        error = WeddingError.none;
        return true;
    }

    private void MailReservationGifts(FieldPlayer partner, WeddingPackage.HallData metadata) {
        var mail = new Mail {
            ReceiverId = partner.Value.Character.Id,
            SenderId = partner.Value.Character.Id,
            Content = 40000008.ToString(), // Specific ID in string/systemmailcontent[locale].xml
            Type = MailType.System,
        };

        using GameStorage.Request db = session.GameStorage.Context();
        mail = db.CreateMail(mail);
        if (mail == null) {
            logger.Error("Failed to create mail for wedding hall reservation for {CharacterId}", partner.Value.Character.Id);
            return;
        }

        // The reservation gifts are the hall's weddingItem rows, the ones the client's reservation page lists
        // (UIWeddingHallReservationPageSelect, m_items); the weddingCompleteItem rows come with the vow.
        foreach (WeddingPackage.HallData.Item rewardItem in metadata.Items) {
            Item? item = session.Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
            if (item == null) {
                continue;
            }

            item = db.CreateItem(mail.Id, item);
            if (item == null) {
                logger.Error("Failed to create item for wedding hall reservation for {CharacterId}", partner.Value.Character.Id);
                continue;
            }

            mail.Items.Add(item);
        }

        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = partner.Value.Character.Id,
                MailId = mail.Id,
            });
        } catch { /* ignored */
        }
    }

    public void CancelReservationRequest() {
        if (session.Field is null) return;
        if (session.CharacterId != WeddingHall.ReserverCharacterId) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_only_booking_character));
            return;
        }

        // If the wedding is about to start or has started, cannot cancel
        if (DateTime.Now >= WeddingHall.CeremonyTime.FromEpochSeconds().AddMinutes(-5)) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_late_wedding_reserve_modify_time));
            return;
        }

        if (!session.Field.TryGetPlayerById(Partner.CharacterId, out FieldPlayer? fieldPartner)) {
            session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_err_same_field));
            return;
        }

        session.Send(WeddingPacket.Error(WeddingError.s_wedding_result_send_wedding_hall_reservation_cancel));
        fieldPartner.Session.Send(WeddingPacket.RequestCancelReservation());
    }

    public WeddingError CancelReservationReply(WeddingError response) {
        if (session.Field is null) return WeddingError.s_wedding_result_err_system;
        if (!session.Field.TryGetPlayerById(Partner.CharacterId, out FieldPlayer? fieldPartner)) {
            return WeddingError.s_wedding_result_err_same_field;
        }

        if (response == WeddingError.decline_reservation_cancel) {
            session.Send(WeddingPacket.DeclineCancelReservation());
            fieldPartner.Session.Send(WeddingPacket.DeclinedCancelReservation(response));
            return WeddingError.none;
        }

        // Give coupon
        int couponItemId = WeddingHall.PackageId switch {
            1 => Constant.Grade1WeddingCouponItemId,
            2 => Constant.Grade2WeddingCouponItemId,
            3 => Constant.Grade3WeddingCouponItemId,
            _ => 0,
        };

        if (couponItemId == 0) {
            logger.Error("Failed to find coupon item for wedding hall {WeddingHallId}", WeddingHall.Id);
            return WeddingError.s_wedding_result_err_system;
        }

        // TODO: Fix rarity ?
        Item? coupon = session.Field?.ItemDrop.CreateItem(couponItemId, 1, 1);
        if (coupon == null) {
            logger.Error("Failed to create wedding coupon for {CharacterId}", session.CharacterId);
            return WeddingError.s_wedding_result_err_system;
        }

        var mail = new Mail {
            ReceiverId = Partner.CharacterId,
            SenderId = Partner.CharacterId,
            Type = MailType.System,
            Content = 40000013.ToString(), // Specific ID in string/systemmailcontent[locale].xml
        };

        using GameStorage.Request db = session.GameStorage.Context();

        mail = db.CreateMail(mail);
        if (mail == null) {
            logger.Error("Failed to create mail for wedding hall cancellation for {CharacterId}", Partner.CharacterId);
            return WeddingError.s_wedding_result_err_system;
        }

        coupon = db.CreateItem(mail.Id, coupon);
        if (coupon == null) {
            logger.Error("Failed to create coupon item for wedding hall cancellation for {CharacterId}", Partner.CharacterId);
            return WeddingError.s_wedding_result_err_system;
        }

        mail.Items.Add(coupon);
        try {
            session.World.MailNotification(new MailNotificationRequest {
                CharacterId = Partner.CharacterId,
                MailId = mail.Id,
            });
        } catch { /* ignored */
        }

        // The guests are read and the row deleted under the row's lock.
        Dictionary<long, long>? cancelGuests = db.CancelWeddingHall(WeddingHall.Id);
        if (cancelGuests == null) {
            logger.Error("Failed to delete wedding hall for {CharacterId}. Hall Id: {WeddingHallId}", session.CharacterId, WeddingHall.Id);
        } else {
            MailGuests(db, WeddingHall, cancelGuests.Keys, WeddingGuestMail.Cancel);
        }

        session.Send(WeddingPacket.AcceptCancelReservation(response));
        fieldPartner.Session.Send(WeddingPacket.AcceptCancelReservation(response));
        WeddingHall = WeddingHall.Default;
        fieldPartner.Session.Marriage.WeddingHall = WeddingHall.Default;

        session.ConditionUpdate(ConditionType.wedding_hall_cancel);
        fieldPartner.Session.ConditionUpdate(ConditionType.wedding_hall_cancel);
        return WeddingError.none;
    }

    public WeddingError CancelNoRefund() {
        if (session.Field is null) return WeddingError.s_wedding_result_err_system;
        if (!session.Field.TryGetPlayerById(Partner.CharacterId, out FieldPlayer? fieldPartner)) {
            return WeddingError.s_wedding_result_err_same_field;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        // The guests are read and the row deleted under the row's lock.
        Dictionary<long, long>? cancelGuests = db.CancelWeddingHall(WeddingHall.Id);
        if (cancelGuests == null) {
            logger.Error("Failed to delete wedding hall for {CharacterId}. Hall Id: {WeddingHallId}", session.CharacterId, WeddingHall.Id);
        } else {
            MailGuests(db, WeddingHall, cancelGuests.Keys, WeddingGuestMail.Cancel);
        }

        session.Send(WeddingPacket.AcceptCancelReservation(WeddingError.accept_reservation_cancel));
        fieldPartner.Session.Send(WeddingPacket.AcceptCancelReservation(WeddingError.accept_reservation_cancel));
        WeddingHall = WeddingHall.Default;
        fieldPartner.Session.Marriage.WeddingHall = WeddingHall.Default;

        session.ConditionUpdate(ConditionType.wedding_hall_cancel);
        fieldPartner.Session.ConditionUpdate(ConditionType.wedding_hall_cancel);

        return WeddingError.none;
    }

    public WeddingError ChangeReservationRequest(WeddingHall hall) {
        if (session.Field is null) return WeddingError.s_wedding_result_err_system;
        if (WeddingHall.Id == 0) {
            return WeddingError.s_wedding_result_err_invalid_wedding_hall;
        }

        // If it's all the same, it's unnecessary to change
        if (hall.CeremonyTime == WeddingHall.CeremonyTime &&
            hall.PackageId == WeddingHall.PackageId &&
            hall.PackageHallId == WeddingHall.PackageHallId &&
            hall.Public == WeddingHall.Public) {
            return WeddingError.s_wedding_result_already_weddinghall_reserve;
        }

        if (!session.TableMetadata.WeddingTable.Packages.TryGetValue(hall.PackageId, out WeddingPackage? unconfirmedPackage) ||
            !unconfirmedPackage.Halls.TryGetValue(hall.PackageHallId, out WeddingPackage.HallData? unconfirmedHallData)) {
            return WeddingError.s_wedding_result_err_invalid_wedding_hall;
        }

        if (!session.TableMetadata.WeddingTable.Packages.TryGetValue(WeddingHall.PackageId, out WeddingPackage? currentWeddingPackage) ||
            !currentWeddingPackage.Halls.TryGetValue(WeddingHall.PackageHallId, out WeddingPackage.HallData? currentHallData)) {
            return WeddingError.s_wedding_result_err_invalid_wedding_hall;
        }

        // Cannot tier down
        if (unconfirmedHallData.Tier < currentHallData.Tier) {
            return WeddingError.s_wedding_result_err_invalid_wedding_hall;
        }

        long cost = GetWeddingHallChangeCost(currentHallData, unconfirmedHallData);
        if (session.Currency.CanAddMeret(-cost) != -cost) {
            return WeddingError.s_wedding_result_err_lack_meratal;
        }

        if (!ValidHallTime(hall.CeremonyTime, out WeddingError error)) {
            return error;
        }

        if (!session.Field.TryGetPlayerById(Partner.CharacterId, out FieldPlayer? partnerPlayer)) {
            return WeddingError.s_wedding_result_err_same_field;
        }

        UnconfirmedReservation = hall;
        partnerPlayer.Session.Marriage.UnconfirmedReservation = hall;

        partnerPlayer.Session.Send(WeddingPacket.ChangeReservationRequest(hall, cost));
        return WeddingError.s_wedding_result_send_wedding_hall_reservation_change;
    }

    public WeddingError ChangeReservationReply(WeddingError response) {
        if (session.Field is null) return WeddingError.s_wedding_result_err_system;
        if (UnconfirmedReservation == null) {
            return WeddingError.s_wedding_result_err_invalid_wedding_hall;
        }

        if (!session.TableMetadata.WeddingTable.Packages.TryGetValue(UnconfirmedReservation.PackageId, out WeddingPackage? package) ||
            !package.Halls.TryGetValue(UnconfirmedReservation.PackageHallId, out WeddingPackage.HallData? hallData)) {
            return WeddingError.s_wedding_result_err_invalid_wedding_hall;
        }

        if (!session.Field.TryGetPlayerById(session.Marriage.Marriage.Partner1.CharacterId, out FieldPlayer? partner)) {
            return WeddingError.s_wedding_result_err_same_field;
        }

        if (response == WeddingError.decline_reservation_change) {
            // TODO: Confirm these packets. There seems to be no response.
            session.Send(WeddingPacket.DeclineReservationChange());
            partner.Session.Send(WeddingPacket.DeclineReservationChange());
            return WeddingError.none;
        }

        if (!session.TableMetadata.WeddingTable.Packages.TryGetValue(WeddingHall.PackageId, out WeddingPackage? currentWeddingPackage) ||
            !currentWeddingPackage.Halls.TryGetValue(WeddingHall.PackageHallId, out WeddingPackage.HallData? currentHallData)) {
            logger.Error("Failed to find current wedding hall data for {CharacterId}", session.CharacterId);
            return WeddingError.s_wedding_result_err_system;
        }

        long differenceCost = GetWeddingHallChangeCost(currentHallData, hallData);

        if (!PayWeddingHall(partner, hallData, differenceCost, out WeddingError payError)) {
            return payError;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        // The changed hall is stored as a new row that keeps the guests of the old one; the read of the
        // guests, the delete and the new row are one transaction under the old row's lock.
        WeddingHall before = WeddingHall;
        WeddingHall? weddingHall = db.ChangeWeddingHall(WeddingHall.Id, UnconfirmedReservation, Marriage);
        if (weddingHall == null) {
            logger.Error("Failed to change wedding hall for {CharacterId}. Hall Id: {WeddingHallId}", session.CharacterId, WeddingHall.Id);
            return WeddingError.s_wedding_result_err_system;
        }

        WeddingHall = weddingHall;
        partner.Session.Marriage.WeddingHall = weddingHall;
        if (WeddingInviteRules.ChangeMail(before, weddingHall)) {
            MailGuests(db, weddingHall, weddingHall.GuestList.Keys, WeddingGuestMail.Change);
        }
        session.Send(WeddingPacket.AcceptReservationChange(WeddingHall, differenceCost));
        partner.Session.Send(WeddingPacket.AcceptReservationChange(WeddingHall, differenceCost));

        return WeddingError.none;
    }

    private long GetWeddingHallChangeCost(WeddingPackage.HallData currentHallData, WeddingPackage.HallData unconfirmedHallData) {
        return Math.Max(0, unconfirmedHallData.MeretCost - currentHallData.MeretCost);
    }

    #region PlayerInfo Events
    private void BeginListen(MarriagePartner partner) {
        if (Marriage.Id == 0) {
            return;
        }
        // Clean up previous token if necessary
        if (partner.TokenSource != null) {
            logger.Warning("BeginListen called on Member {Id} that was already listening", partner.CharacterId);
            EndListen(partner);
        }

        partner.TokenSource = CancellationTokenSource.CreateLinkedTokenSource(tokenSource.Token);
        CancellationToken token = partner.TokenSource.Token;
        var listener = new PlayerInfoListener(UpdateField.Marriage, (type, info) => SyncUpdate(token, partner.CharacterId, type, info));
        session.PlayerInfo.Listen(partner.Info!.CharacterId, listener);
    }

    private void EndListen(MarriagePartner partner) {
        partner.TokenSource?.Cancel();
        partner.TokenSource?.Dispose();
        partner.TokenSource = null;
    }

    private bool SyncUpdate(CancellationToken cancel, long id, UpdateField type, IPlayerInfo info) {
        if (cancel.IsCancellationRequested || Marriage.Id == 0) {
            return true;
        }

        MarriagePartner? partner = Marriage.Partner1.CharacterId == id ? Marriage.Partner1 :
            Marriage.Partner2.CharacterId == id ? Marriage.Partner2 : null;
        if (partner == null) {
            return true;
        }

        bool wasOnline = partner.Info!.Online;
        string name = partner.Info.Name;
        partner.Info.Update(type, info);

        if (name != partner.Info.Name || wasOnline != partner.Info.Online) {
            session.Send(WeddingPacket.UpdateMarriage(Marriage));
        }
        return false;
    }
    #endregion
}
