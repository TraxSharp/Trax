using System.Collections.Concurrent;
using System.Text.Json;

namespace Trax.Api.GraphQL.Client.Typed;

/// <summary>
/// Base class for mode D (POCO-derived) requests. The Query string is generated once per
/// concrete request type from <see cref="TypedQueryGenerator"/> and cached statically.
/// Variables are built reflectively from properties annotated with
/// <see cref="GraphQLArgumentAttribute"/>.
/// </summary>
public abstract class TypedRequest<TResponse> : IGraphQLClientRequest<TResponse>
{
    private static readonly ConcurrentDictionary<
        Type,
        TypedQueryGenerator.GeneratedQuery
    > GeneratedQueryCache = new();

    public virtual string Query => GetOrGenerate().Query;

    public virtual object? Variables
    {
        get
        {
            var generated = GetOrGenerate();
            if (generated.Arguments.Count == 0)
                return null;

            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var arg in generated.Arguments)
                dict[arg.VariableName] = arg.Property.GetValue(this);
            return dict;
        }
    }

    /// <summary>
    /// Walks the <c>data</c> envelope through the wrapper fields declared by
    /// <see cref="GraphQLOperationAttribute.Path"/>, then descends into the root field itself.
    /// For a flat request (no Path), this collapses to a single descent into the root field.
    /// Errors at each step name the field whose value was the wrong shape (not the field we
    /// were trying to descend into), the request type, and the JSON kind that was found.
    /// </summary>
    public virtual JsonElement UnwrapDataElement(JsonElement data)
    {
        var generated = GetOrGenerate();
        var requestType = GetType();
        var current = data;
        string? parentField = null;

        foreach (var segment in generated.PathSegments)
        {
            EnsureDescendable(current, parentField, segment, requestType);
            current = ReadField(current, segment, parentField, requestType);
            parentField = segment;
        }

        EnsureDescendable(current, parentField, generated.RootField, requestType);
        return ReadField(current, generated.RootField, parentField, requestType);
    }

    private TypedQueryGenerator.GeneratedQuery GetOrGenerate() =>
        GeneratedQueryCache.GetOrAdd(
            GetType(),
            t => TypedQueryGenerator.Generate(t, typeof(TResponse))
        );

    private static void EnsureDescendable(
        JsonElement element,
        string? parentField,
        string nextField,
        Type requestType
    )
    {
        if (element.ValueKind == JsonValueKind.Object)
            return;

        var parentDesc = parentField is null ? "the response data envelope" : $"'{parentField}'";
        var kind = element.ValueKind == JsonValueKind.Null ? "null" : element.ValueKind.ToString();

        throw new GraphQLExecutionException(
            $"Cannot descend into '{nextField}' while extracting response for "
                + $"{requestType.Name}: {parentDesc} was {kind}, expected an object.",
            new InvalidOperationException("Response shape mismatch.")
        );
    }

    private static JsonElement ReadField(
        JsonElement parent,
        string field,
        string? parentField,
        Type requestType
    )
    {
        if (parent.TryGetProperty(field, out var value))
            return value;

        var parentDesc = parentField is null ? "the response data envelope" : $"'{parentField}'";
        throw new GraphQLExecutionException(
            $"Field '{field}' missing from {parentDesc} while extracting response for "
                + $"{requestType.Name}. Available fields: {AvailableFields(parent)}.",
            new InvalidOperationException("Response shape mismatch.")
        );
    }

    private static string AvailableFields(JsonElement element)
    {
        var names = new List<string>();
        foreach (var prop in element.EnumerateObject())
            names.Add(prop.Name);
        return names.Count == 0 ? "<none>" : string.Join(", ", names);
    }
}
