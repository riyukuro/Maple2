using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Maple2.Database.Storage;

public abstract class DatabaseRequest<TContext>(TContext context, ILogger logger) : IDisposable
    where TContext : DbContext {
    protected readonly TContext Context = context;
    protected readonly ILogger Logger = logger;

    private IDbContextTransaction? transaction;
    public bool IsTransaction => transaction != null;

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
