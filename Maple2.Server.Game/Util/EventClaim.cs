using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Util;

// A once-only event claim whose gift is stored at once: the gift's rows (bag rows, or mails with their items) and the
// claim's marks (event user values) are written in one transaction (GameSession.TrySavePurchase), so a crash cannot
// keep the gift and lose the mark (the claim taken again after the restart) or the other way round. The marks reach
// memory once the transaction committed, under the account lock, so no session save in between writes the old ones.
// The items go to the bag when it takes all of them, else each by mail (as ItemManager.MailItem); a player between
// fields (no field) gets them by mail. Nothing is given or marked when the transaction is not stored.
public static class EventClaim {
    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(EventClaim));

    // One mark: the claim's value as it is to be stored, keyed by event and type.
    public readonly record struct Mark(int EventId, GameEventUserValueType Type, string Value);

    public static Mark Of(int eventId, GameEventUserValueType type, object value) => new(eventId, type, value.ToString() ?? "");

    // The stored items (bag or mail rows), or null when nothing was stored. Each mark's value must be held already
    // (a GameEventManager.Get of it): its expiration is kept. alsoSave stores a claim's marks kept elsewhere (account
    // fields) in the same transaction, false refusing it; afterCommit puts them in memory once it committed.
    public static List<Item>? Give(GameSession session, IReadOnlyList<Item> items, IReadOnlyList<Mark> marks,
            Func<GameStorage.Request, bool>? alsoSave = null, Action? afterCommit = null) {
        if (Values(session, marks) is not var (characterValues, accountValues)) {
            return null;
        }

        bool toBag = session.Field != null && (items.Count == 0 || session.Item.Inventory.TakesAll(items));
        var rows = new List<Item>();
        var mails = new List<Mail>();
        bool stored = session.TrySavePurchase(db => {
            rows.Clear();
            mails.Clear();
            foreach (Item item in items) {
                item.Slot = -1;
                if (toBag) {
                    Item? row = db.CreateItem(session.CharacterId, item);
                    if (row == null) {
                        return false;
                    }
                    rows.Add(row);
                    continue;
                }
                Mail? mail = db.CreateMail(new Mail {
                    Type = MailType.System,
                    ReceiverId = session.CharacterId,
                    Content = "50000000", // id from string/en/systemmailcontentna.xml, as ItemManager.MailItem
                });
                if (mail == null) {
                    return false;
                }
                Item? attached = db.CreateItem(mail.Id, item);
                if (attached == null) {
                    return false;
                }
                mail.Items.Add(attached);
                mails.Add(mail);
                rows.Add(attached);
            }
            if (!SaveValues(db, session, characterValues, accountValues)) {
                return false;
            }
            if (alsoSave != null && !alsoSave(db)) {
                return false;
            }
            db.AfterCommit(() => {
                // The bag rows first, then the marks: the client gets the items before the claim's values and the
                // reply, as when the items were added before the mark was set. A throw here is caught, so the marks
                // of the committed claim still reach memory (a throw out of the commit's actions answered "not
                // stored" for a claim that was).
                if (toBag) {
                    foreach (Item row in rows) {
                        // The room was checked above; a row the bag no longer takes moves to a mail (it is the
                        // character's stored bag row until then, so it is not lost either way: a load places it).
                        try {
                            if (!session.Item.AddOrMail(row, true, commit: true)) {
                                Logger.Error("Event claim: stored item {ItemId} row {Uid} went to neither bag nor mail of {CharacterId}", row.Id, row.Uid, session.CharacterId);
                            }
                        } catch (Exception ex) {
                            Logger.Error(ex, "Event claim: stored item {ItemId} row {Uid} not added for {CharacterId}; the row stays the character's", row.Id, row.Uid, session.CharacterId);
                        }
                    }
                }
                // The claim is stored: a throw from here on is logged and the claim still answers as given (a throw out
                // of the commit's actions answered "not stored" for a claim that was, and the handler sent no reply).
                // Each mark is set on its own, so one that throws does not keep the others from memory.
                foreach (Mark mark in marks) {
                    try {
                        session.GameEvent.Set(mark.EventId, mark.Type, mark.Value);
                    } catch (Exception ex) {
                        Logger.Error(ex, "Event claim: stored mark {Type} of event {EventId} not set in memory for {CharacterId}", mark.Type, mark.EventId, session.CharacterId);
                    }
                }
                try {
                    afterCommit?.Invoke();
                } catch (Exception ex) {
                    Logger.Error(ex, "Event claim of {Marks} stored for {CharacterId}, but its commit action threw", string.Join(",", marks.Select(mark => $"{mark.EventId}:{mark.Type}")), session.CharacterId);
                }
            });
            return true;
        });
        if (!stored) {
            Logger.Error("Event claim of {Marks} not stored for {CharacterId}, nothing given", string.Join(",", marks.Select(mark => $"{mark.EventId}:{mark.Type}")), session.CharacterId);
            return null;
        }

        foreach (Mail mail in mails) {
            try {
                session.World.MailNotification(new MailNotificationRequest {
                    CharacterId = session.CharacterId,
                    MailId = mail.Id,
                });
            } catch { /* ignored */ }
        }
        return rows;
    }

    // One mail of an event's own gift: the mail as it is to be made (its text, meso and merets) and its items.
    public readonly record struct Gift(Mail Mail, IReadOnlyList<Item> Items);

    // A claim whose gift is the event's own mails (a system mail with the row's text and items): each mail, its
    // items and the claim's marks are written in one transaction, as Give, so a crash cannot keep the mails and lose
    // the marks (sent again after the restart) or the other way round. True when stored: the marks reach memory and
    // the mails are noticed then; nothing is mailed or marked otherwise. Each mark's value must be held already.
    public static bool Mail(GameSession session, IReadOnlyList<Gift> gifts, IReadOnlyList<Mark> marks) {
        if (Values(session, marks) is not var (characterValues, accountValues)) {
            return false;
        }
        bool stored = session.TrySavePurchase(db => {
            foreach (Gift gift in gifts) {
                Mail? mail = db.CreateMail(gift.Mail);
                if (mail == null) {
                    return false;
                }
                foreach (Item item in gift.Items) {
                    item.Slot = -1;
                    if (db.CreateItem(mail.Id, item) == null) {
                        return false;
                    }
                }
            }
            if (!SaveValues(db, session, characterValues, accountValues)) {
                return false;
            }
            db.AfterCommit(() => {
                foreach (Mark mark in marks) {
                    session.GameEvent.Set(mark.EventId, mark.Type, mark.Value);
                }
            });
            return true;
        });
        if (!stored) {
            Logger.Error("Event mail claim of {Marks} not stored for {CharacterId}, nothing mailed", string.Join(",", marks.Select(mark => $"{mark.EventId}:{mark.Type}")), session.CharacterId);
            return false;
        }
        if (gifts.Count > 0) {
            session.Mail.Notify(true);
        }
        return true;
    }

    // The marks as the values to store, split by where they are kept; null (logged) when one is not held.
    private static (List<GameEventUserValue> Character, List<GameEventUserValue> Account)? Values(GameSession session, IReadOnlyList<Mark> marks) {
        var values = new List<GameEventUserValue>();
        foreach (Mark mark in marks) {
            GameEventUserValue? held = session.GameEvent.Held(mark.EventId, mark.Type);
            if (held == null) {
                Logger.Error("Event claim: value {Type} of event {EventId} not held for {CharacterId}, nothing given", mark.Type, mark.EventId, session.CharacterId);
                return null;
            }
            var value = new GameEventUserValue(mark.Type, held.ExpirationTime, mark.EventId);
            value.SetValue(mark.Value);
            values.Add(value);
        }
        return (values.Where(value => !GameEventManager.AccountValue(value.Type)).ToList(),
            values.Where(value => GameEventManager.AccountValue(value.Type)).ToList());
    }

    private static bool SaveValues(GameStorage.Request db, GameSession session, List<GameEventUserValue> characterValues, List<GameEventUserValue> accountValues) {
        return (characterValues.Count == 0 || db.SaveGameEventUserValues(session.CharacterId, characterValues))
               && (accountValues.Count == 0 || db.SaveAccountEventValues(session.AccountId, accountValues));
    }
}
