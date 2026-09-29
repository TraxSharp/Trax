using System.Collections.Concurrent;
using GraphQL.Execution;
using GraphQL.Validation;
using GraphQLParser.AST;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// The default <see cref="IGraphQLClientValidator"/>, registered by <c>AddTraxGraphQLClient</c>.
/// Infrastructure not intended to be used directly; depend on <see cref="IGraphQLClientValidator"/>.
/// </summary>
internal class GraphQLClientValidator : IGraphQLClientValidator
{
    private readonly ISchemaProvider _schemaProvider;
    private readonly DocumentValidator _validator = new();
    private readonly GraphQLDocumentBuilder _documentBuilder = new();

    internal ConcurrentDictionary<string, OperationType> CachedQueries { get; } = new();

    /// <summary>Creates a validator that checks queries against the schema from <paramref name="schemaProvider"/>.</summary>
    /// <param name="schemaProvider">Supplies the schema.</param>
    public GraphQLClientValidator(ISchemaProvider schemaProvider)
    {
        _schemaProvider = schemaProvider;
    }

    /// <inheritdoc/>
    public async Task<OperationType> ValidateAsync(
        string query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(query);

        if (CachedQueries.TryGetValue(query, out var cachedQuery))
            return cachedQuery;

        var document = _documentBuilder.Build(query);

        if (document.Definitions.FirstOrDefault() is not GraphQLOperationDefinition operation)
            throw new GraphQLValidationException(
                query,
                Array.Empty<global::GraphQL.ExecutionError>(),
                "No operation definition found in query."
            );

        var schema = await _schemaProvider.GetSchemaAsync(cancellationToken).ConfigureAwait(false);

        var options = new ValidationOptions { Schema = schema, Document = document };
        var validationResult = await _validator.ValidateAsync(options).ConfigureAwait(false);

        if (!validationResult.IsValid)
            throw new GraphQLValidationException(query, validationResult.Errors.ToArray());

        CachedQueries[query] = operation.Operation;
        return operation.Operation;
    }
}
