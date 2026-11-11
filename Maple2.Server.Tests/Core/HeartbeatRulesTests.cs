using System;
using Maple2.Server.Core.Helpers;

namespace Maple2.Server.Tests.Core;

public class HeartbeatRulesTests {
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    // The old channel has no session for a player who moved on: its answer says nothing once the record names
    // the new channel.
    [TestCase(1, 1, true)]
    [TestCase(1, 2, false)]
    [TestCase(1, -1, false)]
    public void OnlyTheRecordedChannelCounts(int askedChannel, int recordedChannel, bool expected) {
        Assert.That(HeartbeatRules.StillOn(askedChannel, recordedChannel), Is.EqualTo(expected));
    }

    [TestCase(1, 1, false, true)]
    [TestCase(1, 1, true, false)]
    [TestCase(1, 2, false, false)]
    [TestCase(1, 2, true, false)]
    public void AMissDoesNotCountWhileThePlayerLeavesThatChannel(int askedChannel, int recordedChannel, bool leaving, bool expected) {
        Assert.That(HeartbeatRules.MarksOffline(askedChannel, recordedChannel, leaving), Is.EqualTo(expected));
    }

    // The order of the live case: leave channel 1, the heartbeat of channel 1 misses, login on channel 2, and a
    // late miss of channel 1. The player is never marked offline.
    [Test]
    public void ChannelChange_IsNeverMarkedOffline() {
        var tracker = new MigrationTracker(Lifetime);
        int recorded = 1;

        tracker.Begin(7, recorded, Start);
        DateTime miss = Start.AddSeconds(1);
        Assert.That(HeartbeatRules.MarksOffline(1, recorded, tracker.IsLeaving(7, 1, miss)), Is.False);

        recorded = 2; // the login on channel 2 writes the record
        DateTime lateMiss = Start.AddSeconds(2);
        Assert.That(HeartbeatRules.MarksOffline(1, recorded, tracker.IsLeaving(7, 1, lateMiss)), Is.False);

        // On the new channel a miss counts at once: the entry names the channel that was left.
        Assert.That(HeartbeatRules.MarksOffline(2, recorded, tracker.IsLeaving(7, 2, lateMiss)), Is.True);
    }

    // A player who never arrives is marked offline by the first miss after the token ran out.
    [Test]
    public void ChannelChange_ThatNeverArrives_CountsAfterTheTokenLifetime() {
        var tracker = new MigrationTracker(Lifetime);
        tracker.Begin(7, 1, Start);

        Assert.That(tracker.IsLeaving(7, 1, Start + Lifetime - TimeSpan.FromMilliseconds(1)), Is.True);
        Assert.That(tracker.IsLeaving(7, 1, Start + Lifetime), Is.False);
        Assert.That(HeartbeatRules.MarksOffline(1, 1, tracker.IsLeaving(7, 1, Start + Lifetime)), Is.True);
    }

    [Test]
    public void Tracker_KnowsOnlyWhoLeft() {
        var tracker = new MigrationTracker(Lifetime);
        tracker.Begin(7, 1, Start);
        tracker.Begin(0, 1, Start); // a login without a character

        Assert.That(tracker.IsLeaving(8, 1, Start), Is.False);
        Assert.That(tracker.IsLeaving(0, 1, Start), Is.False);
        Assert.That(tracker.IsLeaving(7, 2, Start), Is.False);
        Assert.That(tracker.IsLeaving(7, 1, Start.AddSeconds(-1)), Is.False);
    }
}
