namespace Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

/// <summary>
/// Base class for every exception thrown by the persisted-operations storage
/// layer that represents a refused change, or a saved change that did not reach
/// every node (as opposed to an internal fault). Callers wrapping
/// <see cref="IPersistedOperationStore"/> can catch this single type to render
/// a structured error to the user; <see cref="PersistedOperationNotBroadcastException"/>
/// is the one whose change was saved.
/// </summary>
public abstract class PersistedOperationException : InvalidOperationException
{
    /// <summary>
    /// Stable machine-readable code for the failure category. Consumers
    /// (GraphQL error payloads, dashboard form errors) key off this rather
    /// than the exception subtype.
    /// </summary>
    public abstract string Code { get; }

    /// <summary>Build the exception with a human-readable message.</summary>
    protected PersistedOperationException(string message)
        : base(message) { }

    /// <summary>Build the exception with a human-readable message and inner cause.</summary>
    protected PersistedOperationException(string message, Exception inner)
        : base(message, inner) { }
}
