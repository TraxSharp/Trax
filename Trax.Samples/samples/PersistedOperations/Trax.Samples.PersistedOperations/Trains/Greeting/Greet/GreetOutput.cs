namespace Trax.Samples.PersistedOperations.Trains.Greeting.Greet;

/// <summary>
/// Output from the greet train.
/// </summary>
public record GreetOutput
{
    public required string Greeting { get; init; }
    public required DateTimeOffset GreetedAt { get; init; }
}
