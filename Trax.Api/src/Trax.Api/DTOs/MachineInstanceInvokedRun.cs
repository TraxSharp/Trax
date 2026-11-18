using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// One train run a state-machine instance invoked: the fields the run listings show, never its
/// input or output. <c>executionDetail(id)</c> reads the rest, with sensitive members masked.
/// </summary>
/// <param name="Id">The execution's id.</param>
/// <param name="ExternalId">The execution's external id.</param>
/// <param name="Name">The train that ran (the train interface's full name).</param>
/// <param name="TrainState">Where the run is.</param>
/// <param name="StartTime">When it started (UTC).</param>
/// <param name="EndTime">When it ended (UTC), or <see langword="null"/> while it has not.</param>
/// <param name="FailureClass">How its failure was classified, or <c>Unclassified</c>.</param>
/// <param name="CancellationRequested">Whether a cancel has been requested for it.</param>
/// <param name="IsLive">True for the run the instance's current state waits on.</param>
public record MachineInstanceInvokedRun(
    long Id,
    string ExternalId,
    string Name,
    TrainState TrainState,
    DateTime StartTime,
    DateTime? EndTime,
    Trax.Core.Exceptions.FailureClass FailureClass,
    bool CancellationRequested,
    bool IsLive
)
{
    internal static MachineInstanceInvokedRun From(MachineInstanceRun run) =>
        new(
            run.Id,
            run.ExternalId,
            run.TrainName,
            run.TrainState,
            run.StartTime,
            run.EndTime,
            run.FailureClass,
            run.CancellationRequested,
            run.IsLive
        );
}
