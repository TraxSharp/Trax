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

    // PollingInterval sets both polling intervals, so a range refusal at build names whichever
    // method set the value.
    private string _manifestManagerIntervalSetBy = nameof(ManifestManagerPollingInterval);
    private string _jobDispatcherIntervalSetBy = nameof(JobDispatcherPollingInterval);

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
/// <remarks>
/// A train routed here is submitted through <see cref="CreateSubmitter"/> when it is set, which
/// lets two registrations of the same submitter type (two <c>UseRemoteWorkers</c> calls, for
/// two endpoints) each keep their own options and client. Without it, the submitter is resolved
/// from the container by <see cref="SubmitterType"/>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public record RoutedSubmitterRegistration(
    SubmitterRouting Routing,
    Type SubmitterType,
    Action<IServiceCollection> Register
)
{
    /// <summary>
    /// Creates this registration's submitter from a scope's services. Null resolves
    /// <see cref="SubmitterType"/> from the container instead.
    /// </summary>
    public Func<IServiceProvider, IJobSubmitter>? CreateSubmitter { get; init; }

    /// <summary>
    /// A short description of this registration for error messages, such as the endpoint it
    /// submits to. Null uses the submitter type's name.
    /// </summary>
    public string? Description { get; init; }
}
