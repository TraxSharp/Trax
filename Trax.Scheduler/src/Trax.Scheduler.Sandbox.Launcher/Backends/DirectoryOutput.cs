using System.Formats.Tar;
using System.Globalization;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>
/// Reads a run's output directory on a cluster, in the collector's pod, where the run's output
/// volume is mounted read-only after the sandbox has exited. It walks the tree without following a link, has the tar
/// writer read each entry's type with lstat (so a link, a device or a named pipe is recorded as
/// one and never opened), and passes the tar through the same checks as podman's.
/// </summary>
internal static class DirectoryOutput
{
    public static async Task<(IReadOnlyList<SandboxFile> Files, string? Refused)> ReadAsync(
        string directory,
        SandboxArchiveLimits limits,
        CancellationToken ct
    )
    {
        using var buffer = new MemoryStream();
        var writer = new TarWriter(buffer, TarEntryFormat.Pax, leaveOpen: true);
        await using (writer.ConfigureAwait(false))
        {
            var entries = 0;
            foreach (var entry in Walk(new DirectoryInfo(directory)))
            {
                if (++entries > limits.MaxFiles)
                    return (
                        [],
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"the output has more than {limits.MaxFiles} entries"
                        )
                    );
                if (
                    entry is FileInfo { LinkTarget: null } file
                    && buffer.Length + file.Length > limits.MaxRawBytes
                )
                    return (
                        [],
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"the output is larger than {limits.MaxBytes} bytes"
                        )
                    );
                var name = Path.GetRelativePath(directory, entry.FullName).Replace('\\', '/');
                try
                {
                    await writer.WriteEntryAsync(entry.FullName, name, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return ([], $"'{name}' cannot be read as a plain file or directory");
                }
            }
        }

        buffer.Position = 0;
        return await SandboxArchive.ReadAsync(buffer, limits, "", ct).ConfigureAwait(false);
    }

    /// <summary>Every entry under <paramref name="root"/>; descends only into real directories, never a link to one.</summary>
    private static IEnumerable<FileSystemInfo> Walk(DirectoryInfo root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.None,
            IgnoreInaccessible = false,
        };
        while (pending.Count > 0)
            foreach (var entry in pending.Pop().EnumerateFileSystemInfos("*", options))
            {
                yield return entry;
                if (entry is DirectoryInfo { LinkTarget: null } sub)
                    pending.Push(sub);
            }
    }
}
