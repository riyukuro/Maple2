using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Game.Model;

public class FieldPortal : FieldEntity<Portal> {
    public bool Visible;
    public bool Enabled;
    public bool MinimapVisible;
    public int EndTick;
    public int StartTick;
    public string Model = "";
    public long HomeId;
    public string OwnerName = "";
    public string Password = "";
    public int RoomId;
    // A createRandomRoom portal: its room is made at the first entry, so the room's time starts then.
    public RandomRoomFieldMetadata? RandomRoom;
    // An OpenMassive item's portal: like a random room it leads to a room of its own, made at the first entry,
    // that takes the item's maxCount players (0 = no limit).
    public int? MassiveMaxCount;
    // The room of a random-room or OpenMassive portal, and how many players went in through the portal.
    public FieldManager? RoomField;
    public int RoomEntered;

    public FieldPortal(FieldManager field, int objectId, Portal value) : base(field, objectId, value) {
        Visible = value.Visible;
        Enabled = value.Enable;
        MinimapVisible = value.MinimapVisible;
    }

    private bool? pkConfirm;

    // The client asks before it uses this portal and sends the use when the box is answered, without testing the
    // box again (PortalAction::FUN_140956370, actionkeyobject.cpp:1100-1130; MyPC::OnMessage, mypc.cpp:911-924;
    // PortalAction::FUN_140956be0, actionkeyobject.cpp:1141-1160): a password portal, or a portal from a field without
    // PK into a PK field (the client's confirm also needs the target to be a shadow continent, which the server's map
    // data does not hold, so this takes a few portals more).
    public bool AsksFirst => !string.IsNullOrEmpty(Password) || (pkConfirm ??= PkConfirm());

    private bool PkConfirm() {
        return Field.MapMetadata?.TryGet(Value.TargetMapId, out MapMetadata? target) == true
            && target.Property.PkMode && !Field.Metadata.Property.PkMode;
    }

    public override void Update(long tickCount) {
        if (EndTick != 0 && tickCount > EndTick) {
            Field.RemovePortal(ObjectId);
            return;
        }

        // A player whose box held them may answer the question after walking off (MoveFieldHandler).
        if (Enabled && AsksFirst) {
            foreach (FieldPlayer player in Field.Players.Values) {
                if (PortalRules.InBox(player.Position, Position, Rotation, Value.Dimension, Value.Offset, InteractRules.ReachSlack)) {
                    player.AskedPortalId = Value.Id;
                }
            }
        }
    }
}
