using System.Numerics;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The createInteractObject node puts an interact object where the npc stands (2 nodes: AI_Gigantika_SubBoss leaves
/// scrap metal on death, AI_KritiasSlime_MiniPuzzle_RunAway a golden chest). The client's SSG_INTERACT_ADD reads the
/// same fields as a map interact object: name, state, kind, row id, position, rotation, then the model, the asset
/// (for an MS2InteractActor the kfm), the normal and reactable animations, the scale and a flag. The client accepts
/// kind 1 (mesh) or 8 (billboard) only; both rows (10003001, 12000072) are type 1. Nothing in the packet ends the
/// object, so the server sends Remove after lifeTime.
/// </summary>
public static class AiInteract {
    // The combine spawn table's chests with a kfm name use this model (3666 rows; 208 use MS2InteractMesh).
    public const string ActorModel = "MS2InteractActor";

    // The client keys interact objects by name; one made at run time takes the object id the field gave it.
    public static string EntityId(int objectId) => $"AiCreate_{objectId}";

    public static InteractMeshObject Create(int objectId, AiMetadata.CreateInteractObjectNode node, Vector3 position, Vector3 rotation) {
        return new InteractMeshObject(EntityId(objectId), new Ms2InteractMesh(node.InteractID, position, rotation)) {
            Model = ActorModel,
            Asset = node.KfmName,
            NormalState = node.Normal,
            Reactable = node.Reactable,
        };
    }

    // lifeTime="20" and "120": read like a summon's lifeTime (under 1000 is seconds), so 20 s and 120 s. That is a
    // server reading; the chest row's own hideTime is 10000 ms. 0 is no limit.
    public static TimeSpan LifeTime(int lifeTime) => AiSummon.LifeTime(lifeTime);
}
