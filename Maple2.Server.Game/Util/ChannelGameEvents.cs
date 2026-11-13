using Maple2.Model.Game.Event;

namespace Maple2.Server.Game.Util;

// The game events a channel holds and the receivers (sessions) that loaded them. The World starts and ends an event
// at its start and end time; an event the client gets whose day of the week or time of day limits it (an edge event:
// table/Server/*/GameEvent.xml dayOfWeek or partTime, so far only GuildVsGameOpen 550001, Saturdays 10:55:10-12:00)
// also starts and stops at those edges, which Tick finds. The client opens and closes what such an event runs only
// on its Add and Remove (GameEventManagerCli::Add calls OnStart, Remove vfunc_6).
// Chosen here: the edges are sent as GameEvent Add and Remove, found by a timer once a second.
public sealed class ChannelGameEvents<T> where T : class {
    public sealed record Edges(List<GameEvent> Started, List<GameEvent> Stopped, List<T> Receivers) {
        public bool Any => Started.Count > 0 || Stopped.Count > 0;
    }

    private readonly object mutex = new();
    private readonly IDictionary<int, GameEvent> events;
    // Whether each edge event is on, as the loaded receivers last got it.
    private readonly Dictionary<int, bool> edgeStates = [];
    private readonly HashSet<T> loaded = new(ReferenceEqualityComparer.Instance);

    public ChannelGameEvents(IDictionary<int, GameEvent> events, DateTime now) {
        this.events = events;
        foreach (GameEvent gameEvent in events.Values) {
            if (HasEdges(gameEvent)) {
                edgeStates[gameEvent.Id] = gameEvent.IsActive(now);
            }
        }
    }

    public static bool HasEdges(GameEvent gameEvent) => gameEvent.SentToClient && (gameEvent.Metadata.ActiveDays.Length > 0
        || gameEvent.Metadata.StartPartTime != TimeSpan.Zero || gameEvent.Metadata.EndPartTime != TimeSpan.Zero);

    public List<GameEvent> Active(DateTime now) {
        lock (mutex) {
            return events.Values.Where(gameEvent => IsOn(gameEvent, now)).ToList();
        }
    }

    public GameEvent? Find(int eventId, DateTime now) {
        lock (mutex) {
            return events.TryGetValue(eventId, out GameEvent? gameEvent) && IsOn(gameEvent, now) ? gameEvent : null;
        }
    }

    public List<GameEvent> All() {
        lock (mutex) {
            return events.Values.ToList();
        }
    }

    // The events the receiver starts with, given to send under the lock: an edge Tick finds is in this list or
    // sent to the receiver after it, never both. The receiver gets every change from then on.
    public void Load(T receiver, DateTime now, Action<List<GameEvent>> send) {
        lock (mutex) {
            send(events.Values.Where(gameEvent => IsOn(gameEvent, now)).ToList());
            loaded.Add(receiver);
        }
    }

    public void Drop(T receiver) {
        lock (mutex) {
            loaded.Remove(receiver);
        }
    }

    // An event the World started: the loaded receivers to send it to, none while its day or time is off (Tick
    // sends it when it comes on). Null when the channel holds it already.
    public List<T>? Add(GameEvent gameEvent, DateTime now) {
        lock (mutex) {
            if (!events.TryAdd(gameEvent.Id, gameEvent)) {
                return null;
            }
            if (!HasEdges(gameEvent)) {
                return loaded.ToList();
            }
            bool on = gameEvent.IsActive(now);
            edgeStates[gameEvent.Id] = on;
            return on ? loaded.ToList() : [];
        }
    }

    // An event the World ended: the event and the loaded receivers to send its Remove to, none when its day or
    // time had it off already. Null when the channel does not hold it.
    public (GameEvent Event, List<T> Receivers)? Remove(int eventId) {
        lock (mutex) {
            if (!events.Remove(eventId, out GameEvent? gameEvent)) {
                return null;
            }
            bool on = !edgeStates.Remove(eventId, out bool state) || state;
            return (gameEvent, on ? loaded.ToList() : []);
        }
    }

    // The edge events that came on or went off since the last tick, and the loaded receivers to send them to.
    public Edges Tick(DateTime now) {
        lock (mutex) {
            var started = new List<GameEvent>();
            var stopped = new List<GameEvent>();
            foreach (GameEvent gameEvent in events.Values) {
                if (!HasEdges(gameEvent)) {
                    continue;
                }
                bool on = gameEvent.IsActive(now);
                if (!edgeStates.TryGetValue(gameEvent.Id, out bool was)) {
                    // Put in from outside (tests): loaded by its time, as IsOn gave it.
                    edgeStates[gameEvent.Id] = on;
                    continue;
                }
                if (was == on) {
                    continue;
                }
                edgeStates[gameEvent.Id] = on;
                (on ? started : stopped).Add(gameEvent);
            }
            return new Edges(started, stopped, started.Count > 0 || stopped.Count > 0 ? loaded.ToList() : []);
        }
    }

    // An edge event's last state; an event added to the dictionary from outside (tests) by its time now.
    private bool IsOn(GameEvent gameEvent, DateTime now) =>
        edgeStates.TryGetValue(gameEvent.Id, out bool on) ? on : gameEvent.IsActive(now);
}
