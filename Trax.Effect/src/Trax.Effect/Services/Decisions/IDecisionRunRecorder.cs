using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.Decisions;

/// <summary>
/// Prepares the recording of one run's decisions before its junctions run. Implemented by
/// <c>Trax.Effect.Data</c>'s decision journal, which <c>AddDecisionRecording</c> registers.
/// </summary>
internal interface IDecisionRunRecorder
{
    /// <summary>
    /// Binds the run to its row and loads the answers it replays. Called once per run, after the
    /// row is saved and the start hooks have run, and before the first junction. Whatever it
    /// throws fails the run.
    /// </summary>
    /// <param name="metadata">The run's row.</param>
    /// <param name="train">The train's own type, which Trax.Core names its decisions after.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<DecisionRun> Begin(Metadata metadata, Type train, CancellationToken cancellationToken);
}
