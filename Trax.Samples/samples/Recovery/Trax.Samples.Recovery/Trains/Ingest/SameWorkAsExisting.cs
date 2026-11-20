using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

[Asks(
    "Are the works in this partition that look like works the corpus already holds the same works?",
    Yes = "Merge them: each is the same paper as the corpus work it matches.",
    No = "Keep them apart: the titles are alike but the papers are different."
)]
public sealed class SameWorkAsExisting;
