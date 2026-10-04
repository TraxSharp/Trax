namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Controls which trains' lifecycle events reach the <c>onTrain*</c> GraphQL subscriptions.
/// </summary>
/// <remarks>
/// Two distinct concerns are deliberately separated here:
/// <list type="bullet">
///   <item>
///     <b>User-facing subscriptions</b> — an application streams a curated subset of trains to its
///     own clients. That is opt-in per train via <c>[TraxBroadcast]</c>, which is the default
///     (<see cref="StreamAllTrains"/> is <c>false</c>).
///   </item>
///   <item>
///     <b>Admin observability</b> — an operations dashboard should see every train running on the
///     server, not a per-train opt-in subset. <c>AddTraxGraphQL()</c> sets
///     <see cref="StreamAllTrains"/> to <c>true</c> automatically when the operations surface is
///     exposed (<c>ExposeOperationQueries()</c> / <c>ExposeOperationMutations()</c>), so the admin
///     feed is comprehensive without decorating trains.
///   </item>
/// </list>
/// Registered as a singleton by <c>AddTraxGraphQL()</c>.
/// </remarks>
public sealed class TrainLifecycleStreamOptions
{
    /// <summary>
    /// When <c>true</c>, every train's lifecycle is streamed regardless of <c>[TraxBroadcast]</c>.
    /// When <c>false</c> (default), only <c>[TraxBroadcast]</c> trains are streamed.
    /// </summary>
    public bool StreamAllTrains { get; internal init; }

    /// <summary>
    /// When <c>true</c>, subscribers outside the operations view see the <c>answer</c> and
    /// <c>confidence</c> of a run's steps on <c>onJunctionEvent</c>. When <c>false</c> (default),
    /// only the operations view sees them. Set by
    /// <c>AllowJunctionAnswersForBroadcastSubscribers()</c>. An answer to a question about a
    /// <c>[TraxSensitive]</c> type is withheld from everyone either way.
    /// </summary>
    public bool IncludeJunctionAnswersForBroadcastSubscribers { get; internal init; }
}
