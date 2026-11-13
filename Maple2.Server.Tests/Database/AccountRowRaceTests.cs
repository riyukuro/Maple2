using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Database;

// The World's machine id write at an account's first login (UpdateMachineId) and the game entry's online and channel
// write (LoadPlayer) store the whole account row as they read it. Another write to the row between the read and the
// save is kept: the row version (LastModified) refuses the stale row, so the machine id is stored at a later login and
// the entry fails, instead of the row read before being written back. The other write is made from a second
// connection just before the save; the row version is MySQL's (ON UPDATE CURRENT_TIMESTAMP): the tests need
// MS2_TEST_GAME_DB.
public class AccountRowRaceTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private sealed class Racer : SaveChangesInterceptor {
        public Action? Once;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            Action? write = Once;
            Once = null;
            write?.Invoke();
            return result;
        }
    }

    private readonly Racer racer = new();
    private DbContextOptions options = null!;
    private DbContextOptions plain = null!;
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the row version is MySQL's: set MS2_TEST_GAME_DB");
        }
        options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).AddInterceptors(racer).Options;
        plain = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).Options;
        test = new TestGameSession(options);
        SetLevels(1);
    }

    [TearDown]
    public void TearDown() => racer.Once = null;

    private void SetLevels(int levels) {
        using var context = new Ms2Context(plain);
        context.Database.ExecuteSqlRaw("UPDATE `account` SET `PrestigeLevelsGained` = {0} WHERE `Id` = {1}", levels, test.AccountId);
    }

    private Maple2.Database.Model.Account Stored() {
        using var context = new Ms2Context(plain);
        return context.Account.AsNoTracking().Single(account => account.Id == test.AccountId);
    }

    [Test]
    public void MachineId_OtherWriteBetween_IsKept() {
        Guid before = Stored().MachineId;
        racer.Once = () => SetLevels(7);
        bool stored;
        using (GameStorage.Request db = test.Storage.Context()) {
            stored = db.UpdateMachineId(test.AccountId, Guid.NewGuid());
        }
        Assert.Multiple(() => {
            Assert.That(stored, Is.False);
            Assert.That(Stored().PrestigeLevelsGained, Is.EqualTo(7));
            Assert.That(Stored().MachineId, Is.EqualTo(before));
        });
    }

    [Test]
    public void LoadPlayer_OtherWriteBetween_IsKept() {
        racer.Once = () => SetLevels(7);
        Assert.Throws<DbUpdateConcurrencyException>(() => {
            using GameStorage.Request db = test.Storage.Context();
            db.LoadPlayer(test.AccountId, test.CharacterId, 1, 1);
        });
        Assert.That(Stored().PrestigeLevelsGained, Is.EqualTo(7));
    }

    // No other write: both store as before.
    [Test]
    public void NoOtherWrite_Stores() {
        Guid machineId = Guid.NewGuid();
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.UpdateMachineId(test.AccountId, machineId), Is.True);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.LoadPlayer(test.AccountId, test.CharacterId, 1, 1), Is.Not.Null);
        }
        Assert.Multiple(() => {
            Assert.That(Stored().MachineId, Is.EqualTo(machineId));
            Assert.That(Stored().Online, Is.True);
            Assert.That(Stored().PrestigeLevelsGained, Is.EqualTo(1));
        });
    }
}
