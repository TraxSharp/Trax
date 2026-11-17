using Trax.Core.Extensions;
using Trax.Core.Functional;

namespace Trax.Effect.Models.JunctionMetadata.DTOs;

/// <summary>
/// The per-junction values <see cref="JunctionMetadata.Create"/> combines with the run's
/// <c>Metadata</c>. <c>EffectJunction</c> builds one each time a junction is reached; code outside
/// Trax rarely needs it.
/// </summary>
public class CreateJunctionMetadata
{
    /// <summary>
    /// The junction's display name. <c>EffectJunction</c> passes the junction class's short name
    /// (<c>GetType().Name</c>), not its FullName.
    /// </summary>
    public string Name
    {
        get => field.NotNull(nameof(Name));
        set;
    }

    /// <summary>
    /// An identifier for this one execution of the junction. <c>EffectJunction</c> passes a new
    /// GUID in 32-digit <c>"N"</c> format.
    /// </summary>
    public string ExternalId
    {
        get => field.NotNull(nameof(ExternalId));
        set;
    }

    /// <summary>
    /// UTC time the junction started, or null to leave it unset. <c>EffectJunction</c> passes
    /// null and stamps the time itself after the before-junction effects have run.
    /// </summary>
    public DateTime? StartTimeUtc { get; set; }

    /// <summary>
    /// UTC time the junction finished, or null to leave it unset. <c>EffectJunction</c> passes
    /// null and stamps the time when the junction returns.
    /// </summary>
    public DateTime? EndTimeUtc { get; set; }

    /// <summary>The junction's <c>TIn</c> type argument.</summary>
    public Type InputType
    {
        get => field.NotNull(nameof(InputType));
        set;
    }

    /// <summary>The junction's <c>TOut</c> type argument.</summary>
    public Type OutputType
    {
        get => field.NotNull(nameof(OutputType));
        set;
    }

    /// <summary>
    /// The state of the railway arriving at the junction: <see cref="EitherStatus.IsRight"/> when
    /// the previous junction succeeded, <see cref="EitherStatus.IsLeft"/> when an earlier junction
    /// already failed and this one will be skipped.
    /// </summary>
    public EitherStatus State { get; set; }
}
