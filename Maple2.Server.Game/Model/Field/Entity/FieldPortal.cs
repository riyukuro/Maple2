using Maple2.Model.Metadata;
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
    public FieldManager? RandomRoomField;
    public int RandomRoomEntered;

    public FieldPortal(FieldManager field, int objectId, Portal value) : base(field, objectId, value) {
        Visible = value.Visible;
        Enabled = value.Enable;
        MinimapVisible = value.MinimapVisible;
    }

    public override void Update(long tickCount) {
        if (EndTick != 0 && tickCount > EndTick) {
            Field.RemovePortal(ObjectId);
        }
    }
}
