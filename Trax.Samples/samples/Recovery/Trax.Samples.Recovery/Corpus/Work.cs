using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Samples.Recovery.Corpus;

/// <summary>
/// One paper, shaped like a work from a scholarly index (OpenAlex, Crossref): who wrote it, what it
/// cites, the concepts it was tagged with. The topic map reads these and never writes them.
/// </summary>
[Table("works")]
public class Work
{
    /// <summary>The index's id for the work, for example <c>W30001</c>.</summary>
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Abstract { get; set; } = "";

    public int Year { get; set; }

    /// <summary>The field the index files it under.</summary>
    public string Field { get; set; } = "";

    public List<string> Authors { get; set; } = [];

    /// <summary>The ids of the works it cites. Most are outside this corpus, as in a real index.</summary>
    public List<string> References { get; set; } = [];

    public List<string> Concepts { get; set; } = [];
}
