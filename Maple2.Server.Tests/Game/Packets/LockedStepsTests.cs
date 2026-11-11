using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Maple2.Model.Game.Party;

namespace Maple2.Server.Tests.Game.Packets;

// rv40 MINOR 6/7: the matchmaker's steps run under one lock, their channel calls after it is released.
public class LockedStepsTests {
    [Test]
    public void DeferredRunsAfterTheLockInOrder() {
        var steps = new LockedSteps();
        var log = new List<string>();
        int result = steps.Run(() => {
            LockedSteps.Defer(() => log.Add($"a held={steps.IsHeld}"));
            steps.Run(() => {
                LockedSteps.Defer(() => log.Add($"b held={steps.IsHeld}"));
                log.Add("inner");
            });
            log.Add("outer");
            return 7;
        });
        Assert.That(result, Is.EqualTo(7));
        Assert.That(log, Is.EqualTo(new[] { "inner", "outer", "a held=False", "b held=False" }));
    }

    [Test]
    public void DeferOutsideAStepRunsAtOnce() {
        var log = new List<int>();
        LockedSteps.Defer(() => log.Add(1));
        Assert.That(log, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void AFailingEffectIsReportedAndTheOthersRun() {
        var errors = new List<Exception>();
        var steps = new LockedSteps { OnEffectError = errors.Add };
        var log = new List<int>();
        steps.Run(() => {
            LockedSteps.Defer(() => throw new InvalidOperationException());
            LockedSteps.Defer(() => log.Add(1));
        });
        Assert.That(log, Is.EqualTo(new[] { 1 }));
        Assert.That(errors.Count, Is.EqualTo(1));
        LockedSteps.Defer(() => log.Add(2));
        Assert.That(log, Is.EqualTo(new[] { 1, 2 }));
    }

    // rv42 MINOR 4: a step whose body throws still delivers what it deferred before the throw.
    [Test]
    public void EffectsRunWhenTheBodyThrows() {
        var steps = new LockedSteps();
        var log = new List<int>();
        Assert.Throws<InvalidOperationException>(() => steps.Run(() => {
            LockedSteps.Defer(() => log.Add(1));
            throw new InvalidOperationException();
        }));
        Assert.That(log, Is.EqualTo(new[] { 1 }));
        // Nothing is left behind: outside a step an effect runs at once.
        LockedSteps.Defer(() => log.Add(2));
        Assert.That(log, Is.EqualTo(new[] { 1, 2 }));
    }

    // rv42 MINOR 3: D joins (its effects block on a slow channel), then the leader's 34 on another thread: the
    // stop is delivered after D's registered, not before.
    [Test]
    public void EffectsOfTwoThreadsRunInStepOrder() {
        var steps = new LockedSteps();
        var log = new List<string>();
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        Task join = Task.Run(() => steps.Run(() => {
            LockedSteps.Defer(() => {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                lock (log) log.Add("load D");
            });
            LockedSteps.Defer(() => {
                lock (log) log.Add("registered");
            });
        }));
        Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        // The 34 step runs while D's effects are still out; its stop waits for them.
        Assert.That(Task.Run(() => steps.Run(() => LockedSteps.Defer(() => {
            lock (log) log.Add("stop");
        }))).Wait(TimeSpan.FromSeconds(10)), Is.True);
        lock (log) Assert.That(log, Is.Empty);
        release.Set();
        Assert.That(join.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(log, Is.EqualTo(new[] { "load D", "registered", "stop" }));
    }

    // An effect that blocks does not hold the lock: another thread's step runs meanwhile.
    [Test]
    public void ABlockingEffectDoesNotHoldOtherSteps() {
        var steps = new LockedSteps();
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        Task slow = Task.Run(() => steps.Run(() => LockedSteps.Defer(() => {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        })));
        Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        bool ran = Task.Run(() => steps.Run(() => true)).Wait(TimeSpan.FromSeconds(10));
        release.Set();
        slow.Wait(TimeSpan.FromSeconds(10));
        Assert.That(ran, Is.True);
    }
}
