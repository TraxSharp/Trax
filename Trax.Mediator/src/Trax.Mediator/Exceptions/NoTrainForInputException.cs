namespace Trax.Mediator.Exceptions;

/// <summary>
/// Thrown by the train bus when no registered train takes the input it was given. This is a
/// configuration error in the host: the train was never registered, or its assembly was not
/// scanned.
/// </summary>
/// <remarks>
/// The <see cref="Exception.Message"/> is written for whoever runs the host. It names the input
/// type and the assemblies the registry scanned, which say how the host is built, so log it and
/// do not return it to a caller. It is deliberately not a <c>TrainException</c>, whose message a
/// surface may pass through as a train author's words; a surface that masks other exceptions masks
/// this one too.
/// </remarks>
public class NoTrainForInputException : InvalidOperationException
{
    /// <summary>The runtime type of the input no registered train takes.</summary>
    public Type InputType { get; }

    /// <summary>
    /// The names of the assemblies the registry scanned for trains, or empty when the registry
    /// does not scan.
    /// </summary>
    public IReadOnlyList<string> ScannedAssemblies { get; }

    /// <summary>Creates the exception; the message names the input type and the scanned assemblies.</summary>
    /// <param name="inputType">The input type no registered train takes.</param>
    /// <param name="scannedAssemblies">The assemblies the registry scanned, if it scans.</param>
    public NoTrainForInputException(Type inputType, IReadOnlyList<string> scannedAssemblies)
        : base(Describe(inputType, scannedAssemblies))
    {
        InputType = inputType;
        ScannedAssemblies = scannedAssemblies;
    }

    private static string Describe(Type inputType, IReadOnlyList<string> scannedAssemblies) =>
        $"Could not find train with input type ({inputType.FullName}): no "
        + $"IServiceTrain<{inputType.Name}, TOut> is registered for it. "
        + (
            scannedAssemblies.Count > 0
                ? $"Scanned assemblies: [{string.Join(", ", scannedAssemblies)}]. "
                : ""
        )
        + "Add a train that takes this input type, or add the assembly that holds its train to "
        + "ScanAssemblies(...).";
}
