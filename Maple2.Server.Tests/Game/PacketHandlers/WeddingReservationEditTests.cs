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

// The cancel (Wedding 0x10, 0x12, 0x11) and the change (0x14, 0x15) of a reserved hall, as the client's
// WeddingManager reads them: the asked partner's answer counts only when asked, a declined or partner-less cancel
// opens the reserver's no-refund box (0x11 with reason 10 or 0x22), 0x13 has no body, and nothing is sent for a
// declined change or to the partner who declined a cancel (the client has no reader for 0x12 or 0x15). An answer
// counts only for the hall it was asked about, the tests are run again at the yes, a cancel whose delete fails mails
// no coupon and sends no 0x13, and the promise limit (0x29) is tested first, as the client's CheckBase. The hall's
// change locks the row (SELECT ... FOR UPDATE), which the in-memory store cannot run: it is stored on MySQL only
// (MS2_TEST_GAME_DB).
[TestFixture(false)]
[TestFixture(true)]
public class WeddingReservationEditTests(bool mysql) {
    private static readonly string? Connection = Environment.GetEnvironmentVariable("MS2_TEST_GAME_DB");
    private const int Cost = 300;

    private TestGameSession payer = null!;
    private TestGameSession confirmer = null!;
    private DbContextOptions options = null!;
    private readonly WeddingHandler handler = new();
    private readonly HallDeleteFault fault = new();
    private int mailsBefore;

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [SetUp]
    public void SetUp() {
        fault.Fail = false;
        if (mysql) {
            if (Connection == null) {
                Assert.Ignore("the hall's row lock is MySQL's: set MS2_TEST_GAME_DB");
            }
            options = new DbContextOptionsBuilder().UseMySql(Connection, ServerVersion.AutoDetect(Connection)).AddInterceptors(fault).Options;
            using var context = new Ms2Context(options);
            context.Database.EnsureCreated();
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
        payer.Register(TestGameSession.Metadata(Constant.Grade1WeddingCouponItemId));
        confirmer.Register(TestGameSession.Metadata(Constant.Grade1WeddingCouponItemId));
        var hall = new WeddingPackage.HallData(1, 2000023, 2000023, 1, Cost, [], []);
        var other = new WeddingPackage.HallData(2, 2000023, 2000023, 1, Cost, [], []);
        var table = new WeddingTable(new Dictionary<MarriageExpType, WeddingReward>(),
            new Dictionary<int, WeddingPackage> { [1] = new WeddingPackage(1, 0, new Dictionary<int, WeddingPackage.HallData> { [1] = hall, [2] = other }) });
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

        // The payer reserves a hall a day ahead and the confirmer agrees.
        Assume.That(payer.Session.Marriage.ReserveHall(Reservation(Now + 86400)), Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation));
        Assume.That(confirmer.Session.Marriage.ConfirmReservation(), Is.EqualTo(WeddingError.s_wedding_propose_ok));
        Assume.That(payer.Session.Marriage.WeddingHall.Id, Is.Not.EqualTo(0));
        mailsBefore = Mails(payer.CharacterId);
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

    // A save that deletes a hall row fails, as a database that refuses it would.
    private sealed class HallDeleteFault : SaveChangesInterceptor {
        public bool Fail;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            if (Fail && eventData.Context != null && eventData.Context.ChangeTracker.Entries<Maple2.Database.Model.WeddingHall>()
                    .Any(entry => entry.State == EntityState.Deleted)) {
                throw new DbUpdateException("The test made this save fail");
            }
            return result;
        }
    }

    private WeddingHall Reservation(long ceremonyTime, int hallId = 1) => new() {
        Id = 0,
        MarriageId = payer.Session.Marriage.Marriage.Id,
        PackageId = 1,
        PackageHallId = hallId,
        CeremonyTime = ceremonyTime,
        ReserverAccountId = payer.AccountId,
        ReserverCharacterId = payer.CharacterId,
        ReserverName = payer.Session.PlayerName,
        PartnerName = confirmer.Session.PlayerName,
    };

