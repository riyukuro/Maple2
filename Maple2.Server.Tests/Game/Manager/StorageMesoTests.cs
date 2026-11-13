using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Model;
using Maple2.Model.Error;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.Manager;

// A meso deposit or withdrawal is stored at once, the storage's mesos with the character's: the storage used to be
// stored only when it closed, so a crash after an autosave kept a withdrawal both in the bag and in the storage, or
// lost a deposit. A move whose save fails is given back.
public class StorageMesoTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private StorageManager storage = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session.Currency.Meso = 5000;
        storage = new StorageManager(session);
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        test.FailSave = null;
        storage.Dispose();
    }

    private (long Bag, long Storage) Stored() {
        using var context = new Ms2Context(test.Options);
        long bag = context.Character.AsNoTracking().Single(character => character.Id == test.CharacterId).Currency.Meso;
        long stored = context.ItemStorage.AsNoTracking().SingleOrDefault(info => info.AccountId == test.AccountId)?.Meso ?? 0;
        return (bag, stored);
    }

    [Test]
    public void Deposit_IsStoredAtOnce() {
        storage.DepositMesos(1200);

        Assert.That(session.Currency.Meso, Is.EqualTo(3800));
        Assert.That(Stored(), Is.EqualTo((3800L, 1200L)));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Withdraw_IsStoredAtOnce() {
        storage.DepositMesos(1200);
        storage.WithdrawMesos(700);

        Assert.That(session.Currency.Meso, Is.EqualTo(4500));
        Assert.That(Stored(), Is.EqualTo((4500L, 500L)));
        Assert.That(test.Errors, Is.Empty);
    }

    // The storage's save fails: the deposit is given back, and the client is shown the storage as it was.
    [Test]
    public void Deposit_NotStored_IsGivenBack() {
        test.FailSave = context => context.ChangeTracker.Entries<ItemStorage>().Any();

        storage.DepositMesos(1200);

        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        Assert.That(test.TakeSent().Select(Convert.ToHexString).TakeLast(2), Is.EqualTo(new[] {
            Convert.ToHexString(StorageInventoryPacket.UpdateMesos(0).ToArray()),
            Convert.ToHexString(StorageInventoryPacket.Error(StorageInventoryError.s_store_err_code).ToArray()),
        }));

        // A later move is stored with the right amounts.
        test.FailSave = null;
        test.Errors.Clear();
        storage.DepositMesos(300);
        Assert.That(Stored(), Is.EqualTo((4700L, 300L)));
    }

    [Test]
    public void Withdraw_NotStored_IsGivenBack() {
        storage.DepositMesos(1200);
        test.FailSave = context => context.ChangeTracker.Entries<ItemStorage>().Any();

        storage.WithdrawMesos(700);

        Assert.That(session.Currency.Meso, Is.EqualTo(3800));
        Assert.That(Convert.ToHexString(test.TakeSent()[^2]), Is.EqualTo(Convert.ToHexString(StorageInventoryPacket.UpdateMesos(1200).ToArray())));
    }
}
