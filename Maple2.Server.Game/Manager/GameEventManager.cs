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
    }

    private void GetUserValues() {
        IList<GameEvent> events = session.Events.ToList();

        using GameStorage.Request db = session.GameStorage.Context();
        IList<GameEventUserValue> values = db.GetEventUserValues(session.CharacterId);
        foreach (GameEventUserValue userValue in values) {
            // Remove if the event is no longer active or the value has expired
            if (events.All(gameEvent => gameEvent.Id != userValue.EventId) ||
                userValue.ExpirationTime < DateTime.Now.ToEpochSeconds()) {
                db.RemoveGameEventUserValue(userValue, session.CharacterId);
                continue;
            }

            if (!eventValues.TryGetValue(userValue.EventId, out Dictionary<GameEventUserValueType, GameEventUserValue>? valueDict)) {
                eventValues.Add(userValue.EventId, new Dictionary<GameEventUserValueType, GameEventUserValue> {
                    { userValue.Type, userValue },
                });
            } else {
                valueDict.TryAdd(userValue.Type, userValue);
            }
        }
    }

    private void UpdateDtReward() {
        if (session.Field is null) return;

        IList<GameEvent> events = session.FindEvent(GameEventType.DTReward);
        if (events.Count == 0) {
            return;
        }

        foreach (GameEvent gameEvent in events) {
            if (gameEvent.Metadata.Data is not DTReward dtReward) {
                continue;
            }
            DateTime now = DateTime.Now;

            GameEventUserValue rewardIndexValue = Get(GameEventUserValueType.DTRewardRewardIndex, gameEvent.Id, now.AddDays(1).ToEpochSeconds());

            if (dtReward.Entries.Length <= rewardIndexValue.Int()) {
                continue;
            }

            GameEventUserValue userValue = Get(GameEventUserValueType.DTRewardStartTime, gameEvent.Id, now.AddDays(1).ToEpochSeconds());
            Set(gameEvent.Id, GameEventUserValueType.DTRewardStartTime, DateTime.Now.ToEpochSeconds());
            userValue = Get(GameEventUserValueType.DTRewardCurrentTime, gameEvent.Id, now.AddDays(1).ToEpochSeconds());
            Set(gameEvent.Id, GameEventUserValueType.DTRewardCurrentTime, userValue.Long() + 1);

            // Give reward
            if (userValue.Long() >= dtReward.Entries[rewardIndexValue.Int()].EndDuration) {
                DTReward.Entry entry = dtReward.Entries[rewardIndexValue.Int()];
                Item? item = session.Field?.ItemDrop.CreateItem(entry.Item.ItemId, entry.Item.Rarity, entry.Item.Amount);
                if (item == null) {
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

                Set(gameEvent.Id, GameEventUserValueType.DTRewardRewardIndex, rewardIndexValue.Int() + 1);
                Set(gameEvent.Id, GameEventUserValueType.DTRewardCurrentTime, entry.StartDuration);
            }

            userValue = Get(GameEventUserValueType.DTRewardTotalTime, gameEvent.Id, now.AddDays(1).ToEpochSeconds());
            Set(gameEvent.Id, GameEventUserValueType.DTRewardTotalTime, userValue.Long() + 1);
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
        lock (valuesLock) {
            return GetLocked(type, eventId, expirationTime);
        }
    }

    private GameEventUserValue GetLocked(GameEventUserValueType type, int eventId, long expirationTime) {
        if (!eventValues.TryGetValue(eventId, out Dictionary<GameEventUserValueType, GameEventUserValue>? valueDict)) {
            eventValues.Add(eventId, new Dictionary<GameEventUserValueType, GameEventUserValue> {
                { type, new GameEventUserValue(type, expirationTime, eventId) },
            });
        } else if (!valueDict.ContainsKey(type)) {
            valueDict.Add(type, new GameEventUserValue(type, expirationTime, eventId));
        }

        if (eventValues[eventId][type].ExpirationTime < DateTime.Now.ToEpochSeconds()) {
            eventValues[eventId][type] = new GameEventUserValue(type, expirationTime, eventId);
        }

        return eventValues[eventId][type];
    }

    public void Save(GameStorage.Request db) {
        List<GameEventUserValue> values;
        lock (valuesLock) {
            // Update certain values upon log off
            // TODO: Maybe update this to handle a list of other types that need to be updated upon logoff?
            IEnumerable<GameEventUserValue> accumulatedTimeValues =
                eventValues.Values.SelectMany(dict => dict.Values.Where(value => value.Type == GameEventUserValueType.AttendanceAccumulatedTime));
            foreach (GameEventUserValue userValue in accumulatedTimeValues) {
                userValue.SetValue((DateTime.Now.AddSeconds(userValue.Long()) - session.Player.Value.Character.LastModified).Seconds.ToString());
            }
            values = eventValues.Values.SelectMany(value => value.Values).ToList();
        }
        db.SaveGameEventUserValues(session.CharacterId, values);
    }
}
