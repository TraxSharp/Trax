using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the scheduler-config surface of <see cref="IOperationsService"/>.
/// Exercises both the in-memory singleton mutation and the persisted
/// <c>scheduler_config</c> row used by the dashboard's ServerSettingsPage and the
/// GraphQL <c>operations.config.*</c> namespace.
/// </summary>
[TestFixture]
public class OperationsServiceConfigTests : TestSetup
{
    private IOperationsService _operations = null!;
    private SchedulerConfiguration _cfg = null!;

    [SetUp]
    public async Task GetService()
    {
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
        _cfg = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();

        // Reset the in-memory singleton to a known baseline before every test, since
        // it's a process-wide singleton and prior tests may have mutated it.
        _cfg.ManifestManagerEnabled = true;
        _cfg.JobDispatcherEnabled = true;
        _cfg.ManifestManagerPollingInterval = TimeSpan.FromSeconds(5);
        _cfg.JobDispatcherPollingInterval = TimeSpan.FromSeconds(2);
        _cfg.MaxActiveJobs = 10;
        _cfg.DefaultMaxRetries = 3;
        _cfg.DefaultRetryDelay = TimeSpan.FromMinutes(5);
        _cfg.RetryBackoffMultiplier = 2.0;
        _cfg.MaxRetryDelay = TimeSpan.FromHours(1);
        _cfg.DefaultJobTimeout = TimeSpan.FromMinutes(20);
        _cfg.StalePendingTimeout = TimeSpan.FromMinutes(20);
        _cfg.RecoverStuckJobsOnStartup = true;
        _cfg.DeadLetterRetentionPeriod = TimeSpan.FromDays(30);
        _cfg.AutoPurgeDeadLetters = true;

        // Wipe any persisted row so each test starts from a clean slate.
        var ctx = (Microsoft.EntityFrameworkCore.DbContext)DataContext;
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM trax.scheduler_config");
        DataContext.Reset();
    }

    [Test]
    public void GetSchedulerConfig_ReflectsInMemorySingleton()
    {
        _cfg.MaxActiveJobs = 42;
        _cfg.DefaultMaxRetries = 7;

        var snap = _operations.GetSchedulerConfig();

        snap.MaxActiveJobs.Should().Be(42);
        snap.DefaultMaxRetries.Should().Be(7);
        snap.ManifestManagerEnabled.Should().BeTrue();
    }

    [Test]
    public async Task UpdateSchedulerConfig_MutatesSingletonAndPersistsRow()
    {
        var result = await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(
                MaxActiveJobs: 99,
                DefaultMaxRetries: 5,
                DefaultJobTimeout: TimeSpan.FromMinutes(45)
            ),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Count.Should().Be(3);

        // In-memory mutation
        _cfg.MaxActiveJobs.Should().Be(99);
        _cfg.DefaultMaxRetries.Should().Be(5);
        _cfg.DefaultJobTimeout.Should().Be(TimeSpan.FromMinutes(45));

        // Persisted row
        DataContext.Reset();
        var row = DataContext.SchedulerConfigs.Single();
        row.MaxActiveJobs.Should().Be(99);
        row.DefaultMaxRetries.Should().Be(5);
        row.DefaultJobTimeout.Should().Be(TimeSpan.FromMinutes(45));
    }

    [Test]
    public async Task UpdateSchedulerConfig_NoChanges_NoDbWrite()
    {
        // Match current singleton exactly: no fields differ.
        var result = await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(
                MaxActiveJobs: _cfg.MaxActiveJobs,
                DefaultMaxRetries: _cfg.DefaultMaxRetries
            ),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Count.Should().Be(0);

        DataContext.Reset();
        DataContext.SchedulerConfigs.Should().BeEmpty();
    }

    [Test]
    public async Task UpdateSchedulerConfig_PartialPatch_OnlyTouchedFieldsChange()
    {
        await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(MaxActiveJobs: 99, DefaultMaxRetries: 5),
            CancellationToken.None
        );

