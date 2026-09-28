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

    private static IEnumerable<TestCaseData> OutOfRangeConfigPatches()
    {
        TestCaseData Case(UpdateSchedulerConfigInput input, string field) =>
            new TestCaseData(input, field).SetName(
                $"UpdateSchedulerConfig_{field}_OutOfRange_Refused"
            );

        yield return Case(
            new(ManifestManagerPollingInterval: TimeSpan.Zero),
            "ManifestManagerPollingInterval"
        );
        yield return Case(
            new(JobDispatcherPollingInterval: TimeSpan.FromSeconds(-1)),
            "JobDispatcherPollingInterval"
        );
        yield return Case(new(MaxActiveJobs: 0), "MaxActiveJobs");
        yield return Case(new(DefaultMaxRetries: -1), "DefaultMaxRetries");
        yield return Case(new(DefaultRetryDelay: TimeSpan.FromSeconds(-1)), "DefaultRetryDelay");
        yield return Case(new(RetryBackoffMultiplier: 0.5), "RetryBackoffMultiplier");
        yield return Case(new(MaxRetryDelay: TimeSpan.FromSeconds(-1)), "MaxRetryDelay");
        yield return Case(new(DefaultJobTimeout: TimeSpan.Zero), "DefaultJobTimeout");
        yield return Case(new(StalePendingTimeout: TimeSpan.Zero), "StalePendingTimeout");
        yield return Case(
            new(DeadLetterRetentionPeriod: TimeSpan.FromDays(-1)),
            "DeadLetterRetentionPeriod"
        );
        yield return Case(new(LocalWorkerCount: 0), "LocalWorkerCount");
        yield return Case(new(MetadataCleanupInterval: TimeSpan.Zero), "MetadataCleanupInterval");
        yield return Case(new(MetadataCleanupRetention: TimeSpan.Zero), "MetadataCleanupRetention");
    }

    [TestCaseSource(nameof(OutOfRangeConfigPatches))]
    public async Task UpdateSchedulerConfig_OutOfRange_IsAFailedResultAndChangesNothing(
        UpdateSchedulerConfigInput input,
        string field
    )
    {
        // A valid field alongside the invalid one: a refused patch applies neither.
        var result = await _operations.UpdateSchedulerConfigAsync(
            input with
            {
                DefaultMaxRetries = input.DefaultMaxRetries ?? 9,
            },
            CancellationToken.None
        );

        result.Success.Should().BeFalse("the service validates, not only the dashboard's form");
        result.Message.Should().Contain(field);
        _cfg.DefaultMaxRetries.Should().Be(3, "nothing in a refused patch is applied");
        _cfg.MaxActiveJobs.Should().Be(10);
        DataContext.Reset();
        DataContext.SchedulerConfigs.Should().BeEmpty("a refused patch is not persisted");
    }

    [Test]
    public async Task UpdateSchedulerConfig_NotANumberMultiplier_Refused()
    {
        var result = await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(RetryBackoffMultiplier: double.NaN),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("RetryBackoffMultiplier");
    }

    [Test]
    public async Task UpdateSchedulerConfig_BoundaryValues_Accepted()
    {
        var result = await _operations.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(
                MaxActiveJobs: 1,
                DefaultMaxRetries: 0,
                DefaultRetryDelay: TimeSpan.Zero,
                RetryBackoffMultiplier: 1.0,
                DeadLetterRetentionPeriod: TimeSpan.Zero
            ),
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
    }

    private static IEnumerable<TestCaseData> UnusableDurationPatches()
    {
        TestCaseData Case(UpdateSchedulerConfigInput input, string field, string why) =>
            new TestCaseData(input, field).SetName($"UpdateSchedulerConfig_{field}_{why}_Refused");

        // PeriodicTimer throws below 1 ms, which would fault the poller on the next boot.
        yield return Case(
            new(ManifestManagerPollingInterval: TimeSpan.FromTicks(5_000)),
            "ManifestManagerPollingInterval",
            "SubMillisecond"
        );
        yield return Case(
            new(JobDispatcherPollingInterval: TimeSpan.FromMilliseconds(999)),
            "JobDispatcherPollingInterval",
            "UnderOneSecond"
        );
        yield return Case(
            new(MetadataCleanupInterval: TimeSpan.FromTicks(1)),
            "MetadataCleanupInterval",
            "SubMillisecond"
        );
        // PeriodicTimer also throws above about 49.7 days.
        yield return Case(
            new(ManifestManagerPollingInterval: TimeSpan.FromDays(60)),
            "ManifestManagerPollingInterval",
            "PastTheTimerLimit"
        );
        yield return Case(
            new(StalePendingTimeout: TimeSpan.FromDays(100_000)),
            "StalePendingTimeout",
            "Unbounded"
        );
        yield return Case(
            new(DeadLetterRetentionPeriod: TimeSpan.MaxValue),
            "DeadLetterRetentionPeriod",
            "Unbounded"
        );
        yield return Case(new(LocalWorkerCount: 1_000_000), "LocalWorkerCount", "Unbounded");
    }

    [TestCaseSource(nameof(UnusableDurationPatches))]
    public async Task UpdateSchedulerConfig_ValueTheSchedulerCannotRunWith_IsRefused(
        UpdateSchedulerConfigInput input,
        string field
    )
    {
        var result = await _operations.UpdateSchedulerConfigAsync(input, CancellationToken.None);

        result.Success.Should().BeFalse("a value the pollers cannot run with is never stored");
        result.Message.Should().Contain(field);
        DataContext.Reset();
        DataContext.SchedulerConfigs.Should().BeEmpty();
    }

    [Test]
    public async Task BootstrapHostedService_UnusablePersistedValues_AreSkippedAndLogged()
    {
        // A row written before the service validated, or by hand: values the scheduler cannot
        // run with sit beside ones it can.
        var row = new Trax.Effect.Models.SchedulerConfig.SchedulerConfig
        {
            ManifestManagerPollingInterval = TimeSpan.FromTicks(5_000),
            JobDispatcherPollingInterval = TimeSpan.FromSeconds(3),
            MaxActiveJobs = 0,
            DefaultMaxRetries = 6,
            StalePendingTimeout = TimeSpan.FromDays(100_000),
            LocalWorkerCount = 1_000_000,
            UpdatedAt = DateTime.UtcNow,
        };
        DataContext.SchedulerConfigs.Add(row);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var logger = new WarningCapturingLogger();
        var hosted = new SchedulerConfigBootstrapHostedService(Scope.ServiceProvider, logger);

        await hosted.StartAsync(CancellationToken.None);

        _cfg.ManifestManagerPollingInterval.Should()
            .Be(TimeSpan.FromSeconds(5), "an unusable persisted interval is not applied");
        _cfg.MaxActiveJobs.Should().Be(10);
        _cfg.StalePendingTimeout.Should().Be(TimeSpan.FromMinutes(20));
        _cfg.JobDispatcherPollingInterval.Should()
            .Be(TimeSpan.FromSeconds(3), "the usable values in the same row still apply");
        _cfg.DefaultMaxRetries.Should().Be(6);

        var startPoller = () =>
        {
            using var timer = new PeriodicTimer(_cfg.ManifestManagerPollingInterval);
        };
        startPoller
            .Should()
            .NotThrow("the ManifestManager poller builds its timer from this value at boot");

        logger
            .Warnings.Should()
            .Contain(w => w.Contains("ManifestManagerPollingInterval"))
            .And.Contain(w => w.Contains("MaxActiveJobs"))
            .And.Contain(w => w.Contains("StalePendingTimeout"));
    }

    [Test]
    public void A_sub_millisecond_interval_is_what_faults_the_poller()
    {
        // The premise of the two tests above: PeriodicTimer refuses this outright.
        var act = () => new PeriodicTimer(TimeSpan.FromTicks(5_000));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class WarningCapturingLogger
        : Microsoft.Extensions.Logging.ILogger<SchedulerConfigBootstrapHostedService>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                lock (Warnings)
                    Warnings.Add(formatter(state, exception));
        }
    }
}
