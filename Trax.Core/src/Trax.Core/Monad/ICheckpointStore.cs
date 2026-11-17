using System.Diagnostics.CodeAnalysis;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

/// <summary>
/// Where a run's checkpoints are stored. Trax.Effect implements it over the run's database; a
/// train run without one, as a plain <c>Train</c> is, takes no checkpoint and the step does
/// nothing.
/// </summary>
internal interface ICheckpointStore
{
    /// <summary>
    /// Stores <paramref name="checkpoint"/>. A failure it throws fails the step, classified as the
    /// exception says.
    /// </summary>
    Task Write(CheckpointTaken checkpoint, CancellationToken cancellationToken);
}
