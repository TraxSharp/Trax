using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Topics;

[Asks(
    "In this slice of papers, do papers that cite the same works tend to be about the same topic?",
    Yes = "Trust it: shared references mark papers on one topic here.",
    No = "Ignore it: shared references here are standard citations every paper carries."
)]
public sealed class SameTopic;
