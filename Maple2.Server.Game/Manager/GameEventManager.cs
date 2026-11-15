using System.Collections.Immutable;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;
using Serilog;
using Serilog.Events;

namespace Maple2.Server.Game.Manager;

public sealed class GameEventManager {
    private readonly GameSession session;
    private readonly Dictionary<int, Dictionary<GameEventUserValueType, GameEventUserValue>> eventValues;
    // The packet handlers run on the field thread, but a partner's values are written from the other player's
    // handler (a game of two) and Save runs on logout; every access of eventValues takes this lock.
    private readonly object valuesLock = new();
    private long nextUpdateTick;
    // Set when the level, exp or job may have changed (and at login), so the next second step looks at the user
    // condition rows again.
    private volatile bool userConditionDue = true;
    // The running UserCondition events the last step saw: an event that starts or ends while the player is online
    // sets userConditionDue as well, so rows already met are given at once.
    private int[] userConditionEvents = [];

    // The server's local time; each step reads it once, so that a step across midnight tests and stores its values
    // against one day (tests set it).
    internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    // A number in [0, max) for a chance (tests set it).
    internal Func<int, int> Roll { get; set; } = Random.Shared.Next;

    public MapleopolyTurn Mapleopoly { get; } = new();

    private readonly ILogger logger = Log.Logger.ForContext<GameEventManager>();


    public GameEventManager(GameSession session) {
        this.session = session;
        eventValues = new Dictionary<int, Dictionary<GameEventUserValueType, GameEventUserValue>>();
        GetUserValues();
    }

    public void Update(long tickCount) {
        if (nextUpdateTick > tickCount) {
            return;
        }
        nextUpdateTick = tickCount + 1000;

        UpdateDtReward();
        UpdatePlayGift();
        UpdateDailyLoginReward();
        UpdateUserCondition();
        // Chosen here: the SAHotTime bonus follows its events within this one second step.
        session.Stats.RefreshHotTime();
    }

    private void GetUserValues() {
        IList<GameEvent> events = session.Events.ToList();
        long now = DateTime.Now.ToEpochSeconds();

        using GameStorage.Request db = session.GameStorage.Context();
        foreach (GameEventUserValue userValue in db.GetEventUserValues(session.CharacterId)) {
            // An account's value (below, a stamp card's claims) stored for the character is not read.
            if (AccountValue(userValue.Type)) {
                db.RemoveGameEventUserValue(userValue, session.CharacterId);
                continue;
            }
            GameEventUserValue? loaded = LoadValue(userValue, events, now);
            if (loaded != null) {
                Add(loaded);
            } else if (Outdated(userValue, events, now)) {
                db.RemoveGameEventUserValue(userValue, session.CharacterId);
            }
        }

        // A stamp card's quests are the account's, so its claims are too: they are stored for the account, whatever
        // the character that claimed, and stay when that character is deleted.
        foreach (GameEventUserValue userValue in db.GetAccountEventValues(session.AccountId)) {
            GameEventUserValue? loaded = LoadValue(userValue, events, now);
            if (loaded != null) {
                Add(loaded);
            } else if (Outdated(userValue, events, now)) {
                db.RemoveAccountEventValue(userValue, session.AccountId);
            }
        }

        void Add(GameEventUserValue loaded) {
            if (!eventValues.TryGetValue(loaded.EventId, out Dictionary<GameEventUserValueType, GameEventUserValue>? valueDict)) {
                eventValues.Add(loaded.EventId, new Dictionary<GameEventUserValueType, GameEventUserValue> {
                    { loaded.Type, loaded },
                });
            } else {
                valueDict.TryAdd(loaded.Type, loaded);
            }
        }
    }

    // A stored value as it is loaded: null (not read) when its event is not live or it has run out. A value that
    // lasts as long as its event is first moved to the event's end (FollowEventEnd), then tested: tested at its
    // stored end, a value of an event whose end was moved while it was live would be dropped after the old end.
    // A mentoring return (MentoringReturnUser) is read until its own end also after its event: a ReturnUserCandidate
    // row keeps a return for the next season, which starts after the row ends (MentoringManager.LoadCandidate). It
    // is also kept to the end of a running season of its number (EventValueRules.FollowSeason).
    // A character value of no event (id 0: the new-user and returning-user marks, UserMarks) is read until its own
    // end. An account value of no event is not: the registration stored before mentoring had seasons
    // (MentoringRegistration, event id 0) is moved or dropped at login (MentoringManager), and a loaded one would be
    // stored again at logout (Save) and taken into the next season.
    public static GameEventUserValue? LoadValue(GameEventUserValue userValue, IEnumerable<GameEvent> events, long now) {
        GameEvent? gameEvent = userValue.EventId == 0 ? null : events.FirstOrDefault(gameEvent => gameEvent.Id == userValue.EventId);
        if (gameEvent == null) {
            GameEventUserValue kept = EventValueRules.FollowSeason(userValue, events, now);
            return (kept.Type == GameEventUserValueType.MentoringReturnUser || (kept.EventId == 0 && !AccountValue(kept.Type))) && kept.ExpirationTime > now ? kept : null;
        }
        GameEventUserValue loaded = EventValueRules.FollowSeason(FollowEventEnd(userValue, gameEvent), events, now);
        return loaded.ExpirationTime < now ? null : loaded;
    }

    // A stored value that is not loaded is deleted only once it has run out. While its event is absent (taken out
    // of the event list, or a replay switched off) before the value's end, the value stays stored, unread: an event
    // that comes back before that end is the same run and finds its claims again. After that end the world server
    // starts a new run (GameStorage BeginEventRun).
    public static bool Outdated(GameEventUserValue userValue, IEnumerable<GameEvent> events, long now) {
        GameEvent? gameEvent = events.FirstOrDefault(gameEvent => gameEvent.Id == userValue.EventId);
        GameEventUserValue followed = gameEvent == null ? userValue : FollowEventEnd(userValue, gameEvent);
        return EventValueRules.FollowSeason(followed, events, now).ExpirationTime < now;
    }

