namespace Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials;

/// <summary>
/// Intermediate record passed between junctions in the materials generation pipeline.
/// Grows as each junction adds its output.
/// </summary>
public record MaterialsContext
{
    public required GenerateApplicationMaterialsInput Input { get; init; }
    public required string JobTitle { get; init; }
    public required string JobCompany { get; init; }
    public required string JobDescription { get; init; }
    public required string SkillsJson { get; init; }
    public required string EducationJson { get; init; }
    public required string WorkHistoryJson { get; init; }
    public string? ResumeMarkdown { get; init; }
    public string? ResumeModel { get; init; }
    public string? CoverLetterMarkdown { get; init; }
    public string? CoverLetterModel { get; init; }
}
