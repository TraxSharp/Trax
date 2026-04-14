namespace Trax.Samples.JobHunt.Trains.CreateApplication;

public record CreateApplicationInput
{
    public Guid JobId { get; init; }
    public required string UserId { get; init; }
}
