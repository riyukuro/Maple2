using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Validators;

public class DungeonScoreRulesTests {
    // The fameChallenge rank table (group 5): C at 500, B 1000, A 1500, S 2000, S+ 2500; below 500 is F.
    [TestCase(0, DungeonMissionRank.F)]
    [TestCase(499, DungeonMissionRank.F)]
    [TestCase(500, DungeonMissionRank.C)]
    [TestCase(1600, DungeonMissionRank.A)]
    [TestCase(2500, DungeonMissionRank.SPlus)]
    public void Rank_IsTheHighestScoreReached(int score, DungeonMissionRank expected) {
        DungeonMissionRankMetadata.Score[] scores = [new(DungeonMissionRank.C, 500), new(DungeonMissionRank.B, 1000),
            new(DungeonMissionRank.A, 1500), new(DungeonMissionRank.S, 2000), new(DungeonMissionRank.SPlus, 2500)];
        Assert.That(DungeonScoreRules.Rank(scores, (short) score), Is.EqualTo(expected));
    }

    [TestCase(0, DungeonGroupType.normal, 1)]
    [TestCase(0, DungeonGroupType.raid, 2)]
    [TestCase(0, DungeonGroupType.chaosRaid, 3)]
    [TestCase(0, DungeonGroupType.reverseRaid, 4)]
    [TestCase(0, DungeonGroupType.fameChallenge, 5)]
    [TestCase(0, DungeonGroupType.worldBossDungeon, 6)]
    [TestCase(0, DungeonGroupType.darkStream, 0)] // no rank table
    [TestCase(0, DungeonGroupType.colosseum, 0)]
    [TestCase(7, DungeonGroupType.chaosRaid, 7)] // rankTableID wins
    public void RankTable(int rankTableId, DungeonGroupType groupType, int expected) {
        Assert.That(DungeonScoreRules.RankTableId(rankTableId, groupType), Is.EqualTo(expected));
    }

    [Test]
    public void ScoreIsTheSumOfTheMissions() {
        Assert.That(DungeonScoreRules.RunScore(new short[] { 300, 400, 260 }, 2400), Is.EqualTo(960));
        Assert.That(DungeonScoreRules.RunScore(Array.Empty<short>(), 2400), Is.EqualTo(0));
    }

    [Test]
    public void ScoreIsCappedAtMaxScore() {
        Assert.That(DungeonScoreRules.RunScore(new short[] { 800, 800, 800, 800 }, 2400), Is.EqualTo(2400));
        Assert.That(DungeonScoreRules.RunScore(new short[] { 700, 600 }, 1200), Is.EqualTo(1200));
    }

    [Test]
    public void LifetimeStartsAtMinusOneAndKeepsTheBest() {
        var record = new DungeonRecord(900001);
        Assert.That(record.LifetimeRecord, Is.EqualTo(-1));

        DungeonScoreRules.ApplyLifetime(record, 0);
        Assert.That(record.LifetimeRecord, Is.EqualTo(0)); // a scored clear, even of 0
        DungeonScoreRules.ApplyLifetime(record, 960);
        Assert.That(record.LifetimeRecord, Is.EqualTo(960));
        DungeonScoreRules.ApplyLifetime(record, 558);
        Assert.That(record.LifetimeRecord, Is.EqualTo(960));
    }

    [Test]
    public void NewRecordWritesMinusOne() {
        var writer = new ByteWriter();
        writer.WriteClass(new DungeonRecord(900001));
        var reader = new ByteReader(writer.ToArray(), 0);
        reader.Skip(4 + 8 + 1 + 1 + 8 + 1 + 1 + 8 + 4);
        Assert.That(reader.ReadShort(), Is.EqualTo(-1)); // LifetimeRecord
        Assert.That(reader.ReadShort(), Is.EqualTo(0)); // CurrentRecord
        Assert.That(reader.ReadLong(), Is.EqualTo(0)); // its expiry
    }

    [TestCase(DungeonGroupType.chaosRaid, 60, 60, true)]
    [TestCase(DungeonGroupType.chaosRaid, 70, 60, true)]
    [TestCase(DungeonGroupType.chaosRaid, 50, 60, false)]
    [TestCase(DungeonGroupType.chaosRaid, 70, 99, false)] // a locale with min level 99 has no season room
    [TestCase(DungeonGroupType.raid, 70, 60, false)]
    [TestCase(DungeonGroupType.normal, 70, 60, false)]
    public void SeasonRankRoom(DungeonGroupType groupType, int limitPlayerLevel, int minLevel, bool expected) {
        Assert.That(DungeonScoreRules.IsSeasonRankRoom(groupType, limitPlayerLevel, minLevel), Is.EqualTo(expected));
    }

