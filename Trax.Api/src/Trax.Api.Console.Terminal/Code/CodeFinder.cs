using System.Diagnostics;
using System.Text.RegularExpressions;

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

    /// <summary>The first declaration of the type <paramref name="name"/> under <paramref name="root"/>, or null.</summary>
    public static SourceLocation? Find(string root, string name)
    {
        var simple = name.Split('.', '+')[^1].Split('`')[0];
        if (!Identifier().IsMatch(simple))
            return null;
        var candidates = new List<string>();
        // A train is named by its interface; its class is what an operator wants to read.
        if (simple.Length > 1 && simple[0] == 'I' && char.IsUpper(simple[1]))
            candidates.Add(simple[1..]);
        candidates.Add(simple);
        var files = Files(root).ToList();
        foreach (var candidate in candidates)
        {
            var declaration = new Regex(
                $@"\b(class|record|interface|struct)\s+{Regex.Escape(candidate)}\b",
                RegexOptions.CultureInvariant
            );
            foreach (var file in files)
            {
                var line = 0;
                foreach (var text in File.ReadLines(file))
                {
                    line++;
                    if (declaration.IsMatch(text))
                        return new SourceLocation(file, line);
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
    public static ProcessStartInfo Open(string editor, SourceLocation location)
    {
        var parts = editor.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var info = new ProcessStartInfo(parts[0]) { UseShellExecute = false };
        foreach (var part in parts.Skip(1))
            info.ArgumentList.Add(part);
        switch (Path.GetFileNameWithoutExtension(parts[0]))
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
        return info;
    }

    /// <summary>The editor the operator set: <c>VISUAL</c>, then <c>EDITOR</c>, then vi.</summary>
    public static string Editor(Func<string, string?> environment) =>
        environment("VISUAL") is { Length: > 0 } visual ? visual
        : environment("EDITOR") is { Length: > 0 } editor ? editor
        : "vi";

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
}
