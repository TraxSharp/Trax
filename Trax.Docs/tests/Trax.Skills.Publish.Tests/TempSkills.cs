namespace Trax.Skills.Publish.Tests;

/// <summary>A throwaway <c>skills/</c> folder, deleted on dispose.</summary>
internal sealed class TempSkills : IDisposable
{
    public const string Internal = "metadata:\n  internal: true\n";

    private TempSkills()
    {
        Root = Path.Combine(Path.GetTempPath(), "trax-skills-" + Guid.NewGuid().ToString("N"));
        Skills = Path.Combine(Root, "skills");
        License = Path.Combine(Root, "LICENSE");
    }

    /// <summary>An empty <c>skills/</c> folder with a LICENSE beside it.</summary>
    public static async Task<TempSkills> CreateAsync()
    {
        var skills = new TempSkills();
        Directory.CreateDirectory(skills.Skills);
        await File.WriteAllTextAsync(skills.License, "MIT License\n").ConfigureAwait(false);
        return skills;
    }

    public string Root { get; }

    public string Skills { get; }

    public string License { get; }

    public string Out => Path.Combine(Root, "out");

    /// <summary>A skill that passes every rule, unless the caller overrides part of it.</summary>
    public async Task<TempSkills> SkillAsync(
        string folder,
        string? name = null,
        string description = "Use when writing a train in an app built on Trax.",
        string metadata = Internal,
        string body = "# Heading\n\nText.\n"
    )
    {
        var dir = Path.Combine(Skills, folder);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(
                Path.Combine(dir, "SKILL.md"),
                $"---\nname: {name ?? folder}\ndescription: {description}\n{metadata}---\n{body}"
            )
            .ConfigureAwait(false);
        return this;
    }

    public async Task<TempSkills> FileAsync(string relative, string text = "x")
    {
        var path = Path.Combine(Skills, relative);
        Directory.CreateDirectory(ParentOf(path));
        await File.WriteAllTextAsync(path, text).ConfigureAwait(false);
        return this;
    }

    /// <summary>
    /// A file and a folder outside <see cref="Skills"/>, holding what a link into a skill must not publish.
    /// </summary>
    public async Task<(string File, string Folder)> OutsideAsync()
    {
        var folder = Path.Combine(Root, "outside");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "secret.md");
        await File.WriteAllTextAsync(file, "secret").ConfigureAwait(false);
        return (file, folder);
    }

    /// <summary>A symbolic link at <paramref name="relative"/> under <see cref="Skills"/>.</summary>
    public TempSkills Link(string relative, string target, bool folder = false)
    {
        var path = Path.Combine(Skills, relative);
        Directory.CreateDirectory(ParentOf(path));
        if (folder)
            Directory.CreateSymbolicLink(path, target);
        else
            File.CreateSymbolicLink(path, target);
        return this;
    }

    private static string ParentOf(string path) =>
        Path.GetDirectoryName(path)
        ?? throw new ArgumentException($"'{path}' names no file.", nameof(path));

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
