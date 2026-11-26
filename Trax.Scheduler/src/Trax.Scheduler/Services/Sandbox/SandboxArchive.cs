using System.ComponentModel;
using System.Formats.Tar;
using System.Globalization;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// Reads and writes the tar a sandbox's output travels in. Reading is where the output is checked:
/// regular files and directories only, every path relative and inside the output directory, and
/// at most <see cref="SandboxArchiveLimits"/> files and bytes. Nothing is ever extracted to disk.
/// </summary>
/// <remarks>
/// The launcher reads the tar the container runtime gives it of the sandbox's volume, which the
/// sandbox wrote and so controls entirely, and writes a fresh one from the entries it accepted.
/// The hub reads that one again with the same checks, so a launcher bug alone does not hand the
/// hub a link or a path outside its directory (see scheduler/0021).
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SandboxArchive
{
    /// <summary>
    /// Reads every file in <paramref name="tar"/> under <paramref name="prefix"/>, or says why the
    /// archive is refused. On refusal the list of files is empty.
    /// </summary>
    /// <param name="tar">The archive; read at most <see cref="SandboxArchiveLimits.MaxRawBytes"/> bytes of.</param>
    /// <param name="limits">The caps.</param>
    /// <param name="prefix">
    /// The directory every entry must be under, such as <c>out/</c>, removed from the paths
    /// returned; empty for an archive whose entries are the files themselves.
    /// </param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The files, and null; or no files and the reason for refusing.</returns>
    public static async Task<(IReadOnlyList<SandboxFile> Files, string? Refused)> ReadAsync(
        Stream tar,
        SandboxArchiveLimits limits,
        string prefix,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(tar);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length > 0 && !prefix.EndsWith('/'))
            throw new ArgumentException("A prefix ends with '/'.", nameof(prefix));

        var capped = new CappedReadStream(tar, limits.MaxRawBytes);
        await using (capped.ConfigureAwait(false))
        {
            try
            {
                return await ReadCoreAsync(capped, limits, prefix, ct).ConfigureAwait(false);
            }
            catch (ReadCapExceededException)
            {
                return Refuse(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"the archive is larger than {limits.MaxRawBytes} bytes"
                    )
                );
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException)
            {
                return Refuse("the archive is not a well-formed tar");
            }
            catch (EndOfStreamException) when (capped.Position == 0)
            {
                // No bytes at all: the tar of no files, as WriteAsync writes it.
                return ([], null);
            }
            catch (EndOfStreamException)
            {
                return Refuse("the archive ends early");
            }
        }
    }

    private static async Task<(IReadOnlyList<SandboxFile>, string?)> ReadCoreAsync(
        Stream stream,
        SandboxArchiveLimits limits,
        string prefix,
        CancellationToken ct
    )
    {
        var files = new List<SandboxFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        var entries = 0;

        var reader = new TarReader(stream, leaveOpen: true);
        await using (reader.ConfigureAwait(false))
        {
            while (
                await reader.GetNextEntryAsync(copyData: false, ct).ConfigureAwait(false)
                    is { } entry
            )
            {
                if (++entries > limits.MaxFiles)
                    return Refuse(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"the output has more than {limits.MaxFiles} entries"
                        )
                    );

                var (path, refused) = Check(entry, prefix);
                if (refused is not null)
                    return Refuse(refused);
                if (path is null)
                    continue;

                if (!seen.Add(path))
                    return Refuse($"'{Shown(path)}' appears twice");

                if (entry.Length < 0 || bytes + entry.Length > limits.MaxBytes)
                    return Refuse(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"the output is larger than {limits.MaxBytes} bytes"
                        )
                    );
                bytes += entry.Length;

                var content = new byte[entry.Length];
                if (entry.DataStream is { } data)
                    await data.ReadExactlyAsync(content, ct).ConfigureAwait(false);
                else if (content.Length > 0)
                    return Refuse($"'{Shown(path)}' has no data");

                files.Add(new SandboxFile(path, content));
            }
        }

        return (files, null);
    }

    /// <summary>
    /// The path of a regular file entry; null for a directory or the prefix itself, which are
    /// skipped; or the reason the entry is refused.
    /// </summary>
    private static (string? Path, string? Refused) Check(TarEntry entry, string prefix)
    {
        var name = entry.Name;
        if (prefix.Length > 0 && IsRoot(name, prefix))
            return entry.EntryType == TarEntryType.Directory
                ? (null, null)
                : (null, $"'{Shown(name)}' is not a directory");

        if (Path(name, prefix) is not { } path)
            return (null, $"'{Shown(name)}' is not a plain path inside the output");

        return entry.EntryType switch
        {
            TarEntryType.Directory => (null, null),
            TarEntryType.RegularFile or TarEntryType.V7RegularFile => (path, null),
            _ => (
                null,
                $"'{Shown(path)}' is a {Kind(entry.EntryType)}, and only regular files and "
                    + "directories are handed on"
            ),
        };
    }

    /// <summary>
    /// Writes <paramref name="files"/> as a tar of regular files, each mode 0644, owned by 0:0,
    /// dated the Unix epoch, so the bytes depend on nothing but the files.
    /// </summary>
    public static async Task<byte[]> WriteAsync(
        IReadOnlyList<SandboxFile> files,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(files);
        using var buffer = new MemoryStream();
        var writer = new TarWriter(buffer, TarEntryFormat.Pax, leaveOpen: true);
        await using (writer.ConfigureAwait(false))
        {
            foreach (var file in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, file.Path)
                {
                    Mode =
                        UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.GroupRead
                        | UnixFileMode.OtherRead,
                    ModificationTime = DateTimeOffset.UnixEpoch,
                    Uid = 0,
                    Gid = 0,
                    DataStream = new MemoryStream(file.Content.ToArray(), writable: false),
                };
                await writer.WriteEntryAsync(entry, ct).ConfigureAwait(false);
            }
        }

        return buffer.ToArray();
    }

    private static bool IsRoot(string name, string prefix) =>
        string.Equals(name, prefix, StringComparison.Ordinal)
        || string.Equals(name, prefix[..^1], StringComparison.Ordinal);

    /// <summary>The path under <paramref name="prefix"/>, or null when it is not a plain relative path inside it.</summary>
    internal static string? Path(string name, string prefix)
    {
        if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var path = name[prefix.Length..];
        if (path.EndsWith('/'))
            path = path[..^1];

        if (
            path.Length == 0
            || path.Length > SandboxArchiveLimits.MaxPathLength
            || path[0] == '/'
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Any(char.IsControl)
        )
            return null;

        foreach (var segment in path.Split('/'))
            if (segment.Length == 0 || segment is "." or "..")
                return null;

        return path;
    }

    private static string Kind(TarEntryType type) =>
        type switch
        {
            TarEntryType.SymbolicLink => "symbolic link",
            TarEntryType.HardLink => "hard link",
            TarEntryType.CharacterDevice or TarEntryType.BlockDevice => "device",
            TarEntryType.Fifo => "named pipe",
            _ => type.ToString(),
        };

    // A refusal names the path, cut short and with control characters dropped, since it is the
    // sandbox's text and ends up in a log and a failed run's reason.
    private static string Shown(string path)
    {
        var plain = new string(path.Where(c => !char.IsControl(c)).Take(200).ToArray());
        return plain.Length < path.Length ? plain + "..." : plain;
    }

    private static (IReadOnlyList<SandboxFile>, string?) Refuse(string reason) => ([], reason);
}
