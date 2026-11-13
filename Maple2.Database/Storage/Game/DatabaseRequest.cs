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

    // Ends an open transaction without its writes, so the request can go on outside it.
    public void Rollback() {
        if (transaction == null) {
            return;
        }

        transaction.Rollback();
        transaction.Dispose();
        transaction = null;
    }

    public bool SaveChanges() {
        return Context.TrySaveChanges();
    }

    // A transaction still open here was not committed: a failure return or an exception left it, so its writes are
    // undone. (It used to be committed here, which kept the first writes of a call that had failed half way.)
    public void Dispose() {
        try {
            Rollback();
        } catch (Exception ex) {
            Logger.LogError(ex, "Rollback of an uncommitted transaction failed");
        }
        Context.Dispose();
    }
}
