using Trax.Core.Junction;

namespace Trax.Samples.PersistedOperations.Trains.Greeting.Greet.Junctions;

/// <summary>
/// Builds the greeting in the requested style.
/// </summary>
public class ComposeGreetingJunction : Junction<GreetInput, GreetOutput>
{
    public override Task<GreetOutput> Run(GreetInput input) =>
        Task.FromResult(
            new GreetOutput
            {
                Greeting = input.Style switch
                {
                    GreetingStyle.Formal => $"Good day, {input.Name}.",
                    _ => $"Hey {input.Name}!",
                },
            }
        );
}
