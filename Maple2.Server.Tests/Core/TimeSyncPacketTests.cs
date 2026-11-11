using System;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Tests.Core;

public class TimeSyncPacketTests {
    private static readonly DateTimeOffset Time = new(2026, 11, 11, 8, 16, 0, TimeSpan.Zero);

    [Test]
    public void ZoneIsTheSecondsWestOfUtc() {
        TimeZoneInfo kst = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");
        TimeZoneInfo cst = TimeZoneInfo.CreateCustomTimeZone("CST", TimeSpan.FromHours(-6), "CST", "CST");

        Assert.That(TimeSyncPacket.ZoneSeconds(Time, kst), Is.EqualTo(-32400));
        Assert.That(TimeSyncPacket.ZoneSeconds(Time, cst), Is.EqualTo(21600));
        Assert.That(TimeSyncPacket.ZoneSeconds(Time, TimeZoneInfo.Utc), Is.EqualTo(0));
    }

    [Test]
    public void ClientLocalTimeIsServerLocalTime() {
        TimeZoneInfo cst = TimeZoneInfo.CreateCustomTimeZone("CST", TimeSpan.FromHours(-6), "CST", "CST");
        // CMsTime::GetLocalTm: gmtime(utc - zone + dst * 3600), dst 0.
        DateTimeOffset client = Time.AddSeconds(-TimeSyncPacket.ZoneSeconds(Time, cst));

        Assert.That(client.DateTime, Is.EqualTo(TimeZoneInfo.ConvertTime(Time, cst).DateTime));
    }
}
