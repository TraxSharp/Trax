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
///         .PollingInterval(TimeSpan.FromSeconds(30))
///         .MaxActiveJobs(100)
///         .UseLocalWorkers()
///     )
/// );
/// </code>
/// </remarks>
public partial class SchedulerConfigurationBuilder
{
    private readonly TraxBuilderWithMediator _parentBuilder;
    private readonly SchedulerConfiguration _configuration = new();
    private Action<IServiceCollection>? _taskServerRegistration;
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
    public IServiceCollection ServiceCollection => _parentBuilder.ServiceCollection;

    /// <summary>
    /// Builds the scheduler configuration and registers all services.
    /// </summary>
    /// <returns>The parent builder for continued chaining</returns>
    internal TraxBuilderWithMediator Build()
    {
        ValidateNoCyclicGroupDependencies();

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

        // Register JobDispatcher train (must use AddScopedTraxRoute for property injection)
        _parentBuilder.ServiceCollection.AddScopedTraxRoute<
            IJobDispatcherTrain,
            JobDispatcherTrain
        >();

        // PostgresJobSubmitter is the default submitter since Postgres is required for scheduling.
        // UseRemoteWorkers() and UseInMemoryWorkers() override this via last-registration-wins.
        _parentBuilder.ServiceCollection.AddScoped<IJobSubmitter, PostgresJobSubmitter>();

        // Register additional job submitter services if configured (may override the default above)
        _taskServerRegistration?.Invoke(_parentBuilder.ServiceCollection);

        // Registration order matters: .NET starts IHostedService instances sequentially in registration order.
        // SchedulerStartupService must complete before the polling services begin.
        _parentBuilder.ServiceCollection.AddHostedService<SchedulerStartupService>();
        _parentBuilder.ServiceCollection.AddHostedService<ManifestManagerPollingService>();
        _parentBuilder.ServiceCollection.AddHostedService<JobDispatcherPollingService>();

        // Register the metadata cleanup service if configured
        if (_configuration.MetadataCleanup is not null)
            _parentBuilder.ServiceCollection.AddHostedService<MetadataCleanupPollingService>();

        return _parentBuilder;
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
}
