using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model;

// A reactor placed with a CreateReactor item. It walks its state list by time and by player reactions.
public class FieldReactor : IUpdatable {
    private readonly FieldManager field;
    private readonly HashSet<long> connected = [];
    private long stateTick;

    public int ObjectId { get; }
    public long OwnerId { get; }
    public string Name { get; }
    public ReactorTable.Entry Metadata { get; }
    public Vector3 Position { get; }
    public Vector3 Rotation { get; }
    public int StateIndex { get; private set; }
    public long StateTime { get; private set; }

    public FieldReactor(FieldManager field, int objectId, string name, ReactorTable.Entry metadata, Vector3 position, Vector3 rotation, long ownerId = 0) {
        this.field = field;
        OwnerId = ownerId;
        ObjectId = objectId;
        Name = name;
        Metadata = metadata;
        Position = position;
        Rotation = rotation;
        stateTick = field.FieldTick;
        StateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public ReactorTable.State State => Metadata.States[StateIndex];

    public long[] Connected {
        get {
            lock (connected) {
                return connected.ToArray();
            }
        }
    }

    public int ConnectedCount {
        get {
            lock (connected) {
                return connected.Count;
            }
        }
    }

    public bool IsConnected(long characterId) {
        lock (connected) {
            return connected.Contains(characterId);
        }
    }

    public void Connect(long characterId) {
        lock (connected) {
            connected.Add(characterId);
        }
        field.Broadcast(ReactorPacket.UpdateState(ObjectId, StateIndex, StateTime, Connected));
    }

    // A connected player took the reward. When nobody is left the reactor moves on.
    public void Disconnect(long characterId) {
        bool empty;
        lock (connected) {
            connected.Remove(characterId);
            empty = connected.Count == 0;
        }
        if (empty) {
            Next();
        } else {
            field.Broadcast(ReactorPacket.UpdateState(ObjectId, StateIndex, StateTime, Connected));
        }
    }

    // A connected player left the field. In a reward state that counts as done, so the reactor is not
    // held open by someone who is gone.
    public void Leave(long characterId) {
        bool empty;
        lock (connected) {
            if (!connected.Remove(characterId)) {
                return;
            }
            empty = connected.Count == 0;
        }
        if (empty && State.Type == ReactorStateType.Reward) {
            Next();
        } else {
            field.Broadcast(ReactorPacket.UpdateState(ObjectId, StateIndex, StateTime, Connected));
        }
    }

    public void Update(long tickCount) {
        bool anyConnected;
        lock (connected) {
            anyConnected = connected.Count > 0;
        }

        switch (ReactorRules.Tick(State, StateIndex >= Metadata.States.Length - 1, anyConnected, tickCount - stateTick)) {
            case ReactorRules.Step.Advance:
                Next();
                break;
            case ReactorRules.Step.Remove:
                field.RemoveReactor(ObjectId);
                break;
        }
    }

    private void Next() {
        if (StateIndex >= Metadata.States.Length - 1) {
            field.RemoveReactor(ObjectId);
            return;
        }

        StateIndex++;
        stateTick = field.FieldTick;
        StateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        field.Broadcast(ReactorPacket.UpdateState(ObjectId, StateIndex, StateTime, Connected));
    }
}
