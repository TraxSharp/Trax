using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Skills.Publish;

/// <summary>
/// Writes the tree the <c>Skills</c> workflow commits to the <c>skills</c> branch of <c>TraxSharp/Trax</c>: every
/// skill with its source marker removed and the release's version stamped in, the Claude Code
/// plugin manifest at that version, a README saying where the source is, and the licence. The
/// marketplace that points Claude Code at the branch is <see cref="MarketplaceManifest"/>, kept at
/// the root of <c>main</c>.
/// </summary>
public static partial class Stager
{
    public const string Marketplace = "traxsharp";
    public const string Plugin = "trax";
    public const string Repository = "TraxSharp/Trax";
    public const string Branch = "skills";

    /// <summary>The tag on the branch for a release: <c>v*</c> is the packages' and semantic-release's.</summary>
    public const string TagPrefix = "skills-v";

    public const string Source = "https://github.com/TraxSharp/Trax/tree/main/Trax.Docs/skills";

    private const string Description = "Agent skills for building apps on Trax .NET";

    [GeneratedRegex(
        @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Version { get; }
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>
    /// Stages the skills under <paramref name="skillsRoot"/> into <paramref name="outDir"/>,
    /// which must be empty or absent. Returns the problems that stopped it, empty on success.
    /// </summary>
    public static async Task<IReadOnlyList<SkillProblem>> StageAsync(
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
            // Check refused any link, so this never follows one; Files skips them all the same.
            foreach (var file in SkillRules.Files(folder))
            {
                var dest = Path.Combine(target, Path.GetRelativePath(folder, file));
                Directory.CreateDirectory(
                    Path.GetDirectoryName(dest)
                        ?? throw new InvalidOperationException($"'{dest}' has no directory")
                );
                await File.WriteAllTextAsync(
                        dest,
                        await File.ReadAllTextAsync(file).ConfigureAwait(false)
                    )
                    .ConfigureAwait(false);
            }

            var front = SkillFrontmatter.Parse(
                await File.ReadAllTextAsync(Path.Combine(folder, "SKILL.md")).ConfigureAwait(false)
            );
            await File.WriteAllTextAsync(
                    Path.Combine(target, "SKILL.md"),
                    front.Render(SkillRules.Keys, [new(SkillRules.VersionKey, version)])
                )
                .ConfigureAwait(false);
        }

        var manifests = Path.Combine(outDir, ".claude-plugin");
        Directory.CreateDirectory(manifests);
        await WriteAsync(Path.Combine(manifests, "plugin.json"), PluginManifest(version))
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDir, "README.md"), Readme(version, folders))
            .ConfigureAwait(false);
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
            ["repository"] = $"https://github.com/{Repository}",
            ["license"] = "MIT",
        };

    /// <summary>
    /// The marketplace at <c>.claude-plugin/marketplace.json</c> on <c>main</c>. It carries no
    /// version: the plugin's version is the <c>plugin.json</c> on the branch it points at.
    /// </summary>
    public static JsonObject MarketplaceManifest() =>
        new()
        {
            ["name"] = Marketplace,
            ["owner"] = new JsonObject
            {
                ["name"] = "TraxSharp",
                ["url"] = "https://github.com/TraxSharp",
            },
            ["metadata"] = new JsonObject { ["description"] = Description },
            ["plugins"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = Plugin,
                    ["description"] = Description,
                    ["source"] = new JsonObject
                    {
                        ["source"] = "github",
                        ["repo"] = Repository,
                        ["ref"] = Branch,
                    },
                }
            ),
        };

    /// <summary>The marketplace as the file on <c>main</c> holds it, newline included.</summary>
    public static string MarketplaceJson() => MarketplaceManifest().ToJsonString(Indented) + "\n";

    private static string Readme(string version, IReadOnlyList<string> folders) =>
        $"""
            # Trax .NET agent skills

            Skills for coding agents working in an app built on [Trax .NET](https://traxsharp.net),
            as released in Trax {version}.

            **This branch is generated.** After each Trax release, an approved run of the Skills workflow
            writes it from [`Trax.Docs/skills/`]({Source}) and tags it `{TagPrefix}{version}`. Change the
            skills there, on `main`.

            ## Install

            Claude Code:

            ```
            /plugin marketplace add {Repository}
            /plugin install {Plugin}@{Marketplace}
            ```

            Any agent the skills CLI supports:

            ```
            npx skills add https://github.com/{Repository}/tree/{Branch}
            ```

            To stay on one release, install from its tag:
            `npx skills add https://github.com/{Repository}/tree/{TagPrefix}{version}`.

            ## Skills

            {string.Join('\n', folders.Select(f => $"- `{Path.GetFileName(f)}`"))}

            Each skill's `metadata.trax-version` names the release it matches.

            ## Licence

            MIT, see [LICENSE](LICENSE).

            """;

    private static Task WriteAsync(string path, JsonNode node) =>
        File.WriteAllTextAsync(path, node.ToJsonString(Indented) + "\n");
}
