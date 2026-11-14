using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Xml;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The field's triggers in one update (FieldTrigger.UpdateAll): the states entered in this update run their onEnter
// before any trigger tests its conditions. 02000300's mobspawn tests monster_dead of 1099, which main spawns in its
// first onEnter; tested before main entered, the condition held at once.
public class TriggerUpdateOrderTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int WatcherId = 8;

    // watcher: its first state goes to "early" while "up" is still 0 (as monster_dead holds while 1099 has no monster)
    // and to "late" once it is 1. spawner: sets the watcher's "up" in its first onEnter (as main spawns 1099).
    private const string Watcher = "<ms2><state name=\"wait\">"
        + "<condition name=\"user_value\" key=\"up\" value=\"0\"><transition state=\"early\" /></condition>"
        + "<condition name=\"user_value\" key=\"up\" value=\"1\"><transition state=\"late\" /></condition>"
        + "</state><state name=\"early\" /><state name=\"late\" /></ms2>";
    private const string Spawner = "<ms2><state name=\"start\"><onEnter>"
        + "<action name=\"set_user_value\" trigger_id=\"8\" key=\"up\" value=\"1\" />"
        + "</onEnter></state></ms2>";

    private static FieldTrigger Add(FieldManager field, TriggerCache cache, int id, string name, string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        cache.AddReplace(("xblock", name), TriggerCache.ParseTrigger("xblock", name, document));
        FieldTrigger? trigger = field.AddTrigger(new TriggerModel(id, name, Vector3.Zero, Vector3.Zero));
        Assert.That(trigger, Is.Not.Null);
        return trigger!;
    }

    [Test]
    public void EveryOnEnterOfTheUpdateRunsBeforeAnyCondition() {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = test.Session.Field!;
        var cache = new TriggerCache(null!);
        typeof(FieldManager).GetField("<TriggerCache>k__BackingField", Any)!.SetValue(field, cache);
        typeof(FieldManager).GetField("fieldTriggers", Any)!.SetValue(field, new ConcurrentDictionary<string, FieldTrigger>());
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues), Any)!.SetValue(field, new TriggerUserValues());
        typeof(MapMetadata).GetField("<XBlock>k__BackingField", Any)!.SetValue(field.Metadata, "xblock");

        FieldTrigger watcher = Add(field, cache, WatcherId, "watcher", Watcher);
        FieldTrigger spawner = Add(field, cache, 9, "spawner", Spawner);

        // The watcher first, as the dictionary may give it.
        long tick = 1_000_000;
        FieldTrigger.UpdateAll([watcher, spawner], tick);
        FieldTrigger.UpdateAll([watcher, spawner], tick += 100_000);
        FieldTrigger.UpdateAll([watcher, spawner], tick += 100_000);

        var state = (Maple2.Server.Game.Trigger.Helpers.TriggerState?) typeof(FieldTrigger).GetField("state", Any)!.GetValue(watcher);
        Assert.That(state?.Name, Is.EqualTo("late"));
    }
}