    [Test]
    public void SeasonKeepsTheBestOfTheWeek() {
        var record = new DungeonRecord(900002) { CurrentRecord = 500, CooldownTimestamp = 1000 };
        DungeonScoreRules.ApplySeason(record, 300, 900, 2000);
        Assert.That(record.CurrentRecord, Is.EqualTo(500));
        Assert.That(record.CooldownTimestamp, Is.EqualTo(2000));

        DungeonScoreRules.ApplySeason(record, 700, 1500, 2000);
        Assert.That(record.CurrentRecord, Is.EqualTo(700));
    }

    [Test]
    public void SeasonValueEndsAtItsExpiry() {
        var record = new DungeonRecord(900002) { CurrentRecord = 500, CooldownTimestamp = 1000 };
        DungeonScoreRules.ApplySeason(record, 300, 1000, 3000);
        Assert.That(record.CurrentRecord, Is.EqualTo(300));
        Assert.That(record.CooldownTimestamp, Is.EqualTo(3000));
    }

    // 2025-01-01 is a Wednesday. dayOfWeeks: Sunday = 1.
    [TestCase("2025-01-01 15:00", 6, "2025-01-03 00:00")] // Friday
    [TestCase("2025-01-03 10:00", 6, "2025-01-10 00:00")] // on the day: the next week's
    [TestCase("2025-01-03 00:00", 6, "2025-01-10 00:00")]
    [TestCase("2025-01-01 15:00", 1, "2025-01-05 00:00")] // Sunday
    [TestCase("2025-01-01 15:00", 7, "2025-01-04 00:00")] // Saturday
    [TestCase("2025-01-01 15:00", 0, "2025-01-02 00:00")] // none: the server's reset day, Thursday
    public void SeasonExpiryIsTheNextWeekEnd(string now, int dayOfWeeks, string expected) {
        Assert.That(DungeonScoreRules.SeasonExpiry(DateTime.Parse(now), dayOfWeeks, DateTime.MaxValue), Is.EqualTo(DateTime.Parse(expected)));
    }

    [Test]
    public void SeasonExpiryStopsAtTheSeasonEnd() {
        var end = new DateTime(2025, 1, 2, 23, 59, 59);
        Assert.That(DungeonScoreRules.SeasonExpiry(new DateTime(2025, 1, 1, 15, 0, 0), 6, end), Is.EqualTo(end));
    }

    private const string SeasonXml = """
        <ms2>
            <DungeonSeason seasonID="901" locale="KR" feature="Rank" startTime="2025-01-01-00-00-00" endTime="2025-06-30-23-59-59" maxSeasonScore="5000" systemMailID="900100" dayOfWeeks="6">
                <Reward requireScore="1000" itemID="900201" />
                <Reward requireScore="4000" itemID="900203" grade="3" count="2" />
                <Reward requireScore="2000" itemID="900202" />
            </DungeonSeason>
            <DungeonSeason seasonID="902" locale="KR" feature="Rank" startTime="2025-07-01-00-00-00" endTime="2999-12-31-23-59-59" maxSeasonScore="6000" systemMailID="900100" dayOfWeeks="6" />
            <DungeonSeason seasonID="903" locale="NA" feature="Rank" startTime="2025-01-01-00-00-00" endTime="2999-12-31-23-59-59" maxSeasonScore="7000" systemMailID="900100" dayOfWeeks="6" />
        </ms2>
        """;

    private static DungeonSeasonTable ReadSeasons(string locale) {
        var document = new XmlDocument();
        document.LoadXml(SeasonXml);
        return new DungeonSeasonTable(TableMapper.ReadDungeonSeasons(document, node => node.GetAttribute("locale") == locale));
    }

