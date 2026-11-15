using System;
using System.Collections.Generic;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
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

    [Test]
    public void Pick_MovesTheNamedEndedEventToTheIngestDay() {
        // KR 320002: 2017-08-03 07:00 to 2017-08-24 07:00.
        GameEventMetadata restart = Event(320002, "2017-08-03 07:00", "2017-08-24 07:00");
        GameEventMetadata other = Event(320001, "2016-12-15 07:00", "2017-02-09 07:00");
        DateTime now = new(2026, 11, 14, 15, 30, 0);

        (List<GameEventMetadata> replayed, List<int> missing) = EventReplay.Pick([320002], [other, restart], _ => false, now);

        Assert.That(missing, Is.Empty);
        Assert.That(replayed, Has.Count.EqualTo(1));
        Assert.That(replayed[0].Id, Is.EqualTo(320002));
        Assert.That(replayed[0].StartTime, Is.EqualTo(new DateTime(2026, 11, 14, 7, 0, 0)));
        Assert.That(replayed[0].EndTime, Is.EqualTo(new DateTime(2026, 12, 5, 7, 0, 0)));
        Assert.That(replayed[0].StartTime <= now && now < replayed[0].EndTime, Is.True);
    }

    [Test]
    public void Pick_LeavesKeptEventsAndNamesUnknownIds() {
        GameEventMetadata restart = Event(320002, "2017-08-03 07:00", "2017-08-24 07:00");

        (List<GameEventMetadata> replayed, List<int> missing) = EventReplay.Pick([320002, 320001, 5], [restart],
            id => id == 5, new DateTime(2026, 11, 14));

        // 5 is already in the table (running); 320001 was dropped before (no map), so it is not an ended event.
        Assert.That(replayed.ConvertAll(e => e.Id), Is.EqualTo(new[] { 320002 }));
        Assert.That(missing, Is.EqualTo(new[] { 320001 }));
    }
}
