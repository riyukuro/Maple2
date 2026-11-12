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

    private readonly SortedList<long, Mail> inbox;

    private readonly ILogger logger = Log.ForContext<MailManager>();

    public MailManager(GameSession session) {
        this.session = session;

        inbox = new SortedList<long, Mail>();
        Fetch();
    }

    public void Notify(bool force = false) {
        if (Fetch(force) || force) {
            int unreadMail;
            int unreadAlarm;
            lock (inbox) {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                unreadMail = MailRules.UnreadCount(inbox.Values, now);
                unreadAlarm = MailRules.UnreadAlarmCount(inbox.Values, now);
            }
            session.Send(MailPacket.Notify(unreadMail, unreadMail > 0, unreadAlarm));
        }
    }

    public void Load() {
        lock (inbox) {
            session.Send(MailPacket.StartList());
            foreach (ImmutableList<Mail> batch in inbox.Values.Batch(BATCH_SIZE)) {
                session.Send(MailPacket.Load(batch));
            }
            session.Send(MailPacket.EndList());
        }
    }

    public bool Delete(long mailId) {
        if (!inbox.ContainsKey(mailId)) {
            return false;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.DeleteMail(mailId, session.CharacterId)) {
            return false;
        }

        inbox.Remove(mailId);
        session.Send(MailPacket.Deleted(mailId));
        return true;
    }

    public MailError Read(long mailId) {
        if (!inbox.TryGetValue(mailId, out Mail? inboxMail)) {
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

        inboxMail.Update(mail);
        return MailError.none;
    }

    public MailError Collect(long mailId) {
        if (!inbox.TryGetValue(mailId, out Mail? inboxMail)) {
            return MailError.mail_not_found;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        Mail? mail = db.GetMail(mailId, session.CharacterId);
        if (mail == null) {
            return MailError.mail_not_found;
        }

        lock (session.Item) {
            MailError error = CollectInternal(db, mail);
            inboxMail.Update(mail);
            // The list is sent from the inbox copy: what was given must not be listed again.
            inboxMail.Items.Clear();
            foreach (Item item in mail.Items) {
                inboxMail.Items.Add(item);
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
            }

            lastFetch = DateTime.Now;
            if (inbox.Count <= 0) {
                return false;
            }

            lastReceivedMail = inbox.Keys[inbox.Count - 1];
            return newMail;
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

    // Items on input mail will be cleared.
    private MailError CollectInternal(GameStorage.Request db, Mail mail) {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        MailError refused = MailRules.CollectError(mail, now);
        if (refused != MailError.none) {
            return refused;
        }

        // Nothing is given unless every item fits (existing stacks count) and can be added now.
        if (mail.Items.Count > 0 && !session.Item.Inventory.CanAdd(mail.Items)) {
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

        if (collectMeso || collectMeret || collectGameMeret) {
            Mail? updatedMail = db.UpdateMail(mail);
            if (updatedMail == null) {
                return MailError.s_mail_error;
            }
        }

        if (collectMeso) {
            session.Currency.Meso += mail.Meso;
            session.Send(MailPacket.CollectMeso(mail.Id, now));
        }
        if (collectMeret) {
            session.Currency.Meret += mail.Meret;
        }
        if (collectGameMeret) {
            session.Currency.GameMeret += mail.GameMeret;
        }

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
