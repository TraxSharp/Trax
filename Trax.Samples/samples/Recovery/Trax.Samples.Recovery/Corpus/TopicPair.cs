using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Samples.Recovery.Corpus;

/// <summary>
/// Two works a topic map run placed on the same topic, with the three signals that put them there.
/// Written by the join step, <c>CombineSignals</c>, and by nothing else.
/// </summary>
[Table("topic_pairs")]
public class TopicPair
{
    public long Id { get; set; }

    /// <summary>The run that wrote the pair.</summary>
    public string RunId { get; set; } = "";

    /// <summary>The lower of the two work ids.</summary>
    public string WorkA { get; set; } = "";

    /// <summary>The higher of the two work ids.</summary>
    public string WorkB { get; set; } = "";

    /// <summary>How alike the two abstracts read, from 0 to 1.</summary>
    public double Embedding { get; set; }

    /// <summary>How many works both of them cite.</summary>
    public int SharedReferences { get; set; }

    /// <summary>How many authors wrote both.</summary>
    public int SharedAuthors { get; set; }

    /// <summary>The three signals weighed together, from 0 to 1.</summary>
    public double Score { get; set; }
}
