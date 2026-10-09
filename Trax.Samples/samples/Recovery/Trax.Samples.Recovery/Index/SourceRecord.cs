using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Samples.Recovery.Index;

/// <summary>
/// One record as a scholarly index serves it, kept exactly as it came: an OpenAlex work or a
/// Crossref item, in that index's own JSON shape. Each belongs to one partition, a source and a
/// month, which is the unit the ingest reads.
/// </summary>
[Table("source_records")]
public class SourceRecord
{
    /// <summary>The index it came from, <see cref="IndexFixture.OpenAlex"/> or <see cref="IndexFixture.Crossref"/>.</summary>
    public string Source { get; set; } = "";

    /// <summary>The index's own id for the record: an OpenAlex work id, or a Crossref DOI.</summary>
    public string SourceId { get; set; } = "";

    /// <summary>The month the index published it, as <c>yyyy-MM</c>.</summary>
    public string Month { get; set; } = "";

    /// <summary>The record's JSON, in the index's shape.</summary>
    public string Payload { get; set; } = "";
}
