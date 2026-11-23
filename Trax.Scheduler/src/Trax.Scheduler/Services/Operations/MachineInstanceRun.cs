using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// One train run a state-machine instance invoked, as an operator reads it: the fields the run
/// listings show, never its input or output.
/// </summary>
/// <param name="Id">The run's id (its metadata row), which the run's own page is keyed by.</param>
/// <param name="ExternalId">The run's external id.</param>
/// <param name="TrainName">The train that ran (the train interface's full name).</param>
/// <param name="TrainState">Where the run is.</param>
/// <param name="StartTime">When it started (UTC).</param>
/// <param name="EndTime">When it ended (UTC), or null while it has not.</param>
/// <param name="FailureClass">How its failure was classified, or <c>Unclassified</c>.</param>
/// <param name="CancellationRequested">Whether a cancel has been requested for it.</param>
/// <param name="IsLive">
/// True for the run the instance's current state waits on: its external id is the instance's
/// invoke token. At most one run of an instance is live.
/// </param>
public record MachineInstanceRun(
    long Id,
    string ExternalId,
    string TrainName,
    TrainState TrainState,
    DateTime StartTime,
    DateTime? EndTime,
    FailureClass FailureClass,
    bool CancellationRequested,
    bool IsLive
);
