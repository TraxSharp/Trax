namespace Trax.Samples.JobHunt.Trains.CreateApplication;

public record CreateApplicationOutput
{
    public Guid ApplicationId { get; init; }
    public required string Status { get; init; }
}
