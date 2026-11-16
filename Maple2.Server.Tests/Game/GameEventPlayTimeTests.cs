using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Session;
using Maple2.Database.Extensions;

namespace Maple2.Server.Tests.Game;

// The daily reset of the attendance play time (GameEventManager.ResetPlayTime) and the extension of an event value
// past its old expiry (Extend), on an AttendGift event (KR 170007's shape).
public class GameEventPlayTimeTests {
    private const int EventId = 170007;
    private const int OtherEventId = 170008;
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(20000001));
        foreach (int id in new[] { EventId, OtherEventId }) {
            test.AddEvent(id, GameEventType.AttendGift, new AttendGift([new RewardItem(20000001, 1, 1)], "gift", "title", "content", string.Empty, 3600, null));
        }
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    // The old day's play seconds (AttendanceAccumulatedTime) become 0 until the next midnight and are sent, so the
    // client counts the new day from nothing as well; the play start moves to the reset; an event without the value
    // gets none.
    [Test]
    public void ResetPlayTime_ZeroesTheHeldSecondsUntilTheNextMidnight() {
        GameSession session = Start();
        session.GameEvent.Get(GameEventUserValueType.AttendanceAccumulatedTime, EventId, DateTime.Now.AddHours(1).ToEpochSeconds());
        session.GameEvent.Set(EventId, GameEventUserValueType.AttendanceAccumulatedTime, 1800);
        session.GameEvent.Get(GameEventUserValueType.AttendanceRewardsClaimed, OtherEventId, DateTime.Now.AddDays(5).ToEpochSeconds());
        test.TakeSent();
        DateTime reset = DateTime.Now.Date.AddDays(1).AddMinutes(1);

        session.GameEvent.ResetPlayTime(reset);

        GameEventUserValue value = session.GameEvent.Get(GameEventUserValueType.AttendanceAccumulatedTime, EventId, 0);
        Assert.Multiple(() => {
            Assert.That(session.GameEvent.PlayStart, Is.EqualTo(reset));
            Assert.That(value.Value, Is.EqualTo("0"));
            Assert.That(value.ExpirationTime, Is.EqualTo(reset.Date.AddDays(1).ToEpochSeconds()));
            Assert.That(session.GameEvent.Peek(GameEventUserValueType.AttendanceAccumulatedTime, OtherEventId), Is.Null.Or.Empty);
            Assert.That(test.TakeSent().Select(Convert.ToHexString),
                Is.EqualTo(new[] { Convert.ToHexString(GameEventUserValuePacket.Update(value).ToArray()) }));
        });
    }

    // An event extended after a value was set keeps the value (its text) to the new end; an earlier end changes nothing.
    [Test]
    public void Extend_KeepsTheTextToTheLaterEnd() {
        GameSession session = Start();
        long end = DateTime.Now.AddDays(1).ToEpochSeconds();
        session.GameEvent.Get(GameEventUserValueType.AttendanceRewardsClaimed, EventId, end);
        session.GameEvent.Set(EventId, GameEventUserValueType.AttendanceRewardsClaimed, 4);

        session.GameEvent.Extend(GameEventUserValueType.AttendanceRewardsClaimed, EventId, end + 86400);
        GameEventUserValue extended = session.GameEvent.Get(GameEventUserValueType.AttendanceRewardsClaimed, EventId, 0);
        session.GameEvent.Extend(GameEventUserValueType.AttendanceRewardsClaimed, EventId, end - 86400);
        GameEventUserValue after = session.GameEvent.Get(GameEventUserValueType.AttendanceRewardsClaimed, EventId, 0);

        Assert.Multiple(() => {
            Assert.That((extended.Value, extended.ExpirationTime), Is.EqualTo(("4", end + 86400)));
            Assert.That((after.Value, after.ExpirationTime), Is.EqualTo(("4", end + 86400)));
        });
    }
}