    // The values stored for the account instead of the character.
    // Chosen here: an item collection's claim is the account's as well, so that one set of condition items, which
    // stay with the player, is not claimed by every character of the account in turn.
    // So are the daily login mail's mark (UpdateDailyLoginReward) and the item box's day (EventRewardHandler).
    // So is the returner gift's time (ReturnUserGift): one gift per account in the row's wait, whichever character
    // enters.
    // So are the snowman's values: the KR rows of the latest snowman event (900070) give snowflakes for the account
    // (promotionContents isAccount 1).
    // So are the burning levelup values (2000-2004): the character list picks one character of the account
    // (UICharacterListDialog::ShowBurningLevelupPopup, uicharacterlistdialog.cpp:2041, and the login stores the
    // pick before any character enters), and the picked character's reach and reward claim go with that pick.
    // So is the mentoring registration: one mentor or returning character per account for the season.
    public static bool AccountValue(GameEventUserValueType type) => StampValueTypes.Contains(type)
        || BurningLevelupRules.ValueTypes.Contains(type)
        || type is GameEventUserValueType.CollectItemGroup or GameEventUserValueType.DailyLoginRewardGiven
            or GameEventUserValueType.ReturnUserGiftTime
            or GameEventUserValueType.ItemBoxReceivedDay
            or GameEventUserValueType.SnowflakeCount or GameEventUserValueType.DailyCompleteCount
            or GameEventUserValueType.AccumCompleteCount or GameEventUserValueType.AccumCompleteRewardReceived
        || type is GameEventUserValueType.MentoringRegistration;

    private static GameEventUserValueType[] StampValueTypes => EventValueRules.StampValueTypes;

    // EventValueRules.FollowEventEnd (the login reads the burning levelup values the same way).
    public static GameEventUserValue FollowEventEnd(GameEventUserValue value, GameEvent gameEvent) => EventValueRules.FollowEventEnd(value, gameEvent);

    // The play-time reward's step after `elapsed` more seconds: the row's seconds and the total both grow by them,
    // and the row's reward is due once its seconds reach the row's end.
    public static (long Current, long Total, bool Due) DtRewardStep(long current, long total, long elapsed, int endDuration) {
        return (current + elapsed, total + elapsed, current + elapsed >= endDuration);
    }

    // Chosen here: the play-time values last until the last second of the server's local day, the end most KMS2
    // values carry (a few carry a later day's last second).
    public static long DtRewardExpiry(DateTime now) => now.Date.AddDays(1).AddSeconds(-1).ToEpochSeconds();

    // A claim of the one second step (DTReward, PlayGift, DailyLoginReward, UserCondition) whose store failed is not
    // tried again on the next step but after a wait: ClaimRetryFirst seconds, doubled after each failure in a row up
    // to ClaimRetryMax, so a store that keeps failing (the database down) costs one transaction per wait, not one a
    // second per due event. A stored claim clears its wait; a new session starts with none. Only the first failure
    // in a row is logged as an error, the ones after it at debug.
    public const int ClaimRetryFirst = 30;
    public const int ClaimRetryMax = 600;
    private readonly Dictionary<(int EventId, GameEventType Type), (long NextTry, int Wait)> claimRetries = new();

    private bool ClaimWaiting(int eventId, GameEventType type, long now) {
        return claimRetries.TryGetValue((eventId, type), out (long NextTry, int Wait) retry) && now < retry.NextTry;
    }

    // The failure's log level: Error for the first failure in a row, Debug after it.
    private LogEventLevel ClaimFailed(int eventId, GameEventType type, long now) {
        bool first = !claimRetries.TryGetValue((eventId, type), out (long NextTry, int Wait) retry);
        int wait = first ? ClaimRetryFirst : Math.Min(retry.Wait * 2, ClaimRetryMax);
        claimRetries[(eventId, type)] = (now + wait, wait);
        return first ? LogEventLevel.Error : LogEventLevel.Debug;
    }

    private void ClaimStored(int eventId, GameEventType type) => claimRetries.Remove((eventId, type));

    // The second of the last play-time step of this session.
    private long dtRewardStep;

    // The play-time reward (DTReward), once a second as KMS2 sends it, per event: the time now (700), the total
    // seconds (703), then the row's seconds (701); the seconds grow by the time since the last step. On the step
    // that reaches the row's end the next row's index (702) and the restarted seconds go in place of the row's
    // seconds, and the mail notice follows. The client counts between the steps itself: the row's seconds plus the
    // time since 700 (UIDTEventDialog, uidteventdialog.cpp).
    private void UpdateDtReward() {
        if (session.Field is null) return;

        IList<GameEvent> events = session.FindEvent(GameEventType.DTReward);
        if (events.Count == 0) {
            return;
        }

        DateTime now = Clock();
        long nowSeconds = now.ToEpochSeconds();
        // Chosen here: the first step of a session counts one second; the time away is not play time.
        long elapsed = dtRewardStep == 0 ? 1 : nowSeconds - dtRewardStep;
        if (elapsed <= 0) {
            return;
        }
        dtRewardStep = nowSeconds;
        long expiry = DtRewardExpiry(now);

        foreach (GameEvent gameEvent in events) {
            if (gameEvent.Metadata.Data is not DTReward dtReward) {
                continue;
            }

            GameEventUserValue rewardIndexValue = Get(GameEventUserValueType.DTRewardRewardIndex, gameEvent.Id, expiry, nowSeconds);

            if (dtReward.Entries.Length <= rewardIndexValue.Int()) {
                continue;
            }

            DTReward.Entry entry = dtReward.Entries[rewardIndexValue.Int()];
            long current = Get(GameEventUserValueType.DTRewardCurrentTime, gameEvent.Id, expiry, nowSeconds).Long();
            long total = Get(GameEventUserValueType.DTRewardTotalTime, gameEvent.Id, expiry, nowSeconds).Long();
            (current, total, bool due) = DtRewardStep(current, total, elapsed, entry.EndDuration);

            Get(GameEventUserValueType.DTRewardStartTime, gameEvent.Id, expiry, nowSeconds);
            Set(gameEvent.Id, GameEventUserValueType.DTRewardStartTime, nowSeconds);
            Set(gameEvent.Id, GameEventUserValueType.DTRewardTotalTime, total);
            if (!due) {
                Set(gameEvent.Id, GameEventUserValueType.DTRewardCurrentTime, current);
                continue;
            }
            // A row whose mail was not stored waits (ClaimWaiting) with its seconds held at the row's end, as set on
            // the failure, and not sent again: the dialog shows the row as full, not counting past its end.
            if (ClaimWaiting(gameEvent.Id, GameEventType.DTReward, nowSeconds)) {
                continue;
            }

            // Give reward
            Item? item = session.Field?.ItemDrop.CreateItem(entry.Item.ItemId, entry.Item.Rarity, entry.Item.Amount);
            if (item == null) {
                Set(gameEvent.Id, GameEventUserValueType.DTRewardCurrentTime, current);
                continue;
            }

            // The mail with its item, the next row's index and the restarted seconds are stored in one transaction
            // (EventClaim.Mail): a stop can neither give this row's mail again nor lose it. When it is not stored the
            // row stays due and is tried again after a wait (ClaimFailed).
            var mail = new Mail {
                Type = MailType.System,
                ReceiverId = session.CharacterId,
                SenderId = session.CharacterId,
                Content = entry.MailContentId.ToString(),
            };
            if (!EventClaim.Mail(session, [new EventClaim.Gift(mail, [item])], [
                    EventClaim.Of(gameEvent.Id, GameEventUserValueType.DTRewardRewardIndex, rewardIndexValue.Int() + 1),
                    EventClaim.Of(gameEvent.Id, GameEventUserValueType.DTRewardCurrentTime, entry.StartDuration),
                ])) {
                logger.Write(ClaimFailed(gameEvent.Id, GameEventType.DTReward, nowSeconds),
                    "DTReward mail not stored. Event ID: {EventId}, Reward Index: {RewardIndex}", gameEvent.Id, rewardIndexValue.Int());
                Set(gameEvent.Id, GameEventUserValueType.DTRewardCurrentTime, Math.Min(current, (long) entry.EndDuration));
                continue;
            }
            ClaimStored(gameEvent.Id, GameEventType.DTReward);
        }
    }

