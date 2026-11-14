using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Util;

// The merat payback (feature Payback, table/paybacktable.xml): the merets an account spends on the payback product
// in a month come back by mail the month after, by the share of the table's adventure levels the account gained in
// that month (PaybackRules). The account holds one month's record (PaybackRecord); the record of an earlier month
// is paid when the account is next seen in a later one: its first field entry, the monthly reset of a session
// playing over the month's end, or a payback buy.
public static class PaybackService {
    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(PaybackService));

    private static PaybackTable Table(GameSession session) => session.TableMetadata.PaybackTable;
    private static ConstantsTable Constants(GameSession session) => session.ServerTableMetadata.ConstantsTable;

    // PaybackManagerClient::IsEnabled: the client shows payback only while game event FinishPayback runs.
    public static bool EventRunning(GameSession session) => session.FindEvent(GameEventType.FinishPayback).Count > 0;

    // The product's buy is a payback buy: the configured payback product, in the period, paid in merets, while the
    // event runs.
    public static bool IsPaybackBuy(GameSession session, int premiumMarketId, int childMarketItemId, MeretMarketCurrencyType currency, DateTime now) {
        ConstantsTable constants = Constants(session);
        return currency != MeretMarketCurrencyType.Meso
               && EventRunning(session)
               && (PaybackRules.IsPaybackProduct(constants, premiumMarketId) || PaybackRules.IsPaybackProduct(constants, childMarketItemId))
               && PaybackRules.InPeriod(Table(session), constants, now);
    }

    // The client refuses a payback buy in the settlement minutes of a month's first day (notice 0xd4f); so does the
    // server, for a client whose clock differs.
    public static bool BuyRefused(GameSession session, DateTime now) => PaybackRules.IsResetWindow(Table(session), Constants(session), now);

    // Before a payback buy is paid: an earlier period's record is paid first. False while it is still due (its save
    // failed or a reset was being stored): the buy is refused, so nothing is taken that would not be counted.
    public static bool ReadyForBuy(GameSession session, DateTime now) {
        Settle(session, now);
        return !PaybackRules.Due(session.Player.Value.Account.Payback, Constants(session), now);
    }

    // Counts a paid payback buy in the account's period (ReadyForBuy ran before the pay). The caller stores the
    // record with the purchase (GameStorage.Request.SavePayback).
    public static void Spend(GameSession session, long price, DateTime now) {
        if (price <= 0) {
            return;
        }
        Account account = session.Player.Value.Account;
        if (PaybackRules.Due(account.Payback, Constants(session), now)) {
            Logger.Warning("Payback buy of {Price} merets not counted, month {Month} unpaid AccountId={AccountId}", price, account.Payback!.Month, account.Id);
            return;
        }
        PaybackRecord? current = PaybackRules.Current(account.Payback, Table(session), Constants(session), now, account.PrestigeLevel);
        if (current == null) {
            return;
        }
        account.Payback = current with { Spent = current.Spent + price };
    }

    // Pays an earlier month's record by mail and starts this month's (or clears the record outside the period).
    // Runs on the session's field thread; the record and the mail are stored together or not at all.
    public static bool Settle(GameSession session, DateTime now) {
        Account account = session.Player.Value.Account;
        PaybackTable table = Table(session);
        ConstantsTable constants = Constants(session);
        PaybackRecord? held = account.Payback;
        PaybackRecord? next = PaybackRules.Current(held, table, constants, now, account.PrestigeLevel);
        if (Equals(held, next)) {
            return false;
        }

        Mail? mail = null;
        if (held != null && PaybackRules.Due(held, constants, now)) {
            int levels = PaybackRules.LevelsGained(held, account.PrestigeLevel);
            long merat = PaybackRules.Reward(table, constants, levels, held.Spent);
            if (merat > 0) {
                mail = PaybackMail(session, constants, held, levels, merat, now);
            }
        }

        account.Payback = next;
        Mail? created = null;
        bool stored = session.TrySavePurchase(db => {
            if (mail != null) {
                created = db.CreateMail(mail);
                if (created == null) {
                    return false;
                }
            }
            return db.SavePayback(session.Player);
        });
        if (!stored) {
            // Kept for the next try (the next field entry or monthly reset): nothing was paid.
            account.Payback = held;
            Logger.Warning("Payback month {Month} not settled AccountId={AccountId}", held?.Month, account.Id);
            return false;
        }
        if (created != null) {
            Logger.Information("Payback month {Month}: {Merat} merets mailed AccountId={AccountId} MailId={MailId}",
                held?.Month, created.Meret, account.Id, created.Id);
            session.Mail.Notify(true);
        }
        return true;
    }

    // Mail PaybackMailId (string/systemmailcontent): the title's {0} the month; the content's {0} the month, {1} the
    // merets paid, {2} the share's percent, {3} the adventure levels gained, {4} the merets spent on the product.
    internal static Mail PaybackMail(GameSession session, ConstantsTable constants, PaybackRecord record, int levels, long merat, DateTime now) {
        string month = (record.Month % 100).ToString();
        string percent = PaybackRules.FormatPercent(PaybackRules.PercentUpTo(session.TableMetadata.PaybackTable, levels));
        var mail = new Mail {
            Type = MailType.System,
            ReceiverId = session.CharacterId,
            Content = constants.PaybackMailId.ToString(),
            TitleArgs = [("str", month)],
            ContentArgs = [
                ("str", month),
                ("str", merat.ToString()),
                ("str", percent),
                ("str", levels.ToString()),
                ("str", record.Spent.ToString()),
            ],
            Meret = merat,
        };
        if (constants.PaybackMailPeriodDay > 0) {
            mail.ExpiryTime = new DateTimeOffset(now).AddDays(constants.PaybackMailPeriodDay).ToUnixTimeSeconds();
        }
        return mail;
    }

    // The client reads 0x012D only with the feature on and event FinishPayback running; the table has no levels when
    // the ingest found the feature off.
    public static bool Shown(GameSession session, DateTime now) {
        PaybackTable table = Table(session);
        ConstantsTable constants = Constants(session);
        return EventRunning(session) && (PaybackRules.InPeriod(table, constants, now) || PaybackRules.InMonthPeriod(table, constants, now));
    }

    // The dialog's levels gained and merets spent this month.
    public static void SendStatus(GameSession session, DateTime now) {
        Account account = session.Player.Value.Account;
        PaybackRecord? current = PaybackRules.Current(account.Payback, Table(session), Constants(session), now, account.PrestigeLevel);
        int levels = current == null ? 0 : PaybackRules.LevelsGained(current, account.PrestigeLevel);
        session.Send(PaybackPacket.Status(levels, current?.Spent ?? 0));
    }

    // At field entry: an earlier month is paid, then the dialog gets its status and the payback products.
    public static void EnterField(GameSession session) {
        DateTime now = DateTime.Now;
        Settle(session, now);
        if (!Shown(session, now)) {
            return;
        }
        SendStatus(session, now);
        int productId = Constants(session).PaybackMarketProductSnList;
        PremiumMarketItem? product = productId != 0 ? session.GetPremiumMarketItem(productId) : null;
        session.Send(PaybackPacket.Products(product != null ? [product] : []));
    }

    // The monthly reset may run on another thread: the month is paid on the session's field.
    public static void MonthlyReset(GameSession session) {
        FieldManager? field = session.Field;
        if (field == null) {
            return;
        }
        field.Scheduler.Schedule(() => {
            if (session.Field == field && session.PlaysOn(field) && Settle(session, DateTime.Now) && Shown(session, DateTime.Now)) {
                SendStatus(session, DateTime.Now);
            }
        }, TimeSpan.Zero);
    }
}
