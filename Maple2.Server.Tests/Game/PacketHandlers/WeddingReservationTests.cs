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
using Maple2.Server.Game.PacketHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A new hall reservation (Wedding 0x0D) and the partner's answer (0x0E): the partner who reserved asks the other
// one, only the asked partner's yes (7) stores and pays the hall, once, a no (8) ends the ask, and the client's own
// tests before the bill (WeddingManager::CanVisit) are repeated: the promise limit (0x29) and a ceremony at least
// WeddingHallModifyLimitHour away (0x36).
public class WeddingReservationTests {
    private const int Cost = 300;

    private TestGameSession proposer = null!;
    private TestGameSession accepter = null!;
    private DbContextOptions options = null!;
    private readonly WeddingHandler handler = new();

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [SetUp]
    public void SetUp() {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        proposer = new TestGameSession(options, Gender.Male);
        accepter = new TestGameSession(options, Gender.Female);
        using (GameStorage.Request db = proposer.Storage.Context()) {
            Assume.That(db.CreateBuddy(proposer.CharacterId, accepter.CharacterId, BuddyType.Default), Is.Not.Null);
            Assume.That(db.CreateBuddy(accepter.CharacterId, proposer.CharacterId, BuddyType.Default), Is.Not.Null);
        }
        proposer.Store(TestGameSession.Metadata(Constant.ProposalItemId));
        accepter.Register(TestGameSession.Metadata(Constant.ProposalItemId));
        var hall = new WeddingPackage.HallData(1, 2000023, 2000023, 1, Cost, [], []);
        var table = new WeddingTable(new Dictionary<MarriageExpType, WeddingReward>(),
            new Dictionary<int, WeddingPackage> { [1] = new WeddingPackage(1, 0, new Dictionary<int, WeddingPackage.HallData> { [1] = hall }) });
        foreach (TestGameSession test in new[] { proposer, accepter }) {
            test.World.AddPlayer(proposer.AccountId, proposer.CharacterId, "proposer");
            test.World.AddPlayer(accepter.AccountId, accepter.CharacterId, "accepter");
            test.SetTable(table);
            test.AddMap(TestGameSession.Map(2000023));
            test.Start();
            test.EnterMap(TestGameSession.Map(2000023));
        }
        FieldManager field = proposer.Session.Field!;
        field.Players[1001] = proposer.Session.Player;
        field.Players[1002] = accepter.Session.Player;
        accepter.Session.Field = field;

        handler.Handle(proposer.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteUnicodeString(accepter.Session.PlayerName);
        }));
        handler.Handle(accepter.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteShort(1);
            writer.WriteLong(proposer.CharacterId);
        }));
        Assume.That(proposer.Session.Marriage.Marriage.Status, Is.EqualTo(MaritalStatus.Engaged));

        foreach (TestGameSession test in new[] { proposer, accepter }) {
            test.Session.Currency.Meret = 1000;
            Assume.That(test.Session.SavePurchase(), Is.True);
            test.TakeSent();
        }
    }

    private WeddingHall Reservation(TestGameSession by, long ceremonyTime) => new() {
        Id = 0,
        MarriageId = by.Session.Marriage.Marriage.Id,
        PackageId = 1,
        PackageHallId = 1,
        CeremonyTime = ceremonyTime,
        ReserverAccountId = by.AccountId,
        ReserverCharacterId = by.CharacterId,
        ReserverName = by.Session.PlayerName,
        PartnerName = (by == proposer ? accepter : proposer).Session.PlayerName,
    };

    private void Answer(TestGameSession session, short answer) {
        handler.Handle(session.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0xe);
            writer.WriteShort(answer);
        }));
    }

    private static List<byte> Modes(TestGameSession session) {
        return session.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Wedding).Select(packet => packet[2]).ToList();
    }

    private int StoredHalls() {
        using var context = new Ms2Context(options);
        long marriageId = proposer.Session.Marriage.Marriage.Id;
        return context.WeddingHall.Count(hall => hall.MarriageId == marriageId);
    }

    // Either partner may reserve; the other one is asked and the one who reserved pays.
    [Test]
    public void Reserve_ByEitherPartner_AsksTheOther() {
        Assert.That(accepter.Session.Marriage.ReserveHall(Reservation(accepter, 0)), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        Assert.Multiple(() => {
            Assert.That(Modes(accepter), Is.Empty);
            Assert.That(Modes(proposer), Is.EqualTo(new List<byte> { 0xd }));
        });

        Answer(proposer, 7);
        Assert.Multiple(() => {
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(accepter.Session.Currency.Meret, Is.EqualTo(1000 - Cost));
            Assert.That(proposer.Session.Currency.Meret, Is.EqualTo(1000));
        });
    }

    // The one who reserved cannot answer their own ask: that stored the hall on the other partner's merets.
    [Test]
    public void Reserve_TheReserversOwnYes_IsRefused() {
        Assume.That(proposer.Session.Marriage.ReserveHall(Reservation(proposer, 0)), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        Answer(proposer, 7);
        Assert.Multiple(() => {
            Assert.That(StoredHalls(), Is.EqualTo(0));
            Assert.That(proposer.Session.Marriage.WeddingHall?.Id ?? 0, Is.EqualTo(0));
            Assert.That(accepter.Session.Currency.Meret, Is.EqualTo(1000));
            Assert.That(proposer.Session.Currency.Meret, Is.EqualTo(1000));
        });
    }

    // A second yes stored and paid the hall again.
    [Test]
    public void Reserve_ASecondYes_DoesNotPayAgain() {
        Assume.That(proposer.Session.Marriage.ReserveHall(Reservation(proposer, 0)), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        Answer(accepter, 7);
        Assume.That(StoredHalls(), Is.EqualTo(1));
        Answer(accepter, 7);
        Assert.Multiple(() => {
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(proposer.Session.Currency.Meret, Is.EqualTo(1000 - Cost));
        });
    }

    [Test]
    public void Reserve_ANo_EndsTheAsk() {
        Assume.That(proposer.Session.Marriage.ReserveHall(Reservation(proposer, 0)), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        Answer(accepter, 8);
        Answer(accepter, 7);
        Assert.Multiple(() => {
            Assert.That(StoredHalls(), Is.EqualTo(0));
            Assert.That(proposer.Session.Currency.Meret, Is.EqualTo(1000));
        });
    }

    // The box answers 7 or 8 only; another answer was sent back as a result of its own.
    [Test]
    public void Reserve_AnotherAnswer_SendsNothing() {
        Assume.That(proposer.Session.Marriage.ReserveHall(Reservation(proposer, 0)), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        accepter.TakeSent();
        Answer(accepter, 0x25);
        Assert.Multiple(() => {
            Assert.That(Modes(accepter), Is.Empty);
            Assert.That(StoredHalls(), Is.EqualTo(0));
        });
    }

    [Test]
    public void Reserve_AfterThePromiseLimit_Is0x29() {
        proposer.Session.Marriage.Marriage.CreationTime = Now - 8 * 86400L;
        Assert.That(proposer.Session.Marriage.ReserveHall(Reservation(proposer, Now + 86400)), Is.EqualTo(WeddingError.s_wedding_result_late_promise_time));
        Assert.That(Modes(accepter), Is.Empty);
    }

    [Test]
    public void Reserve_InsideTheModifyLimitHours_Is0x36() {
        Assert.That(proposer.Session.Marriage.ReserveHall(Reservation(proposer, Now + 3600)), Is.EqualTo(WeddingError.s_wedding_result_early_wedding_reserve));
        Assert.That(Modes(accepter), Is.Empty);
    }

    // A hall already stored for this marriage, held by both partners as on login.
    private void Hold(long ceremonyTime) {
        WeddingHall? held;
        using (GameStorage.Request db = proposer.Storage.Context()) {
            held = db.CreateWeddingHall(Reservation(proposer, ceremonyTime), proposer.Session.Marriage.Marriage);
        }
        Assume.That(held, Is.Not.Null);
        proposer.Session.Marriage.WeddingHall = held!;
        accepter.Session.Marriage.WeddingHall = held!;
    }

    // The client refuses a new reservation only while the reserved ceremony is ahead (WeddingProxy::CheckVisit);
    // a couple still inside the promise limit whose ceremony time has passed reserves anew, and the new hall
    // takes the old one's place.
    [Test]
    public void Reserve_AfterAPassedCeremony_StoresTheNewHall() {
        Hold(Now - 3600);
        Assert.That(proposer.Session.Marriage.ReserveHall(Reservation(proposer, Now + 86400)), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        Answer(accepter, 7);
        Assert.Multiple(() => {
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(proposer.Session.Marriage.WeddingHall.CeremonyTime, Is.EqualTo(Now + 86400).Within(5));
            Assert.That(proposer.Session.Currency.Meret, Is.EqualTo(1000 - Cost));
        });
    }

    [Test]
    public void Reserve_WhileTheCeremonyIsAhead_Is0x2b() {
        Hold(Now + 2 * 86400);
        Assert.That(proposer.Session.Marriage.ReserveHall(Reservation(proposer, Now + 86400)), Is.EqualTo(WeddingError.s_wedding_result_already_reservation));
        Assert.Multiple(() => {
            Assert.That(Modes(accepter), Is.Empty);
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(proposer.Session.Currency.Meret, Is.EqualTo(1000));
        });
    }

    // The store refuses a second row while the first one's ceremony is ahead, whoever calls it.
    [Test]
    public void CreateWeddingHall_WhileTheCeremonyIsAhead_StoresNothing() {
        Hold(Now + 2 * 86400);
        using GameStorage.Request db = proposer.Storage.Context();
        Assert.That(db.CreateWeddingHall(Reservation(proposer, Now + 86400), proposer.Session.Marriage.Marriage), Is.Null);
        Assert.That(StoredHalls(), Is.EqualTo(1));
    }

    [Test]
    public void Reserve_ADayAhead_IsAsked() {
        Assert.That(proposer.Session.Marriage.ReserveHall(Reservation(proposer, Now + 86400)), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        Assert.That(Modes(accepter), Is.EqualTo(new List<byte> { 0xd }));
    }

    // The reservation's tests are run again at the yes: a time inside the limit hours by then (0x36), or a promise
    // limit passed while the box was open (0x29), stores and pays nothing, and the ask is gone.
    [Test]
    public void Confirm_InsideTheModifyLimitHoursByThen_Is0x36() {
        Assume.That(proposer.Session.Marriage.ReserveHall(Reservation(proposer, Now + 86400)), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        accepter.Session.Marriage.UnconfirmedReservation!.CeremonyTime = Now + 3600;

        Assert.That(accepter.Session.Marriage.ConfirmReservation(), Is.EqualTo(WeddingError.s_wedding_result_early_wedding_reserve));
        Assert.Multiple(() => {
            Assert.That(StoredHalls(), Is.EqualTo(0));
            Assert.That(proposer.Session.Currency.Meret, Is.EqualTo(1000));
            Assert.That(accepter.Session.Marriage.UnconfirmedReservation, Is.Null);
        });
    }

    [Test]
    public void Confirm_AfterThePromiseLimitByThen_Is0x29() {
        Assume.That(proposer.Session.Marriage.ReserveHall(Reservation(proposer, Now + 86400)), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        accepter.Session.Marriage.Marriage.CreationTime = Now - 8 * 86400L;

        Assert.That(accepter.Session.Marriage.ConfirmReservation(), Is.EqualTo(WeddingError.s_wedding_result_late_promise_time));
        Assert.Multiple(() => {
            Assert.That(StoredHalls(), Is.EqualTo(0));
            Assert.That(proposer.Session.Currency.Meret, Is.EqualTo(1000));
        });
    }
}
