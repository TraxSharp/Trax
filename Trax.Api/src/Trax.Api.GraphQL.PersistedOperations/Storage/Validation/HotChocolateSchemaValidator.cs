using System.Collections.Concurrent;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Validation;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

namespace Trax.Api.GraphQL.PersistedOperations.Storage.Validation;

/// <summary>
/// Validates a candidate persisted-operation document against the live
/// HotChocolate schema. Runs the same validation rules HotChocolate runs at
/// execution time, so anything that passes here will execute at runtime
/// (modulo runtime data shape).
/// </summary>
public sealed class HotChocolateSchemaValidator : IPersistedOperationValidator
{
    // HotChocolate's AuthorizeValidationResultAggregator reads the handler
    // from the validator's contextData under this exact string key. At
    // request time the AuthorizationContextEnricher populates it, but the
    // persisted-operation validator bypasses the request pipeline so we
    // have to seed it ourselves.
    private const string AuthorizationHandlerContextKey =
        "HotChocolate.Authorization.AuthorizationHandler";

    private readonly IServiceProvider _services;
    private readonly string _schemaName;

    private readonly ConcurrentDictionary<string, ExecutorCacheEntry> _executorCache = new();

    /// <summary>
    /// Build the validator. <paramref name="schemaName"/> defaults to the
    /// Trax schema name used by <c>AddTraxGraphQL</c>. The
    /// <see cref="IRequestExecutorResolver"/> is resolved from
    /// <paramref name="services"/> lazily on first validation, so the
    /// validator can be constructed even before GraphQL composition is final.
    /// </summary>
    public HotChocolateSchemaValidator(IServiceProvider services, string schemaName = "trax")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(schemaName);
        _services = services;
        _schemaName = schemaName;
    }

    /// <inheritdoc />
    public async Task ValidateAsync(string document, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);
        ct.ThrowIfCancellationRequested();

        DocumentNode parsed;
        try
        {
            parsed = Utf8GraphQLParser.Parse(document);
        }
        catch (SyntaxException ex)
        {
            // HC parser reports 1-based line/column.
            throw new PersistedOperationParseException(ex.Message, ex.Line, ex.Column, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PersistedOperationParseException(ex.Message, line: null, column: null, ex);
        }

        var entry = await GetExecutorAsync(ct).ConfigureAwait(false);

        // IAuthorizationHandler is registered as scoped by AddAuthorization,
        // so we resolve it through a per-call scope. GetService (not
        // GetRequiredService) keeps the validator usable for hosts that
        // never wire @authorize.
        await using var scope = _services.CreateAsyncScope();
        var contextData = new Dictionary<string, object?>();
        var authorizationHandler = scope.ServiceProvider.GetService<IAuthorizationHandler>();
        if (authorizationHandler is not null)
        {
            contextData[AuthorizationHandlerContextKey] = authorizationHandler;
        }

        var result = await entry
            .Validator.ValidateAsync(
                entry.Schema,
                parsed,
                documentId: new OperationDocumentId("trax-po-validator"),
                contextData: contextData!,
                onlyNonCacheable: false,
                cancellationToken: ct
            )
            .ConfigureAwait(false);

        if (!result.HasErrors)
            return;

        var failures = result.Errors.Select(ToFailure).ToArray();
        throw new PersistedOperationValidationException(failures);
    }

    private async ValueTask<ExecutorCacheEntry> GetExecutorAsync(CancellationToken ct)
    {
        if (_executorCache.TryGetValue(_schemaName, out var cached))
            return cached;

        var resolver = _services.GetRequiredService<IRequestExecutorResolver>();
        var executor = await resolver
            .GetRequestExecutorAsync(_schemaName, ct)
            .ConfigureAwait(false);

        // The IDocumentValidator is registered per-schema via
        // IDocumentValidatorFactory on the root container, not on
        // executor.Services. Resolve through the factory.
        var factory = _services.GetRequiredService<IDocumentValidatorFactory>();
        var validator = factory.CreateValidator(_schemaName);

        var entry = new ExecutorCacheEntry(executor.Schema, validator);
        _executorCache[_schemaName] = entry;
        return entry;
    }

    private static ValidationFailure ToFailure(IError error)
    {
        var locations = error.Locations is { Count: > 0 } locs
            ? locs.Select(l => new ValidationFailureLocation(l.Line, l.Column)).ToArray()
            : Array.Empty<ValidationFailureLocation>();

        var path =
            error.Path?.ToList()?.Where(p => p is not null).Select(p => p!).ToArray()
            ?? Array.Empty<object>();

        return new ValidationFailure(error.Message, locations, path);
    }

    private readonly record struct ExecutorCacheEntry(ISchema Schema, IDocumentValidator Validator);
}
