using System.Diagnostics;
using System.Text.RegularExpressions;
using Trax.Api.Console.Terminal.Connection;

namespace Trax.Api.Console.Terminal.Code;

/// <summary>Where a type's source is: the file and the line its declaration starts on.</summary>
internal sealed record SourceLocation(string Path, int Line);

/// <summary>
/// Finds the source of a type the host names (a train's interface, a junction) in the checkout the console runs in,
/// by its name alone: the host sends no paths, and none is added for this. A train named by its interface
/// (<c>Shop.Trains.IShipOrderTrain</c>) is found by its class first (<c>ShipOrderTrain</c>), then the interface.
/// </summary>
internal static partial class CodeFinder
{
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        "node_modules",
        ".vs",
        ".idea",
    };

    // The variables a credential is read from, which the editor does not inherit.
    private static readonly string[] Secrets =
    [
        ConsoleTarget.ApiKeyVariable,
        ConsoleTarget.TokenVariable,
    ];

    /// <summary>The most source files a search reads before it gives up.</summary>
    public const int MaxFiles = 20_000;

    /// <summary>
    /// The checkout <paramref name="start"/> is in: the nearest directory up from it that holds <c>.git</c>, or
    /// <paramref name="start"/> itself outside one.
    /// </summary>
    public static string Root(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (Path.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
        return start;
    }

    /// <summary>
    /// The first declaration of the type <paramref name="name"/> under <paramref name="root"/>, or null, read off the
    /// calling thread. It reads at most <see cref="MaxFiles"/> files and follows no link, to a file or a directory.
    /// </summary>
    public static Task<SourceLocation?> FindAsync(
        string root,
        string name,
        CancellationToken cancellationToken = default
    ) => Task.Run(() => Find(root, name, cancellationToken), cancellationToken);

    private static SourceLocation? Find(
        string root,
        string name,
        CancellationToken cancellationToken
    )
    {
        var simple = name.Split('.', '+')[^1].Split('`')[0];
        if (!Identifier().IsMatch(simple))
            return null;
        var candidates = new List<string>();
        // A train is named by its interface; its class is what an operator wants to read.
        if (simple.Length > 1 && simple[0] == 'I' && char.IsUpper(simple[1]))
            candidates.Add(simple[1..]);
        candidates.Add(simple);
        var files = Files(root).Take(MaxFiles).ToList();
        foreach (var candidate in candidates)
        {
            var declaration = new Regex(
                $@"\b(class|record|interface|struct)\s+{Regex.Escape(candidate)}\b",
                RegexOptions.CultureInvariant
            );
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = 0;
                try
                {
                    foreach (var text in File.ReadLines(file))
                    {
                        line++;
                        if (declaration.IsMatch(text))
                            return new SourceLocation(file, line);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Gone or unreadable since the walk: the next file may still hold it.
                }
            }
        }
        return null;
    }

    private static IEnumerable<string> Files(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] files,
                dirs;
            try
            {
                files = Directory.GetFiles(dir, "*.cs");
                dirs = Directory.GetDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var file in files.Order(StringComparer.Ordinal))
                if (new FileInfo(file).LinkTarget is null)
                    yield return file;
            foreach (var sub in dirs.OrderDescending(StringComparer.Ordinal))
                if (
                    !Skipped.Contains(Path.GetFileName(sub))
                    && new DirectoryInfo(sub).LinkTarget is null
                )
                    pending.Push(sub);
        }
    }

    /// <summary>
    /// How to open <paramref name="location"/> in <paramref name="editor"/>: the line as an argument where the
    /// editor is one that takes it, otherwise the file alone.
    /// </summary>
    /// <exception cref="InvalidOperationException">The editor cannot be started as given.</exception>
    public static ProcessStartInfo Open(
        string editor,
        SourceLocation location,
        Func<string, string?>? environment = null
    )
    {
        var parts = Words(editor);
        if (parts.Count == 0)
            parts = ["vi"];
        var info = new ProcessStartInfo(
            Command(parts[0], environment ?? Environment.GetEnvironmentVariable)
        )
        {
            UseShellExecute = false,
        };
        // The editor, and whatever it runs (a plugin, a language server), has no use for the console's credential.
        foreach (var secret in Secrets)
            info.Environment.Remove(secret);
        foreach (var part in parts.Skip(1))
            info.ArgumentList.Add(part);
        switch (Path.GetFileNameWithoutExtension(parts[0]).ToLowerInvariant())
        {
            case "code" or "code-insiders" or "cursor" or "codium":
                info.ArgumentList.Add("--goto");
                info.ArgumentList.Add($"{location.Path}:{location.Line}");
                break;
            case "vi"
            or "vim"
            or "nvim"
            or "nano"
            or "emacs"
            or "micro"
            or "hx"
            or "kak"
            or "joe"
            or "mg":
                info.ArgumentList.Add($"+{location.Line}");
                info.ArgumentList.Add(location.Path);
                break;
            default:
                info.ArgumentList.Add(location.Path);
                break;
        }
        // cmd.exe reads a batch file's arguments again, so one with its metacharacters could run a command.
        if (
            Path.GetExtension(info.FileName).ToLowerInvariant() is ".cmd" or ".bat"
            && info.ArgumentList.Any(a => a.IndexOfAny(BatchMetacharacters) >= 0)
        )
            throw new InvalidOperationException(
                $"{info.FileName} is a batch file, and cmd.exe would read a character of its arguments as a "
                    + "command. Set VISUAL or EDITOR to the editor's .exe."
            );
        return info;
    }

    /// <summary>
    /// The editor the operator set: <c>VISUAL</c>, then <c>EDITOR</c>, then vi. One that is blank is not set.
    /// </summary>
    public static string Editor(Func<string, string?> environment) =>
        environment("VISUAL") is { } visual && !string.IsNullOrWhiteSpace(visual) ? visual.Trim()
        : environment("EDITOR") is { } editor && !string.IsNullOrWhiteSpace(editor) ? editor.Trim()
        : "vi";

    private static readonly char[] BatchMetacharacters = ['&', '|', '<', '>', '^', '%', '!', '"'];

    /// <summary>
    /// An editor setting split as a shell splits it, without running one: words between spaces, a quoted word kept
    /// whole (<c>"C:\Program Files\Code\code.exe" --wait</c>), and a backslash outside quotes taking the next
    /// character as it is.
    /// </summary>
    internal static List<string> Words(string text)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        var inWord = false;
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is { } q)
            {
                if (c == q)
                    quote = null;
                else if (q == '"' && c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
                    word.Append(text[++i]);
                else
                    word.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inWord = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inWord)
                    words.Add(word.ToString());
                word.Clear();
                inWord = false;
            }
            else
            {
                // On Windows a backslash is a path's separator, not an escape.
                if (c == '\\' && !OperatingSystem.IsWindows() && i + 1 < text.Length)
                    c = text[++i];
                word.Append(c);
                inWord = true;
            }
        }
        if (inWord)
            words.Add(word.ToString());
        return words;
    }

    /// <summary>
    /// The program <paramref name="command"/> names. Without a shell, Windows starts only a file that exists as named,
    /// so a bare name (<c>code</c>) is looked up on <c>PATH</c> with each of <c>PATHEXT</c>'s extensions, as a shell
    /// would. Elsewhere, and for a path or a name with its extension, it is <paramref name="command"/>.
    /// </summary>
    internal static string Command(
        string command,
        Func<string, string?> environment,
        bool? windows = null,
        Func<string, bool>? exists = null
    )
    {
        if (
            !(windows ?? OperatingSystem.IsWindows())
            || Path.HasExtension(command)
            || command.IndexOfAny(['/', '\\', ':']) >= 0
        )
            return command;
        exists ??= File.Exists;
        var extensions = (environment("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        foreach (
            var dir in (environment("PATH") ?? "").Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        foreach (var extension in extensions)
        {
            var candidate = Path.Join(dir, command + extension.ToLowerInvariant());
            if (exists(candidate))
                return candidate;
        }
        return command;
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
}
