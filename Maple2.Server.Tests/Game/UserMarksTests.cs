using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The new-user and returning-user marks of the GameEvent NewUser and ReturnUser rows (value2, a user value type with
// event id 0), which the client's attendance rows and stamp cards are listed by.
public class UserMarksTests {
    private const long Day = 86400;
    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static readonly GameEventUserValueType Returner = (GameEventUserValueType) 10002;
    private static readonly GameEventUserValueType NewUser = (GameEventUserValueType) 10001;

    private static GameEvent Row(int id, GameEventData data, int endDays = 30) {
        return new GameEvent(new GameEventMetadata(id, GameEventType.ReturnUser, DateTime.Now.AddDays(-3), DateTime.Now.AddDays(endDays),
            TimeSpan.Zero, TimeSpan.Zero, [], data, string.Empty, string.Empty, string.Empty, string.Empty));
    }

    private static ReturnUser LastOnlineBefore(DateTimeOffset date, int mark) => new(0, [], mark, date, 0, 0);
    private static ReturnUser DaysAway(int days, int mark, int season = 0) => new(season, [], mark, DateTimeOffset.MinValue, days, days);

    // A ReturnUser row marks a character last saved before its date, or its days ago or more; a NewUser row a
    // character created on or after its date. A character never saved is no returner.
    [Test]
    public void Due_ByTheRows() {
        DateTimeOffset date = DateTimeOffset.FromUnixTimeSeconds(Now - 60 * Day);
        GameEvent byDate = Row(180112, LastOnlineBefore(date, 10002));
        GameEvent byDays = Row(180104, DaysAway(30, 10003, season: 1));
        GameEvent created = Row(180111, new NewUser(10001, date));
        Assert.Multiple(() => {
            Assert.That(UserMarks.Due([byDate], 0, Now - 61 * Day, Now).Keys, Is.EquivalentTo(new[] { Returner }));
            Assert.That(UserMarks.Due([byDate], 0, Now - 59 * Day, Now), Is.Empty);
            Assert.That(UserMarks.Due([byDate], 0, 0, Now), Is.Empty);
            Assert.That(UserMarks.Due([byDays], 0, Now - 30 * Day, Now).Keys, Is.EquivalentTo(new[] { (GameEventUserValueType) 10003 }));
            Assert.That(UserMarks.Due([byDays], 0, Now - 29 * Day, Now), Is.Empty);
            Assert.That(UserMarks.Due([created], Now - 60 * Day, Now, Now).Keys, Is.EquivalentTo(new[] { NewUser }));
            Assert.That(UserMarks.Due([created], Now - 61 * Day, Now, Now), Is.Empty);
            // A row without a mark or a date marks no one.
            Assert.That(UserMarks.Due([Row(1, LastOnlineBefore(date, 0)), Row(2, new NewUser(10001, DateTimeOffset.MinValue))],
                Now - 60 * Day, Now - 61 * Day, Now), Is.Empty);
        });
    }

    // Two rows of one mark: the mark lasts to the later end.
    [Test]
    public void Due_TheLaterEndOfOneMark() {
        GameEvent shorter = Row(180101, DaysAway(30, 10001), endDays: 5);
        GameEvent longer = Row(180100, new NewUser(10001, DateTimeOffset.FromUnixTimeSeconds(Now - Day)), endDays: 20);
        Dictionary<GameEventUserValueType, long> due = UserMarks.Due([shorter, longer], Now, Now - 40 * Day, Now);
        Assert.That(due[NewUser], Is.EqualTo(longer.EndTime));
    }

    // At entry the mark is set with event id 0 to the row's end and stored at once; it is loaded again until its end
    // although no event has id 0, and the attendance row that requires it opens while the general one closes.
    [Test]
    public void Mark_IsSetStoredAndLoadedUntilItsEnd() {
        var test = new TestGameSession();
        GameEvent row = test.AddEvent(180112, GameEventType.ReturnUser, LastOnlineBefore(DateTimeOffset.FromUnixTimeSeconds(Now - 60 * Day), 10002));
        test.Start();
        GameSession session = test.Session;

        session.GameEvent.Mark(session.Player.Value.Character.CreationTime, Now - 90 * Day);
        Assert.That(session.GameEvent.Peek(Returner, 0), Is.EqualTo(UserMarks.Set));
        Assert.That(AttendanceRules.RequirementMet(new AttendGift.Require(AttendGiftRequirement.UserValue, 10002, 0),
            type => session.GameEvent.Peek((GameEventUserValueType) type, 0), false), Is.True);
        Assert.That(AttendanceRules.RequirementMet(new AttendGift.Require(AttendGiftRequirement.NotUserValue, 10002, 0),
            type => session.GameEvent.Peek((GameEventUserValueType) type, 0), false), Is.False);

        GameEventUserValue stored;
        using (GameStorage.Request db = test.Storage.Context()) {
            stored = db.GetEventUserValues(session.CharacterId).Single(value => value.Type == Returner);
        }
        Assert.Multiple(() => {
            Assert.That((stored.EventId, stored.Value, stored.ExpirationTime), Is.EqualTo((0, UserMarks.Set, row.EndTime)));
            Assert.That(GameEventManager.LoadValue(stored, [], Now), Is.Not.Null);
            Assert.That(GameEventManager.LoadValue(stored, [], row.EndTime + 1), Is.Null);
            Assert.That(GameEventManager.Outdated(stored, [], row.EndTime + 1), Is.True);
        });

        // Entering again with a recent save keeps the mark.
        session.GameEvent.Mark(session.Player.Value.Character.CreationTime, Now);
        Assert.That(session.GameEvent.Peek(Returner, 0), Is.EqualTo(UserMarks.Set));
    }

    // value1 dates to the minute and to the second (KR 180119 and 170036 have seconds); days are no date.
    [Test]
    public void UserDate_ToTheMinuteOrSecond() {
        Assert.Multiple(() => {
            Assert.That(ServerTableMapper.UserDate("2019-04-30-23-59-59"), Is.EqualTo(new DateTimeOffset(new DateTime(2019, 4, 30, 23, 59, 59))));
            Assert.That(ServerTableMapper.UserDate("2020-06-25-00-00"), Is.EqualTo(new DateTimeOffset(new DateTime(2020, 6, 25, 0, 0, 0))));
            Assert.That(ServerTableMapper.UserDate("30"), Is.EqualTo(DateTimeOffset.MinValue));
        });
    }
}
