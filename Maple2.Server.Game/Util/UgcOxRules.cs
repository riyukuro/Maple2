using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The OX quiz event of a home (UGC OX): what the client sends and the rules the server applies to it.
/// </summary>
public static class UgcOxRules {
    // Map 61000011 "OX Quiz Stadium": the map whose trigger script ("main") runs the game with the OxQuizUGC widget.
    public const int StadiumMapId = 61000011;
    // The script's spectator spot (portal 99, the same place as the map's spawn point).
    public const int SpectatorPortalId = 99;
    // The maps the script's move_user actions name (61000021 the Custom OX map, 61000003 the Maple OX map). From the
    // stadium both are taken as the stadium itself: the fallen and the wrapped-up stay in the room (FORK.md).
    private static readonly int[] ScriptMoveMaps = [61000021, 61000003];
    // Item 20300990 "OX Quiz Host Ticket" (function OpenUGCEvent, parameter "ox"): s_ugcox_create_fail_item_lack
    // names it as the item creating the event needs.
    public const int TicketItemId = 20300990;
    // The script's start_mini_game: 10 rounds.
    public const int MaxRounds = 10;
    // Chosen here: an event starts with at least 2 entrants (the host is not one), else it is canceled and the fees
    // go back (s_ugcox_entry_fee_refund_meso_get: "canceled due to lack of participants").
    public const int MinEntrants = 2;
    // The longest question the server takes (chosen here; the client trims it and refuses an empty one).
    public const int MaxQuestionLength = 200;
    // The promote line's limits, the client's own defaults for the owner bar's tooltip (UIOxQuizDialog): 3 lines,
    // 60 s apart.
    public const int PromoteTotal = 3;
    public const int PromoteCooldownMs = 60000;

    // The create dialog's choices, from the client's create dialog (the defaults its combo boxes are filled with):
    // fees 1,000 to 9,000 mesos, 10 to 40 entrants, the winners' share 10% to 100% (the host keeps the rest).
    public static readonly IReadOnlyList<int> Fees = [1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000];
    public static readonly IReadOnlyList<int> Maxes = [10, 20, 30, 40];
    public static readonly IReadOnlyList<int> Shares = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

    /// <summary>
    /// The host's settings from the create dialog (HomeAction 10, sub 0). UIQuizOXCreateDialog::OnCreate
    /// (uiquizoxcreatedialog.cpp) writes the fee (__int64), the most entrants, and the winners' share in percent.
    /// </summary>
    public readonly record struct Settings(long Fee, int Max, int Share);

    public static Settings ReadSettings(IByteReader packet) {
        long fee = packet.ReadLong();
        int max = packet.ReadInt();
        int share = packet.ReadInt();
        return new Settings(fee, max, share);
    }

    // Settings the dialog can send: one of its choices each. Anything else is a forged packet.
    public static bool Valid(Settings settings) {
        return settings.Fee <= int.MaxValue && Fees.Contains((int) settings.Fee)
               && Maxes.Contains(settings.Max) && Shares.Contains(settings.Share);
    }

    // The map a move_user of the stadium script goes to (see ScriptMoveMaps).
    public static int MoveUserMap(int fieldMapId, int mapId) {
        return fieldMapId == StadiumMapId && ScriptMoveMaps.Contains(mapId) ? StadiumMapId : mapId;
    }

    // Why the host cannot create an event, as the client's s_ugcox_create_fail_* keys name it; null when it can.
    // The host pays the event's fee to host it (s_ugcox_create_complete: "You paid a fee of {eventFee} mesos").
    public static string? CreateRefusal(bool inOwnHome, bool designing, bool holdsTicket, bool hasEvent, bool hasFee) {
        if (!inOwnHome) {
            return "s_ugcox_create_fail_other_home";
        }
        if (designing) {
            return "s_ugcox_create_fail_design_home";
        }
        if (hasEvent) {
            return "s_ugcox_create_fail_already";
        }
        if (!holdsTicket) {
            return "s_ugcox_create_fail_item_lack";
        }
        if (!hasFee) {
            return "s_ugcox_create_fail_fee_lack";
        }
        return null;
    }

    /// <summary>
    /// The pot's split at the game's end: the winners share pot x share% equally, each part rounded down
    /// (s_ugcox_entry_prize_meso_get); the host gets the rest, the rounding included
    /// (s_ugcox_host_commission_meso_get). With no winner the host gets the whole pot (chosen here).
    /// </summary>
    public readonly record struct Split(long EachWinner, long Host);

    public static Split Payout(long pot, int share, int winners) {
        if (pot <= 0) {
            return new Split(0, 0);
        }
        if (winners <= 0) {
            return new Split(0, pot);
        }
        long each = pot * Math.Clamp(share, 0, 100) / 100 / winners;
        return new Split(each, pot - each * winners);
    }

    // The client's notice for a payment, with the amount as its {0}.
    public static StringCode PaymentText(UgcOxPaymentKind kind) {
        return kind switch {
            UgcOxPaymentKind.Prize => StringCode.s_ugcox_entry_prize_meso_get,
            UgcOxPaymentKind.Commission => StringCode.s_ugcox_host_commission_meso_get,
            _ => StringCode.s_ugcox_entry_fee_refund_meso_get,
        };
    }

    // The mail of a payee not online on the channel at the payment (chosen here: the client's systemmailcontent
    // table has no OX quiz row, so the mail carries its own text; the mesos are the mail's).
    public static Mail PaymentMail(UgcOxPayment payment) {
        string content = payment.Kind switch {
            UgcOxPaymentKind.Prize => "Your prize for winning the OX Quiz.",
            UgcOxPaymentKind.Commission => "Your mesos for hosting the OX Quiz.",
            _ => "The OX Quiz you entered did not take place. Your entry fee is returned.",
        };
        return new Mail {
            Type = MailType.System,
            ReceiverId = payment.CharacterId,
            SenderName = "OX Quiz",
            Title = "OX Quiz",
            Content = content,
            Meso = payment.Amount,
        };
    }

    // The question as the server keeps it: trimmed, at most MaxQuestionLength; null when empty.
    public static string? Question(string text) {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) {
            return null;
        }
        return trimmed.Length > MaxQuestionLength ? trimmed[..MaxQuestionLength] : trimmed;
    }
}
