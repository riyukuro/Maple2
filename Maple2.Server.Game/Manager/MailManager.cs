using System.Collections.Immutable;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class MailManager {
    private const int BATCH_SIZE = 5;
    private const int FETCH_DELAY = 10;

    private readonly GameSession session;
    private DateTime lastFetch;
    private long lastReceivedMail;
    // A mail with a temporary item came in after the login's fetch and its notice is still to be sent.
    private bool temporaryArrived;
    private readonly bool loaded;

    // Under lock(inbox): the World's mail notifications (ChannelService.MailNotification, a gRPC thread) fetch into it
    // while the session's handlers read, delete and collect.
    private readonly SortedList<long, Mail> inbox;
    // Under lock(inbox): expired mails DropExpired took out of the inbox this session.
    private int droppedExpired;

    private readonly ILogger logger = Log.ForContext<MailManager>();

    public MailManager(GameSession session) {
        this.session = session;

        inbox = new SortedList<long, Mail>();
        Fetch();
        loaded = true;
    }

    public void Notify(bool force = false) {
        if (Fetch(force) || force) {
            SendNotice();
        }
    }

    // The unread counts, after the temporary item notice when a fetched mail brought one.
    private void SendNotice() {
        int unreadMail;
        int unreadAlarm;
        bool temporary;
        lock (inbox) {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            unreadMail = MailRules.UnreadCount(inbox.Values, now);
            unreadAlarm = MailRules.UnreadAlarmCount(inbox.Values, now);
            temporary = temporaryArrived;
            temporaryArrived = false;
        }
        // KMS2 sends the temporary item notice (Mail 15) right before the count of the mail that brought it.
        if (temporary) {
            session.Send(MailPacket.NotifyTemporary());
        }
        session.Send(MailPacket.Notify(unreadMail, unreadMail > 0, unreadAlarm));
    }

    // Pieces of each item on the inbox's mails, after one fetch so a mail just sent counts. A fetch that brought
    // mail sends its notice now (Notify), so a temporary item's notice is not held back to the next notify.
    public Dictionary<int, int> HeldAmounts(IEnumerable<int> itemIds) {
        var held = new Dictionary<int, int>();
        bool newMail;
        lock (inbox) {
            newMail = Fetch(force: true);
            foreach (int itemId in itemIds) {
                held[itemId] = inbox.Values.Sum(mail => mail.Items.Where(item => item.Id == itemId).Sum(item => item.Amount));
            }
        }
        if (newMail) {
            SendNotice();
        }
        return held;
    }

    public void Load() {
        lock (inbox) {
            DropExpired();
            session.Send(MailPacket.StartList());
            foreach (ImmutableList<Mail> batch in inbox.Values.Batch(BATCH_SIZE)) {
                session.Send(MailPacket.Load(batch));
            }
            session.Send(MailPacket.EndList());
        }
    }

    // The most mails the client's list can hold: the inbox and the expired mails dropped since login, which the
    // client keeps on its list (hidden) as no delete is sent for them.
    public int ListCount {
        get {
            lock (inbox) {
                return inbox.Count + droppedExpired;
            }
        }
    }

    // The inbox copy of a mail, or null.
    private Mail? Find(long mailId) {
        lock (inbox) {
            return inbox.GetValueOrDefault(mailId);
        }
    }

    public bool Delete(long mailId) {
        lock (inbox) {
            if (!inbox.TryGetValue(mailId, out Mail? inboxMail)) {
                return false;
            }
            // The client refuses to delete a mail while an uncollected item on it is locked (s_itemlock_mail_remove).
            if (MailRules.HasLockedItem(inboxMail)) {
                logger.Information("Mail {MailId} not deleted: an item on it is locked", mailId);
                return false;
            }
        }

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.DeleteMail(mailId, session.CharacterId)) {
            return false;
        }

        lock (inbox) {
            inbox.Remove(mailId);
        }
        session.Send(MailPacket.Deleted(mailId));
        return true;
    }

    public MailError Read(long mailId) {
        Mail? inboxMail = Find(mailId);
        if (inboxMail == null) {
            return MailError.mail_not_found;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        Mail? mail = db.GetMail(mailId, session.CharacterId);
        if (mail == null) {
            return MailError.mail_not_found;
        }

        MailError error = ReadInternal(db, mail);
        if (error != MailError.none) {
            return error;
        }

        lock (inbox) {
            inboxMail.Update(mail);
        }
        return MailError.none;
    }

    public MailError Collect(long mailId) {
        Mail? inboxMail = Find(mailId);
        if (inboxMail == null) {
            return MailError.mail_not_found;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        Mail? mail = db.GetMail(mailId, session.CharacterId);
        if (mail == null) {
            return MailError.mail_not_found;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        MailError error;
        lock (session.Item) {
            error = CollectCheck(db, mail, now);
        }
        // The currency is stored with the collect mark under the account lock, which is taken before the bag lock
        // (a purchase's after-commit work takes the bag lock under the account lock).
        if (error == MailError.none) {
            error = CollectCurrency(mail, now);
        }
        lock (session.Item) {
            if (error == MailError.none) {
                error = CollectItems(mail, now);
            }
            // Lock order: session.Item, then inbox (nothing takes inbox, then session.Item).
            lock (inbox) {
                inboxMail.Update(mail);
                // The list is sent from the inbox copy: what was given must not be listed again.
                inboxMail.Items.Clear();
                foreach (Item item in mail.Items) {
                    inboxMail.Items.Add(item);
                }
            }
            return error;
        }
    }

    private bool Fetch(bool force = false) {
        long accountId = session.AccountId;
        long characterId = session.CharacterId;

        lock (inbox) {
            if (!force && lastFetch.AddSeconds(FETCH_DELAY) > DateTime.Now) {
                return false;
            }

            bool newMail = false;
            using GameStorage.Request db = session.GameStorage.Context();
            db.BindAccountMailsToCharacter(accountId, characterId);
            foreach (Mail mail in db.GetAllMail(characterId, lastReceivedMail)) {
                newMail = true;
                inbox.Add(mail.Id, mail);
                if (loaded && MailRules.HasTemporaryItem(mail, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
                    temporaryArrived = true;
                }
            }

            DropExpired();
            lastFetch = DateTime.Now;
            if (inbox.Count <= 0) {
                return false;
            }

            lastReceivedMail = inbox.Keys[inbox.Count - 1];
            return newMail;
        }
    }

    // An expired mail is deleted with what is left on it (DeleteMail). The client hides a mail once it is expired
    // (CMailInfo::IsExpired) but still counts it in the list's pages of 14 (UIMailListDialog::RefreshMailList), so
    // kept expired mails left gaps in the pages, and nothing on them can be collected any more (error 21).
    // Under the inbox lock.
    private void DropExpired() {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        List<long> expired = inbox.Values.Where(mail => MailRules.IsExpired(mail, now)).Select(mail => mail.Id).ToList();
        if (expired.Count == 0) {
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        foreach (long mailId in expired) {
            if (!db.DeleteMail(mailId, session.CharacterId)) {
                logger.Warning("Expired mail {MailId} not deleted", mailId);
                continue;
            }
            inbox.Remove(mailId);
            droppedExpired++;
        }
    }

    #region Internal (No locks)
    // ReadTime on input mail will be modified.
    private MailError ReadInternal(GameStorage.Request db, Mail mail) {
        if (mail.ReadTime > 0) {
            return MailError.s_mail_error_alreadyread;
        }

        Mail? readMail = db.MarkMailRead(mail.Id, session.CharacterId);
        if (readMail == null) {
            return MailError.s_mail_error;
        }

        mail.ReadTime = readMail.ReadTime;
        session.Send(MailPacket.Read(mail));
        return MailError.none;
    }

    // The collect's refusals, checked under the bag lock before anything is given.
    private MailError CollectCheck(GameStorage.Request db, Mail mail, long now) {
        MailError refused = MailRules.CollectError(mail, now);
        if (refused != MailError.none) {
            return refused;
        }

        // Nothing is given unless every item fits (existing stacks count) and can be added now.
        // Furnishing goes into furnishing storage (InventoryManager.Add), which must take all of it; it needs no bag slot.
        List<Item> furnishing = mail.Items.Where(item => item.Type.IsFurnishing).ToList();
        List<Item> bag = mail.Items.Where(item => !item.Type.IsFurnishing).ToList();
        // TakesAll counts the slots each attachment needs (one per SlotMax); CanAdd counted one per attachment, so an
        // attachment of more than SlotMax passed with one slot open and the collect stopped partway.
        if (bag.Count > 0 && !session.Item.Inventory.TakesAll(bag)) {
            return MailError.s_mail_error_receiveitem_to_inven;
        }
        if (furnishing.Count > 0 && !session.Item.Furnishing.StorageTakes(furnishing.SelectMany(item =>
                Enumerable.Repeat(new UgcMapPackageRules.Stack(item.Id, item.Template?.Url, item.Metadata.Housing?.MaidId > 0, 1, item.Metadata.Property.SlotMax), Math.Max(1, item.Amount))))) {
            return MailError.s_mail_error_receiveitem_to_inven;
        }

        // Chosen here: collecting an unread mail marks it read first. The collected packet carries the read time
        // and the client takes a time of 0 as not collected.
        if (mail.ReadTime == 0) {
            MailError readError = ReadInternal(db, mail);
            if (readError != MailError.none) {
                return readError;
            }
        }

        return MailError.none;
    }

    // The mail's mesos and merets are stored as collected together with the balance that holds them, in one
    // transaction, and are taken back from the balance when that store fails. The collect mark used to be committed
    // at once and the currency held in memory until the next session save, so a crash in between lost it.
    private MailError CollectCurrency(Mail mail, long now) {
        (long Meso, long Meret, long GameMeret) read = (mail.MesoCollectTime, mail.MeretCollectTime, mail.GameMeretCollectTime);
        bool collectMeso = false;
        if (!mail.MesoCollected()) { // Allow mesos to be collected regardless and just overflow.
            mail.MesoCollectTime = now;
            collectMeso = true;
        }
        bool collectMeret = false;
        if (!mail.MeretCollected() && session.Currency.CanAddMeret(mail.Meret) == mail.Meret) {
            mail.MeretCollectTime = now;
            collectMeret = true;
        }
        bool collectGameMeret = false;
        if (!mail.GameMeretCollected() && session.Currency.CanAddGameMeret(mail.GameMeret) == mail.GameMeret) {
            mail.GameMeretCollectTime = now;
            collectGameMeret = true;
        }

        if (!collectMeso && !collectMeret && !collectGameMeret) {
            return MailError.none;
        }

        // Only one collect marks the mail collected (CollectMail): a second one that read the mail before the first
        // wrote it gives nothing. The currency is given only after the mark is written in the transaction.
        bool marked = false;
        long mesoAdded = 0;
        if (!session.TrySavePurchase(purchase => {
                if (!purchase.CollectMail(mail, read)) {
                    return false;
                }
                marked = true;
                if (collectMeso) {
                    long before = session.Currency.Meso;
                    session.Currency.ChangeMeso(mail.Meso);
                    mesoAdded = session.Currency.Meso - before;
                }
                if (collectMeret) {
                    session.Currency.ChangeMeret(mail.Meret);
                }
                if (collectGameMeret) {
                    session.Currency.ChangeGameMeret(mail.GameMeret);
                }
                return purchase.SaveCurrency(session.Player);
            })) {
            (mail.MesoCollectTime, mail.MeretCollectTime, mail.GameMeretCollectTime) = read;
            if (marked) {
                TakeBack(mesoAdded, collectMeret ? mail.Meret : 0, collectGameMeret ? mail.GameMeret : 0);
            }
            return MailError.s_mail_error;
        }

        if (collectMeso) {
            session.Send(MailPacket.CollectMeso(mail.Id, now));
        }
        return MailError.none;
    }

    // Takes back currency given in a collect whose store failed (nothing of it was stored).
    private void TakeBack(long meso, long meret, long gameMeret) {
        if (meso > 0) {
            session.Currency.TakeMesoUpTo(meso);
            session.Currency.NotifyMeso();
        }
        if (meret > 0 && !session.Currency.TryTakeMeret(meret, reportSpent: false)) {
            logger.Error("Mail collect: {Amount} merets of a failed store could not be taken back from {CharacterId}", meret, session.CharacterId);
        }
        if (gameMeret > 0 && !session.Currency.TryTakeGameMeret(gameMeret, reportSpent: false)) {
            logger.Error("Mail collect: {Amount} red merets of a failed store could not be taken back from {CharacterId}", gameMeret, session.CharacterId);
        }
    }

    // Items on input mail will be cleared.
    private MailError CollectItems(Mail mail, long now) {
        // The slot of an attachment is its place in the list (Mail.WriteTo).
        MailError error = MailError.none;
        for (int i = mail.Items.Count - 1; i >= 0; i--) {
            Item item = mail.Items[i];
            if (!session.Item.Inventory.Add(item, notifyNew: true, commit: true)) {
                logger.Error("Mail {MailId} was collected but items could not be added to inventory. Item: {Item}", mail.Id, item);
                error = MailError.s_mail_error_receiveitem_to_inven;
                break;
            }

            mail.Items.RemoveAt(i);
            session.Send(MailPacket.CollectItem(mail.Id, (byte) i, now));
        }

        // What was given is always reported; the closing packet only when nothing is left behind.
        if (error == MailError.none) {
            session.Send(MailPacket.CollectRead(mail));
        }
        return error;
    }
    #endregion
}