    // The play-time gift's step after `elapsed` more seconds: the gift at `index` is due once the seconds since the
    // last gift reach its requireMins. Past the last gift nothing is due and the seconds stay.
    public static (long Seconds, bool Due) PlayGiftStep(PlayGift.Gift[] gifts, int index, long seconds, long elapsed) {
        if (index < 0 || index >= gifts.Length) {
            return (seconds, false);
        }
        seconds += elapsed;
        return (seconds, seconds >= gifts[index].RequireMinutes * 60L);
    }

    // The second of the last play-time gift step of this session.
    private long playGiftStep;

    // The play-time gifts (PlayGift). The client has no part in this event (no GameEventPlayGift class; its table is
    // read only by the server), so the steps follow the play-time reward's: once a second, per event, the seconds
    // since the last gift grow by the time since the last step, and the gift whose requireMins they reach is mailed
    // as a system mail with the gift's systemMailID and item; the seconds start again from 0. Chosen here: the gifts
    // and seconds are per character and per server-local day, as the play-time reward's (DtRewardExpiry), and the
    // first step of a session counts one second. The mail, the next index and the restarted seconds are stored in
    // one transaction (EventClaim.Mail); a gift not stored stays due and is tried again after a wait (ClaimFailed).
    private void UpdatePlayGift() {
        if (session.Field is null) return;

        IList<GameEvent> events = session.FindEvent(GameEventType.PlayGift);
        if (events.Count == 0) {
            return;
        }

        DateTime now = Clock();
        long nowSeconds = now.ToEpochSeconds();
        long elapsed = playGiftStep == 0 ? 1 : nowSeconds - playGiftStep;
        if (elapsed <= 0) {
            return;
        }
        playGiftStep = nowSeconds;
        long expiry = DtRewardExpiry(now);

        foreach (GameEvent gameEvent in events) {
            if (gameEvent.Metadata.Data is not PlayGift playGift) {
                continue;
            }

            int index = Get(GameEventUserValueType.PlayGiftIndex, gameEvent.Id, expiry, nowSeconds).Int();
            if (index >= playGift.Gifts.Length) {
                continue;
            }
            long seconds = Get(GameEventUserValueType.PlayGiftSeconds, gameEvent.Id, expiry, nowSeconds).Long();
            (seconds, bool due) = PlayGiftStep(playGift.Gifts, index, seconds, elapsed);
            if (!due) {
                Set(gameEvent.Id, GameEventUserValueType.PlayGiftSeconds, seconds);
                continue;
            }

            if (ClaimWaiting(gameEvent.Id, GameEventType.PlayGift, nowSeconds)) {
                Set(gameEvent.Id, GameEventUserValueType.PlayGiftSeconds, seconds);
                continue;
            }
            PlayGift.Gift gift = playGift.Gifts[index];
            Item? item = session.Field?.ItemDrop.CreateItem(gift.Item.ItemId, gift.Item.Rarity, gift.Item.Amount);
            if (item == null) {
                Set(gameEvent.Id, GameEventUserValueType.PlayGiftSeconds, seconds);
                continue;
            }

            var mail = new Mail {
                Type = MailType.System,
                ReceiverId = session.CharacterId,
                SenderId = session.CharacterId,
                Content = gift.MailContentId.ToString(),
            };
            if (!EventClaim.Mail(session, [new EventClaim.Gift(mail, [item])], [
                    EventClaim.Of(gameEvent.Id, GameEventUserValueType.PlayGiftIndex, index + 1),
                    EventClaim.Of(gameEvent.Id, GameEventUserValueType.PlayGiftSeconds, 0L),
                ])) {
                logger.Write(ClaimFailed(gameEvent.Id, GameEventType.PlayGift, nowSeconds),
                    "PlayGift mail not stored. Event ID: {EventId}, Gift: {GiftId}", gameEvent.Id, gift.GiftId);
                Set(gameEvent.Id, GameEventUserValueType.PlayGiftSeconds, seconds);
                continue;
            }
            ClaimStored(gameEvent.Id, GameEventType.PlayGift);
        }
    }

