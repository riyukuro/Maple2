using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Xml;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// timeEventData type 6: the user value a row gives a trigger of its fields at every run. The Kritias fields'
// TimeEventData_triggerValue scripts wait on it (row 100101: map 02020030, trigger 100001, MelodyOn = 1, every 15 min
// for 10 min); before, no server code read type 6 and their puzzle never opened.
public class FieldTriggerEventTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int TriggerId = 100001;
    private const int PuzzleTriggerId = 11000;

    // The KR script 100001_TimeEvent_triggerValue of 02020030 (positions left out).
    private const string Script = "<ms2>"
        + "<state name=\"Wait\"><onEnter><action name=\"set_user_value\" key=\"MelodyOn\" value=\"0\" /></onEnter>"
        + "<condition name=\"user_value\" key=\"MelodyOn\" value=\"1\"><transition state=\"PuzzleOn\" /></condition></state>"
        + "<state name=\"PuzzleOn\"><onEnter><action name=\"set_user_value\" key=\"MelodyOn\" value=\"0\" />"
        + "<action name=\"set_user_value\" key=\"TimeEventOn\" value=\"1\" trigger_id=\"11000\" /></onEnter>"
        + "<condition name=\"wait_seconds_user_value\" key=\"TimeEventLifeTime\"><transition state=\"PuzzleOff\" /></condition></state>"
        + "<state name=\"PuzzleOff\"><onEnter><action name=\"set_user_value\" key=\"TimeEventOn\" value=\"0\" trigger_id=\"11000\" /></onEnter>"
        + "<condition name=\"wait_tick\" wait_tick=\"1000\"><transition state=\"Wait\" /></condition></state>"
        + "</ms2>";

    private static readonly DateTime Start = new(2014, 1, 1, 5, 0, 0);

    private static FieldTriggerEventMetadata Row(int probability = 100) =>
        new(100101, probability, Start, new DateTime(2099, 12, 31, 12, 0, 0), TimeSpan.FromMinutes(15), TimeSpan.Zero,
            TimeSpan.FromMinutes(10), [], TriggerId, "MelodyOn", 1);

    private TestGameSession test = null!;
    private FieldManager field = null!;
    private FieldTrigger trigger = null!;
    private long tick;

    private void Setup(params FieldTriggerEventMetadata[] rows) {
        test = new TestGameSession();
        test.Start();
        field = test.Session.Field!;
        var document = new XmlDocument();
        document.LoadXml(Script);
        var cache = new TriggerCache(null!);
        cache.AddReplace(("xblock", "100001_timeevent_triggervalue"), TriggerCache.ParseTrigger("xblock", "100001_timeevent_triggervalue", document));
        typeof(FieldManager).GetField("<TriggerCache>k__BackingField", Any)!.SetValue(field, cache);
        typeof(FieldManager).GetField("fieldTriggers", Any)!.SetValue(field, new ConcurrentDictionary<string, FieldTrigger>());
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues), Any)!.SetValue(field, new TriggerUserValues());
        typeof(MapMetadata).GetField("<XBlock>k__BackingField", Any)!.SetValue(field.Metadata, "xblock");
        var dict = new Dictionary<int, FieldTriggerEventMetadata>();
        foreach (FieldTriggerEventMetadata row in rows) {
            dict[row.Id] = row with { MapIds = [field.MapId] };
        }
        test.SetServerTable(new TimeEventTable(new Dictionary<int, GlobalPortalMetadata>(), new Dictionary<int, WorldBossMetadata>(),
            FieldTriggerEvent: dict));
        trigger = field.AddTrigger(new TriggerModel(TriggerId, "100001_TimeEvent_triggerValue", Vector3.Zero, Vector3.Zero))!;
        Assert.That(trigger, Is.Not.Null);
        // The field tick runs on from the machine's (wait_seconds_user_value counts from it in real time).
        tick = Environment.TickCount64;
    }

    private void Step(int count = 1) {
        for (int i = 0; i < count; i++) {
            tick += 100_000;
            typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(field, tick);
            trigger.Update(tick);
        }
    }

    private int PuzzleOn => field.UserValues.Get(PuzzleTriggerId, "TimeEventOn");

    // The field made 1 minute into a run gives the trigger the run for its 9 minutes left; the puzzle opens and
    // stays open (TimeEventLifeTime is read, not 0).
    [Test]
    public void RunAliveAtFirstUpdate_OpensThePuzzleForWhatIsLeft() {
        Setup(Row());
        Step(2); // enters Wait (MelodyOn = 0), waits
        Assert.That(PuzzleOn, Is.EqualTo(0));
        field.UpdateTriggerEvents(Start.AddDays(100).AddMinutes(1));
        Assert.That(field.UserValues.Get(TriggerId, "MelodyOn"), Is.EqualTo(1));
        Assert.That(field.UserValues.Get(TriggerId, FieldTriggerEventRules.LifeTimeKey), Is.EqualTo(540));
        Step(4); // Wait -> PuzzleOn, then waits there
        Assert.That(PuzzleOn, Is.EqualTo(1));
        Assert.That(field.UserValues.Get(TriggerId, "MelodyOn"), Is.EqualTo(0));
    }

    // Between two runs nothing is given; the next run start gives the whole lifeTime.
    [Test]
    public void BetweenRuns_WaitsForTheNextStart() {
        Setup(Row());
        Step(2);
        DateTime day = Start.AddDays(100);
        field.UpdateTriggerEvents(day.AddMinutes(11));
        field.UpdateTriggerEvents(day.AddMinutes(14).AddSeconds(59));
        Step(3);
        Assert.That(PuzzleOn, Is.EqualTo(0));
        Assert.That(field.UserValues.Contains(TriggerId, "MelodyOn"), Is.True); // the script's own 0
        Assert.That(field.UserValues.Contains(TriggerId, FieldTriggerEventRules.LifeTimeKey), Is.False);

        field.UpdateTriggerEvents(day.AddMinutes(15));
        Assert.That(field.UserValues.Get(TriggerId, FieldTriggerEventRules.LifeTimeKey), Is.EqualTo(600));
        Step(3);
        Assert.That(PuzzleOn, Is.EqualTo(1));
    }

    // prob 0: the row never runs.
    [Test]
    public void ProbabilityZero_NeverRuns() {
        Setup(Row(probability: 0));
        Step(2);
        field.UpdateTriggerEvents(Start.AddDays(100).AddMinutes(1));
        field.UpdateTriggerEvents(Start.AddDays(100).AddMinutes(15));
        Step(3);
        Assert.That(PuzzleOn, Is.EqualTo(0));
        Assert.That(field.UserValues.Contains(TriggerId, FieldTriggerEventRules.LifeTimeKey), Is.False);
    }

    // A row of another map gives this field nothing.
    [Test]
    public void OtherMapsRow_GivesNothing() {
        Setup();
        test.SetServerTable(new TimeEventTable(new Dictionary<int, GlobalPortalMetadata>(), new Dictionary<int, WorldBossMetadata>(),
            FieldTriggerEvent: new Dictionary<int, FieldTriggerEventMetadata> { [100101] = Row() with { MapIds = [field.MapId + 1] } }));
        Step(2);
        field.UpdateTriggerEvents(Start.AddDays(100).AddMinutes(1));
        Step(3);
        Assert.That(PuzzleOn, Is.EqualTo(0));
    }

    [Test]
    public void Rules() {
        Assert.That(FieldTriggerEventRules.SecondsLeft(Start, Start.AddSeconds(539.2)), Is.EqualTo(540));
        Assert.That(FieldTriggerEventRules.SecondsLeft(Start, Start), Is.EqualTo(0));
        FieldTriggerEventMetadata row = Row() with { RandomTime = TimeSpan.FromMinutes(2) };
        Assert.That(FieldTriggerEventRules.NextRun(row, Start.AddMinutes(1), 0.5), Is.EqualTo(Start.AddMinutes(16)));
        Assert.That(FieldTriggerEventRules.NextRun(row, Start.AddMinutes(1), 0), Is.EqualTo(Start.AddMinutes(15)));
        Assert.That(FieldTriggerEventRules.CurrentRun(row, Start.AddMinutes(11)), Is.Null);
        Assert.That(FieldTriggerEventRules.CurrentRun(row, Start.AddMinutes(16)), Is.EqualTo(Start.AddMinutes(15)));
        Assert.That(FieldTriggerEventRules.NextRun(row with { EndTime = Start.AddMinutes(10) }, Start.AddMinutes(1), 0), Is.Null);
    }
}
