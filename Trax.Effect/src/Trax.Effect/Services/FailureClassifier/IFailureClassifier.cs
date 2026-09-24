using Trax.Core.Exceptions;

namespace Trax.Effect.Services.FailureClassifier;

/// <summary>
/// Decides what kind of failure an exception represents. Register one to have Trax record a
/// <see cref="FailureClass"/> on runs that fail.
/// </summary>
/// <remarks>
/// Optional: with none registered, failures record <see cref="FailureClass.Unclassified"/> and
/// nothing changes. Register it in the container, for example
/// <c>services.AddSingleton&lt;IFailureClassifier, MyClassifier&gt;()</c>. One is used; the last
/// registration wins. A run executed on a remote worker is classified by the classifier
/// registered in the worker's process, and the calling side records the answer it is sent.
///
/// A class the failure already carries, such as one sent back by a remote worker, is kept
/// rather than re-derived. Runs failed by the scheduler rather than by the train (a dispatch
/// failure, or a run the stale-run reaper fails) are not classified and record
/// <see cref="FailureClass.Unclassified"/>.
///
/// It is called where the failure happens, holding the original exception object rather than a
/// wrapper, so it can type-check and read structured error data instead of parsing a message.
///
/// Throwing is not fatal — the exception is logged and the failure records as unclassified. A
/// classifier must never be able to mask the failure it was asked about.
/// </remarks>
public interface IFailureClassifier
{
    /// <summary>
    /// Returns the class of this failure, or null to leave it unclassified.
    /// </summary>
    FailureClass? Classify(Exception exception);
}
