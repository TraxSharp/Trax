using System.Text;
using System.Text.RegularExpressions;

namespace Trax.Cli.Generator;

/// <summary>
/// Turns free text from a schema (descriptions, HTTP paths) into something that can sit inside a
/// generated C# file without changing the code around it. Every template that renders schema text
/// goes through one of these, chosen by where the text lands.
/// </summary>
internal static partial class GeneratedText
{
    /// <summary>
    /// Collapses every run of control characters and line or paragraph separators into one space,
    /// which covers each character C# treats as a line terminator (CR, LF, U+0085, U+2028,
    /// U+2029), so the result is always a single line.
    /// </summary>
    internal static string SingleLine(string text) => LineBreaks().Replace(text, " ").Trim();

    /// <summary>Text for a <c>//</c> comment.</summary>
    internal static string Comment(string text) => SingleLine(text);

    /// <summary>Text for a <c>///</c> documentation comment: one line, with XML markup escaped.</summary>
    internal static string DocComment(string text) =>
        SingleLine(text).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// The inside of a regular <c>"..."</c> string literal. The backslash is escaped before the
    /// quote, so an escaped quote in the input cannot turn into a closing one.
    /// </summary>
    internal static string StringLiteralContent(string text)
    {
        var line = SingleLine(text);
        var sb = new StringBuilder(line.Length);
        foreach (var c in line)
        {
            switch (c)
            {
                case '\\':
                    sb.Append(@"\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"[\p{Cc}\p{Zl}\p{Zp}]+")]
    private static partial Regex LineBreaks();
}
