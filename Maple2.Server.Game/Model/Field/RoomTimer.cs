using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Model;

/// <summary>
/// The time state of a room timer, without the field, so the rules can be tested.
/// </summary>
public class RoomTimerClock {
    // Every field of a dungeon room runs on its own thread and may change the room's one clock.
    private readonly object mutex = new();
    private volatile int startTick;
    private volatile int duration;
    private volatile bool started;
    private volatile bool ended;
    private volatile bool stopped;
    private volatile int stoppedTick;
    private volatile int generation;
    private volatile int lapsVersion;
    private volatile bool lastModifyEffect = true;

    public int StartTick => startTick;
    public int Duration => duration;
    /// <summary>The time runs (Start or Restart was called).</summary>
    public bool Started => started;
    /// <summary>
    /// The time function was ended (dungeon_set_end_time): dungeon_timeout no longer passes. The room still
    /// closes at its time with isExpireTimeOut; the scripts note set_end_time needs that flag blank.
    /// </summary>
    public bool Ended => ended;
    /// <summary>The time was stopped (dungeon_stop_timer) at StoppedTick: the timer never runs out.</summary>
    public bool Stopped => stopped;
    public int StoppedTick => stoppedTick;
    /// <summary>Counts the restarts (dungeon_reset_time), so each field of the room sees one it did not make.</summary>
    public int Generation => generation;
    /// <summary>Counts the lap changes, so each field of the room sends the new marks.</summary>
    public int LapsVersion => lapsVersion;
    /// <summary>Whether the last time change showed the client's notice.</summary>
    public bool LastModifyEffect => lastModifyEffect;
    /// <summary>The lap marks of the gauge timer: id to milliseconds from the start.</summary>
    public SortedDictionary<int, int> Laps { get; } = [];

    public RoomTimerClock(int duration) {
        this.duration = duration;
    }

    public void Start(int tick) {
        lock (mutex) {
            startTick = tick;
            started = true;
        }
    }

    /// <summary>Starts the time unless another field of the room already did.</summary>
    public void StartOnce(int tick) {
        lock (mutex) {
            if (started) {
                return;
            }
            startTick = tick;
            started = true;
        }
    }

    /// <summary>Adds (or removes) time; returns the change actually made.</summary>
    public int Modify(int delta, bool showEffect = true) {
        lock (mutex) {
            int original = duration;
            duration = Math.Max(0, original + delta);
            lastModifyEffect = showEffect;
            return duration - original;
        }
    }

    public void Restart(int tick, int duration) {
        lock (mutex) {
            startTick = tick;
            this.duration = Math.Max(0, duration);
            started = true;
            ended = false;
            stopped = false;
            generation++;
        }
    }

    public void End() => ended = true;

    public void Stop(int tick) {
        lock (mutex) {
            if (stopped) {
                return;
            }
            stoppedTick = tick;
            stopped = true;
        }
    }

    /// <summary>The time ran out (a stopped time never does).</summary>
    public bool Expired(long tickCount) {
        lock (mutex) {
            return !stopped && tickCount > (long) startTick + duration;
        }
    }

    /// <summary>The dungeon_timeout condition: the time ran out and was not ended.</summary>
    public bool TimedOut(long tickCount) => !ended && Expired(tickCount);

    public void SetLap(int id, int lapTime) {
        lock (mutex) {
            Laps[id] = Math.Max(0, lapTime);
            lapsVersion++;
        }
    }

    public void MoveLapToNow(int id, int tick) {
        lock (mutex) {
            Laps[id] = Math.Max(0, (stopped ? stoppedTick : tick) - startTick);
            lapsVersion++;
        }
    }

    /// <summary>A copy of the lap marks, for a packet.</summary>
    public SortedDictionary<int, int> LapsSnapshot() {
        lock (mutex) {
            return new SortedDictionary<int, int>(Laps);
        }
    }
}

public class RoomTimer : IUpdatable {
    private readonly FieldManager field;
    public readonly RoomTimerType Type;
    public readonly RoomTimerClock Clock;
    /// <summary>When the time runs out, the players are sent back and the room is closed (bonus maps, dungeons with isExpireTimeOut).</summary>
    public readonly bool ExpiresRoom;
    /// <summary>The client's timer window was closed (dungeon_close_timer, dungeon_stop_timer); players entering later get none.</summary>
    public bool Closed { get; private set; }
    private bool started;
    private bool expiredHandled;
    // What this field's players were last sent of the shared clock; another field's change shows up in Update.
    private int seenGeneration;
    private bool seenStopped;
    private int seenDuration;
    private int seenLapsVersion;

    public int StartTick => Clock.StartTick;
    public int Duration => Clock.Duration;

    private readonly ILogger logger = Log.Logger.ForContext<RoomTimer>();

    public RoomTimer(FieldManager field, RoomTimerType type, int duration, bool expiresRoom = true)
        : this(field, type, new RoomTimerClock(duration), expiresRoom) { }

