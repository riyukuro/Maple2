using System;

namespace Maple2.Tools.Scheduler;

internal class ScheduledEvent {
    public bool Completed { get; private set; }
    public long ExecutionTime { get; private set; }

    private readonly Action task;
    private readonly TimeSpan interval;
    private readonly bool strict;

    public ScheduledEvent(Action task, long executionTime = 0, TimeSpan? interval = null, bool strict = false) {
        this.task = task;
        ExecutionTime = executionTime;
        this.interval = interval ?? TimeSpan.FromMilliseconds(-1);
        this.strict = strict;
    }

    public bool IsReady(long time) => ExecutionTime <= time;

    // Invokes the task and returns the next execution time. A task that throws is done with as one that returns: run
    // once, it is completed (dropped); repeated, it runs again an interval later. It used to stay due, so the queue ran
    // it again on every loop, the error logged each time.
    public long Invoke() {
        if (Completed) return -1;

        try {
            task.Invoke();
        } finally {
            Advance();
        }
        return Completed ? -1 : ExecutionTime;
    }

    private void Advance() {
        if (interval < TimeSpan.Zero || interval == TimeSpan.Zero) {
            Completed = true;
            return;
        }

        if (strict) {
            ExecutionTime += (long) interval.TotalMilliseconds;
        } else {
            ExecutionTime = Environment.TickCount64 + (long) interval.TotalMilliseconds;
        }
    }
}
