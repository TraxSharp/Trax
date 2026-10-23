namespace Trax.Skills.Publish.Tests;

/// <summary>A throwaway <c>skills/</c> folder, deleted on dispose.</summary>
internal sealed class TempSkills : IDisposable
{
    public const string Internal = "metadata:\n  internal: true\n";

    public TempSkills()
    {
        Root = Path.Combine(Path.GetTempPath(), "trax-skills-" + Guid.NewGuid().ToString("N"));
        Skills = Path.Combine(Root, "skills");
        Directory.CreateDirectory(Skills);
        License = Path.Combine(Root, "LICENSE");
        File.WriteAllText(License, "MIT License\n");
    }

    public string Root { get; }

    public string Skills { get; }

    public string License { get; }

    public string Out => Path.Combine(Root, "out");

    /// <summary>A skill that passes every rule, unless the caller overrides part of it.</summary>
    public TempSkills Skill(
        string folder,
        string? name = null,
        string description = "Use when writing a train in an app built on Trax.",
        string metadata = Internal,
        string body = "# Heading\n\nText.\n"
    )
    {
        var dir = Path.Combine(Skills, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "SKILL.md"),
            $"---\nname: {name ?? folder}\ndescription: {description}\n{metadata}---\n{body}"
        );
        return this;
    }

    public TempSkills File_(string relative, string text = "x")
    {
        var path = Path.Combine(Skills, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return this;
    }

    /// <summary>
    /// A file and a folder outside <see cref="Skills"/>, holding what a link into a skill must not publish.
    /// </summary>
    public (string File, string Folder) Outside()
    {
        var folder = Path.Combine(Root, "outside");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "secret.md");
        File.WriteAllText(file, "secret");
        return (file, folder);
    }

    /// <summary>A symbolic link at <paramref name="relative"/> under <see cref="Skills"/>.</summary>
    public TempSkills Link(string relative, string target, bool folder = false)
    {
        var path = Path.Combine(Skills, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (folder)
            Directory.CreateSymbolicLink(path, target);
        else
            File.CreateSymbolicLink(path, target);
        return this;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
