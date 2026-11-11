namespace Maple2.Model.Game.Party;

// Steps under one lock whose side effects run after it is released. A step runs its body under the lock; the
// actions it defers go into one queue in step order (a step's actions are queued while it holds the lock, so a
// later step's never come before an earlier one's). Once the outermost step on a thread has released the lock it
// runs the queue, unless another thread is running it already: one thread at a time runs the effects, in order
// (rv42 MINOR 3), so a step can return before its effects ran. The effects run also when the body throws
// (rv42 MINOR 4); an effect that throws is passed to OnEffectError and the next one runs. A step inside a step
// only takes the lock again. Outside a step a deferred action runs at once.
public sealed class LockedSteps {
    private readonly object mutex = new();
    private readonly object queueLock = new();
    private readonly Queue<Action> pending = new();
    private bool draining;
    [ThreadStatic] private static LockedSteps? current;

    // Told of an effect that threw (World logs it).
    public Action<Exception>? OnEffectError { get; set; }

    public T Run<T>(Func<T> body) {
        try {
            lock (mutex) {
                LockedSteps? previous = current;
                current = this;
                try {
                    return body();
                } finally {
                    current = previous;
                }
            }
        } finally {
            if (!Monitor.IsEntered(mutex)) {
                Drain();
            }
        }
    }

    public void Run(Action body) => Run(() => {
        body();
        return true;
    });

    public static void Defer(Action effect) {
        LockedSteps? steps = current;
        if (steps == null) {
            effect();
            return;
        }
        lock (steps.queueLock) {
            steps.pending.Enqueue(effect);
        }
    }

    private void Drain() {
        lock (queueLock) {
            if (draining) {
                return;
            }
            draining = true;
        }
        while (true) {
            Action effect;
            lock (queueLock) {
                if (!pending.TryDequeue(out effect!)) {
                    draining = false;
                    return;
                }
            }
            try {
                effect();
            } catch (Exception ex) {
                try {
                    OnEffectError?.Invoke(ex);
                } catch (Exception) { }
            }
        }
    }

    // For a test: whether this thread holds the lock.
    public bool IsHeld => Monitor.IsEntered(mutex);
}
