using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Maple2.Database.Storage;

public abstract class DatabaseRequest<TContext> : IDisposable
    where TContext : DbContext {
    protected readonly TContext Context;
    protected readonly ILogger Logger;

    private IDbContextTransaction? transaction;
    public bool IsTransaction => transaction != null;
    private readonly List<(Action Committed, Action? Dropped)> afterCommit = [];

    // Runs action once the open transaction is committed: memory that may only change when the writes are kept (the
    // bag's delete list). It is dropped when the transaction is rolled back or disposed uncommitted. Without an open
    // transaction a write that returned true is already kept, so action runs at once.
    public void AfterCommit(Action action) => AfterEnd(action, null);

    // As AfterCommit, and dropped runs instead when the transaction ends without a commit: a rollback, a dispose
    // uncommitted, or a commit that threw (its answer lost: the writes may be kept or not).
    public void AfterEnd(Action committed, Action? dropped) {
        if (transaction == null) {
            committed();
            return;
        }
        afterCommit.Add((committed, dropped));
    }

    // The saves on this request that failed, whether or not the storage method that made them reports it: a caller
    // that writes several things in one transaction compares it before and after, and does not commit a part.
    public int FailedSaves { get; private set; }

    protected DatabaseRequest(TContext context, ILogger logger) {
        Context = context;
        Logger = logger;
        Context.SaveChangesFailed += (_, _) => FailedSaves++;
    }

    public void BeginTransaction() {
        transaction = Context.Database.BeginTransaction();
    }

    // Tests only: runs before (false) and right after (true) the transaction's commit, on this thread; a throw there
    // is one of the commit itself (after it: a commit kept whose answer was lost).
    [ThreadStatic] internal static Action<bool>? CommitFault;

    // A commit that throws leaves the transaction open here (Dispose rolls it back, the dropped steps run): its writes
    // may have been kept or not. A caller that can learn which (GameSession's purchase save reads its mark again) ends
    // it with SettleCommit.
    public bool Commit() {
        if (transaction == null) {
            return false;
        }

        CommitFault?.Invoke(false);
        transaction.Commit();
        CommitFault?.Invoke(true);
        transaction.Dispose();
        Committed();
        return true;
    }

    private void Committed() {
        transaction = null; // transaction is completed.
        (Action Committed, Action? Dropped)[] committed = afterCommit.ToArray();
        afterCommit.Clear();
        foreach ((Action action, _) in committed) {
            action();
        }
    }

    // After a commit that threw: the transaction is rolled back first (nothing on the database when the commit was
    // kept; a rollback that fails is logged), so no row lock of it is left when kept reads what was stored. kept
    // answers true (the writes were kept), false (they were not) or null (it cannot tell). The steps of that answer
    // run: AfterCommit's when kept, else the dropped ones, as a rollback runs them (memory then is that of a failure).
    public bool? SettleCommit(Func<bool?> kept) {
        IDbContextTransaction? open = transaction;
        transaction = null;
        if (open != null) {
            try {
                open.Rollback();
            } catch (Exception ex) {
                Logger.LogInformation(ex, "Rollback after a commit that threw failed");
            } finally {
                try {
                    open.Dispose();
                } catch (Exception ex) {
                    Logger.LogError(ex, "Disposing a transaction whose commit threw failed");
                }
            }
        }
        bool? answer = kept();
        (Action Committed, Action? Dropped)[] steps = afterCommit.ToArray();
        afterCommit.Clear();
        if (answer != true) {
            Context.ChangeTracker.Clear();
        }
        foreach ((Action committed, Action? dropped) in steps) {
            if (answer == true) {
                committed();
            } else {
                dropped?.Invoke();
            }
        }
        return answer;
    }

    // Ends an open transaction without its writes, so the request can go on outside it. The tracked entities are
    // dropped too: a failed save leaves its changes pending, and the request's next save would write them again
    // (an item moved to a mail that was rolled back). A rollback that fails (the connection was lost, as the save
    // that failed may have been) is logged, not thrown: the database drops a transaction that was never committed,
    // and the caller still takes its failure path (the black market's refund of the deposit).
    public void Rollback() {
        if (transaction == null) {
            return;
        }

        IDbContextTransaction open = transaction;
        transaction = null;
        (Action Committed, Action? Dropped)[] dropped = afterCommit.ToArray();
        afterCommit.Clear();
        foreach ((_, Action? action) in dropped) {
            action?.Invoke();
        }
        try {
            open.Rollback();
        } catch (Exception ex) {
            Logger.LogError(ex, "Rollback failed; the uncommitted transaction is dropped");
        } finally {
            try {
                open.Dispose();
            } catch (Exception ex) {
                Logger.LogError(ex, "Disposing a transaction that was not rolled back failed");
            }
            Context.ChangeTracker.Clear();
        }
    }

    public bool SaveChanges() {
        return Context.TrySaveChanges();
    }

    // A transaction still open here was not committed: a failure return or an exception left it, so its writes are
    // undone. (It used to be committed here, which kept the first writes of a call that had failed half way.)
    public void Dispose() {
        Rollback();
        Context.Dispose();
    }
}
