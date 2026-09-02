using System.Collections.Concurrent;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Features;
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
    private readonly IServiceProvider _services;
    private readonly string _schemaName;

    private readonly ConcurrentDictionary<string, ExecutorCacheEntry> _executorCache = new();

    /// <summary>
    /// Build the validator. <paramref name="schemaName"/> defaults to the
    /// Trax schema name used by <c>AddTraxGraphQL</c>. The
    /// <see cref="IRequestExecutorProvider"/> is resolved from
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

        // IAuthorizationHandler is registered as scoped by AddAuthorization, so it is
        // resolved through a per-call scope and handed to the validation rules as a
        // feature. GetService (not GetRequiredService) keeps the validator usable for
        // hosts that never wire @authorize.
        await using var scope = _services.CreateAsyncScope();
        var features = new FeatureCollection();
        var authorizationHandler = scope.ServiceProvider.GetService<IAuthorizationHandler>();
        if (authorizationHandler is not null)
        {
            features.Set(authorizationHandler);
        }

        ct.ThrowIfCancellationRequested();

        var result = entry.Validator.Validate(
            entry.Schema,
            new OperationDocumentId("trax-po-validator"),
            parsed,
            features,
            onlyNonCacheable: false
        );

        if (!result.HasErrors)
            return;

        var failures = result.Errors.Select(ToFailure).ToArray();
        throw new PersistedOperationValidationException(failures);
    }

    private async ValueTask<ExecutorCacheEntry> GetExecutorAsync(CancellationToken ct)
    {
        if (_executorCache.TryGetValue(_schemaName, out var cached))
            return cached;

        var provider = _services.GetRequiredService<IRequestExecutorProvider>();
        var executor = await provider.GetExecutorAsync(_schemaName, ct).ConfigureAwait(false);

        // The DocumentValidator lives in the schema's own service provider, so it carries
        // exactly the rule set this schema was composed with.
        var validator = executor.Schema.Services.GetRequiredService<DocumentValidator>();

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

    private readonly record struct ExecutorCacheEntry(
        ISchemaDefinition Schema,
        DocumentValidator Validator
    );
}
