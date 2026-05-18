using System.Text.Json;
using GraphQL.Client.Http;
using GraphQLParser.AST;
using Microsoft.Extensions.Logging;

namespace Trax.Api.GraphQL.Client;

public class GraphQLClientExecutor : IGraphQLClientExecutor
{
    private readonly IGraphQLClientValidator _validator;
    private readonly IGraphQLClientConfiguration _configuration;
    private readonly ILogger<GraphQLClientExecutor>? _logger;

    public GraphQLClientExecutor(
        IGraphQLClientValidator validator,
        IGraphQLClientConfiguration configuration,
        ILogger<GraphQLClientExecutor>? logger = null
    )
    {
        _validator = validator;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TReturn> Run<TReturn>(
        IGraphQLClientRequest<TReturn> request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var operationType = await _validator
            .ValidateAsync(request.Query, cancellationToken)
            .ConfigureAwait(false);

        var httpRequest = new GraphQLHttpRequest(request.Query, request.Variables);

        var response = operationType switch
        {
            OperationType.Query => await _configuration
                .GraphQLHttpClient.SendQueryAsync<JsonElement>(httpRequest, cancellationToken)
                .ConfigureAwait(false),
            OperationType.Mutation => await _configuration
                .GraphQLHttpClient.SendMutationAsync<JsonElement>(httpRequest, cancellationToken)
                .ConfigureAwait(false),
            OperationType.Subscription => throw new NotSupportedException(
                "Subscription operations are not supported by GraphQLClientExecutor."
            ),
            _ => throw new NotSupportedException($"Unsupported operation type: {operationType}."),
        };

        if (response.Errors is { Length: > 0 })
            throw new GraphQLExecutionException(response.Errors);

        if (
            response.Data.ValueKind == JsonValueKind.Undefined
            || response.Data.ValueKind == JsonValueKind.Null
        )
            throw new GraphQLExecutionException(
                "GraphQL response contained no data.",
                new InvalidOperationException("Response data was null/undefined.")
            );

        if (
            _configuration.ResponseStrictness != ResponseStrictness.Lenient
            && request.UsesDefaultExtractor
        )
        {
            ResponseShapeValidator.Validate(
                UnwrapForShapeCheck(response.Data),
                typeof(TReturn),
                _configuration.ResponseStrictness,
                _configuration.JsonSerializerOptions,
                _logger
            );
        }

        return request.Extract(response.Data, _configuration.JsonSerializerOptions);
    }

    /// <summary>
    /// Mirrors the unwrap step in <see cref="IGraphQLClientRequest{T}.Extract"/> so strict-shape
    /// checks see the same JSON object the deserializer will see. If <c>data</c> has a single
    /// top-level property and the request uses the default extractor, the strict check applies
    /// to the unwrapped element. Custom extractors are excluded earlier via
    /// <see cref="IGraphQLClientRequest{T}.UsesDefaultExtractor"/>.
    /// </summary>
    private static JsonElement UnwrapForShapeCheck(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
            return data;

        var enumerator = data.EnumerateObject();
        if (!enumerator.MoveNext())
            return data;

        var first = enumerator.Current;
        if (enumerator.MoveNext())
            return data;

        return first.Value;
    }
}
