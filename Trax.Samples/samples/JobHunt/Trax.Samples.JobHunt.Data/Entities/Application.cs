namespace Trax.Samples.JobHunt.Data.Entities;

public enum ApplicationStatus
{
    Drafted,
    Sent,
    Responded,
    Interviewing,
    Rejected,
    Offered,
    Ghosted,
}

public class Application
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public required string UserId { get; set; }
    public ApplicationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
