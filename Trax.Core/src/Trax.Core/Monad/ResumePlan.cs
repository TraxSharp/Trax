using System.Diagnostics.CodeAnalysis;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

/// <summary>
/// What a resumed run restores and where it starts: the node it skips to, and the checkpoint each
/// chain or branch restores. Built by Trax.Effect from <see cref="ChainVerification.CheckResume"/>
/// and the stored rows.
/// </summary>
/// <param name="Target">
/// The node the run skips to, or null to run the main chain from the top (when only branches have
/// checkpoints to restore).
/// </param>
/// <param name="Inclusive">
/// True to skip the target as well (a checkpoint, restored rather than run); false to run it.
/// </param>
/// <param name="Restored">
/// The checkpoint each chain restores, keyed by branch path, with the main chain under the empty
/// string.
/// </param>
internal sealed record ResumePlan(
    string? Target,
    bool Inclusive,
    IReadOnlyDictionary<string, RestoredCheckpoint> Restored
);
