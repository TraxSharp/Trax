using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Trax.Analyzers.Tests.Harness;

/// <summary>
/// The packages the copied harness reads analyzers, source generators and references from, outside
/// what Microsoft.CodeAnalysis.Testing resolves itself. Upstream downloads a package the NuGet
/// cache does not hold; this one only reads the cache (the global packages folder and the fallback
/// folders), so a test never reaches the network. The restore puts every package the tests use in
/// the cache: the projects under tests/Trax.Analyzers.Tests.Pins reference each one, so it is
/// also in a lockfile. tools/sync-upstream.sh deletes upstream's Harness/NuGetPackages.cs in
/// favour of this file.
/// </summary>
internal static class NuGetPackages
{
    private static readonly ConcurrentDictionary<string, string[]> Cache = new(
        StringComparer.Ordinal
    );

    /// <summary>
    /// Where NuGet extracts packages: NUGET_PACKAGES, or ~/.nuget/packages when it is not set,
    /// then each folder of NUGET_FALLBACK_PACKAGES. A NuGet.config that moves the global packages
    /// folder is not read; set NUGET_PACKAGES instead.
    /// </summary>
    internal static IReadOnlyList<string> PackageFolders { get; } = GetPackageFolders();

    public static Task<string[]> GetReferencesAsync(
        string packageName,
        string version,
        string[] includedPaths
    )
    {
        var key = packageName + '@' + version + ':' + string.Join(',', includedPaths);
        return Task.FromResult(
            Cache.GetOrAdd(
                key,
                _ => GetAssemblies(FindPackage(packageName, version), includedPaths)
            )
        );
    }

    private static List<string> GetPackageFolders()
    {
        var folders = new List<string>
        {
            Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } folder
                ? folder
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nuget",
                    "packages"
                ),
        };
        if (
            Environment.GetEnvironmentVariable("NUGET_FALLBACK_PACKAGES") is
            { Length: > 0 } fallback
        )
        {
            folders.AddRange(
                fallback.Split(
                    Path.PathSeparator,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            );
        }

        return folders;
    }

    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "NuGet names the folders of the global packages folder in lowercase"
    )]
    private static string FindPackage(string packageName, string version)
    {
        // NuGet extracts a package to <id>/<version>, both lowercase, and writes .nupkg.metadata
        // in it once the extraction is complete.
        foreach (var root in PackageFolders)
        {
            var folder = Path.Combine(
                root,
                packageName.ToLowerInvariant(),
                version.ToLowerInvariant()
            );
            if (File.Exists(Path.Combine(folder, ".nupkg.metadata")))
                return folder;
        }

        throw new InvalidOperationException(
            $"{packageName} {version} is not in the NuGet cache ({string.Join(", ", PackageFolders)}). "
                + "The tests never download a package: add it to a project under "
                + "tests/Trax.Analyzers.Tests.Pins and restore, so that the lockfile names it."
        );
    }

    private static string[] GetAssemblies(string packageFolder, string[] includedPaths)
    {
        var result = new List<string>();
        foreach (
            var dll in Directory.EnumerateFiles(packageFolder, "*.dll", SearchOption.AllDirectories)
        )
        {
            var relativePath = Path.GetRelativePath(packageFolder, dll)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (!includedPaths.Any(path => relativePath.StartsWith(path, StringComparison.Ordinal)))
                continue;

            // Not a .NET assembly, though named like one.
            if (Path.GetFileName(dll) is "System.EnterpriseServices.Wrapper.dll")
                continue;

            if (
                Path.GetFileName(dll).EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)
            )
                continue;

            try
            {
                using var stream = File.OpenRead(dll);
                using var peFile = new PEReader(stream);
                _ = peFile.GetMetadataReader();
            }
            catch (BadImageFormatException)
            {
                continue;
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            result.Add(dll);
        }

        return [.. result];
    }
}
