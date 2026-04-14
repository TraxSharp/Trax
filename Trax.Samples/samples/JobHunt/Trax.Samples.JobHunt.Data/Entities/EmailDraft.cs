namespace Trax.Samples.JobHunt.Data.Entities;

public class EmailDraft
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid ContactId { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public DateTime GeneratedAt { get; set; }
}
