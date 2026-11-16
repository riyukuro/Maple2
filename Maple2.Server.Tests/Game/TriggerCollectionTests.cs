using System;
using System.Collections;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// The map's trigger objects the field sends a player on entry (TriggerPacket.Load): every actor, camera, cube,
// effect, ladder, mesh, rope, sound and agent, by kind in that order. The boxes are the server's own (where a
// trigger looks for players) and are not sent.
public class TriggerCollectionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static TriggerCollection CollectionOf(params Ms2Trigger[] triggers) {
        var entities = (MapEntityMetadata) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        typeof(MapEntityMetadata).GetField("<Trigger>k__BackingField", Any)!.SetValue(entities, new TriggerStorage(triggers.ToList()));
        return new TriggerCollection(entities);
    }

    private static TriggerCollection EveryKind() => CollectionOf(
        new Ms2TriggerAgent(9, true),
        new Ms2TriggerSound(8, true),
        new Ms2TriggerRope(7, true),
        new Ms2TriggerMesh(1f, 6, true, false),
        new Ms2TriggerLadder(5, true),
        new Ms2TriggerEffect(4, true),
        new Ms2TriggerCube(3, true),
        new Ms2TriggerCamera(2, true),
        new Ms2TriggerActor("Idle_A", 1, true),
        new Ms2TriggerBox(Vector3.Zero, new Vector3(100, 100, 100), 10, true));

    [Test]
    public void EveryObjectButTheBoxes_IsSentByKind() {
        TriggerCollection triggers = EveryKind();

        Assert.That(triggers.Count, Is.EqualTo(9));
        Assert.That(triggers.Select(trigger => trigger.Id), Is.EqualTo(Enumerable.Range(1, 9)));
        Assert.That(triggers.Boxes.Keys, Is.EqualTo(new[] { 10 }));
        Assert.That(((IEnumerable) triggers).Cast<ITriggerObject>().Count(), Is.EqualTo(9));
    }

    // The load packet: the count, then each object.
    [Test]
    public void TheLoadPacket_CarriesTheCountAndEachObject() {
        TriggerCollection triggers = EveryKind();

        var packet = new ByteReader(TriggerPacket.Load(triggers).ToArray(), 0);
        packet.ReadShort();
        packet.ReadByte();

        Assert.That(packet.ReadInt(), Is.EqualTo(9));
        Assert.That(packet.ReadInt(), Is.EqualTo(1), "the actor first");
    }

    [Test]
    public void AMapWithNoTriggerObject_SendsNone() {
        TriggerCollection triggers = CollectionOf();

        Assert.That(triggers.Count, Is.EqualTo(0));
        Assert.That(triggers, Is.Empty);
    }
}
