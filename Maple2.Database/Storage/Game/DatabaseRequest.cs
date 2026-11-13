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

    public bool Commit() {
        if (transaction == null) {
            return false;
        }

        transaction.Commit();
        transaction.Dispose();
        transaction = null; // transaction is completed.
        (Action Committed, Action? Dropped)[] committed = afterCommit.ToArray();
        afterCommit.Clear();
        foreach ((Action action, _) in committed) {
            action();
        }
        return true;
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
