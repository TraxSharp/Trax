namespace Trax.Samples.Hub.Trains.Lookup;

public record LookupInput
{
    /// <summary>
    /// The ID of the record to look up.
    /// </summary>
    public required string Id { get; init; }
}
