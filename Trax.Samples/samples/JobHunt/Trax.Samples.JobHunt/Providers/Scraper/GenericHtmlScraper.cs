using System.Text.Json;
using System.Text.RegularExpressions;

namespace Trax.Samples.JobHunt.Providers.Scraper;

/// <summary>
/// Extracts job posting data from HTML pages. Tries JSON-LD (schema.org JobPosting)
/// first, then falls back to OpenGraph meta tags, then to the page title and first
/// block of body text.
/// </summary>
public partial class GenericHtmlScraper(HttpClient httpClient) : IJobScraper
{
    public async Task<ScrapeResult> ScrapeAsync(Uri url, CancellationToken ct = default)
    {
        var html = await httpClient.GetStringAsync(url, ct);
        return ParseHtml(html);
    }

    public static ScrapeResult ParseHtml(string html)
    {
        // Try JSON-LD first
        var jsonLd = TryExtractJsonLd(html);
        if (jsonLd is not null)
            return jsonLd;

        // Fall back to OpenGraph + page title
        var ogTitle = ExtractMetaContent(html, "og:title");
        var ogDescription = ExtractMetaContent(html, "og:description");
        var pageTitle = ExtractTagContent(html, "title");

        return new ScrapeResult
        {
            Title = ogTitle ?? pageTitle,
            Company = ExtractMetaContent(html, "og:site_name"),
            Description = ogDescription ?? ExtractFirstParagraphs(html),
        };
    }

    private static ScrapeResult? TryExtractJsonLd(string html)
    {
        var matches = JsonLdRegex().Matches(html);
        foreach (Match match in matches)
        {
            try
            {
                using var doc = JsonDocument.Parse(match.Groups[1].Value);
                var root = doc.RootElement;
                var type = root.TryGetProperty("@type", out var t) ? t.GetString() : null;
                if (type is not "JobPosting")
                    continue;

                return new ScrapeResult
                {
                    Title = root.TryGetProperty("title", out var title) ? title.GetString() : null,
                    Company = ExtractHiringOrganization(root),
                    Description = root.TryGetProperty("description", out var desc)
                        ? StripHtmlTags(desc.GetString() ?? "")
                        : null,
                };
            }
            catch (JsonException)
            {
                // Malformed JSON-LD, try next block
            }
        }

        return null;
    }

    private static string? ExtractHiringOrganization(JsonElement root)
    {
        if (!root.TryGetProperty("hiringOrganization", out var org))
            return null;

        if (org.ValueKind == JsonValueKind.String)
            return org.GetString();

        return org.TryGetProperty("name", out var name) ? name.GetString() : null;
    }

    private static string? ExtractMetaContent(string html, string property)
    {
        var pattern = $"""<meta[^>]*property="{property}"[^>]*content="([^"]*)"[^>]*/?>""";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value;

        // Try name= variant
        pattern = $"""<meta[^>]*name="{property}"[^>]*content="([^"]*)"[^>]*/?>""";
        match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractTagContent(string html, string tag)
    {
        var match = Regex.Match(html, $"<{tag}[^>]*>([^<]+)</{tag}>", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractFirstParagraphs(string html)
    {
        var matches = Regex.Matches(
            html,
            "<p[^>]*>(.+?)</p>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        if (matches.Count == 0)
            return null;

        var paragraphs = matches
            .Take(5)
            .Select(m => StripHtmlTags(m.Groups[1].Value).Trim())
            .Where(p => p.Length > 20);

        var combined = string.Join("\n\n", paragraphs);
        return string.IsNullOrWhiteSpace(combined) ? null : combined;
    }

    private static string StripHtmlTags(string html) => HtmlTagRegex().Replace(html, "").Trim();

    [GeneratedRegex("""<script[^>]*>[\s\S]*?</script>|<[^>]+>""", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(
        """<script[^>]*type\s*=\s*["']application/ld\+json["'][^>]*>([\s\S]*?)</script>""",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex JsonLdRegex();
}