    // The daily login mail (DailyLoginReward): once a day while the event runs, the row's items in one system mail
    // with the row's mail text, for a character of at least the row's level (value3). Chosen here: once per account
    // (the client has no part in this event, so nothing tells), so that a new character of the same account does
    // not get the day's mail again; on the first second step in a field of the day (the server's local day, as the
    // play-time reward's), so a player online at midnight gets the next day's mail too. The mail and the day's mark
    // are stored in one transaction (EventClaim.Mail); a mail not stored is tried again after a wait (ClaimFailed).
    private void UpdateDailyLoginReward() {
        if (session.Field is null) return;

        IList<GameEvent> events = session.FindEvent(GameEventType.DailyLoginReward);
        if (events.Count == 0) {
            return;
        }

        DateTime now = Clock();
        long nowSeconds = now.ToEpochSeconds();
        long expiry = DtRewardExpiry(now);
        short level = session.Player.Value.Character.Level;
        foreach (GameEvent gameEvent in events) {
            if (gameEvent.Metadata.Data is not DailyLoginReward reward || reward.Items.Length == 0 || level < reward.MinLevel) {
                continue;
            }
            if (Get(GameEventUserValueType.DailyLoginRewardGiven, gameEvent.Id, expiry, nowSeconds).Int() != 0
                || ClaimWaiting(gameEvent.Id, GameEventType.DailyLoginReward, nowSeconds)) {
                continue;
            }
            var items = new List<Item>();
            foreach (RewardItem rewardItem in reward.Items) {
                Item? item = session.Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
                if (item != null) {
                    items.Add(item);
                }
            }
            // Rows none of which can be made still mark the day (as before), with no mail.
            EventClaim.Gift[] gifts = items.Count == 0 ? [] : [new EventClaim.Gift(new Mail {
                Type = MailType.System,
                ReceiverId = session.CharacterId,
                SenderId = session.CharacterId,
                Content = reward.MailId > 0 ? reward.MailId.ToString() : string.Empty,
            }, items)];
            if (!EventClaim.Mail(session, gifts, [EventClaim.Of(gameEvent.Id, GameEventUserValueType.DailyLoginRewardGiven, 1)])) {
                logger.Write(ClaimFailed(gameEvent.Id, GameEventType.DailyLoginReward, nowSeconds),
                    "Daily login mail of event {EventId} not stored for {CharacterId}", gameEvent.Id, session.CharacterId);
                continue;
            }
            ClaimStored(gameEvent.Id, GameEventType.DailyLoginReward);
        }
    }

    // The level, exp or job may have changed: the user condition rows are looked at on the next second step.
    public void UserConditionChanged(ConditionType type) {
        if (type is ConditionType.level or ConditionType.exp or ConditionType.job) {
            userConditionDue = true;
        }
    }

    // The user condition event (UserCondition): each row of a running event's group (value1) the character meets
    // and has not been given (UserConditionRules) is given now: its mail, a system mail with the row's mail text
    // (context) and the attachments for every job and the character's (as the notice dialog shows them), and its
    // notice: UserConditionEvent 1 with the last such row, whose preview row the client keeps as the one noticed.
    // The mails, the given rows and the notice are stored in one transaction (EventClaim.Mail): a stop can neither
    // send a mail twice nor lose it; rows not stored are tried again after a wait (ClaimFailed). Chosen here: per character (each character's levels are its own), on the second step after a
    // level, exp or job change, at login and when the running events change, so rows already met when an event
    // starts are given too; one notice per step (the client shows one dialog).
    private void UpdateUserCondition() {
        if (session.Field is null) return;
        IList<GameEvent> events = session.FindEvent(GameEventType.UserCondition);
        int[] running = events.Select(found => found.Id).Order().ToArray();
        if (!running.SequenceEqual(userConditionEvents)) {
            userConditionEvents = running;
            userConditionDue = true;
        }
        if (!userConditionDue) return;
        userConditionDue = false;

        if (events.Count == 0) {
            return;
        }
        IReadOnlyDictionary<int, UserConditionEventMetadata> table = session.TableMetadata.UserConditionEventTable.Entries;
        if (table.Count == 0) {
            return;
        }

        Character character = session.Player.Value.Character;
        long nextExp = session.TableMetadata.ExpTable.NextExp.GetValueOrDefault(character.Level);
        long expRate = UserConditionRules.ExpRate(character.Exp, nextExp);
        int jobCode = (int) character.Job.Code();
        long now = Clock().ToEpochSeconds();
        foreach (GameEvent gameEvent in events.OrderBy(found => found.Id)) {
            if (gameEvent.Metadata.Data is not UserCondition { Group: > 0 } userCondition) {
                continue;
            }
            if (ClaimWaiting(gameEvent.Id, GameEventType.UserCondition, now)) {
                userConditionDue = true; // looked at again once the wait is over
                continue;
            }
            HashSet<int> done = UserConditionRules.Ids(Get(GameEventUserValueType.UserConditionDone, gameEvent.Id, gameEvent.EndTime, now).Value);
            List<UserConditionEventMetadata> due = UserConditionRules.Due(table.Values, userCondition.Group, done, character.Level, expRate, jobCode);
            if (due.Count == 0) {
                continue;
            }
            done.UnionWith(due.Select(row => row.Id));
            var marks = new List<EventClaim.Mark> {
                EventClaim.Of(gameEvent.Id, GameEventUserValueType.UserConditionDone, UserConditionRules.Join(done)),
            };
            UserConditionEventMetadata? notice = due.LastOrDefault(row => row.Notify != null);
            if (notice != null) {
                Get(GameEventUserValueType.UserConditionNotice, gameEvent.Id, gameEvent.EndTime, now);
                marks.Add(EventClaim.Of(gameEvent.Id, GameEventUserValueType.UserConditionNotice, notice.Id));
            }
            var gifts = new List<EventClaim.Gift>();
            foreach (UserConditionEventMetadata row in due) {
                if (row.Mail != null && UserConditionGift(row.Mail, jobCode) is { } gift) {
                    gifts.Add(gift);
                }
            }
            if (!EventClaim.Mail(session, gifts, marks)) {
                logger.Write(ClaimFailed(gameEvent.Id, GameEventType.UserCondition, now),
                    "User condition rows of event {EventId} not stored for {CharacterId}", gameEvent.Id, session.CharacterId);
                userConditionDue = true;
                continue;
            }
            ClaimStored(gameEvent.Id, GameEventType.UserCondition);
            if (notice != null) {
                session.Send(UserConditionEventPacket.Notice(notice.Id));
            }
        }
    }

