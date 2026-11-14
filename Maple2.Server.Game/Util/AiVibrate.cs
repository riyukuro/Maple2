using Maple2.Model.Metadata.FieldEntity;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The hidden vibrate groups of one field. A vibrate object with a hideGroupId (the Group3_0 rocks of maps
/// 02000037 and 02000526, groups 1 to 9) starts hidden. An AI's showVibrate shows the group's hidden objects,
/// and hideVibrateAll hides every shown one again.
/// Client: CSceneStateGame::OnHideVibrate starts a CEntityVibrator fall: it fades the object out, takes its
/// collision away and holds it. OnShowVibrate releases the hold, so the object fades in and drops onto its
/// place. A client never hides an object by itself, so a player entering the field is sent HideVibrate for
/// every hidden one.
/// </summary>
public class AiVibrate {
    private readonly Dictionary<string, int> groups = new();
    private readonly HashSet<string> shown = [];

    public AiVibrate(IEnumerable<FieldVibrateEntity> entities) {
        foreach (FieldVibrateEntity entity in entities) {
            if (entity.HideGroupId != 0 && !string.IsNullOrEmpty(entity.Id.Id)) {
                groups[entity.Id.Id] = entity.HideGroupId;
            }
        }
    }

    /// <summary>The objects a player entering the field must be sent HideVibrate for.</summary>
    public IEnumerable<string> Hidden => groups.Keys.Where(id => !shown.Contains(id));

    /// <summary>Whether the object is hidden now: the client has taken its collision away.</summary>
    public bool IsHidden(string id) => groups.ContainsKey(id) && !shown.Contains(id);

    /// <summary>Shows the group's hidden objects; returns the ones to send ShowVibrate for.</summary>
    public List<string> Show(int groupId) {
        List<string> ids = groups.Where(entry => entry.Value == groupId && !shown.Contains(entry.Key))
            .Select(entry => entry.Key).ToList();
        shown.UnionWith(ids);
        return ids;
    }

    /// <summary>Hides every shown object; returns the ones to send HideVibrate for.</summary>
    public List<string> HideAll() {
        List<string> ids = shown.ToList();
        shown.Clear();
        return ids;
    }
}
