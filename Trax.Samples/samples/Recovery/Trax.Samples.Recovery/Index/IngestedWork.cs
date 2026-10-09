using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Samples.Recovery.Index;

/// <summary>
/// A work the ingest has read from an index, normalised, and resolved against the works the corpus
/// already holds. Keyed by where it came from, so ingesting a partition again rewrites its own rows
/// and adds none.
/// </summary>
[Table("ingested_works")]
public class IngestedWork
{
    public string Source { get; set; } = "";

    public string SourceId { get; set; } = "";

    /// <summary>The partition's month, as <c>yyyy-MM</c>.</summary>
    public string Month { get; set; } = "";

    /// <summary>The DOI, lower case and without a resolver prefix, when the index gave one.</summary>
    public string? Doi { get; set; }

    public string Title { get; set; } = "";

    public string Abstract { get; set; } = "";

    public int Year { get; set; }

    public List<string> Authors { get; set; } = [];

    public List<string> References { get; set; } = [];

    /// <summary>
    /// A SHA-256 of the normalised content, so a later step can tell which works changed since
    /// they were last read.
    /// </summary>
    public string ContentHash { get; set; } = "";

    /// <summary>
    /// <see cref="Resolutions.Created"/>, <see cref="Resolutions.Merged"/> or
    /// <see cref="Resolutions.NeedsReview"/>.
    /// </summary>
    public string Resolution { get; set; } = "";

    /// <summary>The corpus work it was merged into, or proposed for when it needs review.</summary>
    public string? ExistingWorkId { get; set; }
}

/// <summary>What the ingest decided about one work.</summary>
public static class Resolutions
{
    /// <summary>A work the corpus did not hold: added as new.</summary>
    public const string Created = "Created";

    /// <summary>The same work as one the corpus holds: linked to it.</summary>
    public const string Merged = "Merged";

    /// <summary>Possibly a work the corpus holds: kept aside for a person to decide.</summary>
    public const string NeedsReview = "NeedsReview";
}
