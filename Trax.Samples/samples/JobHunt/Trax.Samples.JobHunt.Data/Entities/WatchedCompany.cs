namespace Trax.Samples.JobHunt.Data.Entities;

public class WatchedCompany
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string CompanyName { get; set; }
    public required string CareersUrl { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public string? LastFingerprint { get; set; }
    public DateTime CreatedAt { get; set; }
}