    [Test]
    public void SeasonRowsOfTheLocale() {
        DungeonSeasonTable kr = ReadSeasons("KR");
        Assert.That(kr.Entries, Has.Count.EqualTo(2));
        DungeonSeasonTable.Entry first = kr.Entries[0];
        Assert.That(first.SeasonId, Is.EqualTo(901));
        Assert.That(first.StartTime, Is.EqualTo(new DateTime(2025, 1, 1)));
        Assert.That(first.EndTime, Is.EqualTo(new DateTime(2025, 6, 30, 23, 59, 59)));
        Assert.That(first.MaxSeasonScore, Is.EqualTo(5000));
        Assert.That(first.SystemMailId, Is.EqualTo(900100));
        Assert.That(first.DayOfWeeks, Is.EqualTo(6));
        Assert.That(first.Rewards, Is.EqualTo(new[] {
            new DungeonSeasonTable.Reward(4000, 900203, 3, 2),
            new DungeonSeasonTable.Reward(2000, 900202),
            new DungeonSeasonTable.Reward(1000, 900201),
        }));

        DungeonSeasonTable na = ReadSeasons("NA");
        Assert.That(na.Entries, Has.Count.EqualTo(1));
        Assert.That(na.Entries[0].SeasonId, Is.EqualTo(903));
    }

    [Test]
    public void CurrentSeasonHoldsTheTime() {
        DungeonSeasonTable kr = ReadSeasons("KR");
        Assert.That(kr.Current(new DateTime(2025, 3, 1))?.SeasonId, Is.EqualTo(901));
        Assert.That(kr.Current(new DateTime(2025, 8, 1))?.SeasonId, Is.EqualTo(902));
        Assert.That(kr.Current(new DateTime(2024, 12, 31)), Is.Null);
    }

    [Test]
    public void SeasonRewardIsTheFirstTheScoreMeets() {
        DungeonSeasonTable.Entry season = ReadSeasons("KR").Entries[0];
        Assert.That(season.GetReward(5000)?.ItemId, Is.EqualTo(900203));
        Assert.That(season.GetReward(4000)?.ItemId, Is.EqualTo(900203));
        Assert.That(season.GetReward(3999)?.ItemId, Is.EqualTo(900202));
        Assert.That(season.GetReward(1000)?.ItemId, Is.EqualTo(900201));
        Assert.That(season.GetReward(999), Is.Null);
        Assert.That(ReadSeasons("KR").Entries[1].GetReward(5000), Is.Null);
    }

    [Test]
    public void SeasonTotalSumsPositiveScores() {
        Assert.That(DungeonScoreRules.SeasonTotal(new short[] { 1500, 0, -1, 2000 }, 0), Is.EqualTo(3500));
        Assert.That(DungeonScoreRules.SeasonTotal(new short[] { 3000, 3000 }, 5000), Is.EqualTo(5000));
        Assert.That(DungeonScoreRules.SeasonTotal(Array.Empty<short>(), 5000), Is.EqualTo(0));
    }

    [Test]
    public void FinishedWeeksGroupExpiredScores() {
        long weekEnd = new DateTimeOffset(new DateTime(2025, 1, 3)).ToUnixTimeSeconds();
        long nextEnd = new DateTimeOffset(new DateTime(2025, 1, 10)).ToUnixTimeSeconds();
        var records = new[] {
            new DungeonRecord(1) { CurrentRecord = 1500, CooldownTimestamp = weekEnd },
            new DungeonRecord(2) { CurrentRecord = 2000, CooldownTimestamp = weekEnd },
            new DungeonRecord(3) { CurrentRecord = 0, CooldownTimestamp = weekEnd },
            new DungeonRecord(4) { CurrentRecord = 900, CooldownTimestamp = nextEnd },
            new DungeonRecord(5) { CurrentRecord = 700, CooldownTimestamp = 0 },
        };

        Assert.That(DungeonScoreRules.FinishedWeeks(records, weekEnd - 1), Is.Empty);
        var weeks = new List<IGrouping<long, DungeonRecord>>(DungeonScoreRules.FinishedWeeks(records, weekEnd));
        Assert.That(weeks, Has.Count.EqualTo(1));
        Assert.That(weeks[0].Key, Is.EqualTo(weekEnd));
        Assert.That(weeks[0].Select(record => record.DungeonId), Is.EquivalentTo(new[] { 1, 2 }));

        weeks = new List<IGrouping<long, DungeonRecord>>(DungeonScoreRules.FinishedWeeks(records, nextEnd));
        Assert.That(weeks.Select(week => week.Key), Is.EqualTo(new[] { weekEnd, nextEnd }));
    }
}
