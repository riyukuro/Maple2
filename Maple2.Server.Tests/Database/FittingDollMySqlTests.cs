using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Maple2.Server.Tests.Database;

// A plot save that drops a dressed mannequin, on MySQL: the doll rows move by a conditional update in the save's
// transaction, which the in-memory store cannot show. Runs only when MS2_TEST_GAME_DB holds the connection string
// of a scratch MySQL game database. Another connection changes or locks a row while the save runs: a row that left
// the cube is not mailed, and a row lock that outlasts the lock wait ends the save with null and nothing written.
public class FittingDollMySqlTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int ItemId = 20000101;
    private const int CubeItemId = 50000101;

    private TestGameSession test = null!;
    private GameStorage storage = null!;
    private DbContextOptions options = null!; // the save's own options, with the hooks
    private DbContextOptions plain = null!; // the other connection
    private readonly SaveHook hook = new();
    private readonly ErrorLog errors = new();
    private long saver;
    private long plotId;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("the conditional doll row move needs a real database: set MS2_TEST_GAME_DB");
        }
        errors.Lines.Clear();
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ItemId), TestGameSession.Metadata(CubeItemId));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MailExpiryDays = 30 });
        ServerVersion version = ServerVersion.AutoDetect(Connection);
        plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
        options = new DbContextOptionsBuilder().UseMySql(Connection, version).AddInterceptors(hook, new ShortLockWait()).Options;
        object items = typeof(GameStorage).GetField("itemMetadata", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.Storage)!;
        storage = new GameStorage(options, (ItemMetadataStorage) items, null!, null!, null!, test.TableMetadata, test.ServerTableMetadata,
            errors, null!);

        // An id no character has: the mails are found, and removed, by it.
        saver = 7_100_000_000_000 + Random.Shared.NextInt64(1_000_000_000) * 10;
        using var context = new Ms2Context(plain);
        var map = new Maple2.Database.Model.UgcMap { OwnerId = saver, MapId = Constant.DefaultHomeMapId, Indoor = true, Number = 1, Name = "doll" };
        context.UgcMap.Add(map);
        context.SaveChanges();
        plotId = map.Id;
    }

    [TearDown]
    public void TearDown() {
        if (Connection == null) {
            return;
        }
        hook.OnSaving = null;
        using var context = new Ms2Context(plain);
        long[] mails = context.Mail.Where(mail => mail.ReceiverId == saver).Select(mail => mail.Id).ToArray();
        long[] cubes = context.UgcMapCube.Where(cube => cube.UgcMapId == plotId).Select(cube => cube.Id).ToArray();
        context.Item.Where(item => item.ItemId == ItemId && (mails.Contains(item.OwnerId) || cubes.Contains(item.OwnerId) || item.OwnerId == saver)).ExecuteDelete();
        context.Mail.Where(mail => mail.ReceiverId == saver).ExecuteDelete();
        context.UgcMapCube.Where(cube => cube.UgcMapId == plotId).ExecuteDelete();
        context.UgcMap.Where(map => map.Id == plotId).ExecuteDelete();
    }

    private PlotInfo Plot() {
        var plot = (PlotInfo) RuntimeHelpers.GetUninitializedObject(typeof(PlotInfo));
        plot.Id = plotId;
        return plot;
    }

    private PlotCube PlaceCube(int x) {
        var cube = new PlotCube(TestGameSession.Metadata(CubeItemId)) { Position = new Vector3B(x, 2, 3), Type = PlotCube.CubeType.Construction };
        using GameStorage.Request db = storage.Context();
        return db.CreateCube(Plot(), cube) ?? throw new InvalidOperationException("cube row not created");
    }

    private long OnDoll(long cubeUid, EquipSlot slot) {
        var item = new Item(TestGameSession.Metadata(ItemId)) { Group = ItemGroup.FittingDoll, Slot = (short) slot };
        using GameStorage.Request db = storage.Context();
        return db.CreateItem(cubeUid, item)?.Uid ?? throw new InvalidOperationException("item row not created");
    }

    private Maple2.Database.Model.Item Row(long uid) {
        using var check = new Ms2Context(plain);
        return check.Item.AsNoTracking().Single(row => row.Id == uid);
    }

    private List<Maple2.Database.Model.Mail> Mails() {
        using var check = new Ms2Context(plain);
        return check.Mail.AsNoTracking().Where(mail => mail.ReceiverId == saver).ToList();
    }

    private bool CubeExists(long uid) {
        using var check = new Ms2Context(plain);
        return check.UgcMapCube.AsNoTracking().Any(cube => cube.Id == uid);
    }

    private static bool AddsMails(DbContext context) =>
        context.ChangeTracker.Entries<Maple2.Database.Model.Mail>().Any(entry => entry.State == EntityState.Added);

    // Both rows are mailed in the save's transaction; a row another channel took off the doll after the save read
    // the doll's rows (here: while the mails are written) moves 0 rows, so its mail is dropped and it stays where
    // the other channel put it.
    [Test]
    public void SaveCubes_MovesEachRow_AndSkipsARowThatLeftTheCube() {
        PlotCube kept = PlaceCube(1);
        PlotCube doll = PlaceCube(2);
        long mailedRow = OnDoll(doll.Id, EquipSlot.CL);
        long takenOff = OnDoll(doll.Id, EquipSlot.PA);
        hook.OnSaving = context => {
            if (!AddsMails(context)) return;
            using var other = new Ms2Context(plain);
            int moved = other.Item.Where(item => item.Id == takenOff && item.Group == ItemGroup.FittingDoll)
                .ExecuteUpdate(update => update.SetProperty(item => item.OwnerId, saver).SetProperty(item => item.Group, ItemGroup.Default));
            Assert.That(moved, Is.EqualTo(1));
        };

        var mailed = new List<(long CharacterId, long MailId)>();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveCubes(Plot(), [kept], saver, mailed), Is.Not.Null, string.Join("\n", errors.Lines));
        }

        List<Maple2.Database.Model.Mail> mails = Mails();
        Assert.Multiple(() => {
            Assert.That(CubeExists(doll.Id), Is.False);
            Assert.That(CubeExists(kept.Id), Is.True);
            Assert.That(mails, Has.Count.EqualTo(1));
            Assert.That(mailed, Is.EqualTo(new[] { (saver, mails[0].Id) }));
            Assert.That((Row(mailedRow).OwnerId, Row(mailedRow).Group, Row(mailedRow).Slot), Is.EqualTo((mails[0].Id, ItemGroup.Default, (short) -1)));
            Assert.That((Row(takenOff).OwnerId, Row(takenOff).Group), Is.EqualTo((saver, ItemGroup.Default)));
        });
    }

    // A row lock held by another connection outlasts the lock wait during the move: the database error ends the
    // save with null, and the transaction takes back the cube delete and the mails already written.
    [Test]
    public void SaveCubes_LockTimeout_RollsBackTheMailsAndTheCubeDelete() {
        PlotCube kept = PlaceCube(1);
        PlotCube doll = PlaceCube(2);
        long locked = OnDoll(doll.Id, EquipSlot.CL);
        long other = OnDoll(doll.Id, EquipSlot.PA);
        using var holder = new Ms2Context(plain);
        using var hold = holder.Database.BeginTransaction();
        holder.Database.ExecuteSqlRaw("SELECT Id FROM `item` WHERE Id = {0} FOR UPDATE", locked);
        bool mailsWritten = false;
        hook.OnSaving = context => mailsWritten |= AddsMails(context);

        var mailed = new List<(long CharacterId, long MailId)>();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveCubes(Plot(), [kept], saver, mailed), Is.Null);
        }
        hold.Rollback();

        Assert.Multiple(() => {
            Assert.That(mailsWritten, Is.True);
            Assert.That(mailed, Is.Empty);
            Assert.That(Mails(), Is.Empty);
            Assert.That(CubeExists(doll.Id), Is.True);
            foreach (long uid in new[] { locked, other }) {
                Assert.That((Row(uid).OwnerId, Row(uid).Group), Is.EqualTo((doll.Id, ItemGroup.FittingDoll)));
            }
        });
    }

    // The cube save itself fails: it comes before the mails, so nothing is written.
    [Test]
    public void SaveCubes_FailedCubeSave_WritesNothing() {
        PlotCube kept = PlaceCube(1);
        PlotCube doll = PlaceCube(2);
        long uid = OnDoll(doll.Id, EquipSlot.CL);
        hook.OnSaving = context => {
            if (context.ChangeTracker.Entries<Maple2.Database.Model.UgcMapCube>().Any(entry => entry.State == EntityState.Deleted)) {
                throw new DbUpdateException("The test made this save fail");
            }
        };

        var mailed = new List<(long CharacterId, long MailId)>();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveCubes(Plot(), [kept], saver, mailed), Is.Null);
        }

        Assert.Multiple(() => {
            Assert.That(mailed, Is.Empty);
            Assert.That(Mails(), Is.Empty);
            Assert.That(CubeExists(doll.Id), Is.True);
            Assert.That((Row(uid).OwnerId, Row(uid).Group), Is.EqualTo((doll.Id, ItemGroup.FittingDoll)));
        });
    }

    // FittingDoll 9 deletes a doll row by one conditional delete: a row on the cube goes, a row another channel took
    // off first deletes 0 rows and stays.
    [Test]
    public void DeleteFittingDollItem_DeletesOnlyARowStillOnTheCube() {
        PlotCube doll = PlaceCube(2);
        long onDoll = OnDoll(doll.Id, EquipSlot.CL);
        long takenOff = OnDoll(doll.Id, EquipSlot.PA);
        using (var other = new Ms2Context(plain)) {
            other.Item.Where(item => item.Id == takenOff)
                .ExecuteUpdate(update => update.SetProperty(item => item.OwnerId, saver).SetProperty(item => item.Group, ItemGroup.Default));
        }

        bool deleted, notOnCube;
        using (GameStorage.Request db = storage.Context()) {
            deleted = db.DeleteFittingDollItem(onDoll, doll.Id);
            notOnCube = db.DeleteFittingDollItem(takenOff, doll.Id);
        }

        using var check = new Ms2Context(plain);
        Assert.Multiple(() => {
            Assert.That(deleted, Is.True);
            Assert.That(notOnCube, Is.False);
            Assert.That(check.Item.Any(row => row.Id == onDoll), Is.False);
            Assert.That((Row(takenOff).OwnerId, Row(takenOff).Group), Is.EqualTo((saver, ItemGroup.Default)));
        });
    }

    // The storage's logged errors, with their exception.
    private sealed class ErrorLog : ILogger<GameStorage> {
        public readonly List<string> Lines = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            if (IsEnabled(logLevel)) {
                Lines.Add(formatter(state, exception) + (exception == null ? "" : ": " + exception.GetType().Name + " " + exception.Message));
            }
        }
    }

    private sealed class SaveHook : SaveChangesInterceptor {
        public Action<DbContext>? OnSaving;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            if (eventData.Context != null) {
                OnSaving?.Invoke(eventData.Context);
            }
            return result;
        }
    }

    // A lock wait of 2 s instead of the server's 50 s, on the save's own connections only.
    private sealed class ShortLockWait : DbConnectionInterceptor {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SET SESSION innodb_lock_wait_timeout = 2";
            command.ExecuteNonQuery();
        }
    }
}
