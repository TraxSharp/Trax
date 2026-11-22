using System.Diagnostics;
using System.Globalization;

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
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        ProcessStartInfo psi
    )
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
        await process.WaitForExitAsync().ConfigureAwait(false);
        return (
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false)
        );
    }

    /// <summary>
    /// <see cref="RunAsync(ProcessStartInfo)"/>, stopping the process and everything it started when it runs longer
    /// than <paramref name="timeout"/> (a <see cref="TimeoutException"/> holding what it wrote) or when
    /// <paramref name="ct"/> is cancelled (an <see cref="OperationCanceledException"/>).
    /// </summary>
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        ProcessStartInfo psi,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using var process =
            Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{psi.FileName}'.");

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It exited on its own in the meantime.
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{psi.FileName}' did not finish within {timeout.TotalMinutes:0.#} minutes and was stopped. "
                )
                    + $"Its output:\n{await stdout.ConfigureAwait(false)}{await stderr.ConfigureAwait(false)}",
                ex
            );
        }
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        return (
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false)
        );
    }
}
