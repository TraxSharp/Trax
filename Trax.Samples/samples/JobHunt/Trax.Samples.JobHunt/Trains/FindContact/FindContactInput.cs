namespace Trax.Samples.JobHunt.Trains.FindContact;

public record FindContactInput
{
    public Guid JobId { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
}
