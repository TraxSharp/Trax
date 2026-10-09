namespace Trax.Core.Exceptions;

/// <summary>
/// A run was asked to stop by something other than its own token: the persisted cancel flag the
/// dashboard, the API or a scheduler's timeout sets for a run that may be executing elsewhere.
/// </summary>
/// <remarks>
/// It is an <see cref="OperationCanceledException"/>, so everything that treats a cancellation as
/// one still does. What it adds is that somebody asked for it. Any other
/// <see cref="OperationCanceledException"/> whose token is not the run's, an <c>HttpClient</c>
/// timeout being the common one, arrives as the same type but nobody asked for it: inside a
/// <c>Parallel</c> branch it fails the branch, where this cancels the run.
/// </remarks>
/// <param name="message">What asked the run to stop.</param>
public sealed class CancellationRequestedException(string message)
    : OperationCanceledException(message);
