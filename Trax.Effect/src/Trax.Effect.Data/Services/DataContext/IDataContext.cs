using System.Data;
using Microsoft.EntityFrameworkCore;
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
using Trax.Effect.Services.EffectProvider;

namespace Trax.Effect.Data.Services.DataContext;

/// <summary>
/// Defines the contract for a database context that integrates with the Trax.Effect system.
/// This interface extends IEffectProvider to enable database persistence of train metadata.
/// </summary>
/// <remarks>
/// The IDataContext interface is a central abstraction in the Trax.Effect.Data system.
/// It serves as a bridge between the Trax.Effect tracking system and database persistence,
/// allowing train metadata to be stored in various database systems.
///
/// This interface:
/// 1. Extends IEffectProvider to integrate with the EffectRunner
/// 2. Provides access to the Metadata and Log tables
/// 3. Supports transaction management
/// 4. Allows access to the underlying DbContext implementation
///
/// Different database implementations (PostgreSQL, InMemory, etc.) implement this interface
/// to provide consistent behavior while leveraging specific database features.
/// </remarks>
public interface IDataContext : IEffectProvider, IAsyncDisposable
{
    #region Tables

    /// <summary>
    /// Gets the DbSet for train metadata records.
    /// </summary>
    /// <remarks>
    /// This property provides access to the Metadata table, which stores information about
    /// train executions, including inputs, outputs, state, and timing information.
    ///
    /// The Metadatas DbSet is the primary storage mechanism for train tracking data
    /// and is used by the EffectRunner to persist train execution details.
    /// </remarks>
    DbSet<Metadata> Metadatas { get; }

    /// <summary>
    /// Gets the DbSet for train log entries.
    /// </summary>
    /// <remarks>
    /// This property provides access to the Log table, which stores detailed log entries
    /// generated during train execution.
    ///
    /// The Logs DbSet allows for fine-grained tracking of train execution junctions
    /// and is particularly useful for debugging and auditing.
    /// </remarks>
    DbSet<Log> Logs { get; }

    /// <summary>
    /// Gets the DbSet for train manifest records.
    /// </summary>
    /// <remarks>
    /// This property provides access to the Manifest table, which stores configuration
    /// and property information for trains.
    ///
    /// The Manifests DbSet allows for storing train configurations and properties
    /// that can be serialized/deserialized as JSONB.
    /// </remarks>
    DbSet<Manifest> Manifests { get; }

    /// <summary>
    /// Gets the DbSet for dead letter records.
    /// </summary>
    /// <remarks>
    /// This property provides access to the DeadLetter table, which stores jobs
    /// that have exceeded their retry limits and require manual intervention.
    ///
    /// The DeadLetters DbSet allows for tracking failed jobs, their resolution status,
    /// and any retry attempts made after dead-lettering.
    /// </remarks>
    DbSet<DeadLetter> DeadLetters { get; }

    /// <summary>
    /// The <c>trax.work_queue</c> table: train runs waiting to be dispatched. Every source of a run
    /// (a manifest's schedule, a dashboard or API trigger, a re-run, a deferred enqueue) inserts a row
    /// here, and the scheduler's dispatcher turns a queued row into a <see cref="Metadata"/> run.
    /// </summary>
    DbSet<WorkQueue> WorkQueues { get; }

    /// <summary>
    /// The <c>trax.manifest_group</c> table: groups of manifests that share dispatch settings (a cap on
    /// concurrently active runs, a dispatch priority, and an enabled switch). Every manifest belongs to
    /// exactly one group.
    /// </summary>
    DbSet<ManifestGroup> ManifestGroups { get; }

    /// <summary>
    /// The <c>trax.background_job</c> table used by the scheduler's built-in database task server: one row
    /// per dispatched run waiting for, or held by, a worker. Workers claim rows and delete them when the
    /// run finishes, so the table holds only in-flight work, not history.
    /// </summary>
    DbSet<BackgroundJob> BackgroundJobs { get; }

    /// <summary>
    /// Singleton-row table holding persisted scheduler runtime settings (the
    /// dashboard-editable subset of <c>SchedulerConfiguration</c>). Always contains
    /// zero or one rows.
    /// </summary>
    DbSet<SchedulerConfig> SchedulerConfigs { get; }

    /// <summary>
    /// Persisted GraphQL operation manifest rows. Maps a build-time-stable
    /// operation id to the document text the server resolves it to.
    /// </summary>
    DbSet<Effect.Models.PersistedOperation.PersistedOperation> PersistedOperations { get; }

    /// <summary>
    /// Append-only audit history for every persisted-operation upsert,
    /// deactivate, and restore.
    /// </summary>
    DbSet<Effect.Models.PersistedOperationHistory.PersistedOperationHistory> PersistedOperationHistories { get; }

    /// <summary>
    /// Nonces Trax.Scheduler's runners accepted on signed requests, shared by every instance of a
    /// runner on this database so a request is accepted once between them.
    /// </summary>
    /// <remarks>
    /// <see cref="DataContext{TDbContext}"/> declares this set. The default here keeps an
    /// implementation written before the member existed compiling and loading: it reads the set
    /// from the implementation as a <see cref="DbContext"/>, the same assumption <c>Raw</c> makes.
    /// </remarks>
    DbSet<Effect.Models.RunnerNonce.RunnerNonce> RunnerNonces =>
        ((DbContext)this).Set<Effect.Models.RunnerNonce.RunnerNonce>();

    /// <summary>
    /// The decisions trains made at run time, recorded by <c>AddDecisionRecording</c> and read
    /// back to replay a requeued run's decisions.
    /// </summary>
    /// <remarks>
    /// <see cref="DataContext{TDbContext}"/> declares this set. The default keeps an implementation
    /// written before the member existed compiling and loading, as <see cref="RunnerNonces"/> does.
    /// </remarks>
    DbSet<Effect.Models.RecordedDecision.RecordedDecision> RecordedDecisions =>
        ((DbContext)this).Set<Effect.Models.RecordedDecision.RecordedDecision>();

