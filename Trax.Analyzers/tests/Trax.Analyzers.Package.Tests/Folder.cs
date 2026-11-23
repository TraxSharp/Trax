using System.Reflection;

namespace Trax.Analyzers.Package.Tests;

/// <summary>The Trax.Analyzers folder and the files the package ships from it.</summary>
internal static class Folder
{
    public static string Root { get; } = FindRoot();

    public static string Package => Path.Combine(Root, "src", "Trax.Analyzers.Package");

    public static string RulesFile => Path.Combine(Root, "rules.txt");

    public static string Bans => Path.Combine(Package, "bans");

    public static string Profile(string level, string scope) =>
        Path.Combine(Package, "configuration", level, $"{scope}.globalconfig");

    /// <summary>The Microsoft.CodeAnalysis.BannedApiAnalyzers package folder the restore used.</summary>
    public static string BannedApiAnalyzers { get; } =
        typeof(Folder)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => string.Equals(a.Key, "BannedApiAnalyzers", StringComparison.Ordinal))
            .Value
        ?? throw new InvalidOperationException("The BannedApiAnalyzers path was not recorded");

    private static string FindRoot()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            if (File.Exists(Path.Combine(dir.FullName, "Trax.Analyzers.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException(
            $"No Trax.Analyzers.slnx above {AppContext.BaseDirectory}"
        );
    }
}
