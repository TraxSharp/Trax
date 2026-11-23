using System.Text;
using System.Text.Json;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.Meta.Infrastructure;

/// <summary>
/// Writes the trax tool's third-party notices from what the tool ships: every package in <c>trax.deps.json</c> with
/// runtime assemblies, read from its nuspec in the NuGet packages folder. Each package is listed with its licence and
/// copyright, each licence's text follows once, and an Apache-2.0 package's NOTICE file is copied in whole.
/// </summary>
internal static class ThirdPartyNotices
{
    /// <summary>The committed file, packed at the root of the Trax.Cli package.</summary>
    public static string CommittedPath =>
        RepoRoot.Combine("src", "Trax.Cli", "THIRD-PARTY-NOTICES.txt");

    private static string LicenseDir =>
        RepoRoot.Combine("tests", "Trax.Cli.Tests.Meta", "Licenses");

    /// <summary>A package the tool ships, as its nuspec describes it.</summary>
    internal sealed record Package(
        string Id,
        string Directory,
        string? Expression,
        string? LicenseFile,
        string? LicenseUrl,
        string? Copyright,
        string? ProjectUrl
    );

    public static async Task<string> GenerateAsync()
    {
        var packages = await ShippedAsync().ConfigureAwait(false);
        var text = new StringBuilder();
        text.Append(
            """
            THIRD-PARTY NOTICES

            The trax tool (the Trax.Cli package) is MIT licensed. It ships the assemblies of the packages below, each
            under its own licence: their licence, copyright and project are listed first, then each licence's text
            once, then any NOTICE file a package carries.

            This file is generated from the tool's runtime dependencies by ThirdPartyNoticesTests in
            Trax.Cli/tests/Trax.Cli.Tests.Meta, which rewrites it when they change. Do not edit it by hand.


            PACKAGES
            ========

            """
        );
        AppendPackages(text, packages);
        await AppendLicencesAsync(text, packages).ConfigureAwait(false);
        await AppendNoticesAsync(text, packages).ConfigureAwait(false);
        return text.ToString();
    }

    private static void AppendPackages(StringBuilder text, IReadOnlyList<Package> packages)
    {
        foreach (var p in packages)
        {
            text.Append('\n').Append(p.Id).Append('\n');
            text.Append("  Licence:   ").Append(LicenceLine(p)).Append('\n');
            if (p.Copyright is { Length: > 0 } copyright)
                text.Append("  Copyright: ").Append(copyright).Append('\n');
            if (p.ProjectUrl is { Length: > 0 } url)
                text.Append("  Project:   ").Append(url).Append('\n');
        }
    }

    private static async Task AppendLicencesAsync(
        StringBuilder text,
        IReadOnlyList<Package> packages
    )
    {
        text.Append("\n\nLICENCE TEXTS\n=============\n");
        foreach (
            var id in packages
                .SelectMany(p => LicenceIds(p.Expression))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        )
        {
            text.Append("\n---- ").Append(id).Append(" ----\n\n");
            text.Append(await LicenceTextAsync(id).ConfigureAwait(false));
        }
        foreach (var p in packages.Where(p => p.LicenseFile is not null))
        {
            text.Append("\n---- ")
                .Append(p.Id)
                .Append(": ")
                .Append(p.LicenseFile)
                .Append(" ----\n\n");
            text.Append(
                Normalize(
                    await File.ReadAllTextAsync(Path.Combine(p.Directory, p.LicenseFile.NotNull()))
                        .ConfigureAwait(false)
                )
            );
        }
    }

    /// <summary>Apache-2.0 asks a redistributor to carry the NOTICE file a package ships, so each is copied whole.</summary>
    private static async Task AppendNoticesAsync(
        StringBuilder text,
        IReadOnlyList<Package> packages
    )
    {
        var notices = packages
            .Where(p => LicenceIds(p.Expression).Contains("Apache-2.0", StringComparer.Ordinal))
            .SelectMany(p =>
                System
                    .IO.Directory.EnumerateFiles(p.Directory)
                    .Where(f =>
                        Path.GetFileName(f).StartsWith("NOTICE", StringComparison.OrdinalIgnoreCase)
                    )
                    .Order(StringComparer.Ordinal)
                    .Select(f => (p.Id, File: f))
            )
            .ToList();
        if (notices.Count > 0)
        {
            text.Append("\n\nNOTICE FILES\n============\n");
            foreach (var (id, file) in notices)
            {
                text.Append("\n---- ")
                    .Append(id)
                    .Append(": ")
                    .Append(Path.GetFileName(file))
                    .Append(" ----\n\n");
                text.Append(Normalize(await File.ReadAllTextAsync(file).ConfigureAwait(false)));
            }
        }
    }

