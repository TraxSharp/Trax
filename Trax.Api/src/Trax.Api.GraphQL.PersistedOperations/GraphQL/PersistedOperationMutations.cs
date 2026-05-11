using HotChocolate;
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL;

/// <summary>
/// Body of the <c>operations.persistedOperations</c> mutation namespace.
/// Each mutation wraps <see cref="IPersistedOperationStore"/> and projects
/// structured exceptions into payload <c>errors[]</c> entries with stable
/// <c>code</c> values; mutations never throw to the client.
/// </summary>
public sealed class PersistedOperationMutations
{
    /// <summary>
    /// Upload (insert or update) a persisted operation. Validates the
    /// document against the live schema; rejects shape-changing edits unless
    /// <c>bypassShapeDiff</c> is set.
    /// </summary>
    public async Task<UploadPersistedOperationPayload> UploadPersistedOperation(
        UploadPersistedOperationInput input,
        [Service] IPersistedOperationStore store,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Id))
            return UploadPersistedOperationPayload.Fail(
                new PersistedOperationError(
                    "INVALID_INPUT",
                    "id is required.",
                    Locations: null,
                    Path: null,
                    OldFingerprint: null,
                    NewFingerprint: null
                )
            );
        if (string.IsNullOrWhiteSpace(input.Document))
            return UploadPersistedOperationPayload.Fail(
                new PersistedOperationError(
                    "INVALID_INPUT",
                    "document is required.",
                    Locations: null,
                    Path: null,
                    OldFingerprint: null,
                    NewFingerprint: null
                )
            );

        try
        {
            var row = await store
                .UpsertAsync(
                    input.Id,
                    input.Document,
                    new UpsertOptions
                    {
                        TenantKey = input.TenantKey,
                        Description = input.Description,
                        BypassShapeDiff = input.BypassShapeDiff,
                        Version = input.Version,
                    },
                    ct
                )
                .ConfigureAwait(false);
            return UploadPersistedOperationPayload.Ok(PersistedOperationDto.From(row));
        }
        catch (PersistedOperationParseException ex)
        {
            return UploadPersistedOperationPayload.Fail(
                PersistedOperationError.FromParseException(ex)
            );
        }
        catch (PersistedOperationValidationException ex)
        {
            return UploadPersistedOperationPayload.Fail(
                PersistedOperationError.FromValidationException(ex)
            );
        }
        catch (ShapeDiffViolationException ex)
        {
            return UploadPersistedOperationPayload.Fail(PersistedOperationError.FromShapeDiff(ex));
        }
    }

    /// <summary>Soft-delete an operation. Requires a non-empty reason.</summary>
    public async Task<DeactivatePersistedOperationPayload> DeactivatePersistedOperation(
        DeactivatePersistedOperationInput input,
        [Service] IPersistedOperationStore store,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Id))
            return new DeactivatePersistedOperationPayload(
                null,
                new[]
                {
                    new PersistedOperationError(
                        "INVALID_INPUT",
                        "id is required.",
                        null,
                        null,
                        null,
                        null
                    ),
                }
            );
        if (string.IsNullOrWhiteSpace(input.Reason))
            return new DeactivatePersistedOperationPayload(
                null,
                new[]
                {
                    new PersistedOperationError(
                        "INVALID_INPUT",
                        "reason is required.",
                        null,
                        null,
                        null,
                        null
                    ),
                }
            );

        var existing = await store.GetAsync(input.Id, input.TenantKey, ct).ConfigureAwait(false);
        if (existing is null)
            return new DeactivatePersistedOperationPayload(
                null,
                new[] { PersistedOperationError.NotFound(input.Id) }
            );

        await store
            .DeactivateAsync(input.Id, input.TenantKey, input.Reason, ct)
            .ConfigureAwait(false);
        existing.IsActive = false;
        existing.DeprecationReason = input.Reason;
        return new DeactivatePersistedOperationPayload(
            PersistedOperationDto.From(existing),
            Array.Empty<PersistedOperationError>()
        );
    }

    /// <summary>Reactivate a previously deactivated operation.</summary>
    public async Task<RestorePersistedOperationPayload> RestorePersistedOperation(
        RestorePersistedOperationInput input,
        [Service] IPersistedOperationStore store,
        [Service] IDataContextProviderFactory contextFactory,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Id))
            return new RestorePersistedOperationPayload(
                null,
                new[]
                {
                    new PersistedOperationError(
                        "INVALID_INPUT",
                        "id is required.",
                        null,
                        null,
                        null,
                        null
                    ),
                }
            );

        // Restore requires the row to exist (including deactivated rows). The
        // store's GetAsync filters by IsActive, so look up directly via EF.
        var sentinel = input.TenantKey ?? string.Empty;
        await using var ctx = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var raw = await ctx
            .PersistedOperations.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantKey == sentinel && p.Id == input.Id, ct)
            .ConfigureAwait(false);
        if (raw is null)
            return new RestorePersistedOperationPayload(
                null,
                new[] { PersistedOperationError.NotFound(input.Id) }
            );

        await store.RestoreAsync(input.Id, input.TenantKey, ct).ConfigureAwait(false);
        raw.IsActive = true;
        raw.DeprecationReason = null;
        return new RestorePersistedOperationPayload(
            PersistedOperationDto.From(raw),
            Array.Empty<PersistedOperationError>()
        );
    }
}
