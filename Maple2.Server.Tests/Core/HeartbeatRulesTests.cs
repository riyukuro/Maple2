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

        tracker.Begin(7, recorded, 2, Start);
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
        tracker.Begin(7, 1, 2, Start);

        Assert.That(tracker.IsLeaving(7, 1, Start + Lifetime - TimeSpan.FromMilliseconds(1)), Is.True);
        Assert.That(tracker.IsLeaving(7, 1, Start + Lifetime), Is.False);
        Assert.That(HeartbeatRules.MarksOffline(1, 1, tracker.IsLeaving(7, 1, Start + Lifetime)), Is.True);
    }

    // The channel a player goes to, only for the channel they left and only while the token is valid.
    [Test]
    public void Tracker_KnowsTheTarget() {
        var tracker = new MigrationTracker(Lifetime);
        tracker.Begin(7, 1, 2, Start);

        Assert.Multiple(() => {
            Assert.That(tracker.TargetOf(7, 1, Start), Is.EqualTo(2));
            Assert.That(tracker.TargetOf(7, 2, Start), Is.EqualTo(-1));
            Assert.That(tracker.TargetOf(8, 1, Start), Is.EqualTo(-1));
            Assert.That(tracker.TargetOf(7, 1, Start + Lifetime), Is.EqualTo(-1));
        });
    }

    [Test]
    public void Tracker_KnowsOnlyWhoLeft() {
        var tracker = new MigrationTracker(Lifetime);
        tracker.Begin(7, 1, 2, Start);
        tracker.Begin(0, 1, 2, Start); // a login without a character

        Assert.That(tracker.IsLeaving(8, 1, Start), Is.False);
        Assert.That(tracker.IsLeaving(0, 1, Start), Is.False);
        Assert.That(tracker.IsLeaving(7, 2, Start), Is.False);
        Assert.That(tracker.IsLeaving(7, 1, Start.AddSeconds(-1)), Is.False);
    }

    // The call MigrateOut makes: the channel on record and the channel of the token. A move that stays on the
    // channel is not a channel change, and a miss there counts at once.
    [Test]
    public void Begin_SkipsAMoveInsideOneChannel() {
        var tracker = new MigrationTracker(Lifetime);
        tracker.Begin(7, 1, 1, Start);

        Assert.That(tracker.Count, Is.EqualTo(0));
        Assert.That(HeartbeatRules.MarksOffline(1, 1, tracker.IsLeaving(7, 1, Start)), Is.True);
    }

    // The entry leaves when the record's channel is written (arrival or logout), not only on a later miss.
    [Test]
    public void End_RemovesTheEntry() {
        var tracker = new MigrationTracker(Lifetime);
        tracker.Begin(7, 1, 2, Start);
        tracker.Begin(8, 1, 2, Start);
        Assert.That(tracker.Count, Is.EqualTo(2));

        tracker.End(7);
        tracker.End(9); // unknown: nothing happens
        Assert.That(tracker.Count, Is.EqualTo(1));
        Assert.That(tracker.IsLeaving(7, 1, Start), Is.False);
        Assert.That(tracker.IsLeaving(8, 1, Start), Is.True);
    }

    // The cut is decided when a heartbeat is due, so it comes 90 to 120 s after the client's last packet: a client
    // that stands still for 100 s is cut only when a round falls into seconds 90 to 100 of that time.
    [TestCase(5_000, 100_000, false)] // rounds at 25, 55, 85 and 115 s of the stall: back before the fourth
    [TestCase(25_000, 100_000, true)] // rounds at 5, 35, 65 and 95 s: the fourth finds 95 s of silence
    [TestCase(5_000, 180_000, true)]
    [TestCase(29_000, 180_000, true)]
    public void AStallIsCutAtTheFirstRoundAfterTheLimit(long lastPacketAfterRoundMs, long stallMs, bool cut) {
        long lastReceived = lastPacketAfterRoundMs;
        int unanswered = 0;
        bool wasCut = false;
        for (long round = HeartbeatRules.IntervalMs; round <= lastReceived + stallMs; round += HeartbeatRules.IntervalMs) {
            if (HeartbeatRules.IsSilent(lastReceived, unanswered, round)) {
                wasCut = true;
                break;
            }
            unanswered++;
        }

        Assert.That(wasCut, Is.EqualTo(cut));
    }
}
