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
}
