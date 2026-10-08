using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Index;

namespace Trax.Samples.Recovery.Trains.Ingest.Junctions;

/// <summary>
/// Turns each index's shape into one: the abstract rebuilt from OpenAlex's inverted index or
/// stripped of Crossref's JATS tags, the DOI lower case without a resolver prefix, authors as
/// "Given Family", references as bare ids. Each work gets a hash of that content.
/// </summary>
public partial class NormaliseWorks(DemoPace pace)
    : EffectJunction<FetchedPartition, NormalisedPartition>
{
    public override async Task<NormalisedPartition> Run(FetchedPartition partition)
    {
        await Task.Delay(pace.StepDelay, CancellationToken);

        var works = partition
            .Records.Select(record =>
            {
                using var json = JsonDocument.Parse(record.Payload);
                return partition.Source switch
                {
                    IndexFixture.OpenAlex => FromOpenAlex(record.SourceId, json.RootElement),
                    IndexFixture.Crossref => FromCrossref(record.SourceId, json.RootElement),
                    _ => throw new InvalidOperationException(
                        $"No normaliser for the source {partition.Source}."
                    ),
                };
            })
            .OrderBy(w => w.SourceId, StringComparer.Ordinal)
            .ToList();

        return new NormalisedPartition(partition.Source, partition.Month, works);
    }

    private static NormalisedWork FromOpenAlex(string sourceId, JsonElement work)
    {
        var words = work.GetProperty("abstract_inverted_index")
            .EnumerateObject()
            .SelectMany(word =>
                word.Value.EnumerateArray().Select(at => (At: at.GetInt32(), word.Name))
            )
            .OrderBy(w => w.At)
            .Select(w => w.Name);

        return Work(
            sourceId,
            Doi(work.GetProperty("doi").GetString()),
            work.GetProperty("display_name").GetString() ?? "",
            string.Join(' ', words),
            int.Parse(
                work.GetProperty("publication_date").GetString()![..4],
                CultureInfo.InvariantCulture
            ),
            work.GetProperty("authorships")
                .EnumerateArray()
                .Select(a => a.GetProperty("author").GetProperty("display_name").GetString() ?? "")
                .ToList(),
            work.GetProperty("referenced_works")
                .EnumerateArray()
                .Select(r => r.GetString()!.Split('/')[^1])
                .ToList()
        );
    }

    private static NormalisedWork FromCrossref(string sourceId, JsonElement item) =>
        Work(
            sourceId,
            Doi(item.GetProperty("DOI").GetString()),
            item.GetProperty("title").EnumerateArray().First().GetString() ?? "",
            JatsTag().Replace(item.GetProperty("abstract").GetString() ?? "", ""),
            item.GetProperty("issued").GetProperty("date-parts")[0][0].GetInt32(),
            item.GetProperty("author")
                .EnumerateArray()
                .Select(a =>
                    $"{a.GetProperty("given").GetString()} {a.GetProperty("family").GetString()}"
                )
                .ToList(),
            item.GetProperty("reference")
                .EnumerateArray()
                .Select(r => Doi(r.GetProperty("DOI").GetString()) ?? "")
                .Where(doi => doi.Length > 0)
                .ToList()
        );

    private static NormalisedWork Work(
        string sourceId,
        string? doi,
        string title,
        string @abstract,
        int year,
        List<string> authors,
        List<string> references
    )
    {
        title = Whitespace().Replace(title, " ").Trim();
        @abstract = Whitespace().Replace(@abstract, " ").Trim();
        authors = authors.Select(a => Whitespace().Replace(a, " ").Trim()).ToList();
        references = references.Order(StringComparer.Ordinal).ToList();

        var content = string.Join(
            '\u001f',
            [
                doi ?? "",
                title,
                @abstract,
                year.ToString(CultureInfo.InvariantCulture),
                .. authors,
                "",
                .. references,
            ]
        );
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        return new NormalisedWork(sourceId, doi, title, @abstract, year, authors, references, hash);
    }

    private static string? Doi(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? null
            : DoiResolver().Replace(raw.Trim(), "").ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"</?jats:[^>]*>")]
    private static partial Regex JatsTag();

    [GeneratedRegex(@"^https?://(dx\.)?doi\.org/", RegexOptions.IgnoreCase)]
    private static partial Regex DoiResolver();
}
