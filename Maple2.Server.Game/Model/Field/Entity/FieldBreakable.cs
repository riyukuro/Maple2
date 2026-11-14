using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Model;

public class FieldBreakable : FieldEntity<BreakableActor> {
    public readonly string EntityId;

    private long nextTick;
    // The breakable broke or went away: the npcs on its top are put on the floor on the next field update (the
    // break comes from a packet handler, the npcs move on the field's thread, so the flag is volatile).
    private volatile bool dropPending;

    public BreakableState State { get; private set; }

    // The packet's flag and freeze point (CBreakableObject m_nSyncA / m_nSyncC, breakableobject.cpp:351). A moving
    // breakable runs its move cycle on the server tick; one stopped holds the place the cycle had at FreezeTick
    // (breakableobject.cpp:391). The client starts moving (m_nSyncA = 1, breakableobject.cpp:137).
    public bool Moving { get; private set; } = true;
    public int FreezeTick { get; private set; }
    // The packet's added time (m_nSyncB): the client adds it to the cycle's clock (nTime, breakableobject.cpp:382).
    // Each restart takes the time it stood stopped off it, so the platform goes on from where it stopped instead of
    // jumping ahead by the stopped time (mod the cycle).
    public int AddedTime { get; private set; }
    private int stoppedAt;
    // The entity's moveTimeOffset in ms: the client adds it to the server tick of the cycle (breakableobject.cpp:382).
    private readonly int moveTimeOffset;

    public FieldBreakable(FieldManager field, int objectId, string entityId, BreakableActor breakable, int moveTimeOffset = 0) : base(field, objectId, breakable) {
        EntityId = entityId;
        this.moveTimeOffset = moveTimeOffset;
        State = breakable.Visible ? BreakableState.Show : BreakableState.Hide;
    }

    // set_breakable: starts or stops the move cycle. A stop freezes the cycle where it is now on the client's clock
    // (the server tick it got from TimeSync plus the offset and the added time); a start resumes it from there.
    public bool SetMoving(bool moving) => SetMoving(moving, Environment.TickCount);

    public bool SetMoving(bool moving, int now) {
        if (Moving == moving) {
            return false;
        }

        Moving = moving;
        if (moving) {
            AddedTime = BreakableCycle.Resume(AddedTime, stoppedAt, now);
            FreezeTick = 0;
        } else {
            stoppedAt = now;
            FreezeTick = BreakableCycle.Clock(now, moveTimeOffset, AddedTime);
        }
        Field.Broadcast(BreakablePacket.Update(this));
        return true;
    }

    // set_visible_breakable_object: shows (2) or hides (4) it for good; unlike a break, a hide here does not come back
    // by the reset timer. The caller sends the batch.
    public bool SetVisible(bool visible) {
        BreakableState state = visible ? BreakableState.Show : BreakableState.Hide;
        nextTick = 0;
        if (State == state) {
            return false;
        }

        State = state;
        if (state is BreakableState.Hide) {
            dropPending = true;
        }
        return true;
    }

    public bool UpdateState(BreakableState state) {
        if (State == state) {
            return false;
        }

        State = state;
        Field.Broadcast(BreakablePacket.Update(this));
        if (state is BreakableState.Break or BreakableState.Hide) {
            dropPending = true;
        }

        nextTick = State switch {
            BreakableState.Show => 0,
            BreakableState.Break => Environment.TickCount64 + Value.HideTime,
            BreakableState.Hide => Environment.TickCount64 + Value.ResetTime,
            BreakableState.Unknown5 => 0,
            BreakableState.Unknown6 => 0,
            _ => 0,
        };

        return true;
    }

    public override void Update(long tickCount) {
        if (dropPending) {
            dropPending = false;
            Field.DropNpcsOffBreakable(this);
        }

        if (nextTick == 0 || tickCount < nextTick) {
            return;
        }

        switch (State) {
            case BreakableState.Show:
                break;
            case BreakableState.Break:
                UpdateState(BreakableState.Hide);
                break;
            case BreakableState.Hide:
                UpdateState(BreakableState.Show);
                break;
            case BreakableState.Unknown5:
                break;
            case BreakableState.Unknown6:
                break;
        }
    }
}
