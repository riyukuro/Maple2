using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The partner who reserved a hall pays for it when the other one confirms: the payer's merets and the hall row are
// stored together, through the payer's own session, or not at all. The merets used to be taken from the payer's
// session without being stored (a crash gave them back), and kept when the hall row was not stored. The stored
// merets of a failed hall are checked on MySQL only (MS2_TEST_GAME_DB): the in-memory store has no transactions, so
// the currency saved before the hall's failed save stays stored there.
[TestFixture(false)]
[TestFixture(true)]
public class WeddingReservationPayTests(bool mysql) {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int CouponId = 20300999;
    private const int Grade3CouponId = 20300998;
    private const int Cost = 300;

    private TestGameSession payer = null!;
    private TestGameSession confirmer = null!;
    private DbContextOptions options = null!;
    private readonly HallFault fault = new();
    private Item grade3Coupon = null!;
    private readonly WeddingHandler handler = new();

    [SetUp]
    public void SetUp() {
        fault.Fail = false;
        if (mysql) {
            if (Connection == null) {
                Assert.Ignore("the MySQL fixture needs a real database: set MS2_TEST_GAME_DB");
            }
            options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).AddInterceptors(fault).Options;
        } else {
            options = new DbContextOptionsBuilder()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(fault)
                .Options;
        }
        payer = new TestGameSession(options, Gender.Male);
        confirmer = new TestGameSession(options, Gender.Female);
        using (GameStorage.Request db = payer.Storage.Context()) {
            Assume.That(db.CreateBuddy(payer.CharacterId, confirmer.CharacterId, BuddyType.Default), Is.Not.Null);
            Assume.That(db.CreateBuddy(confirmer.CharacterId, payer.CharacterId, BuddyType.Default), Is.Not.Null);
        }
        payer.Store(TestGameSession.Metadata(Constant.ProposalItemId));
        confirmer.Register(TestGameSession.Metadata(Constant.ProposalItemId));
        ItemMetadata coupon = TestGameSession.Metadata(CouponId);
        payer.Store(coupon with { Property = coupon.Property with { Tag = ItemTag.WeddingHallCoupon_Grade1 } });
        ItemMetadata grade3 = TestGameSession.Metadata(Grade3CouponId);
        grade3Coupon = payer.Store(grade3 with { Property = grade3.Property with { Tag = ItemTag.WeddingHallCoupon_Grade3 } });
        var hall = new WeddingPackage.HallData(1, 2000023, 2000023, 1, Cost, [], []);
        var table = new WeddingTable(new Dictionary<MarriageExpType, WeddingReward>(),
            new Dictionary<int, WeddingPackage> {
                [1] = new WeddingPackage(1, 0, new Dictionary<int, WeddingPackage.HallData> { [1] = hall }),
                [3] = new WeddingPackage(3, 0, new Dictionary<int, WeddingPackage.HallData> { [1] = hall }),
            });
        foreach (TestGameSession test in new[] { payer, confirmer }) {
            test.World.AddPlayer(payer.AccountId, payer.CharacterId, "payer");
            test.World.AddPlayer(confirmer.AccountId, confirmer.CharacterId, "confirmer");
            test.SetTable(table);
            test.AddMap(TestGameSession.Map(2000023));
            test.Start();
            test.EnterMap(TestGameSession.Map(2000023));
        }
        FieldManager field = payer.Session.Field!;
        field.Players[1001] = payer.Session.Player;
        field.Players[1002] = confirmer.Session.Player;
        confirmer.Session.Field = field;

