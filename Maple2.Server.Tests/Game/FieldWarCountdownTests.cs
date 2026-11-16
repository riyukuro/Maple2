using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// A field war room (map type FieldWar) takes players for the constant FieldWarInstanceEnterableDurationSeconds from
// its first one (FieldManager.OnAddPlayer starts the countdown); then each of its triggers gets FieldGameStart: 1 when
// the room holds FieldWarRequirePlayerCount players, else 2 (the room closes).
public class FieldWarCountdownTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private FieldManager field = null!;

    [SetUp]
    public void Room() {
        test = new TestGameSession();
        test.Start();
        field = test.Session.Field!;
        typeof(FieldManager).GetField("fieldTriggers", Any)!.SetValue(field, new ConcurrentDictionary<string, FieldTrigger> {
            ["main"] = TriggerOf(field, 7, "main"),
            ["door"] = TriggerOf(field, 8, "door"),
        });
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues), Any)!.SetValue(field, new TriggerUserValues());
    }

    private static FieldTrigger TriggerOf(FieldManager field, int id, string name) {
        var trigger = (FieldTrigger) RuntimeHelpers.GetUninitializedObject(typeof(FieldTrigger));
        typeof(FieldEntity<TriggerModel>).GetField("<Field>k__BackingField", Any)!.SetValue(trigger, field);
        typeof(FieldEntity<TriggerModel>).GetField("<Value>k__BackingField", Any)!.SetValue(trigger, new TriggerModel(id, name, Vector3.Zero, Vector3.Zero));
        return trigger;
    }

    private void MapType(MapType type) {
        MapMetadata map = field.Metadata with { Property = field.Metadata.Property with { Type = type } };
        typeof(FieldManager).GetField(nameof(FieldManager.Metadata), Any)!.SetValue(field, map);
    }

    private void Constants(int seconds, int players) =>
        test.SetServerTable(new ConstantsTable(FieldWarInstanceEnterableDurationSeconds: seconds, FieldWarRequirePlayerCount: players));

    private void PlayerArrives() => typeof(FieldManager).GetMethod("StartFieldWarCountdown", Any)!.Invoke(field, null);

    private int[] StartValues() => [field.UserValues.Get(7, FieldWarRules.StartKey), field.UserValues.Get(8, FieldWarRules.StartKey)];

    [Test]
    public void EnoughPlayers_StartTheWarOnEveryTrigger() {
        MapType(Maple2.Model.Enum.MapType.FieldWar);
        Constants(seconds: 0, players: 1);

        PlayerArrives();
        test.RunFieldQueue();

        Assert.That(StartValues(), Is.EqualTo(new[] { FieldWarRules.Start, FieldWarRules.Start }));
    }

    [Test]
    public void TooFewPlayers_CloseTheRoom() {
        MapType(Maple2.Model.Enum.MapType.FieldWar);
        Constants(seconds: 0, players: 2);

        PlayerArrives();
        test.RunFieldQueue();

        Assert.That(StartValues(), Is.EqualTo(new[] { FieldWarRules.Close, FieldWarRules.Close }));
    }

    // The window runs from the first player: until it is over the triggers wait, and a later player starts no
    // second countdown.
    [Test]
    public void TheCountdown_RunsOnceFromTheFirstPlayer() {
        MapType(Maple2.Model.Enum.MapType.FieldWar);
        Constants(seconds: 600, players: 1);
        int before = field.Scheduler.Count;

        PlayerArrives();
        PlayerArrives();
        test.RunFieldQueue();

        Assert.That(field.Scheduler.Count, Is.EqualTo(before + 1));
        Assert.That(field.UserValues.Contains(7, FieldWarRules.StartKey), Is.False);
    }

    [Test]
    public void AnotherMapType_HasNoCountdown() {
        MapType(Maple2.Model.Enum.MapType.Dungeon);
        Constants(seconds: 0, players: 1);

        PlayerArrives();
        test.RunFieldQueue();

        Assert.That(field.UserValues.Contains(7, FieldWarRules.StartKey), Is.False);
    }

    [Test]
    public void TryGetTrigger_FindsATriggerByName() {
        Assert.That(field.TryGetTrigger("door", out FieldTrigger? door), Is.True);
        Assert.That(door!.Value.Id, Is.EqualTo(8));
        Assert.That(field.TryGetTrigger("none", out _), Is.False);
    }

    [Test]
    public void ReplaceTrigger_SwapsOnlyTheTriggerStillThere() {
        field.TryGetTrigger("main", out FieldTrigger? main);
        FieldTrigger other = TriggerOf(field, 9, "main");

        Assert.That(field.ReplaceTrigger(main!, other), Is.True);
        Assert.That(field.ReplaceTrigger(main!, other), Is.False);
        field.TryGetTrigger("main", out FieldTrigger? now);
        Assert.That(now, Is.SameAs(other));
    }
}
