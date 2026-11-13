using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class NpcControlRulesTests {
    private static SkillMetadataPush Push(float distance, float duration, int down = 0, float up = 0) =>
        new(Type: 1, EaseType: 1, ApplyField: 1, Distance: distance, UpDistance: up, Down: down, Fall: false, Duration: duration, Probability: 1, Priority: 0, PriorityHitImmune: 0);

    [Test]
    public void HitStateNeedsImmuneBreakAboveHitImmune() {
        Assert.That(NpcControlRules.HitReacts(5, 1, 1, false, ActorState.Idle), Is.True);
        Assert.That(NpcControlRules.HitReacts(5, 1, 2, false, ActorState.Walk), Is.True);
        Assert.That(NpcControlRules.HitReacts(1, 1, 1, true, ActorState.Idle), Is.False);
        Assert.That(NpcControlRules.HitReacts(0, 1, 1, true, ActorState.Idle), Is.False);
        Assert.That(NpcControlRules.HitReacts(5, 99, 1, true, ActorState.Idle), Is.False);
        Assert.That(NpcControlRules.HitReacts(5, 98, 1, true, ActorState.Idle), Is.False);
        Assert.That(NpcControlRules.HitReacts(0, -1, 1, false, ActorState.Idle), Is.True);
        // class 3 and up: never
        Assert.That(NpcControlRules.HitReacts(5, 1, 3, true, ActorState.Idle), Is.False);
        Assert.That(NpcControlRules.HitReacts(5, 1, 7, true, ActorState.Idle), Is.False);
    }

    [Test]
    public void OnlyAPushHitBreaksACastOrTheHold() {
        Assert.That(NpcControlRules.HitReacts(5, 1, 1, false, ActorState.PcSkill), Is.False);
        Assert.That(NpcControlRules.HitReacts(5, 1, 1, true, ActorState.PcSkill), Is.True);
        Assert.That(NpcControlRules.HitReacts(5, 1, 1, false, ActorState.Hit), Is.False);
        Assert.That(NpcControlRules.HitReacts(5, 1, 1, true, ActorState.Hit), Is.True);
        foreach (ActorState state in new[] { ActorState.Spawn, ActorState.Regen, ActorState.Stun, ActorState.Dead }) {
            Assert.That(NpcControlRules.HitReacts(5, 1, 1, true, state), Is.False);
        }
    }

    private static NpcControlRules.HitSource From(Vector3 origin, Vector3? forward = null) =>
        new(origin, origin, forward ?? new Vector3(0, 1, 0));

    [Test]
    public void PushGoesAwayFromTheSourceOnTheGround() {
        Assert.That(NpcControlRules.PushOffset(1, From(new Vector3(0, 0, 0)), new Vector3(300, 0, 150), 50), Is.EqualTo(new Vector3(50, 0, 0)));
        Vector3 offset = NpcControlRules.PushOffset(1, From(new Vector3(100, 100, 0)), new Vector3(400, 500, 0), 150);
        Assert.That(offset.X, Is.EqualTo(90).Within(0.01));
        Assert.That(offset.Y, Is.EqualTo(120).Within(0.01));
        Assert.That(offset.Z, Is.EqualTo(0));
        // on the source point, or no distance: no move
        Assert.That(NpcControlRules.PushOffset(1, From(new Vector3(5, 5, 0)), new Vector3(5, 5, 90), 50), Is.EqualTo(default(Vector3)));
        Assert.That(NpcControlRules.PushOffset(1, From(new Vector3(0, 0, 0)), new Vector3(300, 0, 0), 0), Is.EqualTo(default(Vector3)));
    }

    // Type 2 pulls toward the origin, stopping 150 short of the attacker; one nearer than that is put back out.
    [Test]
    public void PullTypeTwoStopsShortOfTheAttacker() {
        var origin = new Vector3(0, 0, 0);
        Assert.That(NpcControlRules.PushOffset(2, From(origin), new Vector3(500, 0, 0), 200), Is.EqualTo(new Vector3(-200, 0, 0)));
        Vector3 offset = NpcControlRules.PushOffset(2, From(origin), new Vector3(250, 0, 0), 200);
        Assert.That(offset.X, Is.EqualTo(-100).Within(0.01));
        offset = NpcControlRules.PushOffset(2, From(origin), new Vector3(100, 0, 0), 200);
        Assert.That(offset.X, Is.EqualTo(50).Within(0.01));
        Assert.That(NpcControlRules.PushOffset(2, From(origin), new Vector3(150, 0, 0), 200), Is.EqualTo(default(Vector3)));
        // the 150 is measured from the attacker, the direction from the origin
        offset = NpcControlRules.PushOffset(2, new NpcControlRules.HitSource(origin, new Vector3(400, 0, 0), default), new Vector3(500, 0, 0), 200);
        Assert.That(offset.X, Is.EqualTo(50).Within(0.01)); // 100 from the attacker: put back out to 150
    }

    // Type 4 pulls the npc all the way to the origin, whatever pushdistance says.
    [Test]
    public void PullTypeFourGoesToTheOrigin() {
        Vector3 offset = NpcControlRules.PushOffset(4, From(new Vector3(100, 100, 0)), new Vector3(400, 500, 90), 10);
        Assert.That(offset.X, Is.EqualTo(-300).Within(0.01));
        Assert.That(offset.Y, Is.EqualTo(-400).Within(0.01));
        Assert.That(offset.Z, Is.EqualTo(0));
    }

    // Type 5 pushes along the attacker's facing, not away from it.
    [Test]
    public void PushTypeFiveFollowsTheAttackersFacing() {
        Vector3 offset = NpcControlRules.PushOffset(5, From(new Vector3(0, 0, 0), new Vector3(0, 1, 0)), new Vector3(300, 0, 0), 120);
        Assert.That(offset.X, Is.EqualTo(0).Within(0.01));
        Assert.That(offset.Y, Is.EqualTo(120).Within(0.01));
    }

    // Types 3 and -1 (and 6, not in the data) leave the client's direction zero: no move.
    [Test]
    public void OtherPushTypesDoNotMove() {
        foreach (int type in new[] { 3, -1, 6, 0 }) {
            Assert.That(NpcControlRules.PushOffset(type, From(new Vector3(0, 0, 0)), new Vector3(300, 0, 0), 100), Is.EqualTo(default(Vector3)), $"type {type}");
        }
    }

    [Test]
    public void HitEntryCarriesPauseTimePushTimeAndDown() {
        Assert.That(NpcControlRules.Hit(7, 0.1f, Push(50, 0.1f), true), Is.EqualTo(new NpcControlRules.HitEntry(7, 0.1f, 0.1f, 0, false)));
        Assert.That(NpcControlRules.Hit(7, 0.8f, Push(150, 0.5f), true), Is.EqualTo(new NpcControlRules.HitEntry(7, 0.8f, 0.5f, 0, false)));
        Assert.That(NpcControlRules.Hit(8, 0.3f, Push(150, 0.4f, down: 1), true), Is.EqualTo(new NpcControlRules.HitEntry(8, 0.3f, 0.4f, 0, true)));
        // a push that did not move the npc: push time 0
        Assert.That(NpcControlRules.Hit(7, 0.1f, Push(50, 0.1f), false), Is.EqualTo(new NpcControlRules.HitEntry(7, 0.1f, 0, 0, false)));
        // no push
        Assert.That(NpcControlRules.Hit(7, 0, null, false), Is.EqualTo(new NpcControlRules.HitEntry(7, 0, 0, 0, false)));
    }

    [Test]
    public void HitStateGoesOutOnceThenStateNone() {
        var entry = new NpcControlRules.HitEntry(7, 0.1f, 0.1f, 0, false);
        Assert.That(NpcControlRules.WireState(ActorState.Hit, true), Is.EqualTo(ActorState.Hit));
        Assert.That((byte) ActorState.Hit, Is.EqualTo(13));
        Assert.That(NpcControlRules.WireSequence(ActorState.Hit, 3, entry), Is.EqualTo(7));
        Assert.That(NpcControlRules.WireState(ActorState.Hit, false), Is.EqualTo(ActorState.None));
        Assert.That(NpcControlRules.WireSequence(ActorState.Hit, 3, null), Is.EqualTo(-1));
        Assert.That(NpcControlRules.WireState(ActorState.PcSkill, false), Is.EqualTo(ActorState.None));
        Assert.That(NpcControlRules.WireSequence(ActorState.Idle, 3, null), Is.EqualTo(3));
        Assert.That(NpcControlRules.WireSequence(ActorState.Stun, 3, null), Is.EqualTo(-1));
    }

    [Test]
    public void HoldIsTheLaterOfTheSequenceAndThePush() {
        Assert.That(NpcControlRules.HitHold(0.333f, 1, 0.1f, 0, 0), Is.EqualTo(333).Within(1));
        Assert.That(NpcControlRules.HitHold(0.4f, 2, 0, 0, 0), Is.EqualTo(200).Within(1));
        Assert.That(NpcControlRules.HitHold(0.33f, 1, 0.5f, 0.8f, 0), Is.EqualTo(1300).Within(1)); // pause + push outlasts the sequence
        Assert.That(NpcControlRules.HitHold(0.5f, 0, 0.2f, 0.1f, 1f), Is.EqualTo(1500).Within(1)); // the get-up sequence follows
    }

    // A hit added by the damage thread does nothing until the field thread applies it in the next update.
    [Test]
    public void HitIsAppliedOnlyByTheNextUpdate() {
        var pending = new NpcControlRules.PendingHits();
        var applied = new List<Vector3>();

        pending.Add(From(new Vector3(1, 0, 0)), null!);
        pending.Add(From(new Vector3(2, 0, 0)), null!);
        Assert.That(applied, Is.Empty);

        int count = pending.Apply((source, _) => {
            applied.Add(source.Origin);
            pending.Add(From(new Vector3(9, 0, 0)), null!); // a hit dealt while applying waits for the update after
            return source.Origin.X > 1;
        });
        Assert.That(applied.Select(source => source.X), Is.EqualTo(new[] { 1f, 2f }));
        Assert.That(count, Is.EqualTo(1));

        applied.Clear();
        Assert.That(pending.Apply((source, _) => { applied.Add(source.Origin); return true; }), Is.EqualTo(2));
        Assert.That(pending.Apply((_, _) => true), Is.Zero);
    }

    [Test]
    public void HitsFromManyThreadsAllArrive() {
        var pending = new NpcControlRules.PendingHits();
        Parallel.For(0, 8, thread => {
            for (int i = 0; i < 500; i++) {
                pending.Add(From(new Vector3(thread, i, 0)), null!);
            }
        });

        var seen = new HashSet<Vector3>();
        Assert.That(pending.Apply((source, _) => seen.Add(source.Origin)), Is.EqualTo(4000));
    }

    [Test]
    public void BattleFlagOnlyInBattleOrStunned() {
        Assert.That(NpcControlRules.Flags(false, false), Is.EqualTo(0));
        Assert.That(NpcControlRules.Flags(true, false), Is.EqualTo(2));
        Assert.That(NpcControlRules.Flags(false, true), Is.EqualTo(2));
        Assert.That(NpcControlRules.Flags(true, true), Is.EqualTo(2));
    }

    [Test]
    public void CastGoesOutAsStateNone() {
        Assert.That(NpcControlRules.WireState(ActorState.PcSkill), Is.EqualTo(ActorState.None));
        Assert.That((byte) NpcControlRules.WireState(ActorState.PcSkill), Is.EqualTo(0));
        foreach (ActorState state in new[] { ActorState.Idle, ActorState.Walk, ActorState.Jump, ActorState.Stun, ActorState.Talk, ActorState.Regen, ActorState.Spawn }) {
            Assert.That(NpcControlRules.WireState(state), Is.EqualTo(state));
        }
        Assert.That((byte) ActorState.Stun, Is.EqualTo(18));
    }

    [Test]
    public void StunIsSentWithoutASequence() {
        Assert.That(NpcControlRules.WireSequence(ActorState.Stun, 12), Is.EqualTo(-1));
        Assert.That(NpcControlRules.WireSequence(ActorState.Idle, 12), Is.EqualTo(12));
        Assert.That(NpcControlRules.WireSequence(ActorState.None, 12), Is.EqualTo(12));
    }

    [Test]
    public void StunValuesOneAndThreeStun() {
        Assert.That(NpcControlRules.Stuns(0), Is.False);
        Assert.That(NpcControlRules.Stuns(1), Is.True);
        Assert.That(NpcControlRules.Stuns(2), Is.False);
        Assert.That(NpcControlRules.Stuns(3), Is.True);
        Assert.That(NpcControlRules.Stuns(99), Is.False);
    }
}
