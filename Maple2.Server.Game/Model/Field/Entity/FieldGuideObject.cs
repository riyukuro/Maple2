using System.Numerics;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Model;

public class FieldGuideObject(FieldManager field, int objectId, IGuideObject value) : FieldEntity<IGuideObject>(field, objectId, value), IByteSerializable {
    public long CharacterId { get; init; }
    // The rotation as it is sent when set: the entity's own rotation is kept from -180 to 180, KMS2 sends 0 to 360.
    public Vector3? Heading { get; set; }

    public void WriteTo(IByteWriter writer) {
        writer.Write<GuideObjectType>(Value.Type);
        writer.WriteInt(ObjectId);
        writer.WriteLong(CharacterId);
        writer.Write<Vector3>(Position);
        writer.Write<Vector3>(Heading ?? Rotation);
        writer.WriteClass<IGuideObject>(Value);
    }
}
