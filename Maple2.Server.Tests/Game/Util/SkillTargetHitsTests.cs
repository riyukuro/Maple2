using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The hits of the target packets (Skill sub 1) as the client's writers send them (CMagic, magicdata.cpp). Before,
// the server took no id from the packet: it searched the field once per packet (and built a bounce chain per
// packet), so these numbers came out higher.
public class SkillTargetHitsTests {
    private static SkillMetadataAttack Attack(int targetCount, SkillTargetType nonTarget, int methodType = 0,
        BounceType bounceType = BounceType.None, int bounceCount = 0, int pointGroup = 0) {
        var range = new SkillMetadataRange(SkillRegion.Box, 300, 150, 450, 0, 0, default, default, IncludeCasterType.Exclude,
            ApplyTargetType.Hostile, SkillTargetType.None);
        var arrow = new SkillMetadataArrow(Overlap: false, Explosion: false, RayPhysXTest: false, NonTarget: nonTarget,
            BounceType: bounceType, BounceCount: bounceCount, BounceRadius: 500, BounceOverlap: false,
            Collision: default, CollisionAdd: default, RayType: 0);
        var damage = new SkillMetadataDamage(1, 1, 0, 0, false, 0, 0, false, null);
        return new SkillMetadataAttack("Attack_01", pointGroup, targetCount, 0, 0, 0, 0, [], null, range, arrow, damage, [], [],
            AttackMethodType: methodType);
    }

    private static MagicPath Path(int interval = 0, int maxTargets = 0) {
        return new MagicPath(Align: false, AlignHeight: 0, Rotate: true, IgnoreAdjust: false, ExplosionByDestroy: false,
            CatmullRom: false, IgnorePhysXTestInitPosition: false, IgnoreCancelAtSpawnTime: false,
            Direction: Vector3.UnitY, FireOffset: default, FireFixed: default, ControlValue0: default,
            ControlValue1: default, ControlEndOffsetValue: default, TraceTargetOffset: false,
            TraceTargetDuration: 0, Velocity: 500, Distance: 800, RotateZDegree: 0, LifeTime: 0, DelayTime: 0,
            SpawnTime: 0, DestroyTime: 0, ControlRate: 0, LookAtType: 0, PiercingAttackInterval: interval,
            PiercingAttackMaxTargetCount: maxTargets, NonTargetMoveDistance: 0, MoveEndHoldDuration: 0);
    }

    // One target packet as the handler takes it: TakeTarget for the attack, then each hit.
    private static List<int> Pay(SkillHitBudget budget, long attackUid, SkillMetadataAttack attack, IReadOnlyList<MagicPath>? paths,
        params (int Id, byte Ordinal)[] hits) {
        var paid = new List<int>();
        if (!budget.TakeTarget(attackUid, 0, attack.Arrow.BounceCount, SkillHitRules.TargetRounds(attack, paths), hits.Length)) {
            return paid;
        }
        SkillHitShape shape = SkillHitRules.Shape(attack, paths);
        foreach ((int id, byte ordinal) in hits) {
            if (budget.TakeHit(attackUid, id, ordinal, shape) == HitRefusal.None) {
                paid.Add(id);
            }
        }
        return paid;
    }

