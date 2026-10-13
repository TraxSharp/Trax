using System.Text.Json;

namespace Trax.Docs.Tests.Infrastructure;

/// <summary>
/// The packages Trax ships, read from the checkout: what an SDK reference page's
/// <c>package:</c> can name, and the public API baseline each one is checked against.
/// </summary>
internal static class Projects
{
    private static readonly Lazy<IReadOnlyDictionary<string, Package>> Cached = new(Load);

    private static readonly Regex AssemblyName = new(
        @"<AssemblyName>(?<name>[^<]+)</AssemblyName>",
        RegexOptions.Compiled
    );

    /// <summary>A packable project: its csproj, and the baseline of its public API when one exists.</summary>
    public sealed record Package(string Name, string Project, string? Baseline);

    /// <summary>
    /// Every packable library and tool, by project name: a csproj under a <c>src/</c> folder of
    /// a <c>Trax.*</c> folder that does not set <c>IsPackable</c> false or <c>IsTestProject</c>
    /// true. Directory.Build.props leaves IsPackable at the SDK default everywhere, so the
    /// project files are the only place it is turned off. The sample apps never set it and are
    /// not packages, which is why only <c>src/</c> counts.
    /// </summary>
    public static IReadOnlyDictionary<string, Package> Packable => Cached.Value;

    /// <summary>
    /// The npm packages the repository holds, by the <c>name</c> in their <c>package.json</c>:
    /// what a <c>surface: typescript</c> page's <c>package:</c> names.
    /// </summary>
    public static IReadOnlySet<string> NpmPackages()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in Directory.EnumerateDirectories(RepoRoot.Monorepo, "Trax.*"))
        {
            foreach (var file in RepoRoot.FilesUnder(folder, "package.json"))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                if (
                    json.RootElement.TryGetProperty("name", out var name)
                    && name.GetString() is { } n
                )
                    names.Add(n);
            }
        }
        return names;
    }

    private static IReadOnlyDictionary<string, Package> Load()
    {
        var baselines = Baselines();
        var packages = new Dictionary<string, Package>(StringComparer.Ordinal);

        foreach (var folder in Directory.EnumerateDirectories(RepoRoot.Monorepo, "Trax.*"))
        {
            var src = Path.Combine(folder, "src");
            if (!Directory.Exists(src))
                continue;

            foreach (var csproj in RepoRoot.FilesUnder(src, "*.csproj"))
            {
                var text = File.ReadAllText(csproj);
                if (
                    text.Contains(
                        "<IsPackable>false</IsPackable>",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || text.Contains(
                        "<IsTestProject>true</IsTestProject>",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    continue;

                var name = Path.GetFileNameWithoutExtension(csproj);
                // The baseline is named after the assembly, which differs from the package for
                // the CLI (Trax.Cli ships the assembly `trax`).
                var assembly = AssemblyName.Match(text) is { Success: true } m
                    ? m.Groups["name"].Value
                    : name;
                packages[name] = new Package(
                    name,
                    Path.GetRelativePath(RepoRoot.Monorepo, csproj).Replace('\\', '/'),
                    baselines.GetValueOrDefault(assembly)
                );
            }
        }

        return packages;
    }

    /// <summary>Each folder's public API baselines, by the assembly each describes.</summary>
    private static Dictionary<string, string> Baselines()
    {
        var baselines = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var folder in Directory.EnumerateDirectories(RepoRoot.Monorepo, "Trax.*"))
        {
            var tests = Path.Combine(folder, "tests");
            if (!Directory.Exists(tests))
                continue;

            foreach (var project in Directory.EnumerateDirectories(tests))
            {
                var publicApi = Path.Combine(project, "PublicApi");
                if (!Directory.Exists(publicApi))
                    continue;

                foreach (var file in Directory.EnumerateFiles(publicApi, "*.received.txt"))
                    baselines[Path.GetFileName(file)[..^".received.txt".Length]] = file;
            }
        }
        return baselines;
    }
}
