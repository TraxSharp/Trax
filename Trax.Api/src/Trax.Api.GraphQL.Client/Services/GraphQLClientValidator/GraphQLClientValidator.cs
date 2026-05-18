using System.Collections.Concurrent;
using GraphQL.Execution;
using GraphQL.Validation;
using GraphQLParser.AST;

namespace Trax.Api.GraphQL.Client;

public class GraphQLClientValidator : IGraphQLClientValidator
{
    private readonly ISchemaProvider _schemaProvider;
    private readonly DocumentValidator _validator = new();
    private readonly GraphQLDocumentBuilder _documentBuilder = new();

    internal ConcurrentDictionary<string, OperationType> CachedQueries { get; } = new();

    public GraphQLClientValidator(ISchemaProvider schemaProvider)
    {
        _schemaProvider = schemaProvider;
    }

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
