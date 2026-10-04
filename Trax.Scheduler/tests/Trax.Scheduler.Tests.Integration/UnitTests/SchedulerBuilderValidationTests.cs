using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Validates that the scheduler builder catches misconfiguration at build time
/// with helpful error messages, rather than failing at runtime with cryptic DI errors.
/// </summary>
[TestFixture]
public class SchedulerBuilderValidationTests
{
    private static readonly string ConnectionString = TestPostgres.ConnectionStringFor(
        "trax_scheduler_builder_validation"
    );

    #region AddScheduler requires a data provider

    [Test]
    public void AddScheduler_WithoutDataProvider_ThrowsWithHelpfulMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects().AddMediator(typeof(AssemblyMarker).Assembly).AddScheduler()
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddScheduler()*")
            .WithMessage("*UsePostgres*")
            .WithMessage("*UseInMemory*");
    }

    [Test]
    public void AddScheduler_WithoutDataProvider_ErrorContainsCodeExample()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects().AddMediator(typeof(AssemblyMarker).Assembly).AddScheduler()
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*services.AddTrax*")
            .WithMessage("*.AddEffects*")
            .WithMessage("*.UsePostgres(connectionString)*")
            .WithMessage("*.AddScheduler*");
    }

    [Test]
    public void AddScheduler_WithInMemory_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler()
            );

        act.Should().NotThrow();
    }

    [Test]
    public void AddScheduler_WithPostgres_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler()
            );

        act.Should().NotThrow();
    }

    #endregion

    #region Other submitters require data provider but not Postgres

    [Test]
    public void UseRemoteWorkers_WithInMemory_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.UseRemoteWorkers(
                            o => o.BaseUrl = "http://localhost:5000",
                            routing => routing.ForTrain<ITestTrain>()
                        )
                    )
            );

        act.Should().NotThrow();
    }

    [Test]
    public void OverrideSubmitter_WithInMemory_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.OverrideSubmitter(s =>
                            s.AddScoped<IJobSubmitter, InMemoryJobSubmitter>()
                        )
                    )
            );

        act.Should().NotThrow();
    }

    #endregion

    #region Duplicate train routing validation

    [Test]
    public void AddRoutedSubmitter_DuplicateTrainWithoutDescriptions_NamesTheSubmitterTypes()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        RoutedSubmitterRegistration Custom() =>
            new(
                new SubmitterRouting().ForTrain<ITestTrain>(),
                typeof(InMemoryJobSubmitter),
                s => s.AddScoped<InMemoryJobSubmitter>()
            );

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                    {
                        scheduler.AddRoutedSubmitter(Custom());
                        scheduler.AddRoutedSubmitter(Custom());
                        return scheduler;
                    })
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*routed to multiple submitters: 'InMemoryJobSubmitter' and 'InMemoryJobSubmitter'*",
                "a registration with no description is named by its submitter type"
            );
    }

    [Test]
    public void UseRemoteWorkers_DuplicateTrainAcrossSubmitters_ThrowsAtBuildTime()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler
                            .UseRemoteWorkers(
                                o => o.BaseUrl = "http://endpoint-a",
                                routing => routing.ForTrain<ITestTrain>()
                            )
                            .UseRemoteWorkers(
                                o => o.BaseUrl = "http://endpoint-b",
                                routing => routing.ForTrain<ITestTrain>()
                            )
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*routed to multiple submitters*")
            .WithMessage("*ForTrain*");
    }

    [Test]
    public void UseRemoteWorkers_DifferentTrainsAcrossSubmitters_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler
                            .UseRemoteWorkers(
                                o => o.BaseUrl = "http://endpoint-a",
                                routing => routing.ForTrain<ITestTrain>()
                            )
                            .UseRemoteWorkers(
                                o => o.BaseUrl = "http://endpoint-b",
                                routing => routing.ForTrain<ITestTrainB>()
                            )
                    )
            );

        act.Should().NotThrow();
    }

    #endregion

    #region ConfigureLocalWorkers

    [Test]
    public void ConfigureLocalWorkers_WithPostgres_RegistersCustomOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler =>
                    scheduler.ConfigureLocalWorkers(opts => opts.WorkerCount = 8)
                )
        );

        var options = services.BuildServiceProvider().GetService<LocalWorkerOptions>();
        options.Should().NotBeNull();
        options!.WorkerCount.Should().Be(8);
    }

    #endregion

    #region Range checks

    // Each value a builder method or options property accepts is checked when the scheduler is
    // built, against the ranges the operations service holds a runtime change to. Before, each
    // of these built, and the host found out at runtime: a zero polling interval spun the
    // dispatcher and could not be stopped.

    private static Action Building(Action<SchedulerConfigurationBuilder> configure) =>
        () =>
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                    {
                        configure(scheduler);
                        return scheduler;
                    })
            );
        };

    private static readonly TestCaseData[] PollingIntervalsOutOfRange =
    [
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(b => b.PollingInterval(TimeSpan.Zero)),
            nameof(SchedulerConfigurationBuilder.PollingInterval)
        ).SetName("PollingInterval zero"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.PollingInterval(TimeSpan.FromSeconds(-1))
            ),
            nameof(SchedulerConfigurationBuilder.PollingInterval)
        ).SetName("PollingInterval negative"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.ManifestManagerPollingInterval(TimeSpan.Zero)
            ),
            nameof(SchedulerConfigurationBuilder.ManifestManagerPollingInterval)
        ).SetName("ManifestManagerPollingInterval zero"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.JobDispatcherPollingInterval(TimeSpan.FromMilliseconds(500))
            ),
            nameof(SchedulerConfigurationBuilder.JobDispatcherPollingInterval)
        ).SetName("JobDispatcherPollingInterval under a second"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.JobDispatcherPollingInterval(TimeSpan.FromDays(31))
            ),
            nameof(SchedulerConfigurationBuilder.JobDispatcherPollingInterval)
        ).SetName("JobDispatcherPollingInterval over 30 days"),
    ];

    [TestCaseSource(nameof(PollingIntervalsOutOfRange))]
    public void A_polling_interval_out_of_range_is_refused_at_build(
        Action<SchedulerConfigurationBuilder> configure,
        string method
    )
    {
        Building(configure)
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{method} must be between*");
    }

    [Test]
    public void A_negative_DefaultMaxRetries_is_refused_at_build()
    {
        Building(b => b.DefaultMaxRetries(-1))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*DefaultMaxRetries must not be negative*");
    }

    [Test]
    public void A_zero_DefaultMaxRetries_builds()
    {
        Building(b => b.DefaultMaxRetries(0)).Should().NotThrow();
    }

    private static readonly TestCaseData[] RetentionsOutOfRange =
    [
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.DeadLetterRetentionPeriod(TimeSpan.FromSeconds(-1))
            ),
            "DeadLetterRetentionPeriod must be between"
        ).SetName("DeadLetterRetentionPeriod negative"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.DeadLetterRetentionPeriod(TimeSpan.MaxValue)
            ),
            "DeadLetterRetentionPeriod must be between"
        ).SetName("DeadLetterRetentionPeriod past ten years"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.AddMetadataCleanup(c => c.RetentionPeriod = TimeSpan.Zero)
            ),
            "AddMetadataCleanup: RetentionPeriod must be between"
        ).SetName("Metadata cleanup RetentionPeriod zero"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.AddMetadataCleanup(c => c.RetentionPeriod = TimeSpan.FromSeconds(-5))
            ),
            "AddMetadataCleanup: RetentionPeriod must be between"
        ).SetName("Metadata cleanup RetentionPeriod negative"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.AddMetadataCleanup(c => c.CleanupInterval = TimeSpan.Zero)
            ),
            "AddMetadataCleanup: CleanupInterval must be between"
        ).SetName("Metadata cleanup CleanupInterval zero"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.AddMetadataCleanup(c => c.CleanupInterval = TimeSpan.FromDays(31))
            ),
            "AddMetadataCleanup: CleanupInterval must be between"
        ).SetName("Metadata cleanup CleanupInterval over 30 days"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.AddMetadataCleanup(c => c.DeleteBatchSize = 0)
            ),
            "AddMetadataCleanup: DeleteBatchSize must be between 1 and 10000"
        ).SetName("Metadata cleanup DeleteBatchSize zero"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.AddMetadataCleanup(c => c.DeleteBatchSize = 10_001)
            ),
            "AddMetadataCleanup: DeleteBatchSize must be between 1 and 10000"
        ).SetName("Metadata cleanup DeleteBatchSize over the cap"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b =>
                    b.AddMetadataCleanup(c => c.AddTrainType("Some.INoisyTrain", TimeSpan.MaxValue))
            ),
            "AddMetadataCleanup: the retention of 'Some.INoisyTrain' must be between"
        ).SetName("Per-train retention of TimeSpan.MaxValue"),
    ];

    [TestCaseSource(nameof(RetentionsOutOfRange))]
    public void A_retention_or_cleanup_setting_out_of_range_is_refused_at_build(
        Action<SchedulerConfigurationBuilder> configure,
        string problem
    )
    {
        Building(configure).Should().Throw<InvalidOperationException>().WithMessage($"*{problem}*");
    }

    [Test]
    public void Retention_and_cleanup_settings_at_their_limits_build()
    {
        Building(b =>
                b.DeadLetterRetentionPeriod(TimeSpan.Zero)
                    .AddMetadataCleanup(c =>
                    {
                        c.RetentionPeriod = TimeSpan.FromSeconds(1);
                        c.CleanupInterval = TimeSpan.FromDays(30);
                        c.DeleteBatchSize = null;
                        c.AddTrainType("Some.INoisyTrain", TimeSpan.FromDays(3650));
                    })
            )
            .Should()
            .NotThrow();
    }

    private static readonly TestCaseData[] LocalWorkerOptionsOutOfRange =
    [
        new TestCaseData(
            (Action<LocalWorkerOptions>)(o => o.VisibilityTimeout = TimeSpan.FromMilliseconds(1)),
            "VisibilityTimeout must be between"
        ).SetName("VisibilityTimeout of a millisecond"),
        new TestCaseData(
            (Action<LocalWorkerOptions>)(o => o.VisibilityTimeout = TimeSpan.FromDays(3651)),
            "VisibilityTimeout must be between"
        ).SetName("VisibilityTimeout past ten years"),
        new TestCaseData(
            (Action<LocalWorkerOptions>)(o => o.PollingInterval = TimeSpan.Zero),
            "PollingInterval must be greater than zero"
        ).SetName("Worker PollingInterval zero"),
        new TestCaseData(
            (Action<LocalWorkerOptions>)(o => o.WorkerCount = 0),
            "WorkerCount must be between 1 and"
        ).SetName("WorkerCount zero"),
        new TestCaseData(
            (Action<LocalWorkerOptions>)(o => o.BatchSize = 0),
            "BatchSize must be at least 1"
        ).SetName("BatchSize zero"),
        new TestCaseData(
            (Action<LocalWorkerOptions>)(o => o.ShutdownTimeout = TimeSpan.FromSeconds(-1)),
            "ShutdownTimeout must be between"
        ).SetName("ShutdownTimeout negative"),
    ];

    [TestCaseSource(nameof(LocalWorkerOptionsOutOfRange))]
    public void A_local_worker_option_out_of_range_is_refused_at_build(
        Action<LocalWorkerOptions> configure,
        string problem
    )
    {
        Building(b => b.ConfigureLocalWorkers(configure))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*ConfigureLocalWorkers: {problem}*");
    }

    [TestCaseSource(nameof(LocalWorkerOptionsOutOfRange))]
    public void A_standalone_worker_option_out_of_range_is_refused(
        Action<LocalWorkerOptions> configure,
        string problem
    )
    {
        var act = () => new ServiceCollection().AddLogging().AddTraxWorker(configure);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*AddTraxWorker: {problem}*");
    }

    [Test]
    public void Local_worker_options_at_their_limits_build()
    {
        Building(b =>
                b.ConfigureLocalWorkers(o =>
                {
                    o.WorkerCount = 256;
                    o.PollingInterval = TimeSpan.FromMilliseconds(100);
                    o.VisibilityTimeout = TimeSpan.FromSeconds(1);
                    o.BatchSize = 1;
                    o.ShutdownTimeout = TimeSpan.Zero;
                })
            )
            .Should()
            .NotThrow();
    }

    private static readonly TestCaseData[] OtherSettingsOutOfRange =
    [
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(b => b.MaxActiveJobs(0)),
            nameof(SchedulerConfigurationBuilder.MaxActiveJobs)
        ).SetName("MaxActiveJobs zero"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.DefaultRetryDelay(TimeSpan.FromSeconds(-1))
            ),
            nameof(SchedulerConfigurationBuilder.DefaultRetryDelay)
        ).SetName("DefaultRetryDelay negative"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(b => b.RetryBackoffMultiplier(0.5)),
            nameof(SchedulerConfigurationBuilder.RetryBackoffMultiplier)
        ).SetName("RetryBackoffMultiplier below 1"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(b => b.RetryBackoffMultiplier(double.NaN)),
            nameof(SchedulerConfigurationBuilder.RetryBackoffMultiplier)
        ).SetName("RetryBackoffMultiplier NaN"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(b => b.MaxRetryDelay(TimeSpan.FromSeconds(-1))),
            nameof(SchedulerConfigurationBuilder.MaxRetryDelay)
        ).SetName("MaxRetryDelay negative"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(b => b.DefaultJobTimeout(TimeSpan.Zero)),
            nameof(SchedulerConfigurationBuilder.DefaultJobTimeout)
        ).SetName("DefaultJobTimeout zero"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(b => b.StalePendingTimeout(TimeSpan.Zero)),
            nameof(SchedulerConfigurationBuilder.StalePendingTimeout)
        ).SetName("StalePendingTimeout zero"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.StaleInProgressTimeout(TimeSpan.FromMinutes(-1))
            ),
            nameof(SchedulerConfigurationBuilder.StaleInProgressTimeout)
        ).SetName("StaleInProgressTimeout negative"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(b => b.StaleStagedEntryTimeout(TimeSpan.Zero)),
            nameof(SchedulerConfigurationBuilder.StaleStagedEntryTimeout)
        ).SetName("StaleStagedEntryTimeout zero"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.DefaultMisfireThreshold(TimeSpan.FromSeconds(-1))
            ),
            nameof(SchedulerConfigurationBuilder.DefaultMisfireThreshold)
        ).SetName("DefaultMisfireThreshold negative"),
        new TestCaseData(
            (Action<SchedulerConfigurationBuilder>)(
                b => b.SchedulerLivenessThreshold(TimeSpan.Zero)
            ),
            nameof(SchedulerConfigurationBuilder.SchedulerLivenessThreshold)
        ).SetName("SchedulerLivenessThreshold zero"),
    ];

    [TestCaseSource(nameof(OtherSettingsOutOfRange))]
    public void A_retry_timeout_or_limit_out_of_range_is_refused_at_build(
        Action<SchedulerConfigurationBuilder> configure,
        string method
    )
    {
        Building(configure)
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{method} must*");
    }

    [Test]
    public void Polling_intervals_at_their_limits_build()
    {
        Building(b =>
                b.ManifestManagerPollingInterval(TimeSpan.FromSeconds(1))
                    .JobDispatcherPollingInterval(TimeSpan.FromDays(30))
            )
            .Should()
            .NotThrow();
    }

    #endregion
}

internal interface ITestTrain { }

internal interface ITestTrainB { }
