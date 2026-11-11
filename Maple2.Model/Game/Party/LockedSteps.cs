namespace Maple2.Model.Game.Party;

// Steps under one lock whose side effects run after it is released. A step runs its body under the lock; the
// actions it defers run once the outermost step on that thread has released the lock, in the order deferred. A step
// inside a step only takes the lock again. Outside a step a deferred action runs at once.
public sealed class LockedSteps {
    private readonly object mutex = new();
    [ThreadStatic] private static List<Action>? deferred;

    public T Run<T>(Func<T> body) {
        List<Action>? effects = null;
        T result;
        lock (mutex) {
            bool outer = deferred == null;
            if (outer) {
                deferred = [];
            }
            try {
                result = body();
            } finally {
                if (outer) {
                    effects = deferred;
                    deferred = null;
                }
            }
        }
        if (effects != null) {
            foreach (Action effect in effects) {
                try {
                    effect();
                } catch (Exception) { }
            }
        }
        return result;
    }

    public void Run(Action body) => Run(() => {
        body();
        return true;
    });

    public static void Defer(Action effect) {
        if (deferred != null) {
            deferred.Add(effect);
        } else {
            effect();
        }
    }

    // For a test: whether this thread holds the lock.
    public bool IsHeld => Monitor.IsEntered(mutex);
}
