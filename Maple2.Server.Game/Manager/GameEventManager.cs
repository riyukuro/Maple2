using System.Collections.Immutable;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class GameEventManager {
    private readonly GameSession session;
    private readonly Dictionary<int, Dictionary<GameEventUserValueType, GameEventUserValue>> eventValues;
    // The packet handlers run on the field thread, but a partner's values are written from the other player's
    // handler (a game of two) and Save runs on logout; every access of eventValues takes this lock.
    private readonly object valuesLock = new();
    private long nextUpdateTick;

    // The server's local time; each step reads it once, so that a step across midnight tests and stores its values
    // against one day (tests set it).
    internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

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
        UpdateDailyLoginReward();
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
    public static GameEventUserValue? LoadValue(GameEventUserValue userValue, IEnumerable<GameEvent> events, long now) {
        GameEvent? gameEvent = events.FirstOrDefault(gameEvent => gameEvent.Id == userValue.EventId);
        if (gameEvent == null) {
            return null;
        }
        GameEventUserValue loaded = FollowEventEnd(userValue, gameEvent);
        return loaded.ExpirationTime < now ? null : loaded;
    }

    // A stored value that is not loaded is deleted only once it has run out. While its event is absent (taken out
    // of the event list, or a replay switched off) before the value's end, the value stays stored, unread: an event
    // that comes back before that end is the same run and finds its claims again. After that end the world server
    // starts a new run (GameStorage BeginEventRun).
    public static bool Outdated(GameEventUserValue userValue, IEnumerable<GameEvent> events, long now) {
        GameEvent? gameEvent = events.FirstOrDefault(gameEvent => gameEvent.Id == userValue.EventId);
        long end = gameEvent == null ? userValue.ExpirationTime : FollowEventEnd(userValue, gameEvent).ExpirationTime;
        return end < now;
    }

    // The values stored for the account instead of the character.
    // Chosen here: an item collection's claim is the account's as well, so that one set of condition items, which
    // stay with the player, is not claimed by every character of the account in turn.
    // So are the daily login mail's mark (UpdateDailyLoginReward) and the item box's day (EventRewardHandler).
    public static bool AccountValue(GameEventUserValueType type) => StampValueTypes.Contains(type)
        || type is GameEventUserValueType.CollectItemGroup or GameEventUserValueType.DailyLoginRewardGiven
            or GameEventUserValueType.ItemBoxReceivedDay;

    private static readonly GameEventUserValueType[] StampValueTypes = [
        GameEventUserValueType.StampCompleteRewardClaimed,
        GameEventUserValueType.StampRewardsClaimed,
        GameEventUserValueType.StampSpecialRewardsClaimed,
    ];

    // A long-term attendance value, a stamp card's claims and an item collection's claim last as long as their event. When the event's end has
    // moved since the value was stored (a replayed event is moved again by a later ingest), the value follows it:
    // stored at the first end it would run out while the event is still live, and every day or reward row claimed
    // so far could be claimed again. An event that is live again after its end is a new run, whose old values the
    // world server drops at its start (GameStorage BeginEventRun), so none is left here to follow.
    public static GameEventUserValue FollowEventEnd(GameEventUserValue value, GameEvent gameEvent) {
        bool follows = gameEvent.Metadata.Type switch {
            GameEventType.LongTermAttendGift => value.Type is GameEventUserValueType.LongTermAttendClaimedDays or GameEventUserValueType.LongTermAttendPoints
                or GameEventUserValueType.LongTermAttendDays or GameEventUserValueType.LongTermAttendPointTime,
            GameEventType.StampEvent => StampValueTypes.Contains(value.Type),
            GameEventType.CollectItemGroup => value.Type is GameEventUserValueType.CollectItemGroup,
            _ => false,
        };
        if (!follows || value.ExpirationTime == gameEvent.EndTime) {
            return value;
        }
        var moved = new GameEventUserValue(value.Type, gameEvent.EndTime, value.EventId);
        moved.SetValue(value.Value);
        return moved;
    }

    // The play-time reward's step after `elapsed` more seconds: the row's seconds and the total both grow by them,
    // and the row's reward is due once its seconds reach the row's end.
    public static (long Current, long Total, bool Due) DtRewardStep(long current, long total, long elapsed, int endDuration) {
        return (current + elapsed, total + elapsed, current + elapsed >= endDuration);
    }

    // Chosen here: the play-time values last until the last second of the server's local day, the end most KMS2
    // values carry (a few carry a later day's last second).
    public static long DtRewardExpiry(DateTime now) => now.Date.AddDays(1).AddSeconds(-1).ToEpochSeconds();

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

            // Give reward
            Item? item = session.Field?.ItemDrop.CreateItem(entry.Item.ItemId, entry.Item.Rarity, entry.Item.Amount);
            if (item == null) {
                Set(gameEvent.Id, GameEventUserValueType.DTRewardCurrentTime, current);
                continue;
            }

            // The next row's index and the restarted seconds are stored before the mail is made: a stop between the
            // two loses this row's mail instead of giving it again after the restart.
            Set(gameEvent.Id, GameEventUserValueType.DTRewardRewardIndex, rewardIndexValue.Int() + 1);
            Set(gameEvent.Id, GameEventUserValueType.DTRewardCurrentTime, entry.StartDuration);
            if (!Store(gameEvent.Id, GameEventUserValueType.DTRewardRewardIndex)
                || !Store(gameEvent.Id, GameEventUserValueType.DTRewardCurrentTime)) {
                logger.Error("Failed to store the DTReward index. Event ID: {EventId}, Reward Index: {RewardIndex}", gameEvent.Id, rewardIndexValue.Int());
                continue;
            }

            using GameStorage.Request db = session.GameStorage.Context();
            var mail = new Mail {
                Type = MailType.System,
                ReceiverId = session.CharacterId,
                SenderId = session.CharacterId,
                Content = entry.MailContentId.ToString(),
            };

            mail = db.CreateMail(mail);
            if (mail == null) {
                logger.Error("Failed to create mail for DTReward. Event ID: {EventId}, Reward Index: {RewardIndex}", gameEvent.Id, rewardIndexValue.Int());
                return;
            }

            item = db.CreateItem(mail.Id, item);
            if (item == null) {
                logger.Error("Failed to create item for DTReward. Event ID: {EventId}, Reward Index: {RewardIndex}", gameEvent.Id, rewardIndexValue.Int());
                return;
            }

            mail.Items.Add(item);
            session.Mail.Notify(true);
        }
    }

    // The daily login mail (DailyLoginReward): once a day while the event runs, the row's items in one system mail
    // with the row's mail text, for a character of at least the row's level (value3). Chosen here: once per account
    // (the client has no part in this event, so nothing tells), so that a new character of the same account does
    // not get the day's mail again; on the first second step in a field of the day (the server's local day, as the
    // play-time reward's), so a player online at midnight gets the next day's mail too. The day's mark is stored
    // before the mail is made: a stop between the two loses that day's mail instead of sending it twice.
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
            if (Get(GameEventUserValueType.DailyLoginRewardGiven, gameEvent.Id, expiry, nowSeconds).Int() != 0) {
                continue;
            }
            Set(gameEvent.Id, GameEventUserValueType.DailyLoginRewardGiven, 1);
            if (!Store(gameEvent.Id, GameEventUserValueType.DailyLoginRewardGiven)) {
                logger.Error("Failed to store the daily login mail of event {EventId} for {CharacterId}", gameEvent.Id, session.CharacterId);
                continue;
            }

            var items = new List<Item>();
            foreach (RewardItem rewardItem in reward.Items) {
                Item? item = session.Field?.ItemDrop.CreateItem(rewardItem.ItemId, rewardItem.Rarity, rewardItem.Amount);
                if (item != null) {
                    items.Add(item);
                }
            }
            if (items.Count == 0) {
                continue;
            }

            using GameStorage.Request db = session.GameStorage.Context();
            Mail? mail = db.CreateMail(new Mail {
                Type = MailType.System,
                ReceiverId = session.CharacterId,
                SenderId = session.CharacterId,
                Content = reward.MailId > 0 ? reward.MailId.ToString() : string.Empty,
            });
            if (mail == null) {
                logger.Error("Failed to create the daily login mail of event {EventId} for {CharacterId}", gameEvent.Id, session.CharacterId);
                continue;
            }
            foreach (Item item in items) {
                Item? created = db.CreateItem(mail.Id, item);
                if (created == null) {
                    logger.Error("Failed to create item {ItemId} of the daily login mail of event {EventId}", item.Id, gameEvent.Id);
                    continue;
                }
                mail.Items.Add(created);
            }
            session.Mail.Notify(true);
        }
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
        // One packet: the client clears its list on every Load (GameEventUserValue::Read,
        // commonfiles/gameeventuservalue.cpp:139), so batches would leave only the last one.
        List<GameEventUserValue> values;
        lock (valuesLock) {
            values = eventValues.Values.SelectMany(dict => dict.Values).ToList();
        }
        session.Send(GameEventUserValuePacket.Load(values));
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
