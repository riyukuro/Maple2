using System;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using GameEvent = Maple2.File.Parser.Xml.Table.Server.GameEvent;

namespace Maple2.Server.Tests.Ingest;

// The ingest's gameEvent row: event times written to the second (the bonus rows' "2099-04-20-23-59-59") are read
// like those to the minute; a row the ingest cannot read keeps no time and is dropped as ended.
public class GameEventIngestTests {
    private static GameEvent Row(string start, string end) => new() {
        eventType = nameof(GameEventType.MiniGameBonusReward),
        eventStart = start,
        eventEnd = end,
        partTime = "",
        value1 = "oxquiz",
        value2 = """<items><v itemID="30001258" grade="1" count="40" /></items>""",
        value3 = "",
        value4 = "",
    };

    private static readonly MiniGameBonusReward Data = new(["oxquiz"], [new RewardItem(30001258, 1, 40)]);

    [Test]
    public void TimesToTheSecond() {
        GameEventMetadata row = ServerTableMapper.MapGameEvent(99000030, Row("2023-03-20-00-00-00", "2099-04-20-23-59-59"), GameEventType.MiniGameBonusReward, Data);
        Assert.Multiple(() => {
            Assert.That(row.StartTime, Is.EqualTo(new DateTime(2023, 3, 20, 0, 0, 0)));
            Assert.That(row.EndTime, Is.EqualTo(new DateTime(2099, 4, 20, 23, 59, 59)));
            Assert.That(row.EndTime, Is.GreaterThan(DateTime.UtcNow));
            Assert.That(row.Data, Is.SameAs(Data));
        });
    }

    [Test]
    public void TimesToTheMinute() {
        GameEventMetadata row = ServerTableMapper.MapGameEvent(1, Row("2023-03-20-00-00", "2099-04-20-23-59"), GameEventType.MiniGameBonusReward, Data);
        Assert.Multiple(() => {
            Assert.That(row.StartTime, Is.EqualTo(new DateTime(2023, 3, 20, 0, 0, 0)));
            Assert.That(row.EndTime, Is.EqualTo(new DateTime(2099, 4, 20, 23, 59, 0)));
        });
    }

    [Test]
    public void UnreadableTime_Ended() {
        GameEventMetadata row = ServerTableMapper.MapGameEvent(1, Row("2023-03-20", "2099-04-20"), GameEventType.MiniGameBonusReward, Data);
        Assert.That(row.EndTime, Is.LessThan(DateTime.UtcNow));
    }

    // A partTime listing several windows (18 KR rows, e.g. the ShutdownMapleSurvival "00:00:00-18:59:59,22:00:01-23:59:59")
    // keeps each window; it was read as no part time, so the event ran all day.
    [Test]
    public void SeveralPartTimes_KeepEachWindow() {
        GameEvent data = Row("2023-03-20-00-00-00", "2099-04-20-23-59-59");
        data.partTime = "00:00:00-18:59:59,22:00:01-23:59:59";
        GameEventMetadata row = ServerTableMapper.MapGameEvent(1, data, GameEventType.MiniGameBonusReward, Data);
        Assert.Multiple(() => {
            Assert.That(row.PartTimes, Is.EqualTo(new[] {
                new PartTimeWindow(TimeSpan.Zero, new TimeSpan(18, 59, 59)),
                new PartTimeWindow(new TimeSpan(22, 0, 1), new TimeSpan(23, 59, 59)),
            }));
            Assert.That(row.StartPartTime, Is.EqualTo(TimeSpan.Zero));
            Assert.That(row.EndPartTime, Is.EqualTo(new TimeSpan(23, 59, 59)));
        });

        var gameEvent = new Maple2.Model.Game.Event.GameEvent(row);
        var day = new DateTime(2030, 6, 15);
        Assert.Multiple(() => {
            Assert.That(gameEvent.IsActive(day.AddHours(10)), Is.True);
            Assert.That(gameEvent.IsActive(day.Add(new TimeSpan(18, 59, 59))), Is.True);
            Assert.That(gameEvent.IsActive(day.AddHours(19)), Is.False);
            Assert.That(gameEvent.IsActive(day.AddHours(22)), Is.False);
            Assert.That(gameEvent.IsActive(day.Add(new TimeSpan(22, 0, 1))), Is.True);
            Assert.That(gameEvent.IsActive(day.AddHours(23.5)), Is.True);
        });
    }

    [TestCase("14:00:00-15:59:59", 0)]
    [TestCase("", 0)]
    [TestCase("16:00:00-16:10:00,18:00:00-18:10:00,20:00:00-20:10:00,22:00:00-22:10:00", 4)]
    public void PartTimes_ListedOnlyForSeveralWindows(string partTime, int count) {
        GameEvent data = Row("2023-03-20-00-00-00", "2099-04-20-23-59-59");
        data.partTime = partTime;
        GameEventMetadata row = ServerTableMapper.MapGameEvent(1, data, GameEventType.MiniGameBonusReward, Data);
        Assert.That(row.PartTimes?.Length ?? 0, Is.EqualTo(count));
        if (partTime.Length > 0) {
            Assert.That(row.StartPartTime, Is.EqualTo(TimeSpan.Parse(partTime.Split('-')[0])));
            Assert.That(row.EndPartTime, Is.EqualTo(TimeSpan.Parse(partTime.Split('-')[^1])));
        }
    }
}
