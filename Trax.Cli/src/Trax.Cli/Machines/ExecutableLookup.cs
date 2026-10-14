namespace Trax.Cli.Machines;

/// <summary>
/// Finds the executable a command name refers to by walking <c>PATH</c> itself, never the current directory or
/// this tool's own directory.
/// </summary>
/// <remarks>
/// Given a bare file name, <see cref="System.Diagnostics.Process"/> looks in the application's directory and
/// the current directory before <c>PATH</c>. <c>trax machine generate</c> and <c>check</c> are run from
/// inside a checkout, so a file named <c>node</c> at its root would run in place of the real one. Here the
/// name is resolved first and the process is started from the full path, so only an absolute <c>PATH</c>
/// entry is searched. An empty or relative <c>PATH</c> entry, which a shell reads as the current directory,
/// is skipped.
/// <para>
/// The rule for a value given on purpose (<c>--node</c>): an absolute path is used as it is, a bare name is
/// looked up on <c>PATH</c> like the default, and a relative path such as <c>./node</c> or <c>bin/node</c>
/// is refused, because it names whatever the directory the command happens to run from holds.
/// </para>
/// <para>
/// On Windows only a real executable (<c>.exe</c> or <c>.com</c>) is run. A <c>node.cmd</c> or <c>node.bat</c>
/// shim, which version managers install and <c>PATHEXT</c> would find, runs through <c>cmd.exe</c>, which
/// parses the arguments a second time by its own rules, so an argument can break out of its quoting
/// (BatBadBut). The lookup skips such a shim and, when it is all <c>PATH</c> holds, refuses it by name; a
/// <c>--node</c> naming one, absolute or not, is refused too.
/// </para>
/// </remarks>
internal static class ExecutableLookup
{
    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD";

    // The only extensions run on Windows: an image the OS loads itself, with no interpreter between.
    private static readonly string[] RealExecutables = [".COM", ".EXE"];

    /// <summary>
    /// The full path <paramref name="command"/> runs, or null when it is not found. Throws
    /// <see cref="InvalidOperationException"/> for a relative path, which is never resolved, and on Windows for
    /// a batch file, named or the only match on <c>PATH</c>, which is never run.
    /// </summary>
    public static string? Resolve(string command) =>
        Resolve(
            command,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATHEXT"),
            OperatingSystem.IsWindows()
        );

    /// <summary>
    /// The lookup with its environment passed in, so the Windows rules are testable anywhere.
    /// <paramref name="isFile"/> stands in for the file system's "is there a file here" (default
    /// <see cref="File.Exists(string)"/>), which is what lets a test lay out a Windows <c>PATH</c> on any OS.
    /// </summary>
    internal static string? Resolve(
        string command,
        string? path,
        string? pathExt,
        bool windows,
        Func<string, bool>? isFile = null
    )
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("The node executable's name is empty.");
        isFile ??= File.Exists;

        if (windows)
            RefuseNonExecutable(command);

        if (IsFullyQualified(command, windows))
            return IsExecutable(command, windows, isFile) ? command : null;

        if (HasDirectory(command, windows))
            throw new InvalidOperationException(
                $"--node must be an absolute path or a command name found on PATH, got the relative path "
                    + $"'{command}'. A relative path runs whatever the current directory holds; pass the full "
                    + "path instead."
            );

        var separator = windows ? ';' : ':';
        string? shim = null;
        foreach (var entry in (path ?? "").Split(separator))
        {
            var dir = windows ? entry.Trim().Trim('"') : entry;
            // An empty or relative entry means the current directory to a shell: never search it.
            if (dir.Length == 0 || !IsFullyQualified(dir, windows))
                continue;

            foreach (var name in Candidates(command, pathExt, windows))
            {
                var candidate = Join(dir, name, windows);
                if (!IsExecutable(candidate, windows, isFile))
                    continue;
                if (!windows || IsRealExecutable(candidate))
                    return candidate;
                // A batch shim PATHEXT found: never run, but remembered to name in the refusal. Anything
                // else that is not an image (node.ps1 in a PATHEXT) is passed over like a missing file.
                if (IsBatchFile(candidate))
                    shim ??= candidate;
            }
        }

