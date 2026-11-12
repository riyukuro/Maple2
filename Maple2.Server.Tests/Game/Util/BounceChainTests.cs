using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The bounce chain of an arrow attack with targetCount 1, as the client builds it.
public class BounceChainTests {
    private sealed class Mob(string name) {
        public override string ToString() => name;
    }

    private static readonly Mob A = new("A"), B = new("B"), C = new("C"), D = new("D"), E = new("E"), F = new("F");

    // Every mob sees the others nearest first in a line A..F, itself included.
    private static readonly Mob[] Line = [A, B, C, D, E, F];

    private static IReadOnlyList<Mob> Near(Mob from) {
        int at = Array.IndexOf(Line, from);
        return Line.OrderBy(mob => Math.Abs(Array.IndexOf(Line, mob) - at)).ToList();
    }

    [Test]
    public void ChainHitsBounceCountMoreTargets() {
        // A storm splitter: chain, 4 bounces, no overlap: five different targets.
        List<Mob> hits = BounceChain.Build(A, 4, BounceType.Chain, false, Near);
        Assert.That(hits, Is.EqualTo(new[] { A, B, C, D, E }));
    }

    [Test]
    public void ChainStopsWhenNoneLeft() {
        List<Mob> hits = BounceChain.Build(A, 4, BounceType.Chain, false, from => from == A ? [A, B] : [B, A]);
        Assert.That(hits, Is.EqualTo(new[] { A, B }));
    }

    [Test]
    public void ChainWithOverlapStartsAgainWithoutTheLastHit() {
        IReadOnlyList<Mob> TwoMobs(Mob from) => from == A ? [A, B] : [B, A];
        List<Mob> hits = BounceChain.Build(A, 3, BounceType.Chain, true, TwoMobs, others => others[0]);
        Assert.That(hits, Is.EqualTo(new[] { A, B, A, B }));
    }

    [Test]
    public void RangeNeverRepeats() {
        List<Mob> hits = BounceChain.Build(C, 5, BounceType.Range, true, Near);
        Assert.That(hits, Has.Count.EqualTo(6));
        Assert.That(hits.Distinct().Count(), Is.EqualTo(6));
    }

    [Test]
    public void PierceAndBoomerangAreNotPicked() {
        Assert.That(BounceChain.Build(A, 5, BounceType.Pierce, true, Near), Is.EqualTo(new[] { A }));
        Assert.That(BounceChain.Build(A, 8, BounceType.Boomerang, true, Near), Is.EqualTo(new[] { A }));
    }

    [Test]
    public void NoBounceIsOneHit() {
        Assert.That(BounceChain.Build(A, 0, BounceType.Chain, false, Near), Is.EqualTo(new[] { A }));
    }
}
