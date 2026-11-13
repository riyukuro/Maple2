using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// An npc's bouncing shot: the server builds the chain (BounceChain.For) and the chained target records.
public class NpcBounceChainTests {
    private sealed class Mob(int id) {
        public readonly int Id = id;
        public bool Dead;
        public override string ToString() => $"#{Id}";
    }

    private static readonly Mob[] Line = [new(1), new(2), new(3), new(4), new(5)];

    private static SkillMetadataArrow Arrow(BounceType type, int count, bool overlap = false) {
        return new SkillMetadataArrow(Overlap: true, Explosion: false, RayPhysXTest: false, NonTarget: SkillTargetType.Owner,
            BounceType: type, BounceCount: count, BounceRadius: 300, BounceOverlap: overlap,
            Collision: Vector3.Zero, CollisionAdd: Vector3.Zero, RayType: 0);
    }

    // Every mob sees the others nearest first along the line, itself included; counts the searches.
    private int searches;
    private IReadOnlyList<Mob> Near(Mob from) {
        searches++;
        int at = Array.IndexOf(Line, from);
        return Line.OrderBy(mob => Math.Abs(Array.IndexOf(Line, mob) - at)).ToList();
    }

    [SetUp]
    public void Reset() {
        searches = 0;
        foreach (Mob mob in Line) {
            mob.Dead = false;
        }
    }

    [Test]
    public void OnlyRangeAndChainBounceAround() {
        Assert.That(BounceChain.For<Mob>(Arrow(BounceType.None, 3), mob => mob.Id, Near), Is.Null);
        Assert.That(BounceChain.For<Mob>(Arrow(BounceType.Pierce, 3), mob => mob.Id, Near), Is.Null);
        Assert.That(BounceChain.For<Mob>(Arrow(BounceType.Chain, 0), mob => mob.Id, Near), Is.Null);
        Assert.That(BounceChain.For<Mob>(Arrow(BounceType.Range, 2), mob => mob.Id, Near), Is.Not.Null);
    }

    [Test]
    public void AHitsChainIsBuiltOnceAndDrawnAsItHits() {
        BounceChain.PerCast<Mob> chain = BounceChain.For<Mob>(Arrow(BounceType.Chain, 2), mob => mob.Id, Near)!;
        IReadOnlyList<Mob> drawn = chain.Of(Line[0]);
        int built = searches;
        Assert.That(drawn, Is.EqualTo(new[] { Line[0], Line[1], Line[2] }));
        Assert.That(chain.Hits([Line[0]]), Is.EqualTo(drawn));
        Assert.That(searches, Is.EqualTo(built));
    }

    [Test]
    public void EachObjectTakesTheHitOnce() {
        // Two first hits whose chains cross: 1 -> 2 -> 3 and 4 -> 3 -> 2.
        BounceChain.PerCast<Mob> chain = BounceChain.For<Mob>(Arrow(BounceType.Range, 2), mob => mob.Id, Near)!;
        List<Mob> hits = chain.Hits([Line[0], Line[3]]);
        Assert.That(hits.Select(mob => mob.Id), Is.EqualTo(new[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void AChainedObjectThatDiedTakesNoHitButTheFirstHitDoes() {
        BounceChain.PerCast<Mob> chain = BounceChain.For<Mob>(Arrow(BounceType.Chain, 2), mob => mob.Id, Near)!;
        chain.Of(Line[0]);
        Line[0].Dead = true;
        Line[1].Dead = true;
        Assert.That(chain.Hits([Line[0]], mob => mob.Dead).Select(mob => mob.Id), Is.EqualTo(new[] { 1, 3 }));
    }

    [Test]
    public void ChainedRecordsLinkToTheOneBeforeWithTheBounceNumber() {
        // The client's own chain: a link's index is bounceCount - left + 1 and it hangs under the last record.
        TargetRecord root = NpcProjectileRules.PathRecord(1, 11);
        List<TargetRecord> records = NpcProjectileRules.ChainRecords(root, [12, 13], 4);
        Assert.That(records.Select(record => (record.PrevUid, record.Uid, record.TargetId, (int) record.Unknown, (int) record.Index)),
            Is.EqualTo(new[] { (root.Uid, 4L, 12, 0, 1), (4L, 5L, 13, 0, 2) }));
        Assert.That(NpcProjectileRules.ChainRecords(root, [], 4), Is.Empty);
    }
}
