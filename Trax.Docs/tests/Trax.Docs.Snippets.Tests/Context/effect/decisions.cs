// What the compiled examples on effect/decisions.md take as given.
global using static DecisionsPage;
global using Trax.Effect.Data.Extensions;
global using Trax.Effect.Data.Postgres.Extensions;
global using Trax.Effect.Extensions;
using Trax.Core.Decisions;

public static class DecisionsPage
{
    public static string secret => "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
}

public class DecisionAuditor : IDecisionObserver
{
    public Task Decided(DecisionMade decision, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task Routed(TrackRouted routing, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
