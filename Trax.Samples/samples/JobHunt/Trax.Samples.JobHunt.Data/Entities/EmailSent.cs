namespace Trax.Samples.JobHunt.Data.Entities;

public class EmailSent
{
    public Guid Id { get; set; }
    public Guid DraftId { get; set; }
    public DateTime SentAt { get; set; }
    public required string Provider { get; set; }
    public required string MessageId { get; set; }
}
