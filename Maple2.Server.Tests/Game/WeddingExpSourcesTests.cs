using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Game;

// The marriage's exp as the client reads it: Wedding 0's exp long is the log's sum (WeddingProxy::GetExp,
// weddingproxy.cpp:705, adds the marriedLife row for each day since the vow itself), and the log's lines the client
// names (s_wedding_dialog_exp_2-5, 8) are written by their reward rows: the couple message, a game entry, the vow.
public class WeddingExpSourcesTests {
    private TestGameSession test = null!;
    private long partnerId;
    private long marriageId;

    // KR weddingreward.xml.
    private static WeddingTable Table() => new(new Dictionary<MarriageExpType, WeddingReward> {
        [MarriageExpType.marriedLife] = new(MarriageExpType.marriedLife, 5, MarriageExpLimit.none),
        [MarriageExpType.attendance] = new(MarriageExpType.attendance, 5, MarriageExpLimit.day),
        [MarriageExpType.coupleMessage] = new(MarriageExpType.coupleMessage, 5, MarriageExpLimit.day),
        [MarriageExpType.marriageRegister] = new(MarriageExpType.marriageRegister, 100, MarriageExpLimit.month),
    }, new Dictionary<int, WeddingPackage>());

    private void Marry(bool married, DateTime marriageTime) {
        test = new TestGameSession();
        test.Start();
        typeof(Maple2.Server.Game.Session.GameSession).GetField(nameof(Maple2.Server.Game.Session.GameSession.Scheduler))!
            .SetValue(test.Session, new Maple2.Tools.Scheduler.EventQueue(Serilog.Log.Logger));
        test.SetTable(Table());
        using GameStorage.Request db = test.Storage.Context();
        Account account = db.CreateAccount(new Account { Username = "partner" }, "pw");
        var partner = new Character { AccountId = account.Id, Name = "partner", Mastery = new Mastery() };
        partner.ReturnMaps.Push(2000062);
        partnerId = db.CreateCharacter(partner)!.Id;
        marriageId = db.CreateMarriage(test.CharacterId, partnerId)!.Id;
        if (married) {
            Assume.That(db.SetMarried(marriageId, marriageTime), Is.True);
        }
        test.Session.Marriage.Marriage = db.GetMarriage(test.CharacterId)!;
        test.Session.Marriage.WeddingHall = WeddingHall.Default;
    }


    private List<(MarriageExpType Type, long Amount)> StoredLog() {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        return context.Marriage.Single(row => row.Id == marriageId).ExpHistory.Select(exp => (exp.Type, exp.Amount)).ToList();
    }

    [Test]
    public void LogTypes_AreTheClientsNumbers() {
        Assert.That(new[] { MarriageExpType.coupleMessage, MarriageExpType.CoupleMessageBride, MarriageExpType.AttendanceGroom,
            MarriageExpType.AttendanceBride, MarriageExpType.Wedding }.Select(type => (short) type), Is.EqualTo(new short[] { 2, 3, 4, 5, 8 }));
        Assert.That(MarriageExpRules.Attendance(1, 1), Is.EqualTo(MarriageExpType.AttendanceGroom));
        Assert.That(MarriageExpRules.Attendance(2, 1), Is.EqualTo(MarriageExpType.AttendanceBride));
        Assert.That(MarriageExpRules.CoupleMessage(1, 1), Is.EqualTo(MarriageExpType.coupleMessage));
        Assert.That(MarriageExpRules.CoupleMessage(2, 1), Is.EqualTo(MarriageExpType.CoupleMessageBride));
    }

    // IsNotRecentDayChange and IsMonthChanged: calendar changes, not 24 hours or 30 days.
    [Test]
    public void Due_ByTheCalendar() {
        var now = new DateTime(2026, 3, 1, 0, 1, 0);
        (MarriageExpType, DateTime)[] Log(DateTime time) => [(MarriageExpType.AttendanceGroom, time)];
        Assert.Multiple(() => {
            Assert.That(MarriageExpRules.Due([], MarriageExpType.AttendanceGroom, MarriageExpLimit.day, now), Is.True);
            Assert.That(MarriageExpRules.Due(Log(now.AddMinutes(-2)), MarriageExpType.AttendanceGroom, MarriageExpLimit.day, now), Is.True);
            Assert.That(MarriageExpRules.Due(Log(now.AddSeconds(-30)), MarriageExpType.AttendanceGroom, MarriageExpLimit.day, now), Is.False);
            Assert.That(MarriageExpRules.Due(Log(now.AddDays(1)), MarriageExpType.AttendanceGroom, MarriageExpLimit.day, now), Is.False);
            // Another line does not close it.
            Assert.That(MarriageExpRules.Due(Log(now.AddSeconds(-30)), MarriageExpType.AttendanceBride, MarriageExpLimit.day, now), Is.True);
            Assert.That(MarriageExpRules.Due(Log(now.AddMinutes(-2)), MarriageExpType.AttendanceGroom, MarriageExpLimit.month, now), Is.True);
            Assert.That(MarriageExpRules.Due(Log(now.AddSeconds(-30)), MarriageExpType.AttendanceGroom, MarriageExpLimit.month, now), Is.False);
            Assert.That(MarriageExpRules.Due(Log(now), MarriageExpType.AttendanceGroom, MarriageExpLimit.none, now), Is.True);
        });
    }

