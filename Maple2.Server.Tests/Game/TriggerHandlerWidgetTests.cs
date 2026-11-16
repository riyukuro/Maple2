using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// The Trigger packet's widget update (command 9) for the guide widget, a type the server has no handler for, and
// the cube script commands that read their arguments and do nothing.
public class TriggerHandlerWidgetTests {
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        typeof(FieldManager).GetField(nameof(FieldManager.Widgets))!.SetValue(test.Session.Field, new Dictionary<string, Widget>());
        test.TakeSent();
    }

    private void Send(params byte[] bytes) =>
        new TriggerHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            foreach (byte b in bytes) {
                writer.WriteByte(b);
            }
        }));

    private void SendWidget(byte type, int arg) =>
        new TriggerHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(9);
            writer.WriteByte(type);
            writer.WriteInt(arg);
        }));

    [Test]
    public void TheGuideWidget_TakesTheTriggerEventFlag() {
        var guide = new Widget(test.Session.Field!);
        test.Session.Field!.Widgets["Guide"] = guide;

        SendWidget((byte) WidgetType.Guide, 1);

        Assert.That(guide.Conditions["IsTriggerEvent"], Is.EqualTo(1));
    }

    [Test]
    public void TheGuideWithoutAWidget_ChangesNothing() {
        SendWidget((byte) WidgetType.Guide, 1);

        Assert.That(test.Session.Field!.Widgets, Is.Empty);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void AnUnhandledWidgetType_IsDropped() {
        Assert.DoesNotThrow(() => SendWidget(99, 5));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void TheCubeScriptCommands_DoNothing() {
        // Load (11), save (13) and discard (22) read a cube id (and the save an xml string) and send nothing.
        Assert.DoesNotThrow(() => Send(11, 1, 0, 0, 0));
        Assert.DoesNotThrow(() => Send(22, 1, 0, 0, 0));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    // The trigger editor's save (13) as UITriggerEditorDialog writes it: the cube id, then the script as an A-string
    // (under 10000 characters). It is read whole and answered with nothing; the server keeps no cube scripts.
    [Test]
    public void TheCubeScriptSave_IsReadWhole() {
        var packet = TestGameSession.Packet(writer => {
            writer.WriteByte(13);
            writer.WriteInt(1048577);
            writer.WriteString("<ms2><state name=\"a\" /></ms2>");
        });

        Assert.DoesNotThrow(() => new TriggerHandler().Handle(test.Session, packet));
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
