using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using NUnit.Framework;

namespace Maple2.Server.Tests.Ingest;

// The mentoring season out of the box: when no season row runs or is to come, the seasons after the last one.
public class DefaultMentoringSeasonTests {
    private static readonly DateTime Now = new(2026, 11, 14, 19, 30, 0);
    private static readonly int[] Quests = [74000001, 74000002];

    // KR 180117: season 10, 2019-04-11 07:00 to 2019-06-20 07:00 (70 days), 30 days away.
    private static GameEventMetadata Season(int id, int season, DateTime start, DateTime end) {
        return new GameEventMetadata(id, GameEventType.ReturnUser, start, end, TimeSpan.Zero, TimeSpan.Zero, [],
            new ReturnUser(season, Quests, 10003, DateTimeOffset.MinValue, 30, 0), "30", "10003", season.ToString(), "74000001,74000002");
    }

    private static readonly GameEventMetadata Last = Season(180117, 10, new DateTime(2019, 4, 11, 7, 0, 0), new DateTime(2019, 6, 20, 7, 0, 0));
    private static readonly GameEventMetadata Earlier = Season(180116, 9, new DateTime(2019, 2, 7, 7, 0, 0), new DateTime(2019, 4, 11, 7, 0, 0));
    // A ReturnUser row without a season (a returner mark, KR 180119) is no season.
    private static readonly GameEventMetadata Mark = new(180119, GameEventType.ReturnUser, new DateTime(2019, 8, 1), new DateTime(2019, 8, 22),
        TimeSpan.Zero, TimeSpan.Zero, [], new ReturnUser(0, [], 10006, new DateTimeOffset(new DateTime(2019, 4, 30)), 0, 0), "", "10006", "", "");

    // No season row runs: the season running now is added, then the ones after it to the horizon, each a season
    // length after the one before and numbered on from the last row, with that row's rule and quests.
    [Test]
    public void NoLiveRow_TheSeasonsAfterTheLast() {
        List<GameEventMetadata> rows = DefaultMentoringSeason.Rows([Earlier, Mark, Last], Now, true, new HashSet<int>());
        TimeSpan length = TimeSpan.FromDays(70);
        GameEventMetadata running = rows[0];
        var data = (ReturnUser) running.Data;
        Assert.Multiple(() => {
            Assert.That(running.StartTime, Is.LessThanOrEqualTo(Now));
            Assert.That(running.EndTime, Is.GreaterThan(Now));
            Assert.That(running.EndTime - running.StartTime, Is.EqualTo(length));
            Assert.That((running.StartTime - Last.EndTime).Ticks % length.Ticks, Is.Zero);
            // Seasons 11 onwards start at the last row's end: 2026-11-14 is in the 39th season after it (season 49).
            long k = (running.StartTime - Last.EndTime).Ticks / length.Ticks + 1;
            Assert.That(data.SeasonId, Is.EqualTo(10 + k));
            Assert.That((data.SeasonId, running.StartTime, running.EndTime), Is.EqualTo((49, new DateTime(2026, 10, 1, 7, 0, 0), new DateTime(2026, 12, 10, 7, 0, 0))));
            Assert.That(running.Id, Is.EqualTo(DefaultMentoringSeason.IdBase + data.SeasonId));
            Assert.That(running.Value3, Is.EqualTo(data.SeasonId.ToString()));
            Assert.That((data.QuestIds, data.DaysInactive, data.RequiredUserValue), Is.EqualTo((Quests, 30, 10003)));
            Assert.That(running.Type, Is.EqualTo(GameEventType.ReturnUser));
            for (int i = 1; i < rows.Count; i++) {
                Assert.That(rows[i].StartTime, Is.EqualTo(rows[i - 1].EndTime));
                Assert.That(((ReturnUser) rows[i].Data).SeasonId, Is.EqualTo(((ReturnUser) rows[i - 1].Data).SeasonId + 1));
            }
            Assert.That(rows[^1].StartTime, Is.LessThanOrEqualTo(Now + DefaultMentoringSeason.Horizon));
            Assert.That(rows[^1].EndTime, Is.GreaterThan(Now + DefaultMentoringSeason.Horizon));
            // A later ingest gives the same season for the same time.
            GameEventMetadata later = DefaultMentoringSeason.Rows([Last, Earlier], Now.AddDays(30), true, new HashSet<int>())
                .Single(row => row.Id == rows[1].Id);
            Assert.That((later.StartTime, later.EndTime), Is.EqualTo((rows[1].StartTime, rows[1].EndTime)));
        });
    }

