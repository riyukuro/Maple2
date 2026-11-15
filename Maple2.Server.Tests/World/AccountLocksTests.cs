using System;
using System.Threading;
using System.Threading.Tasks;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// The World's account lock (AccountLocks): a take returns a token the release must name. Before, a release removed
// whatever lock the account had and an expired lock was removed and added again in two steps, so a load or save that
// ran past the 30 s expiry freed the lock of the holder that took it over, and two takes of one expired lock could
// both succeed: two servers wrote the same item rows at once.
public class AccountLocksTests {
    private const long Account = 7;
    private DateTime now;
    private AccountLocks locks = null!;

    [SetUp]
    public void Fresh() {
        now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        locks = new AccountLocks(() => now);
    }

    [Test]
    public void AHeldLock_IsRefused_UntilItExpires() {
        long first = locks.Take(Account);
        now += AccountLocks.Timeout;
        long beforeExpiry = locks.Take(Account);
        now += TimeSpan.FromMilliseconds(1);
        long afterExpiry = locks.Take(Account);

        Assert.Multiple(() => {
            Assert.That(first, Is.Not.Zero);
            Assert.That(beforeExpiry, Is.Zero);
            Assert.That(afterExpiry, Is.Not.Zero.And.Not.EqualTo(first));
        });
    }

    [Test]
    public void TheLateReleaseOfAnExpiredHolder_KeepsTheNewHoldersLock() {
        long late = locks.Take(Account);
        now += AccountLocks.Timeout + TimeSpan.FromSeconds(1);
        long current = locks.Take(Account);

        bool lateFreed = locks.Release(Account, late);
        long third = locks.Take(Account);

        Assert.Multiple(() => {
            Assert.That(lateFreed, Is.False);
            Assert.That(third, Is.Zero, "the lock is still the new holder's");
            Assert.That(locks.Release(Account, current), Is.True);
            Assert.That(locks.Take(Account), Is.Not.Zero, "free once its holder gave it back");
        });
    }

    [Test]
    public void AReleaseWithoutAToken_FreesNothing() {
        locks.Take(Account);
        Assert.Multiple(() => {
            Assert.That(locks.Release(Account, 0), Is.False);
            Assert.That(locks.Take(Account), Is.Zero);
        });
    }

    // Many takes of one expired lock at once: one of them gets it.
    [Test]
    public void RacingTakesOfAnExpiredLock_GiveItToOne() {
        for (int round = 0; round < 200; round++) {
            locks.Take(Account);
            now += AccountLocks.Timeout + TimeSpan.FromSeconds(1);

            int won = 0;
            using var start = new Barrier(8);
            Task[] takers = new Task[8];
            for (int i = 0; i < takers.Length; i++) {
                takers[i] = Task.Factory.StartNew(() => {
                    start.SignalAndWait();
                    if (locks.Take(Account) != 0) {
                        Interlocked.Increment(ref won);
                    }
                }, TaskCreationOptions.LongRunning);
            }
            Task.WaitAll(takers);

            Assert.That(won, Is.EqualTo(1), $"round {round}");
            now += AccountLocks.Timeout + TimeSpan.FromSeconds(1);
        }
    }
}
