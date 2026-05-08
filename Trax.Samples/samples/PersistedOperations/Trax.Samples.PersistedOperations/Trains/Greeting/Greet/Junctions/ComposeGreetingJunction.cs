using Trax.Core.Junction;

namespace Trax.Samples.PersistedOperations.Trains.Greeting.Greet.Junctions;

/// <summary>
/// Builds a greeting string for the supplied name. The wording lives in
/// this junction so that a hot-fix to the persisted document (or to this
/// junction) lets you change the greeting without redeploying clients.
/// </summary>
public class ComposeGreetingJunction : Junction<GreetInput, GreetOutput>
{
    public override Task<GreetOutput> Run(GreetInput input)
    {
        var output = new GreetOutput
        {
            Greeting = $"Hello, {input.Name}.",
            GreetedAt = DateTimeOffset.UtcNow,
        };
        return Task.FromResult(output);
    }
}
