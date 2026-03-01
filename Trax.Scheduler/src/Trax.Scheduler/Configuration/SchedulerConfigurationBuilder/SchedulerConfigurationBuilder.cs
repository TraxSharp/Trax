using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Extensions;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Services.JobDispatcherPollingService;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Services.ManifestScheduler;
using Trax.Scheduler.Services.MetadataCleanupPollingService;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Utilities;
using Trax.Scheduler.Workflows.JobDispatcher;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Fluent builder for configuring the Trax.Core scheduler.
/// </summary>
/// <remarks>
/// This builder allows configuring the scheduler as part of the Trax.Core effects setup:
/// <code>
/// services.AddTraxEffects(options => options
///     .AddServiceTrainBus(assemblies)
///     .AddPostgresEffect(connectionString)
///     .AddScheduler(scheduler => scheduler
///         .PollingInterval(TimeSpan.FromSeconds(30))
///         .MaxActiveJobs(100)
///         .UseHangfire(config => config.UsePostgreSqlStorage(...))
///     )
/// );
/// </code>
/// </remarks>
public partial class SchedulerConfigurationBuilder
{
    private readonly TraxEffectConfigurationBuilder _parentBuilder;
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
    /// <param name="parentBuilder">The parent Trax.Core effect configuration builder</param>
    public SchedulerConfigurationBuilder(TraxEffectConfigurationBuilder parentBuilder)
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
    internal TraxEffectConfigurationBuilder Build()
    {
        ValidateNoCyclicGroupDependencies();

        // Exclude internal scheduler workflows from MaxActiveJobs count
        foreach (var name in AdminWorkflows.FullNames)
            _configuration.ExcludedWorkflowTypeNames.Add(name);

        // Register the configuration
        _parentBuilder.ServiceCollection.AddSingleton(_configuration);

        // Register the cancellation registry (singleton — shared across all workers)
        _parentBuilder.ServiceCollection.AddSingleton<
            ICancellationRegistry,
            CancellationRegistry
        >();

        // Register IManifestScheduler
        _parentBuilder.ServiceCollection.AddScoped<IManifestScheduler, ManifestScheduler>();

        // Register IDormantDependentContext with forwarding so both concrete type
        // (for ExecuteScheduledWorkflowStep.Initialize) and interface (for user steps)
        // resolve to the same scoped instance
        _parentBuilder.ServiceCollection.AddScoped<DormantDependentContext>();
        _parentBuilder.ServiceCollection.AddScoped<IDormantDependentContext>(sp =>
            sp.GetRequiredService<DormantDependentContext>()
        );

        // Register JobDispatcher workflow (must use AddScopedTraxRoute for property injection)
        _parentBuilder.ServiceCollection.AddScopedTraxRoute<
            IJobDispatcherWorkflow,
            JobDispatcherWorkflow
        >();

        // Register task server if configured
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
