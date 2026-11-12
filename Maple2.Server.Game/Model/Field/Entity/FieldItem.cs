using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Model;

/// <summary>
/// The u16 drop kind of FieldAddItem, per drop source as KMS2 sends it. The client reads it into CDropItem:
/// 33 places the drop at the interact object found at its position, 3200 is the pet trap drop.
/// </summary>
public static class ItemDropKind {
    public const short None = 0;
    public const short NpcDeath = 21;
    public const short NpcHit = 24;
    public const short InteractObject = 33;
    public const short PlayerDrop = 1005;
    public const short Trigger = 1062;
    // Gathering: 3100 + the recipe's mastery type (3103 mining, 3104 gathering, 3105 breeding, 3106 farming).
    public const short Mastery = 3100;
    public const short Pet = 3200;
}

public class FieldItem : FieldEntity<Item> {
    // An unpicked drop leaves the field 90 s after it lands, unless its source gives another duration.
    public const long DefaultDurationMs = 90_000;
    // An item a player drops from the inventory stays 60 s.
    public const long PlayerDropDurationMs = 60_000;

    // A trigger's spawn point drop stays for the spawn point's Lifetime in seconds; 0 keeps the default.
    public static long SpawnPointDurationMs(float lifetimeSeconds) {
        if (lifetimeSeconds <= 0) {
            return 0;
        }
        return (long) Math.Min(lifetimeSeconds * 1000d, int.MaxValue);
    }

    public IFieldEntity? Owner { get; init; }

    private readonly long despawnTick;
    public bool FixedPosition { get; init; }

    public long ReceiverId { get; init; }
    public DropType Type { get; init; }
    public short DropKind { get; init; }

    public FieldItem(FieldManager field, int objectId, Item value, long msDuration = 0) : base(field, objectId, value) {
        despawnTick = Environment.TickCount64 + (msDuration == 0 ? DefaultDurationMs : msDuration);
    }

    public void Pickup(FieldPlayer looter) {
        if (Value.IsMeso()) {
            Field.Broadcast(ItemPickupPacket.PickupMeso(ObjectId, looter, Value.Amount));
        } else if (Value.IsStamina()) {
            Field.Broadcast(ItemPickupPacket.PickupStamina(ObjectId, looter, Value.Amount));
        } else {
            Field.Broadcast(ItemPickupPacket.PickupItem(ObjectId, looter));
        }
    }

    public override void Update(long tickCount) {
        if (tickCount > despawnTick) {
            Field.RemoveItem(ObjectId);
        }
    }
}
