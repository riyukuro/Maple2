using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model;

public class FieldInteract : FieldEntity<InteractObjectMetadata> {
    public readonly string EntityId;

    private long nextTick;
    private long reactTick;

    public IInteractObject Object { get; init; }
    public InteractType Type => Value.Type;

    private int reactLimit;
    public InteractState State { get; private set; }
    public int SpawnId { get; init; }
    // A map treasure chest (MapChestRules): what it gives when opened.
    public MapChestRules.Kind Chest { get; init; }
    // A map treasure chest is added with the Add packet, which says reactable: it starts so, with no update sent.
    public InteractState InitialState { init => State = value; }

    public FieldInteract(FieldManager field, int objectId, string entityId, InteractObjectMetadata value, IInteractObject interactObject) : base(field, objectId, value) {
        EntityId = entityId;
        State = InteractState.Normal;
        nextTick = Environment.TickCount64;
        reactLimit = Value.ReactCount > 0 ? Value.ReactCount : int.MaxValue;
        Object = interactObject;
    }

    private readonly GatherRespawn gatherRespawn = new();

    // A gathered object: its state stays as it is for the field; only this character waits resetTime.
    public bool Gather(long characterId) {
        if (State != InteractState.Reactable) {
            return false;
        }
        return gatherRespawn.Take(characterId, Environment.TickCount64, Value.Time.Reset);
    }

    public bool React() {
        if (State != InteractState.Reactable) {
            return false;
        }

        reactTick = Environment.TickCount64;
        reactLimit--;
        // A map treasure chest is opened once: it hides after its hide time, as KMS2 sends it.
        if (Chest != MapChestRules.Kind.None) {
            reactLimit = 0;
        }
        if (reactLimit > 0) {
            SetState(InteractState.Normal);
            nextTick = reactTick + Value.Time.Reset;
        } else {
            if (Value.Time.Hide > 0) {
                SetState(InteractState.Normal);
                nextTick = reactTick + Value.Time.Hide;
            } else {
                // Immediately hide if no delay.
                SetState(InteractState.Hidden);
                nextTick = 0;
            }
        }

        return true;
    }

    public void SetState(InteractState state) {
        if (State == state) {
            return;
        }

        State = state;
        Field.Broadcast(InteractObjectPacket.Update(this));
    }

    public override void Update(long tickCount) {
        if (Object is InteractBillBoardObject billboard && DateTime.Now.ToEpochSeconds() > billboard.ExpirationTime) {
            SetState(InteractState.Hidden);
            Field.RemoveInteract(Object);
            return;
        }

        if (nextTick == 0 || tickCount < nextTick) {
            return;
        }

        // Checks if we should reset (resetTime > 0). An opened map treasure chest hides whatever its resetTime.
        if (State == InteractState.Normal && MapChestRules.StateAfterWait(Chest, reactLimit, Value.Time.Reset) is { } next) {
            SetState(next);
        }

        nextTick = 0;
    }
}