    // The server runs the added season as the data's own: with only the ended rows a session has no season, with the
    // rows added it has the one running now, which the client's GameEvent load gets (its season number > 0 opens
    // the Community menu's Mentoring entry).
    [Test]
    public void AddedRows_TheServerRunsTheSeason() {
        DateTime now = DateTime.Now;
        var bare = new TestGameSession();
        bare.AddEvent(Last.Id, Last.Type, Last.Data, Last.StartTime, Last.EndTime);
        bare.Start();
        Assert.That(bare.Session.Mentoring.SeasonRuns, Is.False);

        var test = new TestGameSession();
        test.AddEvent(Last.Id, Last.Type, Last.Data, Last.StartTime, Last.EndTime);
        foreach (GameEventMetadata row in DefaultMentoringSeason.Rows([Last], now, true, new HashSet<int>()).Take(2)) {
            test.AddEvent(row.Id, row.Type, row.Data, row.StartTime, row.EndTime);
        }
        test.Start();
        GameEvent season = test.Session.FindEvent(GameEventType.ReturnUser).Single();
        Assert.Multiple(() => {
            Assert.That(test.Session.Mentoring.SeasonRuns, Is.True);
            Assert.That(((ReturnUser) season.Metadata.Data).SeasonId, Is.GreaterThan(10));
            Assert.That(season.Id, Is.EqualTo(DefaultMentoringSeason.IdBase + ((ReturnUser) season.Metadata.Data).SeasonId));
        });
    }

    // A season row that runs or is still to come is the season: nothing is added.
    [Test]
    public void LiveOrComingRow_NoneAdded() {
        GameEventMetadata live = Season(180200, 11, Now.AddDays(-10), Now.AddDays(60));
        GameEventMetadata coming = Season(180201, 11, Now.AddDays(10), Now.AddDays(80));
        Assert.Multiple(() => {
            Assert.That(DefaultMentoringSeason.Rows([Last, live], Now, true, new HashSet<int>()), Is.Empty);
            Assert.That(DefaultMentoringSeason.Rows([Last, coming], Now, true, new HashSet<int>()), Is.Empty);
        });
    }

    // The Mentoring feature off (the operator's feature.xml), or no season row in the data: none.
    [Test]
    public void FeatureOffOrNoSeasonRow_None() {
        Assert.Multiple(() => {
            Assert.That(DefaultMentoringSeason.Rows([Last], Now, false, new HashSet<int>()), Is.Empty);
            Assert.That(DefaultMentoringSeason.Rows([Mark], Now, true, new HashSet<int>()), Is.Empty);
            Assert.That(DefaultMentoringSeason.Rows([], Now, true, new HashSet<int>()), Is.Empty);
            // A last row without a readable end (MinValue) adds none.
            Assert.That(DefaultMentoringSeason.Rows([Last with { EndTime = DateTime.MinValue }], Now, true, new HashSet<int>()), Is.Empty);
        });
    }

    // An id the data already has is not taken.
    [Test]
    public void TakenId_Skipped() {
        List<GameEventMetadata> rows = DefaultMentoringSeason.Rows([Last], Now, true, new HashSet<int>());
        List<GameEventMetadata> without = DefaultMentoringSeason.Rows([Last], Now, true, new HashSet<int> { rows[0].Id });
        Assert.That(without.Select(row => row.Id), Is.EqualTo(rows.Skip(1).Select(row => row.Id)));
    }
}
