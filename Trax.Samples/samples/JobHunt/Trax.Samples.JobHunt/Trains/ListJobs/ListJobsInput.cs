using Trax.Samples.JobHunt.Data.Entities;

namespace Trax.Samples.JobHunt.Trains.ListJobs;

public record ListJobsInput
{
    public required string UserId { get; init; }
    public JobStatus? Status { get; init; }
}
