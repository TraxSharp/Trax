namespace Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials;

public record GenerateApplicationMaterialsInput
{
    public Guid JobId { get; init; }
    public required string UserId { get; init; }
    public string? ResumePromptOverride { get; init; }
    public string? CoverLetterPromptOverride { get; init; }
}
