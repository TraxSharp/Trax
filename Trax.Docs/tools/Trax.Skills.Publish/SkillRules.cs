namespace Trax.Skills.Publish;

/// <summary>A rule a skill folder breaks, and which folder.</summary>
public sealed record SkillProblem(string Skill, string Message)
{
    public override string ToString() => $"{Skill}: {Message}";
}

/// <summary>
/// The rules a skill under <c>Trax.Docs/skills/</c> is held to before a release publishes it.
/// See <c>Trax.Docs/adr/0056-agent-skills-are-written-in-the-docs-and-published-from-a-release.md</c>.
/// </summary>
public static class SkillRules
{
    /// <summary>The skills CLI's and Claude Code's limit on a skill name.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The Agent Skills limit on a description, which is what an agent matches a task against.</summary>
    public const int MaxDescriptionLength = 1024;

    /// <summary>The scalars a source skill may declare. Anything else is a field one reader ignores and another acts on.</summary>
    public static readonly IReadOnlyList<string> Keys = ["name", "description"];

    /// <summary>The metadata a release writes into each published skill.</summary>
    public const string VersionKey = "trax-version";

    /// <summary>The metadata that keeps a source skill out of <c>npx skills add TraxSharp/Trax</c>.</summary>
    public const string InternalKey = "internal";

    private static readonly Regex Name = new("^[a-z0-9]+(-[a-z0-9]+)*$");
    private static readonly Regex Link = new(@"\]\((?<target>[^)\s]+)\)");

    /// <summary>Every skill folder directly under <paramref name="skillsRoot"/>, by name.</summary>
    public static IReadOnlyList<string> Folders(string skillsRoot) =>
        Directory.Exists(skillsRoot)
            ? [.. Directory.EnumerateDirectories(skillsRoot).Order(StringComparer.Ordinal)]
            : [];

    public static IReadOnlyList<SkillProblem> Check(string skillsRoot)
    {
        var problems = new List<SkillProblem>();
        if (Directory.Exists(skillsRoot))
            problems.AddRange(
                Directory
                    .EnumerateFiles(skillsRoot)
                    .Select(f => new SkillProblem(
                        Path.GetFileName(f),
                        "a file directly under skills/ is not in any skill; put it in a skill's folder"
                    ))
            );
        foreach (var folder in Folders(skillsRoot))
            problems.AddRange(CheckOne(folder));
        return problems;
    }

    public static IReadOnlyList<SkillProblem> CheckOne(string folder)
    {
        var skill = Path.GetFileName(folder);
        var problems = new List<SkillProblem>();
        void Add(string message) => problems.Add(new SkillProblem(skill, message));

        if (IsLink(new DirectoryInfo(folder)))
        {
            Add(
                "the skill's folder is a symbolic link. A published skill ships its own files, "
                    + "never what a link points at"
            );
            return problems;
        }

        foreach (var entry in Entries(folder))
        {
            var relative = Path.GetRelativePath(folder, entry.FullName);
            if (IsLink(entry))
                Add(
                    $"'{relative}' is a symbolic link. A published skill ships its own files, "
                        + "never what a link points at"
                );
            else if (entry is FileInfo && !entry.Name.EndsWith(".md", StringComparison.Ordinal))
                Add(
                    $"'{relative}' is not markdown. A published skill ships "
                        + "instructions only, never a script an agent could be told to run"
                );
        }

        var path = Path.Combine(folder, "SKILL.md");
        if (!File.Exists(path))
        {
            Add("there is no SKILL.md");
            return problems;
        }

        var text = File.ReadAllText(path);
        var front = SkillFrontmatter.Parse(text);
        if (front.Error is not null)
        {
            Add($"SKILL.md frontmatter: {front.Error}");
            return problems;
        }

        foreach (var key in front.Keys.Where(k => !Keys.Contains(k)))
            Add($"frontmatter key '{key}' is not one of: {string.Join(", ", Keys)}, metadata");

        var name = front.Scalar("name");
        if (name is null)
            Add("frontmatter has no 'name'");
        else if (name != skill)
            Add($"'name' is '{name}' but the folder is '{skill}'; the skills CLI installs by name");
        else if (!Name.IsMatch(name) || name.Length > MaxNameLength)
            Add(
                $"'name' must be lowercase words joined by hyphens, at most {MaxNameLength} characters"
            );

        var description = front.Scalar("description");
        if (string.IsNullOrWhiteSpace(description))
            Add("frontmatter has no 'description'; it is what an agent matches a task against");
        else if (description.Length > MaxDescriptionLength)
            Add(
                $"'description' is {description.Length} characters; the limit is {MaxDescriptionLength}"
            );
        else if (description.Contains('<') || description.Contains('>'))
            Add("'description' contains '<' or '>', which Claude Code rejects");

        if (front.Metadata.GetValueOrDefault(InternalKey) != "true")
            Add(
                $"metadata has no '{InternalKey}: true'. Without it `npx skills add TraxSharp/Trax` "
                    + "offers this skill from main, before any release ships it"
            );
        if (front.Metadata.ContainsKey(VersionKey))
            Add($"metadata carries '{VersionKey}'; the release writes it");
        foreach (var key in front.Metadata.Keys.Where(k => k != InternalKey && k != VersionKey))
            Add($"metadata key '{key}' is not one the release knows");

        foreach (var target in RelativeLinks(text))
        {
            var full = Path.GetFullPath(Path.Combine(folder, target));
            if (
                !full.StartsWith(
                    Path.GetFullPath(folder) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal
                )
            )
                Add($"link '{target}' leaves the skill's folder, which is all that is published");
            else if (!File.Exists(full))
                Add($"link '{target}' names a file that does not exist");
        }

        return problems;
    }

    /// <summary>
    /// Every file and folder under <paramref name="folder"/>, links included. A linked folder is listed but never
    /// entered, so nothing outside the skill is reached through it.
    /// </summary>
    public static IEnumerable<FileSystemInfo> Entries(string folder)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = 0,
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
        };
        foreach (
            var entry in new DirectoryInfo(folder)
                .EnumerateFileSystemInfos("*", options)
                .OrderBy(e => e.Name, StringComparer.Ordinal)
        )
        {
            yield return entry;
            if (entry is DirectoryInfo dir && !IsLink(dir))
                foreach (var inner in Entries(dir.FullName))
                    yield return inner;
        }
    }

    /// <summary>Every file under <paramref name="folder"/> that is not a link and is not reached through one.</summary>
    public static IEnumerable<string> Files(string folder) =>
        Entries(folder).Where(e => e is FileInfo && !IsLink(e)).Select(e => e.FullName);

    /// <summary>A symbolic link or other reparse point, to a file or a folder.</summary>
    public static bool IsLink(FileSystemInfo entry) =>
        entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

    /// <summary>Every link target that is neither absolute nor a bare anchor, without its anchor.</summary>
    public static IEnumerable<string> RelativeLinks(string markdown) =>
        Links(markdown)
            .Where(t =>
                !t.StartsWith('#')
                && !t.Contains("://", StringComparison.Ordinal)
                && !t.StartsWith("mailto:", StringComparison.Ordinal)
            )
            .Select(t => t.Split('#')[0]);

    /// <summary>Every markdown link target in <paramref name="markdown"/>, outside code fences.</summary>
    public static IEnumerable<string> Links(string markdown)
    {
        var inFence = false;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                inFence = !inFence;
            else if (!inFence)
                foreach (Match m in Link.Matches(line))
                    yield return m.Groups["target"].Value;
        }
    }
}
