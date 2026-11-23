namespace Trax.Mediator.Services.TrainExecution;

/// <summary>How a train is run, beyond its name and input.</summary>
public sealed record RunTrainOptions
{
    /// <summary>Records the current caller as the run's starter, as <see cref="QueueTrainOptions.RecordCaller"/> does.</summary>
    public bool RecordCaller { get; init; }
}
