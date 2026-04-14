namespace Trax.Samples.JobHunt.Data.Entities;

public enum JobStatus
{
    Active,
    Closed,
    Archived,
}

public class Job
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string Title { get; set; }
    public required string Company { get; set; }
    public string? Url { get; set; }
    public required string RawDescription { get; set; }
    public JobStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
