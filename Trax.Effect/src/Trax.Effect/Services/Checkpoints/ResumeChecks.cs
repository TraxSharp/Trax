using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Monad;
using Trax.Core.Utils;
using Trax.Effect.Services.JunctionEvents;

namespace Trax.Effect.Services.Checkpoints;

/// <summary>
/// A run's resume verdicts and checkpoints, as <see cref="IRunResumes.CheckMany"/> reads them. It
/// names nodes only: never a checkpoint's stored state or the tracks stored with it.
/// </summary>
/// <param name="Latest">Whether the run can resume after its latest checkpoint.</param>
/// <param name="At">Whether it can resume at each point asked about, by node id.</param>
/// <param name="Checkpoints">
/// The nodes holding a checkpoint the run can resume from: those it wrote, and for a resumed run,
/// those the runs it resumed wrote before the point it resumed at.
/// </param>
/// <param name="Written">The nodes at which the run itself wrote a checkpoint.</param>
/// <param name="Restored">
/// For a resumed run, the nodes before the point it resumed at, which it skipped and whose work
/// the checkpoint restored; empty for any other run, or when the resume can no longer be planned
/// against the running chain.
/// </param>
internal sealed record ResumeChecks(
    ResumeVerdict Latest,
    IReadOnlyDictionary<string, ResumeVerdict> At,
    IReadOnlyCollection<string> Checkpoints,
    IReadOnlyCollection<string> Written,
    IReadOnlyCollection<string> Restored
)
{
    /// <summary>
    /// For a resumed run, the track each routing step before the point it resumed at took, by
    /// node id, as the checkpoint it restored stored it. The run recorded no step for those
    /// routing steps, so this is how the tracks it passed over show as skipped. A route the
    /// checkpoint withheld (its key is marked sensitive) is not here.
    /// </summary>
    public IReadOnlyDictionary<string, string> RestoredTracks { get; init; } =
        new Dictionary<string, string>();
}
