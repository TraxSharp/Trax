using System.Diagnostics;
using System.Globalization;

namespace Trax.Cli.Deploy;

/// <summary>The tag a deploy gives the images it builds, on every target that builds them.</summary>
internal static class ImageTags
{
    /// <summary>
    /// A tag no earlier deploy used: the time, then the commit and whether the tree had changes, so an image says
    /// what it was built from.
    /// </summary>
    public static async Task<string> CreateAsync(string directory, DateTime now)
    {
        var tag = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var commit = await GitAsync(directory, "rev-parse", "--short=12", "HEAD")
            .ConfigureAwait(false);
        if (commit is null)
            return tag;
        var dirty =
            await GitAsync(directory, "status", "--porcelain").ConfigureAwait(false) is
            { Length: > 0 };
        return $"{tag}-{commit}{(dirty ? "-dirty" : "")}";
    }

    private static async Task<string?> GitAsync(string directory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        try
        {
            var (exit, stdout, _) = await ChildProcess.RunAsync(psi).ConfigureAwait(false);
            return exit == 0 ? stdout.Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
