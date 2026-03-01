using Microsoft.EntityFrameworkCore.Storage;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.Data.Services.DataContextTransaction;

/// <summary>
/// Provides a concrete implementation of IDataContextTransaction that wraps an Entity Framework Core transaction.
/// This class simplifies transaction management by delegating operations to the data context and underlying transaction.
/// </summary>
/// <param name="db">The data context associated with this transaction</param>
/// <param name="tx">The underlying Entity Framework Core transaction</param>
/// <remarks>
/// The DataContextTransaction class is a key component in the Trax.Effect.Data transaction management system.
/// It implements the IDataContextTransaction interface and provides a bridge between the Trax.Effect.Data
/// transaction abstraction and Entity Framework Core's transaction implementation.
///
/// This class:
/// 1. Implements IDataContextTransaction to provide a consistent transaction API
/// 2. Delegates transaction operations to the data context and underlying EF Core transaction
/// 3. Ensures proper resource cleanup through the IDisposable implementation
///
/// By wrapping the EF Core transaction, this class simplifies transaction management for consumers
/// and ensures that transactions are properly managed regardless of the specific database provider.
/// </remarks>
public class DataContextTransaction(IDataContext db, IDbContextTransaction tx)
    : IDataContextTransaction
{
    /// <summary>
    /// Commits the transaction.
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method commits the transaction by delegating to the CommitTransaction method
    /// on the associated data context. This makes all changes permanent in the database.
    ///
    /// It should be called after all operations within the transaction have completed successfully.
    /// </remarks>
    public async Task Commit() => await db.CommitTransaction();

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method rolls back the transaction by delegating to the RollbackTransaction method
    /// on the associated data context. This discards all changes made within the transaction.
    ///
    /// It should be called when an error occurs within the transaction and the changes
    /// should not be persisted.
    /// </remarks>
    public async Task Rollback() => await db.RollbackTransaction();

    /// <summary>
    /// Disposes the underlying transaction, releasing any resources used.
    /// </summary>
    /// <remarks>
    /// This method ensures that the underlying EF Core transaction is properly disposed,
    /// releasing any database resources associated with it.
    ///
    /// It is automatically called when the transaction is used in a using statement
    /// or when Dispose is explicitly called.
    /// </remarks>
    public void Dispose() => tx.Dispose();
}
