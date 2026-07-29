namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

/// <summary>
/// Opt-in toggles for the predefined <c>operations</c> namespace. The Trax operational
/// surface (health, manifest discovery, dead letters, scheduler control mutations) is
/// disabled by default. Mutations in particular expose direct scheduler control and
/// should only be reachable when the consumer has thought about who can call them.
/// </summary>
public partial class TraxGraphQLBuilder
{
    internal bool OperationQueriesExposed { get; private set; }

    internal bool OperationMutationsExposed { get; private set; }

    /// <summary>
    /// Adds the <c>operations</c> namespace under <c>RootQuery</c>, exposing health,
    /// train discovery, manifests, manifest groups, executions, and the nested
    /// <c>operations.deadLetters</c> read queries. Off by default — these endpoints
    /// reveal the topology of the deployment and should only be exposed when the
    /// consumer has decided who can read them.
    /// </summary>
    public TraxGraphQLBuilder ExposeOperationQueries()
    {
        OperationQueriesExposed = true;
        return this;
    }

    /// <summary>
    /// Adds the <c>operations</c> namespace under <c>RootMutation</c>, exposing
    /// scheduler-control mutations (trigger, enable, disable, cancel, group ops)
    /// and the nested <c>operations.deadLetters</c> requeue/acknowledge ops. Off by
    /// default — these mutations call <see cref="Trax.Scheduler.Services.TraxScheduler.ITraxScheduler"/>
    /// directly and an unauthenticated caller could disrupt scheduled work.
    /// </summary>
    public TraxGraphQLBuilder ExposeOperationMutations()
    {
        OperationMutationsExposed = true;
        return this;
    }

    internal bool AnonymousOperationsAllowed { get; private set; }

    /// <summary>
    /// Acknowledges that the operations (admin) namespace is reachable without the builder's
    /// <c>RequireAuthorization()</c> gate. Exposing <see cref="ExposeOperationMutations"/> without
    /// a gate otherwise fails at startup: those mutations drive the scheduler directly, so
    /// anonymous access to them must be a deliberate choice, never a forgotten one. Call this only
    /// when the surface is protected another way (a private network, a sidecar, ASP.NET endpoint
    /// authorization) or is intentionally public. It has no effect on the schema; it only records
    /// that anonymous operations are intended so the startup guard stays quiet.
    /// </summary>
    public TraxGraphQLBuilder AllowAnonymousOperations()
    {
        AnonymousOperationsAllowed = true;
        return this;
    }
}
