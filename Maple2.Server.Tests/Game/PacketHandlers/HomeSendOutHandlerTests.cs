using System;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 49 (send everyone out, no body): only the owner's request in the owner's home moves the visitors.
public class HomeSendOutHandlerTests {
    private TestGameSession ownerTest = null!;
    private TestGameSession visitorTest = null!;
    private GameSession owner => ownerTest.Session;
    private GameSession visitor => visitorTest.Session;

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
        owner.Field!.Players[visitor.Player.ObjectId + 1000] = visitor.Player;
        visitor.Field = owner.Field;
    }

    private void SendOut(TestGameSession from) {
        new RequestCubeHandler { TableMetadata = from.TableMetadata }.Handle(from.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(49);
        }));
    }

    [Test]
    public void TheOwnerSendsTheVisitorOutAndStays() {
        Assert.That(owner.AccountId, Is.Not.EqualTo(visitor.AccountId));

        SendOut(ownerTest);

        Assert.That(visitorTest.World.Moves, Has.Count.EqualTo(1));
        Assert.That(ownerTest.World.Moves, Is.Empty);
    }

    [Test]
    public void AVisitorsRequestMovesNobody() {
        SendOut(visitorTest);

        Assert.That(visitorTest.World.Moves, Is.Empty);
        Assert.That(ownerTest.World.Moves, Is.Empty);
    }
}