    // A row's mail as it is to be made: the attachments for every job and the character's; null when it carries
    // nothing (no item made, no meso, no merets).
    private EventClaim.Gift? UserConditionGift(UserConditionEventMetadata.MailData mailData, int jobCode) {
        var items = new List<Item>();
        foreach (UserConditionEventMetadata.Attachment attachment in UserConditionRules.ForJob(mailData.Attachments, jobCode)) {
            Item? item = session.Field?.ItemDrop.CreateItem(attachment.ItemId, attachment.Rarity, attachment.Amount);
            if (item != null) {
                items.Add(item);
            }
        }
        if (items.Count == 0 && mailData.Meso <= 0 && mailData.Merat <= 0) {
            return null;
        }
        return new EventClaim.Gift(new Mail {
            Type = MailType.System,
            ReceiverId = session.CharacterId,
            SenderId = session.CharacterId,
            Content = mailData.Context > 0 ? mailData.Context.ToString() : string.Empty,
            Meso = Math.Max(0, mailData.Meso),
            Meret = Math.Max(0, mailData.Merat),
        }, items);
    }

    // The preview row of the last row noticed, sent at login (UserConditionEvent 0): the row the client keeps as the
    // one noticed. 0 when none was, or its row is gone. With several events, the highest id's.
    public int UserConditionPreview() {
        IReadOnlyDictionary<int, UserConditionEventMetadata> table = session.TableMetadata.UserConditionEventTable.Entries;
        foreach (GameEvent gameEvent in session.FindEvent(GameEventType.UserCondition).OrderByDescending(found => found.Id)) {
            if (int.TryParse(Peek(GameEventUserValueType.UserConditionNotice, gameEvent.Id), out int id)
                && table.TryGetValue(id, out UserConditionEventMetadata? row)) {
                return row.Notify?.Preview ?? 0;
            }
        }
        return 0;
    }

    // One attend point a day for each live long-term attendance event (chosen here: the client does not say how
    // points are earned). As in KMS2: the points, then the time of the grant, then EventReward 27. Called on the
    // client's EventReward 27 (sent when the event loads) and at the daily reset for players online.
    public void GrantLongTermAttendPoints() {
        foreach (GameEvent gameEvent in session.FindEvent(GameEventType.LongTermAttendGift)) {
            if (gameEvent.Metadata.Data is not LongTermAttendGift) {
                continue;
            }
            DateTime now = DateTime.Now;
            DateTime? lastGrant = long.TryParse(Peek(GameEventUserValueType.LongTermAttendPointTime, gameEvent.Id), out long seconds)
                ? seconds.FromEpochSeconds() : null;
            if (!LongTermAttendRules.PointDue(lastGrant, now)) {
                continue;
            }

            GameEventUserValue points = Get(GameEventUserValueType.LongTermAttendPoints, gameEvent.Id, gameEvent.EndTime);
            Set(gameEvent.Id, GameEventUserValueType.LongTermAttendPoints, points.Int() + 1);
            Get(GameEventUserValueType.LongTermAttendPointTime, gameEvent.Id, gameEvent.EndTime);
            Set(gameEvent.Id, GameEventUserValueType.LongTermAttendPointTime, now.ToEpochSeconds());
            session.Send(EventRewardPacket.LongTermAttendPoint());
        }
    }

    // When the play time of this login began: the time sent in TimeSync 3, from which the client counts the
    // attendance gift's play seconds. Moved to the reset time at the daily reset.
    public DateTime PlayStart { get; private set; } = DateTime.Now;

    public void StartPlayTime(DateTime now) {
        PlayStart = now;
    }

    // The daily reset: the new day's play time starts at now, and the old day's stored seconds (106) are replaced
    // by 0 until the next midnight, sent to the client so that both count the new day from nothing.
    public void ResetPlayTime(DateTime now) {
        PlayStart = now;
        var updates = new List<GameEventUserValue>();
        lock (valuesLock) {
            foreach ((int eventId, Dictionary<GameEventUserValueType, GameEventUserValue> values) in eventValues) {
                if (!values.ContainsKey(GameEventUserValueType.AttendanceAccumulatedTime)) {
                    continue;
                }
                var value = new GameEventUserValue(GameEventUserValueType.AttendanceAccumulatedTime, now.Date.AddDays(1).ToEpochSeconds(), eventId);
                value.SetValue("0");
                values[GameEventUserValueType.AttendanceAccumulatedTime] = value;
                updates.Add(value);
            }
        }
        foreach (GameEventUserValue value in updates) {
            session.Send(GameEventUserValuePacket.Update(value));
        }
    }

    // The seconds played today for an attendance gift: the day's stored seconds (106) plus this login's.
    public long AttendancePlaySeconds(int eventId, DateTime now) {
        long.TryParse(Peek(GameEventUserValueType.AttendanceAccumulatedTime, eventId), out long stored);
        return AttendanceRules.PlaySeconds(stored, PlayStart, now);
    }

    // The attendance events the player has started with an item (user value 104), with the time each runs out,
    // for Attendance 6.
    public List<(int EventId, long EndTime)> ItemStartedEvents() {
        long now = DateTime.Now.ToEpochSeconds();
        lock (valuesLock) {
            return eventValues.Values.SelectMany(dict => dict.Values)
                .Where(value => value.Type == GameEventUserValueType.AttendanceItemStartEnd && value.ExpirationTime > now)
                .Select(value => (value.EventId, value.ExpirationTime))
                .ToList();
        }
    }

    // Snowflakes (GameEventSnowman): each source of a running snowman event whose condition this is gives its points
    // with its chance; the snowmen that can proceed take them (SnowmanRules.Gain) and the client is told
    // (TaskEvent 4). The snowflakes and today's completes last the day ("reset daily at midnight", the dialog's
    // help), so the dialog, which reads them again at the day's change, finds them gone.
    // Chosen here: the sources' daily limits are not kept (the KR rows of 900070 allow 99999 a day). With two
    // snowman events running, only the one the claims use (SnowmanEvent) gathers snowflakes.
    public void SnowmanCondition(ConditionType type) {
        if (SnowmanEvent() is { Metadata.Data: Snowman snowman } gameEvent) {
            int point = 0;
            foreach (Snowman.Source source in snowman.Sources) {
                if (source.Type == type && Roll(10000) < source.Prop) {
                    point += source.Point;
                }
            }
            if (point == 0) {
                return;
            }

            DateTime time = Clock();
            long now = time.ToEpochSeconds();
            long dayEnd = DtRewardExpiry(time);
            int length = snowman.Snowmen.Length;
            int[] snowflakes = SnowmanRules.Counts(Get(GameEventUserValueType.SnowflakeCount, gameEvent.Id, dayEnd, now).Value, length);
            int[] daily = SnowmanRules.Counts(Get(GameEventUserValueType.DailyCompleteCount, gameEvent.Id, dayEnd, now).Value, length);
            if (!SnowmanRules.Gain(snowman, snowflakes, daily, point)) {
                return;
            }
            Set(gameEvent.Id, GameEventUserValueType.SnowflakeCount, SnowmanRules.Join(snowflakes));
            session.Send(TaskEventPacket.Snowflake());
        }
    }

