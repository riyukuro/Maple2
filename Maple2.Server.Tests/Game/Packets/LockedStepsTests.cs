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
    public void AFailingEffectDoesNotStopTheOthersOrTheNextStep() {
        var steps = new LockedSteps();
        var log = new List<int>();
        steps.Run(() => {
            LockedSteps.Defer(() => throw new InvalidOperationException());
            LockedSteps.Defer(() => log.Add(1));
        });
        Assert.That(log, Is.EqualTo(new[] { 1 }));
        // A step whose body throws leaves no deferred list behind.
        Assert.Throws<InvalidOperationException>(() => steps.Run(() => throw new InvalidOperationException()));
        LockedSteps.Defer(() => log.Add(2));
        Assert.That(log, Is.EqualTo(new[] { 1, 2 }));
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
