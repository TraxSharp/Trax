using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Samples.Recovery.Index;

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