    // The promotionContents rows (PromotionTable): each running row of this condition whose code and target the
    // condition meets (ConditionUtil.Check, as a quest or trophy condition) gives its individual drop box with its
    // chance, to a player of its level, while its counter (the account's or the character's) is below its limit for
    // the period. The counter is taken and the box's items stored (to the bag, else by mail) in one transaction
    // (ClaimPromotion), and the reward notifier lists them (TaskEvent with the row's type).
    // Chosen here: one box per counted action, whatever its count (a gathering of several counts once), and the row's
    // limit is taken also when the box's roll gives no item.
    public void PromotionCondition(ConditionType type, string targetString, long targetLong, string codeString, long codeLong) {
        if (session.Field is null) return;
        PromotionTable table = session.ServerTableMetadata.PromotionTable;
        if (!table.ByType.Contains(type)) {
            return;
        }
        DateTime now = Clock();
        int level = session.Player.Value.Character.Level;
        foreach (PromotionTable.Entry entry in table.ByType[type]) {
            if (!PromotionRules.Runs(entry, now) || level < entry.RequireLevel
                || !entry.Condition.Check(session, targetString, targetLong, codeString, codeLong)
                || Roll(10000) >= entry.Prop) {
                continue;
            }
            ICollection<Item> items = session.Field.ItemDrop.GetIndividualDropItems(session, level, entry.IndividualDropBoxId);
            if (entry.LimitType != PromotionLimit.none) {
                // After a store this condition may be counted in: its account lock is let go first.
                PromotionTable.Entry limited = entry;
                session.AfterStore(() => ClaimPromotion(limited, items, now));
                continue;
            }
            foreach (Item item in items) {
                if (!session.Item.Inventory.Add(item, true)) {
                    session.Item.MailItem(item);
                }
            }
            if (items.Count > 0) {
                session.Send(TaskEventPacket.Reward(entry.Kind, items));
            }
        }
    }

    // A limited row's drop: the count and the items (bag rows, or mails when the bag does not take them) are stored
    // in one transaction (EventClaim.Give). The count was written on its own before the items' rows, so an item
    // write that failed (or a stop between the two) spent the count with nothing given. A reached limit gives
    // nothing; a failed store gives and counts nothing (logged), and the counted action goes on.
    private void ClaimPromotion(PromotionTable.Entry entry, ICollection<Item> items, DateTime now) {
        long characterId = entry.IsAccount ? 0 : session.CharacterId;
        // A counter that is full is read first: a reached limit costs no store (account lock, transaction, item rows
        // written and rolled back) for each counted action.
        try {
            using GameStorage.Request read = session.GameStorage.Context();
            if (read.PromotionCountFull(session.AccountId, characterId, entry.ShareId, entry.LimitCount, now.ToEpochSeconds())) {
                return;
            }
        } catch (Exception ex) {
            logger.Error(ex, "Promotion {Id} limit of {CharacterId} not read", entry.Id, session.CharacterId);
            return;
        }
        bool full = false;
        List<Item>? given = EventClaim.Give(session, items.ToList(), [], alsoSave: db => {
            bool? taken = db.TakePromotionCountInStore(session.AccountId, characterId, entry.ShareId, entry.LimitCount,
                PromotionRules.Expiration(entry, now), now.ToEpochSeconds());
            full = taken == false;
            return taken == true;
        }, refused: () => full);
        if (given == null) {
            if (!full) {
                logger.Error("Promotion {Id} drop of {CharacterId} not stored", entry.Id, session.CharacterId);
            }
            return;
        }
        if (items.Count > 0) {
            session.Send(TaskEventPacket.Reward(entry.Kind, items));
        }
    }

    // The running burning levelup event this character was picked for (the account's 2000 value is its id) and
    // has not completed (2001): the lowest id when several run.
    public GameEvent? BurningLevelupEvent() {
        return session.FindEvent(GameEventType.BurningLevelup)
            .Where(found => found.Metadata.Data is BurningLevelup
                            && Peek(GameEventUserValueType.BurningLevelupCharacter, found.Id) == session.CharacterId.ToString()
                            && !BurningLevelupRules.IsSet(Peek(GameEventUserValueType.BurningLevelupReached, found.Id)))
            .MinBy(found => found.Id);
    }

    // The levels added on reaching `level` by exp (ExperienceManager.LevelUp): GetBonusLevel of the picked
    // character's event, 0 without one.
    public int BurningLevelupBonus(int level) {
        return BurningLevelupEvent() is { Metadata.Data: BurningLevelup data } ? BurningLevelupRules.BonusLevel(data, level) : 0;
    }

    // The level a level set (a level potion, GM `level`) lands on: the level plus its GetBonusLevel for the picked
    // character, as a level up by exp reaches (BurningLevelupRules.Landing).
    public int BurningLevelupLanding(int level) {
        return BurningLevelupEvent() is { Metadata.Data: BurningLevelup data }
            ? BurningLevelupRules.Landing(data, level, session.ServerTableMetadata.ConstantsTable.characterMaxLevel)
            : level;
    }

