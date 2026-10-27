using System.Globalization;
using System.Text;

namespace Trax.Api.Console.Terminal.Lists;

/// <summary>
/// Text the host sent, made safe to write to a terminal. A host's error message, a state's name or a log line could
/// carry an escape sequence (a clipboard write, a retitle, text hidden by colour), a bidirectional control that
/// reorders what the operator reads, or an invisible character, so every control character, format character
/// (<c>\p{Cf}</c>: the bidirectional embeddings, overrides, isolates and marks U+061C, U+200E and U+200F among them)
/// and line or paragraph separator is replaced before it is printed.
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
        // By code point, so a format character outside the BMP (a tag character) is found, and a lone surrogate,
        // which EnumerateRunes reads as U+FFFD, is replaced too.
        if (!text.EnumerateRunes().Any(r => Unsafe(r, keepLines) || r == Rune.ReplacementChar))
            return text;
        var clean = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            clean.Append(Unsafe(rune, keepLines) ? Replacement.ToString() : rune.ToString());
        return clean.ToString();
    }

    private static bool Unsafe(Rune c, bool keepLines)
    {
        if (keepLines && c.Value is '\n' or '\t')
            return false;
        return Rune.GetUnicodeCategory(c)
            is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator;
    }
}
