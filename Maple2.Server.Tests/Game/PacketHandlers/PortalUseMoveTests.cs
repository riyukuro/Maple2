using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A portal that leads to another portal of the same map (FieldManager.UsePortal): the player is moved there with
// UserMoveByPortal and gets the portal invincibility; a touch portal puts the player in front of it, an interact
// portal on it. A quest portal goes away once its owner uses it.
public class PortalUseMoveTests : TriggerTestBase {
    private void StartWithPortals() {
        Start();
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", BindingFlags.Instance | BindingFlags.NonPublic)!;
        portals.SetValue(Field, System.Activator.CreateInstance(portals.FieldType));
        // The arrival's invincible window reads the player's field tick.
        typeof(Actor<Player>).GetField("<Field>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(test.Session.Player, Field);
    }

    private FieldPortal Spawn(int id, int targetPortalId, PortalActionType action, Vector3 position, float frontOffset = 0,
            PortalType type = PortalType.Field) {
        return Field.SpawnPortal(new Portal(id, Field.MapId, targetPortalId, type, action, position, Vector3.Zero,
            new Vector3(100, 100, 100), frontOffset, 0, true, true, true), position);
    }

    // The UserMoveByPortal position, less its 25 up.
    private Vector3 MovedTo() {
        ByteReader reader = Sent(SendOp.UserMoveByPortal).Single();
        Assert.That(reader.ReadInt(), Is.EqualTo(test.Session.Player.ObjectId));
        return reader.Read<Vector3>() - new Vector3(0, 0, 25);
    }

    [Test]
    public void ToAnInteractPortal_MovesOntoIt() {
        StartWithPortals();
        Spawn(1, 2, PortalActionType.Interact, Vector3.Zero);
        Spawn(2, 1, PortalActionType.Interact, new Vector3(900, 300, 0));
        test.TakeSent();

        Assert.That(Field.UsePortal(test.Session, 1, ""), Is.True);

        Assert.That(MovedTo(), Is.EqualTo(new Vector3(900, 300, 0)));
    }

    // In front of a touch portal by its front offset, or by a block without one, so the player does not stand in it.
    [TestCase(0f, 150f)]
    [TestCase(400f, 400f)]
    public void ToATouchPortal_MovesInFrontOfIt(float frontOffset, float distance) {
        StartWithPortals();
        Spawn(1, 2, PortalActionType.Interact, Vector3.Zero);
        Spawn(2, 1, PortalActionType.Touch, new Vector3(900, 300, 0), frontOffset);
        test.TakeSent();

        Assert.That(Field.UsePortal(test.Session, 1, ""), Is.True);

        Assert.That(Vector3.Distance(MovedTo(), new Vector3(900, 300, 0)), Is.EqualTo(distance).Within(0.01f));
    }

    // A target portal the field lacks: the use is taken but the player stays.
    [Test]
    public void ToAMissingPortal_StaysWhereItIs() {
        StartWithPortals();
        Spawn(1, 9, PortalActionType.Interact, Vector3.Zero);
        test.TakeSent();

        Assert.That(Field.UsePortal(test.Session, 1, ""), Is.True);

        Assert.That(Sent(SendOp.UserMoveByPortal), Is.Empty);
    }

    // A quest portal is used once: its owner is told it is gone, then moved.
    [Test]
    public void AQuestPortal_IsRemovedForItsOwner() {
        StartWithPortals();
        var portal = new FieldQuestPortal(test.Session.Player, Field, 900, new Portal(1, Field.MapId, 2, PortalType.Quest,
            PortalActionType.Interact, Vector3.Zero, Vector3.Zero, new Vector3(100, 100, 100), 0, 0, true, false, true));
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((System.Collections.IDictionary) portals.GetValue(Field)!)[portal.ObjectId] = portal;
        Spawn(2, 1, PortalActionType.Interact, new Vector3(900, 300, 0));
        test.TakeSent();

        Assert.That(Field.UsePortal(test.Session, 1, ""), Is.True);

        System.Collections.Generic.List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Does.Contain(SendOp.FieldPortal).And.Contain(SendOp.UserMoveByPortal));
        Assert.That(Field.TryGetPortal(1, out _), Is.False);
    }
}
