namespace Trax.Samples.JobHunt.Trains.GetProfile;

public record GetProfileOutput
{
    public required string UserId { get; init; }
    public required string SkillsJson { get; init; }
    public required string EducationJson { get; init; }
    public required string WorkHistoryJson { get; init; }
}
