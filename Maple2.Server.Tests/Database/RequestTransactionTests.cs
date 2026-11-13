using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// A request's transaction that is not committed is rolled back when the request is disposed, so a call that fails half
// way (a failed save or an exception) leaves none of its writes. The in-memory store has no transactions: the tests
// need MS2_TEST_GAME_DB, the connection string of a scratch MySQL game database. Mail rows name their receiver by id
// alone, so the ids here are ones no character or account has.
public class RequestTransactionTests {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");

    private TestGameSession test = null!;
    private GameStorage storage = null!;
    private DbContextOptions plain = null!;
    private readonly CommandHook hook = new();
    private long account;
    private long character;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("transactions need a real database: set MS2_TEST_GAME_DB");
        }
        hook.Before = null;
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MailExpiryDays = 30 });
        ServerVersion version = ServerVersion.AutoDetect(Connection);
        plain = new DbContextOptionsBuilder().UseMySql(Connection, version).Options;
        DbContextOptions options = new DbContextOptionsBuilder().UseMySql(Connection, version).AddInterceptors(hook).Options;
        object items = typeof(GameStorage).GetField("itemMetadata", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.Storage)!;
        storage = new GameStorage(options, (ItemMetadataStorage) items, test.MapMetadata, null!, null!, test.TableMetadata,
            test.ServerTableMetadata, NullLogger<GameStorage>.Instance, null!);
        account = 7_400_000_000_000 + Random.Shared.NextInt64(1_000_000_000) * 10;
        character = account + 1;
    }

    [TearDown]
    public void TearDown() {
        if (Connection == null) {
            return;
        }
        hook.Before = null;
        using var context = new Ms2Context(plain);
        context.Mail.Where(mail => mail.ReceiverId == account || mail.ReceiverId == character).ExecuteDelete();
    }

    private long[] Mails(long receiverId) {
        using var check = new Ms2Context(plain);
        return check.Mail.Where(mail => mail.ReceiverId == receiverId).Select(mail => mail.Id).OrderBy(id => id).ToArray();
    }

    [Test]
    public void DisposeRollsBackAnUncommittedTransaction() {
        using (GameStorage.Request db = storage.Context()) {
            db.BeginTransaction();
            Assert.That(db.CreateMail(new Mail { ReceiverId = account }), Is.Not.Null);
        }

        Assert.That(Mails(account), Is.Empty);
    }

    [Test]
    public void CommittedWritesStay() {
        using (GameStorage.Request db = storage.Context()) {
            db.BeginTransaction();
            Assert.That(db.CreateMail(new Mail { ReceiverId = account }), Is.Not.Null);
            Assert.That(db.Commit(), Is.True);
        }

        Assert.That(Mails(account), Has.Length.EqualTo(1));
    }

    // Binding an account's mails to its first character deletes them and adds them again under the character. When the
    // second save throws, the deletion is undone: the account keeps its mails (they used to be lost, with their items).
    [Test]
    public void BindAccountMailsThatFailsHalfWayKeepsTheMails() {
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.CreateMail(new Mail { ReceiverId = account }), Is.Not.Null);
            Assert.That(db.CreateMail(new Mail { ReceiverId = account }), Is.Not.Null);
        }
        long[] before = Mails(account);
        hook.Before = command => {
            if (command.CommandText.Contains("INSERT INTO `mail`")) {
                throw new InvalidOperationException("insert refused");
            }
        };

        Assert.Catch(() => {
            using GameStorage.Request db = storage.Context();
            db.BindAccountMailsToCharacter(account, character);
        });
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(Mails(account), Is.EqualTo(before));
            Assert.That(Mails(character), Is.Empty);
        });
    }

    private sealed class CommandHook : DbCommandInterceptor {
        public Action<DbCommand>? Before;

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) {
            Before?.Invoke(command);
            return result;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
                                                                          InterceptionResult<DbDataReader> result) {
            Before?.Invoke(command);
            return result;
        }
    }
}
