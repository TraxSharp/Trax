using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Trax.Cli.Deploy;

/// <summary>
/// Writes a first <c>trax.deploy.json</c> from the projects under a directory: every runnable project that references
/// a Trax package, outside tests, becomes a service.
/// </summary>
internal static partial class DeployInit
{
    public static DeployFile Create(string directory, string? app)
    {
        var projects = Directory
            .EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !InBuildOutputOrTests(directory, p))
            .Where(IsDeployableTraxHost)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (projects.Count == 0)
            throw new InvalidOperationException(
                $"No project under {directory} runs a Trax host: a deployed project is a web or console app that "
                    + "references a Trax package, outside a test project."
            );

        var file = new DeployFile { App = app ?? AppName(directory) };
        if (!DeployFile.Name().IsMatch(file.App))
            throw new InvalidOperationException(
                $"'{file.App}' is not an app name: lower case, a letter first, then letters, digits or dashes. "
                    + "Give one with --app."
            );
        foreach (var project in projects)
        {
            var name = projects.Count == 1 ? "hub" : ServiceName(project);
            if (file.Services.ContainsKey(name))
                name = $"{name}-{file.Services.Count + 1}";
            file.Services[name] = new DeployService
            {
                Project = Path.GetRelativePath(directory, project).Replace('\\', '/'),
            };
        }
        return file;
    }

    private static bool InBuildOutputOrTests(string root, string project)
    {
        var parts = Path.GetRelativePath(root, project).Split(Path.DirectorySeparatorChar);
        return parts.Any(p => p is "bin" or "obj" or "tests" or "test" or "node_modules")
            || Path.GetFileNameWithoutExtension(project)
                .EndsWith(".Tests", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeployableTraxHost(string project)
    {
        XDocument xml;
        try
        {
            xml = XDocument.Load(project);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            return false;
        }
        var root = xml.Root;
        if (root is null)
            return false;
        var web =
            ((string?)root.Attribute("Sdk"))?.StartsWith(
                "Microsoft.NET.Sdk.Web",
                StringComparison.Ordinal
            ) ?? false;
        var exe = root.Descendants("OutputType")
            .Any(e => string.Equals(e.Value.Trim(), "Exe", StringComparison.OrdinalIgnoreCase));
        var trax = root.Descendants("PackageReference")
            .Concat(root.Descendants("ProjectReference"))
            .Select(e => Path.GetFileName((string?)e.Attribute("Include") ?? ""))
            .Select(n =>
                n.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                    ? n[..^".csproj".Length]
                    : n
            )
            .Any(n => n.StartsWith("Trax.", StringComparison.Ordinal));
        return (web || exe) && trax;
    }

    /// <summary><c>Orders.Worker</c> becomes <c>worker</c>, <c>Orders</c> becomes <c>orders</c>.</summary>
    internal static string ServiceName(string project) =>
        Slug(Path.GetFileNameWithoutExtension(project).Split('.')[^1]);

    internal static string AppName(string directory) =>
        Slug(new DirectoryInfo(Path.GetFullPath(directory)).Name);

    private static string Slug(string name)
    {
        var slug = NotNameChars().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return slug.Length > 0 && char.IsLetter(slug[0])
            ? slug
            : $"app{(slug.Length > 0 ? "-" : "")}{slug}";
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotNameChars();
}
