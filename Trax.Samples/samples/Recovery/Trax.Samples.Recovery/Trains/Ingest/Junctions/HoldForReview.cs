using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Index;

namespace Trax.Samples.Recovery.Trains.Ingest.Junctions;

/// <summary>
/// The <c>Unsure</c> track: the close matches are kept aside for a person, with the corpus work
/// each might be.
/// </summary>
public class HoldForReview(DemoPace pace)
    : ResolveWorks(pace, Resolutions.NeedsReview, unsure: true);
