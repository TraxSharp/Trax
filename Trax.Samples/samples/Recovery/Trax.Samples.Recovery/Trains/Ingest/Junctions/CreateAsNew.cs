using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Index;

namespace Trax.Samples.Recovery.Trains.Ingest.Junctions;

/// <summary>The <c>No</c> track: the close matches are different papers, and are created as new.</summary>
public class CreateAsNew(DemoPace pace) : ResolveWorks(pace, Resolutions.Created, unsure: false);
