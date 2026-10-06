namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Request describing a train to run now, on a worker, without going through the work queue.
/// Shared by the dashboard's Run dialog and any API surface that exposes a run, so both read
/// the input, authorize and submit the same way.
/// </summary>
/// <param name="TrainName">
/// Fully qualified name of the train interface, the same name
/// <see cref="QueueTrainInput.TrainName"/> takes.
/// </param>
/// <param name="InputJson">
/// JSON payload that deserializes to the train's input type. Property names match whatever their
/// case, and a property given twice is refused (<c>docs/0023</c>). <c>null</c> or blank is read as
/// an empty object, so a train whose input needs no values can be run without one.
/// </param>
public record RunTrainInput(string TrainName, string? InputJson = null);
