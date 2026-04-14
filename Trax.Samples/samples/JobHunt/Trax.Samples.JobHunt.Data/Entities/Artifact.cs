namespace Trax.Samples.JobHunt.Data.Entities;

public enum ArtifactType
{
    Resume,
    CoverLetter,
}

public class Artifact
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public required string UserId { get; set; }
    public ArtifactType Type { get; set; }
    public required string Content { get; set; }
    public required string ModelUsed { get; set; }
    public DateTime GeneratedAt { get; set; }
}
