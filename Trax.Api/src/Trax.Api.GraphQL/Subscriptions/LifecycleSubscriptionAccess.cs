using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.Services.Authorization;
using Trax.Core.Extensions;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Decides what a subscriber may receive from the lifecycle and data-change subscriptions, from
/// the same authorization that guards the data they stream.
/// </summary>
/// <remarks>
/// <para>
/// <b>Operations view.</b> When the operations surface is exposed, a subscriber that satisfies the
/// operations gate (<c>GateOperations(...)</c> or <c>GateOperationsToAuthenticatedUsers()</c>, or
/// anyone when the host chose <c>AllowAnonymousOperations()</c>) sees every train, with the same
/// detail <c>operations.executions</c> shows. With no gate declared, nobody does.
/// </para>
/// <para>
/// <b>Broadcast view.</b> Anyone else sees only <c>[TraxBroadcast]</c> trains whose own posture
/// they satisfy: <c>[TraxAllowAnonymous]</c> admits every subscriber, <c>[TraxAuthorize]</c> an
/// authenticated one that meets its policies and roles, and a train that declares neither is
/// admitted only behind an endpoint policy, which startup requires of it. A failure reason is shown only when the train raised it as a <c>TrainException</c>,
/// whose message is written for clients; host detail is withheld, and so is a run's output unless
/// the host named the train with <c>AllowOutputForBroadcastSubscribers()</c>.
/// </para>
/// <para>
/// A subscriber who could receive nothing is refused when subscribing, and a subscription is
/// checked again every connection re-check interval (<see cref="Recheck"/>): one whose subscriber
/// no longer satisfies the endpoint policy, or no longer receives everything it was admitted to,
/// ends.
/// See <c>docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md</c>.
/// </para>
/// </remarks>
internal sealed class LifecycleSubscriptionAccess(
    GraphQLConfiguration configuration,
    ITrainDiscoveryService discovery,
    IAuthorizationService authorization,
    TrainLifecycleStreamOptions streamOptions,
    IServiceScopeFactory scopes,
    TimeProvider? time = null
)
{
    /// <summary>What replaces a failure reason a broadcast subscriber may not see.</summary>
    internal const string MaskedFailureReason = "Unexpected Execution Error";

    private readonly Lazy<IReadOnlyList<TrainRegistration>> _broadcastTrains = new(() =>
        discovery.DiscoverTrains().Where(r => r.IsBroadcastEnabled).ToList()
    );

    private bool OperationsExposed =>
        configuration.OperationQueriesExposed || configuration.OperationMutationsExposed;

    /// <summary>The lifecycle events <paramref name="user"/> may receive.</summary>
    public async ValueTask<LifecycleVisibility> LifecycleFor(ClaimsPrincipal? user)
    {
        if (await SatisfiesOperationsAsync(user).ConfigureAwait(false))
            return LifecycleVisibility.Operations;

        var trains = new HashSet<string>(StringComparer.Ordinal);
        foreach (var train in _broadcastTrains.Value)
            if (await SatisfiesTrainAsync(user, train).ConfigureAwait(false))
                trains.Add(train.ServiceType.FullName.NotNull());

        return new LifecycleVisibility(All: false, trains)
        {
            JunctionAnswers = streamOptions.IncludeJunctionAnswersForBroadcastSubscribers,
            OutputTrains = configuration.BroadcastOutputTrains,
        };
    }

    /// <summary>
    /// True when <paramref name="user"/> may receive data-change signals: the operations
    /// authorization when the operations surface is exposed, otherwise any authenticated caller.
    /// </summary>
    public async ValueTask<bool> DataChangesFor(ClaimsPrincipal? user) =>
        OperationsExposed
            ? await SatisfiesOperationsAsync(user).ConfigureAwait(false)
            : IsAuthenticated(user);

    /// <summary>
    /// True when <paramref name="user"/> may follow the operator's feeds that exist only in the
    /// operations view, such as <c>onMachineInstanceChanged</c>: the operations surface is exposed
    /// and the subscriber satisfies its gate. Never true for a broadcast subscriber.
    /// </summary>
    public ValueTask<bool> OperationsFor(ClaimsPrincipal? user) => SatisfiesOperationsAsync(user);

    /// <summary>
    /// The endpoint policy and <paramref name="stillAuthorized"/>, asked again for
    /// <paramref name="user"/> at the connection's credential re-check interval while a
    /// subscription is open.
    /// </summary>
    public SubscriptionRecheck Recheck(
        ClaimsPrincipal? user,
        Func<CancellationToken, ValueTask<bool>> stillAuthorized
    ) =>
        SubscriptionRecheck.Of(
            configuration,
            time ?? TimeProvider.System,
            scopes,
            user,
            stillAuthorized
        );

    /// <summary>
    /// The re-check of a lifecycle or junction subscription admitted with
    /// <paramref name="admitted"/>: it passes while <paramref name="user"/> still satisfies the
    /// endpoint policy and sees everything it was admitted to.
    /// </summary>
    public SubscriptionRecheck LifecycleRecheck(
        ClaimsPrincipal? user,
        LifecycleVisibility admitted
    ) =>
        Recheck(user, async _ => (await LifecycleFor(user).ConfigureAwait(false)).Covers(admitted));

    private async ValueTask<bool> SatisfiesOperationsAsync(ClaimsPrincipal? user)
    {
        if (!OperationsExposed)
            return false;

        // No gate of its own is a public namespace only when the host said so. The endpoint gate
        // never stands in for it: it admits the application's users, not its operators.
        var gate = configuration.OperationsAuthorizeAttributes;
        if (gate.Count == 0)
            return configuration.AnonymousOperationsAllowed;

        if (!IsAuthenticated(user))
            return false;

        AuthorizeDirectives.ExtractRules(gate, out var policies, out var roleSets);
        foreach (var policy in policies)
            if (!(await authorization.AuthorizeAsync(user, policy).ConfigureAwait(false)).Succeeded)
                return false;

        // Each @authorize(roles:) on the operations field is an IsInRole check, and the field
        // carries one per GateOperations(roles:) call, all of which must pass.
        return roleSets.All(roles => roles.Any(user.IsInRole));
    }

    private async ValueTask<bool> SatisfiesTrainAsync(
        ClaimsPrincipal? user,
        TrainRegistration train
    )
    {
        if (train.HasAllowAnonymousAttribute)
            return true;

        // No marker of its own is allowed only on a gated endpoint, where the endpoint policy the
        // request pipeline enforced is the gate, exactly as for a train exposed as a field.
        if (!train.HasAuthorizeAttribute)
            return configuration.AuthorizationRequired && IsAuthenticated(user);

        if (!IsAuthenticated(user))
            return false;

        foreach (var policy in train.RequiredPolicies)
            if (!(await authorization.AuthorizeAsync(user, policy).ConfigureAwait(false)).Succeeded)
                return false;

        // Exact, as TrainAuthorizationService and @authorize compare roles (Trax.Docs
        // adr/0026-train-roles-match-exactly-like-authorize.md), and one requirement per
        // attribute that names roles, as TrainAuthorizationService applies them.
        return TrainRoleRequirements.For(train).All(roles => roles.Any(user.IsInRole));
    }

    private static bool IsAuthenticated([NotNullWhen(true)] ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true;
}
