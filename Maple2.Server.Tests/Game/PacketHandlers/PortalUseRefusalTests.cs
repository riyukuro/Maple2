using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The refusals of a portal use (FieldManager.UsePortal) that leave the player where it is: a portal the field does
// not have, a wrong password (answered with the alert notice), and the dungeon lobby portal outside a dungeon. The
// portals and the target field are made up.
public class PortalUseRefusalTests : TriggerTestBase {
    private const string TestPortalCode = "test-code";
    private const string WrongPortalCode = "wrong-code";

    private FieldPortal StartWithPortal(PortalType type) {
        Start();
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", BindingFlags.Instance | BindingFlags.NonPublic)!;
        portals.SetValue(Field, System.Activator.CreateInstance(portals.FieldType));
        FieldPortal portal = Field.SpawnPortal(new Portal(2, 2000901, 1, type, PortalActionType.Interact, Vector3.Zero, Vector3.Zero,
            new Vector3(100, 100, 100), 0, 0, true, true, true));
        test.TakeSent();
        return portal;
    }

    [Test]
    public void APortalTheFieldLacks_IsRefusedWithNoAnswer() {
        StartWithPortal(PortalType.Field);

        Assert.That(Field.UsePortal(test.Session, 77, ""), Is.False);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void AWrongPassword_IsAnsweredWithTheNoticeOnly() {
        FieldPortal portal = StartWithPortal(PortalType.Field);
        portal.Password = TestPortalCode;

        Assert.That(Field.UsePortal(test.Session, 2, WrongPortalCode), Is.False);

        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
        Assert.That(test.Session.Field, Is.SameAs(Field));
    }

    [Test]
    public void TheDungeonLobbyPortalOutsideADungeon_IsRefused() {
        StartWithPortal(PortalType.DungeonReturnToLobby);

        Assert.That(Field.UsePortal(test.Session, 2, ""), Is.False);
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
