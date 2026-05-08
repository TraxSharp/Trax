using System.Data;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Models.Metadata;
using Trax.Effect.Data.Services.DataContextTransaction;
using Trax.Effect.Models;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.SchedulerConfig;
using Trax.Effect.Models.WorkQueue;

namespace Trax.Effect.Data.Services.DataContext;

/// <summary>
/// Provides a generic implementation of IDataContext that can be used with any Entity Framework Core DbContext.
/// This class serves as a base for specific database implementations like PostgresContext and InMemoryContext.
/// </summary>
/// <typeparam name="TDbContext">The specific DbContext type</typeparam>
/// <param name="options">The options to be used by the DbContext</param>
/// <remarks>
/// The DataContext class is a key component in the Trax.Effect.Data system.
/// It implements the IDataContext interface and extends Entity Framework Core's DbContext,
/// providing a bridge between the Trax.Effect tracking system and database persistence.
///
/// This class:
/// 1. Implements IDataContext to integrate with the Trax.Effect system
/// 2. Extends DbContext to leverage Entity Framework Core's ORM capabilities
/// 3. Provides DbSet properties for Metadata and Log entities
/// 4. Implements transaction management
/// 5. Implements the Track and SaveChanges methods required by IEffectProvider
///
/// The generic type parameter TDbContext allows this class to be used with different
/// DbContext implementations, making it adaptable to various database systems.
/// </remarks>
public class DataContext<TDbContext>(DbContextOptions<TDbContext> options)
    : DbContext(options),
        IDataContext
    where TDbContext : DbContext
{
    #region Tables

    /// <summary>
    /// Gets or sets the DbSet for train metadata records.
    /// </summary>
    /// <remarks>
    /// This property provides access to the Metadata table, which stores information about
    /// train executions, including inputs, outputs, state, and timing information.
    ///
    /// The Metadatas DbSet is the primary storage mechanism for train tracking data
    /// and is used by the EffectRunner to persist train execution details.
    /// </remarks>
    public DbSet<Metadata> Metadatas { get; set; }

    /// <summary>
    /// Gets or sets the DbSet for train log entries.
    /// </summary>
    /// <remarks>
    /// This property provides access to the Log table, which stores detailed log entries
    /// generated during train execution.
    ///
    /// The Logs DbSet allows for fine-grained tracking of train execution junctions
    /// and is particularly useful for debugging and auditing.
    /// </remarks>
    public DbSet<Log> Logs { get; set; }

    /// <summary>
    /// Gets or sets the DbSet for train manifest records.
    /// </summary>
    /// <remarks>
    /// This property provides access to the Manifest table, which stores configuration
    /// and property information for trains.
    ///
    /// The Manifests DbSet allows for storing train configurations and properties
    /// that can be serialized/deserialized as JSONB.
    /// </remarks>
    public DbSet<Manifest> Manifests { get; set; }

    /// <summary>
    /// Gets or sets the DbSet for dead letter records.
    /// </summary>
    /// <remarks>
    /// This property provides access to the DeadLetter table, which stores jobs
    /// that have exceeded their retry limits and require manual intervention.
    ///
    /// The DeadLetters DbSet allows for tracking failed jobs, their resolution status,
    /// and any retry attempts made after dead-lettering.
    /// </remarks>
    public DbSet<DeadLetter> DeadLetters { get; set; }

    public DbSet<WorkQueue> WorkQueues { get; set; }

    public DbSet<ManifestGroup> ManifestGroups { get; set; }

    public DbSet<BackgroundJob> BackgroundJobs { get; set; }

    public DbSet<SchedulerConfig> SchedulerConfigs { get; set; }

    /// <summary>
    /// Gets or sets the DbSet for persisted GraphQL operation manifest rows.
    /// </summary>
    public DbSet<Effect.Models.PersistedOperation.PersistedOperation> PersistedOperations { get; set; }

    /// <summary>
    /// Gets or sets the DbSet for persisted-operation audit history.
    /// </summary>
    public DbSet<Effect.Models.PersistedOperationHistory.PersistedOperationHistory> PersistedOperationHistories { get; set; }

    #endregion

    /// <summary>
    /// Configures the model that was discovered by convention from the entity types.
    /// </summary>
    /// <param name="modelBuilder">The builder being used to construct the model for this context</param>
    /// <remarks>
    /// This method is called when the model for a derived context has been initialized, but
    /// before the model has been locked down and used to initialize the context.
    ///
    /// It applies entity configurations using the ApplyEntityOnModelCreating extension method,
    /// which scans for entity configuration methods and applies them to the model builder.
    ///
    /// Derived contexts can override this method to further configure the model,
    /// such as adding database-specific configurations.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyEntityOnModelCreating();

        // Persisted-operation entities use a string composite primary key,
        // so they do not implement IModel and are not discovered by
        // ApplyEntityOnModelCreating. Map them explicitly.
        Models.PersistedOperation.PersistentPersistedOperation.OnModelCreating(modelBuilder);
        Models.PersistedOperationHistory.PersistentPersistedOperationHistory.OnModelCreating(
            modelBuilder
        );
    }

    /// <summary>
    /// Gets or sets the number of changes tracked by the context.
    /// </summary>
    /// <remarks>
    /// This property tracks the number of entities that have been modified but not yet
    /// persisted to the database. It can be used to determine if there are pending changes
    /// that need to be saved.
    /// </remarks>
    public int Changes { get; set; }

    /// <summary>
    /// Begins a new database transaction with the default isolation level (ReadCommitted).
    /// </summary>
    /// <returns>A transaction object that can be used to commit or rollback changes</returns>
    /// <remarks>
    /// This method starts a new database transaction with the ReadCommitted isolation level.
    /// The transaction must be explicitly committed or rolled back using the CommitTransaction
    /// or RollbackTransaction methods.
    ///
    /// Transactions ensure that multiple database operations are treated as a single atomic unit,
    /// either all succeeding or all failing together.
    /// </remarks>
    public Task<IDataContextTransaction> BeginTransaction() =>
        BeginTransaction(IsolationLevel.ReadCommitted);

    /// <inheritdoc />
    public Task<IDataContextTransaction> BeginTransaction(CancellationToken cancellationToken) =>
        BeginTransaction(IsolationLevel.ReadCommitted, cancellationToken);

    /// <summary>
    /// Begins a new database transaction with the specified isolation level.
    /// </summary>
    /// <param name="isolationLevel">The transaction isolation level to use</param>
    /// <returns>A transaction object that can be used to commit or rollback changes</returns>
    /// <remarks>
    /// This method starts a new database transaction with the specified isolation level.
    /// The isolation level determines how the transaction interacts with other concurrent transactions.
    ///
    /// The method creates a new DataContextTransaction that wraps the underlying EF Core transaction,
    /// providing a consistent interface for transaction management across different database implementations.
    ///
    /// The transaction must be explicitly committed or rolled back using the
    /// CommitTransaction or RollbackTransaction methods.
    /// </remarks>
    public async Task<IDataContextTransaction> BeginTransaction(IsolationLevel isolationLevel) =>
        new DataContextTransaction.DataContextTransaction(
            this,
            await Database.BeginTransactionAsync()
        );

    /// <inheritdoc />
    public async Task<IDataContextTransaction> BeginTransaction(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken
    ) =>
        new DataContextTransaction.DataContextTransaction(
            this,
            await Database.BeginTransactionAsync(cancellationToken)
        );

    /// <summary>
    /// Commits the current transaction.
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method commits the current transaction, making all changes permanent.
    /// It should be called after all operations within the transaction have completed successfully.
    ///
    /// If no transaction is active, this method may throw an exception or have no effect,
    /// depending on the database provider.
    /// </remarks>
    public async Task CommitTransaction() => await Database.CommitTransactionAsync();

    /// <summary>
    /// Rolls back the current transaction.
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method rolls back the current transaction, discarding all changes made within it.
    /// It should be called when an error occurs within the transaction and the changes
    /// should not be persisted.
    ///
    /// If no transaction is active, this method may throw an exception or have no effect,
    /// depending on the database provider.
    /// </remarks>
    public Task RollbackTransaction() => Database.RollbackTransactionAsync();

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    /// <param name="stoppingToken">A token to monitor for cancellation requests</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method persists all tracked changes to the database. It is called by the
    /// EffectRunner when SaveChanges is called on the runner.
    ///
    /// This implementation delegates to the base SaveChangesAsync method, which handles
    /// the actual persistence of changes to the database.
    /// </remarks>
    public async Task SaveChanges(CancellationToken stoppingToken)
    {
        await base.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    /// Begins tracking the specified model in the Added state.
    /// </summary>
    /// <param name="model">The model to track</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method adds the specified model to the context's change tracker in the Added state,
    /// indicating that it should be inserted into the database when SaveChanges is called.
    ///
    /// This implementation is called by the EffectRunner when Track is called on the runner,
    /// allowing train metadata to be automatically persisted to the database.
    /// </remarks>
    public async Task Track(IModel model)
    {
        var entry = Entry(model);

        // Skip if already tracked — avoids forcing Added → Modified
        // on entities that haven't been saved yet (breaks InMemory provider).
        if (entry.State != EntityState.Detached)
            return;

        // Set state directly to avoid graph traversal — both base.Update() and Attach()
        // walk ALL loaded navigation properties, which generates unnecessary UPDATE
        // statements (e.g., Manifest) and can cause lock contention.
        // Setting entry.State only affects this entity, not navigations.
        entry.State = model.Id > 0 ? EntityState.Modified : EntityState.Added;
    }

    public async Task Update(IModel model)
    {
        var entry = Entry(model);

        // Skip if already tracked — EF change tracking detects property mutations
        // automatically via snapshot comparison.
        if (entry.State == EntityState.Detached)
            entry.State = model.Id > 0 ? EntityState.Modified : EntityState.Added;
    }

    /// <summary>
    /// Resets the context, clearing all tracked entities.
    /// </summary>
    /// <remarks>
    /// This method clears the change tracker, removing all entities that are being tracked
    /// by the context. This is useful when the context has been used for a long time
    /// and may be tracking many entities, which can impact performance.
    ///
    /// After calling Reset, any entities that were previously tracked will need to be
    /// re-attached to the context if they need to be persisted.
    /// </remarks>
    public void Reset() => ChangeTracker.Clear();
}
