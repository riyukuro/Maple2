using System;
using System.Collections.Generic;
using System.Linq;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Server.Core.Constants;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Tests.Login;

// The character list's read holds the account's world lock when it can get it, and goes on without it when it cannot:
// the list only reads. A world that is down, refuses the lock or the release, or cannot say whether the login notice
// is on still lets the player see the character list.
public class LoginSessionWorldFailureTests {
    private TestLoginSession test = null!;
    private LoginSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        session = test.Start();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private List<SendOp> List() {
        test.TakeSent();
        session.ListCharacters();
        return test.TakeSentOps();
    }

    [Test]
    public void WorldDownAtTheLock_ListStillSent() {
        test.World.AcquireLockError = new RpcException(new Status(StatusCode.Unavailable, "down"));

        Assert.Multiple(() => {
            Assert.That(List(), Does.Contain(SendOp.CharacterList));
            Assert.That(test.World.LockRequests, Is.EqualTo(1));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // Held by another server's save: three tries, then the list is read without the lock and the miss is logged.
    [Test]
    public void LockHeldElsewhere_ThreeTries_ListStillSent() {
        test.World.AcquireLockErrorText = "held";

        Assert.Multiple(() => {
            Assert.That(List(), Does.Contain(SendOp.CharacterList));
            Assert.That(test.World.LockRequests, Is.EqualTo(3));
            Assert.That(test.Errors, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ReleaseRefused_ListStillSent() {
        test.World.ReleaseLockErrorText = "not held";

        Assert.Multiple(() => {
            Assert.That(List(), Does.Contain(SendOp.CharacterList));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    [TestCase(StatusCode.Unavailable, 0)]
    [TestCase(StatusCode.Internal, 1)]
    public void ReleaseFailed_ListStillSent(StatusCode status, int errors) {
        test.World.ReleaseLockError = new RpcException(new Status(status, "release"));

        Assert.Multiple(() => {
            Assert.That(List(), Does.Contain(SendOp.CharacterList));
            Assert.That(test.Errors, Has.Count.EqualTo(errors));
        });
    }

    // A world that cannot be asked about the login notice: the events go out without it.
    [Test]
    public void NoticeUnknown_EventsWithoutTheNotice() {
        test.World.NoticeLogin = true;
        test.World.AdminError = new RpcException(new Status(StatusCode.Unavailable, "down"));
        test.TakeSent();

        session.SendEvents();

        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { "B200" + "00" + "00000000" }));
            Assert.That(test.World.Admins, Has.Count.EqualTo(1));
        });
    }

    // An account row that is gone (deleted while the login was let in): the read throws, and the handler's catch or the
    // session's read loop ends the request.
    [Test]
    public void AccountGone_ListThrows() {
        using (var context = new Ms2Context(test.Rows.Options)) {
            context.Account.Remove(context.Account.Find(test.AccountId)!);
            context.SaveChanges();
        }

        Assert.Throws<InvalidOperationException>(() => session.ListCharacters());
    }

    // A dispose whose server entry cannot be dropped still ends the session, once.
    [Test]
    public void Dispose_ServerFailure_StillDisposed() {
        TestLoginSession.SetField(session, nameof(LoginSession.Server), null!);

        session.Dispose();
        session.Dispose();

        Assert.That(session.State, Is.EqualTo(SessionState.Disconnected));
    }
}