        if (shim is not null)
            throw new InvalidOperationException(
                $"The only '{command}' on PATH is {shim}, a batch file, and trax runs node only from a real "
                    + "executable (.exe or .com): a .cmd or .bat runs through cmd.exe, which parses its arguments "
                    + "again and can let one escape its quoting. Pass --node with the absolute path of node.exe "
                    + "(a version manager keeps it under its own install directory), or put node.exe on PATH."
            );
        return null;
    }

    // On Windows a name without an extension is tried with each PATHEXT extension, as cmd does. The batch
    // extensions stay in the list so that a shim is found and refused by name rather than reported missing.
    internal static IEnumerable<string> Candidates(string command, string? pathExt, bool windows)
    {
        if (!windows)
        {
            yield return command;
            yield break;
        }

        var hasExtension = Path.GetExtension(command).Length > 0;
        if (hasExtension)
            yield return command;
        foreach (
            var ext in (string.IsNullOrWhiteSpace(pathExt) ? DefaultPathExt : pathExt).Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
            if (!hasExtension || !command.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                yield return command + ext;
    }

    /// <summary>Whether Windows runs <paramref name="path"/> directly: a <c>.exe</c> or <c>.com</c>.</summary>
    internal static bool IsRealExecutable(string path) =>
        RealExecutables.Contains(WindowsExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsBatchFile(string path) =>
        WindowsExtension(path).Equals(".cmd", StringComparison.OrdinalIgnoreCase)
        || WindowsExtension(path).Equals(".bat", StringComparison.OrdinalIgnoreCase);

    // The extension as Windows reads it, a backslash or a slash ending a directory, whatever OS this runs on.
    private static string WindowsExtension(string path)
    {
        var name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..];
    }

    // A --node naming a batch file is refused in any form, and an absolute one, which is run as given, must be
    // an executable image itself. A bare name has its extension added by the lookup.
    private static void RefuseNonExecutable(string command)
    {
        if (IsBatchFile(command))
            throw new InvalidOperationException(
                $"--node names a batch file, '{command}', and trax runs node only from a real executable "
                    + "(.exe or .com): a .cmd or .bat runs through cmd.exe, which parses its arguments again and "
                    + "can let one escape its quoting. Pass the absolute path of node.exe instead."
            );
        if (IsFullyQualified(command, windows: true) && !IsRealExecutable(command))
            throw new InvalidOperationException(
                $"--node must name node.exe on Windows, got '{command}': trax runs only a real executable "
                    + "(.exe or .com), never a script or a file without an extension."
            );
    }

    private static bool IsFullyQualified(string path, bool windows) =>
        windows
            ? path.Length >= 3
                && char.IsAsciiLetter(path[0])
                && path[1] == ':'
                && path[2] is '\\' or '/'
                || path.StartsWith(@"\\", StringComparison.Ordinal)
                || path.StartsWith("//", StringComparison.Ordinal)
            : path.StartsWith('/');

    private static bool HasDirectory(string path, bool windows) =>
        path.Contains('/') || (windows && (path.Contains('\\') || path.Contains(':')));

    private static string Join(string dir, string name, bool windows)
    {
        var separator = windows ? '\\' : '/';
        return dir.EndsWith(separator) || dir.EndsWith('/') ? dir + name : dir + separator + name;
    }

    // A directory is not a match, and on Unix neither is a file nobody may execute, as with a shell's lookup.
    private static bool IsExecutable(string path, bool windows, Func<string, bool> isFile)
    {
        if (!isFile(path))
            return false;
        if (windows || OperatingSystem.IsWindows())
            return true;
        const UnixFileMode anyExecute =
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(path) & anyExecute) != 0;
    }
}
