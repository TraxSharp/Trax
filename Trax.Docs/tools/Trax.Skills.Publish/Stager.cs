using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Skills.Publish;

/// <summary>
/// Writes the tree a release commits to <c>TraxSharp/skills</c>: every skill with its source
/// marker removed and the release's version stamped in, a Claude Code plugin and marketplace
/// manifest at the root, a README saying where the source is, and the licence.
/// </summary>
public static class Stager
{
    public const string Marketplace = "traxsharp";
    public const string Plugin = "trax";
    public const string Repository = "https://github.com/TraxSharp/skills";
    public const string Source = "https://github.com/TraxSharp/Trax/tree/main/Trax.Docs/skills";

    private const string Description = "Agent skills for building apps on Trax .NET";
    private static readonly Regex Version = new(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$");
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Stages the skills under <paramref name="skillsRoot"/> into <paramref name="outDir"/>,
    /// which must be empty or absent. Returns the problems that stopped it, empty on success.
    /// </summary>
    public static IReadOnlyList<SkillProblem> Stage(
        string skillsRoot,
        string version,
        string licenseFile,
        string outDir
    )
    {
        if (!Version.IsMatch(version))
            return [new("release", $"'{version}' is not a release version")];
        if (!File.Exists(licenseFile))
            return [new("release", $"licence '{licenseFile}' does not exist")];
        if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any())
            return [new("release", $"'{outDir}' is not empty; a stage writes a whole tree")];

        var folders = SkillRules.Folders(skillsRoot);
        if (folders.Count == 0)
            return [new("release", $"there are no skills under '{skillsRoot}'")];
        var problems = SkillRules.Check(skillsRoot);
        if (problems.Count > 0)
            return problems;

        foreach (var folder in folders)
        {
            var target = Path.Combine(outDir, "skills", Path.GetFileName(folder));
            foreach (
                var file in Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories)
            )
            {
                var dest = Path.Combine(target, Path.GetRelativePath(folder, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllText(dest, File.ReadAllText(file));
            }

            var front = SkillFrontmatter.Parse(File.ReadAllText(Path.Combine(folder, "SKILL.md")));
            File.WriteAllText(
                Path.Combine(target, "SKILL.md"),
                front.Render(SkillRules.Keys, [new(SkillRules.VersionKey, version)])
            );
        }

        var manifests = Path.Combine(outDir, ".claude-plugin");
        Directory.CreateDirectory(manifests);
        Write(Path.Combine(manifests, "plugin.json"), PluginManifest(version));
        Write(Path.Combine(manifests, "marketplace.json"), MarketplaceManifest(version));
        File.WriteAllText(Path.Combine(outDir, "README.md"), Readme(version, folders));
        File.Copy(licenseFile, Path.Combine(outDir, "LICENSE"));
        return [];
    }

    public static JsonObject PluginManifest(string version) =>
        new()
        {
            ["name"] = Plugin,
            ["version"] = version,
            ["description"] = Description,
            ["author"] = new JsonObject
            {
                ["name"] = "TraxSharp",
                ["url"] = "https://github.com/TraxSharp",
            },
            ["homepage"] = "https://traxsharp.net",
            ["repository"] = Repository,
            ["license"] = "MIT",
        };

    public static JsonObject MarketplaceManifest(string version) =>
        new()
        {
            ["name"] = Marketplace,
            ["owner"] = new JsonObject
            {
                ["name"] = "TraxSharp",
                ["url"] = "https://github.com/TraxSharp",
            },
            ["metadata"] = new JsonObject { ["description"] = Description, ["version"] = version },
            ["plugins"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = Plugin,
                    ["source"] = "./",
                    ["description"] = Description,
                    ["version"] = version,
                }
            ),
        };

    private static string Readme(string version, IReadOnlyList<string> folders) =>
        $"""
            # Trax .NET agent skills

            Skills for coding agents working in an app built on [Trax .NET](https://traxsharp.net),
            as released in Trax {version}.

            **This repository is generated.** Each Trax release writes it from
            [`Trax.Docs/skills/`]({Source}) and tags it `v{version}`. Pull requests and issues go to
            [TraxSharp/Trax](https://github.com/TraxSharp/Trax).

            ## Install

            Claude Code:

            ```
            /plugin marketplace add TraxSharp/skills
            /plugin install {Plugin}@{Marketplace}
            ```

            Any agent the skills CLI supports:

            ```
            npx skills add TraxSharp/skills
            ```

            To stay on one release, install from its tag:
            `npx skills add https://github.com/TraxSharp/skills/tree/v{version}/skills/<name>`.

            ## Skills

            {string.Join("\n", folders.Select(f => $"- `{Path.GetFileName(f)}`"))}

            Each skill's `metadata.trax-version` names the release it matches.

            ## Licence

            MIT, see [LICENSE](LICENSE).

            """;

    private static void Write(string path, JsonNode node) =>
        File.WriteAllText(path, node.ToJsonString(Indented) + "\n");
}