    // Called after every level change. A change from below the jumping target to it or past it is the jumping done:
    // 2004 is set and stored (the client shows its jumpingComplete box on the same change, and the character list
    // offers no other pick). At or past the end level the picked character has completed the event: 2001 is set and
    // stored, the event's effect goes, and the reward widget can be claimed (EventReward 0x0f).
    public void BurningLevelupReach(int oldLevel, int level) {
        if (BurningLevelupEvent() is not { Metadata.Data: BurningLevelup data } gameEvent) {
            return;
        }
        if (BurningLevelupRules.JumpsPast(data, oldLevel, level)
            && !BurningLevelupRules.IsSet(Peek(GameEventUserValueType.BurningLevelupJumpingDone, gameEvent.Id))) {
            Get(GameEventUserValueType.BurningLevelupJumpingDone, gameEvent.Id, gameEvent.EndTime);
            Set(gameEvent.Id, GameEventUserValueType.BurningLevelupJumpingDone, 1);
            if (!Store(gameEvent.Id, GameEventUserValueType.BurningLevelupJumpingDone)) {
                logger.Error("Failed to store the burning levelup jumping. Event ID: {EventId}", gameEvent.Id);
            }
        }
        if (level < data.EndLevel) {
            return;
        }
        Get(GameEventUserValueType.BurningLevelupReached, gameEvent.Id, gameEvent.EndTime);
        Set(gameEvent.Id, GameEventUserValueType.BurningLevelupReached, 1);
        if (!Store(gameEvent.Id, GameEventUserValueType.BurningLevelupReached)) {
            logger.Error("Failed to store the burning levelup reach. Event ID: {EventId}", gameEvent.Id);
        }
        if (data.AdditionalEffectId > 0) {
            session.Player.Buffs.Remove(data.AdditionalEffectId, session.Player.ObjectId);
        }
    }

    // The jumping item (jumpingOption): on entering the field the tutorial ends in (tutorialEndFieldId), the picked
    // character below the item's target level gets it once, to the bag or by mail; 2003 is set and stored, and
    // EventReward 0x11 opens the client's notify box. Called on each field entry.
    // The item (bag row, or a mail when the bag refuses it) and 2003 are written in one transaction (EventClaim.Give),
    // so a crash cannot keep the item and lose 2003 (a second item at the next entry) or the other way round; nothing
    // is given or marked when it is not stored, and the next entry tries again. The client gets the 2003 Update (Set,
    // once committed) and the notify box only once both are kept.
    public void BurningLevelupFieldEnter() {
        if (session.Field is null || BurningLevelupEvent() is not { Metadata.Data: BurningLevelup { Jumping: not null } data } gameEvent
            || BurningLevelupRules.IsSet(Peek(GameEventUserValueType.BurningLevelupJumpingDone, gameEvent.Id))
            || !BurningLevelupRules.GivesJumpingItem(data, session.Field.MapId, session.Player.Value.Character.Level,
                BurningLevelupRules.IsSet(Peek(GameEventUserValueType.BurningLevelupJumpingNotify, gameEvent.Id)))) {
            return;
        }
        RewardItem reward = data.Jumping.Item;
        Item? item = session.Field.ItemDrop.CreateItem(reward.ItemId, reward.Rarity, reward.Amount);
        if (item == null) {
            logger.Error("Burning levelup jumping item {ItemId} of event {EventId} could not be made", reward.ItemId, gameEvent.Id);
            return;
        }
        const GameEventUserValueType notify = GameEventUserValueType.BurningLevelupJumpingNotify;
        Get(notify, gameEvent.Id, gameEvent.EndTime);
        if (EventClaim.Give(session, [item], [EventClaim.Of(gameEvent.Id, notify, 1)]) == null) {
            logger.Error("Burning levelup jumping item {ItemId} of event {EventId} not stored; no item given", reward.ItemId, gameEvent.Id);
            return;
        }
        session.Send(BurningLevelupPacket.JumpingItem(gameEvent.Id));
    }

    // The event's effect (additionalEffectId; the client never reads it, KR 90000838 is a marker buff without
    // stats) while the picked character has not completed the event, given on each field entry (LoadFieldBuffs).
    public void RefreshBurningLevelupBuff() {
        if (session.Field is null || BurningLevelupEvent() is not { Metadata.Data: BurningLevelup { AdditionalEffectId: > 0 } data }) {
            return;
        }
        session.Player.Buffs.AddBuff(session.Player, session.Player, data.AdditionalEffectId, 1, session.Field.FieldTick);
    }

    // The running snowman event that gathers snowflakes and pays claims: the lowest id when several run.
    public GameEvent? SnowmanEvent() {
        return session.FindEvent(GameEventType.Snowman).Where(found => found.Metadata.Data is Snowman).MinBy(found => found.Id);
    }

    // The new-user and returning-user marks due at this entry (UserMarks), set with event id 0 to the end of the
    // row that gives them and stored at once: the next entry's last save is recent, so a mark lost with the session
    // would not come back. Before Load, which sends them. Times in epoch seconds.
    public void Mark(long creationTime, long lastSave) {
        long now = DateTime.Now.ToEpochSeconds();
        Dictionary<GameEventUserValueType, long> due = UserMarks.Due(
            session.FindEvent(GameEventType.ReturnUser).Concat(session.FindEvent(GameEventType.NewUser)), creationTime, lastSave, now);
        List<GameEventUserValue> marked = [];
        lock (valuesLock) {
            foreach ((GameEventUserValueType type, long end) in due) {
                GameEventUserValue held = GetLocked(type, 0, end, now);
                if (held.Value == UserMarks.Set && held.ExpirationTime >= end) {
                    continue;
                }
                var mark = new GameEventUserValue(type, Math.Max(end, held.ExpirationTime), 0);
                mark.SetValue(UserMarks.Set);
                eventValues[0][type] = mark;
                marked.Add(mark);
            }
        }
        if (marked.Count == 0) {
            return;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.SaveGameEventUserValues(session.CharacterId, marked)) {
            logger.Warning("User marks of {CharacterId} not stored", session.CharacterId);
        }
    }

    public void Set(int gameEventId, GameEventUserValueType type, object value) {
        GameEventUserValue gameEventUserValue;
        lock (valuesLock) {
            gameEventUserValue = SetLocked(gameEventId, type, value);
        }
        session.Send(GameEventUserValuePacket.Update(gameEventUserValue));
    }

    private GameEventUserValue SetLocked(int gameEventId, GameEventUserValueType type, object value) {
        if (!eventValues.TryGetValue(gameEventId, out Dictionary<GameEventUserValueType, GameEventUserValue>? eventDictionary)) {
            throw new ArgumentOutOfRangeException(nameof(gameEventId), gameEventId, "Invalid game event id.");
        }
        if (!eventDictionary.TryGetValue(type, out GameEventUserValue? gameEventUserValue)) {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid game event type.");
        }

        string newValue = value.ToString() ?? throw new ArgumentException("Invalid value type.");
        gameEventUserValue.SetValue(newValue);
        return gameEventUserValue;
    }

