using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Thrown by <see cref="IPersistedOperationStore.UpsertAsync"/> when an
/// edit would change the response shape of an existing persisted
/// operation. Pass <see cref="UpsertOptions.BypassShapeDiff"/> = true
/// (the dashboard <c>--force</c> path) when the operator has verified
/// the change is shape-safe.
/// </summary>
public sealed class ShapeDiffViolationException : PersistedOperationException
{
    /// <summary>Stable code surfaced via <see cref="Code"/>.</summary>
    public const string CodeValue = "SHAPE_DIFF_VIOLATION";

    /// <inheritdoc />
    public override string Code => CodeValue;

    /// <summary>
    /// The id of the operation whose shape would change.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Fingerprint stored on the existing row.
    /// </summary>
    public string OldFingerprint { get; }

    /// <summary>
    /// Fingerprint computed from the proposed new document.
    /// </summary>
    public string NewFingerprint { get; }

    /// <summary>
    /// Build the exception. Internal constructor; produced by the storage
    /// layer when a shape-changing edit is rejected.
    /// </summary>
    public ShapeDiffViolationException(string id, string oldFingerprint, string newFingerprint)
        : base(
            $"Persisted operation '{id}' edit rejected: response shape changed "
                + $"(old fingerprint {oldFingerprint[..8]}…, new {newFingerprint[..8]}…). "
                + "Pass UpsertOptions { BypassShapeDiff = true } if the change is shape-safe."
        )
    {
        Id = id;
        OldFingerprint = oldFingerprint;
        NewFingerprint = newFingerprint;
    }
}
