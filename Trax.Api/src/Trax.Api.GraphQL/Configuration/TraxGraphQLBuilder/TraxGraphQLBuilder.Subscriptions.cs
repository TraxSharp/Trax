namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

public partial class TraxGraphQLBuilder
{
    internal bool JunctionAnswersForBroadcastAllowed { get; private set; }

    /// <summary>
    /// Lets subscribers outside the operations view see the answers a run's routing steps acted on
    /// (<c>answer</c> and <c>confidence</c> on <c>onJunctionEvent</c>). Off by default: a broadcast
    /// subscriber sees that a question was asked and which step ran, but not what was answered.
    /// </summary>
    /// <remarks>
    /// An answer can say as much about a run as its output, and a broadcast subscriber is anyone
    /// the train's own posture admits, so a host opts in only when its broadcast trains' answers
    /// are meant for those callers. The operations view always sees them, and an answer to a
    /// question about a <c>[TraxSensitive]</c> type is never published to anyone. See
    /// <c>docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md</c>.
    /// </remarks>
    public TraxGraphQLBuilder AllowJunctionAnswersForBroadcastSubscribers()
    {
        JunctionAnswersForBroadcastAllowed = true;
        return this;
    }

    internal HashSet<Type> BroadcastOutputTrains { get; } = [];

    /// <summary>
    /// Lets subscribers outside the operations view see <c>output</c> on the lifecycle events of
    /// the <c>[TraxBroadcast]</c> train <typeparamref name="TTrain"/>. Off by default: a broadcast
    /// subscriber sees that a run completed, not what it returned.
    /// </summary>
    /// <remarks>
    /// A train's posture admits a subscriber to every run of it, not only the runs that subscriber
    /// started, so an output reaches every caller the train admits. Opt a train in only when its
    /// output is meant for all of them. The operations view always sees output.
    /// </remarks>
    /// <typeparam name="TTrain">The train's interface or class, as registered with the mediator.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// Thrown while <c>AddTraxGraphQL</c> runs when <typeparamref name="TTrain"/> is not a
    /// registered train, or does not carry <c>[TraxBroadcast]</c>.
    /// </exception>
    public TraxGraphQLBuilder AllowOutputForBroadcastSubscribers<TTrain>() =>
        AllowOutputForBroadcastSubscribers(typeof(TTrain));

    /// <inheritdoc cref="AllowOutputForBroadcastSubscribers{TTrain}"/>
    /// <param name="trainType">The train's interface or class, as registered with the mediator.</param>
    public TraxGraphQLBuilder AllowOutputForBroadcastSubscribers(Type trainType)
    {
        ArgumentNullException.ThrowIfNull(trainType);
        BroadcastOutputTrains.Add(trainType);
        return this;
    }
}
