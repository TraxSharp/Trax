namespace Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials;

public record GenerateApplicationMaterialsOutput
{
    public Guid ResumeArtifactId { get; init; }
    public Guid CoverLetterArtifactId { get; init; }
    public required string ResumeMarkdown { get; init; }
    public required string CoverLetterMarkdown { get; init; }
}
