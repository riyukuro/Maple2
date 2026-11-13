using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 49 (send everyone out, no body): only the owner's request in the owner's home tells the visitors to leave
// (ResponseCube 0x1a) and sends them out ten seconds later.
public class HomeSendOutHandlerTests {
    private TestGameSession ownerTest = null!;
    private TestGameSession visitorTest = null!;
    private GameSession owner => ownerTest.Session;
    private GameSession visitor => visitorTest.Session;
    private int VisitorEntry => visitor.Player.ObjectId + 1000;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        ownerTest = new TestGameSession(options);
        visitorTest = new TestGameSession(options);
        ownerTest.Start();
        visitorTest.Start();
        ownerTest.EnterHome();
        // The visitor stands in the owner's home.
        visitorTest.EnterMap(TestGameSession.Map(Constant.DefaultHomeMapId));
        visitor.Player.Value.Character.MapId = Constant.DefaultHomeMapId;
        owner.Field!.Players[VisitorEntry] = visitor.Player;
        visitor.Field = owner.Field;
    }

    private void SendOut(TestGameSession from) {
        new RequestCubeHandler { TableMetadata = from.TableMetadata }.Handle(from.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(49);
        }));
    }

    [Test]
    public void TheOwnerTellsTheVisitorToLeaveAndStays() {
        Assert.That(owner.AccountId, Is.Not.EqualTo(visitor.AccountId));
        visitorTest.TakeSent();
        ownerTest.TakeSent();
        int timers = owner.Field!.Scheduler.Count;

        SendOut(ownerTest);

        List<byte[]> sent = visitorTest.TakeSent().Where(p => TestGameSession.Op(p) == SendOp.ResponseCube).ToList();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0], Is.EqualTo(new byte[] { (byte) SendOp.ResponseCube, (byte) ((int) SendOp.ResponseCube >> 8), 0x1a }));
        Assert.That(ownerTest.TakeSentOps(), Does.Not.Contain(SendOp.ResponseCube));
        // Not yet: the box gives ten seconds.
        Assert.That(visitorTest.World.Moves, Is.Empty);
        Assert.That(owner.Field.Scheduler.Count, Is.EqualTo(timers + 1));
        Assert.That(ownerTest.World.Moves, Is.Empty);
    }

    [Test]
    public void TheTimerSendsOutTheVisitorStillThere() {
        RequestCubeHandler.SendOutStillThere(owner.Field!, new Dictionary<int, FieldPlayer> { [VisitorEntry] = visitor.Player });

        Assert.That(visitorTest.World.Moves, Has.Count.EqualTo(1));
        Assert.That(ownerTest.World.Moves, Is.Empty);
    }

    // Exit before the timer, or left and came back as a new field entry: the old timer moves nobody.
    [Test]
    public void TheTimerSkipsAVisitorThatLeft() {
        var told = new Dictionary<int, FieldPlayer> { [VisitorEntry] = visitor.Player };
        owner.Field!.Players.TryRemove(VisitorEntry, out _);

        RequestCubeHandler.SendOutStillThere(owner.Field, told);

        Assert.That(visitorTest.World.Moves, Is.Empty);
    }

    [Test]
    public void AVisitorsRequestMovesNobody() {
        SendOut(visitorTest);

        Assert.That(visitorTest.World.Moves, Is.Empty);
        Assert.That(ownerTest.World.Moves, Is.Empty);
    }
}