    private static string LicenceLine(Package p) =>
        p.Expression
        ?? (
            p.LicenseFile is not null
                ? $"see \"{p.Id}: {p.LicenseFile}\" below"
                : $"see {p.LicenseUrl}"
        );

    /// <summary>The licence ids in an SPDX expression (<c>MIT OR Apache-2.0</c> gives both).</summary>
    internal static IEnumerable<string> LicenceIds(string? expression) =>
        expression is null
            ? []
            : expression
                .Split([' ', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t is not ("OR" or "AND" or "WITH"));

    /// <summary>
    /// The licence's text from <c>Licenses/</c>, with the template's copyright placeholders pointed at the holders
    /// each package names above.
    /// </summary>
    private static async Task<string> LicenceTextAsync(string id)
    {
        var path = Path.Combine(LicenseDir, $"{id}.txt");
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"A package the trax tool ships is under {id}, and {RepoRoot.Relative(path)} has no text for it. "
                    + $"Add the licence's text there ('gh api licenses/<key> -q .body' gives GitHub's copy)."
            );
        var lines = Normalize(await File.ReadAllTextAsync(path).ConfigureAwait(false)).Split('\n');
        return string.Join(
            '\n',
            lines.Select(l =>
                l.StartsWith("Copyright (c) [year]", StringComparison.Ordinal)
                    ? "Copyright (c) the holders named above for each package under this licence."
                    : l.Replace("[fullname]", "THE COPYRIGHT HOLDER", StringComparison.Ordinal)
            )
        );
    }

    /// <summary>Every package in the tool's runtime closure that ships an assembly, read from its nuspec.</summary>
    internal static async Task<IReadOnlyList<Package>> ShippedAsync()
    {
        using var deps = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "trax.deps.json"))
                .ConfigureAwait(false)
        );
        var root = deps.RootElement;
        var target = root.GetProperty("runtimeTarget").GetProperty("name").GetString().NotNull();
        var libraries = root.GetProperty("libraries");
        var folder = await PackagesFolderAsync().ConfigureAwait(false);
        var packages = new List<Package>();
        foreach (var entry in root.GetProperty("targets").GetProperty(target).EnumerateObject())
        {
            var library = libraries.GetProperty(entry.Name);
            if (
                !string.Equals(
                    library.GetProperty("type").GetString(),
                    "package",
                    StringComparison.Ordinal
                )
                || !entry.Value.TryGetProperty("runtime", out var runtime)
                || !runtime.EnumerateObject().Any()
            )
                continue;
            var id = entry.Name[..entry.Name.IndexOf('/', StringComparison.Ordinal)];
            var dir = Path.Combine(folder, library.GetProperty("path").GetString().NotNull());
            packages.Add(await ReadAsync(id, dir).ConfigureAwait(false));
        }
        return [.. packages.OrderBy(p => p.Id, StringComparer.Ordinal)];
    }

    private static async Task<Package> ReadAsync(string id, string dir)
    {
        var nuspec =
            System.IO.Directory.EnumerateFiles(dir, "*.nuspec").SingleOrDefault()
            ?? throw new InvalidOperationException($"{dir} holds no nuspec for {id}.");
        var xml = XDocument.Parse(await File.ReadAllTextAsync(nuspec).ConfigureAwait(false));
        var metadata = xml
            .Root.NotNull()
            .Elements()
            .Single(e => string.Equals(e.Name.LocalName, "metadata", StringComparison.Ordinal));
        string? Field(string name) =>
            metadata
                .Elements()
                .FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, name, StringComparison.Ordinal)
                )
                ?.Value.Trim()
                is { Length: > 0 } v
                ? v
                : null;
        var license = metadata
            .Elements()
            .FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "license", StringComparison.Ordinal)
            );
        var type = license?.Attribute("type")?.Value;
        return new Package(
            id,
            dir,
            string.Equals(type, "expression", StringComparison.Ordinal)
                ? license?.Value.Trim()
                : null,
            string.Equals(type, "file", StringComparison.Ordinal) ? license?.Value.Trim() : null,
            license is null ? Field("licenseUrl") : null,
            Field("copyright"),
            Field("projectUrl")
        );
    }

    /// <summary>The NuGet packages folder the tool was restored from, as its assets file records it.</summary>
    private static async Task<string> PackagesFolderAsync()
    {
        var assets = RepoRoot.Combine("src", "Trax.Cli", "obj", "project.assets.json");
        using var doc = JsonDocument.Parse(
            await File.ReadAllTextAsync(assets).ConfigureAwait(false)
        );
        return doc.RootElement.GetProperty("packageFolders").EnumerateObject().First().Name;
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
}
