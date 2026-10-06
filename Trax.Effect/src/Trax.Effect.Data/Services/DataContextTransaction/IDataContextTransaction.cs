namespace Trax.Effect.Data.Services.DataContextTransaction;

/// <summary>
/// Defines the contract for a database transaction in the Trax.Effect.Data system.
/// This interface provides a consistent abstraction over different database transaction implementations.
/// </summary>
/// <remarks>
/// The IDataContextTransaction interface is an important abstraction in the Trax.Effect.Data system.
/// It provides a consistent way to work with database transactions across different database implementations.
///
/// This interface:
/// 1. Inherits from IDisposable to ensure proper resource cleanup
/// 2. Provides a consistent API for transaction management
///
/// By using this interface, the system can work with different database transaction implementations
/// without being tied to a specific database provider.
///
/// Transactions are essential for ensuring data consistency when multiple database operations
/// need to be treated as a single atomic unit. They ensure that either all operations succeed
/// or all operations fail, preventing partial updates that could leave the database in an
/// inconsistent state.
///
/// This interface is typically used in conjunction with the BeginTransaction, CommitTransaction,
/// and RollbackTransaction methods on the IDataContext interface.
/// </remarks>
public interface IDataContextTransaction : IDisposable
{
    /// <summary>
    /// Commits the transaction.
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method commits the transaction, making all changes permanent.
    /// It should be called after all operations within the transaction have completed successfully.
    ///
    /// If the transaction has already been committed or rolled back, this method may throw
    /// an exception or have no effect, depending on the implementation.
    /// </remarks>
    Task Commit();

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method rolls back the transaction, discarding all changes made within it.
    /// It should be called when an error occurs within the transaction and the changes
    /// should not be persisted.
    ///
    /// If the transaction has already been committed or rolled back, this method may throw
    /// an exception or have no effect, depending on the implementation.
    /// </remarks>
    Task Rollback();
}
