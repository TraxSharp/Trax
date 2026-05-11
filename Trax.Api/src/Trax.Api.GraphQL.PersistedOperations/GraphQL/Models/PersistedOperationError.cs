using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>
/// Structured error surfaced inside a mutation payload. Mutations never throw
/// to the GraphQL caller; failures are returned in the <c>errors</c> field
/// with a stable <c>code</c> so clients can branch without string-matching.
/// </summary>
public sealed record PersistedOperationError(
    string Code,
    string Message,
    IReadOnlyList<PersistedOperationErrorLocation>? Locations,
    IReadOnlyList<string>? Path,
    string? OldFingerprint,
    string? NewFingerprint
)
{
    internal static PersistedOperationError FromParseException(PersistedOperationParseException ex)
    {
        var locs =
            ex.Line is { } line && ex.Column is { } column
                ? new[] { new PersistedOperationErrorLocation(line, column) }
                : null;
        return new(
            ex.Code,
            ex.OriginalMessage,
            locs,
            Path: null,
            OldFingerprint: null,
            NewFingerprint: null
        );
    }

    internal static IEnumerable<PersistedOperationError> FromValidationException(
        PersistedOperationValidationException ex
    )
    {
        foreach (var failure in ex.Failures)
        {
            var locs =
                failure.Locations.Count > 0
                    ? failure
                        .Locations.Select(l => new PersistedOperationErrorLocation(
                            l.Line,
                            l.Column
                        ))
                        .ToArray()
                    : null;
            var path =
                failure.Path.Count > 0
                    ? failure.Path.Select(p => p.ToString() ?? "").ToArray()
                    : null;
            yield return new PersistedOperationError(
                ex.Code,
                failure.Message,
                locs,
                path,
                null,
                null
            );
        }
    }

    internal static PersistedOperationError FromShapeDiff(ShapeDiffViolationException ex) =>
        new(ex.Code, ex.Message, Locations: null, Path: null, ex.OldFingerprint, ex.NewFingerprint);

    /// <summary>Build a NOT_FOUND error.</summary>
    public static PersistedOperationError NotFound(string id) =>
        new(
            "NOT_FOUND",
            $"Persisted operation '{id}' was not found.",
            Locations: null,
            Path: null,
            OldFingerprint: null,
            NewFingerprint: null
        );
}

/// <summary>1-based location in the candidate document.</summary>
public sealed record PersistedOperationErrorLocation(int Line, int Column);
