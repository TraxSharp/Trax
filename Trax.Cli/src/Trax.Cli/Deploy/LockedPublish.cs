using System.Diagnostics;
using System.Globalization;
using Trax.Core.Extensions;

namespace Trax.Cli.Deploy;

/// <summary>
/// How trax deploy builds every image: <c>dotnet restore --locked-mode</c> of the project, then
/// <c>dotnet publish --no-restore</c>. A publish that restores for itself resolves afresh and rewrites the
/// <c>packages.lock.json</c> of the project and of every project it references.
/// <para>
/// The app is published without a runtime identifier and the image is made for <see cref="Runtime"/> with
/// <c>ContainerRuntimeIdentifier</c>, so it runs as <c>dotnet app.dll</c> on the runtime image of that platform. A
/// RID-specific publish needs a RID-specific restore: <c>-r</c> sets the runtime on every referenced project, whose
/// lockfiles hold no section for it, and <c>RuntimeIdentifiers</c> in the project makes restore download the runtime
/// packs, which no lockfile pins, and makes a publish without a runtime build one image per listed runtime.
/// </para>
/// </summary>
internal static class LockedPublish
{
    /// <summary>The platform the aws and kubernetes targets run an image on.</summary>
    internal const string Runtime = "linux-x64";

    internal static ProcessStartInfo Restore(string projectPath)
    {
        var psi = Dotnet(projectPath);
        foreach (var arg in new[] { "restore", projectPath, "--locked-mode" })
            psi.ArgumentList.Add(arg);
        return psi;
    }

    /// <summary><c>dotnet publish</c> of the project in Release from the restore before it; the caller adds the rest.</summary>
    internal static ProcessStartInfo Publish(string projectPath)
    {
        var psi = Dotnet(projectPath);
        foreach (var arg in new[] { "publish", projectPath, "-c", "Release", "--no-restore" })
            psi.ArgumentList.Add(arg);
        return psi;
    }

    /// <summary>
    /// Runs <paramref name="restore"/> and then <paramref name="publish"/>, stopping at the first that fails with what
    /// it wrote and, for a lockfile or runtime the project is missing, what to change.
    /// </summary>
    internal static async Task RunAsync(
        ProcessStartInfo restore,
        ProcessStartInfo publish,
        string what,
        Func<ProcessStartInfo, Task<(int ExitCode, string StdOut, string StdErr)>>? run = null
    )
    {
        run ??= ChildProcess.RunAsync;
        var project = restore.ArgumentList[1];
        foreach (var step in new[] { restore, publish })
        {
            var (exit, stdout, stderr) = await run(step).ConfigureAwait(false);
            if (exit != 0)
                throw new InvalidOperationException(
                    Failure(what, project, step == restore, exit, stdout + stderr)
                );
        }
    }

    internal static string Failure(
        string what,
        string projectPath,
        bool restoring,
        int exit,
        string output
    )
    {
        var name = Path.GetFileName(projectPath);
        // NU1004: a lockfile that does not match its project, which a locked restore refuses rather than rewrites.
        var hint = output.Contains("NU1004", StringComparison.Ordinal)
            ? "A packages.lock.json does not match its project. trax deploy restores in locked mode and does not "
                + $"rewrite lockfiles: regenerate them with `dotnet restore {projectPath} --force-evaluate`, check what "
                + "changed and commit them.\n"
            : "";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(restoring ? "Restoring" : "Publishing")} {name} for {what} failed (exit {exit}).\n{hint}{output}"
        );
    }

    private static ProcessStartInfo Dotnet(string projectPath) =>
        new("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath).NotNull(),
            UseShellExecute = false,
        };
}
