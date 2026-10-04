using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.Services.Authorization;
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
/// operations authorization (the <c>GateOperations(...)</c> gate, or none when the host chose
/// <c>AllowAnonymousOperations()</c> or gated the whole endpoint) sees every train, with the same
/// detail <c>operations.executions</c> shows.
/// </para>
/// <para>
/// <b>Broadcast view.</b> Anyone else sees only <c>[TraxBroadcast]</c> trains whose own posture
/// they satisfy: <c>[TraxAllowAnonymous]</c> admits every subscriber, <c>[TraxAuthorize]</c> an
/// authenticated one that meets its policies and roles, and a train that declares neither is
/// admitted only behind an endpoint policy, which startup requires of it. A failure reason is shown only when the train raised it as a <c>TrainException</c>,
/// whose message is written for clients; host detail is withheld.
/// </para>
/// <para>
/// A subscriber who could receive nothing is refused when subscribing.
/// See <c>docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md</c>.
/// </para>
/// </remarks>
internal sealed class LifecycleSubscriptionAccess(
    GraphQLConfiguration configuration,
    ITrainDiscoveryService discovery,
    IAuthorizationService authorization,
    TrainLifecycleStreamOptions streamOptions
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
                trains.Add(train.ServiceType.FullName!);

        return new LifecycleVisibility(All: false, trains)
        {
            JunctionAnswers = streamOptions.IncludeJunctionAnswersForBroadcastSubscribers,
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

    private async ValueTask<bool> SatisfiesOperationsAsync(ClaimsPrincipal? user)
    {
        if (!OperationsExposed)
            return false;

        // No gate of its own: the host either published the namespace deliberately or gated the
        // whole endpoint, which the request pipeline has already enforced for this subscriber.
        var gate = configuration.OperationsAuthorizeAttributes;
        if (gate.Count == 0)
            return true;

        if (!IsAuthenticated(user))
            return false;

        AuthorizeDirectives.ExtractRules(gate, out var policies, out var roleSets);
        foreach (var policy in policies)
            if (
                !(await authorization.AuthorizeAsync(user!, policy).ConfigureAwait(false)).Succeeded
            )
                return false;

        // Each @authorize(roles:) on the operations field is an IsInRole check, and the field
        // carries one per GateOperations(roles:) call, all of which must pass.
        return roleSets.All(roles => roles.Any(user!.IsInRole));
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
            if (
                !(await authorization.AuthorizeAsync(user!, policy).ConfigureAwait(false)).Succeeded
            )
                return false;

        // Exact, as TrainAuthorizationService and @authorize compare roles (Trax.Docs
        // adr/0026-train-roles-match-exactly-like-authorize.md), and one requirement per
        // attribute that names roles, as TrainAuthorizationService applies them.
        return TrainRoleRequirements.For(train).All(roles => roles.Any(user!.IsInRole));
    }

    private static bool IsAuthenticated(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true;
}

/// <summary>Which lifecycle events a subscriber receives, and in how much detail.</summary>
/// <param name="All">Every train, with operations detail.</param>
/// <param name="Trains">When not <paramref name="All"/>, the broadcast trains the subscriber may see.</param>
internal sealed record LifecycleVisibility(bool All, IReadOnlySet<string> Trains)
{
    public static readonly LifecycleVisibility Operations = new(
        true,
        new HashSet<string>(StringComparer.Ordinal)
    );

    public bool IsEmpty => !All && Trains.Count == 0;

    /// <summary>
    /// Outside the operations view, whether the answers on a run's steps are shown. Off unless the
    /// host called <c>AllowJunctionAnswersForBroadcastSubscribers()</c>.
    /// </summary>
    public bool JunctionAnswers { get; init; }

    /// <summary>
    /// The event as this subscriber should see it, or <c>null</c> when it should not see it.
    /// </summary>
    public TrainLifecycleEvent? Present(TrainLifecycleEvent e)
    {
        if (All)
            return e;

        if (!Trains.Contains(e.TrainName))
            return null;

        return e with
        {
            FailureReason =
                e.FailureReason is null ? null
                : string.Equals(e.FailureException, "TrainException", StringComparison.Ordinal)
                    ? e.FailureReason
                : LifecycleSubscriptionAccess.MaskedFailureReason,
            HostName = null,
            HostEnvironment = null,
        };
    }

    /// <summary>
    /// The junction event as this subscriber should see it, or <c>null</c> when it should not see
    /// it: exactly when it would not see the train's own events.
    /// </summary>
    /// <remarks>
    /// Outside the operations view the host detail a step carries is withheld, as it is from a
    /// train event: the decider's type name, and the exception type of a failure unless it is a
    /// <c>TrainException</c>, the one a train raises for its clients. The answer and confidence
    /// are withheld too unless the host opted in (<see cref="JunctionAnswers"/>), and so is
    /// everything on a step on a decision track that names what ran there, of any kind: its name,
    /// its question key and its answer. The step itself, its position and its timing still arrive.
    /// </remarks>
    public JunctionEvent? Present(JunctionEvent e)
    {
        if (All)
            return e;

        if (!Trains.Contains(e.TrainName))
            return null;

        // Without the opt-in a broadcast subscriber is not shown answers, and what ran on a
        // decision track (a junction, a further question, a further route) gives the answer away,
        // so whatever names it is withheld as well.
        var step =
            !JunctionAnswers && e.Junction.OnATrack ? e.Junction.WithTrackWithheld() : e.Junction;
        return e with
        {
            Junction = step with
            {
                Decider = null,
                Answer = JunctionAnswers ? step.Answer : null,
                Confidence = JunctionAnswers ? step.Confidence : null,
                FailureException = string.Equals(
                    step.FailureException,
                    "TrainException",
                    StringComparison.Ordinal
                )
                    ? step.FailureException
                    : null,
            },
        };
    }
}
