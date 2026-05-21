using System.Text.Json;

namespace Trax.Api.GraphQL.Client;

public interface IGraphQLClientRequest<out TResponse> : IGenericGraphQLClientRequest
{
    /// <summary>
    /// Navigates from the GraphQL <c>data</c> envelope to the JsonElement that should be
    /// deserialized as <typeparamref name="TResponse"/>. Default: if <c>data</c> has a single
    /// top-level property, unwrap it; otherwise return <c>data</c> unchanged.
    /// Override to walk nested envelopes (see TypedRequest.Path).
    /// </summary>
    JsonElement UnwrapDataElement(JsonElement data)
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

    /// <summary>
    /// Extracts the response from the GraphQL <c>data</c> envelope.
    /// Default: navigate via <see cref="UnwrapDataElement"/>, then deserialize the result
    /// as <typeparamref name="TResponse"/>. Override to fully customize the extraction.
    /// </summary>
    TResponse Extract(JsonElement data, JsonSerializerOptions options)
    {
        var element = UnwrapDataElement(data);

        // A JSON null at the unwrapped leaf is a legitimate GraphQL result (nullable field).
        // Returning default(TResponse) lets nullable response types observe null; non-nullable
        // response types get default-of-T, which is what the GraphQL spec implies if the
        // server returns null on a non-nullable field (the field would have errored out
        // anyway and we'd have thrown via Errors before reaching here).
        if (element.ValueKind == JsonValueKind.Null)
            return default!;

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
