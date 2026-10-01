using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// The operations surface can live on an API-only host that talks to a remote scheduler
/// (add-trax-graphql: "AddTraxJobRunner() plus services.AddScoped&lt;IOperationsService,
/// OperationsService&gt;()"), or on any one of several scheduler hosts. A settings change made
/// there changes only the setting it names, and reaches the running schedulers without a restart.
///
/// <para>Enforces <c>docs/adr/0010-a-settings-save-writes-only-what-it-names-and-every-scheduler-applies-it.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0010-a-settings-save-writes-only-what-it-names-and-every-scheduler-applies-it.md"
)]
[TestFixture]
[NonParallelizable]
public class SchedulerConfigFromApiHostTests
{
    private const string AdrPath =
        "docs/adr/0010-a-settings-save-writes-only-what-it-names-and-every-scheduler-applies-it.md";

    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(10);

    private static ServiceProvider ApiHost() =>
        new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(TestPostgres.ConnectionString))
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
            )
            .AddTraxJobRunner()
            .AddScoped<IOperationsService, OperationsService>()
            .BuildServiceProvider();

    private static Task<SchedulerE2EFixture> Scheduler() =>
        SchedulerE2EFixture.CreateAsync(s =>
            s.StalePendingTimeout(TimeSpan.FromMinutes(5))
                .JobDispatcherPollingInterval(TimeSpan.FromSeconds(10))
        );

    private static SchedulerConfigBootstrapHostedService SettingsService(SchedulerE2EFixture fx) =>
        fx
            .Services.GetServices<IHostedService>()
            .OfType<SchedulerConfigBootstrapHostedService>()
            .Single();

    private static async Task<OperationResult> Save(
        IServiceProvider services,
        UpdateSchedulerConfigInput input
    )
    {
        using var scope = services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IOperationsService>()
            .UpdateSchedulerConfigAsync(input, CancellationToken.None);
    }

    private static async Task DeleteConfigRow()
    {
        await using var host = ApiHost();
        var factory = host.GetRequiredService<IDataContextProviderFactory>();
        using var db = await factory.CreateDbContextAsync(CancellationToken.None);
        await db.SchedulerConfigs.ExecuteDeleteAsync();
    }

    private static async Task WaitFor(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + SyncTimeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50); // determinism: polls the condition, bounded by SyncTimeout
        condition().Should().BeTrue($"{because}. See {AdrPath}.");
    }

    [SetUp]
    [TearDown]
    public Task CleanRow() => DeleteConfigRow();

    [Test]
    public async Task Changing_one_setting_from_an_api_host_changes_only_that_setting()
    {
        // The scheduler host made an earlier change, so the settings row exists.
        await using (var first = await Scheduler())
            (await Save(first.Services, new UpdateSchedulerConfigInput(DefaultMaxRetries: 4)))
                .Success.Should()
                .BeTrue();

        await using (var apiHost = ApiHost())
            (await Save(apiHost, new UpdateSchedulerConfigInput(MaxActiveJobs: 20)))
                .Success.Should()
                .BeTrue();

        // The scheduler process, configured in code, restarts.
        await using var scheduler = await Scheduler();
        var settings = SettingsService(scheduler);
        await settings.StartAsync(CancellationToken.None);
        await settings.StopAsync(CancellationToken.None);

        scheduler
            .Configuration.MaxActiveJobs.Should()
            .Be(20, "that is the change the operator made");
        scheduler.Configuration.DefaultMaxRetries.Should().Be(4, "the earlier change stands");
        scheduler
            .Configuration.StalePendingTimeout.Should()
            .Be(
                TimeSpan.FromMinutes(5),
                $"nobody changed the stale pending timeout, and a save writes only what it names ({AdrPath})"
            );
        scheduler
            .Configuration.JobDispatcherPollingInterval.Should()
            .Be(TimeSpan.FromSeconds(10), "nobody changed the dispatcher polling interval");
    }

    [Test]
    public async Task An_api_host_saves_first_and_the_scheduler_keeps_its_other_configured_settings()
    {
        await using (var apiHost = ApiHost())
            (await Save(apiHost, new UpdateSchedulerConfigInput(MaxActiveJobs: 20)))
                .Success.Should()
                .BeTrue("the row stores only what a save names, so any host may create it");

        await using var scheduler = await Scheduler();
        var settings = SettingsService(scheduler);
        await settings.StartAsync(CancellationToken.None);
        await settings.StopAsync(CancellationToken.None);

        scheduler
            .Configuration.MaxActiveJobs.Should()
            .Be(20, "that is the change the operator made");
        scheduler
            .Configuration.StalePendingTimeout.Should()
            .Be(
                TimeSpan.FromMinutes(5),
                $"the API host's default was never saved, so the code value applies ({AdrPath})"
            );
        scheduler
            .Configuration.JobDispatcherPollingInterval.Should()
            .Be(TimeSpan.FromSeconds(10), "nobody changed the dispatcher polling interval");
    }

    [Test]
    public async Task A_later_change_in_code_applies_to_every_setting_no_save_named()
    {
        await using (var apiHost = ApiHost())
            (await Save(apiHost, new UpdateSchedulerConfigInput(MaxActiveJobs: 20)))
                .Success.Should()
                .BeTrue();

        // A later deploy changes the stale pending timeout in code.
        await using var scheduler = await SchedulerE2EFixture.CreateAsync(s =>
            s.StalePendingTimeout(TimeSpan.FromMinutes(7))
        );
        var settings = SettingsService(scheduler);
        await settings.StartAsync(CancellationToken.None);
        await settings.StopAsync(CancellationToken.None);

        scheduler.Configuration.MaxActiveJobs.Should().Be(20);
        scheduler
            .Configuration.StalePendingTimeout.Should()
            .Be(TimeSpan.FromMinutes(7), "no save named it, so the code value stands");
    }

    [Test]
    public async Task A_change_saved_on_one_host_reaches_a_running_scheduler_on_another()
    {
        await using var running = await Scheduler();
        running.Configuration.SettingsRefreshInterval = TimeSpan.FromMilliseconds(100);
        var settings = SettingsService(running);
        await settings.StartAsync(CancellationToken.None);
        try
        {
            await using (var other = await Scheduler())
                (await Save(other.Services, new UpdateSchedulerConfigInput(MaxActiveJobs: 7)))
                    .Success.Should()
                    .BeTrue();

            await WaitFor(
                () => running.Configuration.MaxActiveJobs == 7,
                "the running scheduler applies a change another host saved"
            );

            await using (var apiHost = ApiHost())
                (await Save(apiHost, new UpdateSchedulerConfigInput(JobDispatcherEnabled: false)))
                    .Success.Should()
                    .BeTrue();

            await WaitFor(
                () => !running.Configuration.JobDispatcherEnabled,
                "a change saved on an API-only host reaches it too"
            );
            running.Configuration.MaxActiveJobs.Should().Be(7);
        }
        finally
        {
            await settings.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Removing_the_stored_row_returns_a_running_scheduler_to_its_configured_settings()
    {
        await using var running = await Scheduler();
        running.Configuration.SettingsRefreshInterval = TimeSpan.FromMilliseconds(100);
        var settings = SettingsService(running);
        await settings.StartAsync(CancellationToken.None);
        try
        {
            (await Save(running.Services, new UpdateSchedulerConfigInput(MaxActiveJobs: 7)))
                .Success.Should()
                .BeTrue();
            running.Configuration.MaxActiveJobs.Should().Be(7);

            // A refresh has to have seen the row: removing one this host never read has nothing
            // to undo.
            var seen = settings.CompletedRefreshes;
            await WaitFor(() => settings.CompletedRefreshes > seen, "a refresh reads the row");

            await DeleteConfigRow();

            await WaitFor(
                () =>
                    running.Configuration.MaxActiveJobs
                    == new SchedulerConfiguration().MaxActiveJobs,
                "with no stored settings the configured ones apply"
            );
            running.Configuration.StalePendingTimeout.Should().Be(TimeSpan.FromMinutes(5));
        }
        finally
        {
            await settings.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task With_no_stored_row_a_runtime_change_survives_the_refresh()
    {
        await using var running = await Scheduler();
        running.Configuration.SettingsRefreshInterval = TimeSpan.FromMilliseconds(100);
        var settings = SettingsService(running);
        await settings.StartAsync(CancellationToken.None);
        try
        {
            running.Configuration.DefaultRetryDelay = TimeSpan.FromSeconds(2);
            var after = settings.CompletedRefreshes;

            await WaitFor(
                () => settings.CompletedRefreshes >= after + 2,
                "two refreshes finish after the change"
            );

            running
                .Configuration.DefaultRetryDelay.Should()
                .Be(
                    TimeSpan.FromSeconds(2),
                    "with no row ever applied, a refresh has nothing to undo"
                );
        }
        finally
        {
            await settings.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task A_failure_count_window_saved_on_an_api_host_reaches_a_running_scheduler()
    {
        await using var running = await Scheduler();
        running.Configuration.SettingsRefreshInterval = TimeSpan.FromMilliseconds(100);
        var settings = SettingsService(running);
        await settings.StartAsync(CancellationToken.None);
        try
        {
            await using (var apiHost = ApiHost())
                (
                    await Save(
                        apiHost,
                        new UpdateSchedulerConfigInput
                        {
                            FailureCountWindow = TimeSpan.FromHours(2),
                        }
                    )
                )
                    .Success.Should()
                    .BeTrue();

            await WaitFor(
                () => running.Configuration.FailureCountWindow == TimeSpan.FromHours(2),
                "the failure count window is stored in the row's overrides like any other setting"
            );
            running
                .Configuration.StalePendingTimeout.Should()
                .Be(TimeSpan.FromMinutes(5), "the save named only the failure count window");
        }
        finally
        {
            await settings.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task A_row_saved_before_overrides_existed_keeps_every_stored_value_through_a_later_save()
    {
        // Written by a version that stored every column and had no overrides.
        await using (var apiHost = ApiHost())
        {
            var factory = apiHost.GetRequiredService<IDataContextProviderFactory>();
            using var db = await factory.CreateDbContextAsync(CancellationToken.None);
            db.SchedulerConfigs.Add(
                new Trax.Effect.Models.SchedulerConfig.SchedulerConfig
                {
                    StalePendingTimeout = TimeSpan.FromMinutes(3),
                    UpdatedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChanges(CancellationToken.None);

            (await Save(apiHost, new UpdateSchedulerConfigInput(MaxActiveJobs: 20)))
                .Success.Should()
                .BeTrue();
        }

        await using var scheduler = await Scheduler();
        var settings = SettingsService(scheduler);
        await settings.StartAsync(CancellationToken.None);
        await settings.StopAsync(CancellationToken.None);

        scheduler.Configuration.MaxActiveJobs.Should().Be(20);
        scheduler
            .Configuration.StalePendingTimeout.Should()
            .Be(TimeSpan.FromMinutes(3), "every column of an older row is an operator's value");
    }

    [Test]
    public async Task A_refresh_that_finds_the_stored_row_unchanged_leaves_the_settings_alone()
    {
        await using var running = await Scheduler();
        running.Configuration.SettingsRefreshInterval = TimeSpan.FromMilliseconds(100);
        var settings = SettingsService(running);
        await settings.StartAsync(CancellationToken.None);
        try
        {
            (await Save(running.Services, new UpdateSchedulerConfigInput(MaxActiveJobs: 7)))
                .Success.Should()
                .BeTrue();
            var seen = settings.CompletedRefreshes;
            await WaitFor(() => settings.CompletedRefreshes > seen, "a refresh reads the row");

            // Changed in this process only; the stored row still says 7.
            running.Configuration.MaxActiveJobs = 9;
            var after = settings.CompletedRefreshes;
            await WaitFor(
                () => settings.CompletedRefreshes >= after + 2,
                "two refreshes finish after the change"
            );

            running
                .Configuration.MaxActiveJobs.Should()
                .Be(9, "a refresh applies the row only when it changed since it was last applied");
        }
        finally
        {
            await settings.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task With_no_refresh_interval_the_stored_row_applies_once_at_startup_and_is_not_read_again()
    {
        await using var running = await Scheduler();
        (await Save(running.Services, new UpdateSchedulerConfigInput(MaxActiveJobs: 7)))
            .Success.Should()
            .BeTrue();
        running.Configuration.MaxActiveJobs = 3;
        running.Configuration.SettingsRefreshInterval = TimeSpan.Zero;

        var settings = SettingsService(running);
        await settings.StartAsync(CancellationToken.None);
        try
        {
            running.Configuration.MaxActiveJobs.Should().Be(7, "startup applies the stored row");

            running.Configuration.MaxActiveJobs = 9;

            // negative-wait: no refresh may run, which only elapsed time can show.
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            settings.CompletedRefreshes.Should().Be(0);
            running
                .Configuration.MaxActiveJobs.Should()
                .Be(9, "with no refresh the row is not applied again");
        }
        finally
        {
            await settings.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task An_interval_set_to_zero_while_refreshing_slows_the_refresh_rather_than_spinning()
    {
        await using var running = await Scheduler();
        running.Configuration.SettingsRefreshInterval = TimeSpan.FromMilliseconds(100);
        var settings = SettingsService(running);
        await settings.StartAsync(CancellationToken.None);
        try
        {
            await WaitFor(() => settings.CompletedRefreshes > 0, "the refresh is running");

            running.Configuration.SettingsRefreshInterval = TimeSpan.Zero;
            var after = settings.CompletedRefreshes;

            // negative-wait: counts the refreshes in a fixed window, which only elapsed time can
            // show. A zero interval taken literally would refresh continuously.
            await Task.Delay(TimeSpan.FromSeconds(1));

            settings
                .CompletedRefreshes.Should()
                .BeLessThanOrEqualTo(
                    after + 1,
                    "a non-positive interval falls back to waiting 5 seconds between refreshes"
                );
        }
        finally
        {
            await settings.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Stopping_while_a_refresh_waits_on_the_database_ends_the_refresh_cleanly()
    {
        await using var running = await Scheduler();
        running.Configuration.SettingsRefreshInterval = TimeSpan.FromMilliseconds(100);
        var settings = SettingsService(running);
        await settings.StartAsync(CancellationToken.None);

        // Another session holds the settings table, so the next refresh's read waits on it.
        await using var other = new NpgsqlConnection(TestPostgres.ConnectionString);
        await other.OpenAsync();
        await using var transaction = await other.BeginTransactionAsync();
        await using (
            var command = new NpgsqlCommand(
                "LOCK TABLE trax.scheduler_config IN ACCESS EXCLUSIVE MODE",
                other
            )
        )
            await command.ExecuteNonQueryAsync();

        // Probed from its own connection: inside the lock's transaction the activity view
        // would keep showing the snapshot it first read.
        await using var probe = new NpgsqlConnection(TestPostgres.ConnectionString);
        await probe.OpenAsync();
        await WaitFor(
            () => SessionsWaitingOnALock(probe) > 0,
            "the refresh's read waits on the locked table"
        );

        var stop = () => settings.StopAsync(CancellationToken.None);

        await stop.Should()
            .NotThrowAsync("a refresh cancelled by the stop is the stop working, not a failure");
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task A_saved_purge_does_not_override_a_purge_turned_off_in_code()
    {
        await using (var apiHost = ApiHost())
        {
            (
                await Save(
                    apiHost,
                    new UpdateSchedulerConfigInput(DeadLetterRetentionPeriod: TimeSpan.FromDays(40))
                )
            )
                .Success.Should()
                .BeTrue();
            // Stored as named even though true is the default: the API host cannot know the code value.
            (await Save(apiHost, new UpdateSchedulerConfigInput(AutoPurgeDeadLetters: true)))
                .Success.Should()
                .BeTrue();
        }

        // A later deploy keeps resolved dead letters for an audit.
        await using var scheduler = await SchedulerE2EFixture.CreateAsync(s =>
            s.AutoPurgeDeadLetters(false)
        );
        var settings = SettingsService(scheduler);
        await settings.StartAsync(CancellationToken.None);
        await settings.StopAsync(CancellationToken.None);

        scheduler
            .Configuration.AutoPurgeDeadLetters.Should()
            .BeFalse("a purge runs only when both the code and the saved setting allow it");
        scheduler
            .Configuration.DeadLetterRetentionPeriod.Should()
            .Be(TimeSpan.FromDays(40), "the code states no retention, so the saved one applies");
    }

    [Test]
    public async Task The_longer_of_a_saved_and_a_coded_dead_letter_retention_applies()
    {
        await using (var apiHost = ApiHost())
            (
                await Save(
                    apiHost,
                    new UpdateSchedulerConfigInput(DeadLetterRetentionPeriod: TimeSpan.FromDays(10))
                )
            )
                .Success.Should()
                .BeTrue();

        await using (
            var longerInCode = await SchedulerE2EFixture.CreateAsync(s =>
                s.DeadLetterRetentionPeriod(TimeSpan.FromDays(60))
            )
        )
        {
            var settings = SettingsService(longerInCode);
            await settings.StartAsync(CancellationToken.None);
            await settings.StopAsync(CancellationToken.None);
            longerInCode.Configuration.DeadLetterRetentionPeriod.Should().Be(TimeSpan.FromDays(60));
        }

        await using var shorterInCode = await SchedulerE2EFixture.CreateAsync(s =>
            s.DeadLetterRetentionPeriod(TimeSpan.FromDays(5))
        );
        var shorter = SettingsService(shorterInCode);
        await shorter.StartAsync(CancellationToken.None);
        await shorter.StopAsync(CancellationToken.None);
        shorterInCode.Configuration.DeadLetterRetentionPeriod.Should().Be(TimeSpan.FromDays(10));
    }

    [Test]
    public async Task A_purge_turned_off_from_the_dashboard_applies_over_the_code_default()
    {
        await using (var apiHost = ApiHost())
            (await Save(apiHost, new UpdateSchedulerConfigInput(AutoPurgeDeadLetters: false)))
                .Success.Should()
                .BeTrue();

        await using var scheduler = await SchedulerE2EFixture.CreateAsync(s =>
            s.AutoPurgeDeadLetters()
        );
        var settings = SettingsService(scheduler);
        await settings.StartAsync(CancellationToken.None);
        await settings.StopAsync(CancellationToken.None);

        scheduler.Configuration.AutoPurgeDeadLetters.Should().BeFalse();
    }

    [Test]
    public async Task Two_hosts_making_the_first_save_at_once_both_succeed()
    {
        await using var apiHost = ApiHost();

        // Another host's first save has inserted the row but not committed it yet.
        await using var other = new NpgsqlConnection(TestPostgres.ConnectionString);
        await other.OpenAsync();
        await using var transaction = await other.BeginTransactionAsync();
        await using (
            var insert = new NpgsqlCommand(
                "INSERT INTO trax.scheduler_config (id, overrides, max_active_jobs, updated_at) "
                    + "VALUES (1, '{\"MaxActiveJobs\": 7}', 7, now())",
                other,
                transaction
            )
        )
            await insert.ExecuteNonQueryAsync();

        // This save reads no row, so it inserts one, and its insert waits on the other's.
        var save = Save(apiHost, new UpdateSchedulerConfigInput(DefaultMaxRetries: 6));
        await using var probe = new NpgsqlConnection(TestPostgres.ConnectionString);
        await probe.OpenAsync();
        await WaitFor(
            () => SessionsWaitingOnALock(probe) > 0,
            "the second first-save's insert waits on the first one's"
        );
        await transaction.CommitAsync();

        var result = await save;

        result.Success.Should().BeTrue("the row now exists, so the save applies to it");
        var factory = apiHost.GetRequiredService<IDataContextProviderFactory>();
        using var db = await factory.CreateDbContextAsync(CancellationToken.None);
        var row = await db.SchedulerConfigs.AsNoTracking().SingleAsync();
        row.TryGetOverride<int?>("MaxActiveJobs", out var maxActiveJobs).Should().BeTrue();
        maxActiveJobs.Should().Be(7, "the other host's save stands");
        row.TryGetOverride<int>("DefaultMaxRetries", out var retries).Should().BeTrue();
        retries.Should().Be(6);
    }

    [Test]
    public async Task A_scheduler_whose_first_settings_read_fails_still_applies_a_later_save()
    {
        await using var apiHost = ApiHost();
        var configuration = new SchedulerConfiguration
        {
            SettingsRefreshInterval = TimeSpan.FromMilliseconds(100),
        };
        var factory = new FailsOnceDataContextFactory(
            apiHost.GetRequiredService<IDataContextProviderFactory>()
        );
        await using var host = new ServiceCollection()
            .AddSingleton(configuration)
            .AddSingleton<IDataContextProviderFactory>(factory)
            .BuildServiceProvider();
        var settings = new SchedulerConfigBootstrapHostedService(
            host,
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<SchedulerConfigBootstrapHostedService>
                .Instance
        );

        // The first read fails (the database is briefly unreachable at startup).
        await settings.StartAsync(CancellationToken.None);
        try
        {
            factory.Failed.Should().BeTrue();

            (await Save(apiHost, new UpdateSchedulerConfigInput(JobDispatcherEnabled: false)))
                .Success.Should()
                .BeTrue();

            await WaitFor(
                () => !configuration.JobDispatcherEnabled,
                "the refresh runs even when the startup read failed, so a later save arrives"
            );
        }
        finally
        {
            await settings.StopAsync(CancellationToken.None);
        }
    }

    private sealed class FailsOnceDataContextFactory(IDataContextProviderFactory inner)
        : IDataContextProviderFactory
    {
        private int _calls;

        public bool Failed => Volatile.Read(ref _calls) > 0;

        public Task<Trax.Effect.Data.Services.DataContext.IDataContext> CreateDbContextAsync(
            CancellationToken cancellationToken
        ) =>
            Interlocked.Increment(ref _calls) == 1
                ? throw new InvalidOperationException("the database is not reachable yet")
                : inner.CreateDbContextAsync(cancellationToken);

        public Trax.Effect.Services.EffectProvider.IEffectProvider Create() => inner.Create();
    }

    private static long SessionsWaitingOnALock(NpgsqlConnection connection)
    {
        using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity "
                + "WHERE datname = current_database() AND wait_event_type = 'Lock'",
            connection
        );
        return (long)command.ExecuteScalar()!;
    }
}
