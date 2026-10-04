using System.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextTransaction;
using Trax.Effect.Models;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.EffectClaim;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.PersistedOperation;
using Trax.Effect.Models.PersistedOperationHistory;
using Trax.Effect.Models.SchedulerConfig;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Models.WorkQueue;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// An <see cref="IDataContext"/> written before the state-machine tables existed declares neither
/// <see cref="IDataContext.SnapshotDrafts"/> nor <see cref="IDataContext.EffectClaims"/>. The interface's
/// defaults reach them through the implementation's own <see cref="DbContext"/>, so such a context still
/// reads and writes both tables.
/// </summary>
[TestFixture]
public class DataContextDefaultTablesTests
{
    [Test]
    public async Task A_context_without_the_draft_tables_reaches_them_through_its_DbContext()
    {
        var options = new DbContextOptionsBuilder<PreDraftDataContext>()
            .UseInMemoryDatabase($"pre-draft-{Guid.NewGuid():N}")
            .Options;
        var id = Guid.NewGuid();

        await using (var writer = new PreDraftDataContext(options))
        {
            IDataContext context = writer;
            context.SnapshotDrafts.Add(
                new SnapshotDraft
                {
                    Id = id,
                    UserKey = "u",
                    Machine = "turnstile",
                    State = "Locked",
                }
            );
            context.EffectClaims.Add(new EffectClaim { EffectKey = "order:place:u" });
            await writer.SaveChangesAsync();
        }

        await using var reader = new PreDraftDataContext(options);
        IDataContext read = reader;
        (await read.SnapshotDrafts.SingleAsync()).Id.Should().Be(id);
        (await read.EffectClaims.SingleAsync()).EffectKey.Should().Be("order:place:u");
    }

    /// <summary>
    /// Maps only the two state-machine tables, and implements every member a pre-existing context
    /// would, except the two with defaults. The other tables are left out of its model.
    /// </summary>
    private sealed class PreDraftDataContext(DbContextOptions<PreDraftDataContext> options)
        : DbContext(options),
            IDataContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<SnapshotDraft>()
                .HasKey(d => new
                {
                    d.UserKey,
                    d.Machine,
                    d.Id,
                });
            modelBuilder.Entity<EffectClaim>().HasKey(c => c.EffectKey);

            // EF finds a DbSet property even when it is an explicit implementation.
            modelBuilder.Ignore<Trax.Effect.Models.Metadata.Metadata>();
            modelBuilder.Ignore<Log>();
            modelBuilder.Ignore<Manifest>();
            modelBuilder.Ignore<DeadLetter>();
            modelBuilder.Ignore<WorkQueue>();
            modelBuilder.Ignore<ManifestGroup>();
            modelBuilder.Ignore<BackgroundJob>();
            modelBuilder.Ignore<SchedulerConfig>();
            modelBuilder.Ignore<PersistedOperation>();
            modelBuilder.Ignore<PersistedOperationHistory>();
        }

        DbSet<Trax.Effect.Models.Metadata.Metadata> IDataContext.Metadatas =>
            throw new NotSupportedException();
        DbSet<Log> IDataContext.Logs => throw new NotSupportedException();
        DbSet<Manifest> IDataContext.Manifests => throw new NotSupportedException();
        DbSet<DeadLetter> IDataContext.DeadLetters => throw new NotSupportedException();
        DbSet<WorkQueue> IDataContext.WorkQueues => throw new NotSupportedException();
        DbSet<ManifestGroup> IDataContext.ManifestGroups => throw new NotSupportedException();
        DbSet<BackgroundJob> IDataContext.BackgroundJobs => throw new NotSupportedException();
        DbSet<SchedulerConfig> IDataContext.SchedulerConfigs => throw new NotSupportedException();
        DbSet<PersistedOperation> IDataContext.PersistedOperations =>
            throw new NotSupportedException();
        DbSet<PersistedOperationHistory> IDataContext.PersistedOperationHistories =>
            throw new NotSupportedException();

        public int Changes { get; set; }

        public Task<IDataContextTransaction> BeginTransaction() =>
            throw new NotSupportedException();

        public Task<IDataContextTransaction> BeginTransaction(
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<IDataContextTransaction> BeginTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        public Task<IDataContextTransaction> BeginTransaction(
            IsolationLevel isolationLevel,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task CommitTransaction() => throw new NotSupportedException();

        public Task RollbackTransaction() => throw new NotSupportedException();

        public void Reset() => ChangeTracker.Clear();

        public Task SaveChanges(CancellationToken cancellationToken) =>
            SaveChangesAsync(cancellationToken);

        public Task Track(IModel model) => throw new NotSupportedException();

        public Task Update(IModel model) => throw new NotSupportedException();
    }
}