        // Now update only one field and verify the others stay the same.
        var result = await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(DefaultMaxRetries: 8),
            CancellationToken.None
        );

        result.Count.Should().Be(1);

        DataContext.Reset();
        var row = DataContext.SchedulerConfigs.Single();
        row.MaxActiveJobs.Should().Be(99);
        row.DefaultMaxRetries.Should().Be(8);
    }

    [Test]
    public async Task UpdateSchedulerConfig_ClearMaxActiveJobs_SetsNull()
    {
        _cfg.MaxActiveJobs = 50;

        var result = await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(ClearMaxActiveJobs: true),
            CancellationToken.None
        );

        result.Count.Should().Be(1);
        _cfg.MaxActiveJobs.Should().BeNull();

        DataContext.Reset();
        DataContext.SchedulerConfigs.Single().MaxActiveJobs.Should().BeNull();
    }

    [Test]
    public async Task UpdateSchedulerConfig_ClearMaxActiveJobs_AlreadyNull_NoOp()
    {
        _cfg.MaxActiveJobs = null;

        var result = await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(ClearMaxActiveJobs: true),
            CancellationToken.None
        );

        result.Count.Should().Be(0);

        DataContext.Reset();
        DataContext.SchedulerConfigs.Should().BeEmpty();
    }

    [Test]
    public async Task UpdateSchedulerConfig_BumpsUpdatedAtOnlyOnRealChange()
    {
        // First write creates a row.
        await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(DefaultMaxRetries: 9),
            CancellationToken.None
        );

        DataContext.Reset();
        var firstUpdatedAt = DataContext.SchedulerConfigs.Single().UpdatedAt;

        // Second call with no changes leaves UpdatedAt untouched.
        await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(DefaultMaxRetries: 9),
            CancellationToken.None
        );

        DataContext.Reset();
        DataContext
            .SchedulerConfigs.Single()
            .UpdatedAt.Should()
            .BeCloseTo(firstUpdatedAt, TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public async Task UpdateSchedulerConfig_SecondUpdate_UpdatesExistingRow()
    {
        // First update creates the row.
        await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(DefaultMaxRetries: 4),
            CancellationToken.None
        );
        // Second update on the existing row.
        await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(DefaultMaxRetries: 11),
            CancellationToken.None
        );

        DataContext.Reset();
        var rows = DataContext.SchedulerConfigs.ToList();
        rows.Should().ContainSingle();
        rows[0].DefaultMaxRetries.Should().Be(11);
    }

    [Test]
    public async Task UpdateSchedulerConfig_AllFields_PersistedCorrectly()
    {
        var input = new UpdateSchedulerConfigInput(
            ManifestManagerEnabled: false,
            JobDispatcherEnabled: false,
            ManifestManagerPollingInterval: TimeSpan.FromSeconds(15),
            JobDispatcherPollingInterval: TimeSpan.FromSeconds(20),
            MaxActiveJobs: 50,
            DefaultMaxRetries: 7,
            DefaultRetryDelay: TimeSpan.FromMinutes(10),
            RetryBackoffMultiplier: 3.5,
            MaxRetryDelay: TimeSpan.FromHours(2),
            DefaultJobTimeout: TimeSpan.FromMinutes(45),
            StalePendingTimeout: TimeSpan.FromMinutes(30),
            RecoverStuckJobsOnStartup: false,
            DeadLetterRetentionPeriod: TimeSpan.FromDays(60),
            AutoPurgeDeadLetters: false
        );

        var result = await _operations.UpdateSchedulerConfigAsync(input, CancellationToken.None);

        result.Count.Should().Be(14);

        DataContext.Reset();
        var row = DataContext.SchedulerConfigs.Single();
        row.ManifestManagerEnabled.Should().BeFalse();
        row.JobDispatcherEnabled.Should().BeFalse();
        row.ManifestManagerPollingInterval.Should().Be(TimeSpan.FromSeconds(15));
        row.JobDispatcherPollingInterval.Should().Be(TimeSpan.FromSeconds(20));
        row.MaxActiveJobs.Should().Be(50);
        row.DefaultMaxRetries.Should().Be(7);
        row.DefaultRetryDelay.Should().Be(TimeSpan.FromMinutes(10));
        row.RetryBackoffMultiplier.Should().Be(3.5);
        row.MaxRetryDelay.Should().Be(TimeSpan.FromHours(2));
        row.DefaultJobTimeout.Should().Be(TimeSpan.FromMinutes(45));
        row.StalePendingTimeout.Should().Be(TimeSpan.FromMinutes(30));
        row.RecoverStuckJobsOnStartup.Should().BeFalse();
        row.DeadLetterRetentionPeriod.Should().Be(TimeSpan.FromDays(60));
        row.AutoPurgeDeadLetters.Should().BeFalse();
    }

    [Test]
    public async Task BootstrapHostedService_LoadsPersistedRowAtStartup()
    {
        // Persist a row directly so we can verify the hosted service applies it.
        await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(MaxActiveJobs: 77, DefaultMaxRetries: 13),
            CancellationToken.None
        );

        // Mutate the singleton to something else, then run the bootstrap directly to
        // simulate startup re-applying the persisted row.
        _cfg.MaxActiveJobs = 1;
        _cfg.DefaultMaxRetries = 1;

        var hosted = new SchedulerConfigBootstrapHostedService(
            Scope.ServiceProvider,
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<SchedulerConfigBootstrapHostedService>
                .Instance
        );
        await hosted.StartAsync(CancellationToken.None);

        _cfg.MaxActiveJobs.Should().Be(77);
        _cfg.DefaultMaxRetries.Should().Be(13);
    }

    [Test]
    public async Task BootstrapHostedService_NoPersistedRow_LeavesSingletonUntouched()
    {
        _cfg.DefaultMaxRetries = 99;

        var hosted = new SchedulerConfigBootstrapHostedService(
            Scope.ServiceProvider,
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<SchedulerConfigBootstrapHostedService>
                .Instance
        );
        await hosted.StartAsync(CancellationToken.None);

        _cfg.DefaultMaxRetries.Should().Be(99);
    }

    [Test]
    public void GetSchedulerConfig_TouchesEverySnapshotField()
    {
        // Exercise every accessor on the snapshot record so the synthesised property
        // getters all show as covered.
        var snap = _operations.GetSchedulerConfig();
        _ = snap.ManifestManagerEnabled;
        _ = snap.JobDispatcherEnabled;
        _ = snap.ManifestManagerPollingInterval;
        _ = snap.JobDispatcherPollingInterval;
        _ = snap.MaxActiveJobs;
        _ = snap.DefaultMaxRetries;
        _ = snap.DefaultRetryDelay;
        _ = snap.RetryBackoffMultiplier;
        _ = snap.MaxRetryDelay;
        _ = snap.DefaultJobTimeout;
        _ = snap.StalePendingTimeout;
        _ = snap.RecoverStuckJobsOnStartup;
        _ = snap.DeadLetterRetentionPeriod;
        _ = snap.AutoPurgeDeadLetters;
        _ = snap.LocalWorkerCount;
        _ = snap.MetadataCleanupInterval;
        _ = snap.MetadataCleanupRetention;
    }

    [Test]
    public async Task UpdateSchedulerConfig_MetadataCleanupFields_Persisted()
    {
        // The fixture wires AddMetadataCleanup(), so cfg.MetadataCleanup is non-null and
        // these branches are exercised end-to-end.
        var newInterval = TimeSpan.FromMinutes(7);
        var newRetention = TimeSpan.FromHours(3);

        var result = await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(
                MetadataCleanupInterval: newInterval,
                MetadataCleanupRetention: newRetention
            ),
            CancellationToken.None
        );

        result.Count.Should().Be(2);
        _cfg.MetadataCleanup!.CleanupInterval.Should().Be(newInterval);
        _cfg.MetadataCleanup.RetentionPeriod.Should().Be(newRetention);

        DataContext.Reset();
        var row = DataContext.SchedulerConfigs.Single();
        row.MetadataCleanupInterval.Should().Be(newInterval);
        row.MetadataCleanupRetention.Should().Be(newRetention);
    }

    [Test]
    public async Task UpdateSchedulerConfig_LocalWorkerCountInput_NoOpWhenLocalWorkersNotRegistered()
    {
        // The fixture uses UseInMemoryWorkers, so LocalWorkerOptions is NOT registered;
        // both LocalWorkerCount and ClearLocalWorkerCount must silently not count as
        // changes.
        var result = await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(LocalWorkerCount: 12, ClearLocalWorkerCount: true),
            CancellationToken.None
        );

        result.Count.Should().Be(0);

        DataContext.Reset();
        DataContext.SchedulerConfigs.Should().BeEmpty();
    }

    [Test]
    public void UpdateSchedulerConfigInput_ClearLocalWorkerCount_DefaultsFalse()
    {
        // Touch the synthesised getters so every accessor on the record is covered.
        var input = new UpdateSchedulerConfigInput();
        input.ClearLocalWorkerCount.Should().BeFalse();
        input.LocalWorkerCount.Should().BeNull();
        input.ClearMaxActiveJobs.Should().BeFalse();
    }

    [Test]
    public async Task BootstrapHostedService_StopAsync_NoOp()
    {
        var hosted = new SchedulerConfigBootstrapHostedService(
            Scope.ServiceProvider,
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<SchedulerConfigBootstrapHostedService>
                .Instance
        );
        await hosted.StopAsync(CancellationToken.None);
        // Pure no-op; just exercise the path for coverage.
    }
}
