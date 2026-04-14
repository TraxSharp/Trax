namespace Trax.Samples.JobHunt.Trains.AddJob;

public record AddJobOutput
{
    public Guid JobId { get; init; }
    public required string UserId { get; init; }
    public required string Title { get; init; }
    public required string Company { get; init; }
}
