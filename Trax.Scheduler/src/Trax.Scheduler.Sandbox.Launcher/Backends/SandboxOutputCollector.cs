using System.Globalization;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>
/// What the collector Job does with a run's output volume on a cluster: reads it as
/// <see cref="DirectoryOutput"/> reads a directory (no link followed, nothing but a regular file
/// opened, every cap applied), and writes either the accepted files as a tar or the reason it
/// refused them. The launcher reads that tar again with the same checks. Runs in its own pod, so
/// the untrusted files are never opened in the launcher's. See scheduler/0021.
/// </summary>
internal static class SandboxOutputCollector
{
    /// <summary>The accepted output, as <see cref="SandboxArchive.WriteAsync"/> writes it.</summary>
    public const string OutputFile = "output.tar";

    /// <summary>Why the output was refused, in place of <see cref="OutputFile"/>.</summary>
    public const string RefusedFile = "refused.txt";

    public static async Task CollectAsync(
        string from,
        string to,
        SandboxArchiveLimits limits,
        CancellationToken ct
    )
    {
        var (files, refused) = await DirectoryOutput
            .ReadAsync(from, limits, ct)
            .ConfigureAwait(false);
        if (refused is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(to, RefusedFile), refused, ct)
                .ConfigureAwait(false);
            return;
        }
        var tar = await SandboxArchive.WriteAsync(files, ct).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(to, OutputFile), tar, ct).ConfigureAwait(false);
    }

    /// <summary>Reads back what <see cref="CollectAsync"/> wrote to <paramref name="directory"/>; a refusal when it wrote neither.</summary>
    public static async Task<(IReadOnlyList<SandboxFile> Files, string? Refused)> ReadAsync(
        string directory,
        SandboxArchiveLimits limits,
        CancellationToken ct
    )
    {
        var refused = Path.Combine(directory, RefusedFile);
        if (File.Exists(refused))
        {
            var text = await File.ReadAllTextAsync(refused, ct).ConfigureAwait(false);
            return ([], text.Length > 500 ? text[..500] : text);
        }
        var output = Path.Combine(directory, OutputFile);
        if (!File.Exists(output))
            return ([], "the collector left no output");
        var stream = File.OpenRead(output);
        await using (stream.ConfigureAwait(false))
            return await SandboxArchive.ReadAsync(stream, limits, "", ct).ConfigureAwait(false);
    }

    /// <summary>The limits from the collector's arguments.</summary>
    internal static SandboxArchiveLimits Limits(string maxBytes, string maxFiles) =>
        new(
            long.Parse(maxBytes, NumberStyles.None, CultureInfo.InvariantCulture),
            int.Parse(maxFiles, NumberStyles.None, CultureInfo.InvariantCulture)
        );
}
