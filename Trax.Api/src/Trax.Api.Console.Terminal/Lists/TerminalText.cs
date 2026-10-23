using System.Text;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>
/// Text the host sent, made safe to write to a terminal. A host's error message, a state's name or a log line could
/// carry an escape sequence (a clipboard write, a retitle, text hidden by colour) or a bidirectional override that
/// reorders what the operator reads, so every control character and override is replaced before it is printed.
/// </summary>
internal static class TerminalText
{
    private const char Replacement = '\uFFFD';

    /// <summary><paramref name="text"/> on one line: every control character, line breaks included, replaced.</summary>
    public static string Line(string? text) => Clean(text, keepLines: false);

    /// <summary><paramref name="text"/> with its line breaks and tabs kept and every other control character replaced.</summary>
    public static string Block(string? text) => Clean(text, keepLines: true);

    private static string Clean(string? text, bool keepLines)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        if (!text.Any(c => Unsafe(c, keepLines)))
            return text;
        var clean = new StringBuilder(text.Length);
        foreach (var c in text)
            clean.Append(Unsafe(c, keepLines) ? Replacement : c);
        return clean.ToString();
    }

    private static bool Unsafe(char c, bool keepLines)
    {
        if (keepLines && c is '\n' or '\t')
            return false;
        return char.IsControl(c)
            || c is >= '\u202A' and <= '\u202E'
            || c is >= '\u2066' and <= '\u2069'
            || c is '\u2028' or '\u2029';
    }
}
