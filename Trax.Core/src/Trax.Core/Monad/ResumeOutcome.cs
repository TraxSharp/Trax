using Trax.Core.Decisions;
using Trax.Core.Functional;

namespace Trax.Core.Monad;

/// <summary>
/// Whether a run can resume, and from where: the plan when it can, the reason when it cannot.
/// </summary>
/// <param name="RefusalCode">Why it cannot, as a stable code, or null when it can. See <see cref="ResumeRefusals"/>.</param>
/// <param name="Refusal">Why it cannot, phrased for an operator, or null when it can.</param>
/// <param name="Target">The node the resumed run skips to, or null to run the main chain from the top.</param>
/// <param name="Inclusive">True when the target is a checkpoint the run restores rather than runs.</param>
/// <param name="MainCheckpoint">The checkpoint the main chain restores, or null.</param>
/// <param name="BranchCheckpoints">The checkpoint each branch restores, by branch path.</param>
/// <param name="StateTypes">Each restored checkpoint's declared state type, by node id.</param>
/// <param name="TrackTypes">
/// The <see cref="TrackTaken{TKey}"/> types each restored checkpoint can hold, by node id: one
/// for every routing step before it on its path.
/// </param>
internal sealed record ResumeOutcome(
    string? RefusalCode,
    string? Refusal,
    string? Target,
    bool Inclusive,
    string? MainCheckpoint,
    IReadOnlyDictionary<string, string> BranchCheckpoints,
    IReadOnlyDictionary<string, Type> StateTypes,
    IReadOnlyDictionary<string, IReadOnlyList<Type>> TrackTypes
)
{
    /// <summary>True when the run can resume as planned.</summary>
    public bool CanResume => RefusalCode is null;

    internal static ResumeOutcome Refused(string code, string reason) =>
        new(
            code,
            reason,
            Target: null,
            Inclusive: false,
            MainCheckpoint: null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, Type>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<Type>>(StringComparer.Ordinal)
        );
}
