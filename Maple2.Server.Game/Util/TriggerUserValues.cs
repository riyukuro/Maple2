using System.Collections.Concurrent;

namespace Maple2.Server.Game.Util;

/// <summary>
/// A field's trigger user values. Each trigger has its own: a trigger reads only its own (user_value,
/// wait_seconds_user_value), and a write names the trigger it is for. Every write from outside a trigger names one:
/// the AI nodes TriggerSetUserValue/TriggerModifyUserValue (all 950 give a TriggerID), and the npc script
/// functions that set a key (all give setTriggerValueTriggerID). A trigger's own set_user_value names its target by
/// trigger_id, or writes its own value without one; the set_user_value_from_* actions and add_user_value always
/// write their own. The data relies on this: 47142 of 47356 sets with a trigger_id go to a trigger that reads the key, and
/// many scripts of one map keep a counter under the same key (WaveTime in each mob-wave script of 02000378), which
/// one field-wide value would mix up.
/// </summary>
public class TriggerUserValues {
    private readonly ConcurrentDictionary<(int TriggerId, string Key), int> values = new();

    /// <summary>The trigger a write goes to: the one it names, else the writing trigger.</summary>
    public static int Target(int ownTriggerId, int triggerId) => triggerId != 0 ? triggerId : ownTriggerId;

    public int Get(int triggerId, string key) => values.GetValueOrDefault((triggerId, key));

    public bool Contains(int triggerId, string key) => values.ContainsKey((triggerId, key));

    public void Set(int triggerId, string key, int value) => values[(triggerId, key)] = value;

    /// <summary>
    /// The AI node TriggerModifyUserValue (189 nodes in 63 AI files) adds its value to a trigger's user value. The
    /// files' comments say so: "PhaseSumTotal = PhaseSumTotal + 1", add 1 to MonsterMany when a monster enters
    /// battle and take 1 off when it dies (value 1 x120, -1 x63), and BridgeAppear +1 from each boss until it is 3.
    /// A key the trigger does not have yet starts from 0.
    /// </summary>
    public int Add(int triggerId, string key, int value) =>
        values.AddOrUpdate((triggerId, key), value, (_, current) => current + value);
}
