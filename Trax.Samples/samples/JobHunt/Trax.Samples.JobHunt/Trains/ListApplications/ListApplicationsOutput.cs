namespace Trax.Samples.JobHunt.Trains.ListApplications;

public record ApplicationSummary
{
    public Guid Id { get; init; }
    public Guid JobId { get; init; }
    public required string Status { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record ListApplicationsOutput
{
    public required List<ApplicationSummary> Applications { get; init; }
}
