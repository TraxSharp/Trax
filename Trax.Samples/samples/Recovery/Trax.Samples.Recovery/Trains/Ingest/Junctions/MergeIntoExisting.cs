using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Index;

namespace Trax.Samples.Recovery.Trains.Ingest.Junctions;

/// <summary>The <c>Yes</c> track: each close match is the corpus work it matches, and is linked to it.</summary>
public class MergeIntoExisting(DemoPace pace)
    : ResolveWorks(pace, Resolutions.Merged, unsure: false);
