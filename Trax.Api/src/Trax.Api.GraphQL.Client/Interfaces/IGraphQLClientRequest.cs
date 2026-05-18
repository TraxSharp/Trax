using System.Text.Json;

namespace Trax.Api.GraphQL.Client;

public interface IGraphQLClientRequest<out TResponse> : IGenericGraphQLClientRequest
{
    /// <summary>
    /// Extracts the response from the GraphQL <c>data</c> envelope.
    /// Default: if <c>data</c> has a single top-level property, unwrap it and deserialize.
    /// Otherwise, deserialize the entire <c>data</c> object as <typeparamref name="TResponse"/>.
    /// Override to customize.
    /// </summary>
    TResponse Extract(JsonElement data, JsonSerializerOptions options)
    {
        var element = data;

        if (element.ValueKind == JsonValueKind.Object)
        {
            var enumerator = element.EnumerateObject();
            if (enumerator.MoveNext())
            {
                var first = enumerator.Current;
                if (!enumerator.MoveNext())
                    element = first.Value;
            }
        }

        return element.Deserialize<TResponse>(options)
            ?? throw new GraphQLExecutionException(
                $"Failed to deserialize GraphQL response data as {typeof(TResponse).Name}.",
                new InvalidOperationException("Deserialization returned null.")
            );
    }

    /// <summary>
    /// True if this request uses the default <see cref="Extract"/> behavior (unwrap-and-deserialize).
    /// Strict response-shape validation only applies when this is true, since custom extractors
    /// may legitimately reshape the response in ways the validator cannot model.
    /// </summary>
    bool UsesDefaultExtractor => true;
}
