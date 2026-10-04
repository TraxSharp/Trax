using Trax.Effect.Models.Metadata;

namespace Trax.Effect.Services.JunctionEvents;

/// <summary>
/// Works out which attempt of its manifest a run is, for its junction events. Implemented by
/// Trax.Effect.Data, which <c>AddJunctionEvents</c> registers.
/// </summary>
internal interface IRunAttempts
{
    /// <summary>
    /// 1 plus the number of the manifest's failed runs since its last completed or cancelled one,
    /// before <paramref name="metadata"/>; null for a run with no manifest. May throw: the caller
    /// logs and publishes no attempt.
    /// </summary>
    Task<int?> AttemptOf(Metadata metadata, CancellationToken cancellationToken);
}
