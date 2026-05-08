using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Services.SqliteContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Log.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;
using Metadata = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

public class SqliteProviderTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services
            .AddScopedTraxRoute<ITestTrain, TestTrain>()
            .AddScopedTraxRoute<IFailingTrain, FailingTrain>()
            .BuildServiceProvider();

    #region CRUD Operations

    [Test]
    public async Task Track_NewMetadata_InsertsOnSave()
    {
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

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.Id.Should().Be(metadata.Id);
        found.Name.Should().Be("TestMetadata");
    }

    [Test]
    public async Task Track_NewMetadata_AssignsId()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "IdAssignmentTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        metadata.Id.Should().Be(0, "Id should be 0 before save");

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        metadata.Id.Should().BeGreaterThan(0, "SQLite should auto-generate an Id");
    }

    [Test]
    public async Task Track_MultipleEntities_PersistsAll()
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

    [Test]
    public async Task Track_ThenUpdate_ThenSave_PersistsUpdate()
    {
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

        await context.Track(metadata);
        metadata.TrainState = TrainState.InProgress;
        await context.Update(metadata);
        await context.SaveChanges(CancellationToken.None);

        context.Reset();
        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.TrainState.Should().Be(TrainState.InProgress);
    }

    [Test]
    public async Task Track_ThenSave_ThenModify_ThenSave_PersistsBothWrites()
    {
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

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        var idAfterInsert = metadata.Id;
        idAfterInsert.Should().BeGreaterThan(0);

        metadata.TrainState = TrainState.Completed;
        metadata.EndTime = DateTime.UtcNow;
        await context.Update(metadata);
        await context.SaveChanges(CancellationToken.None);

        context.Reset();
        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == idAfterInsert);
        found.Should().NotBeNull();
        found!.TrainState.Should().Be(TrainState.Completed);
        found.EndTime.Should().NotBeNull();
    }

    [Test]
    public async Task Track_AlreadyTrackedEntity_IsNoOp()
    {
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
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        context.Reset();
        var count = await context.Metadatas.CountAsync(x => x.ExternalId == metadata.ExternalId);
        count.Should().Be(1);
    }

    [Test]
    public async Task SaveChanges_WithNoTrackedEntities_DoesNotThrow()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var act = async () => await context.SaveChanges(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Update_DetachedEntity_ReattachesAndPersists()
    {
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

        metadata.TrainState = TrainState.Failed;
        await context.Update(metadata);
        await context.SaveChanges(CancellationToken.None);

        context.Reset();
        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.TrainState.Should().Be(TrainState.Failed);
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
    public async Task Transaction_Commit_PersistsEntity()
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
    public async Task Transaction_Rollback_DiscardsEntity()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var externalId = Guid.NewGuid().ToString("N");
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "TransactionRollbackTest",
                Input = Unit.Default,
                ExternalId = externalId,
            }
        );

        var transaction = await context.BeginTransaction();
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        await context.RollbackTransaction();
        transaction.Dispose();

        context.Reset();
        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.ExternalId == externalId);
        found.Should().BeNull("rollback should discard the inserted entity");
    }

    [Test]
    public async Task Transaction_CommitAndRollback_DoNotThrow()
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

        var act2 = async () =>
        {
            var transaction = await context.BeginTransaction();
            await context.RollbackTransaction();
            transaction.Dispose();
        };

        await act2.Should().NotThrowAsync();
    }

    [Test]
    public async Task Transaction_WithCancellationToken_DoesNotThrow()
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
    public async Task Transaction_ScheduleManyPattern_PersistsAllEntities()
    {
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
        found2.Should().NotBeNull();
    }

    #endregion

    #region Entity Type Tests - Log

    [Test]
    public async Task Track_Log_PersistsCorrectly()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        // Log requires a metadata foreign key, so create one first
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "LogParent",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        var log = Log.Create(
            new CreateLog
            {
                Level = LogLevel.Information,
                Message = "Test log message",
                CategoryName = "TestCategory",
                EventId = 42,
            }
        );

        // Set the metadata FK via the raw context
        var raw = context.Raw<SqliteContext>();
        var logEntry = raw.Entry(log);
        logEntry.Property("MetadataId").CurrentValue = metadata.Id;

        await context.Track(log);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.Logs.FirstOrDefaultAsync(x => x.Id == log.Id);
        found.Should().NotBeNull();
        found!.Level.Should().Be(LogLevel.Information);
        found.Message.Should().Be("Test log message");
        found.Category.Should().Be("TestCategory");
        found.EventId.Should().Be(42);
    }

    #endregion

    #region Entity Type Tests - SchedulerConfig

    [Test]
    public async Task SchedulerConfig_RoundTrip_PersistsAllColumns()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        // Singleton-row table: ensure no leftover row from a prior test in the fixture.
        await context.SchedulerConfigs.ExecuteDeleteAsync();

        // SchedulerConfig is a singleton row (id always = 1) and the model initialiser
        // sets Id = 1 in its ctor. The shared OperationsService uses DbSet.Add directly
        // for inserts because IDataContext.Track infers Added/Modified from `Id > 0`,
        // which would misclassify the singleton as an update on first persist.
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
        found.JobDispatcherEnabled.Should().BeFalse();
        found.ManifestManagerPollingInterval.Should().Be(TimeSpan.FromSeconds(15));
        found.JobDispatcherPollingInterval.Should().Be(TimeSpan.FromSeconds(20));
        found.MaxActiveJobs.Should().Be(50);
        found.DefaultMaxRetries.Should().Be(7);
        found.DefaultRetryDelay.Should().Be(TimeSpan.FromMinutes(10));
        found.RetryBackoffMultiplier.Should().Be(3.5);
        found.MaxRetryDelay.Should().Be(TimeSpan.FromHours(2));
        found.DefaultJobTimeout.Should().Be(TimeSpan.FromMinutes(45));
        found.StalePendingTimeout.Should().Be(TimeSpan.FromMinutes(30));
        found.RecoverStuckJobsOnStartup.Should().BeFalse();
        found.DeadLetterRetentionPeriod.Should().Be(TimeSpan.FromDays(60));
        found.AutoPurgeDeadLetters.Should().BeFalse();
        found.LocalWorkerCount.Should().Be(8);
        found.MetadataCleanupInterval.Should().Be(TimeSpan.FromMinutes(7));
        found.MetadataCleanupRetention.Should().Be(TimeSpan.FromHours(3));
    }

    [Test]
    public async Task SchedulerConfig_NullableColumnsRoundTripAsNull()
    {
        // Defaults: MaxActiveJobs is set to 10 by the model initialiser; the rest of the
        // nullable columns (LocalWorkerCount, MetadataCleanup*) default to null. Verify
        // each round-trips correctly when explicitly null.
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await context.SchedulerConfigs.ExecuteDeleteAsync();

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

    [Test]
    public async Task SchedulerConfig_UpdateExistingRow_UpdatesNotInserts()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await context.SchedulerConfigs.ExecuteDeleteAsync();

        // Insert
        context.SchedulerConfigs.Add(
            new Effect.Models.SchedulerConfig.SchedulerConfig
            {
                DefaultMaxRetries = 5,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Update
        var existing = await context.SchedulerConfigs.FirstAsync();
        existing.DefaultMaxRetries = 11;
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Still exactly one row, with the updated value.
        var rows = await context.SchedulerConfigs.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].DefaultMaxRetries.Should().Be(11);
    }

    #endregion

    #region Entity Type Tests - PersistedOperation

    [Test]
    public async Task PersistedOperation_RoundTrip_PersistsAllColumns()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await context.PersistedOperationHistories.ExecuteDeleteAsync();
        await context.PersistedOperations.ExecuteDeleteAsync();

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
        await context.PersistedOperationHistories.ExecuteDeleteAsync();
        await context.PersistedOperations.ExecuteDeleteAsync();

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
        // tenant_key is on the composite PK; "" is the sentinel for "no tenant".
        found!.TenantKey.Should().Be(string.Empty);
        found.DeprecationReason.Should().BeNull();
        found.Description.Should().BeNull();
    }

    [Test]
    public async Task PersistedOperation_CompositeKey_AllowsSameIdAcrossTenants()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await context.PersistedOperationHistories.ExecuteDeleteAsync();
        await context.PersistedOperations.ExecuteDeleteAsync();

        context.PersistedOperations.AddRange(
            new Effect.Models.PersistedOperation.PersistedOperation
            {
                TenantKey = "a",
                Id = "shared_v1",
                OperationName = "Shared",
                Version = 1,
                Document = "query Shared { a }",
                ShapeFingerprint = "h1",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
            new Effect.Models.PersistedOperation.PersistedOperation
            {
                TenantKey = "b",
                Id = "shared_v1",
                OperationName = "Shared",
                Version = 1,
                Document = "query Shared { b }",
                ShapeFingerprint = "h2",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var rows = await context
            .PersistedOperations.Where(p => p.Id == "shared_v1")
            .OrderBy(p => p.TenantKey)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows[0].Document.Should().Contain("a");
        rows[1].Document.Should().Contain("b");
    }

    [Test]
    public async Task PersistedOperationHistory_RoundTrip_PersistsAllColumnsAndAssignsHistoryId()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await context.PersistedOperationHistories.ExecuteDeleteAsync();

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
        entry.HistoryId.Should().BeGreaterThan(0, "the surrogate key is DB-generated");

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
    public async Task PersistedOperationHistory_OrderedByChangedAtDesc_ReturnsMostRecentFirst()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await context.PersistedOperationHistories.ExecuteDeleteAsync();

        for (var i = 0; i < 3; i++)
        {
            context.PersistedOperationHistories.Add(
                new Effect.Models.PersistedOperationHistory.PersistedOperationHistory
                {
                    TenantKey = "",
                    Id = "ordered_v1",
                    Document = $"v{i}",
                    ShapeFingerprint = "fp",
                    ChangeType = "Upsert",
                    ChangedAt = DateTime.UtcNow.AddMinutes(i),
                    ChangedReason = $"step{i}",
                }
            );
        }
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var rows = await context
            .PersistedOperationHistories.Where(h => h.Id == "ordered_v1")
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => h.Document)
            .ToListAsync();

        rows.Should().Equal("v2", "v1", "v0");
    }

    [Test]
    public async Task PersistedOperation_FilterByIsActive_ReturnsOnlyActive()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        await context.PersistedOperationHistories.ExecuteDeleteAsync();
        await context.PersistedOperations.ExecuteDeleteAsync();

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
    }

    #endregion

    #region Entity Type Tests - ManifestGroup

    [Test]
    public async Task Track_ManifestGroup_PersistsCorrectly()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var group = new ManifestGroup
        {
            Name = $"test-group-{Guid.NewGuid():N}",
            Priority = 5,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await context.Track(group);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.ManifestGroups.FirstOrDefaultAsync(x => x.Id == group.Id);
        found.Should().NotBeNull();
        found!.Name.Should().Be(group.Name);
        found.Priority.Should().Be(5);
        found.IsEnabled.Should().BeTrue();
    }

    #endregion

    #region Entity Type Tests - Manifest

    [Test]
    public async Task Track_Manifest_PersistsCorrectly()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        // Create a ManifestGroup first (Manifest requires ManifestGroupId)
        var group = new ManifestGroup
        {
            Name = $"manifest-test-group-{Guid.NewGuid():N}",
            Priority = 0,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await context.Track(group);
        await context.SaveChanges(CancellationToken.None);

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(ITestTrain),
                ScheduleType = ScheduleType.Cron,
                CronExpression = "0 3 * * *",
                MaxRetries = 5,
            }
        );
        manifest.ManifestGroupId = group.Id;

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.Manifests.FirstOrDefaultAsync(x => x.Id == manifest.Id);
        found.Should().NotBeNull();
        found!.Name.Should().Be(typeof(ITestTrain).FullName);
        found.ScheduleType.Should().Be(ScheduleType.Cron);
        found.CronExpression.Should().Be("0 3 * * *");
        found.MaxRetries.Should().Be(5);
        found.ManifestGroupId.Should().Be(group.Id);
    }

    #endregion

    #region Entity Type Tests - WorkQueue

    [Test]
    public async Task Track_WorkQueue_PersistsCorrectly()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var workQueue = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(ITestTrain).FullName!,
                Input = """{"key":"value"}""",
                InputTypeName = "System.String",
                Priority = 10,
            }
        );

        await context.Track(workQueue);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.WorkQueues.FirstOrDefaultAsync(x => x.Id == workQueue.Id);
        found.Should().NotBeNull();
        found!.TrainName.Should().Be(typeof(ITestTrain).FullName);
        found.Input.Should().Be("""{"key":"value"}""");
        found.Status.Should().Be(WorkQueueStatus.Queued);
        found.Priority.Should().Be(10);
    }

    #endregion

    #region Entity Type Tests - BackgroundJob

    [Test]
    public async Task Track_BackgroundJob_PersistsCorrectly()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        // BackgroundJob requires a MetadataId
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "BackgroundJobParent",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metadata.Id,
                Input = """{"task":"process"}""",
                InputType = "System.String",
                Priority = 3,
            }
        );

        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == job.Id);
        found.Should().NotBeNull();
        found!.MetadataId.Should().Be(metadata.Id);
        found.Input.Should().Be("""{"task":"process"}""");
        found.InputType.Should().Be("System.String");
        found.Priority.Should().Be(3);
    }

    #endregion

    #region Entity Type Tests - DeadLetter

    [Test]
    public async Task Track_DeadLetter_PersistsCorrectly()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        // DeadLetter requires a Manifest (which requires a ManifestGroup)
        var group = new ManifestGroup
        {
            Name = $"deadletter-group-{Guid.NewGuid():N}",
            Priority = 0,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await context.Track(group);
        await context.SaveChanges(CancellationToken.None);

        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(ITestTrain), ScheduleType = ScheduleType.None }
        );
        manifest.ManifestGroupId = group.Id;

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Max retries exceeded",
                RetryCount = 3,
            }
        );

        await context.Track(deadLetter);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.DeadLetters.FirstOrDefaultAsync(x => x.Id == deadLetter.Id);
        found.Should().NotBeNull();
        found!.Reason.Should().Be("Max retries exceeded");
        found.RetryCountAtDeadLetter.Should().Be(3);
        found.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);
        found.ManifestId.Should().Be(manifest.Id);
    }

    #endregion

    #region Factory vs Scoped Context

    [Test]
    public async Task FactoryContext_And_ScopedContext_ShareSameDatabase()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var factoryContext = (IDataContext)factory.Create();

        var externalId = Guid.NewGuid().ToString("N");
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "SharedDatabaseTest",
                Input = Unit.Default,
                ExternalId = externalId,
            }
        );

        await factoryContext.Track(metadata);
        await factoryContext.SaveChanges(CancellationToken.None);

        var scopedContext = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        var found = await scopedContext.Metadatas.FirstOrDefaultAsync(x =>
            x.ExternalId == externalId
        );

        found.Should().NotBeNull("factory and scoped contexts must share the same SQLite database");
        found!.Name.Should().Be("SharedDatabaseTest");
    }

    [Test]
    public async Task MultipleFactoryContexts_ShareSameDatabase()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context1 = (IDataContext)factory.Create();
        var context2 = (IDataContext)factory.Create();

        var externalId = Guid.NewGuid().ToString("N");
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "MultiFactoryTest",
                Input = Unit.Default,
                ExternalId = externalId,
            }
        );

        await context1.Track(metadata);
        await context1.SaveChanges(CancellationToken.None);

        var found = await context2.Metadatas.FirstOrDefaultAsync(x => x.ExternalId == externalId);

        found.Should().NotBeNull("all factory contexts must share the same database file");
    }

    #endregion

    #region Reset Tests

    [Test]
    public async Task Reset_ClearsChangeTracker()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "ResetTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);

        // After Track, the entity is in the change tracker
        var raw = context.Raw<SqliteContext>();
        raw.ChangeTracker.Entries().Should().NotBeEmpty();

        context.Reset();

        raw.ChangeTracker.Entries().Should().BeEmpty("Reset should clear all tracked entities");
    }

    [Test]
    public async Task Reset_AfterSave_AllowsFreshQuery()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "ResetQueryTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Querying after reset should return the entity from the database, not the tracker
        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.Name.Should().Be("ResetQueryTest");
    }

    #endregion

    #region JSON Column Tests

    [Test]
    public async Task Metadata_InputJson_SerializesAsTextAndRoundTrips()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "JsonInputTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        // Manually set Input JSON to verify TEXT storage
        metadata.Input = """{"testKey":"testValue","number":42}""";

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.Input.Should().Be("""{"testKey":"testValue","number":42}""");
    }

    [Test]
    public async Task Metadata_OutputJson_SerializesAsTextAndRoundTrips()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "JsonOutputTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        metadata.Output = """{"result":"success","count":7}""";

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.Output.Should().Be("""{"result":"success","count":7}""");
    }

    [Test]
    public async Task Metadata_HostLabelsJson_SerializesAsTextAndRoundTrips()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "HostLabelsTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        metadata.HostLabels = """{"region":"us-east-1","team":"platform"}""";

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.HostLabels.Should().Be("""{"region":"us-east-1","team":"platform"}""");
    }

    [Test]
    public async Task WorkQueue_InputJson_RoundTrips()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var workQueue = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(ITestTrain).FullName!,
                Input = """{"items":[1,2,3],"nested":{"a":"b"}}""",
                InputTypeName = "TestInput",
            }
        );

        await context.Track(workQueue);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.WorkQueues.FirstOrDefaultAsync(x => x.Id == workQueue.Id);
        found.Should().NotBeNull();
        found!.Input.Should().Be("""{"items":[1,2,3],"nested":{"a":"b"}}""");
    }

    [Test]
    public async Task Manifest_PropertiesJson_RoundTrips()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var group = new ManifestGroup
        {
            Name = $"json-props-group-{Guid.NewGuid():N}",
            Priority = 0,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await context.Track(group);
        await context.SaveChanges(CancellationToken.None);

        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(ITestTrain), ScheduleType = ScheduleType.None }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.Properties = """{"$type":"TestProps","value":123}""";

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.Manifests.FirstOrDefaultAsync(x => x.Id == manifest.Id);
        found.Should().NotBeNull();
        found!.Properties.Should().Be("""{"$type":"TestProps","value":123}""");
    }

    [Test]
    public async Task Manifest_ExclusionsJson_RoundTrips()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var group = new ManifestGroup
        {
            Name = $"exclusions-group-{Guid.NewGuid():N}",
            Priority = 0,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await context.Track(group);
        await context.SaveChanges(CancellationToken.None);

        var exclusionsJson = """[{"dayOfWeek":0,"startHour":0,"endHour":6}]""";
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(ITestTrain),
                ScheduleType = ScheduleType.Cron,
                CronExpression = "0 * * * *",
                Exclusions = exclusionsJson,
            }
        );
        manifest.ManifestGroupId = group.Id;

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.Manifests.FirstOrDefaultAsync(x => x.Id == manifest.Id);
        found.Should().NotBeNull();
        found!.Exclusions.Should().Be(exclusionsJson);
    }

    [Test]
    public async Task BackgroundJob_InputJson_RoundTrips()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "BgJobJsonTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metadata.Id,
                Input = """{"command":"run","args":["--verbose"]}""",
                InputType = "CommandInput",
            }
        );

        await context.Track(job);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.BackgroundJobs.FirstOrDefaultAsync(x => x.Id == job.Id);
        found.Should().NotBeNull();
        found!.Input.Should().Be("""{"command":"run","args":["--verbose"]}""");
    }

    [Test]
    public async Task NullJsonColumns_PersistAsNull()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "NullJsonTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        // Explicitly null out JSON fields
        metadata.Input = null;
        metadata.Output = null;
        metadata.HostLabels = null;

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.Input.Should().BeNull();
        found.Output.Should().BeNull();
        found.HostLabels.Should().BeNull();
    }

    #endregion

    #region DateTime/UTC Tests

    [Test]
    public async Task DateTime_StoredAsUtc_ReturnedAsUtc()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var now = DateTime.UtcNow;
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "UtcTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        metadata.EndTime = now;

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.StartTime.Kind.Should().Be(DateTimeKind.Utc);
        found.EndTime.Should().NotBeNull();
        found.EndTime!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Test]
    public async Task DateTime_NullableFields_PreserveUtcKind()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var scheduledTime = new DateTime(2026, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "NullableDateTimeTest",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        metadata.ScheduledTime = scheduledTime;
        metadata.EndTime = null;

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
        found.Should().NotBeNull();
        found!.ScheduledTime.Should().NotBeNull();
        found.ScheduledTime!.Value.Kind.Should().Be(DateTimeKind.Utc);
        found.ScheduledTime.Value.Should().BeCloseTo(scheduledTime, TimeSpan.FromSeconds(1));
        found.EndTime.Should().BeNull();
    }

    [Test]
    public async Task ManifestGroup_DateTimes_RoundTripAsUtc()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var createdAt = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 3, 20, 14, 30, 0, DateTimeKind.Utc);

        var group = new ManifestGroup
        {
            Name = $"utc-group-{Guid.NewGuid():N}",
            Priority = 0,
            IsEnabled = true,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };

        await context.Track(group);
        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        var found = await context.ManifestGroups.FirstOrDefaultAsync(x => x.Id == group.Id);
        found.Should().NotBeNull();
        found!.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        found.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
        found.CreatedAt.Should().BeCloseTo(createdAt, TimeSpan.FromSeconds(1));
        found.UpdatedAt.Should().BeCloseTo(updatedAt, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Enum Storage Tests

    [Test]
    public async Task TrainState_AllValues_RoundTrip()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        foreach (var state in Enum.GetValues<TrainState>())
        {
            var metadata = Metadata.Create(
                new CreateMetadata
                {
                    Name = $"EnumTest_{state}",
                    Input = Unit.Default,
                    ExternalId = Guid.NewGuid().ToString("N"),
                }
            );

            metadata.TrainState = state;

            await context.Track(metadata);
            await context.SaveChanges(CancellationToken.None);
            context.Reset();

            var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);
            found.Should().NotBeNull($"metadata with TrainState.{state} should be persisted");
            found!.TrainState.Should().Be(state);
            context.Reset();
        }
    }

    [Test]
    public async Task LogLevel_AllCommonValues_RoundTrip()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        // Create a parent metadata
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "LogLevelEnumParent",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        var levels = new[]
        {
            LogLevel.Trace,
            LogLevel.Debug,
            LogLevel.Information,
            LogLevel.Warning,
            LogLevel.Error,
            LogLevel.Critical,
        };

        foreach (var level in levels)
        {
            var log = Log.Create(
                new CreateLog
                {
                    Level = level,
                    Message = $"Log at {level}",
                    CategoryName = "EnumTest",
                    EventId = (int)level,
                }
            );

            var raw = context.Raw<SqliteContext>();
            raw.Entry(log).Property("MetadataId").CurrentValue = metadata.Id;

            await context.Track(log);
            await context.SaveChanges(CancellationToken.None);
            context.Reset();

            var found = await context.Logs.FirstOrDefaultAsync(x => x.Id == log.Id);
            found.Should().NotBeNull($"log with LogLevel.{level} should be persisted");
            found!.Level.Should().Be(level);
            context.Reset();
        }
    }

    [Test]
    public async Task ScheduleType_AllValues_RoundTrip()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var group = new ManifestGroup
        {
            Name = $"schedule-enum-group-{Guid.NewGuid():N}",
            Priority = 0,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await context.Track(group);
        await context.SaveChanges(CancellationToken.None);

        foreach (var scheduleType in Enum.GetValues<ScheduleType>())
        {
            var manifest = Manifest.Create(
                new CreateManifest
                {
                    Name = typeof(ITestTrain),
                    ScheduleType = scheduleType,
                    CronExpression = scheduleType == ScheduleType.Cron ? "0 * * * *" : null,
                    IntervalSeconds = scheduleType == ScheduleType.Interval ? 60 : null,
                }
            );
            manifest.ManifestGroupId = group.Id;

            await context.Track(manifest);
            await context.SaveChanges(CancellationToken.None);
            context.Reset();

            var found = await context.Manifests.FirstOrDefaultAsync(x => x.Id == manifest.Id);
            found.Should().NotBeNull($"manifest with ScheduleType.{scheduleType} should persist");
            found!.ScheduleType.Should().Be(scheduleType);
            context.Reset();
        }
    }

    [Test]
    public async Task DeadLetterStatus_AllValues_RoundTrip()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var group = new ManifestGroup
        {
            Name = $"dl-enum-group-{Guid.NewGuid():N}",
            Priority = 0,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await context.Track(group);
        await context.SaveChanges(CancellationToken.None);

        var manifest = Manifest.Create(
            new CreateManifest { Name = typeof(ITestTrain), ScheduleType = ScheduleType.None }
        );
        manifest.ManifestGroupId = group.Id;

        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        foreach (var status in Enum.GetValues<DeadLetterStatus>())
        {
            var deadLetter = DeadLetter.Create(
                new CreateDeadLetter
                {
                    Manifest = manifest,
                    Reason = $"Status test: {status}",
                    RetryCount = 1,
                }
            );

            // DeadLetter.Create sets status to AwaitingIntervention; override it
            if (status == DeadLetterStatus.Retried)
                deadLetter.Requeue("test retry");
            else if (status == DeadLetterStatus.Acknowledged)
                deadLetter.Acknowledge("test");

            await context.Track(deadLetter);
            await context.SaveChanges(CancellationToken.None);
            context.Reset();

            var found = await context.DeadLetters.FirstOrDefaultAsync(x => x.Id == deadLetter.Id);
            found.Should().NotBeNull($"dead letter with status {status} should persist");
            found!.Status.Should().Be(status);
            context.Reset();
        }
    }

    [Test]
    public async Task WorkQueueStatus_AllValues_RoundTrip()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        foreach (var status in Enum.GetValues<WorkQueueStatus>())
        {
            var workQueue = WorkQueue.Create(
                new CreateWorkQueue { TrainName = typeof(ITestTrain).FullName! }
            );
            workQueue.Status = status;

            await context.Track(workQueue);
            await context.SaveChanges(CancellationToken.None);
            context.Reset();

            var found = await context.WorkQueues.FirstOrDefaultAsync(x => x.Id == workQueue.Id);
            found.Should().NotBeNull($"work queue with status {status} should persist");
            found!.Status.Should().Be(status);
            context.Reset();
        }
    }

    [Test]
    public async Task MisfirePolicy_AllValues_RoundTrip()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var group = new ManifestGroup
        {
            Name = $"misfire-enum-group-{Guid.NewGuid():N}",
            Priority = 0,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await context.Track(group);
        await context.SaveChanges(CancellationToken.None);

        foreach (var policy in Enum.GetValues<MisfirePolicy>())
        {
            var manifest = Manifest.Create(
                new CreateManifest
                {
                    Name = typeof(ITestTrain),
                    ScheduleType = ScheduleType.Cron,
                    CronExpression = "0 * * * *",
                    MisfirePolicy = policy,
                }
            );
            manifest.ManifestGroupId = group.Id;

            await context.Track(manifest);
            await context.SaveChanges(CancellationToken.None);
            context.Reset();

            var found = await context.Manifests.FirstOrDefaultAsync(x => x.Id == manifest.Id);
            found.Should().NotBeNull($"manifest with MisfirePolicy.{policy} should persist");
            found!.MisfirePolicy.Should().Be(policy);
            context.Reset();
        }
    }

    #endregion

    #region Change Tracking Tests

    [Test]
    public async Task ChangeTracking_NewEntity_IsAdded()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        var raw = context.Raw<SqliteContext>();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "ChangeTrackingAdded",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);

        var entry = raw.Entry(metadata);
        entry.State.Should().Be(EntityState.Added);
    }

    [Test]
    public async Task ChangeTracking_AfterSave_IsUnchanged()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        var raw = context.Raw<SqliteContext>();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "ChangeTrackingUnchanged",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        var entry = raw.Entry(metadata);
        entry.State.Should().Be(EntityState.Unchanged);
    }

    [Test]
    public async Task ChangeTracking_ModifyTrackedEntity_IsModified()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        var raw = context.Raw<SqliteContext>();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "ChangeTrackingModified",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        // Mutate a property on the tracked entity
        metadata.TrainState = TrainState.Completed;

        var entry = raw.Entry(metadata);
        entry.State.Should().Be(EntityState.Modified);
    }

    [Test]
    public async Task ChangeTracking_Reset_ClearsAllEntries()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        var raw = context.Raw<SqliteContext>();

        var metadata1 = Metadata.Create(
            new CreateMetadata
            {
                Name = "ChangeTrackingReset1",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        var metadata2 = Metadata.Create(
            new CreateMetadata
            {
                Name = "ChangeTrackingReset2",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata1);
        await context.Track(metadata2);
        await context.SaveChanges(CancellationToken.None);

        raw.ChangeTracker.Entries().Should().HaveCount(2);

        context.Reset();

        raw.ChangeTracker.Entries().Should().BeEmpty();
    }

    #endregion

    #region SqlDialect Tests

    [Test]
    public void SqlDialect_IsRegistered_AsSqlite()
    {
        var dialect = Scope.ServiceProvider.GetRequiredService<ISqlDialect>();
        dialect.Should().NotBeNull();
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

        train.Metadata.Should().NotBeNull();
        train.Metadata!.TrainState.Should().BeOneOf([TrainState.Cancelled, TrainState.InProgress]);
    }

    [Test]
    public async Task Run_TrainExternalId_IsSetOnMetadata()
    {
        var train = Scope.ServiceProvider.GetRequiredService<ITestTrain>();

        await train.Run(Unit.Default);

        train.Metadata.Should().NotBeNull();
        train.Metadata!.ExternalId.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Run_CompletedTrain_MetadataPersistedToDatabase()
    {
        var train = Scope.ServiceProvider.GetRequiredService<ITestTrain>();

        await train.Run(Unit.Default);

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();

        var found = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == train.Metadata!.Id);
        found.Should().NotBeNull();
        found!.TrainState.Should().Be(TrainState.Completed);
        found.Name.Should().Be(typeof(ITestTrain).FullName);
    }

    #endregion

    #region Test Trains

    private interface ITestTrain : IServiceTrain<Unit, Unit> { }

    private class TestTrain : ServiceTrain<Unit, Unit>, ITestTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input)
        {
            CancellationToken.ThrowIfCancellationRequested();
            return Activate(input).Resolve();
        }
    }

    private interface IFailingTrain : IServiceTrain<Unit, Unit> { }

    private class FailingTrain : ServiceTrain<Unit, Unit>, IFailingTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            new TrainException("Test failure");
    }

    #endregion
}
