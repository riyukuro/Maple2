using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The channel's game events and the edges of the events whose day of the week or time of day limits them: each
// receiver gets an event's Add and Remove once, whether it loads before, at or after an edge.
public class ChannelGameEventsTests {
    // A Saturday; the KR 550001 row is on Saturdays 10:55:10-12:00.
    private static readonly DateTime Saturday = new(2026, 11, 14);
    private static readonly DateTime Before = Saturday.AddHours(10).AddMinutes(55);
    private static readonly DateTime During = Saturday.AddHours(11);
    private static readonly DateTime After = Saturday.AddHours(12).AddMinutes(1);

    private sealed class Receiver {
        public readonly List<GameEvent> Got = [];
    }

    private static GameEvent VsGameOpen(int id = 550001) => new(new GameEventMetadata(id, GameEventType.GuildVsGameOpen,
        new DateTime(2026, 1, 1), new DateTime(2027, 1, 1), new TimeSpan(10, 55, 10), new TimeSpan(12, 0, 0), [DayOfWeek.Saturday],
        new GuildVsGameOpen(), "", "", "", ""));

    private static GameEvent Plain(int id) => new(new GameEventMetadata(id, GameEventType.GuildVsGameOpen,
        new DateTime(2026, 1, 1), new DateTime(2027, 1, 1), TimeSpan.Zero, TimeSpan.Zero, [], new GuildVsGameOpen(), "", "", "", ""));

    private static ChannelGameEvents<Receiver> Events(DateTime now, params GameEvent[] events) =>
        new(events.ToDictionary(gameEvent => gameEvent.Id), now);

    [Test]
    public void OnlyDayOrTimeLimitedEventsHaveEdges() {
        Assert.Multiple(() => {
            Assert.That(ChannelGameEvents<Receiver>.HasEdges(VsGameOpen()), Is.True);
            Assert.That(ChannelGameEvents<Receiver>.HasEdges(Plain(1)), Is.False);
        });
    }

    [Test]
    public void EdgesAreFoundOnce() {
        ChannelGameEvents<Receiver> events = Events(Before, VsGameOpen(), Plain(1));
        var receiver = new Receiver();
        events.Load(receiver, Before, receiver.Got.AddRange);
        Assert.That(receiver.Got.Select(gameEvent => gameEvent.Id), Is.EqualTo(new[] { 1 }));

        Assert.That(events.Tick(Before).Any, Is.False);
        ChannelGameEvents<Receiver>.Edges start = events.Tick(During);
        Assert.Multiple(() => {
            Assert.That(start.Started.Select(gameEvent => gameEvent.Id), Is.EqualTo(new[] { 550001 }));
            Assert.That(start.Stopped, Is.Empty);
            Assert.That(start.Receivers, Is.EqualTo(new[] { receiver }));
        });
        // No second start while it stays on.
        Assert.That(events.Tick(During.AddMinutes(1)).Any, Is.False);

        ChannelGameEvents<Receiver>.Edges stop = events.Tick(After);
        Assert.Multiple(() => {
            Assert.That(stop.Started, Is.Empty);
            Assert.That(stop.Stopped.Select(gameEvent => gameEvent.Id), Is.EqualTo(new[] { 550001 }));
            Assert.That(stop.Receivers, Is.EqualTo(new[] { receiver }));
        });
        Assert.That(events.Tick(After.AddMinutes(1)).Any, Is.False);
    }

    // A receiver loading when the edge has passed but no tick has found it yet gets the event from the tick, not
    // in its load list too.
    [Test]
    public void LoadAtTheEdgeIsNotSentTwice() {
        ChannelGameEvents<Receiver> events = Events(Before, VsGameOpen());
        var receiver = new Receiver();
        events.Load(receiver, During, receiver.Got.AddRange);
        Assert.That(receiver.Got, Is.Empty);

        ChannelGameEvents<Receiver>.Edges start = events.Tick(During);
        Assert.That(start.Started, Has.Count.EqualTo(1));
        Assert.That(start.Receivers, Is.EqualTo(new[] { receiver }));

        // Loaded after the tick: in the load list, and the next tick sends nothing.
        var late = new Receiver();
        events.Load(late, During, late.Got.AddRange);
        Assert.That(late.Got.Select(gameEvent => gameEvent.Id), Is.EqualTo(new[] { 550001 }));
        Assert.That(events.Tick(During).Any, Is.False);
    }

    [Test]
    public void DroppedReceiverGetsNoEdges() {
        ChannelGameEvents<Receiver> events = Events(Before, VsGameOpen());
        var receiver = new Receiver();
        events.Load(receiver, Before, receiver.Got.AddRange);
        events.Drop(receiver);
        ChannelGameEvents<Receiver>.Edges start = events.Tick(During);
        Assert.That(start.Started, Has.Count.EqualTo(1));
        Assert.That(start.Receivers, Is.Empty);
    }

    // The World's Add and Remove of an edge event outside its day or time send nothing; the tick sends it.
    [Test]
    public void AddAndRemoveWhileOff() {
        ChannelGameEvents<Receiver> events = Events(Before);
        var receiver = new Receiver();
        events.Load(receiver, Before, receiver.Got.AddRange);

        Assert.That(events.Add(VsGameOpen(), Before), Is.Empty);
        Assert.That(events.Add(VsGameOpen(), Before), Is.Null);
        Assert.That(events.Find(550001, Before), Is.Null);
        ChannelGameEvents<Receiver>.Edges start = events.Tick(During);
        Assert.That(start.Started, Has.Count.EqualTo(1));
        Assert.That(events.Find(550001, During), Is.Not.Null);
        Assert.That(events.Tick(After).Stopped, Has.Count.EqualTo(1));

        (GameEvent Event, List<Receiver> Receivers)? removed = events.Remove(550001);
        Assert.That(removed, Is.Not.Null);
        Assert.That(removed!.Value.Receivers, Is.Empty);
        Assert.That(events.Remove(550001), Is.Null);
        Assert.That(events.Tick(During.AddDays(7)).Any, Is.False);
    }

    [Test]
    public void AddAndRemoveWhileOn() {
        ChannelGameEvents<Receiver> events = Events(During);
        var receiver = new Receiver();
        events.Load(receiver, During, receiver.Got.AddRange);

        Assert.That(events.Add(VsGameOpen(), During), Is.EqualTo(new[] { receiver }));
        Assert.That(events.Tick(During).Any, Is.False);
        Assert.That(events.Remove(550001)!.Value.Receivers, Is.EqualTo(new[] { receiver }));
        // A plain event goes to every receiver.
        Assert.That(events.Add(Plain(1), Before), Is.EqualTo(new[] { receiver }));
        Assert.That(events.Remove(1)!.Value.Receivers, Is.EqualTo(new[] { receiver }));
    }
}
