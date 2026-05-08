using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;
using Metadata = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

public class InMemoryProviderTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services
            .AddScopedTraxRoute<ITestTrain, TestTrain>()
            .AddScopedTraxRoute<IFailingTrain, FailingTrain>()
            .AddScopedTraxRoute<ITypedTrain, TypedTrain>()
            .BuildServiceProvider();

    #region DataContext Direct Tests

    [Test]
    public async Task Track_NewEntity_InsertsOnSave()
    {
        // Arrange
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "TestMetadata",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        // Act
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Assert
        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.Id.Should().Be(metadata.Id);
        found.Name.Should().Be("TestMetadata");
    }

    [Test]
    public async Task Track_ThenUpdate_ThenSave_DoesNotThrow()
    {
        // This is the exact flow ServiceTrain uses:
        // InitializeServiceTrain → Track(metadata)
        // StartServiceTrain → Update(metadata) with state change
        // ServiceTrain.Run → SaveChanges()
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "TrackUpdateTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        // Track (entity goes to Added state)
        await context.Track(metadata);

        // Update (should not force Added → Modified)
        metadata.TrainState = TrainState.InProgress;
        await context.Update(metadata);

        // SaveChanges should INSERT, not UPDATE
        await context.SaveChanges(CancellationToken.None);

        // Verify it persisted
        context.Reset();
        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.TrainState.Should().Be(TrainState.InProgress);
    }

    [Test]
    public async Task Track_ThenSave_ThenModify_ThenSave_PersistsBothWrites()
    {
        // Simulates the full ServiceTrain lifecycle:
        // 1. Track + Save (initial INSERT)
        // 2. Mutate properties + Save (UPDATE)
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "MultiSaveTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        // First save: INSERT
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        var idAfterInsert = metadata.Id;
        idAfterInsert.Should().BeGreaterThan(0, "InMemory provider should auto-generate an Id");

        // Modify and save again: UPDATE
        metadata.TrainState = TrainState.Completed;
        metadata.EndTime = DateTime.UtcNow;
        await context.Update(metadata);
        await context.SaveChanges(CancellationToken.None);

        // Verify the update persisted
        context.Reset();
        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == idAfterInsert);
        found.Should().NotBeNull();
        found!.TrainState.Should().Be(TrainState.Completed);
        found.EndTime.Should().NotBeNull();
    }

    [Test]
    public async Task Track_AlreadyTrackedEntity_IsNoOp()
    {
        // Calling Track twice on the same entity should not throw or change state
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "DoubleTrackTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);
        await context.Track(metadata); // Second call should be a no-op

        // Should still INSERT once, not throw
        await context.SaveChanges(CancellationToken.None);

        context.Reset();
        var count = await context.Metadatas.CountAsync(x => x.Name == "DoubleTrackTest");
        count.Should().Be(1);
    }

    [Test]
    public async Task Update_AlreadyTrackedEntity_DetectsChangesViaSavedSnapshots()
    {
        // After Track + Save, calling Update on a tracked entity should
        // still allow SaveChanges to detect mutations via snapshot comparison
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "SnapshotTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        // Mutate after save (entity is now Unchanged)
        metadata.TrainState = TrainState.Failed;
        await context.Update(metadata); // Should skip base.Update() for tracked entities
        await context.SaveChanges(CancellationToken.None);

        context.Reset();
        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.TrainState.Should().Be(TrainState.Failed);
    }

    [Test]
    public async Task MultipleEntities_CanBeTrackedAndSaved()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata1 = Metadata.Create(
            new CreateMetadata
            {
                Name = "Multi1",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        var metadata2 = Metadata.Create(
            new CreateMetadata
            {
                Name = "Multi2",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata1);
        await context.Track(metadata2);
        await context.SaveChanges(CancellationToken.None);

        context.Reset();
        var found1 = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata1.Id);
        var found2 = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata2.Id);
        found1.Should().NotBeNull();
        found1!.Name.Should().Be("Multi1");
        found2.Should().NotBeNull();
        found2!.Name.Should().Be("Multi2");
    }

    #endregion

    #region Entity Type Tests - SchedulerConfig

    [Test]
    public async Task SchedulerConfig_RoundTrip_PersistsAllColumns()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        // InMemory provider doesn't support ExecuteDeleteAsync; remove rows individually.
        foreach (var existing in await context.SchedulerConfigs.ToListAsync())
            ((Microsoft.EntityFrameworkCore.DbContext)context).Remove(existing);
        await context.SaveChanges(CancellationToken.None);

        // Singleton row with id = 1. Use DbSet.Add directly — see the comment in
        // OperationsService.PersistAsync for why IDataContext.Track misclassifies this.
        var row = new Effect.Models.SchedulerConfig.SchedulerConfig
        {
            ManifestManagerEnabled = false,
            JobDispatcherEnabled = false,
            ManifestManagerPollingInterval = TimeSpan.FromSeconds(15),
            JobDispatcherPollingInterval = TimeSpan.FromSeconds(20),
            MaxActiveJobs = 50,
            DefaultMaxRetries = 7,
            DefaultRetryDelay = TimeSpan.FromMinutes(10),
            RetryBackoffMultiplier = 3.5,
            MaxRetryDelay = TimeSpan.FromHours(2),
            DefaultJobTimeout = TimeSpan.FromMinutes(45),
            StalePendingTimeout = TimeSpan.FromMinutes(30),
            RecoverStuckJobsOnStartup = false,
            DeadLetterRetentionPeriod = TimeSpan.FromDays(60),
            AutoPurgeDeadLetters = false,
            LocalWorkerCount = 8,
            MetadataCleanupInterval = TimeSpan.FromMinutes(7),
            MetadataCleanupRetention = TimeSpan.FromHours(3),
            UpdatedAt = DateTime.UtcNow,
        };

        context.SchedulerConfigs.Add(row);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.SchedulerConfigs.FirstOrDefaultAsync(x =>
            x.Id == Effect.Models.SchedulerConfig.SchedulerConfig.SingletonId
        );

        found.Should().NotBeNull();
        found!.ManifestManagerEnabled.Should().BeFalse();
        found.MaxActiveJobs.Should().Be(50);
        found.RetryBackoffMultiplier.Should().Be(3.5);
        found.DeadLetterRetentionPeriod.Should().Be(TimeSpan.FromDays(60));
        found.LocalWorkerCount.Should().Be(8);
        found.MetadataCleanupInterval.Should().Be(TimeSpan.FromMinutes(7));
        found.MetadataCleanupRetention.Should().Be(TimeSpan.FromHours(3));
    }

    #endregion

    #region Entity Type Tests - PersistedOperation

    [Test]
    public async Task PersistedOperation_RoundTrip_PersistsAllColumns()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        // InMemory provider doesn't support ExecuteDeleteAsync; remove rows individually.
        foreach (var existing in await context.PersistedOperations.ToListAsync())
            ((Microsoft.EntityFrameworkCore.DbContext)context).Remove(existing);
        await context.SaveChanges(CancellationToken.None);

        var now = DateTime.UtcNow;
        var row = new Effect.Models.PersistedOperation.PersistedOperation
        {
            TenantKey = "tenant-rt",
            Id = "userProfile_v3",
            OperationName = "UserProfile",
            Version = 3,
            Document = "query UserProfile($id: Int!) { user(id: $id) { id name email } }",
            ShapeFingerprint = new string('a', 64),
            IsActive = false,
            DeprecationReason = "broken filter",
            Description = "manifest entry for v3",
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.PersistedOperations.Add(row);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.PersistedOperations.FirstOrDefaultAsync(p =>
            p.TenantKey == "tenant-rt" && p.Id == "userProfile_v3"
        );

        found.Should().NotBeNull();
        found!.OperationName.Should().Be("UserProfile");
        found.Version.Should().Be(3);
        found.Document.Should().Contain("$id: Int!");
        found.ShapeFingerprint.Should().Be(new string('a', 64));
        found.IsActive.Should().BeFalse();
        found.DeprecationReason.Should().Be("broken filter");
        found.Description.Should().Be("manifest entry for v3");
        found.CreatedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
        found.UpdatedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task PersistedOperation_NullableColumnsRoundTripAsNull()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var row = new Effect.Models.PersistedOperation.PersistedOperation
        {
            TenantKey = "",
            Id = "nulls_v1",
            OperationName = "Nulls",
            Version = 1,
            Document = "{ x }",
            ShapeFingerprint = "fp",
            DeprecationReason = null,
            Description = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        context.PersistedOperations.Add(row);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.PersistedOperations.FirstOrDefaultAsync(p => p.Id == "nulls_v1");
        found.Should().NotBeNull();
        // tenant_key is part of the composite PK; the schema stores ''
        // for "no tenant". The C#-facing IPersistedOperationStore normalizes
        // null<->"" at the boundary, but at the EF layer the value is "".
        found!.TenantKey.Should().Be(string.Empty);
        found.DeprecationReason.Should().BeNull();
        found.Description.Should().BeNull();
    }

    [Test]
    public async Task PersistedOperationHistory_RoundTrip_PersistsAllColumnsAndAssignsHistoryId()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        foreach (var existing in await context.PersistedOperationHistories.ToListAsync())
            ((Microsoft.EntityFrameworkCore.DbContext)context).Remove(existing);
        await context.SaveChanges(CancellationToken.None);

        var now = DateTime.UtcNow;
        var entry = new Effect.Models.PersistedOperationHistory.PersistedOperationHistory
        {
            TenantKey = "tenant-h",
            Id = "history_v1",
            Document = "query H { z }",
            ShapeFingerprint = new string('b', 64),
            ChangeType = "Upsert",
            ChangedAt = now,
            ChangedReason = "initial",
        };

        context.PersistedOperationHistories.Add(entry);
        await context.SaveChanges(CancellationToken.None);
        entry.HistoryId.Should().BeGreaterThan(0, "the EF model assigns a surrogate key on insert");

        context.Reset();

        var found = await context.PersistedOperationHistories.FirstOrDefaultAsync(h =>
            h.Id == "history_v1"
        );
        found.Should().NotBeNull();
        found!.TenantKey.Should().Be("tenant-h");
        found.Document.Should().Be("query H { z }");
        found.ShapeFingerprint.Should().Be(new string('b', 64));
        found.ChangeType.Should().Be("Upsert");
        found.ChangedReason.Should().Be("initial");
        found.ChangedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task PersistedOperation_QueryByLinq_FiltersCorrectly()
    {
        // Proves the entity is queryable via EF LINQ — the GraphQL hot path
        // and IPersistedOperationStore.GetAsync both rely on this.
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        foreach (var existing in await context.PersistedOperations.ToListAsync())
            ((Microsoft.EntityFrameworkCore.DbContext)context).Remove(existing);
        await context.SaveChanges(CancellationToken.None);

        context.PersistedOperations.AddRange(
            new Effect.Models.PersistedOperation.PersistedOperation
            {
                TenantKey = "",
                Id = "active_v1",
                OperationName = "Active",
                Version = 1,
                Document = "{ x }",
                ShapeFingerprint = "fp",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
            new Effect.Models.PersistedOperation.PersistedOperation
            {
                TenantKey = "",
                Id = "inactive_v1",
                OperationName = "Inactive",
                Version = 1,
                Document = "{ x }",
                ShapeFingerprint = "fp",
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var actives = await context.PersistedOperations.Where(p => p.IsActive).ToListAsync();
        actives.Select(p => p.Id).Should().ContainSingle().Which.Should().Be("active_v1");

        var byOpName = await context
            .PersistedOperations.Where(p => p.OperationName == "Inactive")
            .Select(p => p.Id)
            .ToListAsync();
        byOpName.Should().ContainSingle().Which.Should().Be("inactive_v1");
    }

    #endregion

    #region SchedulerConfig - additional

    [Test]
    public async Task SchedulerConfig_NullableColumnsRoundTripAsNull()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        // InMemory provider doesn't support ExecuteDeleteAsync; remove rows individually.
        foreach (var existing in await context.SchedulerConfigs.ToListAsync())
            ((Microsoft.EntityFrameworkCore.DbContext)context).Remove(existing);
        await context.SaveChanges(CancellationToken.None);

        var row = new Effect.Models.SchedulerConfig.SchedulerConfig
        {
            MaxActiveJobs = null,
            LocalWorkerCount = null,
            MetadataCleanupInterval = null,
            MetadataCleanupRetention = null,
            UpdatedAt = DateTime.UtcNow,
        };

        context.SchedulerConfigs.Add(row);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.SchedulerConfigs.FirstOrDefaultAsync();
        found.Should().NotBeNull();
        found!.MaxActiveJobs.Should().BeNull();
        found.LocalWorkerCount.Should().BeNull();
        found.MetadataCleanupInterval.Should().BeNull();
        found.MetadataCleanupRetention.Should().BeNull();
    }

    #endregion

    #region Transaction Tests

    [Test]
    public async Task BeginTransaction_DoesNotThrow()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var act = async () =>
        {
            var transaction = await context.BeginTransaction();
            transaction.Dispose();
        };

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task BeginTransaction_CommitTransaction_DoesNotThrow()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var act = async () =>
        {
            var transaction = await context.BeginTransaction();
            await context.CommitTransaction();
            transaction.Dispose();
        };

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task BeginTransaction_RollbackTransaction_DoesNotThrow()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var act = async () =>
        {
            var transaction = await context.BeginTransaction();
            await context.RollbackTransaction();
            transaction.Dispose();
        };

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task BeginTransaction_WithCancellationToken_DoesNotThrow()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var act = async () =>
        {
            var transaction = await context.BeginTransaction(CancellationToken.None);
            transaction.Dispose();
        };

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Transaction_TrackAndCommit_PersistsEntity()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "TransactionCommitTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        var transaction = await context.BeginTransaction();
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        await context.CommitTransaction();
        transaction.Dispose();

        context.Reset();
        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.Name.Should().Be("TransactionCommitTest");
    }

    [Test]
    public async Task Transaction_ScheduleManyPattern_DoesNotThrow()
    {
        // Simulates the exact pattern used by TraxScheduler.ScheduleManyAsync:
        // BeginTransaction → Track multiple entities → SaveChanges → CommitTransaction
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata1 = Metadata.Create(
            new CreateMetadata
            {
                Name = "ScheduleMany1",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        var metadata2 = Metadata.Create(
            new CreateMetadata
            {
                Name = "ScheduleMany2",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        var transaction = await context.BeginTransaction();

        try
        {
            await context.Track(metadata1);
            await context.Track(metadata2);
            await context.SaveChanges(CancellationToken.None);
            await context.CommitTransaction();
        }
        catch
        {
            await context.RollbackTransaction();
            throw;
        }
        finally
        {
            transaction.Dispose();
        }

        context.Reset();
        var found1 = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata1.Id);
        var found2 = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata2.Id);
        found1.Should().NotBeNull();
        found1!.Name.Should().Be("ScheduleMany1");
        found2.Should().NotBeNull();
        found2!.Name.Should().Be("ScheduleMany2");
    }

    [Test]
    public async Task FactoryContext_And_ScopedContext_ShareSameDatabase()
    {
        // This reproduces the scheduler bug: TraxScheduler creates contexts via
        // IDataContextProviderFactory (factory path), while polling services resolve
        // IDataContext from DI (scoped path). Both must see the same data.
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var factoryContext = (IDataContext)factory.Create();

        var externalId = Guid.NewGuid().ToString("N");
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "SharedRootTest",
                Input = Unit.Default,
                ExternalId = externalId,
            }
        );

        // Write via factory context (scheduler path)
        await factoryContext.Track(metadata);
        await factoryContext.SaveChanges(CancellationToken.None);

        // Read via scoped context (polling path)
        var scopedContext = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        var found = await scopedContext.Metadatas.FirstOrDefaultAsync(x =>
            x.ExternalId == externalId
        );

        found.Should().NotBeNull("factory and scoped contexts must share the same database root");
        found!.Name.Should().Be("SharedRootTest");
    }

    #endregion

    #region ServiceTrain Integration Tests

    [Test]
    public async Task Run_CompletedTrain_PersistsCorrectMetadata()
    {
        var train = Scope.ServiceProvider.GetRequiredService<ITestTrain>();

        await train.Run(Unit.Default);

        train.Metadata.Should().NotBeNull();
        train.Metadata!.Name.Should().Be(typeof(ITestTrain).FullName);
        train.Metadata.TrainState.Should().Be(TrainState.Completed);
        train.Metadata.FailureException.Should().BeNullOrEmpty();
        train.Metadata.FailureReason.Should().BeNullOrEmpty();
        train.Metadata.FailureJunction.Should().BeNullOrEmpty();
        train.Metadata.Id.Should().BeGreaterThan(0);
        train.Metadata.StartTime.Should().NotBe(default);
        train.Metadata.EndTime.Should().NotBeNull();
    }

    [Test]
    public async Task Run_FailedTrain_PersistsFailureMetadata()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IFailingTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<TrainException>();

        train.Metadata.Should().NotBeNull();
        train.Metadata!.TrainState.Should().Be(TrainState.Failed);
        train.Metadata.FailureException.Should().NotBeNullOrEmpty();
        train.Metadata.FailureReason.Should().NotBeNullOrEmpty();
        train.Metadata.EndTime.Should().NotBeNull();
    }

    [Test]
    public async Task Run_CancelledTrain_ThrowsOperationCancelledException()
    {
        var train = Scope.ServiceProvider.GetRequiredService<ITestTrain>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await train.Run(Unit.Default, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // When a train is pre-cancelled, the cancellation fires inside RunInternal
        // and is caught by the outer exception handler. The metadata should reflect
        // either Cancelled or InProgress depending on whether SaveChanges also got
        // cancelled — either way, the train should NOT be Completed or Pending.
        train.Metadata.Should().NotBeNull();
        train.Metadata!.TrainState.Should().BeOneOf([TrainState.Cancelled, TrainState.InProgress]);
    }

    [Test]
    public async Task Run_MultipleTrainsSequentially_AllComplete()
    {
        // Registered as Scoped, so GetRequiredService returns the same instance.
        // Run one train, then run another independently to verify InMemory
        // handles multiple train executions.
        var train = Scope.ServiceProvider.GetRequiredService<ITestTrain>();
        await train.Run(Unit.Default);

        train.Metadata.Should().NotBeNull();
        train.Metadata!.TrainState.Should().Be(TrainState.Completed);
        train.Metadata.Id.Should().BeGreaterThan(0);

        // Use a different train type in the same scope
        var typedTrain = Scope.ServiceProvider.GetRequiredService<ITypedTrain>();
        var result = await typedTrain.Run("test");

        result.Should().Be(4);
        typedTrain.Metadata.Should().NotBeNull();
        typedTrain.Metadata!.TrainState.Should().Be(TrainState.Completed);
    }

    [Test]
    public async Task Run_TypedTrain_PersistsCorrectState()
    {
        var train = Scope.ServiceProvider.GetRequiredService<ITypedTrain>();

        var result = await train.Run("hello");

        result.Should().Be(5);
        train.Metadata.Should().NotBeNull();
        train.Metadata!.TrainState.Should().Be(TrainState.Completed);
        train.Metadata.Name.Should().Be(typeof(ITypedTrain).FullName);
    }

    [Test]
    public async Task Run_TrainExternalId_IsSetOnMetadata()
    {
        var train = Scope.ServiceProvider.GetRequiredService<ITestTrain>();

        await train.Run(Unit.Default);

        train.Metadata.Should().NotBeNull();
        train.Metadata!.ExternalId.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Test Trains

    private class TestTrain : ServiceTrain<Unit, Unit>, ITestTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input)
        {
            CancellationToken.ThrowIfCancellationRequested();
            return Activate(input).Resolve();
        }
    }

    private interface ITestTrain : IServiceTrain<Unit, Unit> { }

    private class FailingTrain : ServiceTrain<Unit, Unit>, IFailingTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            new TrainException("Intentional failure for testing");
    }

    private interface IFailingTrain : IServiceTrain<Unit, Unit> { }

    private class TypedTrain : ServiceTrain<string, int>, ITypedTrain
    {
        protected override async Task<Either<Exception, int>> RunInternal(string input) =>
            input.Length;
    }

    private interface ITypedTrain : IServiceTrain<string, int> { }

    #endregion
}
