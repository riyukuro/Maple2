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
    public int StartTick { get; private set; }
    public int Duration { get; private set; }
    /// <summary>
    /// The time function was ended (dungeon_set_end_time): dungeon_timeout no longer passes. The room still
    /// closes at its time with isExpireTimeOut; the scripts note set_end_time needs that flag blank.
    /// </summary>
    public bool Ended { get; private set; }
    /// <summary>The time was stopped (dungeon_stop_timer) at StoppedTick: the timer never runs out.</summary>
    public bool Stopped { get; private set; }
    public int StoppedTick { get; private set; }
    /// <summary>The lap marks of the gauge timer: id to milliseconds from the start.</summary>
    public SortedDictionary<int, int> Laps { get; } = [];

    public RoomTimerClock(int duration) {
        Duration = duration;
    }

    public void Start(int tick) => StartTick = tick;

    /// <summary>Adds (or removes) time; returns the change actually made.</summary>
    public int Modify(int delta) {
        int original = Duration;
        Duration = Math.Max(0, Duration + delta);
        return Duration - original;
    }

    public void Restart(int tick, int duration) {
        StartTick = tick;
        Duration = Math.Max(0, duration);
        Ended = false;
        Stopped = false;
    }

    public void End() => Ended = true;

    public void Stop(int tick) {
        if (Stopped) {
            return;
        }
        Stopped = true;
        StoppedTick = tick;
    }

    /// <summary>The time ran out (a stopped time never does).</summary>
    public bool Expired(long tickCount) => !Stopped && tickCount > (long) StartTick + Duration;

    /// <summary>The dungeon_timeout condition: the time ran out and was not ended.</summary>
    public bool TimedOut(long tickCount) => !Ended && Expired(tickCount);

    public void SetLap(int id, int lapTime) => Laps[id] = Math.Max(0, lapTime);

    public void MoveLapToNow(int id, int tick) => Laps[id] = Math.Max(0, (Stopped ? StoppedTick : tick) - StartTick);
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

    public int StartTick => Clock.StartTick;
    public int Duration => Clock.Duration;

    private readonly ILogger logger = Log.Logger.ForContext<RoomTimer>();

    public RoomTimer(FieldManager field, RoomTimerType type, int duration, bool expiresRoom = true) {
        this.field = field;
        Type = type;
        Clock = new RoomTimerClock(duration);
        ExpiresRoom = expiresRoom;
    }

    public void Modify(int tick, bool showEffect = true) {
        int delta = Clock.Modify(tick);
        field.Broadcast(RoomTimerPacket.Modify(this, delta, showEffect));
    }

    /// <summary>dungeon_reset_time: the timer runs again for the given time from now.</summary>
    public void Restart(int duration) {
        Clock.Restart((int) field.FieldTick, duration);
        started = true;
        Closed = false;
        expiredHandled = false;
        field.Broadcast(RoomTimerPacket.Restart(this));
    }

    /// <summary>dungeon_set_end_time: the time function ends; dungeon_timeout no longer passes.</summary>
    public void End() => Clock.End();

    /// <summary>dungeon_stop_timer: the time stops and the client's timer window closes.</summary>
    public void Stop() {
        Clock.Stop((int) field.FieldTick);
        Closed = true;
        field.Broadcast(RoomTimerPacket.Stop());
    }

    /// <summary>dungeon_close_timer: the client's timer window closes.</summary>
    public void Close() {
        Closed = true;
        field.Broadcast(RoomTimerPacket.Cancel());
    }

    public void SetLap(int id, int lapTime) {
        Clock.SetLap(id, lapTime);
        field.Broadcast(RoomTimerPacket.Laps(Clock.Laps));
    }

    public void MoveLapToNow(int id) {
        Clock.MoveLapToNow(id, (int) field.FieldTick);
        field.Broadcast(RoomTimerPacket.Laps(Clock.Laps));
    }

    /// <summary>A player who enters while the timer runs gets its window and laps.</summary>
    public void Load(FieldPlayer player) {
        if (!started || Closed) {
            return;
        }
        player.Session.Send(RoomTimerPacket.Start(this));
        if (Clock.Laps.Count > 0) {
            player.Session.Send(RoomTimerPacket.Laps(Clock.Laps));
        }
    }

    public void Update(long tickCount) {
        if (!started) {
            Clock.Start((int) tickCount);
            field.Broadcast(RoomTimerPacket.Start(this));
            started = true;
        }

        if (!ExpiresRoom || expiredHandled || !Clock.Expired(tickCount)) {
            return;
        }
        expiredHandled = true;

        foreach ((int objectId, FieldPlayer player) in field.Players) {
            int returnMapId = player.Session.ReturnMapId();
            player.Session.Send(player.Session.PrepareField(returnMapId, returnMapId)
                ? FieldEnterPacket.Request(player)
                : FieldEnterPacket.Error(MigrationError.s_move_err_default));
        }

        logger.Debug("Room timer expired, disposing field {FieldId}", field.MapId);
        field.Dispose();
    }

    public bool Expired(long tickCount) => Clock.Expired(tickCount);

    public bool TimedOut(long tickCount) => Clock.TimedOut(tickCount);
}
