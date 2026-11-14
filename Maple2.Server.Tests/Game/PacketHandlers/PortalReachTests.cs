using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A portal use (RequestMoveField 0) is refused with no answer unless the character is in the portal's box: the client
// uses only a portal whose box holds it (PortalAction::vfunc_1, actionkeyobject.cpp:860). The server took any portal
// of the field from anywhere. The portal and its target field are made up.
public class PortalReachTests : TriggerTestBase {
    private FieldManager StartWithPortal(Vector3 player) {
        Start();
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", BindingFlags.Instance | BindingFlags.NonPublic)!;
        portals.SetValue(Field, System.Activator.CreateInstance(portals.FieldType));
        // The portal is locked, so a use that gets past the box check is answered with the password notice.
        FieldPortal portal = Field.SpawnPortal(new Portal(2, 2000901, 1, PortalType.Field, PortalActionType.Interact, new Vector3(1000, 0, 0),
            Vector3.Zero, new Vector3(100, 100, 100), 0, 0, true, true, true));
        portal.Password = "1234";
        test.Session.Player.Position = player;
        test.TakeSent();
        FieldManager field = Field;
        new MoveFieldHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteInt(field.MapId);
            writer.WriteInt(2);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteUnicodeString("");
            writer.WriteUnicodeString("");
        }));
        return field;
    }

    [Test]
    public void APortalFarAway_IsRefusedWithNoAnswer() {
        FieldManager field = StartWithPortal(Vector3.Zero);
        Assert.Multiple(() => {
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(test.Session.Field, Is.SameAs(field));
        });
    }

    [Test]
    public void APortalAtThePlayer_IsUsed() {
        FieldManager field = StartWithPortal(new Vector3(1020, 0, 0));
        byte[] notice = NoticePacket.Message(StringCode.s_home_password_mismatch, NoticePacket.Flags.Alert).ToArray();
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(notice) }));
    }

    // The box is half the size each way plus the slack, turned by the Z rotation; the offset moves it in world space.
    [Test]
    public void InBox_TurnsByZAndMovesByTheOffset() {
        Vector3 size = new(400, 100, 100);
        Assert.Multiple(() => {
            Assert.That(PortalRules.InBox(new Vector3(340, 0, 0), Vector3.Zero, Vector3.Zero, size, Vector3.Zero, 150), Is.True);
            Assert.That(PortalRules.InBox(new Vector3(0, 340, 0), Vector3.Zero, Vector3.Zero, size, Vector3.Zero, 150), Is.False);
            Assert.That(PortalRules.InBox(new Vector3(0, 340, 0), Vector3.Zero, new Vector3(0, 0, 90), size, Vector3.Zero, 150), Is.True);
            Assert.That(PortalRules.InBox(new Vector3(0, 0, 400), Vector3.Zero, Vector3.Zero, size, Vector3.Zero, 150), Is.False);
            Assert.That(PortalRules.InBox(new Vector3(1000, 0, 0), Vector3.Zero, Vector3.Zero, size, new Vector3(1000, 0, 0), 150), Is.True);
            // A server-made portal has no offset, so the box at the position alone holds too.
            Assert.That(PortalRules.InBox(Vector3.Zero, Vector3.Zero, Vector3.Zero, size, new Vector3(1000, 0, 0), 150), Is.True);
        });
    }
}
