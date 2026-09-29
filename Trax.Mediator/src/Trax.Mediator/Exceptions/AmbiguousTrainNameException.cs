namespace Trax.Mediator.Exceptions;

/// <summary>
/// Thrown when a caller references a train by a friendly (short) name that matches
/// more than one registered train. Disambiguate by passing the interface
/// <see cref="Type.FullName"/> instead.
/// </summary>
/// <remarks>
/// Registered trains that share an unqualified class name across different namespaces
/// are allowed. The lookup only fails when the disambiguating name (<see cref="Type.FullName"/>)
/// is not provided. This exception is thrown as a fail-closed guard rather than picking
/// an arbitrary candidate.
/// </remarks>
public class AmbiguousTrainNameException : InvalidOperationException
{
    /// <summary>The name the caller passed, which matched more than one train's short name.</summary>
    public string RequestedName { get; }

    /// <summary>
    /// The <see cref="Type.FullName"/> of each matching train's service interface. Any one of them
    /// resolves unambiguously. Also listed in the message.
    /// </summary>
    public IReadOnlyList<string> CandidateFullNames { get; }

    /// <summary>Creates the exception; the message lists every candidate.</summary>
    /// <param name="requestedName">The ambiguous name the caller passed.</param>
    /// <param name="candidateFullNames">The full names of the trains it matched.</param>
    public AmbiguousTrainNameException(
        string requestedName,
        IReadOnlyList<string> candidateFullNames
    )
        : base(
            $"Train name '{requestedName}' is ambiguous. Matching registrations: "
                + string.Join(", ", candidateFullNames)
                + ". Pass the interface FullName to disambiguate."
        )
    {
        RequestedName = requestedName;
        CandidateFullNames = candidateFullNames;
    }
}
