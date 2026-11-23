using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Monad;
using Trax.Core.Utils;
using Trax.Effect.Services.JunctionEvents;

namespace Trax.Effect.Services.Checkpoints;

/// <summary>
/// Whether a run can resume, and where: what an operator surface shows and what a resumed run
/// acts on.
/// </summary>
/// <param name="CanResume">True when the run can resume as asked.</param>
/// <param name="Code">Why it cannot, as a stable code (<c>no-checkpoint</c>, <c>missing-input</c>, <c>chain-changed</c> and the rest), or null.</param>
/// <param name="Reason">Why it cannot, phrased for an operator, or null.</param>
/// <param name="Target">The step the resumed run skips to, or null to run its main chain from the top.</param>
/// <param name="Checkpoint">The checkpoint its main chain restores, or null.</param>
internal sealed record ResumeVerdict(
    bool CanResume,
    string? Code,
    string? Reason,
    string? Target,
    string? Checkpoint
);
