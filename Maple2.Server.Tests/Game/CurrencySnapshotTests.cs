using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The wallets are changed on several threads (the session's, the field's, another session's trade); a save copies
// them under the wallets' lock (Currency.Lock, which CurrencyManager changes them under), so a stored row never mixes
// balances from before and after one change. Before, the saves read the balances with no lock while CurrencyManager
// held one of its own. The test holds the lock as a change in progress would.
public class CurrencySnapshotTests {
    private static readonly System.TimeSpan Blocked = System.TimeSpan.FromMilliseconds(300);

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Currency wallets => session.Player.Value.Currency;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session.Currency.Meso = 1000;
        session.Currency.Meret = 10;
        test.TakeSent();
    }

    private (long Meso, long Meret) Stored() {
        using var context = new Ms2Context(test.Options);
        long meso = context.Character.AsNoTracking().Single(row => row.Id == session.CharacterId).Currency.Meso;
        long meret = context.Account.AsNoTracking().Single(row => row.Id == session.AccountId).Currency.Meret;
        return (meso, meret);
    }

    [Test]
    public void Snapshot_CopiesEveryBalance() {
        long value = 1;
        FieldInfo[] balances = typeof(Currency).GetFields().Where(field => field.FieldType == typeof(long)).ToArray();
        foreach (FieldInfo field in balances) {
            field.SetValue(wallets, value++);
        }

        Currency copy = wallets.Snapshot();
        Assert.Multiple(() => {
            Assert.That(copy, Is.Not.SameAs(wallets));
            Assert.That(copy.Lock, Is.Not.SameAs(wallets.Lock));
            foreach (FieldInfo field in balances) {
                Assert.That(field.GetValue(copy), Is.EqualTo(field.GetValue(wallets)), field.Name);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ASave_WaitsForAChangeInProgress(bool fullSave) {
        Task<bool> save;
        lock (wallets.Lock) {
            // The first half of a change of two balances (AddUnsent adds mesos and merets in one step).
            wallets.Meso = 5000;
            save = Task.Run(() => {
                using GameStorage.Request db = test.Storage.Context();
                return fullSave ? db.SavePlayer(session.Player.Value) : db.SaveCurrency(session.Player.Value);
            });
            Assert.That(save.Wait(Blocked), Is.False, "the save read the wallets during the change");
            wallets.Meret = 50;
        }

        Assert.That(save.Wait(System.TimeSpan.FromSeconds(30)) && save.Result, Is.True);
        Assert.That(Stored(), Is.EqualTo((5000L, 50L)));
    }

    [Test]
    public void ACurrencyManagerChange_TakesTheWalletsLock() {
        Task change;
        lock (wallets.Lock) {
            change = Task.Run(() => session.Currency.ChangeMeso(500));
            Assert.That(change.Wait(Blocked), Is.False, "the change did not wait for the wallets' lock");
            Assert.That(wallets.Meso, Is.EqualTo(1000));
        }

        Assert.That(change.Wait(System.TimeSpan.FromSeconds(30)), Is.True);
        Assert.That(session.Currency.Meso, Is.EqualTo(1500));
    }
}
