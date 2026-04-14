namespace Trax.Samples.JobHunt.Trains.FindContact;

public record FindContactOutput
{
    public Guid ContactId { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string Source { get; init; }
}
