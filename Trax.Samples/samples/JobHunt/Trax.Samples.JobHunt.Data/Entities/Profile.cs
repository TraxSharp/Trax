namespace Trax.Samples.JobHunt.Data.Entities;

public class Profile
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public string SkillsJson { get; set; } = "[]";
    public string EducationJson { get; set; } = "[]";
    public string WorkHistoryJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; }
}
