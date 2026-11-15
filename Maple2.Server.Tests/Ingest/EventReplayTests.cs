using System;
using System.Collections.Generic;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

// MS2_REPLAY_EVENTS: the ended events of the listed ids are kept and moved to start on the ingest day.
public class EventReplayTests {
    private static GameEventMetadata Event(int id, string start, string end) {
        return new GameEventMetadata(id, GameEventType.EpicRestart, DateTime.Parse(start), DateTime.Parse(end), TimeSpan.Zero, TimeSpan.Zero, [],
            new AttendGift([], "", "", "", "", 0, null), "", "", "", "");
    }

    [Test]
    public void Ids_ReadsTheListAndRefusesATypo() {
        Assert.That(EventReplay.Ids(null), Is.Empty);
        Assert.That(EventReplay.Ids(" "), Is.Empty);
        Assert.That(EventReplay.Ids("320002"), Is.EqualTo(new[] { 320002 }));
        Assert.That(EventReplay.Ids("320002, 99000056 320002"), Is.EqualTo(new[] { 320002, 99000056 }));
        Assert.Throws<ArgumentException>(() => EventReplay.Ids("320002,epic"));
        Assert.Throws<ArgumentException>(() => EventReplay.Ids("0"));
    }

    private static readonly DateTime Now = new(2026, 11, 14, 15, 30, 0);

    private static List<EventReplay.Result> Pick(IReadOnlyCollection<int> ids, IReadOnlyCollection<GameEventMetadata> ended,
            Func<int, bool>? kept = null, Func<int, int>? clientRank = null) {
        return EventReplay.Pick(ids, ended, kept ?? (_ => false), Now, clientRank ?? (_ => 1), _ => true);
    }

    [Test]
    public void Pick_MovesTheNamedEndedEventToTheIngestDay() {
        // KR 320002: 2017-08-03 07:00 to 2017-08-24 07:00.
        GameEventMetadata restart = Event(320002, "2017-08-03 07:00", "2017-08-24 07:00");
        GameEventMetadata other = Event(320001, "2016-12-15 07:00", "2017-02-09 07:00");

        List<EventReplay.Result> results = Pick([320002], [other, restart]);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Outcome, Is.EqualTo(EventReplay.Outcome.Replayed));
        GameEventMetadata moved = results[0].Moved!;
        Assert.That(moved.Id, Is.EqualTo(320002));
        Assert.That(moved.StartTime, Is.EqualTo(new DateTime(2026, 11, 14, 7, 0, 0)));
        Assert.That(moved.EndTime, Is.EqualTo(new DateTime(2026, 12, 5, 7, 0, 0)));
        Assert.That(moved.StartTime <= Now && Now < moved.EndTime, Is.True);
    }

    [Test]
    public void Pick_LeavesKeptEventsAndNamesUnknownIds() {
        GameEventMetadata restart = Event(320002, "2017-08-03 07:00", "2017-08-24 07:00");

        List<EventReplay.Result> results = Pick([320002, 320001, 5], [restart], kept: id => id == 5);

        // 5 is already in the table (running); 320001 was dropped before (no map), so it is not an ended event.
        Assert.That(results.ConvertAll(r => (r.Id, r.Outcome)), Is.EqualTo(new[] {
            (320002, EventReplay.Outcome.Replayed), (320001, EventReplay.Outcome.Missing), (5, EventReplay.Outcome.Kept),
        }));
        EventReplay.Result kept = results.Find(r => r.Id == 5);
        Assert.That(kept.Moved, Is.Null);
        // Every id gets a line, the kept one too.
        Assert.That(EventReplay.Line(kept), Does.Contain("5").And.Contain("already in the table"));
        Assert.That(results.TrueForAll(r => !string.IsNullOrEmpty(EventReplay.Line(r))), Is.True);
    }

    // A named AttendGift goes through the same client-rank test as MS2_REPLAY_ATTEND_GIFT: an item without a rank in the
    // client's item data would make the client assert when the gift list is sent.
    [Test]
    public void Pick_RefusesANamedGiftWithAnItemTheClientHasNoRankFor() {
        GameEventMetadata gift = new GameEventMetadata(99000056, GameEventType.AttendGift, DateTime.Parse("2025-01-01 00:00"),
            DateTime.Parse("2025-01-05 23:59"), TimeSpan.Zero, TimeSpan.Zero, [],
            new AttendGift([new RewardItem(900101, 5, 1), new RewardItem(900201, 5, 1)], "", "", "", "", 600, null), "", "", "", "");
        var ranks = new Dictionary<int, int> { [900101] = 3 };

        List<EventReplay.Result> refused = Pick([99000056], [gift], clientRank: itemId => ranks.GetValueOrDefault(itemId));
        Assert.That(refused[0].Outcome, Is.EqualTo(EventReplay.Outcome.NoRewards));
        Assert.That(refused[0].Moved, Is.Null);
        Assert.That(EventReplay.Line(refused[0]), Does.Contain("99000056"));

        ranks[900201] = 1;
        List<EventReplay.Result> replayed = Pick([99000056], [gift], clientRank: itemId => ranks.GetValueOrDefault(itemId));
        Assert.That(replayed[0].Outcome, Is.EqualTo(EventReplay.Outcome.Replayed));
        Assert.That(replayed[0].Moved!.StartTime <= Now && Now < replayed[0].Moved!.EndTime, Is.True);
    }
}
