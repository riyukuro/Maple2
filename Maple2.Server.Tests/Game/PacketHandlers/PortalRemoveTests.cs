using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// FieldManager.RemovePortal: a portal of the field goes once and its removal is broadcast; an unknown one is false
// and sends nothing. The portal and its target are made up.
public class PortalRemoveTests : TriggerTestBase {
    private FieldPortal StartWithPortal() {
        Start();
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", BindingFlags.Instance | BindingFlags.NonPublic)!;
        portals.SetValue(Field, System.Activator.CreateInstance(portals.FieldType));
        FieldPortal portal = Field.SpawnPortal(new Portal(2, 2000901, 1, PortalType.Field, PortalActionType.Interact, Vector3.Zero, Vector3.Zero,
            new Vector3(100, 100, 100), 0, 0, true, true, true));
        test.TakeSent();
        return portal;
    }

    [Test]
    public void ARemovedPortal_GoesOnceAndTheRemovalIsBroadcast() {
        FieldPortal portal = StartWithPortal();

        Assert.That(Field.RemovePortal(portal.ObjectId), Is.True);
        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
        Assert.That(Field.TryGetPortal(2, out _), Is.False);

        Assert.That(Field.RemovePortal(portal.ObjectId), Is.False);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void AnUnknownPortal_IsFalseWithNoPacket() {
        StartWithPortal();

        Assert.That(Field.RemovePortal(12345), Is.False);
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
