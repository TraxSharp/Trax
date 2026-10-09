using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.Samples.Recovery.Index;

/// <summary>
/// Two scholarly indexes over three months, canned so the ingest needs no network: 18 made-up works
/// in six partitions of three, each record in its index's own JSON shape. OpenAlex sends an id URL,
/// a DOI URL, a title, an inverted-index abstract, authorships and referenced work URLs; Crossref
/// sends a bare DOI, a title array, a JATS abstract, given and family names and references by DOI.
/// </summary>
/// <remarks>
/// Two works overlap the topic map's corpus on purpose. <c>OpenAlex/2025-01</c> carries a record of
/// a paper the corpus already holds, under its exact title, so the model answers yes, the same work.
/// <c>Crossref/2025-02</c> carries a paper whose title is close to one in the corpus but not the
/// same, so the model is unsure and the work is kept for review. Every other work is new.
/// </remarks>
public static partial class IndexFixture
{
    public const string OpenAlex = "OpenAlex";
    public const string Crossref = "Crossref";

    /// <summary>The sources the fixture covers.</summary>
    public static readonly IReadOnlyList<string> Sources = [OpenAlex, Crossref];

    /// <summary>The months each source has a partition for.</summary>
    public static readonly IReadOnlyList<string> Months = ["2025-01", "2025-02", "2025-03"];

    /// <summary>Every record, in source, month and id order.</summary>
    public static readonly IReadOnlyList<SourceRecord> Records =
    [
        // ── OpenAlex, January: one is a paper the corpus holds (W30001) ──────
        OpenAlexWork(
            "W41001",
            "2025-01-14",
            "Recharge pulses in a fractured chalk aquifer",
            "Groundwater recharge through fractured chalk arrives in pulses after winter storms. "
                + "Borehole levels and tracer arrivals locate the fractures that carry most recharge.",
            ["Ines Varga", "Tomas Ruel"],
            ["W30002", "W30003"]
        ),
        OpenAlexWork(
            "W41002",
            "2025-01-20",
            "Sediment plumes after glacier lake outbursts",
            "Outburst floods from glacier lakes carry sediment plumes far down the valley. Satellite "
                + "images date each plume and trace it to the lake that drained.",
            ["Asha Rao", "Lars Edvik"],
            ["W50101", "W50102"]
        ),
        OpenAlexWork(
            "W41003",
            "2025-01-27",
            "Kelp forest recovery after a marine heatwave",
            "Kelp canopies lost in a marine heatwave regrew within three years where urchin numbers "
                + "stayed low. Divers counted canopy and urchins at fixed transects.",
            ["Marisol Ortega"],
            ["W50103"]
        ),
        // ── OpenAlex, February ───────────────────────────────────────────────
        OpenAlexWork(
            "W41004",
            "2025-02-03",
            "Peatland carbon loss under drainage ditches",
            "Drained peat loses carbon fastest beside the ditches. Chamber measurements across "
                + "transects show emissions falling with distance from the drain.",
            ["Hanna Koski", "Lars Edvik"],
            ["W50104", "W50105"]
        ),
        OpenAlexWork(
            "W41005",
            "2025-02-11",
            "Lichen cover as a record of air quality",
            "Lichen cover on old trees tracks decades of air quality. Surveys repeated on the same "
                + "trunks show sensitive species returning as pollution fell.",
            ["Priya Nair"],
            ["W50106"]
        ),
        OpenAlexWork(
            "W41006",
            "2025-02-24",
            "River otter return to restored floodplains",
            "Otters returned to floodplains within two years of reconnection to the river. Spraint "
                + "surveys and camera traps followed their spread upstream.",
            ["Marisol Ortega", "Ben Achterberg"],
            ["W50107", "W50108"]
        ),
        // ── OpenAlex, March ──────────────────────────────────────────────────
        OpenAlexWork(
            "W41007",
            "2025-03-05",
            "Coral spawning timing and lunar light",
            "Corals spawn on nights set by moonlight and water temperature. Light loggers on the "
                + "reef matched spawning nights to the lunar cycle.",
            ["Keoni Kahale"],
            ["W50109"]
        ),
        OpenAlexWork(
            "W41008",
            "2025-03-12",
            "Snowpack density from repeat drone surveys",
            "Repeat drone surveys measure snow depth across a basin. Combined with a few snow pits, "
                + "they map snowpack density and stored water.",
            ["Lars Edvik", "Asha Rao"],
            ["W50110", "W50111"]
        ),
        OpenAlexWork(
            "W41009",
            "2025-03-26",
            "Beaver dams slow wildfire spread along creeks",
            "Creeks dammed by beavers stayed green through wildfire. Burn severity maps show fire "
                + "slowing where ponds kept the valley floor wet.",
            ["Ben Achterberg"],
            ["W50112"]
        ),
        // ── Crossref, January ────────────────────────────────────────────────
        CrossrefWork(
            "10.5555/TRAX.42001",
            "2025-01-09",
            "Tidal marsh accretion against sea level rise",
            "Tidal marshes build up sediment as the sea rises. Marker horizons show which marshes "
                + "keep pace and which are drowning.",
            [("Nadia", "Haddad"), ("Owen", "Pryce")],
            ["10.5555/trax.50201", "10.5555/trax.50202"]
        ),
        CrossrefWork(
            "10.5555/TRAX.42002",
            "2025-01-16",
            "Mangrove roots trap microplastics",
            "Mangrove roots trap microplastics carried in on the tide. Cores from the root zone hold "
                + "more fragments than the open mud beyond.",
            [("Siti", "Rahman")],
            ["10.5555/trax.50203"]
        ),
        CrossrefWork(
            "10.5555/TRAX.42003",
            "2025-01-30",
            "Earthworm burrows and infiltration in orchards",
            "Earthworm burrows let rain soak into orchard soils. Dye tracing follows water down the "
                + "burrows below the compacted wheel tracks.",
            [("Owen", "Pryce"), ("Clara", "Maier")],
            ["10.5555/trax.50204", "10.5555/trax.50205"]
        ),
        // ── Crossref, February: one is close to a corpus paper (W30028) ──────
        CrossrefWork(
            "10.5555/TRAX.42004",
            "2025-02-06",
            "Green roofs hold back stormwater in cities",
            "Green roofs store rain in their substrate and release it slowly, lowering the runoff "
                + "peak from city storms.",
            [("Eva", "Lund"), ("Clara", "Maier")],
            ["10.5555/trax.50206"]
        ),
        CrossrefWork(
            "10.5555/TRAX.42005",
            "2025-02-13",
            "Bat activity under dimmed street lighting",
            "Bats returned to streets where lights were dimmed after midnight. Acoustic detectors "
                + "logged passes before and after the change.",
            [("Siti", "Rahman"), ("Jonas", "Weber")],
            ["10.5555/trax.50207"]
        ),
        CrossrefWork(
            "10.5555/TRAX.42006",
            "2025-02-27",
            "Seagrass meadows store carbon in sediment",
            "Seagrass meadows bury carbon in the sediment beneath them. Cores dated by lead isotopes "
                + "give burial rates over a century.",
            [("Nadia", "Haddad")],
            ["10.5555/trax.50208", "10.5555/trax.50209"]
        ),
        // ── Crossref, March ──────────────────────────────────────────────────
        CrossrefWork(
            "10.5555/TRAX.42007",
            "2025-03-04",
            "Fog harvesting nets in coastal deserts",
            "Mesh nets on coastal desert ridges collect fog water. Yields depend on mesh, wind and "
                + "the height of the marine cloud layer.",
            [("Rosa", "Quispe")],
            ["10.5555/trax.50210"]
        ),
        CrossrefWork(
            "10.5555/TRAX.42008",
            "2025-03-18",
            "Wolf presence and riverbank vegetation",
            "Where wolves returned, browsing on riverbank willows fell and the willows grew taller. "
                + "Plots inside and outside wolf territories were compared.",
            [("Jonas", "Weber"), ("Rosa", "Quispe")],
            ["10.5555/trax.50211", "10.5555/trax.50212"]
        ),
        CrossrefWork(
            "10.5555/TRAX.42009",
            "2025-03-25",
            "Desert crust mosses fix nitrogen after rain",
            "Mosses in desert soil crusts fix nitrogen within hours of rain. Incubations of wetted "
                + "crust measured the burst and how fast it fades.",
            [("Clara", "Maier")],
            ["10.5555/trax.50213"]
        ),
    ];

