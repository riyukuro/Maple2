using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A portal use (RequestMoveField 0) of a portal whose enabled flag is off is refused with no answer: the client never
// uses one (actionkeyobject.cpp:887, mouseinteract.cpp:406) and waits for no answer. The server sent a debug message
// box in English. The trigger is KR field 02000296's (main.xml): its portal 2 shows but is off until the trigger
// turns it on. The target field is made up.
public class DisabledPortalTests : TriggerTestBase {
    private FieldPortal StartWithPortal() {
        Start();
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", BindingFlags.Instance | BindingFlags.NonPublic)!;
        portals.SetValue(Field, System.Activator.CreateInstance(portals.FieldType));
        FieldPortal portal = Field.SpawnPortal(new Portal(2, 2000901, 1, PortalType.Field, PortalActionType.Interact, Vector3.Zero, Vector3.Zero,
            new Vector3(100, 100, 100), 0, 0, true, true, true));
        Action("""<action name="set_portal" portal_id="2" visible="1" enable="0" minimap_visible="1" />""");
        test.TakeSent();
        return portal;
    }

    [Test]
    public void ADisabledPortal_IsRefusedWithNoAnswer() {
        FieldPortal portal = StartWithPortal();
        FieldManager field = Field;
        Assert.That((portal.Visible, portal.Enabled), Is.EqualTo((true, false)));

        new MoveFieldHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteInt(field.MapId);
            writer.WriteInt(2);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteUnicodeString("");
            writer.WriteUnicodeString("");
        }));

        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(test.Session.Field, Is.SameAs(field));
        });
    }

    [Test]
    public void UsePortal_OfADisabledPortal_IsFalse() {
        StartWithPortal();
        Assert.Multiple(() => {
            Assert.That(Field.UsePortal(test.Session, 2, ""), Is.False);
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }
}