    /// <summary>
    /// The <c>trax.junction_run</c> table: the steps of each run, in order, written by
    /// <c>AddJunctionEvents</c>. Read a run's timeline with
    /// <c>JunctionRuns.ForRun(metadataId)</c> (<c>JunctionRunQueries</c>), ordered by <c>Position</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="DataContext{TDbContext}"/> declares this set. The default keeps an implementation
    /// written before the member existed compiling and loading, as <see cref="RunnerNonces"/> does.
    /// </remarks>
    DbSet<Effect.Models.JunctionRun.JunctionRun> JunctionRuns =>
        ((DbContext)this).Set<Effect.Models.JunctionRun.JunctionRun>();

    /// <summary>
    /// The <c>trax.snapshot_draft</c> table: one row per user's state-machine draft or system-owned instance,
    /// written by Trax.Effect.StateMachine.Persistence. Filter by <c>OwnerKind</c> and <c>UserKey</c> when querying
    /// directly, because the draft id alone is unique neither across users nor between a user and the system.
    /// </summary>
    /// <remarks>
    /// <see cref="DataContext{TDbContext}"/> declares this set. The default here keeps an implementation written
    /// before the member existed compiling and loading, as <see cref="RunnerNonces"/> does.
    /// </remarks>
    DbSet<Effect.Models.SnapshotDraft.SnapshotDraft> SnapshotDrafts =>
        ((DbContext)this).Set<Effect.Models.SnapshotDraft.SnapshotDraft>();

    /// <summary>
    /// The <c>trax.effect_claim</c> table: one row per exactly-once effect intent, written by
    /// Trax.Effect.StateMachine.Persistence. A row with a null receipt is in flight; one with a receipt has run.
    /// </summary>
    /// <remarks>
    /// <see cref="DataContext{TDbContext}"/> declares this set. The default here keeps an implementation written
    /// before the member existed compiling and loading, as <see cref="RunnerNonces"/> does.
    /// </remarks>
    DbSet<Effect.Models.EffectClaim.EffectClaim> EffectClaims =>
        ((DbContext)this).Set<Effect.Models.EffectClaim.EffectClaim>();

    #endregion

    /// <summary>
    /// Gets the raw DbContext implementation for advanced operations.
    /// </summary>
    /// <typeparam name="TDbContext">The specific DbContext type</typeparam>
    /// <returns>The underlying DataContext implementation</returns>
    /// <remarks>
    /// This method provides access to the concrete DataContext implementation,
    /// allowing for advanced operations that may not be exposed through the IDataContext interface.
    ///
    /// Use this method with caution, as it bypasses the abstraction provided by IDataContext
    /// and may lead to implementation-specific code.
    /// </remarks>
    DataContext<TDbContext> Raw<TDbContext>()
        where TDbContext : DbContext => (DataContext<TDbContext>)this;

    /// <summary>
    /// Gets or sets the number of changes tracked by the context.
    /// </summary>
    /// <remarks>
    /// This property tracks the number of entities that have been modified but not yet
    /// persisted to the database. It can be used to determine if there are pending changes
    /// that need to be saved.
    /// </remarks>
    int Changes { get; set; }

    /// <summary>
    /// Begins a new database transaction with the default isolation level.
    /// </summary>
    /// <returns>A transaction object that can be used to commit or rollback changes</returns>
    /// <remarks>
    /// This method starts a new database transaction with the default isolation level
    /// (typically ReadCommitted). The transaction must be explicitly committed or rolled back
    /// using the CommitTransaction or RollbackTransaction methods.
    ///
    /// Transactions ensure that multiple database operations are treated as a single atomic unit,
    /// either all succeeding or all failing together.
    /// </remarks>
    Task<IDataContextTransaction> BeginTransaction();

    /// <summary>
    /// Begins a new database transaction with cancellation support.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>A transaction object that can be used to commit or rollback changes</returns>
    Task<IDataContextTransaction> BeginTransaction(CancellationToken cancellationToken);

    /// <summary>
    /// Begins a new database transaction with the specified isolation level.
    /// </summary>
    /// <param name="isolationLevel">The transaction isolation level to use</param>
    /// <returns>A transaction object that can be used to commit or rollback changes</returns>
    /// <remarks>
    /// This method starts a new database transaction with the specified isolation level.
    /// The isolation level determines how the transaction interacts with other concurrent transactions.
    ///
    /// Common isolation levels include:
    /// - ReadUncommitted: Allows dirty reads (reading uncommitted changes from other transactions)
    /// - ReadCommitted: Prevents dirty reads but allows non-repeatable reads
    /// - RepeatableRead: Prevents dirty reads and non-repeatable reads
    /// - Serializable: Provides the highest isolation, preventing all concurrency issues
    ///
    /// The transaction must be explicitly committed or rolled back using the
    /// CommitTransaction or RollbackTransaction methods.
    /// </remarks>
    Task<IDataContextTransaction> BeginTransaction(IsolationLevel isolationLevel);

    /// <summary>
    /// Begins a new database transaction with the specified isolation level and cancellation support.
    /// </summary>
    /// <param name="isolationLevel">The transaction isolation level to use</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>A transaction object that can be used to commit or rollback changes</returns>
    Task<IDataContextTransaction> BeginTransaction(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken
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
    /// depending on the implementation.
    /// </remarks>
    Task CommitTransaction();

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
    /// depending on the implementation.
    /// </remarks>
    Task RollbackTransaction();

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
    void Reset();
}