        // Engaged: the payer proposes and the confirmer accepts.
        handler.Handle(payer.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteUnicodeString(confirmer.Session.PlayerName);
        }));
        handler.Handle(confirmer.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteShort(1);
            writer.WriteLong(payer.CharacterId);
        }));
        Assume.That(payer.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Engaged));

        payer.Session.Currency.Meret = 1000;
        Assume.That(payer.Session.SavePurchase(), Is.True);
        payer.TakeSent();
        confirmer.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        fault.Fail = false;
        if (!mysql || Connection == null || payer == null) {
            return;
        }
        using var context = new Ms2Context(options);
        long marriageId = payer.Session.Marriage.Marriage.Id;
        long[] characters = [payer.CharacterId, confirmer.CharacterId];
        context.WeddingHall.Where(hall => hall.MarriageId == marriageId).ExecuteDelete();
        context.Marriage.Where(marriage => marriage.Id == marriageId).ExecuteDelete();
        context.Mail.Where(mail => characters.Contains(mail.ReceiverId)).ExecuteDelete();
        context.Item.Where(item => characters.Contains(item.OwnerId)).ExecuteDelete();
        context.Buddy.Where(buddy => characters.Contains(buddy.OwnerId)).ExecuteDelete();
    }

    // The sessions share these options, so the sessions' own save faults are not on them: this one fails a save
    // that adds a hall row, as a database that refuses it would.
    private sealed class HallFault : SaveChangesInterceptor {
        public bool Fail;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            if (Fail && eventData.Context != null && eventData.Context.ChangeTracker.Entries<Maple2.Database.Model.WeddingHall>()
                    .Any(entry => entry.State == EntityState.Added)) {
                throw new DbUpdateException("The test made this save fail");
            }
            return result;
        }
    }

    private WeddingHall Reservation(int packageId = 1) => new() {
        Id = 0,
        MarriageId = payer.Session.Marriage.Marriage.Id,
        PackageId = packageId,
        PackageHallId = 1,
        ReserverAccountId = payer.AccountId,
        ReserverCharacterId = payer.CharacterId,
        ReserverName = payer.Session.PlayerName,
        PartnerName = confirmer.Session.PlayerName,
    };

    private void FailHallSave() => fault.Fail = true;

    private long StoredMeret() {
        using var context = new Ms2Context(options);
        return context.Account.AsNoTracking().Single(account => account.Id == payer.AccountId).Currency.Meret;
    }

    private int StoredHalls() {
        using var context = new Ms2Context(options);
        long marriageId = payer.Session.Marriage.Marriage.Id;
        return context.WeddingHall.Count(hall => hall.MarriageId == marriageId);
    }

    private int Coupons() => payer.Session.Item.Inventory.Find(CouponId).Sum(item => item.Amount);

    // The payer's stored coupon rows (a used-up coupon's row goes to owner 0).
    private int StoredCoupons() {
        using var context = new Ms2Context(options);
        return context.Item.Where(item => item.OwnerId == payer.CharacterId && item.ItemId == CouponId).Sum(item => item.Amount);
    }

    [Test]
    public void Confirm_StoresTheMeretsWithTheHall() {
        Assume.That(payer.Session.Marriage.ReserveHall(Reservation()), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));

        Assert.That(confirmer.Session.Marriage.ConfirmReservation(), Is.EqualTo(WeddingError.s_wedding_propose_ok));
        Assert.Multiple(() => {
            Assert.That(payer.Session.Currency.Meret, Is.EqualTo(1000 - Cost));
            Assert.That(StoredMeret(), Is.EqualTo(1000 - Cost));
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(payer.Session.Marriage.WeddingHall.Id, Is.Not.EqualTo(0));
            Assert.That(Coupons(), Is.EqualTo(1));
        });
    }

    // The hall of a ceremony whose time has passed, stored for this marriage and held by both partners.
    private void HoldPassedHall() {
        WeddingHall passed = Reservation();
        passed.CeremonyTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600;
        WeddingHall? held;
        using (GameStorage.Request db = payer.Storage.Context()) {
            held = db.CreateWeddingHall(passed, payer.Session.Marriage.Marriage);
        }
        Assume.That(held, Is.Not.Null);
        payer.Session.Marriage.WeddingHall = held!;
        confirmer.Session.Marriage.WeddingHall = held!;
    }

    // A new reservation after a passed ceremony replaces the passed hall's row with the new one, paid once.
    [Test]
    public void Confirm_AfterAPassedCeremony_ReplacesTheRow() {
        HoldPassedHall();
        long passedId = payer.Session.Marriage.WeddingHall.Id;
        Assume.That(payer.Session.Marriage.ReserveHall(Reservation()), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));

        Assert.That(confirmer.Session.Marriage.ConfirmReservation(), Is.EqualTo(WeddingError.s_wedding_propose_ok));
        Assert.Multiple(() => {
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(payer.Session.Marriage.WeddingHall.Id, Is.Not.EqualTo(0).And.Not.EqualTo(passedId));
            Assert.That(StoredMeret(), Is.EqualTo(1000 - Cost));
        });
    }

    // The new row is not stored: the passed hall's row stays (on MySQL the delete is rolled back with the add).
    [Test]
    public void HallNotStored_AfterAPassedCeremony_KeepsThePassedRow() {
        if (!mysql) {
            Assert.Ignore("the in-memory store has no transactions");
        }
        HoldPassedHall();
        Assume.That(payer.Session.Marriage.ReserveHall(Reservation()), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        FailHallSave();

        Assert.That(confirmer.Session.Marriage.ConfirmReservation(), Is.Not.EqualTo(WeddingError.s_wedding_propose_ok));
        Assert.Multiple(() => {
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(StoredMeret(), Is.EqualTo(1000));
        });
    }

    // The hall row is not stored: no merets are taken, in the session or stored, and no hall is set.
    [Test]
    public void HallNotStored_TakesNoMerets() {
        Assume.That(payer.Session.Marriage.ReserveHall(Reservation()), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        FailHallSave();

        Assert.That(confirmer.Session.Marriage.ConfirmReservation(), Is.EqualTo(WeddingError.s_wedding_result_err_system));
        Assert.Multiple(() => {
            Assert.That(payer.Session.Currency.Meret, Is.EqualTo(1000));
            if (mysql) {
                Assert.That(StoredMeret(), Is.EqualTo(1000));
            }
            Assert.That(StoredHalls(), Is.EqualTo(0));
            Assert.That(payer.Session.Marriage.WeddingHall?.Id ?? 0, Is.EqualTo(0));
        });

        // The same reservation confirmed again, now stored, is paid once.
        fault.Fail = false;
        Assert.That(confirmer.Session.Marriage.ConfirmReservation(), Is.EqualTo(WeddingError.s_wedding_propose_ok));
        Assert.That((payer.Session.Currency.Meret, StoredMeret()), Is.EqualTo((1000L - Cost, 1000L - Cost)));
    }

    // A coupon reservation whose hall is not stored keeps its coupon; one that is stored uses it up and no merets.
    [Test]
    public void Coupon_UsedOnlyWhenTheHallIsStored() {
        Assume.That(payer.Session.Marriage.ReserveHall(Reservation(), useCoupon: true), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        FailHallSave();

        Assert.That(confirmer.Session.Marriage.ConfirmReservation(), Is.EqualTo(WeddingError.s_wedding_result_err_system));
        Assert.That(Coupons(), Is.EqualTo(1));
        // The coupon's row is written in the hall's transaction: refused with the hall, it stays stored (MySQL only,
        // the in-memory store has no transactions).
        if (mysql) {
            Assert.That(StoredCoupons(), Is.EqualTo(1));
        }

        fault.Fail = false;
        Assert.That(confirmer.Session.Marriage.ConfirmReservation(), Is.EqualTo(WeddingError.s_wedding_propose_ok));
        Assert.Multiple(() => {
            Assert.That(Coupons(), Is.EqualTo(0));
            Assert.That(StoredCoupons(), Is.EqualTo(0));
            Assert.That((payer.Session.Currency.Meret, StoredMeret()), Is.EqualTo((1000L, 1000L)));
            Assert.That(StoredHalls(), Is.EqualTo(1));
        });
    }

    // A reservation refused because the partner is away (0x22) leaves nothing behind: the coupon it picked used
    // to be kept on the one who reserved and paid the ask still open instead of its merets.
    [Test]
    public void Coupon_PickedByAReservationWithoutThePartner_IsNotUsed() {
        Assume.That(payer.Session.Marriage.ReserveHall(Reservation()), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        Assume.That(payer.Session.Field!.Players.TryRemove(1002, out FieldPlayer? away), Is.True);
        Assume.That(payer.Session.Marriage.ReserveHall(Reservation(), useCoupon: true), Is.EqualTo(WeddingError.s_wedding_result_err_same_field));
        payer.Session.Field.Players[1002] = away!;

        Assert.That(confirmer.Session.Marriage.ConfirmReservation(), Is.EqualTo(WeddingError.s_wedding_propose_ok));
        Assert.Multiple(() => {
            Assert.That(Coupons(), Is.EqualTo(1));
            Assert.That(payer.Session.Currency.Meret, Is.EqualTo(1000 - Cost));
        });
    }

    // The coupon is checked against the hall's grade when the hall is paid. A grade-3 ask whose coupon is gone was
    // paid with the grade-1 coupon a later reservation picked (and its cancel mailed back a grade-3 coupon).
    [Test]
    public void Coupon_OfAnotherGrade_DoesNotPayTheHall() {
        Assume.That(payer.Session.Marriage.ReserveHall(Reservation(packageId: 3), useCoupon: true), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        Assume.That(payer.Session.Item.Inventory.Consume(grade3Coupon.Uid, 1), Is.True);
        Assume.That(payer.Session.Field!.Players.TryRemove(1002, out FieldPlayer? away), Is.True);
        Assume.That(payer.Session.Marriage.ReserveHall(Reservation(), useCoupon: true), Is.EqualTo(WeddingError.s_wedding_result_err_same_field));
        payer.Session.Field.Players[1002] = away!;

        Assert.That(confirmer.Session.Marriage.ConfirmReservation(), Is.EqualTo(WeddingError.s_wedding_result_lack_coupon));
        Assert.Multiple(() => {
            Assert.That(StoredHalls(), Is.EqualTo(0));
            Assert.That(Coupons(), Is.EqualTo(1));
            Assert.That(payer.Session.Currency.Meret, Is.EqualTo(1000));
        });
    }
}
