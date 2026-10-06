using System.IO.Compression;
using System.Security;
using System.Xml.Linq;
using static Trax.Samples.Templates.Tests.Utils.Dotnet;

namespace Trax.Samples.Templates.Tests.Utils;

/// <summary>
/// The packages a scaffold restores: the template package and every Trax package it pins, packed
/// from this repository at one throwaway version, in a folder feed a scaffold's
/// <c>nuget.config</c> points at.
///
/// <para>
/// A packed template pins every Trax package at the version it was packed with
/// (Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md), so a scaffold
/// can only restore if those exact packages exist. Packing them here tests the templates against
/// the code in the same commit, which is also what the release ships. The version is new on every
/// run, so the global packages folder never serves a stale build under it.
/// </para>
///
/// <para>
/// The Trax packages are built under their own configuration, <c>ScaffoldTest</c>, so packing
/// them at another version does not rebuild the outputs the rest of the run is testing.
/// </para>
/// </summary>
internal static class ScaffoldFeed
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Packed? _packed;

    public sealed record Packed(string Root, string Feed, string TemplatesNupkg, string Version);

    /// <summary>Packs everything once per test run and returns where it is.</summary>
    public static async Task<Packed> Get()
    {
        await Gate.WaitAsync();
        try
        {
            return _packed ??= await Pack();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Writes a <c>nuget.config</c> into <paramref name="directory"/> that resolves Trax packages
    /// from the feed and everything else from nuget.org, as a consumer with the packages
    /// published would.
    /// </summary>
    public static void WriteNuGetConfig(string directory, Packed packed) =>
        File.WriteAllText(
            Path.Combine(directory, "nuget.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="scaffold" value="{SecurityElement.Escape(packed.Feed)}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <clear />
                <packageSource key="scaffold">
                  <package pattern="Trax.*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """
        );

    /// <summary>
    /// Deletes the feed, and the packages a run restored into the global packages folder under
    /// its throwaway version, which nothing will ask for again.
    /// </summary>
    public static void Delete()
    {
        if (_packed is null)
            return;

        if (Directory.Exists(_packed.Root))
            Directory.Delete(_packed.Root, recursive: true);

        var globalPackages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages"
            );
        if (!Directory.Exists(globalPackages))
            return;
        foreach (var package in Directory.EnumerateDirectories(globalPackages, "trax.*"))
        {
            var restored = Path.Combine(package, _packed.Version.ToLowerInvariant());
            if (Directory.Exists(restored))
                Directory.Delete(restored, recursive: true);
        }
    }

    private static async Task<Packed> Pack()
    {
        var version = $"0.0.0-scaffold.{DateTime.UtcNow:yyyyMMddHHmmss}";
        var root = Path.Combine(Path.GetTempPath(), "trax-scaffold-feed-" + Guid.NewGuid());
        var feed = Path.Combine(root, "feed");
        Directory.CreateDirectory(feed);
        try
        {
            return await PackInto(root, feed, version);
        }
        catch
        {
            // Delete() only knows about a feed that finished packing.
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    private static async Task<Packed> PackInto(string root, string feed, string version)
    {
        var templatesProject = Path.Combine(
            RepoRoot(),
            "templates",
            "Trax.Samples.Templates.csproj"
        );
        await Run(root, "pack", templatesProject, "--output", root, $"-p:Version={version}");
        var templatesNupkg = Directory.GetFiles(root, "*.nupkg").Single();

        // The Trax packages the templates pin are exactly the ones a scaffold can resolve, and
        // every one of them is in the restore graph of the template projects, so each is
        // already restored.
        var projects = PinnedTraxPackages(templatesNupkg).Select(id => TraxProject(id)).ToList();

        var traversal = Path.Combine(root, "pack-trax.proj");
        File.WriteAllText(
            traversal,
            new XDocument(
                new XElement(
                    "Project",
                    new XElement(
                        "ItemGroup",
                        projects.Select(p => new XElement(
                            "TraxPackage",
                            new XAttribute("Include", p)
                        ))
                    ),
                    new XElement(
                        "Target",
                        new XAttribute("Name", "PackTrax"),
                        new XElement(
                            "MSBuild",
                            new XAttribute("Projects", "@(TraxPackage)"),
                            new XAttribute("Targets", "Pack"),
                            new XAttribute("BuildInParallel", "true"),
                            new XAttribute(
                                "Properties",
                                $"Configuration=ScaffoldTest;Version={version};"
                                    + $"PackageOutputPath={feed};EnablePackageValidation=false"
                            )
                        )
                    )
                )
            ).ToString()
        );
        await Run(
            root,
            "msbuild",
            traversal,
            "-t:PackTrax",
            "-m",
            "-nodeReuse:false",
            "-nologo",
            "-v:minimal"
        );

        return new Packed(root, feed, templatesNupkg, version);
    }

    /// <summary>
    /// The Trax package ids the packed templates pin, read from each template's
    /// Directory.Packages.props inside the package just packed.
    /// </summary>
    private static List<string> PinnedTraxPackages(string templatesNupkg)
    {
        using var zip = ZipFile.OpenRead(templatesNupkg);
        return zip
            .Entries.Where(e => e.Name == "Directory.Packages.props")
            .SelectMany(e =>
            {
                using var stream = e.Open();
                return XDocument.Load(stream).Descendants("PackageVersion").ToList();
            })
            .Select(e => e.Attribute("Include")!.Value)
            .Where(id => id.StartsWith("Trax.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string TraxProject(string id)
    {
        var monorepo = Path.GetDirectoryName(RepoRoot())!;
        return Directory
                .EnumerateDirectories(monorepo, "Trax.*")
                .Select(folder => Path.Combine(folder, "src", id, id + ".csproj"))
                .SingleOrDefault(File.Exists)
            ?? throw new InvalidOperationException(
                $"The templates pin {id}, but no folder in the repository has src/{id}/{id}.csproj."
            );
    }
}