    // Stores one value now instead of at logout: a claim whose reward is stored at once (a mailed item) must not
    // be lost with the session. False when there is no such value or the database refused it.
    public bool Store(int gameEventId, GameEventUserValueType type) {
        GameEventUserValue? value;
        lock (valuesLock) {
            value = eventValues.GetValueOrDefault(gameEventId)?.GetValueOrDefault(type);
        }
        if (value == null) {
            return false;
        }
        using GameStorage.Request db = session.GameStorage.Context();
        return AccountValue(type) ? db.SaveAccountEventValues(session.AccountId, [value]) : db.SaveGameEventUserValues(session.CharacterId, [value]);
    }

    public void Load() {
        // One packet per client store: the client clears a store on every Load of it (GameEventUserValue::Read,
        // commonfiles/gameeventuservalue.cpp:139), so batches would leave only the last one. The second store is sent
        // even when empty, so a character entered after another does not keep the other's values.
        List<GameEventUserValue> values;
        lock (valuesLock) {
            values = eventValues.Values.SelectMany(dict => dict.Values).ToList();
        }
        session.Send(GameEventUserValuePacket.Load(values.Where(value => GameEventUserValuePacket.Store(value.Type) == GameEventUserValuePacket.FirstStore).ToList()));
        session.Send(GameEventUserValuePacket.Load(values.Where(value => GameEventUserValuePacket.Store(value.Type) == GameEventUserValuePacket.SecondStore).ToList(),
            GameEventUserValuePacket.SecondStore));
    }

    // The value held for the event and type, expired or not; null when there is none. Unlike Get it adds no value.
    public GameEventUserValue? Held(int eventId, GameEventUserValueType type) {
        lock (valuesLock) {
            return eventValues.GetValueOrDefault(eventId)?.GetValueOrDefault(type);
        }
    }

    // The text of an unexpired value, null when there is none; unlike Get it adds no value.
    public string? Peek(GameEventUserValueType type, int eventId) {
        lock (valuesLock) {
            if (!eventValues.TryGetValue(eventId, out Dictionary<GameEventUserValueType, GameEventUserValue>? valueDict)
                || !valueDict.TryGetValue(type, out GameEventUserValue? value)
                || value.ExpirationTime <= DateTime.Now.ToEpochSeconds()) {
                return null;
            }
            return value.Value;
        }
    }

    // Whether any event holds an unexpired value of this type with this text, the events that have ended included.
    public bool HasValue(GameEventUserValueType type, string text, long now) {
        lock (valuesLock) {
            return eventValues.Values.Any(values => values.TryGetValue(type, out GameEventUserValue? value)
                                                    && value.ExpirationTime > now && value.Value == text);
        }
    }

    // The value of `type` on `eventId` kept until `expirationTime` when that is later than its own expiration (an
    // event extended after the value was set); its text stays. The caller stores it.
    public void Extend(GameEventUserValueType type, int eventId, long expirationTime) {
        lock (valuesLock) {
            if (!eventValues.TryGetValue(eventId, out Dictionary<GameEventUserValueType, GameEventUserValue>? values)
                || !values.TryGetValue(type, out GameEventUserValue? value) || value.ExpirationTime >= expirationTime) {
                return;
            }
            var extended = new GameEventUserValue(type, expirationTime, eventId);
            extended.SetValue(value.Value);
            values[type] = extended;
        }
    }

    public GameEventUserValue Get(GameEventUserValueType type, int eventId, long expirationTime) {
        return Get(type, eventId, expirationTime, DateTime.Now.ToEpochSeconds());
    }

    // As above, with the value tested against `now` (epoch seconds): the time the expiration was taken from, so a
    // step across midnight does not find its own day's value run out and start it again.
    public GameEventUserValue Get(GameEventUserValueType type, int eventId, long expirationTime, long now) {
        lock (valuesLock) {
            return GetLocked(type, eventId, expirationTime, now);
        }
    }

    private GameEventUserValue GetLocked(GameEventUserValueType type, int eventId, long expirationTime, long now) {
        if (!eventValues.TryGetValue(eventId, out Dictionary<GameEventUserValueType, GameEventUserValue>? valueDict)) {
            eventValues.Add(eventId, new Dictionary<GameEventUserValueType, GameEventUserValue> {
                { type, new GameEventUserValue(type, expirationTime, eventId) },
            });
        } else if (!valueDict.ContainsKey(type)) {
            valueDict.Add(type, new GameEventUserValue(type, expirationTime, eventId));
        }

        if (eventValues[eventId][type].ExpirationTime < now) {
            eventValues[eventId][type] = new GameEventUserValue(type, expirationTime, eventId);
        }

        return eventValues[eventId][type];
    }

    // The 106 value to store at now: the unexpired seconds plus this login's, ending at the next midnight.
    public static GameEventUserValue StoredPlayTime(GameEventUserValue value, DateTime playStart, DateTime now) {
        long stored = value.ExpirationTime > now.ToEpochSeconds() ? value.Long() : 0;
        var result = new GameEventUserValue(value.Type, now.Date.AddDays(1).ToEpochSeconds(), value.EventId);
        result.SetValue(AttendanceRules.PlaySeconds(stored, playStart, now).ToString());
        return result;
    }

    public void Save(GameStorage.Request db) {
        List<GameEventUserValue> values;
        lock (valuesLock) {
            // The day's play seconds (106) are stored with this login's added, for the next login; the value kept
            // here stays as the client has it, so both go on counting from the same play start. As in KMS2 it
            // lasts until the next midnight.
            DateTime now = DateTime.Now;
            values = eventValues.Values.SelectMany(value => value.Values)
                .Select(value => value.Type == GameEventUserValueType.AttendanceAccumulatedTime ? StoredPlayTime(value, PlayStart, now) : value)
                .ToList();
        }
        db.SaveGameEventUserValues(session.CharacterId, values.Where(value => !AccountValue(value.Type)).ToList());
        db.SaveAccountEventValues(session.AccountId, values.Where(value => AccountValue(value.Type)).ToList());
    }
}
