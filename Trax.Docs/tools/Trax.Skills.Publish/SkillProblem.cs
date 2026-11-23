namespace Trax.Skills.Publish;

/// <summary>A rule a skill folder breaks, and which folder.</summary>
public sealed record SkillProblem(string Skill, string Message)
{
    public override string ToString() => $"{Skill}: {Message}";
}