    private static SkillHitBudget Cast(params (long Uid, int TargetId)[] attacks) {
        var budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 0, held: false, count: attacks.Length, tick: 0), Is.True);
        foreach ((long uid, int targetId) in attacks) {
            budget.Declare(uid, 0, targetId);
        }
        return budget;
    }

    [Test]
    public void BasicAttackHitsEachObjectOnceUpToTheTargetCount() {
        // 10100001: a region (AttackMethodType 0) with nonTarget 1 and target count 5.
        SkillMetadataAttack attack = Attack(5, SkillTargetType.Owner);
        SkillHitBudget budget = Cast((1, 0));
        Assert.That(Pay(budget, 1, attack, null, (101, 0), (102, 0), (101, 0), (103, 0), (104, 0), (105, 0), (106, 0)),
            Is.EqualTo(new[] { 101, 102, 103, 104, 105 }), "an object once, 5 objects");
        Assert.That(Pay(budget, 1, attack, null, (107, 0)), Is.Empty, "a region sends one packet per attack");
        Assert.That(Pay(Cast((2, 0)), 2, attack, null, (101, 1)), Is.Empty, "a region's hits carry 0");
    }

    [Test]
    public void TargetedRegionHitsOnlyItsOwnTarget() {
        // 10200171 on 3 mobs: a cylinder with target count 8 and no nonTarget. The attack point packet declares
        // one attack per mob; each target packet names the 3 mobs in the cylinder. 3 hits, not 9.
        SkillMetadataAttack attack = Attack(8, SkillTargetType.None);
        SkillHitBudget budget = Cast((1, 101), (2, 102), (3, 103));
        var paid = new List<int>();
        for (long uid = 1; uid <= 3; uid++) {
            paid.AddRange(Pay(budget, uid, attack, null, (101, 0), (102, 0), (103, 0)));
        }
        Assert.That(paid, Is.EqualTo(new[] { 101, 102, 103 }));
    }

    [Test]
    public void ChainLinkHitsOnlyItsLink() {
        // 10300071: an arrow (AttackMethodType 1), bounce type 2 (chain), bounce count 7, no nonTarget. Each link
        // of the chain is an attack of its own with its own target and its own target packet: 8 hits, not 8 times 8.
        SkillMetadataAttack attack = Attack(1, SkillTargetType.None, methodType: 1, bounceType: BounceType.Chain, bounceCount: 7);
        var links = new (long, int)[8];
        for (int i = 0; i < 8; i++) {
            links[i] = (1 + i, 101 + i);
        }
        SkillHitBudget budget = Cast(links);
        var paid = new List<int>();
        for (int i = 0; i < 8; i++) {
            var all = new (int, byte)[8];
            for (int j = 0; j < 8; j++) {
                all[j] = (101 + j, (byte) j);
            }
            paid.AddRange(Pay(budget, 1 + i, attack, null, (101 + i, 0)));
            paid.AddRange(Pay(budget, 1 + i, attack, null, all));
        }
        Assert.That(paid, Is.EqualTo(new[] { 101, 102, 103, 104, 105, 106, 107, 108 }));
    }

    [Test]
    public void BoomerangCountsItsHitsAndComesBackOnce() {
        // 10800011: an arrow, bounce type 4 (boomerang), bounce count 8, nonTarget 1. The packets below carry its hits
        // out and then back, the way back starting where a number comes below the one before.
        SkillMetadataAttack attack = Attack(1, SkillTargetType.Owner, methodType: 1, bounceType: BounceType.Boomerang, bounceCount: 8);
        MagicPath[] paths = [Path()];
        SkillHitBudget budget = Cast((1, 0));
        Assert.That(Pay(budget, 1, attack, paths, (101, 0), (102, 1)), Is.EqualTo(new[] { 101, 102 }));
        Assert.That(Pay(budget, 1, attack, paths, (103, 2)), Is.EqualTo(new[] { 103 }));
        // Back from the move's end: counted from 0 again, the same objects once more; once only.
        Assert.That(Pay(budget, 1, attack, paths, (101, 0), (102, 1)), Is.EqualTo(new[] { 101, 102 }));
        Assert.That(Pay(budget, 1, attack, paths, (103, 2), (101, 3)), Is.EqualTo(new[] { 103 }));
        Assert.That(Pay(budget, 1, attack, paths, (101, 0)), Is.Empty, "it comes back once");
        Assert.That(Pay(budget, 1, attack, paths, (104, 8)), Is.Empty, "a number past its most");
    }

    [Test]
    public void DroppedPacketKeepsTheArrowsLaterHits() {
        // A pierce with bounce count 4 whose second packet the server never took (late, or past the cap): the
        // numbers after it skip one. Before, every later hit of the arrow was refused for the wrong number.
        SkillMetadataAttack attack = Attack(1, SkillTargetType.Owner, methodType: 1, bounceType: BounceType.Pierce, bounceCount: 4);
        MagicPath[] paths = [Path()];
        SkillHitBudget budget = Cast((1, 0));
        Assert.That(Pay(budget, 1, attack, paths, (101, 0)), Is.EqualTo(new[] { 101 }));
        Assert.That(Pay(budget, 1, attack, paths, (103, 2)), Is.EqualTo(new[] { 103 }), "102 at 1 dropped");
        Assert.That(Pay(budget, 1, attack, paths, (104, 3)), Is.EqualTo(new[] { 104 }));

        // A boomerang that lost its last packet out (102 at 1) and its first packet back (102 at 0): its next hit
        // back, 101 at 1, is the way back though its number is not below the server's.
        SkillMetadataAttack boomerang = Attack(1, SkillTargetType.Owner, methodType: 1, bounceType: BounceType.Boomerang, bounceCount: 8);
        SkillHitBudget back = Cast((1, 0));
        Assert.That(Pay(back, 1, boomerang, paths, (101, 0)), Is.EqualTo(new[] { 101 }));
        Assert.That(Pay(back, 1, boomerang, paths, (101, 1)), Is.EqualTo(new[] { 101 }), "back");
        Assert.That(Pay(back, 1, boomerang, paths, (101, 2)), Is.Empty, "once back");
    }

    [Test]
    public void PacketsWithoutHitsUseNothing() {
        // A repiercing pierce in a crowd past its piercingAttackMaxTargetCount: the client clamps the hits to 0
        // and still sends the packet (magicdata.cpp:1925-1946), each tick. Before, each one used up a packet of the
        // 3 x 7 the arrow may send, and the re-hits after the interval were refused.
        SkillMetadataAttack attack = Attack(1, SkillTargetType.Owner, methodType: 1, bounceType: BounceType.Pierce, bounceCount: 2);
        MagicPath[] paths = [Path(interval: 300, maxTargets: 2)];
        SkillHitBudget budget = Cast((1, 0));
        for (int i = 0; i < 50; i++) {
            Assert.That(budget.TakeTarget(1, 0, attack.Arrow.BounceCount, SkillHitRules.TargetRounds(attack, paths), hits: 0), Is.True, $"empty packet {i}");
        }
        Assert.That(Pay(budget, 1, attack, paths, (101, 0)), Is.EqualTo(new[] { 101 }));
        Assert.That(budget.TakeTarget(9, 0, attack.Arrow.BounceCount, hits: 0), Is.False, "not declared");
    }

    [Test]
    public void PierceHitsBounceCountDistinctObjectsInOnePacket() {
        // 10500021: an arrow, bounce type 3 (pierce), bounce count 4. Before, a packet paid one object (target
        // count 1); the client pierces up to 4.
        SkillMetadataAttack attack = Attack(1, SkillTargetType.Owner, methodType: 1, bounceType: BounceType.Pierce, bounceCount: 4);
        MagicPath[] paths = [Path()];
        SkillHitBudget budget = Cast((1, 0));
        Assert.That(Pay(budget, 1, attack, paths, (101, 0), (102, 1), (103, 2), (104, 3), (105, 4)),
            Is.EqualTo(new[] { 101, 102, 103, 104 }));
        Assert.That(Pay(budget, 1, attack, paths, (105, 4)), Is.Empty, "full");

        // A piercing interval forgets what the arrow pierced: the same object again, the number unchanged.
        SkillHitBudget again = Cast((1, 0));
        MagicPath[] interval = [Path(interval: 300)];
        Assert.That(Pay(again, 1, attack, interval, (101, 0)), Is.EqualTo(new[] { 101 }));
        Assert.That(Pay(again, 1, attack, interval, (101, 1), (102, 1)), Is.EqualTo(new[] { 101, 102 }));

        // piercingAttackMaxTargetCount caps the objects below the bounce count.
        SkillHitBudget capped = Cast((1, 0));
        Assert.That(Pay(capped, 1, attack, [Path(maxTargets: 2)], (101, 0), (102, 1), (103, 2)), Is.EqualTo(new[] { 101, 102 }));
    }

    [Test]
    public void PointGroupHitsAnObjectOncePerSync() {
        SkillMetadataAttack attack = Attack(5, SkillTargetType.Owner, pointGroup: 1);
        var budget = new SkillHitBudget();
        Assert.That(budget.TakePoint(0, 0, held: false, count: 2, tick: 0), Is.True);
        budget.Declare(1, 0, 0);
        budget.Declare(2, 0, 0);
        Assert.That(Pay(budget, 1, attack, null, (101, 0)), Is.EqualTo(new[] { 101 }));
        Assert.That(Pay(budget, 2, attack, null, (101, 0), (102, 0)), Is.EqualTo(new[] { 102 }), "the group hit it");
        budget.Sync();
        budget.Declare(3, 0, 0);
        Assert.That(Pay(budget, 3, attack, null, (101, 0)), Is.EqualTo(new[] { 101 }), "after the cast's tick sync");
    }

    [Test]
    public void BoomerangSendsItsPacketsAgainOnTheWayBack() {
        // A boomerang with bounce count 2 sends one packet per hit: 2 out, 2 back. Before, a packet cap of
        // 1 + bounceCount refused the way back.
        SkillMetadataAttack attack = Attack(1, SkillTargetType.Owner, methodType: 1, bounceType: BounceType.Boomerang, bounceCount: 2);
        MagicPath[] paths = [Path()];
        SkillHitBudget budget = Cast((1, 0));
        var paid = new List<int>();
        for (int way = 0; way < 2; way++) {
            for (int i = 0; i < 2; i++) {
                paid.AddRange(Pay(budget, 1, attack, paths, (101 + i, (byte) i)));
            }
        }
        Assert.That(paid, Is.EqualTo(new[] { 101, 102, 101, 102 }));
        // 2 ways of 1 + 2 packets: 2 more packets the hits refuse, then none.
        Assert.That(Pay(budget, 1, attack, paths, (104, 0)), Is.Empty, "no third way");
        Assert.That(Pay(budget, 1, attack, paths, (104, 2)), Is.Empty, "full");
        Assert.That(budget.TakeTarget(1, 0, attack.Arrow.BounceCount, SkillHitRules.TargetRounds(attack, paths)), Is.False, "6 packets");
    }

    [Test]
    public void RepiercingArrowSendsAPacketPerInterval() {
        // A pierce with bounce count 2 and a 300 ms piercing interval over a 1.6 s flight (800 at 500 a second) hits
        // the same object again each interval: 1 + ceil(1600 / 300) = 7 rounds of 3 packets.
        SkillMetadataAttack attack = Attack(1, SkillTargetType.Owner, methodType: 1, bounceType: BounceType.Pierce, bounceCount: 2);
        MagicPath[] paths = [Path(interval: 300)];
        Assert.That(SkillHitRules.TargetRounds(attack, paths), Is.EqualTo(7));
        SkillHitBudget budget = Cast((1, 0));
        Assert.That(Pay(budget, 1, attack, paths, (101, 0)), Is.EqualTo(new[] { 101 }));
        for (int i = 1; i < 21; i++) {
            Assert.That(Pay(budget, 1, attack, paths, (101, 1)), Is.EqualTo(new[] { 101 }), $"packet {i}");
        }
        Assert.That(Pay(budget, 1, attack, paths, (101, 1)), Is.Empty, "past the flight");
        Assert.That(SkillHitRules.TargetRounds(attack, [Path()]), Is.EqualTo(1), "no interval");
        Assert.That(SkillHitRules.TargetRounds(attack, [Path(interval: 300) with { MoveEndHoldDuration = 1000 }]), Is.EqualTo(10),
            "a 1 s hold at the move's end: 1 + ceil(2600 / 300)");
        Assert.That(SkillHitRules.TargetRounds(Attack(5, SkillTargetType.Owner), paths), Is.EqualTo(1), "a region");
    }

    // Region paths (Skill 1 2): one at the arrow's end (CMagic::FUN_14145f570) and one per piercing interval
    // (FUN_141466da0). Before, a boomerang without an interval was allowed 2 per arrow (its target packet rounds),
    // and a boomerang with one 2 + ceil(2F / I).
    [Test]
    public void RegionPathRoundsAreTheEndAndTheIntervals() {
        SkillMetadataAttack pierce = Attack(1, SkillTargetType.Owner, methodType: 1, bounceType: BounceType.Pierce, bounceCount: 2);
        SkillMetadataAttack boomerang = Attack(1, SkillTargetType.Owner, methodType: 1, bounceType: BounceType.Boomerang, bounceCount: 2);
        Assert.Multiple(() => {
            Assert.That(SkillHitRules.RegionPathRounds(boomerang, [Path()]), Is.EqualTo(1), "boomerang, no interval");
            Assert.That(SkillHitRules.TargetRounds(boomerang, [Path()]), Is.EqualTo(2));
            Assert.That(SkillHitRules.RegionPathRounds(boomerang, [Path(interval: 300)]), Is.EqualTo(12), "1 + ceil(3200 / 300)");
            Assert.That(SkillHitRules.RegionPathRounds(pierce, [Path(interval: 300)]), Is.EqualTo(7), "1 + ceil(1600 / 300)");
            Assert.That(SkillHitRules.RegionPathRounds(pierce, [Path()]), Is.EqualTo(1), "pierce, no interval");
            Assert.That(SkillHitRules.RegionPathRounds(pierce, null), Is.EqualTo(1));
            Assert.That(SkillHitRules.RegionPathRounds(Attack(1, SkillTargetType.Owner, methodType: 1), [Path(interval: 300)]), Is.EqualTo(1), "no bounce");
            Assert.That(SkillHitRules.RegionPathHitCap(pierce), Is.EqualTo(2));
            Assert.That(SkillHitRules.RegionPathHitCap(boomerang), Is.EqualTo(0), "a boomerang resumes on its way back");
        });
    }

    // A piercing arrow sends no interval region path once it hit max(1, m_n110) different objects
    // (CMagic::FUN_141465b30 returns first, magicdata.cpp:1877-1884), only its end one. Before, it kept its interval
    // allowance after the cap.
    [Test]
    public void PiercingRegionPathsStopAtTheHitCap() {
        SkillMetadataAttack attack = Attack(1, SkillTargetType.Owner, methodType: 1, bounceType: BounceType.Pierce, bounceCount: 2);
        MagicPath[] paths = [Path(interval: 300)];
        int rounds = SkillHitRules.RegionPathRounds(attack, paths);
        int cap = SkillHitRules.RegionPathHitCap(attack);
        SkillHitBudget budget = Cast((1, 0));
        budget.DeclareArrow(0, 0, 0, attackUid: 1);
        Assert.That(budget.TakeRegionPath(0, 0, 0, rounds, 10, cap), Is.True, "interval 1");
        Assert.That(Pay(budget, 1, attack, paths, (101, 0)), Is.EqualTo(new[] { 101 }));
        Assert.That(budget.TakeRegionPath(0, 0, 0, rounds, 20, cap), Is.True, "interval 2, one object hit");
        Assert.That(Pay(budget, 1, attack, paths, (101, 1)), Is.EqualTo(new[] { 101 }), "the same object again");
        Assert.That(budget.TakeRegionPath(0, 0, 0, rounds, 30, cap), Is.True, "interval 3, still one object");
        Assert.That(Pay(budget, 1, attack, paths, (102, 1)), Is.EqualTo(new[] { 102 }));
        Assert.That(budget.TakeRegionPath(0, 0, 0, rounds, 40, cap), Is.True, "the end");
        Assert.That(budget.TakeRegionPath(0, 0, 0, rounds, 50, cap), Is.False, "past the cap");

        SkillHitBudget uncapped = Cast((1, 0));
        uncapped.DeclareArrow(0, 0, 0, attackUid: 1);
        Assert.That(Pay(uncapped, 1, attack, paths, (101, 0), (102, 1)), Is.EqualTo(new[] { 101, 102 }));
        for (int i = 0; i < rounds; i++) {
            Assert.That(uncapped.TakeRegionPath(0, 0, 0, rounds, 10 + i, hitCap: 0), Is.True, $"no cap, round {i}");
        }
    }

    [Test]
    public void ArrowWithNonTargetTwoHitsAnyObject() {
        // An arrow is held to its own target only with nonTarget 0; with 2 it hits what it meets.
        SkillMetadataAttack attack = Attack(1, SkillTargetType.Target, methodType: 1, bounceType: BounceType.Pierce, bounceCount: 2);
        SkillHitBudget budget = Cast((1, 101));
        Assert.That(Pay(budget, 1, attack, [Path()], (102, 0), (103, 1)), Is.EqualTo(new[] { 102, 103 }));
        SkillMetadataAttack held = Attack(1, SkillTargetType.None, methodType: 1);
        Assert.That(Pay(Cast((1, 101)), 1, held, null, (102, 0)), Is.Empty, "nonTarget 0");
    }

    [Test]
    public void RegionWithTargetCountZeroHitsNothing() {
        SkillMetadataAttack attack = Attack(0, SkillTargetType.Owner);
        Assert.That(Pay(Cast((1, 0)), 1, attack, null, (101, 0)), Is.Empty);
    }
}
