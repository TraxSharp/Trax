using System.Collections.Concurrent;
using System.Reflection;

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

    public virtual string Query =>
        GeneratedQueryCache
            .GetOrAdd(GetType(), t => TypedQueryGenerator.Generate(t, typeof(TResponse)))
            .Query;

    public virtual object? Variables
    {
        get
        {
            var generated = GeneratedQueryCache.GetOrAdd(
                GetType(),
                t => TypedQueryGenerator.Generate(t, typeof(TResponse))
            );
            if (generated.Arguments.Count == 0)
                return null;

            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var arg in generated.Arguments)
                dict[arg.VariableName] = arg.Property.GetValue(this);
            return dict;
        }
    }
}
