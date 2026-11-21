using System.Globalization;
using System.Text;

namespace Trax.Docs.Tests.Infrastructure;

/// <summary>The heading ids traxsharp.net gives a page, computed the way rehype-slug does.</summary>
internal static partial class Headings
{
    [GeneratedRegex(@"^\s*(```|~~~)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Fence { get; }

    [GeneratedRegex(
        @"^ {0,3}#{1,6}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex AtxHeading { get; }

    /// <summary>
    /// The ids rehype-slug gives a page's headings: github-slugger over each heading's text,
    /// numbered <c>-1</c>, <c>-2</c> on repeats.
    /// </summary>
    public static HashSet<string> Ids(string content)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inFence = false;
        var start = 0;

        // Front matter is not part of the rendered page.
        if (lines.Length > 0 && string.Equals(lines[0], "---", StringComparison.Ordinal))
        {
            var end = Array.IndexOf(lines, "---", 1);
            if (end > 0)
                start = end + 1;
        }

        for (var i = start; i < lines.Length; i++)
        {
            if (Fence.IsMatch(lines[i]))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
                continue;

            var m = AtxHeading.Match(lines[i]);
            if (!m.Success)
                continue;

            var slug = Slug(PlainText(m.Groups["text"].Value));
            var unique = slug;
            while (occurrences.ContainsKey(unique))
            {
                occurrences[slug]++;
                unique = string.Create(CultureInfo.InvariantCulture, $"{slug}-{occurrences[slug]}");
            }
            occurrences[unique] = 0;
            ids.Add(unique);
        }
        return ids;
    }

    /// <summary>A heading's rendered text: link and code markup dropped, their text kept.</summary>
    public static string PlainText(string heading)
    {
        var text = ImageMarkup.Replace(heading, "");
        text = LinkMarkup.Replace(text, "${text}");
        text = CodeMarkup.Replace(text, "${text}");
        text = EscapedPunctuation.Replace(text, "${text}");
        return text;
    }

    /// <summary>
    /// github-slugger: lowercase, drop everything but letters, marks, digits, connector
    /// punctuation, spaces and hyphens, then turn each space into a hyphen.
    /// </summary>
    public static string Slug(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            var keep =
                c is ' ' or '-'
                || char.GetUnicodeCategory(c)
                    is UnicodeCategory.UppercaseLetter
                        or UnicodeCategory.LowercaseLetter
                        or UnicodeCategory.TitlecaseLetter
                        or UnicodeCategory.ModifierLetter
                        or UnicodeCategory.OtherLetter
                        or UnicodeCategory.NonSpacingMark
                        or UnicodeCategory.SpacingCombiningMark
                        or UnicodeCategory.EnclosingMark
                        or UnicodeCategory.DecimalDigitNumber
                        or UnicodeCategory.LetterNumber
                        or UnicodeCategory.OtherNumber
                        or UnicodeCategory.ConnectorPunctuation;
            if (keep)
                sb.Append(c == ' ' ? '-' : c);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageMarkup { get; }

    [GeneratedRegex(
        @"\[(?<text>[^\]]*)\]\([^)]*\)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex LinkMarkup { get; }

    [GeneratedRegex(@"`(?<text>[^`]*)`", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeMarkup { get; }

    [GeneratedRegex(@"\\(?<text>\p{P}|\p{S})", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EscapedPunctuation { get; }
}
