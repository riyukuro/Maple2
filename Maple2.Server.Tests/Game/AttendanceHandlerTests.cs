using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// The Attendance packet (RecvOp.Attendance) while no attend event runs: each mode is answered with nothing and
// changes nothing, whatever the body asks for.
public class AttendanceHandlerTests {
    private TestGameSession test = null!;
    private AttendanceHandler handler = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        handler = new AttendanceHandler { ItemMetadata = test.ItemMetadata };
        test.Session.Currency.ChangeMeso(10_000);
        test.Session.Currency.ChangeMeret(10_000, reportSpent: false);
        test.TakeSent();
    }

    private void Send(byte command, System.Action<Maple2.PacketLib.Tools.ByteWriter>? body = null) {
        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            body?.Invoke(writer);
        }));
    }

    [Test]
    public void Claim_WithoutAnEventSendsNothing() {
        Send(0);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void BeginTimer_WithoutAnEventSendsNothing() {
        Send(1);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void WindowOpened_NeedsNoReply() {
        Send(2);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void MeretPayOpened_NeedsNoReply() {
        Send(3);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void SelectedEvent_NeedsNoReply() {
        Send(8, writer => writer.WriteInt(5));
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void EarlyParticipation_UnknownEventTakesNothing() {
        Send(4, writer => {
            writer.WriteInt(123456);
            writer.WriteLong(0);
        });

        Assert.That(test.TakeSent(), Is.Empty);
        Assert.That(test.Session.Currency.Meso, Is.EqualTo(10_000));
        Assert.That(test.Session.Currency.Meret, Is.EqualTo(10_000));
    }

    [Test]
    public void EarlyParticipation_UnknownEventWithACouponTakesNothing() {
        Send(4, writer => {
            writer.WriteInt(123456);
            writer.WriteLong(777);
        });

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void UnknownCommandIsIgnored() {
        Send(99);
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
