using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Game.Manager.Field;

public partial class FieldManager {
    public readonly TriggerCollection TriggerObjects;

    private readonly ConcurrentDictionary<int, FieldBreakable> triggerBreakable = new();
    private readonly ConcurrentDictionary<string, FieldTrigger> fieldTriggers = new();
    public readonly Dictionary<string, TickTimer> Timers = new();
    public readonly Dictionary<string, int> UserValues = new();
    public readonly Dictionary<string, Widget> Widgets = new();
    public readonly Dictionary<int, List<TriggerState>> States = new();
    // The score the score_board_score condition tests. Only score_board_set_score writes it (no kill, pickup or
    // packet score reaches it); null until a script sets it, and score_board_create/remove clear it.
    public int? ScoreBoardScore;
    // Dungeon variables of a field that is not part of a dungeon instance (a dungeon's fields use their room
    // record's store instead).
    public readonly ConcurrentDictionary<int, int> DungeonVariables = new();
    // Nif effects add_effect_nif put on npcs, by npc object id; a player who enters later gets them in one
    // Trigger LoadEffects packet, as the client keeps them in its own table by object id.
    public readonly ConcurrentDictionary<int, (string NifPath, bool IsOutline, float Scale, float RotateZ)> NpcEffects = new();

    // The effects of npcs still in the field, in the LoadEffects packet's form.
    public static List<(int ObjectId, string NifPath, bool IsOutline, float Scale, float RotateZ)> LiveNpcEffects(
            IReadOnlyDictionary<int, (string NifPath, bool IsOutline, float Scale, float RotateZ)> effects, Func<int, bool> isLive) {
        return effects.Where(entry => isLive(entry.Key))
            .OrderBy(entry => entry.Key)
            .Select(entry => (entry.Key, entry.Value.NifPath, entry.Value.IsOutline, entry.Value.Scale, entry.Value.RotateZ))
            .ToList();
    }

    // The ceremony of a wedding hall room, set when the first character enters it by Wedding 0x1a; null in every
    // other field.
    public WeddingCeremony? Wedding;

    public FieldTrigger? AddTrigger(TriggerModel trigger) {
        try {
            var fieldTrigger = new FieldTrigger(this, NextLocalId(), trigger) {
                Position = trigger.Position,
                Rotation = trigger.Rotation,
            };
            fieldTriggers[trigger.Name] = fieldTrigger;

            return fieldTrigger;
        } catch (ArgumentException ex) {
            logger.Warning(ex, "Invalid Trigger: {Exception}", ex.Message);
            return null;
        }
    }

    public ICollection<FieldTrigger> EnumerateTrigger() => fieldTriggers.Values;

    public bool TryGetTrigger(string name, [NotNullWhen(true)] out FieldTrigger? fieldTrigger) {
        return fieldTriggers.TryGetValue(name, out fieldTrigger);
    }

    // Used for debugging triggers
    public bool ReplaceTrigger(FieldTrigger oldFieldTrigger, FieldTrigger newFieldTrigger) {
        return fieldTriggers.TryUpdate(oldFieldTrigger.Value.Name, newFieldTrigger, oldFieldTrigger);
    }
}
