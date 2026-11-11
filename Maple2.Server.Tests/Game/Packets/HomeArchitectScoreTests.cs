using System;
using Google.Protobuf;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

// A home's architect scores carried to every channel after a commendation, and read again when a month begins.
public class HomeArchitectScoreTests {
    // Client: CSceneStateGame::OnHomeCommand case 1 reads int PC object id, long, int this month, int all time.
    [Test]
    public void UpdateIsObjectIdTimeThenScores() {
        ByteWriter writer = HomeCommandPacket.UpdateArchitectScore(0x11223344, 3, 40);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.HomeCommand));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x11223344));
        reader.ReadLong();
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(40));
        Assert.That(writer.Length, Is.EqualTo(23));
    }

    [Test]
    public void Set_ReportsOnlyAChange() {
        var home = new Home { CurrentArchitectScore = 2, ArchitectScore = 9 };

        Assert.That(HomeArchitectScore.Set(home, 2, 9), Is.False);
        Assert.That(HomeArchitectScore.Set(home, 3, 10), Is.True);
        Assert.That(home.CurrentArchitectScore, Is.EqualTo(3));
        Assert.That(home.ArchitectScore, Is.EqualTo(10));
        Assert.That(HomeArchitectScore.Set(home, 0, 10), Is.True);
        Assert.That(home.CurrentArchitectScore, Is.EqualTo(0));
    }

    // Neither the home nor its owner on a channel: nothing to do.
    [Test]
    public void Apply_WithNothingLoaded_DoesNothing() {
        Assert.DoesNotThrow(() => HomeArchitectScore.Apply(null, null, 1, 1));
    }

    // The request survives the trip through the World: the score case, the owner, both scores, and a map id the World
    // and the channels accept (above 0).
    [Test]
    public void Request_RoundTrips() {
        FieldPlotRequest request = HomeArchitectScore.Request(62000123, 7001, 4, 55, 2);

        FieldPlotRequest parsed = FieldPlotRequest.Parser.ParseFrom(request.ToByteArray());

        Assert.That(parsed.RequestCase, Is.EqualTo(FieldPlotRequest.RequestOneofCase.ArchitectScore));
        Assert.That(parsed.MapId, Is.EqualTo(62000123));
        Assert.That(parsed.IgnoreChannel, Is.EqualTo(2));
        Assert.That(parsed.ArchitectScore.OwnerId, Is.EqualTo(7001));
        Assert.That(parsed.ArchitectScore.Current, Is.EqualTo(4));
        Assert.That(parsed.ArchitectScore.Total, Is.EqualTo(55));
    }

    private static long At(TimeZoneInfo zone, int year, int month, int day, int hour, int minute) {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUnixTimeSeconds();
    }

    // A reset fired a moment early, on time or hours late reads the month that has just begun.
    [TestCase(2030, 11, 30, 23, 59)]
    [TestCase(2030, 12, 1, 0, 0)]
    [TestCase(2030, 12, 1, 6, 0)]
    public void MonthAtReset_IsTheNewMonth(int year, int month, int day, int hour, int minute) {
        TimeZoneInfo zone = TimeZoneInfo.Utc;

        (long begin, long end) = HomeCommendRules.MonthAtReset(At(zone, year, month, day, hour, minute), zone);

        Assert.That(begin, Is.EqualTo(At(zone, 2030, 12, 1, 0, 0)));
        Assert.That(end, Is.EqualTo(At(zone, 2031, 1, 1, 0, 0)));
    }

    // In the middle of a month it is that month.
    [Test]
    public void MonthAtReset_MidMonth() {
        TimeZoneInfo zone = TimeZoneInfo.Utc;

        (long begin, long _) = HomeCommendRules.MonthAtReset(At(zone, 2030, 6, 15, 12, 0), zone);

        Assert.That(begin, Is.EqualTo(At(zone, 2030, 6, 1, 0, 0)));
    }
}
