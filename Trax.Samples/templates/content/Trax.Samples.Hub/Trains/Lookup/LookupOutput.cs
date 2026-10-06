namespace Trax.Samples.Hub.Trains.Lookup;

public record LookupOutput
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DateTime CreatedAt { get; init; }
}
