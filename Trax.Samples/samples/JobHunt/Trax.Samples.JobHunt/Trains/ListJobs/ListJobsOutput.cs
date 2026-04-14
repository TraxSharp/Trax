namespace Trax.Samples.JobHunt.Trains.ListJobs;

public record JobSummary
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Company { get; init; }
    public string? Url { get; init; }
    public required string Status { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record ListJobsOutput
{
    public required List<JobSummary> Jobs { get; init; }
}
