using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Mediator.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Services.JobDispatcherPollingService;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Services.MetadataCleanupPollingService;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.MetadataCleanup;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Fluent builder for configuring the Trax.Core scheduler.
/// </summary>
/// <remarks>
/// This builder allows configuring the scheduler as part of the Trax.Core effects setup:
/// <code>
/// services.AddTrax(trax => trax
///     .AddEffects(effects => effects.UsePostgres(connectionString))
///     .AddMediator(assemblies)
///     .AddScheduler(scheduler => scheduler
///         .UseLocalWorkers()
///         .Schedule&lt;IMyTrain&gt;("my-job", new MyInput(), Every.Minutes(5))
///     )
/// );
/// </code>
/// </remarks>
public partial class SchedulerConfigurationBuilder
{
    private readonly TraxBuilderWithMediator _parentBuilder;
    private readonly SchedulerConfiguration _configuration = new();
    private Action<IServiceCollection>? _taskServerRegistration;
    private string? _configuredSubmitterSource;
    private Action<IServiceCollection>? _remoteRunRegistration;
    private string? _rootScheduledExternalId;
    private string? _lastScheduledExternalId;

    // Dependency graph tracking for cycle detection at build time
    private readonly Dictionary<string, string> _externalIdToGroupId = new();
    private readonly List<(string ParentExternalId, string ChildExternalId)> _dependencyEdges = [];

    /// <summary>
    /// Creates a new scheduler configuration builder.
    /// </summary>
    /// <param name="parentBuilder">The builder after mediator has been configured</param>
    public SchedulerConfigurationBuilder(TraxBuilderWithMediator parentBuilder)
    {
        _parentBuilder = parentBuilder;
    }

    /// <summary>
    /// Gets the service collection for registering services.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection ServiceCollection => _parentBuilder.ServiceCollection;

    /// <summary>
    /// Builds the scheduler configuration and registers all services.
    /// </summary>
    internal void Build()
    {
        ValidateNoCyclicGroupDependencies();
        ValidateSubmitterRequirements();

        _configuration.HasDatabaseProvider = _parentBuilder.HasDatabaseProvider;

        // Exclude internal scheduler trains from MaxActiveJobs count
        foreach (var name in AdminTrains.FullNames)
            _configuration.ExcludedTrainTypeNames.Add(name);

        // Register the configuration
        _parentBuilder.ServiceCollection.AddSingleton(_configuration);

        // Register the cancellation registry (singleton — shared across all workers)
        _parentBuilder.ServiceCollection.AddSingleton<
            ICancellationRegistry,
            CancellationRegistry
        >();

        // Register ITraxScheduler
        _parentBuilder.ServiceCollection.AddScoped<ITraxScheduler, TraxScheduler>();

        // Register IDormantDependentContext with forwarding so both concrete type
        // (for RunScheduledTrainStep.Initialize) and interface (for user steps)
        // resolve to the same scoped instance
        _parentBuilder.ServiceCollection.AddScoped<DormantDependentContext>();
        _parentBuilder.ServiceCollection.AddScoped<IDormantDependentContext>(sp =>
            sp.GetRequiredService<DormantDependentContext>()
        );

        // Register internal scheduler trains (AddScopedTraxRoute for property injection).
        // InMemory uses a simplified train that skips PostgreSQL-specific steps
        // (CancelTimedOutJobs, ReapStalePending) and dispatches jobs inline.
        if (_parentBuilder.HasDatabaseProvider)
        {
            _parentBuilder.ServiceCollection.AddScopedTraxRoute<
                IManifestManagerTrain,
                ManifestManagerTrain
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
            IJobDispatcherTrain,
            JobDispatcherTrain
        >();
        _parentBuilder.ServiceCollection.AddScopedTraxRoute<
            IMetadataCleanupTrain,
            MetadataCleanupTrain
        >();

        // Register the job submitter. If the user explicitly configured one (UseLocalWorkers,
        // UseRemoteWorkers, OverrideSubmitter), use that. Otherwise, default based on the
        // data provider: PostgresJobSubmitter for Postgres, InMemoryJobSubmitter for InMemory.
        if (_taskServerRegistration is not null)
        {
            _taskServerRegistration.Invoke(_parentBuilder.ServiceCollection);
        }
        else if (_parentBuilder.HasDatabaseProvider)
        {
            _parentBuilder.ServiceCollection.AddScoped<IJobSubmitter, PostgresJobSubmitter>();
        }
        else
        {
            _parentBuilder.ServiceCollection.AddScopedTraxRoute<IJobRunnerTrain, JobRunnerTrain>();
            _parentBuilder.ServiceCollection.AddScoped<IJobSubmitter, InMemoryJobSubmitter>();
        }

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

            if (_configuration.MetadataCleanup is not null)
                _parentBuilder.ServiceCollection.AddHostedService<MetadataCleanupPollingService>();
        }
    }

    /// <summary>
    /// Validates that the manifest group dependency graph is a DAG (no circular dependencies).
    /// </summary>
    private void ValidateNoCyclicGroupDependencies()
    {
        if (_dependencyEdges.Count == 0)
            return;

        // Derive group-level edges from manifest-level edges
        var groupNodes = new System.Collections.Generic.HashSet<string>(
            _externalIdToGroupId.Values
        );
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
                "AddScheduler() requires a data provider (UsePostgres() or UseInMemory()). "
                    + "The scheduler's background services need a data context to manage manifests, "
                    + "metadata, and work queue entries.\n\n"
                    + "Add a data provider to your effects configuration:\n\n"
                    + "  services.AddTrax(trax => trax\n"
                    + "      .AddEffects(effects => effects.UsePostgres(connectionString)) // or .UseInMemory()\n"
                    + "      .AddMediator(assemblies)\n"
                    + "      .AddScheduler(scheduler => ...)\n"
                    + "  );\n"
            );
        }

        if (
            _configuredSubmitterSource is nameof(UseLocalWorkers)
            && !_parentBuilder.HasDatabaseProvider
        )
        {
            throw new InvalidOperationException(
                "UseLocalWorkers() requires a PostgreSQL database provider. "
                    + "Local workers poll the trax.background_job table using PostgreSQL's "
                    + "FOR UPDATE SKIP LOCKED for atomic job dequeue.\n\n"
                    + "Add UsePostgres() to your effects configuration:\n\n"
                    + "  services.AddTrax(trax => trax\n"
                    + "      .AddEffects(effects => effects.UsePostgres(connectionString))\n"
                    + "      .AddMediator(assemblies)\n"
                    + "      .AddScheduler(scheduler => scheduler.UseLocalWorkers())\n"
                    + "  );\n\n"
                    + "If you don't need PostgreSQL, omit UseLocalWorkers() to use the default "
                    + "in-memory submitter, or use UseRemoteWorkers() / UseSqsWorkers() to "
                    + "dispatch to an external system."
            );
        }
    }
}
