using Trax.Effect.Attributes;

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

    internal List<TraxAuthorizeAttribute> OperationsAuthorizeAttributes { get; } = [];

    internal bool OperationsGated => OperationsAuthorizeAttributes.Count > 0;

    /// <summary>
    /// Gates the <c>operations</c> namespace itself, leaving the rest of the endpoint open. The
    /// <c>@authorize</c> directive goes onto the <c>operations</c> field of <c>RootQuery</c> and
    /// <c>RootMutation</c>, so everything under it is unreachable without the policy or role,
    /// while a public train or query model on the same endpoint stays reachable.
    /// </summary>
    /// <remarks>
    /// This is the posture an open endpoint actually wants. <c>RequireAuthorization()</c> gates
    /// the whole endpoint, which a host with pre-login surfaces cannot do, and
    /// <see cref="AllowAnonymousOperations"/> publishes the control plane. Without this there was
    /// no third answer, so a host that needed one reached for the anonymous opt-in and got a
    /// public scheduler console.
    /// <para>
    /// Repeated calls accumulate: policies are AND'd, roles are unioned and OR'd, the same way
    /// repeated <c>[TraxAuthorize]</c> attributes combine on a train or an entity.
    /// </para>
    /// </remarks>
    /// <param name="policy">
    /// An ASP.NET Core authorization policy name that must be satisfied. <c>null</c> with no
    /// roles requires only an authenticated caller.
    /// </param>
    /// <param name="roles">
    /// A comma-separated list of roles. The caller must hold at least one.
    /// </param>
    public TraxGraphQLBuilder GateOperations(string? policy = null, string? roles = null)
    {
        OperationsAuthorizeAttributes.Add(
            new TraxAuthorizeAttribute { Policy = policy, Roles = roles }
        );
        return this;
    }
}
