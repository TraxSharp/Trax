using Trax.Effect.Models.PersistedOperation;

namespace Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

/// <summary>
/// Thrown by <see cref="IPersistedOperationStore"/> when a change was saved and applied to this
/// node's caches but could not be sent to the other nodes: the broker did not confirm the
/// broadcast.
/// </summary>
/// <remarks>
/// The change is in the store and is not rolled back. Another node keeps serving what it cached
/// until that entry reaches its maximum age (<c>WithCacheMaxAge</c>), then reads the store
/// again. Repeating the change sends it again. See
/// <c>docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md</c>.
/// </remarks>
public sealed class PersistedOperationNotBroadcastException : PersistedOperationException
{
    /// <summary>Stable code surfaced via <see cref="Code"/>.</summary>
    public const string CodeValue = "CHANGE_NOT_BROADCAST";

    /// <inheritdoc />
    public override string Code => CodeValue;

    /// <summary>The id of the operation that changed.</summary>
    public string Id { get; }

    /// <summary>
    /// The operation as saved, when the change was an upload; null for a deactivation or a
    /// restore, whose callers already hold the row.
    /// </summary>
    public PersistedOperation? Operation { get; }

    /// <summary>Build the exception for a saved change whose broadcast failed.</summary>
    public PersistedOperationNotBroadcastException(
        string id,
        PersistedOperation? operation,
        Exception inner
    )
        : base(
            $"The change to persisted operation '{id}' is saved, but it could not be sent to the "
                + "other nodes. Each of them keeps serving what it cached until that entry reaches "
                + "its maximum age, then reads the store again. Repeat the change to send it again.",
            inner
        )
    {
        Id = id;
        Operation = operation;
    }
}