    /// <summary>The key of a partition, as <c>Source/yyyy-MM</c>.</summary>
    public static string PartitionKey(string source, string month) => $"{source}/{month}";

    private static SourceRecord OpenAlexWork(
        string id,
        string date,
        string title,
        string @abstract,
        string[] authors,
        string[] references
    )
    {
        var payload = new JsonObject
        {
            ["id"] = $"https://openalex.org/{id}",
            ["doi"] = $"https://doi.org/10.5555/trax.{id[1..]}",
            ["display_name"] = title,
            ["publication_date"] = date,
            ["abstract_inverted_index"] = InvertedIndex(@abstract),
            ["authorships"] = new JsonArray(
                authors
                    .Select(name =>
                        (JsonNode)
                            new JsonObject
                            {
                                ["author"] = new JsonObject { ["display_name"] = name },
                            }
                    )
                    .ToArray()
            ),
            ["referenced_works"] = new JsonArray(
                references
                    .Select(r => (JsonNode)JsonValue.Create($"https://openalex.org/{r}")!)
                    .ToArray()
            ),
        };
        return Record(OpenAlex, id, date, payload);
    }

    private static SourceRecord CrossrefWork(
        string doi,
        string date,
        string title,
        string @abstract,
        (string Given, string Family)[] authors,
        string[] references
    )
    {
        var parts = date.Split('-').Select(int.Parse).ToArray();
        var payload = new JsonObject
        {
            ["DOI"] = doi,
            ["title"] = new JsonArray(JsonValue.Create(title)),
            ["abstract"] = $"<jats:p>{@abstract}</jats:p>",
            ["author"] = new JsonArray(
                authors
                    .Select(a =>
                        (JsonNode)new JsonObject { ["given"] = a.Given, ["family"] = a.Family }
                    )
                    .ToArray()
            ),
            ["issued"] = new JsonObject
            {
                ["date-parts"] = new JsonArray(
                    new JsonArray(parts.Select(p => (JsonNode)JsonValue.Create(p)).ToArray())
                ),
            },
            ["reference"] = new JsonArray(
                references
                    .Select(
                        (r, i) => (JsonNode)new JsonObject { ["key"] = $"ref{i + 1}", ["DOI"] = r }
                    )
                    .ToArray()
            ),
        };
        return Record(Crossref, doi, date, payload);
    }

    private static SourceRecord Record(string source, string id, string date, JsonObject payload) =>
        new()
        {
            Source = source,
            SourceId = id,
            Month = date[..7],
            Payload = payload.ToJsonString(),
        };

    // OpenAlex serves an abstract as each word and the positions it appears at, never as text.
    private static JsonObject InvertedIndex(string text)
    {
        var index = new JsonObject();
        var position = 0;
        foreach (var word in Whitespace().Split(text.Trim()))
        {
            if (index[word] is not JsonArray positions)
                index[word] = positions = [];
            positions.Add(JsonValue.Create(position++));
        }
        return index;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
