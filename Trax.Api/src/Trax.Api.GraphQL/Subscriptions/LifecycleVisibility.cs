using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.Services.Authorization;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>Which lifecycle events a subscriber receives, and in how much detail.</summary>
/// <param name="All">Every train, with operations detail.</param>
/// <param name="Trains">When not <paramref name="All"/>, the broadcast trains the subscriber may see.</param>
internal sealed record LifecycleVisibility(bool All, IReadOnlySet<string> Trains)
{
    public static readonly LifecycleVisibility Operations = new(
        All: true,
        new HashSet<string>(StringComparer.Ordinal)
    );

    public bool IsEmpty => !All && Trains.Count == 0;

    /// <summary>
    /// Whether this visibility admits everything <paramref name="other"/> admits: the operations
    /// view covers every view, and a broadcast view covers another whose trains it all holds.
    /// </summary>
    public bool Covers(LifecycleVisibility other) =>
        All || (!other.All && other.Trains.All(Trains.Contains));

    /// <summary>
    /// Outside the operations view, whether the answers on a run's steps are shown. Off unless the
    /// host called <c>AllowJunctionAnswersForBroadcastSubscribers()</c>.
    /// </summary>
    public bool JunctionAnswers { get; init; }

    /// <summary>
    /// Outside the operations view, the trains whose events keep <c>output</c>: the ones the host
    /// named with <c>AllowOutputForBroadcastSubscribers()</c>. Every other train's output is
    /// withheld.
    /// </summary>
    public IReadOnlySet<string> OutputTrains { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The event as this subscriber should see it, or <see langword="null"/> when it should not see it.
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
            Output = OutputTrains.Contains(e.TrainName) ? e.Output : null,
            HostName = null,
            HostEnvironment = null,
        };
    }

    /// <summary>
    /// The junction event as this subscriber should see it, or <see langword="null"/> when it should not see
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
