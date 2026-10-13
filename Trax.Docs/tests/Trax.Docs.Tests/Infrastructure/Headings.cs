using System.Globalization;
using System.Text;

namespace Trax.Docs.Tests.Infrastructure;

/// <summary>The heading ids traxsharp.net gives a page, computed the way rehype-slug does.</summary>
internal static class Headings
{
    private static readonly Regex Fence = new(@"^\s*(```|~~~)", RegexOptions.Compiled);

    private static readonly Regex AtxHeading = new(
        @"^ {0,3}#{1,6}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// The ids rehype-slug gives a page's headings: github-slugger over each heading's text,
    /// numbered <c>-1</c>, <c>-2</c> on repeats.
    /// </summary>
    public static HashSet<string> Ids(string content)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        var start = 0;

        // Front matter is not part of the rendered page.
        if (lines.Length > 0 && lines[0] == "---")
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
                unique = $"{slug}-{occurrences[slug]}";
            }
            occurrences[unique] = 0;
            ids.Add(unique);
        }
        return ids;
    }

    /// <summary>A heading's rendered text: link and code markup dropped, their text kept.</summary>
    public static string PlainText(string heading)
    {
        var text = Regex.Replace(heading, @"!\[[^\]]*\]\([^)]*\)", "");
        text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");
        text = Regex.Replace(text, @"`([^`]*)`", "$1");
        text = Regex.Replace(text, @"\\(\p{P}|\p{S})", "$1");
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
}