    [Test]
    public void GradeExp_CountsTheDaysSinceTheVow() {
        var vow = new DateTime(2026, 3, 1, 23, 0, 0);
        var now = new DateTime(2026, 3, 4, 1, 0, 0);
        Assert.Multiple(() => {
            Assert.That(MarriageExpRules.GradeExp(MaritalStatus.Married, vow, now, 5, 40), Is.EqualTo(3 * 5 + 40));
            Assert.That(MarriageExpRules.GradeExp(MaritalStatus.DivorceFiledByGroom, vow, now, 5, 40), Is.EqualTo(55));
            Assert.That(MarriageExpRules.GradeExp(MaritalStatus.Engaged, vow, now, 5, 40), Is.EqualTo(0));
            Assert.That(MarriageExpRules.GradeExp(MaritalStatus.Married, vow, now, 5, -100), Is.EqualTo(0));
        });
    }

    // Married three days ago: the sent exp is the log's sum (the attendance line of this entry), not the days as well;
    // the grade counts the days.
    [Test]
    public void Load_SendsTheLogsSum_AndWritesTheAttendanceOnceADay() {
        Marry(true, DateTime.Now.AddDays(-3));
        test.TakeSent();

        test.Session.Marriage.Load();
        test.Session.Marriage.Load();

        Assert.That(StoredLog(), Is.EqualTo(new[] { (MarriageExpType.AttendanceGroom, 5L) }));
        // Marriage.Exp is the exp long Wedding 0 writes (Marriage.WriteTo).
        Assert.That(test.Session.Marriage.Marriage.Exp, Is.EqualTo(5));
        Assert.That(test.Session.Marriage.GradeExp(), Is.EqualTo(3 * 5 + 5));

        // Another session of the same day writes nothing more.
        var again = new Maple2.Server.Game.Manager.MarriageManager(test.Session);
        using (GameStorage.Request db = test.Storage.Context()) {
            again.Marriage = db.GetMarriage(test.CharacterId)!;
        }
        again.WeddingHall = WeddingHall.Default;
        again.Load();
        Assert.That(StoredLog(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Load_OfAnEngagedCouple_WritesNothing() {
        Marry(false, default);
        test.Session.Marriage.Load();
        Assert.That(StoredLog(), Is.Empty);
        Assert.That(test.Session.Marriage.GradeExp(), Is.EqualTo(0));
    }

    [Test]
    public void CoupleMessage_WritesItsLineOnceADay() {
        Marry(true, DateTime.Now.AddDays(-1));

        test.Session.Marriage.SetCoupleMessage("a");
        test.Session.Marriage.SetCoupleMessage("b");

        Assert.That(StoredLog(), Is.EqualTo(new[] { (MarriageExpType.coupleMessage, 5L) }));
        Assert.That(test.Session.Marriage.Marriage.Exp, Is.EqualTo(5));
    }

    // The storage refuses a character outside the marriage and a line that is not due.
    [Test]
    public void AddMarriageExp_Refusals() {
        Marry(true, DateTime.Now);
        using GameStorage.Request db = test.Storage.Context();
        DateTime now = DateTime.Now;
        Assert.That(db.AddMarriageExp(marriageId, 999, MarriageExpRules.Attendance, 5, MarriageExpLimit.day, now), Is.False);
        Assert.That(db.AddMarriageExp(marriageId, partnerId, MarriageExpRules.Attendance, 5, MarriageExpLimit.day, now), Is.True);
        Assert.That(db.AddMarriageExp(marriageId, partnerId, MarriageExpRules.Attendance, 5, MarriageExpLimit.day, now), Is.False);
        Assert.That(db.AddMarriageExp(marriageId, partnerId, MarriageExpRules.Attendance, 0, MarriageExpLimit.none, now), Is.False);
        Assert.That(StoredLog(), Is.EqualTo(new[] { (MarriageExpType.AttendanceBride, 5L) }));
    }
}