    private void Send(TestGameSession session, byte command, short? answer = null) {
        handler.Handle(session.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            if (answer != null) {
                writer.WriteShort(answer.Value);
            }
        }));
    }

    // The Wedding packets sent, as mode and body.
    private static List<(byte Mode, byte[] Body)> Wedding(TestGameSession session) {
        return session.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Wedding)
            .Select(packet => (packet[2], packet[3..])).ToList();
    }

    private static byte[] Short(short value) => BitConverter.GetBytes(value);

    private int StoredHalls() {
        using var context = new Ms2Context(options);
        long marriageId = payer.Session.Marriage.Marriage.Id;
        return context.WeddingHall.Count(hall => hall.MarriageId == marriageId);
    }

    private int Mails(long characterId) {
        using var context = new Ms2Context(options);
        return context.Mail.Count(mail => mail.ReceiverId == characterId);
    }

    private void PartnerLeaves() {
        Assume.That(payer.Session.Field!.Players.TryRemove(1002, out _), Is.True);
    }

    // The hall cancelled and another one reserved, as between an ask and its answer.
    private long ReplaceHall() {
        using (GameStorage.Request db = payer.Storage.Context()) {
            Assume.That(db.CancelWeddingHall(payer.Session.Marriage.WeddingHall.Id), Is.Not.Null);
        }
        WeddingHall? next;
        using (GameStorage.Request db = payer.Storage.Context()) {
            next = db.CreateWeddingHall(Reservation(Now + 2 * 86400), payer.Session.Marriage.Marriage);
        }
        Assume.That(next, Is.Not.Null);
        payer.Session.Marriage.WeddingHall = next!;
        confirmer.Session.Marriage.WeddingHall = next!;
        return next!.Id;
    }

    private void SetCeremony(long time) {
        payer.Session.Marriage.WeddingHall.CeremonyTime = time;
        confirmer.Session.Marriage.WeddingHall.CeremonyTime = time;
    }

    [Test]
    public void Cancel_PartnerAway_OpensTheNoRefundBox() {
        PartnerLeaves();
        Send(payer, 0x10);

        Assert.That(Wedding(payer), Is.EqualTo(new List<(byte, byte[])> { (0x11, Short(0x22)) }));
        Assert.That(StoredHalls(), Is.EqualTo(1));

        Send(payer, 0x11, 9);
        Assert.Multiple(() => {
            Assert.That(Wedding(payer), Is.EqualTo(new List<(byte, byte[])> { (0x13, Array.Empty<byte>()) }));
            Assert.That(StoredHalls(), Is.EqualTo(0));
            Assert.That(payer.Session.Marriage.WeddingHall.Id, Is.EqualTo(0));
            Assert.That(payer.World.MarriageRequests.Single().ReceiverId, Is.EqualTo(confirmer.CharacterId));
            Assert.That(payer.World.MarriageRequests.Single().RemoveWeddingHall, Is.Not.Null);
        });
    }

    [Test]
    public void Cancel_Declined_OpensTheRequestersNoRefundBox() {
        Send(payer, 0x10);
        Assert.That(Wedding(payer), Is.EqualTo(new List<(byte, byte[])> { (2, [17]) }));
        Assert.That(Wedding(confirmer), Is.EqualTo(new List<(byte, byte[])> { (0x10, Array.Empty<byte>()) }));

        Send(confirmer, 0x12, 10);
        Assert.Multiple(() => {
            Assert.That(Wedding(confirmer), Is.Empty);
            Assert.That(Wedding(payer), Is.EqualTo(new List<(byte, byte[])> { (0x11, Short(10)) }));
        });

        // No to the no-refund box keeps the hall and sends nothing.
        Send(payer, 0x11, 10);
        Assert.Multiple(() => {
            Assert.That(Wedding(payer), Is.Empty);
            Assert.That(StoredHalls(), Is.EqualTo(1));
        });
    }

    [Test]
    public void Cancel_Accepted_BothGetTheEmptyCancel() {
        Send(payer, 0x10);
        payer.TakeSent();
        confirmer.TakeSent();

        Send(confirmer, 0x12, 9);
        Assert.Multiple(() => {
            Assert.That(Wedding(payer), Is.EqualTo(new List<(byte, byte[])> { (0x13, Array.Empty<byte>()) }));
            Assert.That(Wedding(confirmer), Is.EqualTo(new List<(byte, byte[])> { (0x13, Array.Empty<byte>()) }));
            Assert.That(StoredHalls(), Is.EqualTo(0));
            Assert.That(Mails(payer.CharacterId) - mailsBefore, Is.EqualTo(1));
        });
    }

    // An answer to a cancel nobody asked for, or a no-refund answer to a box never opened, changes nothing.
    [Test]
    public void Answers_WithoutTheAsk_AreIgnored() {
        Send(confirmer, 0x12, 9);
        Send(payer, 0x11, 9);
        Assert.Multiple(() => {
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(Mails(payer.CharacterId) - mailsBefore, Is.EqualTo(0));
            Assert.That(Wedding(payer), Is.Empty);
            Assert.That(Wedding(confirmer), Is.Empty);
        });
    }

    // The client's limit is WeddingHallModifyLimitHour (3) hours before the ceremony, not 5 minutes.
    [Test]
    public void Cancel_InsideTheLimitHours_Refused() {
        SetCeremony(Now + 2 * 3600);
        Send(payer, 0x10);
        Assert.Multiple(() => {
            Assert.That(Wedding(payer), Is.EqualTo(new List<(byte, byte[])> { (2, [0x2a]) }));
            Assert.That(Wedding(confirmer), Is.Empty);
        });
    }

    [Test]
    public void Cancel_ByThePartnerWhoDidNotReserve_Refused() {
        Send(confirmer, 0x10);
        Assert.That(Wedding(confirmer), Is.EqualTo(new List<(byte, byte[])> { (2, [0x3e]) }));
    }

    [Test]
    public void Change_SameReservation_Is0x2c() {
        WeddingHall current = payer.Session.Marriage.WeddingHall;
        Assert.That(payer.Session.Marriage.ChangeReservationRequest(Reservation(current.CeremonyTime)),
            Is.EqualTo(WeddingError.s_wedding_result_already_same_reservation));
    }

    [Test]
    public void Change_InsideTheLimitHours_Refused() {
        SetCeremony(Now + 2 * 3600);
        Assert.That(payer.Session.Marriage.ChangeReservationRequest(Reservation(Now + 2 * 3600, hallId: 2)),
            Is.EqualTo(WeddingError.s_wedding_result_late_wedding_reserve_modify_time));
    }

    // Only the hall changes: the kept time is the couple's own row, not a taken one.
    [Test]
    public void Change_HallOnly_IsAsked_DeclineSendsNothing() {
        WeddingHall current = payer.Session.Marriage.WeddingHall;
        Assert.That(payer.Session.Marriage.ChangeReservationRequest(Reservation(current.CeremonyTime, hallId: 2)),
            Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation_change));
        Assert.That(Wedding(confirmer).Select(packet => packet.Mode).ToArray(), Is.EqualTo(new byte[] { 0x14 }));

        Send(confirmer, 0x15, 0xc);
        Assert.Multiple(() => {
            Assert.That(Wedding(confirmer), Is.Empty);
            Assert.That(Wedding(payer), Is.Empty);
            Assert.That(payer.Session.Marriage.WeddingHall.PackageHallId, Is.EqualTo(1));
        });
    }

    [Test]
    public void Change_Accepted_BothGetTheNewTicket() {
        if (!mysql) {
            Assert.Ignore("the change locks the hall's row: MySQL only");
        }
        WeddingHall current = payer.Session.Marriage.WeddingHall;
        Assume.That(payer.Session.Marriage.ChangeReservationRequest(Reservation(current.CeremonyTime, hallId: 2)),
            Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation_change));
        confirmer.TakeSent();

        Send(confirmer, 0x15, 0xb);
        Assert.Multiple(() => {
            Assert.That(Wedding(payer).Select(packet => packet.Mode).ToArray(), Is.EqualTo(new byte[] { 0x16 }));
            Assert.That(Wedding(confirmer).Select(packet => packet.Mode).ToArray(), Is.EqualTo(new byte[] { 0x16 }));
            Assert.That(payer.Session.Marriage.WeddingHall.PackageHallId, Is.EqualTo(2));
        });

        // A second answer is not asked for.
        Send(confirmer, 0x15, 0xb);
        Assert.That(Wedding(payer), Is.Empty);
    }

    // The hall is deleted before the coupon is mailed: a delete that fails mails no coupon, keeps the hall on both
    // and sends no 0x13 (the coupon was mailed first and the failed delete only logged).
    [Test]
    public void Cancel_DeleteFails_MailsNoCouponAndKeepsTheHall() {
        Send(payer, 0x10);
        payer.TakeSent();
        confirmer.TakeSent();
        fault.Fail = true;

        Send(confirmer, 0x12, 9);
        Assert.Multiple(() => {
            Assert.That(Wedding(confirmer), Is.EqualTo(new List<(byte, byte[])> { (2, [unchecked((byte) WeddingError.s_wedding_result_err_system)]) }));
            Assert.That(Wedding(payer), Is.Empty);
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(Mails(payer.CharacterId) - mailsBefore, Is.EqualTo(0));
            Assert.That(payer.Session.Marriage.WeddingHall.Id, Is.Not.EqualTo(0));
            Assert.That(confirmer.Session.Marriage.WeddingHall.Id, Is.Not.EqualTo(0));
        });
    }

    [Test]
    public void CancelNoRefund_DeleteFails_KeepsTheHall() {
        PartnerLeaves();
        Send(payer, 0x10);
        payer.TakeSent();
        fault.Fail = true;

        Send(payer, 0x11, 9);
        Assert.Multiple(() => {
            Assert.That(Wedding(payer), Is.EqualTo(new List<(byte, byte[])> { (2, [unchecked((byte) WeddingError.s_wedding_result_err_system)]) }));
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(payer.Session.Marriage.WeddingHall.Id, Is.Not.EqualTo(0));
            Assert.That(payer.World.MarriageRequests, Is.Empty);
        });
    }

    // The client's CheckBase tests the promise limit before the cancel's and the change's own tests.
    [Test]
    public void Cancel_AfterThePromiseLimit_Is0x29() {
        payer.Session.Marriage.Marriage.CreationTime = Now - 8 * 86400L;
        Send(payer, 0x10);
        Assert.Multiple(() => {
            Assert.That(Wedding(payer), Is.EqualTo(new List<(byte, byte[])> { (2, [0x29]) }));
            Assert.That(Wedding(confirmer), Is.Empty);
        });
    }

    [Test]
    public void Change_AfterThePromiseLimit_Is0x29() {
        payer.Session.Marriage.Marriage.CreationTime = Now - 8 * 86400L;
        WeddingHall current = payer.Session.Marriage.WeddingHall;
        Assert.That(payer.Session.Marriage.ChangeReservationRequest(Reservation(current.CeremonyTime, hallId: 2)),
            Is.EqualTo(WeddingError.s_wedding_result_late_promise_time));
    }

    // An answer counts only for the hall it was asked about: the yes to a cancel asked about a hall since replaced
    // deleted the new hall and mailed a coupon.
    [Test]
    public void Cancel_AskedAboutAReplacedHall_IsIgnored() {
        Send(payer, 0x10);
        long next = ReplaceHall();
        payer.TakeSent();
        confirmer.TakeSent();
        int mails = Mails(payer.CharacterId);

        Send(confirmer, 0x12, 9);
        Assert.Multiple(() => {
            Assert.That(Wedding(payer), Is.Empty);
            Assert.That(Wedding(confirmer), Is.Empty);
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(payer.Session.Marriage.WeddingHall.Id, Is.EqualTo(next));
            Assert.That(Mails(payer.CharacterId), Is.EqualTo(mails));
        });
    }

    [Test]
    public void NoRefund_OpenedForAReplacedHall_IsIgnored() {
        PartnerLeaves();
        Send(payer, 0x10);
        long next = ReplaceHall();
        payer.TakeSent();

        Send(payer, 0x11, 9);
        Assert.Multiple(() => {
            Assert.That(Wedding(payer), Is.Empty);
            Assert.That(StoredHalls(), Is.EqualTo(1));
            Assert.That(payer.Session.Marriage.WeddingHall.Id, Is.EqualTo(next));
        });
    }

    [Test]
    public void Change_AskedAboutAReplacedHall_IsIgnored() {
        WeddingHall current = payer.Session.Marriage.WeddingHall;
        Assume.That(payer.Session.Marriage.ChangeReservationRequest(Reservation(current.CeremonyTime, hallId: 2)),
            Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation_change));
        long next = ReplaceHall();
        payer.TakeSent();
        confirmer.TakeSent();

        Send(confirmer, 0x15, 0xb);
        Assert.Multiple(() => {
            Assert.That(Wedding(payer), Is.Empty);
            Assert.That(Wedding(confirmer), Is.Empty);
            Assert.That(payer.Session.Marriage.WeddingHall.Id, Is.EqualTo(next));
            Assert.That(payer.Session.Marriage.WeddingHall.PackageHallId, Is.EqualTo(1));
        });
    }

    // The change's tests are run again at the yes: the limit hours reached while the box was open refuse it (0x2a).
    [Test]
    public void Change_YesInsideTheLimitHours_Refused() {
        WeddingHall current = payer.Session.Marriage.WeddingHall;
        Assume.That(payer.Session.Marriage.ChangeReservationRequest(Reservation(current.CeremonyTime, hallId: 2)),
            Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation_change));
        confirmer.TakeSent();
        SetCeremony(Now + 2 * 3600);

        Send(confirmer, 0x15, 0xb);
        Assert.Multiple(() => {
            Assert.That(Wedding(confirmer), Is.EqualTo(new List<(byte, byte[])> { (2, [0x2a]) }));
            Assert.That(Wedding(payer), Is.Empty);
            Assert.That(payer.Session.Marriage.WeddingHall.PackageHallId, Is.EqualTo(1));
        });
    }

    // Only the asked partner keeps the change: the one who asked wrote it into their own 0x0D ask field too.
    [Test]
    public void Change_LeavesTheRequestersReservationAskAlone() {
        WeddingHall current = payer.Session.Marriage.WeddingHall;
        Assume.That(payer.Session.Marriage.ChangeReservationRequest(Reservation(current.CeremonyTime, hallId: 2)),
            Is.EqualTo(WeddingError.s_wedding_result_send_wedding_hall_reservation_change));
        Assert.Multiple(() => {
            Assert.That(payer.Session.Marriage.UnconfirmedReservation, Is.Null);
            Assert.That(confirmer.Session.Marriage.UnconfirmedReservation, Is.Null);
        });
    }
}
