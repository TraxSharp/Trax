namespace Trax.Samples.JobHunt.Data.Entities;

public class Contact
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public bool Verified { get; set; }
    public required string Source { get; set; }
    public DateTime CreatedAt { get; set; }
}
