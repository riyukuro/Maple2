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
        test = new TestGameSession();
        test.Start();
        field = test.Session.Field!;
        typeof(FieldManager).GetField("fieldProperties", Any)!.SetValue(field, new ConcurrentDictionary<FieldProperty, IFieldProperty>());
        var document = new XmlDocument();
        document.LoadXml("<ms2><state name=\"scene\"><onEnter>" + enter + "</onEnter>"
            + "<condition name=\"true\"><transition state=\"gone\" /></condition></state></ms2>");
        var cache = new TriggerCache(null!);
        cache.AddReplace(("xblock", "main"), TriggerCache.ParseTrigger("xblock", "main", document));
        typeof(FieldManager).GetField("<TriggerCache>k__BackingField", Any)!.SetValue(field, cache);
        typeof(FieldManager).GetField("fieldTriggers", Any)!.SetValue(field, new ConcurrentDictionary<string, FieldTrigger>());
        typeof(FieldManager).GetField(nameof(FieldManager.UserValues), Any)!.SetValue(field, new TriggerUserValues());
        typeof(MapMetadata).GetField("<XBlock>k__BackingField", Any)!.SetValue(field.Metadata, "xblock");
        trigger = field.AddTrigger(new TriggerModel(1, "main", Vector3.Zero, Vector3.Zero))!;
        tick = Environment.TickCount64;
        test.TakeSent();
        Step(); // enters scene; its condition moves on to the missing state in the same update
    }

    private void Step(int count = 1) {
        for (int i = 0; i < count; i++) {
            tick += 100_000;
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
}
