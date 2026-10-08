using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.DeadLetterRequeue;
using Trax.Scheduler.Services.DecisionRecording;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Services.Effects;
using Trax.Scheduler.Services.JobDispatcherPollingService;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.LogLevels;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Services.MetadataCleanupPollingService;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.QueuedWorkListenerService;
using Trax.Scheduler.Services.RunOutcomes;
using Trax.Scheduler.Services.SchedulerLiveness;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.ManifestManager.Utilities;
using Trax.Scheduler.Trains.MetadataCleanup;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    /// <summary>
    /// Builds the scheduler configuration and registers all services.
    /// </summary>
    internal void Build()
    {
        ValidateNoCyclicGroupDependencies();
        ValidateSeedDeclarations();
        ValidateSubmitterRequirements();
        ValidateRoutedSubmitters();
        ValidateLimits();

        _configuration.HasDatabaseProvider = _parentBuilder.HasDatabaseProvider;
        _configuration.IsSchedulerHost = true;

        // Exclude internal scheduler trains from MaxActiveJobs count
        foreach (var name in AdminTrains.FullNames)
            _configuration.ExcludedTrainTypeNames.Add(name);

        // Register the configuration. Replace, not Add: AddTraxJobRunner registers an empty one
        // for a runner-only host, and a host that called it first must still get this one.
        _parentBuilder.ServiceCollection.Replace(ServiceDescriptor.Singleton(_configuration));

        // Liveness tracking: the JobDispatcher stamps this after each successful cycle so a
        // wedged scheduler (up but dispatching nothing) can be detected via
        // AddHealthChecks().AddTraxSchedulerLiveness(). TryAdd so a consumer-provided
        // TimeProvider (e.g. a test clock) wins.
        _parentBuilder.ServiceCollection.TryAddSingleton(TimeProvider.System);
        // The dispatcher takes the concrete monitor, the only type that can record a cycle; the
        // health check and everything else resolve the read-only interface over the same instance.
        _parentBuilder.ServiceCollection.AddSingleton<SchedulerLivenessMonitor>();
        _parentBuilder.ServiceCollection.AddSingleton<ISchedulerLivenessMonitor>(sp =>
            sp.GetRequiredService<SchedulerLivenessMonitor>()
        );

        // Register the cancellation registry (singleton — shared across all workers)
        _parentBuilder.ServiceCollection.Replace(
            ServiceDescriptor.Singleton<ICancellationRegistry, CancellationRegistry>()
        );

        // Register ITraxScheduler
        _parentBuilder.ServiceCollection.AddScoped<ITraxScheduler, TraxScheduler>();

        // The effects list, toggle and settings editor the dashboard and the GraphQL API share
        // (docs/0022). It acts on this process only.
        _parentBuilder.ServiceCollection.TryAddScoped<
            IEffectSettingsService,
            EffectSettingsService
        >();

        // Chooses the run a manifest's retry replays the decisions of (docs/adr/0017).
        _parentBuilder.ServiceCollection.TryAddScoped(sp => new RetryDecisionReplay(
            sp.GetRequiredService<IDataContextProviderFactory>(),
            sp.GetRequiredService<ILogger<RetryDecisionReplay>>()
        ));

        // Register IOperationsService — shared between dashboard UI and GraphQL operations
        // mutations so both surfaces have identical validation and persistence behaviour.
        _parentBuilder.ServiceCollection.AddScoped<IOperationsService, OperationsService>();

        // The background requeue-all both surfaces start and read back (docs/0022). A singleton:
        // the jobs are this node's, and a fold runs until it finishes or the host stops.
        _parentBuilder.ServiceCollection.TryAddSingleton<
            IDeadLetterRequeueJobs,
            DeadLetterRequeueJobs
        >();

        // The runtime log levels the dashboard and the GraphQL API share (docs/0022), applied to
        // this process's logger filters.
        RegisterLogLevelOverrides(_parentBuilder.ServiceCollection);

        // Reads the persisted scheduler_config row at startup and applies it to the
        // in-memory SchedulerConfiguration singleton, then keeps checking it so a save made on
        // any host reaches this one within seconds.
        _parentBuilder.ServiceCollection.AddHostedService<SchedulerConfigBootstrapHostedService>();

        // Holds the lifecycle events of the runs a ManifestManager cycle fails until its
        // transaction commits.
        _parentBuilder.ServiceCollection.TryAddScoped<DeferredOutcomeEvents>();

        // Register IDormantDependentContext with forwarding so both concrete type
        // (for RunScheduledTrainJunction.Initialize) and interface (for user steps)
        // resolve to the same scoped instance
        _parentBuilder.ServiceCollection.AddScoped<DormantDependentContext>();
        _parentBuilder.ServiceCollection.AddScoped<IDormantDependentContext>(sp =>
            sp.GetRequiredService<DormantDependentContext>()
        );

        // Register internal scheduler trains (AddScopedTraxRoute for property injection).
        // InMemory uses a simplified train that skips PostgreSQL-specific steps
        // (CancelTimedOutJobs, ReapStalePending) and dispatches jobs inline.
        //
        // The job dispatcher claims work with provider-specific SQL, so it is registered only with
        // a database provider, the only place JobDispatcherPollingService runs it. Registered
        // without one, it is a train the mediator's startup check can never build, and the host is
        // refused.
        if (_parentBuilder.HasDatabaseProvider)
        {
            _parentBuilder.ServiceCollection.AddScopedTraxRoute<
                IManifestManagerTrain,
                ManifestManagerTrain
            >();
            _parentBuilder.ServiceCollection.AddScopedTraxRoute<
                IJobDispatcherTrain,
                JobDispatcherTrain
            >();
        }
        else
        {
            _parentBuilder.ServiceCollection.AddScopedTraxRoute<
                IManifestManagerTrain,
                InMemoryManifestManagerTrain
            >();
        }
        _parentBuilder.ServiceCollection.AddScopedTraxRoute<
            IMetadataCleanupTrain,
            MetadataCleanupTrain
        >();
        _parentBuilder.ServiceCollection.AddScopedTraxRoute<
            Trains.DeadLetterCleanup.IDeadLetterCleanupTrain,
            Trains.DeadLetterCleanup.DeadLetterCleanupTrain
        >();

        // Register the job submitter and local workers.
        // Priority: UseInMemoryWorkers/OverrideSubmitter > default (Postgres local workers or InMemory).
        if (_taskServerRegistration is not null)
        {
            _taskServerRegistration.Invoke(_parentBuilder.ServiceCollection);
        }
        else if (_parentBuilder.HasDatabaseProvider)
        {
            // Default: PostgresJobSubmitter + local worker threads
            if (
                _parentBuilder.ServiceCollection.Any(d =>
                    d.ServiceType == typeof(LocalWorkerOptions)
                )
            )
                throw new InvalidOperationException(LocalWorkerOptions.RegisteredTwiceMessage);
            _parentBuilder.ServiceCollection.AddSingleton(_localWorkerOptions);
            _parentBuilder.ServiceCollection.AddScoped<IJobSubmitter, PostgresJobSubmitter>();
            _parentBuilder.ServiceCollection.AddScopedTraxRoute<IJobRunnerTrain, JobRunnerTrain>();
            _parentBuilder.ServiceCollection.AddHostedService<Services.LocalWorkerService.LocalWorkerService>();
        }
        else
        {
            _parentBuilder.ServiceCollection.AddScopedTraxRoute<IJobRunnerTrain, JobRunnerTrain>();
            _parentBuilder.ServiceCollection.AddScoped<IJobSubmitter, InMemoryJobSubmitter>();
        }

        // This host runs trains, so it refuses to start when the ones that ask a decider cannot
        // replay a requeue's recorded decisions. Registered once however many paths ask.
        DecisionRecordingStartupCheck.Register(_parentBuilder.ServiceCollection);

        // Register routed submitters (UseRemoteWorkers, UseSqsWorkers with ForTrain routing)
        RegisterRoutedSubmitters();

        // Always register routing configuration (DispatchJobsJunction requires it via constructor injection).
        // An empty configuration with no routes is a no-op — GetSubmitterType returns null for all trains.
        _parentBuilder.ServiceCollection.AddSingleton(_routingConfiguration);

        // Register remote run executor if configured (overrides default LocalRunExecutor)
        _remoteRunRegistration?.Invoke(_parentBuilder.ServiceCollection);

        // Registration order matters: .NET starts IHostedService instances sequentially in registration order.
        // SchedulerStartupService must complete before the polling services begin.
        _parentBuilder.ServiceCollection.AddHostedService<SchedulerStartupService>();

        // ManifestManagerPollingService runs for both providers. With Postgres, the resolved
        // IManifestManagerTrain uses advisory locks and creates WorkQueue entries. With InMemory,
        // the resolved InMemoryManifestManagerTrain dispatches jobs inline — no JobDispatcher needed.
        _parentBuilder.ServiceCollection.AddHostedService<ManifestManagerPollingService>();

        // JobDispatcher and MetadataCleanup use PostgreSQL-specific operations
        // (FOR UPDATE SKIP LOCKED, ExecuteUpdateAsync, ExecuteDeleteAsync) that are not
        // supported by the InMemory EF Core provider.
        if (_parentBuilder.HasDatabaseProvider)
        {
            _parentBuilder.ServiceCollection.AddHostedService<JobDispatcherPollingService>();

            // Ends the dispatcher's wait when the provider reports queued work: on Postgres from
            // any host, on SQLite from this one. The poll stays the fallback.
            _parentBuilder.ServiceCollection.AddSingleton<DispatcherWake>();
            _parentBuilder.ServiceCollection.AddSingleton<QueuedWorkListenerService>();
            _parentBuilder.ServiceCollection.AddHostedService(sp =>
                sp.GetRequiredService<QueuedWorkListenerService>()
            );

            if (_configuration.MetadataCleanup is not null)
            {
                // Before the polling service, which sweeps as soon as it starts: a contradictory
                // set of per-train retentions should fail the host rather than be resolved
                // silently in the first sweep.
                _parentBuilder.ServiceCollection.AddHostedService<MetadataCleanupConfigurationValidator>();
                _parentBuilder.ServiceCollection.AddHostedService<MetadataCleanupPollingService>();
            }

            // Always registered: AutoPurgeDeadLetters can be switched on at runtime, and the
            // cleanup reads it on every run rather than here.
            _parentBuilder.ServiceCollection.AddHostedService<Services.DeadLetterCleanupPollingService.DeadLetterCleanupPollingService>();
        }
    }

    /// <summary>
    /// Registers routed submitters and builds the routing configuration.
    /// </summary>
    private void RegisterRoutedSubmitters()
    {
        // [TraxRemote] trains go to the first routed registration. With none, there is nowhere to
        // send them, and running them locally instead would put a train marked remote, often for
        // isolation, on the one host it was kept off.
        var remoteTrains = new TrainDiscoveryService(_parentBuilder.ServiceCollection)
            .DiscoverTrains()
            .Where(r => r.IsRemote)
            .Select(r => r.ServiceType.FullName!)
            .ToList();

        if (_routedSubmitterRegistrations.Count == 0)
        {
            if (remoteTrains.Count > 0)
                throw new InvalidOperationException(
                    $"{string.Join(", ", remoteTrains.Order().Select(n => $"'{n}'"))} "
                        + $"{(remoteTrains.Count == 1 ? "is" : "are")} marked [TraxRemote], but "
                        + "this scheduler has no remote submitter to send "
                        + $"{(remoteTrains.Count == 1 ? "it" : "them")} to, so "
                        + $"{(remoteTrains.Count == 1 ? "it" : "they")} would run on this host. "
                        + "Add UseRemoteWorkers(), UseSqsWorkers() or UseLambdaWorkers() to the "
                        + "scheduler, or remove the attribute from a train that should run locally."
                );

            return;
        }

        foreach (var registration in _routedSubmitterRegistrations)
        {
            // Register the submitter with its dependencies (HttpClient, options, etc.)
            registration.Register(_parentBuilder.ServiceCollection);

            // Add explicit ForTrain routes, to this registration rather than to its type: two
            // registrations of one submitter type each submit to their own endpoint.
            foreach (var trainName in registration.Routing.TrainNames)
            {
                _routingConfiguration.AddRoute(trainName, registration);
            }
        }

        // Register the [TraxRemote] trains for attribute-based routing
        foreach (var train in remoteTrains)
            _routingConfiguration.AddAttributeRemoteTrain(train);

        // [TraxRemote] trains go to the first routed registration, of whatever kind
        _routingConfiguration.SetAttributeDefaultSubmitter(_routedSubmitterRegistrations[0]);
    }

    /// <summary>
    /// Validates that the manifest group dependency graph is a DAG (no circular dependencies).
    /// </summary>
    private void ValidateNoCyclicGroupDependencies()
    {
        if (_dependencyEdges.Count == 0)
            return;

        // Derive group-level edges from manifest-level edges
        var groupNodes = new HashSet<string>(_externalIdToGroupId.Values);
        var groupEdges = _dependencyEdges
            .Select(e =>
            {
                _externalIdToGroupId.TryGetValue(e.ParentExternalId, out var fromGroup);
                _externalIdToGroupId.TryGetValue(e.ChildExternalId, out var toGroup);
                return (From: fromGroup, To: toGroup);
            })
            .Where(e => e.From is not null && e.To is not null && e.From != e.To)
            .Select(e => (e.From!, e.To!))
            .Distinct()
            .ToList();

        if (groupEdges.Count == 0)
            return;

        var result = DagValidator.TopologicalSort(groupNodes, groupEdges);

        if (!result.IsAcyclic)
        {
            var cycleGroups = string.Join(", ", result.CycleMembers.Order());
            throw new InvalidOperationException(
                $"Circular dependency detected among manifest groups: [{cycleGroups}]. "
                    + "Manifest groups must form a directed acyclic graph (DAG)."
            );
        }
    }

    /// <summary>
    /// Validates that the configured job submitter has the required infrastructure.
    /// </summary>
    private void ValidateSubmitterRequirements()
    {
        // The scheduler's hosted services (ManifestManagerPollingService, JobDispatcherPollingService)
        // depend on IDataContextProviderFactory. Without a data provider, they can't be resolved.
        if (!_parentBuilder.HasDataProvider)
        {
            throw new InvalidOperationException(
                "AddScheduler() requires a data provider (UsePostgres(), UseSqlite(), or UseInMemory()). "
                    + "The scheduler's background services need a data context to manage manifests, "
                    + "metadata, and work queue entries.\n\n"
                    + "Add a data provider to your effects configuration:\n\n"
                    + "  services.AddTrax(trax => trax\n"
                    + "      .AddEffects(effects => effects.UsePostgres(connectionString)) // or .UseSqlite(connectionString) / .UseInMemory()\n"
                    + "      .AddMediator(assemblies)\n"
                    + "      .AddScheduler(scheduler => ...)\n"
                    + "  );\n"
            );
        }
    }

    /// <summary>
    /// Validates that no train appears in multiple routed submitter configurations.
    /// </summary>
    private void ValidateRoutedSubmitters()
    {
        if (_routedSubmitterRegistrations.Count <= 1)
            return;

        var seen = new Dictionary<string, string>();
        foreach (var registration in _routedSubmitterRegistrations)
        {
            var submitterName = registration.Description ?? registration.SubmitterType.Name;
            foreach (var trainName in registration.Routing.TrainNames)
            {
                if (seen.TryGetValue(trainName, out var existingSubmitter))
                {
                    throw new InvalidOperationException(
                        $"Train '{trainName}' is routed to multiple submitters: "
                            + $"'{existingSubmitter}' and '{submitterName}'. "
                            + "Each train can only be routed to one submitter.\n\n"
                            + "Remove the duplicate ForTrain<T>() call from one of the submitter configurations."
                    );
                }
                seen[trainName] = submitterName;
            }
        }
    }

    /// <summary>
    /// Registers the runtime log levels once: the overrides as a post-configure step and change
    /// source of the logger filter options, so a level set at runtime applies over every
    /// configuration source and the logger factory re-reads its filters, and the service that
    /// reads and sets them.
    /// </summary>
    internal static void RegisterLogLevelOverrides(IServiceCollection services)
    {
        if (services.Any(sd => sd.ServiceType == typeof(LogLevelOverrides)))
            return;

        services.AddSingleton<LogLevelOverrides>();
        services.AddSingleton<IPostConfigureOptions<LoggerFilterOptions>>(sp =>
            sp.GetRequiredService<LogLevelOverrides>()
        );
        services.AddSingleton<IOptionsChangeTokenSource<LoggerFilterOptions>>(sp =>
            sp.GetRequiredService<LogLevelOverrides>()
        );
        services.TryAddSingleton<ILogLevelService>(sp => new LogLevelService(
            sp.GetRequiredService<LogLevelOverrides>(),
            sp.GetService<IConfiguration>(),
            sp.GetService<IOptionsMonitor<LoggerFilterOptions>>()
        ));
    }
}
