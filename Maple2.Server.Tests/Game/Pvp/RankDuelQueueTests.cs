using System.Linq;
using Maple2.Model.Game.Pvp;

namespace Maple2.Server.Tests.Game.Pvp;

// The World's rank duel queue (Pvp 14 register, 15 unregister, 16 join) on its own.
public class RankDuelQueueTests {
    [Test]
    public void TwoRegistered_AreMatchedInOrder() {
        var queue = new RankDuelQueue();
        Assert.That(queue.Register(1, 0, out RankDuelMatch? none), Is.EqualTo(RankDuelError.none));
        Assert.That(none, Is.Null);
        Assert.That(queue.IsQueued(1), Is.True);

        Assert.That(queue.Register(2, 5, out RankDuelMatch? match), Is.EqualTo(RankDuelError.none));
        Assert.That(match, Is.Not.Null);
        Assert.That((match!.First, match.Second, match.CreatedTick), Is.EqualTo((1L, 2L, 5L)));
        Assert.That(queue.IsQueued(1) || queue.IsQueued(2), Is.False);
        Assert.That(queue.MatchOf(1), Is.SameAs(match));
        Assert.That(queue.MatchOf(2), Is.SameAs(match));
    }

    [Test]
    public void TheLongestWaiting_ArePairedFirst() {
        var queue = new RankDuelQueue();
        queue.Register(1, 0, out _);
        queue.Register(2, 0, out RankDuelMatch? first);
        queue.Register(3, 0, out _);
        Assert.That(queue.Unregister(3), Is.EqualTo(RankDuelError.none));
        queue.Register(4, 0, out RankDuelMatch? none);
        Assert.That(none, Is.Null);
        queue.Register(5, 0, out RankDuelMatch? second);
        Assert.That(first!.Characters, Is.EqualTo(new long[] { 1, 2 }));
        Assert.That(second!.Characters, Is.EqualTo(new long[] { 4, 5 }));
        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
    }

    [Test]
    public void QueuedOrMatched_IsBusy() {
        var queue = new RankDuelQueue();
        queue.Register(1, 0, out _);
        Assert.That(queue.Register(1, 0, out _), Is.EqualTo(RankDuelError.Busy));
        queue.Register(2, 0, out _);
        Assert.That(queue.Register(2, 0, out _), Is.EqualTo(RankDuelError.Busy));
        Assert.That(queue.Unregister(2), Is.EqualTo(RankDuelError.NotQueued));
    }

    [Test]
    public void Join_NeedsTheMatchField() {
        var queue = new RankDuelQueue();
        queue.Register(1, 0, out _);
        queue.Register(2, 0, out RankDuelMatch? match);
        Assert.That(queue.Join(1, out _), Is.EqualTo(RankDuelError.NoMatch));
        Assert.That(queue.SetRoom(match!.Id, 1, 65010003, 77), Is.True);

        Assert.That(queue.Join(1, out RankDuelMatch? joined), Is.EqualTo(RankDuelError.none));
        Assert.That((joined!.Channel, joined.MapId, joined.RoomId), Is.EqualTo((1, 65010003, 77)));
        Assert.That(joined.Joined, Is.EquivalentTo(new long[] { 1 }));
        Assert.That(queue.Join(3, out _), Is.EqualTo(RankDuelError.NoMatch));
    }

    [Test]
    public void End_FreesBoth() {
        var queue = new RankDuelQueue();
        queue.Register(1, 0, out _);
        queue.Register(2, 0, out RankDuelMatch? match);
        Assert.That(queue.End(match!.Id), Is.SameAs(match));
        Assert.That(queue.End(match.Id), Is.Null);
        Assert.That(queue.MatchOf(1), Is.Null);
        Assert.That(queue.Register(1, 0, out _), Is.EqualTo(RankDuelError.none));
    }

    // A logout leaves the queue; a matched character stays in its match (its field counts it absent).
    [Test]
    public void Remove_TakesOnlyTheQueued() {
        var queue = new RankDuelQueue();
        queue.Register(1, 0, out _);
        Assert.That(queue.Remove(1), Is.True);
        queue.Register(2, 0, out _);
        queue.Register(3, 0, out RankDuelMatch? match);
        Assert.That(queue.Remove(2), Is.False);
        Assert.That(queue.MatchOf(2), Is.SameAs(match));
    }

    [Test]
    public void Expire_NoShowAndLifetime() {
        var queue = new RankDuelQueue();
        queue.Register(1, 0, out _);
        queue.Register(2, 0, out RankDuelMatch? noShow);
        queue.SetRoom(noShow!.Id, 1, 65010001, 10);
        queue.Register(3, 0, out _);
        queue.Register(4, 0, out RankDuelMatch? joined);
        queue.SetRoom(joined!.Id, 1, 65010001, 11);
        queue.Join(3, out _);

        Assert.That(queue.Expire(RankDuelQueue.NoShowMs - 1), Is.Empty);
        Assert.That(queue.Expire(RankDuelQueue.NoShowMs).Select(match => match.Id), Is.EqualTo(new[] { noShow.Id }));
        Assert.That(queue.MatchOf(1), Is.Null);
        Assert.That(queue.Expire(RankDuelQueue.LifetimeMs).Select(match => match.Id), Is.EqualTo(new[] { joined.Id }));
        Assert.That(queue.MatchOf(3), Is.Null);
    }
}
