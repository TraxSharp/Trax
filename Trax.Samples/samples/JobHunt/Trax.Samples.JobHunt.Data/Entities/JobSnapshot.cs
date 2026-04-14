namespace Trax.Samples.JobHunt.Data.Entities;

public class JobSnapshot
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public DateTime FetchedAt { get; set; }
    public required string ContentHash { get; set; }
    public required string RawContent { get; set; }
    public string? DiffFromPrevious { get; set; }
}
