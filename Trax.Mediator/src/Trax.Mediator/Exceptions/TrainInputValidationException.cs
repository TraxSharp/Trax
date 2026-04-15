namespace Trax.Mediator.Exceptions;

/// <summary>
/// Thrown by <see cref="Services.TrainExecution.TrainExecutionService"/> when the
/// caller-supplied input JSON fails a pre-deserialization check (for example, it
/// exceeds the configured maximum size).
/// </summary>
/// <remarks>
/// The public <see cref="Exception.Message"/> is intentionally generic. Detailed
/// context (the offending size, the cap, etc.) is available on properties for
/// server-side logging.
/// </remarks>
public class TrainInputValidationException : InvalidOperationException
{
    public string TrainName { get; }
    public int ObservedBytes { get; }
    public int MaxBytes { get; }

    public TrainInputValidationException(string trainName, int observedBytes, int maxBytes)
        : base("The train input failed validation.")
    {
        TrainName = trainName;
        ObservedBytes = observedBytes;
        MaxBytes = maxBytes;
    }
}
