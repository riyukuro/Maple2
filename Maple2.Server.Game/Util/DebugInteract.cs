using System.Numerics;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

/// <summary>
/// InteractObject 9, the client's object create dialog (UIObjectCreateDialog::OnObjectCreate,
/// uiobjectcreatedialog.cpp:143-166), a debug window: the GM picks a row of the interact object table (the list's
/// "code" is the row id, RefreshObjectList) and the client sends that id, four strings, a count and the GM's position
/// and rotation. The server adds the object there and every player of the field gets SSG_INTERACT_ADD, as for an AI's
/// createInteractObject (AiInteract).
/// Chosen here: the four strings fill the four strings of SSG_INTERACT_ADD in their order (model, asset, normal and
/// reactable animation); the client does not name them. The count is read and not used: the dialog does not say what
/// it counts, so one object is made per press. The object stays until the field closes.
/// </summary>
public static class DebugInteract {
    public static string EntityId(int objectId) => $"DebugCreate_{objectId}";

    public static InteractMeshObject Create(int objectId, int interactId, string model, string asset, string normal,
                                            string reactable, Vector3 position, Vector3 rotation) {
        return new InteractMeshObject(EntityId(objectId), new Ms2InteractMesh(interactId, position, rotation)) {
            Model = model,
            Asset = asset,
            NormalState = normal,
            Reactable = reactable,
        };
    }
}
