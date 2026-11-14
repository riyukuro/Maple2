using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// A script that moves to a state it does not have stops there; when it left a cinematic open or the players
// locked (KR 52000092 after quest 50100520: set_cinematic_ui 1, then a move to a completion state for quest 50100520 that the script never defines),
// the trigger closes the cinematic and unlocks the players once.
public class TriggerStuckCinematicTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private FieldManager field = null!;
    private FieldTrigger trigger = null!;
    private long tick;

    private void Setup(string enter) {
        SetupStates("<state name=\"scene\"><onEnter>" + enter + "</onEnter>"
            + "<condition name=\"true\"><transition state=\"gone\" /></condition></state>");
    }

    private void SetupStates(string states) {
        test = new TestGameSession();
        test.Start();
        field = test.Session.Field!;
        typeof(FieldManager).GetField("fieldProperties", Any)!.SetValue(field, new ConcurrentDictionary<FieldProperty, IFieldProperty>());
        var document = new XmlDocument();
        document.LoadXml("<ms2>" + states + "</ms2>");
        var cache = new TriggerCache(null!);
        cache.AddReplace(("xblock", "main"), TriggerCache.ParseTrigger("xblock", "main", document));
        typeof(FieldManager).GetField("<TriggerCache>k__BackingField", Any)!.SetValue(field, cache);
        typeof(FieldManager).GetField("fieldTriggers", Any)!.SetValue(field, new ConcurrentDictionary<string, FieldTrigger>());
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues), Any)!.SetValue(field, new TriggerUserValues());
        typeof(MapMetadata).GetField("<XBlock>k__BackingField", Any)!.SetValue(field.Metadata, "xblock");
        trigger = field.AddTrigger(new TriggerModel(1, "main", Vector3.Zero, Vector3.Zero))!;
        tick = Environment.TickCount64;
        test.TakeSent();
        Step(); // enters the first state; Setup's condition moves on to the missing state in the same update
    }

    private void Step(int count = 1, long by = 100_000) {
        for (int i = 0; i < count; i++) {
            tick += by;
            typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(field, tick);
            trigger.Update(tick);
        }
    }

    // Each Cinematic packet as (command, first byte or -1), and each FieldProperty one as (command, type).
    private (List<(byte, int)> Cinematic, List<(byte, byte)> Property) Sent() {
        var cinematic = new List<(byte, int)>();
        var property = new List<(byte, byte)>();
        foreach (byte[] packet in test.TakeSent()) {
            var reader = new ByteReader(packet, 2);
            switch (TestGameSession.Op(packet)) {
                case SendOp.Cinematic:
                    byte command = reader.ReadByte();
                    cinematic.Add((command, reader.Available > 0 ? reader.ReadByte() : -1));
                    break;
                case SendOp.FieldProperty:
                    property.Add((reader.ReadByte(), reader.ReadByte()));
                    break;
            }
        }
        return (cinematic, property);
    }

    [Test]
    public void OpenCinematicAndLock_AreGivenBackOnce() {
        Setup("<action name=\"set_cinematic_ui\" type=\"1\" /><action name=\"set_cinematic_ui\" type=\"3\" /><action name=\"lock_my_pc\" is_lock=\"1\" />");
        (List<(byte, int)> cinematic, List<(byte, byte)> property) = Sent();
        // the script's key mode on (1, true) and view (3); then key mode off (1, false) and close (2)
        Assert.That(cinematic.Select(x => x.Item1), Is.EqualTo(new byte[] { 1, 3, 1, 2 }));
        Assert.That(cinematic[0].Item2, Is.EqualTo(1));
        Assert.That(cinematic[2].Item2, Is.EqualTo(0));
        // LockPlayer (4) added (1), then removed (2)
        Assert.That(property, Is.EqualTo(new List<(byte, byte)> { (1, 4), (2, 4) }));
        Assert.That(field.GetFieldProperty(FieldProperty.LockPlayer), Is.Not.InstanceOf<FieldPropertyLockPlayer>());

        Step(3);
        Assert.That(Sent().Cinematic, Is.Empty);
    }

    // Only what is still open is given back: a key mode only gets its key mode turned off.
    [Test]
    public void KeyModeOnly_TurnsItOff() {
        Setup("<action name=\"set_cinematic_ui\" type=\"1\" />");
        (List<(byte, int)> cinematic, List<(byte, byte)> property) = Sent();
        Assert.That(cinematic, Is.EqualTo(new List<(byte, int)> { (1, 1), (1, 0) }));
        Assert.That(property, Is.Empty);
    }

    // A script that closed its cutscene itself gets nothing more.
    [Test]
    public void ClosedCinematic_SendsNothing() {
        Setup("<action name=\"set_cinematic_ui\" type=\"1\" /><action name=\"set_cinematic_ui\" type=\"3\" /><action name=\"lock_my_pc\" is_lock=\"1\" />"
            + "<action name=\"set_cinematic_ui\" type=\"0\" /><action name=\"set_cinematic_ui\" type=\"2\" /><action name=\"lock_my_pc\" is_lock=\"0\" />");
        (List<(byte, int)> cinematic, List<(byte, byte)> property) = Sent();
        // only the script's own four and two
        Assert.That(cinematic.Select(x => x.Item1), Is.EqualTo(new byte[] { 1, 3, 1, 2 }));
        Assert.That(property, Is.EqualTo(new List<(byte, byte)> { (1, 4), (2, 4) }));
    }

    private const string OpenAndLock = "<action name=\"set_cinematic_ui\" type=\"1\" /><action name=\"set_cinematic_ui\" type=\"3\" />"
        + "<action name=\"lock_my_pc\" is_lock=\"1\" />";

    // A state with no condition and no transition can never be left (KR 52000150's contents_trigger end state
    // keeps the key mode on): 10 s after it was entered the trigger gives the players back, once.
    [Test]
    public void DeadEndState_GivesBackAfterTenSeconds() {
        SetupStates("<state name=\"scene\"><onEnter>" + OpenAndLock + "</onEnter></state>");
        (List<(byte, int)> cinematic, List<(byte, byte)> property) = Sent();
        Assert.That(cinematic.Select(x => x.Item1), Is.EqualTo(new byte[] { 1, 3 }));
        Assert.That(property, Is.EqualTo(new List<(byte, byte)> { (1, 4) }));

        Step(9, 1_000);
        Assert.That(Sent().Cinematic, Is.Empty);

        Step(1, 1_000);
        (cinematic, property) = Sent();
        Assert.That(cinematic, Is.EqualTo(new List<(byte, int)> { (1, 0), (2, -1) }));
        Assert.That(property, Is.EqualTo(new List<(byte, byte)> { (2, 4) }));

        Step(20, 1_000);
        (cinematic, property) = Sent();
        Assert.That(cinematic, Is.Empty);
        Assert.That(property, Is.Empty);
    }

    // A long cutscene whose state still has a condition is never cut, however long it waits.
    [Test]
    public void StateWithCondition_IsNotCut() {
        SetupStates("<state name=\"scene\"><onEnter>" + OpenAndLock + "</onEnter>"
            + "<condition name=\"wait_tick\" wait_tick=\"30000\"><transition state=\"end\" /></condition></state>"
            + "<state name=\"end\"><onEnter><action name=\"set_cinematic_ui\" type=\"0\" /><action name=\"set_cinematic_ui\" type=\"2\" /></onEnter></state>");
        Sent();

        Step(60, 1_000);
        (List<(byte, int)> cinematic, List<(byte, byte)> property) = Sent();
        Assert.That(cinematic, Is.Empty);
        Assert.That(property, Is.Empty);
    }

    // A change another trigger of the field makes after the dead end was entered (here a new cutscene) is left
    // to that trigger.
    [Test]
    public void DeadEndState_LeavesALaterCutsceneAlone() {
        SetupStates("<state name=\"scene\"><onEnter>" + OpenAndLock + "</onEnter></state>");
        Sent();

        Step(5, 1_000);
        trigger.Context.SetCinematicUi(3, string.Empty, false);
        Sent();

        Step(20, 1_000);
        Assert.That(Sent().Cinematic, Is.Empty);
    }

    // A state with no condition that sets a skip state waits for the player's skip key (KR 51000001's intro):
    // the skip moves it on, so nothing is given back.
    [Test]
    public void DeadEndState_WaitingForSkip_IsNotCut() {
        SetupStates("<state name=\"intro\"><onEnter>" + OpenAndLock + "<action name=\"set_skip\" state=\"tutorial\" /></onEnter></state>"
            + "<state name=\"tutorial\"><onEnter><action name=\"set_cinematic_ui\" type=\"0\" /><action name=\"set_cinematic_ui\" type=\"2\" /></onEnter></state>");
        Sent();

        Step(30, 1_000);
        (List<(byte, int)> cinematic, List<(byte, byte)> property) = Sent();
        Assert.That(cinematic, Is.Empty);
        Assert.That(property, Is.Empty);
    }
}
