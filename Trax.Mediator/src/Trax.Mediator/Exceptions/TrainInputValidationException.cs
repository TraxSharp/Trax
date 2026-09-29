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
    /// <summary>The short (unqualified) service type name of the train whose input was refused.</summary>
    public string TrainName { get; }

    /// <summary>The UTF-8 byte length of the input JSON the caller sent.</summary>
    public int ObservedBytes { get; }

    /// <summary>The cap in force, from <c>MediatorConfiguration.MaxInputJsonBytes</c>.</summary>
    public int MaxBytes { get; }

    /// <summary>
    /// Creates the exception. The message is always "The train input failed validation."; the
    /// arguments go to the properties only.
    /// </summary>
    /// <param name="trainName">The short service type name of the train.</param>
    /// <param name="observedBytes">The UTF-8 byte length of the input.</param>
    /// <param name="maxBytes">The configured maximum.</param>
    public TrainInputValidationException(string trainName, int observedBytes, int maxBytes)
        : base("The train input failed validation.")
    {
        TrainName = trainName;
        ObservedBytes = observedBytes;
        MaxBytes = maxBytes;
    }
}
