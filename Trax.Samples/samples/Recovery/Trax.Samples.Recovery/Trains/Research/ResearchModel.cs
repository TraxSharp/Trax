using System.ComponentModel;
using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Research;

/// <summary>What the agent is asked to research, and for whom. The first model call is about it.</summary>
public sealed record ResearchBrief(string RunId, string Topic, string Audience);

/// <summary>What the search found. The second model call is about it.</summary>
public sealed record Findings(
    string RunId,
    string Topic,
    string Source,
    IReadOnlyList<string> Notes
);

/// <summary>The findings after the step the depth decision chose.</summary>
public sealed record CheckedFindings(Findings Findings, string Depth);

/// <summary>The train's output.</summary>
public sealed record ResearchReport(string Topic, string Source, string Depth, string Summary);

[Asks("Where should a research agent look first to answer this brief?")]
public enum Source
{
    [Description("The public web: news, vendor pages and blog posts. Best for a broad audience.")]
    Web,

    [Description("Peer-reviewed papers and preprints. Best when the topic is a research question.")]
    Papers,

    [Description("The company wiki and past reports. Best for an internal, engineering audience.")]
    Wiki,
}

[Asks("How much more digging do these findings need before they can be summarized?")]
public enum Depth
{
    [Description("They are enough: skim them and summarize.")]
    Skim,

    [Description("They need checking against the full texts before anyone relies on them.")]
    CrossCheck,
}
