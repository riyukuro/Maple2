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
}
