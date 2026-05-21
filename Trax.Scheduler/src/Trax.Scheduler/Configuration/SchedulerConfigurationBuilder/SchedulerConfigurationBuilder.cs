using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Trax.Mediator.Configuration;
using Trax.Scheduler.Services.JobSubmitter;

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
///         .Schedule&lt;IMyTrain&gt;("my-job", new MyInput(), Every.Minutes(5))
///     )
/// );
/// </code>
/// Local workers are enabled by default when PostgreSQL is configured.
/// Use <c>UseRemoteWorkers()</c> to route specific trains to a remote endpoint.
/// </remarks>
public partial class SchedulerConfigurationBuilder
{
    private readonly TraxBuilderWithMediator _parentBuilder;
    private readonly SchedulerConfiguration _configuration = new();
    private readonly LocalWorkerOptions _localWorkerOptions = new();
    private readonly JobSubmitterRoutingConfiguration _routingConfiguration = new();

    private readonly List<RoutedSubmitterRegistration> _routedSubmitterRegistrations = [];

    // Legacy: supports UseInMemoryWorkers() and OverrideSubmitter()
    private Action<IServiceCollection>? _taskServerRegistration;

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
    /// Adds a routed submitter registration. Used by extension methods (e.g., UseSqsWorkers)
    /// to register additional submitter backends with per-train routing.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddRoutedSubmitter(RoutedSubmitterRegistration registration) =>
        _routedSubmitterRegistrations.Add(registration);

    /// <summary>
    /// Sets the remote run executor registration. Used by extension methods (e.g., UseLambdaRun)
    /// to override the default <c>LocalRunExecutor</c> with a remote implementation.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void SetRemoteRunRegistration(Action<IServiceCollection> registration) =>
        _remoteRunRegistration = registration;
}

/// <summary>
/// Record for tracking a routed submitter registration.
/// Used by extension methods (e.g., <c>UseSqsWorkers()</c>) to register additional submitter backends.
/// </summary>
public record RoutedSubmitterRegistration(
    SubmitterRouting Routing,
    Type SubmitterType,
    Action<IServiceCollection> Register
);
