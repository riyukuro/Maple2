using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Database.Extensions;

namespace Maple2.Server.Tests.Validators;

// The attendance gift's play time: the client counts the stored seconds (user value 106) plus the time since the
// play start of TimeSync 3, and claims by itself (Attendance 0) when the event's play seconds are reached.
public class AttendancePlayTimeTests {
    private static readonly DateTime Login = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Local);

    // Made-up times: a fresh day, a later login of the same day, and a login that ran over midnight.
    [Test]
    public void PlaySeconds() {
        Assert.That(AttendanceRules.PlaySeconds(0, Login, Login), Is.EqualTo(0));
        Assert.That(AttendanceRules.PlaySeconds(0, Login, Login.AddSeconds(599)), Is.EqualTo(599));
        Assert.That(AttendanceRules.PlaySeconds(0, Login, Login.AddSeconds(600)), Is.EqualTo(600));
        Assert.That(AttendanceRules.PlaySeconds(400, Login, Login.AddSeconds(200)), Is.EqualTo(600));
        // The time since the start alone counts: saving the character moves nothing.
        Assert.That(AttendanceRules.PlaySeconds(0, Login, Login.AddHours(3)), Is.EqualTo(3 * 3600));
        // A start on an earlier day counts from today's midnight only.
        Assert.That(AttendanceRules.PlaySeconds(0, Login, Login.Date.AddDays(1).AddSeconds(30)), Is.EqualTo(30));
        // A clock that ran backwards or a broken value gives no negative time.
        Assert.That(AttendanceRules.PlaySeconds(0, Login, Login.AddSeconds(-5)), Is.EqualTo(0));
        Assert.That(AttendanceRules.PlaySeconds(-7, Login, Login.AddSeconds(5)), Is.EqualTo(5));
    }

    // What is stored for the next login: the day's seconds with this login's added, until the next midnight. The
    // value kept in the session is not changed, so a second save does not count the login twice.
    [Test]
    public void StoredPlayTime() {
        long midnight = Login.Date.AddDays(1).ToEpochSeconds();
        var value = new GameEventUserValue(GameEventUserValueType.AttendanceAccumulatedTime, midnight, 7001);
        value.SetValue("15621");

        GameEventUserValue first = GameEventManager.StoredPlayTime(value, Login, Login.AddSeconds(15));
        Assert.That((first.Type, first.EventId, first.Value, first.ExpirationTime),
            Is.EqualTo((GameEventUserValueType.AttendanceAccumulatedTime, 7001, "15636", midnight)));
        Assert.That(value.Value, Is.EqualTo("15621"));

        GameEventUserValue second = GameEventManager.StoredPlayTime(value, Login, Login.AddSeconds(40));
        Assert.That(second.Value, Is.EqualTo("15661"));

        // Over midnight the old day's seconds have expired; the new day counts from its start.
        DateTime nextDay = Login.Date.AddDays(1).AddSeconds(90);
        GameEventUserValue over = GameEventManager.StoredPlayTime(value, Login, nextDay);
        Assert.That((over.Value, over.ExpirationTime), Is.EqualTo(("90", Login.Date.AddDays(2).ToEpochSeconds())));
    }

    // KMS2 sends the play seconds near the end of a session as GameEventUserValue 1: byte 1, byte 0, int type 106,
    // int event, the seconds as text, long expiry (the next midnight).
    [Test]
    public void PlayTimeUpdateBytes() {
        var value = new GameEventUserValue(GameEventUserValueType.AttendanceAccumulatedTime, 0x60F19EF0, 170040);
        value.SetValue("15636");
        byte[] expected = Convert.FromHexString("01006A00000038980200050031003500360033003600F09EF16000000000");
        Assert.That(Body(GameEventUserValuePacket.Update(value)), Is.EqualTo(expected));
    }

    // TimeSync 3, the play start: byte 3 and the long time, 9 bytes, as KMS2 sends it at login and at the daily reset.
    [Test]
    public void PlayStartBytes() {
        byte[] body = Body(TimeSyncPacket.Set(DateTimeOffset.FromUnixTimeSeconds(1632668400)));
        Assert.That(body, Is.EqualTo(Convert.FromHexString("03F08A506100000000")));
    }

    private static byte[] Body(ByteWriter writer) {
        return writer.ToArray()[2..];
    }
}
