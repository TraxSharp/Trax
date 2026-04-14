namespace Trax.Samples.JobHunt.Trains.GetArtifacts;

public record GetArtifactsInput
{
    public Guid JobId { get; init; }
    public required string UserId { get; init; }
}
