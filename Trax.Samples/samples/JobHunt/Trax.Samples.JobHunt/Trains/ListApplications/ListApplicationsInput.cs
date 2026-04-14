namespace Trax.Samples.JobHunt.Trains.ListApplications;

public record ListApplicationsInput
{
    public required string UserId { get; init; }
}
