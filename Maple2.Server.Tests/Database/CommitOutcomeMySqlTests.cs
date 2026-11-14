using System;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// A purchase save whose commit throws on MySQL: the store mark written in its transaction is read again to learn
// whether the commit landed. Landed: the purchase is kept (no refund). Not landed: it failed and its undo runs. The
// mark cannot be read: the result is unknown, nothing is refunded, the session is cut and every later store refused,
// so the next login loads what the database holds.
// Runs only when MS2_TEST_GAME_DB holds the connection string of a scratch MySQL game database.
public class CommitOutcomeMySqlTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private DbContextOptions plain = null!;
    private IDisposable? held;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("a commit that throws after landing needs a real database: set MS2_TEST_GAME_DB");
        }
        ServerVersion version = ServerVersion.AutoDetect(Connection);
        plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
        test = new TestGameSession(new DbContextOptionsBuilder().UseMySql(Connection, version).Options);
        held = GameStorage.HoldOwners(test.AccountId, test.CharacterId);
        test.Start();
        session.Currency.Meso = 5000;
        Assume.That(session.SavePurchase(), Is.True, "the wallet stored before the purchase");
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        DatabaseRequest<Ms2Context>.CommitFault = null;
        held?.Dispose();
        held = null;
    }

    private long StoredMeso() {
        using var context = new Ms2Context(plain);
        return context.Character.AsNoTracking().Single(row => row.Id == test.CharacterId).Currency.Meso;
    }

    private bool Ended() =>
        (int) typeof(Maple2.Server.Core.Network.Session).GetField("disconnecting", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)! == 1;

    private bool Buy() {
        session.Currency.Meso -= 1000;
        return session.TrySavePurchase(_ => true, () => session.Currency.Meso += 1000);
    }

    private static void ThrowOnCommit(bool afterIt) {
        DatabaseRequest<Ms2Context>.CommitFault = after => {
            if (after == afterIt) {
                DatabaseRequest<Ms2Context>.CommitFault = null;
                throw new InvalidOperationException("the commit's answer was lost");
            }
        };
    }

    [Test]
    public void CommitThrowsAfterLanding_IsKept() {
        ThrowOnCommit(afterIt: true);

        Assert.That(Buy(), Is.True);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(4000));
            Assert.That(StoredMeso(), Is.EqualTo(4000));
            Assert.That(Ended(), Is.False);
        });
        session.Currency.Meso = 3000;
        Assert.That(session.SavePurchase(), Is.True, "the session stores on");
        Assert.That(StoredMeso(), Is.EqualTo(3000));
    }

    [Test]
    public void CommitThrowsBeforeLanding_IsRefunded() {
        ThrowOnCommit(afterIt: false);

        Assert.That(Buy(), Is.False);

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
            Assert.That(StoredMeso(), Is.EqualTo(5000));
            Assert.That(Ended(), Is.False);
        });
        session.Currency.Meso = 3000;
        Assert.That(session.SavePurchase(), Is.True, "the session stores on");
        Assert.That(StoredMeso(), Is.EqualTo(3000));
    }

    // The mark cannot be read: no refund to memory that the database may not hold, the session is cut, and no later
    // store writes over what the database holds.
    [Test]
    public void CommitThrowsAndTheMarkCannotBeRead_IsUnknown() {
        ThrowOnCommit(afterIt: true);
        session.MarkReadFault = () => throw new InvalidOperationException("the mark cannot be read");

        Assert.That(Buy(), Is.False);

        Assert.Multiple(() => {
            Assert.That(StoredMeso(), Is.EqualTo(4000));
            Assert.That(Ended(), Is.True);
        });
        session.Currency.Meso = 9000;
        Assert.That(session.SavePurchase(), Is.False, "a store after an unknown one is refused");
        Assert.That(session.SessionSave(), Is.False, "a session save after an unknown store is refused");
        Assert.That(StoredMeso(), Is.EqualTo(4000));
    }
}
