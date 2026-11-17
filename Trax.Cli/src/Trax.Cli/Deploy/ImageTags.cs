using System.Diagnostics;

namespace Trax.Cli.Deploy;

/// <summary>The tag a deploy gives the images it builds, on every target that builds them.</summary>
internal static class ImageTags
{
    /// <summary>
    /// A tag no earlier deploy used: the time, then the commit and whether the tree had changes, so an image says
    /// what it was built from.
    /// </summary>
    public static string Create(string directory, DateTime now)
    {
        var tag = now.ToString(
            "yyyyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture
        );
        var commit = Git(directory, "rev-parse", "--short=12", "HEAD");
        if (commit is null)
            return tag;
        var dirty = Git(directory, "status", "--porcelain") is { Length: > 0 };
        return $"{tag}-{commit}{(dirty ? "-dirty" : "")}";
    }

    private static string? Git(string directory, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = directory };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        try
        {
            var (exit, stdout, _) = ChildProcess.Run(psi);
            return exit == 0 ? stdout.Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
