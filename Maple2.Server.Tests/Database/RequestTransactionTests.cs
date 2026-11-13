using System;
using System.Collections.Generic;
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
    private readonly List<long> itemUids = [];
    private const int ItemId = 50000102;

    [SetUp]
    public void SetUp() {
        if (Connection == null) {
            Assert.Ignore("transactions need a real database: set MS2_TEST_GAME_DB");
        }
        hook.Before = null;
        itemUids.Clear();
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ItemId));
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
        context.Item.Where(item => itemUids.Contains(item.Id)).ExecuteDelete();
        context.BlackMarketListing.Where(listing => listing.CharacterId == character).ExecuteDelete();
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

    // An item of the character's, in a row of its own.
    private Item CreateItem() {
        using GameStorage.Request db = storage.Context();
        Item? item = db.CreateItem(character, new Item(TestGameSession.Metadata(ItemId)));
        Assert.That(item, Is.Not.Null);
        itemUids.Add(item!.Uid);
        return item;
    }

    private long ItemOwner(long uid) {
        using var check = new Ms2Context(plain);
        return check.Item.Where(item => item.Id == uid).Select(item => item.OwnerId).Single();
    }

    // The attachment's save fails: the mail is not sent (it used to be sent without the item), and the item stays
    // where it was.
    [Test]
    public void MailWhoseItemSaveFailsIsNotSent() {
        Item item = CreateItem();
        hook.Before = command => {
            if (command.CommandText.Contains("UPDATE `item`")) {
                throw new InvalidOperationException("update refused");
            }
        };

        using (GameStorage.Request db = storage.Context()) {
            var mail = new Mail { ReceiverId = account };
            mail.Items.Add(item);
            Assert.That(db.CreateMail(mail), Is.Null);
        }
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(Mails(account), Is.Empty);
            Assert.That(ItemOwner(item.Uid), Is.EqualTo(character));
        });
    }

    // Mails sent one after the other on one request (the inventory's overflow mails): a mail that failed leaves no
    // pending change behind, so the next mail's save does not move the failed mail's item to the mail that was
    // rolled back.
    [Test]
    public void FailedMailLeavesNothingForTheNextSave() {
        Item first = CreateItem();
        Item second = CreateItem();
        bool refuse = true;
        hook.Before = command => {
            if (refuse && command.CommandText.Contains("UPDATE `item`")) {
                refuse = false;
                throw new InvalidOperationException("update refused");
            }
        };

        Mail? sent;
        using (GameStorage.Request db = storage.Context()) {
            var failed = new Mail { ReceiverId = account };
            failed.Items.Add(first);
            Assert.That(db.CreateMail(failed), Is.Null);
            var next = new Mail { ReceiverId = account };
            next.Items.Add(second);
            sent = db.CreateMail(next);
        }
        hook.Before = null;

        Assert.That(sent, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(Mails(account), Is.EqualTo(new[] { sent!.Id }));
            Assert.That(ItemOwner(first.Uid), Is.EqualTo(character));
            Assert.That(ItemOwner(second.Uid), Is.EqualTo(sent!.Id));
        });
    }

    private long[] Listings() {
        using var check = new Ms2Context(plain);
        return check.BlackMarketListing.Where(listing => listing.CharacterId == character).Select(listing => listing.Id).ToArray();
    }

    private BlackMarketListing? List(Item item) {
        using GameStorage.Request db = storage.Context();
        return db.CreateBlackMarketingListing(new BlackMarketListing(item) {
            AccountId = account, CharacterId = character, Deposit = 100, Price = 1000, Quantity = 1,
            ExpiryTime = DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds(),
        });
    }

    // A listing is its row and its item moved to it, written together.
    [Test]
    public void ListingStoresTheRowAndMovesTheItem() {
        Item item = CreateItem();

        BlackMarketListing? listing = List(item);

        Assert.That(listing, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(Listings(), Is.EqualTo(new[] { listing!.Id }));
            Assert.That(ItemOwner(item.Uid), Is.EqualTo(listing!.Id));
        });
    }

    // The item's move fails: no listing row is left (the seller gets the item and the deposit back by mail, and a
    // row left behind was loaded by World and could be bought, moving the refunded item to the buyer).
    [Test]
    public void ListingWhoseItemSaveFailsLeavesNoRow() {
        Item item = CreateItem();
        hook.Before = command => {
            if (command.CommandText.Contains("UPDATE `item`")) {
                throw new InvalidOperationException("update refused");
            }
        };

        Assert.That(List(item), Is.Null);
        hook.Before = null;

        Assert.Multiple(() => {
            Assert.That(Listings(), Is.Empty);
            Assert.That(ItemOwner(item.Uid), Is.EqualTo(character));
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