    /// <summary>
    /// A timer on a clock the field may share: every field of a dungeon room runs on the room's one clock, so a
    /// field entered later shows the time left and closes at the same time.
    /// </summary>
    public RoomTimer(FieldManager field, RoomTimerType type, RoomTimerClock clock, bool expiresRoom) {
        this.field = field;
        Type = type;
        Clock = clock;
        ExpiresRoom = expiresRoom;
        started = clock.Started;
        // A field that joins later starts from the clock as it is; Load gives its players the window.
        seenGeneration = clock.Generation;
        seenStopped = clock.Stopped;
        seenDuration = clock.Duration;
        seenLapsVersion = clock.LapsVersion;
        Closed = clock.Stopped;
    }

    public void Modify(int tick, bool showEffect = true) {
        int delta = Clock.Modify(tick, showEffect);
        seenDuration += delta;
        field.Broadcast(RoomTimerPacket.Modify(this, delta, showEffect));
    }

    /// <summary>
    /// The time still counts down to the room's close: started, not stopped and not run out. An ended time function
    /// (dungeon_set_end_time) or a closed window (dungeon_close_timer) still closes the room at its time, so it counts.
    /// </summary>
    public bool Counting => started && !Clock.Stopped && !expiredHandled;

    /// <summary>
    /// A dead npc's extendRoomTime: adds that time, with the client's notice, while the time counts. A timer that has
    /// not started, was stopped or ran out is left alone.
    /// </summary>
    public bool Extend(int tick) {
        if (tick <= 0 || !Counting) {
            return false;
        }
        Modify(tick, showEffect: true);
        return true;
    }

    /// <summary>dungeon_reset_time: the timer runs again for the given time from now.</summary>
    public void Restart(int duration) {
        Clock.Restart((int) field.FieldTick, duration);
        started = true;
        Sync(broadcast: true);
    }

    /// <summary>dungeon_set_end_time: the time function ends; dungeon_timeout no longer passes.</summary>
    public void End() => Clock.End();

    /// <summary>dungeon_stop_timer: the time stops and the client's timer window closes.</summary>
    public void Stop() {
        Clock.Stop((int) field.FieldTick);
        if (seenStopped) {
            // An earlier stop (here or in another field) the field's players saw: the window is already closed.
            Closed = true;
            return;
        }
        Sync(broadcast: true);
    }

    /// <summary>dungeon_close_timer: the client's timer window closes.</summary>
    public void Close() {
        Closed = true;
        field.Broadcast(RoomTimerPacket.Cancel());
    }

    public void SetLap(int id, int lapTime) {
        Clock.SetLap(id, lapTime);
        Sync(broadcast: true);
    }

    public void MoveLapToNow(int id) {
        Clock.MoveLapToNow(id, (int) field.FieldTick);
        Sync(broadcast: true);
    }

    /// <summary>A player who enters while the timer runs gets its window and laps.</summary>
    public void Load(FieldPlayer player) {
        if (!started || Closed) {
            return;
        }
        player.Session.Send(RoomTimerPacket.Start(this));
        SortedDictionary<int, int> laps = Clock.LapsSnapshot();
        if (laps.Count > 0) {
            player.Session.Send(RoomTimerPacket.Laps(laps));
        }
    }

    public void Update(long tickCount) {
        if (!started) {
            // A shared clock another field of the room started keeps its time.
            Clock.StartOnce((int) tickCount);
            started = true;
            Sync(broadcast: false);
            field.Broadcast(RoomTimerPacket.Start(this));
        } else {
            Sync(broadcast: true);
        }

        if (!ExpiresRoom || expiredHandled || !Clock.Expired(tickCount)) {
            return;
        }
        expiredHandled = true;

        foreach ((int objectId, FieldPlayer player) in field.Players) {
            int returnMapId = player.Session.ReturnMapId();
            player.Session.SendFieldEnter(player.Session.PrepareField(returnMapId, returnMapId));
        }

        logger.Debug("Room timer expired, disposing field {FieldId}", field.MapId);
        field.Dispose();
    }

    /// <summary>
    /// Brings this field up to the shared clock: a restart, stop, time change or lap mark made here or in another
    /// field of the room reaches this field's players once. A restart opens the window again and lets the room
    /// close at the new time.
    /// </summary>
    private void Sync(bool broadcast) {
        int generation = Clock.Generation;
        if (generation != seenGeneration) {
            seenGeneration = generation;
            seenStopped = false;
            seenDuration = Clock.Duration;
            Closed = false;
            expiredHandled = false;
            if (broadcast) {
                field.Broadcast(RoomTimerPacket.Restart(this));
            }
        }
        if (Clock.Stopped && !seenStopped) {
            seenStopped = true;
            Closed = true;
            if (broadcast) {
                field.Broadcast(RoomTimerPacket.Stop());
            }
        }
        int duration = Clock.Duration;
        if (duration != seenDuration) {
            int delta = duration - seenDuration;
            seenDuration = duration;
            if (broadcast) {
                field.Broadcast(RoomTimerPacket.Modify(this, delta, Clock.LastModifyEffect));
            }
        }
        int lapsVersion = Clock.LapsVersion;
        if (lapsVersion != seenLapsVersion) {
            seenLapsVersion = lapsVersion;
            if (broadcast) {
                field.Broadcast(RoomTimerPacket.Laps(Clock.LapsSnapshot()));
            }
        }
    }

    public bool Expired(long tickCount) => Clock.Expired(tickCount);

    public bool TimedOut(long tickCount) => Clock.TimedOut(tickCount);
}
