using System.Diagnostics;

namespace Trax.Cli;

/// <summary>Runs a process to completion and captures both of its output streams.</summary>
internal static class ChildProcess
{
    /// <summary>
    /// Starts <paramref name="psi"/> with stdout and stderr redirected and returns once it has exited.
    /// Both streams are drained at the same time: reading one to its end before the other deadlocks as
    /// soon as the child fills the other's pipe buffer (about 64 KB), because the child then blocks on
    /// that write and never closes the stream being read.
    /// </summary>
    public static (int ExitCode, string StdOut, string StdErr) Run(ProcessStartInfo psi)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using var process =
            Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{psi.FileName}'.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
